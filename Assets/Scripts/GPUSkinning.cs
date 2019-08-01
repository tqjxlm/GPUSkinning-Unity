using System.Collections.Generic;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using System;
using System.Linq;

// Level of details of a set of instances
class InstanceBatch
{
    public Matrix4x4[] Transforms { get; private set; }

    public Vector4[] AnimStates { get; private set; }

    public int Count { get; private set; } = 0;

    int Capacity;

    public Mesh Mesh { get; private set; }

    public MaterialPropertyBlock InstanceProperties { get; private set; } = new MaterialPropertyBlock();

    public InstanceBatch(int capacity, Mesh mesh)
    {
        Transforms = new Matrix4x4[capacity];
        AnimStates = new Vector4[capacity];
        Capacity = capacity;
        Mesh = mesh;
        InstanceProperties.SetVectorArray("_AnimState", AnimStates);
    }

    public void Reset()
    {
        Count = 0;
    }

    public void Push(Matrix4x4 transform, Vector4 state)
    {
        if (Count == Capacity)
        {
            return;
        }
        Transforms[Count] = transform;
        AnimStates[Count] = state;
        Count++;
    }
}

[Serializable]
public class LODSettings
{
    public float distance;
    public int size;
    public GameObject lodPrefab;

    [HideInInspector]
    public int maxSize;
}

// This script should be attached to the top level of a skinned game object
// Two skeletal game object with the same bone settings should be asigned to it as LODs
public class GPUSkinning : MonoBehaviour
{
    public const uint NUM_TEXTURE = 2;      // In a dual quaternion implementation, we store 2 Vector4 in 2 textures
    public const uint MAXIMUM_BONE = 64;    // 64 bones should be enough, but up to 500 is tested to be OK
    public const uint ATLAS_PADDING = 1;    // Atlas padding is used to avoid sample bleeding in texture atlas

    public Shader GPUSkinShader;
    public Shader GPUSkinShaderSimple;
    public List<LODSettings> lodSettings = new List<LODSettings>(3);
    public Transform weaponBone;

    // Component references
    CrowdManager crowdMgr;
    WeaponManager weaponMgr;
    Camera mainCamera;
    Profiler profiler;

    // Materials
    Material GPUSkinMaterial;
    Material GPUSkinMaterialSimple;
    Material GPUWeaponMaterial;
    Texture2D[] bakedAnimation = new Texture2D[NUM_TEXTURE];
    Texture2D[] weaponTexture = new Texture2D[NUM_TEXTURE];
    int AnimationTextureSize;
    int WeaponTextureSize;
    int boneNumber;

    // Render info
    bool mortonSort = true;
    float instanceCullThreshold;
    List<InstanceBatch> LODBatches = new List<InstanceBatch>();
    List<InstanceBatch> weaponBatches = new List<InstanceBatch>();
    int[] instanceID;
    float[] instanceDistance;

    int[] weaponID;
    float[] weaponDistance;

    // Control
    UnityEngine.Rendering.ShadowCastingMode shadowCastingMode;
    bool shadowReceivingMode;

    #region Setup

    // Start is called before the first frame update
    void Start()
    {
        mortonSort = HardwareAdapter.MortonSortEnabled;
        profiler = FindObjectOfType<Profiler>();
        mainCamera = Camera.main;

        crowdMgr = GetComponent<CrowdManager>();
        weaponMgr = GetComponent<WeaponManager>();

        instanceID = new int[crowdMgr.Count];
        instanceDistance = new float[crowdMgr.Count];
        weaponID = new int[weaponMgr.Count];
        weaponDistance = new float[weaponMgr.Count];

        // Rendering preperation
        PrepareRenderData();

        InitLODs();

        // Controls
        InitControls();

        // Spawn crowd
        crowdMgr.Spawn();
        instanceCullThreshold = crowdMgr.Radius * 2;

        StartCoroutine("ReOrder");
        StartCoroutine("AdaptPerformance");
    }

    // Init lod settings and additional LODs for more data than total capacity
    void InitLODs()
    {
        int lodCapacity = 0;
        for (int i = 0; i < lodSettings.Count; i++)
        {
            lodSettings[i].distance *= lodSettings[i].distance;
            lodSettings[i].maxSize = lodSettings[i].size;
            if (lodSettings[i].size > 1000)
            {
                lodSettings[i].size = 1000;
            }
            lodCapacity += lodSettings[i].size;
        }

        // If the provided LODs does not cover the crowd count,
        // generate more LODs until they do
        LODSettings lastLOD = lodSettings.Last();
        while (crowdMgr.Count > lodCapacity)
        {
            LODSettings lod = new LODSettings
            {
                distance = lastLOD.distance,
                lodPrefab = lastLOD.lodPrefab,
                size = Math.Min(crowdMgr.Count - lodCapacity, 1000)
            };
            lod.maxSize = lod.size;

            lodSettings.Add(lod);

            LODBatches.Add(new InstanceBatch(lod.maxSize, LODBatches.Last().Mesh));

            lodCapacity += lod.size;
        }
    }

    // Initialize a GPUSkinning material with properties provided by a normal material
    Material ApplyMaterialWithGPUSkinning(Shader shader, Material originalMaterial)
    {
        Material mat = new Material(shader) { enableInstancing = true };
        mat.CopyPropertiesFromMaterial(originalMaterial);
        ToggleKeyword(mat, mortonSort, "MORTON_CODE", "XY_INDEXING");
        ToggleKeyword(mat, true, "CHARACTER", "WEAPON");

        for (int i = 0; i < NUM_TEXTURE; i++)
        {
            mat.SetTexture("_Animation" + i, bakedAnimation[i]);
        }

        if (mortonSort)
        {
            int pow = (int)(Math.Log(AnimationTextureSize, 2) + 0.5);
            mat.SetInt("_size", AnimationTextureSize);
            mat.SetInt("_pow", pow);
        }
        else
        {
            mat.SetFloat("_foldingOffset", (float)boneNumber / AnimationTextureSize);
        }

        return mat;
    }

    Material ApplyWeaponMaterialWithGPUSkinning(Shader shader, Material originalMaterial)
    {
        Material mat = new Material(shader) { enableInstancing = true };
        mat.CopyPropertiesFromMaterial(originalMaterial);
        ToggleKeyword(mat, false, "MORTON_CODE", "XY_INDEXING");
        ToggleKeyword(mat, false, "CHARACTER", "WEAPON");

        for (int i = 0; i < NUM_TEXTURE; i++)
        {
            mat.SetTexture("_Animation" + i, weaponTexture[i]);
        }

        mat.SetFloat("_foldingOffset", 1.0f / WeaponTextureSize);
        mat.SetFloat("_weaponRescale", (float)AnimationTextureSize / WeaponTextureSize);

        return mat;
    }

    void PrepareRenderData()
    {
        // Element indexing
        for (int i = 0; i < crowdMgr.Count; i++)
        {
            instanceID[i] = i;
        }

        for (int i = 0; i < weaponMgr.Count; i++)
        {
            weaponID[i] = i;
        }

        SkinnedMeshRenderer firstLODRenderer = lodSettings[0].lodPrefab.GetComponent<SkinnedMeshRenderer>();
        boneNumber = firstLODRenderer.bones.Count();

        LoadBakedAnimations();

        // Init materials for rendering
        GPUSkinMaterial = ApplyMaterialWithGPUSkinning(GPUSkinShader, firstLODRenderer.sharedMaterial);
        GPUSkinMaterialSimple = ApplyMaterialWithGPUSkinning(GPUSkinShaderSimple, firstLODRenderer.sharedMaterial);
        GPUWeaponMaterial = ApplyWeaponMaterialWithGPUSkinning(GPUSkinShader, weaponMgr.WeaponMaterial);

        // Process weapons
        if (weaponMgr.AvailableWeapons.Count > 0)
        {
            int maxDrawnWeapon = lodSettings[0].size + lodSettings[1].size;
            foreach (Mesh mesh in weaponMgr.AvailableWeapons)
            {
                weaponBatches.Add(new InstanceBatch(maxDrawnWeapon, ProcessMesh(mesh)));
            }
        }

        // Process character meshes
        for (int i = 0; i < lodSettings.Count; i++)
        {
            LODSettings lod = lodSettings[i];
            Mesh meshNoWeapon = ProcessMesh(lod.lodPrefab.GetComponent<SkinnedMeshRenderer>().sharedMesh);

            LODBatches.Add(new InstanceBatch(lod.size, meshNoWeapon));

            Destroy(lod.lodPrefab);
        }
    }

    void LoadBakedAnimations()
    {
        // Try to load the animation texture
        for (int i = 0; i < NUM_TEXTURE; i++)
        {
            string texturePath = string.Format(
                "BakedAnimations/{0}_BakedAnimation_{1}{2}", gameObject.name, mortonSort ? "Morton" : "XY", i);
            bakedAnimation[i] = Resources.Load<Texture2D>(texturePath);

            texturePath = string.Format("BakedAnimations/{0}_BakedAnimation_Weapon{1}", gameObject.name, i);
            weaponTexture[i] = Resources.Load<Texture2D>(texturePath);

            if (bakedAnimation[i] == null)
            {
                Debug.LogException(new Exception(string.Format("Baked animation {0} not found or not valid. Please bake animation first", texturePath)));
                return;
            }
        }

        AnimationTextureSize = bakedAnimation[1].height;
        WeaponTextureSize = weaponTexture[1].height;

        // Get frame info. It will be used in CrowdManager to calculate Y index for sampling the animation texture.
        float[] frameLength = new float[crowdMgr.Animations.Count];
        float[] frameOffset = new float[crowdMgr.Animations.Count];
        float currentFrame = mortonSort ? 0 : 0.5f;
        for (int i = 0; i < crowdMgr.Animations.Count; i++)
        {
            var anim = crowdMgr.Animations[i];
            MathHelper.GetAnimationTime(anim.clip, mortonSort ? 1 : anim.sampleRate, out float animationTimeStep, out uint keyFrameCnt);
            frameLength[i] = mortonSort ? keyFrameCnt : (float)keyFrameCnt / AnimationTextureSize;
            frameOffset[i] = mortonSort ? currentFrame : currentFrame / AnimationTextureSize;
            currentFrame += keyFrameCnt + ATLAS_PADDING;
        }

        crowdMgr.FrameLength = frameLength;
        crowdMgr.FrameOffset = frameOffset;
    }

    // Fetch information from skinned mesh renderer
    Mesh ProcessMesh(Mesh originalMesh)
    {
        // Copy the shared mesh to modify it
        Mesh mesh = Instantiate(originalMesh);

        BoneWeight[] w = mesh.boneWeights;
        Vector4[] boneInfo = new Vector4[mesh.vertexCount];
        Matrix4x4[] bindposes = mesh.bindposes;
        Vector3[] vertices = mesh.vertices;
        Vector3[] normals = mesh.normals;
        for (int i = 0; i < mesh.vertexCount; i++)
        {
            // Convert integer index to normalized index
            float weight = w[i].weight0 / (w[i].weight0 + w[i].weight1);

            if (mortonSort)
            {
                boneInfo[i] = new Vector4(
                    MathHelper.EncodeMorton((uint)w[i].boneIndex0),
                    MathHelper.EncodeMorton((uint)w[i].boneIndex1),
                    weight
                    );
            }
            else
            {
                boneInfo[i] = new Vector4(
                    (0.5f + w[i].boneIndex0) / AnimationTextureSize,
                    (0.5f + w[i].boneIndex1) / AnimationTextureSize,
                    weight
                    );
            }

            // Transform vectors to animation space in advance
            vertices[i] = bindposes[w[i].boneIndex0].MultiplyPoint3x4(vertices[i]) * weight +
                bindposes[w[i].boneIndex1].MultiplyPoint3x4(vertices[i]) * (1 - weight);
            normals[i] = bindposes[w[i].boneIndex0].MultiplyVector(normals[i]) * weight +
                bindposes[w[i].boneIndex1].MultiplyVector(normals[i]) * (1 - weight);
        }
        mesh.tangents = boneInfo;
        mesh.vertices = vertices;
        mesh.normals = normals;

        mesh.Optimize();

        return mesh;
    }

    #endregion

    #region Update

    // Update is called once per frame
    void Update()
    {
        GatherRenderData();

        RenderInstanced();
    }

    // Limit instancing number to maintain the target frame rate
    IEnumerator AdaptPerformance()
    {
        for (; ; )
        {
            yield return new WaitForSeconds(profiler.benchmarkDuration);
            if (profiler.AverageFPS < HardwareAdapter.TargetFrameRate)
            {
                if (lodSettings[1].size > lodSettings[0].size)
                {
                    lodSettings[1].size -= 10;
                }
            }
            else if (lodSettings[1].size < lodSettings[1].maxSize)
            {
                lodSettings[1].size += 10;
            }
        }
    }

    // Order the crowd using distance from the camera
    IEnumerator ReOrder()
    {
        for (; ; )
        {
            for (int i = 0; i < crowdMgr.Count; i++)
            {
                instanceDistance[i] = (Camera.main.transform.position - crowdMgr.Positions[i]).sqrMagnitude;
            }
            Array.Sort(instanceID, (a, b) => instanceDistance[a].CompareTo(instanceDistance[b]));
            yield return new WaitForSeconds(.1f);

            for (int i = 0; i < weaponMgr.Count; i++)
            {
                weaponDistance[i] = (Camera.main.transform.position - weaponMgr.Positions[i]).sqrMagnitude;
            }
            Array.Sort(weaponID, (a, b) => weaponDistance[a].CompareTo(weaponDistance[b]));
            yield return new WaitForSeconds(.1f);
        }
    }

    // Scan instances to gather render information for every LOD
    void GatherRenderData()
    {
        Plane[] planes = GeometryUtility.CalculateFrustumPlanes(mainCamera);

        for (int i = 0; i < LODBatches.Count; i++)
        {
            LODBatches[i].Reset();
        }
        foreach (int i in instanceID)
        {
            // Frustum culling
            bool culled = false;
            for (int j = 0; j < 6; j++)
            {
                if (planes[j].GetDistanceToPoint(crowdMgr.Positions[i]) < -instanceCullThreshold)
                {
                    culled = true;
                    break;
                }
            }

            // LOD picking
            if (!culled)
            {
                int lod = 0;
                float dist = instanceDistance[i];
                for(; lod < lodSettings.Count; lod++)
                {
                    if (dist < lodSettings[lod].distance && LODBatches[lod].Count < lodSettings[lod].size)
                    {
                        break;
                    }
                }

                if (lod < lodSettings.Count)
                {
                    LODBatches[lod].Push(crowdMgr.Transforms[i], crowdMgr.AnimationStatusGPU[i]);
                }
            }
        }

        if (weaponBatches.Count > 0)
        {
            for (int i = 0; i < weaponBatches.Count; i++)
            {
                weaponBatches[i].Reset();
            }
            foreach (int i in weaponID)
            {
                int weaponType = weaponMgr.Types[i];

                // Frustum culling
                bool culled = false;
                float cullThreshold = weaponMgr.Radius[weaponType] * 2;
                for (int j = 0; j < 6; j++)
                {
                    if (planes[j].GetDistanceToPoint(weaponMgr.Positions[i]) < -cullThreshold)
                    {
                        culled = true;
                        break;
                    }
                }

                // LOD picking
                if (!culled)
                {
                    int userID = weaponMgr.Users[i];
                    if (userID != -1)
                    {
                        weaponBatches[weaponType].Push(crowdMgr.Transforms[userID], crowdMgr.AnimationStatusGPU[userID]);
                    }
                    else
                    {
                        weaponBatches[weaponType].Push(weaponMgr.Transforms[i], weaponMgr.NullAnimStatus);
                    }
                }
            }
        }
    }

    // Manually draw meshes with instancing
    void RenderInstanced()
    {
        profiler.ResetDisplay();
        profiler.Log("Instance count: " + LODBatches.Sum(item => item.Count));

        // Draw weapons (without LOD)
        for (int i = 0; i < weaponBatches.Count; i++)
        {
            InstanceBatch batch = weaponBatches[i];
            batch.InstanceProperties.SetVectorArray("_AnimState", batch.AnimStates);

            Graphics.DrawMeshInstanced(
                batch.Mesh, 0, GPUWeaponMaterial,
                batch.Transforms, batch.Count, batch.InstanceProperties,
                shadowCastingMode, shadowReceivingMode);
        }

        // Draw characters (for every LOD)
        for (int i = 0; i < LODBatches.Count; i++)
        {
            InstanceBatch lod = LODBatches[i];
            lod.InstanceProperties.SetVectorArray("_AnimState", lod.AnimStates);

            profiler.Log(string.Format("LOD_{0}: {1}", i, lod.Count));

            if (i < 2)
            {
                Graphics.DrawMeshInstanced(
                    lod.Mesh, 0, GPUSkinMaterial,
                    lod.Transforms, lod.Count, lod.InstanceProperties,
                    shadowCastingMode, shadowReceivingMode);
            }
            else
            {
                Graphics.DrawMeshInstanced(
                    lod.Mesh, 0, GPUSkinMaterialSimple,
                    lod.Transforms, lod.Count, lod.InstanceProperties,
                    UnityEngine.Rendering.ShadowCastingMode.Off, false);
            }
        }
    }

    #endregion

    #region Controls

    // Initialise UI controls
    void InitControls()
    {
        GameObject PBRToggleWidget = GameObject.Find("PBRToggle");
        Toggle PBRToggle = PBRToggleWidget.GetComponent<Toggle>();
        PBRToggle.onValueChanged.AddListener(delegate {
            GPUSkinShader.maximumLOD = PBRToggle.isOn ? 200 : 150;
            GPUSkinShaderSimple.maximumLOD = PBRToggle.isOn ? 200 : 150;
        });
        GPUSkinShader.maximumLOD = PBRToggle.isOn ? 200 : 150;
        GPUSkinShaderSimple.maximumLOD = PBRToggle.isOn ? 200 : 150;

        GameObject shadowCastToggleWidget = GameObject.Find("ShadowCastToggle");
        Toggle shadowCastToggle = shadowCastToggleWidget.GetComponent<Toggle>();
        shadowCastToggle.onValueChanged.AddListener(delegate {
            shadowCastingMode = shadowCastToggle.isOn ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
        });
        shadowCastingMode = shadowCastToggle.isOn ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;

        GameObject shadowReceiveToggleWidget = GameObject.Find("ShadowReceiveToggle");
        Toggle shadowReceiveToggle = shadowReceiveToggleWidget.GetComponent<Toggle>();
        shadowReceiveToggle.onValueChanged.AddListener(delegate {
            shadowReceivingMode = shadowReceiveToggle.isOn;
        });
        shadowReceivingMode = shadowReceiveToggle.isOn;
    }

    // Toggle shader keyword for a material
    void ToggleKeyword(Material mat, bool on, string onKeyword, string offKeyword = "_")
    {
        if (on)
        {
            mat.EnableKeyword(onKeyword);
            mat.DisableKeyword(offKeyword);
        }
        else
        {
            mat.EnableKeyword(offKeyword);
            mat.DisableKeyword(onKeyword);
        }
    }

    #endregion
}
