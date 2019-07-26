using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System.IO;

public class MeshCombiner
{
    [MenuItem("GameObject/Merge Mesh", false, 0)]
    public static void MergeSkinnedMesh()
    {
        GameObject go = Selection.activeGameObject;
        SkinnedMeshRenderer[] smRenderers = go.GetComponentsInChildren<SkinnedMeshRenderer>();
        List<Transform>[] boneCollections = new List<Transform>[smRenderers.Length];
        List<BoneWeight> boneWeights = new List<BoneWeight>();
        List<CombineInstance> combineInstances = new List<CombineInstance>();
        int numSubs = 0;

        Material originalMaterial = smRenderers[0].sharedMaterial;
        string path = StoragePathUsing1stMeshAndSubPath(smRenderers[0].sharedMesh, "MergedMeshes");

        foreach (SkinnedMeshRenderer smr in smRenderers)
            numSubs += smr.sharedMesh.subMeshCount;

        int[] meshIndex = new int[numSubs];
        for (int s = 0; s < smRenderers.Length; s++)
        {
            SkinnedMeshRenderer smr = smRenderers[s];
            List<Transform> bones = new List<Transform>();
            boneCollections[s] = bones;

            BoneWeight[] meshBoneweight = smr.sharedMesh.boneWeights;

            // May want to modify this if the renderer shares bones as unnecessary bones will get added.
            foreach (BoneWeight bw in meshBoneweight)
            {
                boneWeights.Add(bw);
            }

            Transform[] meshBones = smr.bones;
            foreach (Transform bone in meshBones)
                bones.Add(bone);

            CombineInstance ci = new CombineInstance { mesh = smr.sharedMesh };
            meshIndex[s] = ci.mesh.vertexCount;
            ci.transform = smr.transform.localToWorldMatrix;
            combineInstances.Add(ci);

            Object.DestroyImmediate(smr.gameObject);
        }

        List<Matrix4x4> bindposes = new List<Matrix4x4>();

        for (int b = 0; b < boneCollections[0].Count; b++)
        {
            bindposes.Add(boneCollections[0][b].worldToLocalMatrix * go.transform.worldToLocalMatrix);
        }

        SkinnedMeshRenderer r = go.AddComponent<SkinnedMeshRenderer>();
        r.sharedMesh = new Mesh();
        r.sharedMesh.CombineMeshes(combineInstances.ToArray(), true, true);

        r.sharedMaterial = originalMaterial;

        r.bones = boneCollections[0].ToArray();
        r.sharedMesh.boneWeights = boneWeights.ToArray();
        r.sharedMesh.bindposes = bindposes.ToArray();
        r.sharedMesh.RecalculateBounds();

        string meshPath = AssetDatabase.GenerateUniqueAssetPath(path + "/" + go.name + ".asset");
        AssetDatabase.CreateAsset(r.sharedMesh, meshPath);
        AssetDatabase.SaveAssets();

        Debug.Log("bindposes size: " + bindposes.Count);
        Debug.Log("bone size: " + boneCollections[0].Count);
    }

    static string StoragePathUsing1stMeshAndSubPath(Mesh mesh, string subPath)
    {
        if (mesh != null)
        {
            string assetPath = AssetDatabase.GetAssetPath(mesh) + "/../";            
            if (!Directory.Exists(assetPath + subPath)) AssetDatabase.CreateFolder(assetPath, subPath);
            return assetPath + subPath;
        }
        return null;
    }
}
