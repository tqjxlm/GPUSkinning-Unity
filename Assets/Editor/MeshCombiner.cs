using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System.IO;

public class MeshCombiner
{
    [MenuItem("GameObject/GPUSkinning/Merge Mesh", false, 0)]
    public static void MergeSkinnedMeshEditor()
    {
        GameObject go = Selection.activeGameObject;
        MergeSkinnedMesh(go);
    }

    public static void MergeSkinnedMesh(GameObject go)
    {
        SkinnedMeshRenderer[] smRenderers = go.GetComponentsInChildren<SkinnedMeshRenderer>();
        List<Transform>[] boneCollections = new List<Transform>[smRenderers.Length];
        List<BoneWeight> boneWeights = new List<BoneWeight>();
        List<CombineInstance> combineInstances = new List<CombineInstance>();
        int numSubs = 0;

        Material originalMaterial = smRenderers[0].sharedMaterial;

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

        GameObject child = new GameObject("LOD0");
        child.transform.parent = go.transform;
        SkinnedMeshRenderer r = child.AddComponent<SkinnedMeshRenderer>();
        r.sharedMesh = new Mesh();
        r.sharedMesh.CombineMeshes(combineInstances.ToArray(), true, true);

        r.sharedMaterial = originalMaterial;

        r.bones = boneCollections[0].ToArray();
        r.sharedMesh.boneWeights = boneWeights.ToArray();
        r.sharedMesh.bindposes = bindposes.ToArray();
        r.sharedMesh.RecalculateBounds();

        r.sharedMesh.Optimize();

        string path = AssetHelper.GetStoragePath("MergedMeshes");
        string meshPath = AssetDatabase.GenerateUniqueAssetPath(path + "/" + go.name + ".asset");
        AssetHelper.SaveAsset(r.sharedMesh, meshPath);

        Debug.Log("bindposes size: " + bindposes.Count);
        Debug.Log("bone size: " + boneCollections[0].Count);
    }

    [MenuItem("GameObject/GPUSkinning/Deform Mesh - Fat", false, 0)]
    public static void DeformMeshFat()
    {
        GameObject go = Selection.activeGameObject;
        MeshFilter meshFilter = go.GetComponent<MeshFilter>();
        Mesh newMesh = Object.Instantiate(meshFilter.sharedMesh);
        Vector3[] vertices = newMesh.vertices;
        Vector3 center = Vector3.zero;
        for (int i = 0; i < newMesh.vertexCount; i++)
        {
            center += vertices[i];
        }
        center /= newMesh.vertexCount;

        for (int i = 0; i < newMesh.vertexCount; i++)
        {
            vertices[i].x = vertices[i].x + (vertices[i].x - center.x) * vertices[i].y;
            vertices[i].z = vertices[i].z + (vertices[i].z - center.z) * vertices[i].y;
        }
        newMesh.vertices = vertices;

        string path = AssetHelper.GetStoragePath("DeformedMeshes");
        string meshPath = AssetDatabase.GenerateUniqueAssetPath(path + "/" + go.name + "_Deformed.asset");
        AssetHelper.SaveAsset(newMesh, meshPath);
    }

    [MenuItem("GameObject/GPUSkinning/Deform Mesh - Long", false, 0)]
    public static void DeformMeshLong()
    {
        GameObject go = Selection.activeGameObject;
        MeshFilter meshFilter = go.GetComponent<MeshFilter>();
        Mesh newMesh = Object.Instantiate(meshFilter.sharedMesh);
        Vector3[] vertices = newMesh.vertices;
        float minY = float.MaxValue;
        for (int i = 0; i < newMesh.vertexCount; i++)
        {
            minY = Mathf.Min(minY, vertices[i].y);
        }

        for (int i = 0; i < newMesh.vertexCount; i++)
        {
            vertices[i].y = vertices[i].y + (vertices[i].y - minY) * 1.5f;
        }
        newMesh.vertices = vertices;

        string path = AssetHelper.GetStoragePath("DeformedMeshes");
        string meshPath = AssetDatabase.GenerateUniqueAssetPath(path + "/" + go.name + "_Deformed.asset");
        AssetHelper.SaveAsset(newMesh, meshPath);
    }
}
