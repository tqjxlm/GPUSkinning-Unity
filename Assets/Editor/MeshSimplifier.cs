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

        string path = GetStoragePath(sourceMesh, "SimplifiedMeshes");
        string meshPath = AssetDatabase.GenerateUniqueAssetPath(path + "/" + go.name + "_LOD.asset");
        AssetDatabase.CreateAsset(destMesh, meshPath);
        AssetDatabase.SaveAssets();

        Debug.Log("Simplified mesh saved to " + meshPath);
    }

    static string GetStoragePath(Mesh mesh, string subPath)
    {
        if (mesh != null)
        {
            string assetPath = AssetDatabase.GetAssetPath(mesh);
            string assetDir = assetPath.Remove(assetPath.LastIndexOf('/')) + "/../";
            if (!Directory.Exists(assetDir + subPath)) AssetDatabase.CreateFolder(assetDir, subPath);
            return assetDir + subPath;
        }
        return null;
    }
}
