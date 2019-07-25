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
    public const uint MAXIMUM_BONE = 64;    // 64 bones should be enough, but up to 500 is tested to be OK
    public const uint ATLAS_PADDING = 1;    // Atlas padding is used to avoid sample bleeding in texture atlas

    public Shader GPUSkinShader;
    public Shader GPUSkinShaderSimple;
    public List<LODSettings> lodSettings = new List<LODSettings>(3);

    CrowdManager crowd;
    Camera mainCamera;
    ExternalProfiler profiler;

    Vector2 textureSize;

    // Render info
    Material GPUSkinMaterial;
    Material GPUSkinMaterialSimple;
    MaterialPropertyBlock instanceProperties;
    bool mortonSort = true;
    float meshRadius;
    List<Mesh> meshLODs = new List<Mesh>();
    List<InstanceLOD> instanceLODs = new List<InstanceLOD>();
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
        profiler = gameObject.AddComponent<ExternalProfiler>();
        mainCamera = Camera.main;
        crowd = GetComponent<CrowdManager>();
        elementID = new int[crowd.crowdCount];
        elementDistance = new float[crowd.crowdCount];

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

        for (int i = 0; i < crowd.crowdCount; i++)
        {
            elementID[i] = i;
        }

        GPUSkinMaterial = new Material(GPUSkinShader);
        instanceProperties = new MaterialPropertyBlock();

        // Retrieve bone and material information (do only once since we assume all lod share the same bone setting)
        GPUSkinMaterial.CopyPropertiesFromMaterial(lodSettings[0].mesh.GetComponent<SkinnedMeshRenderer>().sharedMaterial);
        GPUSkinMaterial.enableInstancing = true;
        ToggleKeyword(GPUSkinMaterial, mortonSort, "MORTON_CODE", "XY_INDEXING");

        // Load baked animations and discard built-in animator
        LoadBakedAnimations();

        if (!HardwareAdapter.MortonSortEnabled)
        {
            GPUSkinMaterialSimple = new Material(GPUSkinShaderSimple) { enableInstancing = true };
            GPUSkinMaterialSimple.CopyPropertiesFromMaterial(lodSettings[2].mesh.GetComponent<SkinnedMeshRenderer>().sharedMaterial);
            GPUSkinMaterialSimple.CopyPropertiesFromMaterial(GPUSkinMaterial);
        }

        foreach (LODSettings lod in lodSettings)
        {
            RegisterLOD(lod.mesh);
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

        // Controls
        InitControls();

        // Spawn crowd
        crowd.Spawn();
        meshRadius = crowd.MeshRadius * 2;

        StartCoroutine("ReOrder");
        //StartCoroutine("AdaptPerformance");
    }

    bool LoadBakedAnimations()
    {
        Texture2D[] bakedAnimation = new Texture2D[NUM_TEXTURE];
        for (int i = 0; i < NUM_TEXTURE; i++)
        {
            string texturePath = string.Format(
                "BakedAnimations/{0}_{2}{1}", gameObject.name, i, mortonSort ? "BakedAnimation_Morton" : "BakedAnimation");
            bakedAnimation[i] = Resources.Load<Texture2D>(texturePath);
            if (bakedAnimation[i] == null)
            {
                Debug.LogError("Baked animation assets not found or not valid. Please bake animation first");
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
            GPUSkinMaterial.SetTexture("_Animation" + i, bakedAnimation[i]);
        }

        if (mortonSort)
        {
            int pow = (int)(Math.Log(bakedAnimation[0].width, 2) + 0.5);
            GPUSkinMaterial.SetInt("_size", (int)textureSize.x);
            GPUSkinMaterial.SetInt("_pow", pow);
        }

        return true;
    }

    // Fetch information from skinned mesh renderer
    void RegisterLOD(GameObject child)
    {
        // Copy the shared mesh to modify it
        SkinnedMeshRenderer oldSkinnedRenderer = child.GetComponent<SkinnedMeshRenderer>();
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
        instanceLODs.Add(new InstanceLOD(lodSettings[instanceLODs.Count].maxSize));

        // Deactive the child to prevent the default mesh renderer
        child.SetActive(false);
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
