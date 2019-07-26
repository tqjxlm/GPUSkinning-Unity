using System.Collections.Generic;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using System;
using System.Linq;

// Level of details of a set of instances
class InstanceLOD
{
    public Matrix4x4[] Transforms { get; private set; }
    public Vector4[] AnimStates { get; private set; }
    public int Count { get; private set; }

    public InstanceLOD(int crowdCount)
    {
        Transforms = new Matrix4x4[crowdCount];
        AnimStates = new Vector4[crowdCount];
        Count = 0;
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
    public GameObject mesh;

    [HideInInspector]
    public int maxSize;
}

// This script should be attached to the top level of a skinned game object
// Two skeletal game object with the same bone settings should be asigned to it as LODs
public class GPUSkinning : MonoBehaviour
{
    public const uint NUM_TEXTURE = 2;      // In a dual quaternion implementation, we store 2 Vector4 in 2 textures
    public const uint ATLAS_PADDING = 1;    // Atlas padding is used to avoid sample bleeding in texture atlas

    public Shader GPUSkinShader;
    public Shader GPUSkinShaderSimple;
    public List<LODSettings> lodSettings = new List<LODSettings>(3);
    public Transform TopLevelWeaponBone;
    public List<Mesh> AvailableWeapons;
    public Material WeaponMaterial;

    CrowdManager crowd;
    Camera mainCamera;
    ExternalProfiler profiler;
    Transform[] allBones;

    // Materials
    Material GPUSkinMaterial;
    Material GPUSkinMaterialSimple;
    Material GPUWeaponMaterial;
    Material GPUWeaponMaterialSimple;
    MaterialPropertyBlock instanceProperties;
    Vector2 textureSize;
    bool mortonSort = true;

    // Render data
    float meshRadius;
    float[] elementDistance;
    List<Mesh> meshLODs = new List<Mesh>();
    List<Mesh> weaponMeshes = new List<Mesh>();
    List<InstanceLOD> instanceLODs = new List<InstanceLOD>();
    int[] elementID;

    // Control
    UnityEngine.Rendering.ShadowCastingMode shadowCastingMode;
    bool shadowReceivingMode;

    #region Setup

    // Start is called before the first frame update
    void Start()
    {
        mortonSort = HardwareAdapter.MortonSortEnabled;
        profiler = gameObject.AddComponent<ExternalProfiler>();
        mainCamera = Camera.main;
        crowd = GetComponent<CrowdManager>();
        elementID = new int[crowd.crowdCount];
        elementDistance = new float[crowd.crowdCount];

        ProcessRenderData();

        InitLODs();

        // Controls
        InitControls();

        // Spawn crowd
        crowd.Spawn();
        meshRadius = crowd.MeshRadius * 2;

        StartCoroutine("ReOrder");
        //StartCoroutine("AdaptPerformance");
    }

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

        LODSettings lastLOD = lodSettings.Last();
        while (crowd.crowdCount > lodCapacity)
        {
            LODSettings lod = new LODSettings
            {
                distance = lastLOD.distance,
                mesh = lastLOD.mesh,
                size = Math.Min(crowd.crowdCount - lodCapacity, 1000)
            };
            lod.maxSize = lod.size;

            lodSettings.Add(lod);
            meshLODs.Add(meshLODs.Last());
            instanceLODs.Add(new InstanceLOD(lodSettings[instanceLODs.Count].maxSize));

            lodCapacity += lod.size;
        }
        instanceProperties.SetVectorArray("_AnimState", instanceLODs[2].AnimStates);
    }

    void ProcessRenderData()
    {
        for (int i = 0; i < crowd.crowdCount; i++)
        {
            elementID[i] = i;
        }
        SkinnedMeshRenderer firstLODRenderer = lodSettings[0].mesh.GetComponent<SkinnedMeshRenderer>();

        // Init materials for rendering
        GPUSkinMaterial = new Material(GPUSkinShader) { enableInstancing = true };
        instanceProperties = new MaterialPropertyBlock();
        GPUSkinMaterial.CopyPropertiesFromMaterial(firstLODRenderer.sharedMaterial);
        ToggleKeyword(GPUSkinMaterial, mortonSort, "MORTON_CODE", "XY_INDEXING");
        LoadBakedAnimations(ref GPUSkinMaterial, "All");

        if (!HardwareAdapter.MortonSortEnabled)
        {
            GPUSkinMaterialSimple = new Material(GPUSkinShaderSimple) { enableInstancing = true };
            GPUSkinMaterialSimple.CopyPropertiesFromMaterial(lodSettings[2].mesh.GetComponent<SkinnedMeshRenderer>().sharedMaterial);
            GPUSkinMaterialSimple.CopyPropertiesFromMaterial(GPUSkinMaterial);
        }

        if (AvailableWeapons.Count > 0)
        {
            GPUWeaponMaterial = new Material(GPUSkinShader) { enableInstancing = true };
            GPUWeaponMaterial.CopyPropertiesFromMaterial(WeaponMaterial);
            GPUWeaponMaterial.CopyPropertiesFromMaterial(GPUSkinMaterial);
            LoadBakedAnimations(ref GPUWeaponMaterial, "Weapon");

            GPUWeaponMaterialSimple = new Material(GPUSkinShaderSimple) { enableInstancing = true };
            GPUWeaponMaterialSimple.CopyPropertiesFromMaterial(lodSettings[2].mesh.GetComponent<SkinnedMeshRenderer>().sharedMaterial);
            GPUWeaponMaterialSimple.CopyPropertiesFromMaterial(GPUWeaponMaterial);
        }

        // Process character meshes
        foreach (LODSettings lod in lodSettings)
        {
            RegisterLOD(lod.mesh);
            instanceLODs.Add(new InstanceLOD(lodSettings[instanceLODs.Count].size));
        }

        // Process weapons
        Dictionary<string, int> weaponBones = GetWeaponBones();
        foreach (Mesh mesh in AvailableWeapons)
        {
            RegisterWeapon(mesh, weaponBones);
        }
    }

    public Dictionary<string, int> GetWeaponBones()
    {
        if (TopLevelWeaponBone == null)
        {
            return null;
        }
        Dictionary<string, int> weaponBoneDict = new Dictionary<string, int>();
        SkinnedMeshRenderer firstLODRenderer = lodSettings[0].mesh.GetComponent<SkinnedMeshRenderer>();
        allBones = firstLODRenderer.bones;

        for (int i = 0; i < allBones.Length; i++)
        {
            if (allBones[i].name == TopLevelWeaponBone.name)
            {
                weaponBoneDict[allBones[i].name] = weaponBoneDict.Count;
            }
        }
        foreach (Transform bone in TopLevelWeaponBone)
        {
            for (int i = 0; i < allBones.Length; i++)
            {
                if (allBones[i].name == bone.name)
                {
                    weaponBoneDict[allBones[i].name] = weaponBoneDict.Count;
                    break;
                }
            }
        }

        return weaponBoneDict;
    }

    bool LoadBakedAnimations(ref Material mat, string textureTag)
    {
        Texture2D[] bakedAnimation = new Texture2D[NUM_TEXTURE];
        for (int i = 0; i < NUM_TEXTURE; i++)
        {
            string texturePath = string.Format(
                "BakedAnimations/{0}_BakedAnimation_{1}_{2}{3}", gameObject.name, textureTag, mortonSort ? "Morton" : "XY", i);
            bakedAnimation[i] = Resources.Load<Texture2D>(texturePath);
            if (bakedAnimation[i] == null)
            {
                Debug.LogError(string.Format("Baked animation {0} not found or not valid. Please bake animation first", texturePath));
                return false;
            }
        }

        float[] frameLength = new float[crowd.Animations.Count];
        float[] frameOffset = new float[crowd.Animations.Count];
        textureSize = new Vector2(bakedAnimation[0].width, bakedAnimation[1].height);

        float currentFrame = mortonSort ? 0 : 0.5f;
        for (int i = 0; i < crowd.Animations.Count; i++)
        {
            var anim = crowd.Animations[i];
            MathHelper.GetAnimationTime(anim.clip, mortonSort ? 1 : anim.sampleRate, out float animationTimeStep, out uint keyFrameCnt);
            frameLength[i] = mortonSort ? keyFrameCnt : keyFrameCnt / textureSize.y;
            frameOffset[i] = mortonSort ? currentFrame : currentFrame / textureSize.y;
            currentFrame += keyFrameCnt + ATLAS_PADDING;
        }

        // Load textures to GPU
        crowd.FrameLength = frameLength;
        crowd.FrameOffset = frameOffset;
        for (int i = 0; i < NUM_TEXTURE; i++)
        {
            mat.SetTexture("_Animation" + i, bakedAnimation[i]);
        }

        if (mortonSort)
        {
            int pow = (int)(Math.Log(bakedAnimation[0].width, 2) + 0.5);
            mat.SetInt("_size", (int)textureSize.x);
            mat.SetInt("_pow", pow);
        }

        return true;
    }

    // Fetch information from skinned mesh renderer
    void RegisterLOD(GameObject lod)
    {
        // Copy the shared mesh to modify it
        SkinnedMeshRenderer oldSkinnedRenderer = lod.GetComponent<SkinnedMeshRenderer>();
        Mesh mesh = Instantiate(oldSkinnedRenderer.sharedMesh);

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
                    (0.5f + w[i].boneIndex0) / textureSize.x,
                    (0.5f + w[i].boneIndex1) / textureSize.x,
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

        // Register LODs
        meshLODs.Add(mesh);

        // Destroy the child to prevent the default mesh renderer
        Destroy(lod);
    }

    void RegisterWeapon(Mesh weapon, Dictionary<string, int> weaponBoneDict)
    {
        Mesh mesh = Instantiate(weapon);

        BoneWeight[] w = mesh.boneWeights;
        Vector4[] boneInfo = new Vector4[mesh.vertexCount];
        Matrix4x4[] bindposes = mesh.bindposes;
        Vector3[] vertices = mesh.vertices;
        Vector3[] normals = mesh.normals;
        for (int i = 0; i < mesh.vertexCount; i++)
        {
            // Convert integer index to normalized index
            float weight = w[i].weight0 / (w[i].weight0 + w[i].weight1);
            if (!weaponBoneDict.TryGetValue(allBones[w[i].boneIndex0].name, out int bone0))
            {
                Debug.LogError("Weapon not rigged");
            }
            weaponBoneDict.TryGetValue(allBones[w[i].boneIndex1].name, out int bone1);

            if (mortonSort)
            {
                boneInfo[i] = new Vector4(
                    MathHelper.EncodeMorton((uint)bone0),
                    MathHelper.EncodeMorton((uint)bone1),
                    weight
                    );
            }
            else
            {
                boneInfo[i] = new Vector4(
                    (0.5f + bone0) / textureSize.x,
                    (0.5f + bone1) / textureSize.x,
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

        weaponMeshes.Add(mesh);
    }

    #endregion

    #region Update

    // Update is called once per frame
    void Update()
    {
        profiler.Reset();

        CullLOD();

        RenderInstanced();
    }

    IEnumerator AdaptPerformance()
    {
        yield return new WaitForSeconds(1.0f);
        for (; ; )
        {
            if (profiler.FPS < HardwareAdapter.TargetFrameRate)
            {
                lodSettings[2].size -= 10;
            }
            else if (lodSettings[2].size < lodSettings[2].maxSize && profiler.FPS > HardwareAdapter.TargetFrameRate + 3)
            {
                lodSettings[2].size += 10;
            }
            yield return new WaitForSeconds(.1f);
        }
    }

    IEnumerator ReOrder()
    {
        for (; ; )
        {
            for (int i = 0; i < crowd.crowdCount; i++)
            {
                elementDistance[i] = (Camera.main.transform.position - crowd.Positions2D[i]).sqrMagnitude;
            }
            Array.Sort(elementID, (a, b) => elementDistance[a].CompareTo(elementDistance[b]));
            yield return new WaitForSeconds(.1f);
        }
    }

    void CullLOD()
    {
        for (int i = 0; i < instanceLODs.Count; i++)
        {
            instanceLODs[i].Reset();
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
                    if (dist < lodSettings[lod].distance && instanceLODs[lod].Count < lodSettings[lod].size)
                    {
                        break;
                    }
                }
                if (lod < lodSettings.Count)
                {
                    instanceLODs[lod].Push(crowd.Transforms[i], crowd.AnimationStatusGPU[i]);
                }
            }
        }
    }

    // Manually draw meshes in spawing mode
    void RenderInstanced()
    {
        profiler.Log("Instance count: " + instanceLODs.Sum(item => item.Count));

        for (int i = 0; i < instanceLODs.Count; i++)
        {
            profiler.Log(string.Format("LOD_{0}: {1}", i, instanceLODs[i].Count));

            instanceProperties.SetVectorArray("_AnimState", instanceLODs[i].AnimStates);

            // We assume single mesh here (no sub meshes)
            if (i < 2)
            {
                Graphics.DrawMeshInstanced(
                    meshLODs[i], 0, GPUSkinMaterial,
                    instanceLODs[i].Transforms, instanceLODs[i].Count, instanceProperties,
                    shadowCastingMode, shadowReceivingMode);
            }
            else
            {
                Graphics.DrawMeshInstanced(
                    meshLODs[i], 0, HardwareAdapter.MortonSortEnabled ? GPUSkinMaterial : GPUSkinMaterialSimple,
                    instanceLODs[i].Transforms, instanceLODs[i].Count, instanceProperties,
                    UnityEngine.Rendering.ShadowCastingMode.Off, false);
            }

            // Weapons
            if (weaponMeshes.Count > 0)
            {
                for (int weapon = 0; weapon < weaponMeshes.Count; weapon++)
                {
                    if (i < 2)
                    {
                        Graphics.DrawMeshInstanced(
                            weaponMeshes[weapon], 0, GPUWeaponMaterial,
                            instanceLODs[i].Transforms, instanceLODs[i].Count, instanceProperties,
                            shadowCastingMode, shadowReceivingMode);
                    }
                    else
                    {
                        //Graphics.DrawMeshInstanced(
                        //    weaponMeshes[weapon], 0, HardwareAdapter.MortonSortEnabled ? GPUWeaponMaterial : GPUWeaponMaterialSimple,
                        //    instanceLODs[i].Transforms, instanceLODs[i].Count, instanceProperties,
                        //    UnityEngine.Rendering.ShadowCastingMode.Off, false);
                    }
                }
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
