using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System;

public class AnimationBaker
{
    const uint NUM_TEXTURE = GPUSkinRenderer.NUM_TEXTURE;
    const uint ATLAS_PADDING = GPUSkinRenderer.ATLAS_PADDING;

    static DualQuaternion[] boneTransforms;
    static Dictionary<string, int> allBones;
    static int weaponBoneID;

    // Retrieve bone mapping, bind poses and material properties
    static Dictionary<string, int> RetrieveBoneDictionary(SkinnedMeshRenderer renderer)
    {
        Dictionary<string, int> boneDict = new Dictionary<string, int>();

        Transform[] bones = renderer.bones;
        for (int i = 0; i < bones.Length; i++)
        {
            boneDict[bones[i].name] = i;
        }

        return boneDict;
    }

    // Recursively update all bone transforms
    static void FetchBoneMatrices(Transform bone, DualQuaternion parentTransform)
    {
        if (allBones.TryGetValue(bone.name, out int idx))
        {
            boneTransforms[idx] = parentTransform * (new DualQuaternion(bone));

            foreach (Transform childBone in bone)
            {
                FetchBoneMatrices(childBone, boneTransforms[idx]);
            }
        }
    }

    // Bake an animation into texture
    [MenuItem("GameObject/GPUSkinning/Bake Animation", false, 0)]
    static void BakeAnimations()
    {
        GameObject go = Selection.activeGameObject;
        Animator animator = go.GetComponent<Animator>();
        CrowdManager crowd = go.GetComponent<CrowdManager>();
        GPUSkinRenderer renderer = go.GetComponent<GPUSkinRenderer>();

        if (animator == null || crowd == null)
        {
            Debug.LogError("Invalid object. Please assign an animator and a virtual crowd to the game object");
            return;
        }

        animator.speed = 0;
        allBones = RetrieveBoneDictionary(go.GetComponentInChildren<SkinnedMeshRenderer>());
        boneTransforms = new DualQuaternion[allBones.Count];
        if (!allBones.TryGetValue(renderer.weaponBone.name, out weaponBoneID))
        {
            weaponBoneID = -1;
            Debug.Log("Weapon binding bone is not valid, skipping...");
        }

        // Bake character bones
        BakeAnimation(crowd, animator, go, true);
        BakeAnimation(crowd, animator, go, false);
    }

    static void BakeFrame(uint globalFrameIndex, bool morton, int size, int weaponSize, Color[][] pixels, Color[][] weaponPixels)
    {
        // Store the bone animation to an array of textures
        for (uint bone = 0; bone < allBones.Count; bone++)
        {
            DualQuaternion dq = boneTransforms[bone];
            if (dq == null) continue;
            var row0 = dq.real.ToVector4();
            var row1 = dq.dual.ToVector4();

            uint z;

            if (morton)
            {
                uint y = globalFrameIndex;
                uint x = bone;
                z = MathHelper.EncodeMorton(x, y);
            }
            else
            {
                uint y = globalFrameIndex % (uint)size;
                uint x = bone + globalFrameIndex / (uint)size * (uint)allBones.Count;
                z = y * (uint)size + x;
            }
            pixels[0][z] = row0;
            pixels[1][z] = row1;

            if (bone == weaponBoneID)
            {
                uint y = globalFrameIndex % (uint)weaponSize;
                uint x = 0 + globalFrameIndex / (uint)weaponSize * 1;
                z = y * (uint)weaponSize + x;
                weaponPixels[0][z] = row0;
                weaponPixels[1][z] = row1;
            }
        }
    }

    static void BakeAnimation(CrowdManager crowd, Animator animator, GameObject go, bool morton)
    {
        // Get bone and animator info
        List<AnimationResource> animations = crowd.Animations;
        Transform rootBone = animator.GetBoneTransform(HumanBodyBones.Hips);
        Transform globalRoot = rootBone.parent.transform;
        DualQuaternion globalTransform = new DualQuaternion(globalRoot.rotation, globalRoot.position);

        // Decide texture size
        uint totalFrame = 0;
        uint maxFrame = 0;
        foreach (AnimationResource anim in animations)
        {
            MathHelper.GetAnimationTime(anim.clip, morton ? 1 : anim.sampleRate, out float animationTimeStep, out uint keyFrameCnt);
            totalFrame += keyFrameCnt + ATLAS_PADDING;
            maxFrame = Math.Max(maxFrame, keyFrameCnt);
        }

        // Allocate texture: NUM_TEXTURE * [MAXIMUM_BONE, totalFrame]
        int size = 1;
        int foldings = 1;

        if (morton)
        {
            while (size < totalFrame || size < allBones.Count)
            {
                size *= 2;
            }
        }
        else
        {
            while (foldings * size < totalFrame)
            {
                size *= 2;
                foldings = size / allBones.Count;
            }
        }

        Texture2D[] animTexture = new Texture2D[NUM_TEXTURE];
        Color[][] pixels = new Color[NUM_TEXTURE][];
        for (int i = 0; i < NUM_TEXTURE; i++)
        {
            animTexture[i] = new Texture2D(size, size, TextureFormat.RGBAHalf, false, false)
            {
                wrapMode = TextureWrapMode.Mirror
            };
            pixels[i] = animTexture[i].GetPixels();
        }

        // Allocate weapon texture
        int weaponSize = 1;
        while (weaponSize * weaponSize < totalFrame)
        {
            weaponSize *= 2;
        }

        Texture2D[] weaponTexture = new Texture2D[NUM_TEXTURE];
        Color[][] weaponPixels = new Color[NUM_TEXTURE][];
        for (int i = 0; i < NUM_TEXTURE; i++)
        {
            weaponTexture[i] = new Texture2D(
                weaponSize, weaponSize, TextureFormat.RGBAHalf, false, false)
            {
                wrapMode = TextureWrapMode.Mirror
            };
            weaponPixels[i] = weaponTexture[i].GetPixels();
        }

        // Bake for every available animation clip
        uint frameOffset = 0;
        for (int animID = 0; animID < animations.Count; animID++)
        {
            AnimationResource anim = animations[animID];
            AnimationClip clip = anim.clip;
            MathHelper.GetAnimationTime(clip,
                morton ? 1 : anim.sampleRate,
                out float animationTimeStep,
                out uint keyFrameCnt);

            for (uint frame = 0; frame < keyFrameCnt + ATLAS_PADDING; frame++)
            {
                // Evaluate a frame
                animator.Play(clip.name, -1, (float)frame / keyFrameCnt);
                animator.Update(animationTimeStep);
                FetchBoneMatrices(rootBone, globalTransform);

                BakeFrame(frameOffset + frame, morton, size, weaponSize, pixels, weaponPixels);
            }

            frameOffset += keyFrameCnt + ATLAS_PADDING;
        }

        for (int i = 0; i < NUM_TEXTURE; i++)
        {
            animTexture[i].SetPixels(pixels[i]);
            animTexture[i].Apply();

            weaponTexture[i].SetPixels(weaponPixels[i]);
            weaponTexture[i].Apply();
        }

        // Save assets
        if (!AssetDatabase.IsValidFolder("Assets/Resources/BakedAnimations"))
        {
            AssetDatabase.CreateFolder("Assets/Resources", "BakedAnimations");
        }

        string assetPath;
        for (int i = 0; i < NUM_TEXTURE; i++)
        {
            if (morton)
            {
                assetPath = string.Format("Assets/Resources/BakedAnimations/{0}_BakedAnimation_Morton{1}.asset", go.name, i);
            }
            else
            {
                assetPath = string.Format("Assets/Resources/BakedAnimations/{0}_BakedAnimation_XY{1}.asset", go.name, i);
            }
            AssetHelper.SaveAsset(animTexture[i], assetPath, true);

            assetPath = string.Format("Assets/Resources/BakedAnimations/{0}_BakedAnimation_Weapon{1}.asset", go.name, i);
            AssetHelper.SaveAsset(weaponTexture[i], assetPath, true);
        }

        Debug.Log(string.Format("Animation {0} baked to Assets/Resources/BakedAnimations/", go.name));
    }
}
