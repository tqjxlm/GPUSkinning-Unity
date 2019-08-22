using UnityEngine;
using UnityEditor;
using System.IO;

public class MeshSimplifier
{
    [MenuItem("GameObject/GPUSkinning/Simplify Mesh", false, 0)]
    static void SimplifyMesh()
    {
        // Get mesh
        GameObject go = Selection.activeGameObject;
        SkinnedMeshRenderer smRenderer = go.GetComponentInChildren<SkinnedMeshRenderer>();
        Mesh sourceMesh = smRenderer.sharedMesh;

        // Simplify
        float quality = 0.7f;
        var meshSimplifier = new UnityMeshSimplifier.MeshSimplifier();
        meshSimplifier.Initialize(sourceMesh);
        meshSimplifier.SimplifyMesh(quality);
        var destMesh = meshSimplifier.ToMesh();
        destMesh.bindposes = sourceMesh.bindposes;

        destMesh.Optimize();

        // Save to asset
        string path = AssetHelper.GetStoragePath("SimplifiedMeshes");
        string meshPath = AssetDatabase.GenerateUniqueAssetPath(path + "/" + go.name + "_LOD.asset");
        AssetHelper.SaveAsset(destMesh, meshPath);

        // Add the new mesh as sibling
        GameObject lodGo = new GameObject("New LOD");
        lodGo.transform.parent = go.transform;

        SkinnedMeshRenderer lodRenderer = lodGo.AddComponent<SkinnedMeshRenderer>();
        UnityEditorInternal.ComponentUtility.CopyComponent(smRenderer);
        UnityEditorInternal.ComponentUtility.PasteComponentValues(lodRenderer);

        lodRenderer.sharedMesh = destMesh;

        Debug.Log("Simplified mesh saved to " + meshPath);
    }
}
