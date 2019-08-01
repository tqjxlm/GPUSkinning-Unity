using UnityEngine;
using UnityEditor;
using System.IO;

public class MeshSimplifier
{
    [MenuItem("GameObject/Simplify Mesh", false, 0)]
    static void SimplifyMesh()
    {
        GameObject go = Selection.activeGameObject;
        SkinnedMeshRenderer smRenderer = go.GetComponentInChildren<SkinnedMeshRenderer>();
        Mesh sourceMesh = smRenderer.sharedMesh;

        float quality = 0.7f;
        var meshSimplifier = new UnityMeshSimplifier.MeshSimplifier();
        meshSimplifier.Initialize(sourceMesh);
        meshSimplifier.SimplifyMesh(quality);
        var destMesh = meshSimplifier.ToMesh();
        destMesh.bindposes = sourceMesh.bindposes;

        destMesh.Optimize();

        string path = AssetHelper.GetStoragePath("SimplifiedMeshes");
        string meshPath = AssetDatabase.GenerateUniqueAssetPath(path + "/" + go.name + "_LOD.asset");
        AssetHelper.SaveAsset(destMesh, meshPath);

        smRenderer.sharedMesh = destMesh;

        Debug.Log("Simplified mesh saved to " + meshPath);
    }
}
