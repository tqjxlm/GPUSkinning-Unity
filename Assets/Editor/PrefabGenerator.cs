using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

public class PrefabGenerator
{
    [MenuItem("GameObject/GPUSkinning/Generate Prefab", false, 0)]
    static void GeneratePrefab()
    {
        GameObject go = Selection.activeGameObject;

        // Combine mesh if necessary
        if (go.transform.childCount > 2)
        {
            MeshCombiner.MergeSkinnedMesh(go);
        }

        // Components
        var renderer = go.AddComponent<GPUSkinRenderer>();
        var crowdMgr = go.AddComponent<CrowdManager>();
        var weaponMgr = go.AddComponent<WeaponManager>();
        var collider = go.AddComponent<CapsuleCollider>();
        var animator = go.GetComponent<Animator>();
        if (!animator)
        {
            animator = go.AddComponent<Animator>();
        }
        animator.enabled = false;

        renderer.GPUSkinShader = (Shader)AssetDatabase.LoadAssetAtPath("Assets/Materials/GPUSkinShader.shader", typeof(Shader));
        renderer.GPUSkinShaderSimple = (Shader)AssetDatabase.LoadAssetAtPath("Assets/Materials/GPUSkinShaderSimple.shader", typeof(Shader));

        var mesh = go.GetComponentInChildren<SkinnedMeshRenderer>();
        if (mesh)
        {
            var bounds = mesh.bounds;
            collider.center = bounds.center;
            collider.radius = new Vector2(bounds.extents.x, bounds.extents.y).magnitude;
            collider.height = bounds.size.z;
        }
    }
}
