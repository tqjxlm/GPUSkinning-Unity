using System.Collections.Generic;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using System;
using System.Linq;

// Level of details of a set of instances
class IntanceBatch
{
    public Matrix4x4[] Transforms { get; private set; }

    public Vector4[] AnimStates { get; private set; }

    public int Count { get; private set; } = 0;

    public Mesh Mesh { get; private set; }

    public MaterialPropertyBlock InstanceProperties { get; private set; } = new MaterialPropertyBlock();

    public IntanceBatch(int crowdCount, Mesh mesh)
    {
        Transforms = new Matrix4x4[crowdCount];
        AnimStates = new Vector4[crowdCount];
        Mesh = mesh;
        InstanceProperties.SetVectorArray("_AnimState", AnimStates);
    }

    public void Reset()
    {
        Count = 0;
    }

    public void Push(Matrix4x4 transform, Vector4 state)
    {
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
    public List<Mesh> AvailableWeapons;
    public Material WeaponMaterial;

    // Component references
    CrowdManager crowd;
    Camera mainCamera;
    Profiler profiler;

    // Materials
    Material GPUSkinMaterial;
    Material GPUSkinMaterialSimple;
    Material GPUWeaponMaterial;
    Texture2D[] bakedAnimation;
    public int TextureSize { get; private set; }
    int boneNumber;

    // Render info
    bool mortonSort = true;
    float meshRadius;
    List<IntanceBatch> LODBatches = new List<IntanceBatch>();
    List<IntanceBatch> weaponBatches = new List<IntanceBatch>();
    int[] elementID;
    float[] elementDistance;

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
        crowd = GetComponent<CrowdManager>();
        elementID = new int[crowd.crowdCount];
        elementDistance = new float[crowd.crowdCount];

        PrepareRenderData();

        InitLODs();

        // Controls
        InitControls();

        // Spawn crowd
        crowd.Spawn(AvailableWeapons.Count);
        meshRadius = crowd.MeshRadius * 2;

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
        while (crowd.crowdCount > lodCapacity)
        {
            LODSettings lod = new LODSettings
            {
                distance = lastLOD.distance,
                lodPrefab = lastLOD.lodPrefab,
                size = Math.Min(crowd.crowdCount - lodCapacity, 1000)
            };
            lod.maxSize = lod.size;

            lodSettings.Add(lod);

            LODBatches.Add(new IntanceBatch(lod.maxSize, LODBatches.Last().Mesh));

            lodCapacity += lod.size;
        }
    }

    // Initialize a GPUSkinning material with properties provided by a normal material
    Material ApplyMaterialWithGPUSkinning(Shader shader, Material originalMaterial)
    {
        Material mat = new Material(shader) { enableInstancing = true };
        mat.CopyPropertiesFromMaterial(originalMaterial);
        ToggleKeyword(mat, mortonSort, "MORTON_CODE", "XY_INDEXING");

        for (int i = 0; i < NUM_TEXTURE; i++)
        {
            mat.SetTexture("_Animation" + i, bakedAnimation[i]);
        }

        if (mortonSort)
        {
            int pow = (int)(Math.Log(TextureSize, 2) + 0.5);
            mat.SetInt("_size", TextureSize);
            mat.SetInt("_pow", pow);
        }

        mat.SetFloat("_foldingOffset", (float)boneNumber / TextureSize);

        return mat;
    }

    void PrepareRenderData()
    {
        // Element indexing
        for (int i = 0; i < crowd.crowdCount; i++)
        {
            elementID[i] = i;
        }

        SkinnedMeshRenderer firstLODRenderer = lodSettings[0].lodPrefab.GetComponent<SkinnedMeshRenderer>();
        boneNumber = firstLODRenderer.bones.Count();

        LoadBakedAnimations();

        // Init materials for rendering
        GPUSkinMaterial = ApplyMaterialWithGPUSkinning(GPUSkinShader, firstLODRenderer.sharedMaterial);
        GPUSkinMaterialSimple = ApplyMaterialWithGPUSkinning(GPUSkinShaderSimple, firstLODRenderer.sharedMaterial);

        // Process weapons
        if (AvailableWeapons.Count > 0)
        {
            GPUWeaponMaterial = ApplyMaterialWithGPUSkinning(GPUSkinShader, WeaponMaterial);

            int maxDrawnWeapon = lodSettings[0].size + lodSettings[1].size;
            foreach (Mesh mesh in AvailableWeapons)
            {
                weaponBatches.Add(new IntanceBatch(maxDrawnWeapon, ProcessMesh(mesh)));
            }
        }

        // Process character meshes
        for (int i = 0; i < lodSettings.Count; i++)
        {
            LODSettings lod = lodSettings[i];
            Mesh meshNoWeapon = ProcessMesh(lod.lodPrefab.GetComponent<SkinnedMeshRenderer>().sharedMesh);

            LODBatches.Add(new IntanceBatch(lod.size, meshNoWeapon));

            Destroy(lod.lodPrefab);
        }
    }

    void LoadBakedAnimations()
    {
        // Try to load the animation texture
        bakedAnimation = new Texture2D[NUM_TEXTURE];
        for (int i = 0; i < NUM_TEXTURE; i++)
        {
            string texturePath = string.Format(
                "BakedAnimations/{0}_BakedAnimation_{1}{2}", gameObject.name, mortonSort ? "Morton" : "XY", i);
            bakedAnimation[i] = Resources.Load<Texture2D>(texturePath);
            if (bakedAnimation[i] == null)
            {
                Debug.LogException(new Exception(string.Format("Baked animation {0} not found or not valid. Please bake animation first", texturePath)));
                return;
            }
        }

        TextureSize = bakedAnimation[1].height;

        // Get frame info. It will be used in CrowdManager to calculate Y index for sampling the animation texture.
        float[] frameLength = new float[crowd.Animations.Count];
        float[] frameOffset = new float[crowd.Animations.Count];
        float currentFrame = mortonSort ? 0 : 0.5f;
        for (int i = 0; i < crowd.Animations.Count; i++)
        {
            var anim = crowd.Animations[i];
            MathHelper.GetAnimationTime(anim.clip, mortonSort ? 1 : anim.sampleRate, out float animationTimeStep, out uint keyFrameCnt);
            frameLength[i] = mortonSort ? keyFrameCnt : (float)keyFrameCnt / TextureSize;
            frameOffset[i] = mortonSort ? currentFrame : currentFrame / TextureSize;
            currentFrame += keyFrameCnt + ATLAS_PADDING;
        }

        crowd.FrameLength = frameLength;
        crowd.FrameOffset = frameOffset;
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
                    (0.5f + w[i].boneIndex0) / TextureSize,
                    (0.5f + w[i].boneIndex1) / TextureSize,
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
            for (int i = 0; i < crowd.crowdCount; i++)
            {
                elementDistance[i] = (Camera.main.transform.position - crowd.Positions2D[i]).sqrMagnitude;
            }
            Array.Sort(elementID, (a, b) => elementDistance[a].CompareTo(elementDistance[b]));
            yield return new WaitForSeconds(.15f);
        }
    }

    // Scan instances to gather render information for every LOD
    void GatherRenderData()
    {
        for (int i = 0; i < LODBatches.Count; i++)
        {
            LODBatches[i].Reset();
        }

        for (int i = 0; i < weaponBatches.Count; i++)
        {
            weaponBatches[i].Reset();
        }

        Plane[] planes = GeometryUtility.CalculateFrustumPlanes(mainCamera);
        foreach (int i in elementID)
        {
            // Frustum culling
            bool culled = false;
            for (int j = 0; j < 6; j++)
            {
                if (planes[j].GetDistanceToPoint(crowd.Positions2D[i]) < -meshRadius)
                {
                    culled = true;
                    break;
                }
            }

            // LOD picking
            if (!culled)
            {
                int lod = 0;
                float dist = elementDistance[i];
                for(; lod < lodSettings.Count; lod++)
                {
                    if (dist < lodSettings[lod].distance && LODBatches[lod].Count < lodSettings[lod].size)
                    {
                        break;
                    }
                }

                if (lod < lodSettings.Count)
                {
                    LODBatches[lod].Push(crowd.Transforms[i], crowd.AnimationStatusGPU[i]);
                }

                if (weaponBatches.Count > 0 && lod < 2)
                {
                    weaponBatches[crowd.Weapons[i]].Push(crowd.Transforms[i], crowd.AnimationStatusGPU[i]);
                }
            }
        }
    }

    // Manually draw meshes in spawing mode
    void RenderInstanced()
    {
        profiler.ResetDisplay();
        profiler.Log("Instance count: " + LODBatches.Sum(item => item.Count));

        // Draw weapons (without LOD)
        for (int i = 0; i < weaponBatches.Count; i++)
        {
            IntanceBatch batch = weaponBatches[i];
            batch.InstanceProperties.SetVectorArray("_AnimState", batch.AnimStates);

            Graphics.DrawMeshInstanced(
                batch.Mesh, 0, GPUWeaponMaterial,
                batch.Transforms, batch.Count, batch.InstanceProperties,
                shadowCastingMode, shadowReceivingMode);
        }

        // Draw characters (for every LOD)
        for (int i = 0; i < LODBatches.Count; i++)
        {
            IntanceBatch lod = LODBatches[i];
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
