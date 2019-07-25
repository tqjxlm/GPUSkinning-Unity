using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

public class AnimationBaker
{
    const uint NUM_TEXTURE = GPUSkinning.NUM_TEXTURE;
    const uint MAXIMUM_BONE = GPUSkinning.MAXIMUM_BONE;
    const uint ATLAS_PADDING = GPUSkinning.ATLAS_PADDING;

    // Bone info, all with size MAXIMUM_BONE
    static DualQuaternion[] boneTransforms = new DualQuaternion[MAXIMUM_BONE];
    static Matrix4x4[] bindPoses;
    static Dictionary<string, int> boneDict = new Dictionary<string, int>();

    // Retrieve bone mapping, bind poses and material properties
    static void RetrieveBoneInfo(SkinnedMeshRenderer renderer)
    {
        bindPoses = renderer.sharedMesh.bindposes;

        Transform[] bones = renderer.bones;
        for (int i = 0; i < bones.Length; i++)
        {
            boneDict[bones[i].name] = i;
        }
    }

    // Recursively update all bone transforms
    static void FetchBoneMatrices(Transform bone, DualQuaternion parentTransform)
    {
        if (boneDict.TryGetValue(bone.name, out int idx))
        {
            boneTransforms[idx] = parentTransform * (new DualQuaternion(bone));

            foreach (Transform childBone in bone)
            {
                FetchBoneMatrices(childBone, boneTransforms[idx]);
            }
        }
    }

    // Bake an animation into texture
    [MenuItem("GameObject/Bake Animation", false, 0)]
    static void BakeAnimations()
    {
        GameObject go = Selection.activeGameObject;
        Animator animator = go.GetComponent<Animator>();
        CrowdManager crowd = go.GetComponent<CrowdManager>();
        if (animator == null || crowd == null)
        {
            Debug.LogError("Invalid object. Please assign an animator and a virtual crowd to the game object");
            return;
        }

        BakeAnimation(crowd, animator, go, true);
        BakeAnimation(crowd, animator, go, false);
    }

    static void BakeAnimation(CrowdManager crowd, Animator animator, GameObject go, bool morton)
    {
        // Get bone and animator info
        List<AnimationResource> animations = crowd.Animations;
        animator.speed = 0;
        Transform rootBone = animator.GetBoneTransform(HumanBodyBones.Hips);
        Transform globalRoot = rootBone.parent.transform;
        DualQuaternion globalTransform = new DualQuaternion(globalRoot.rotation, globalRoot.position);

        SkinnedMeshRenderer renderer = go.GetComponentInChildren<SkinnedMeshRenderer>();
        RetrieveBoneInfo(renderer);

        // Decide texture size
        uint totalFrame = 0;
        foreach (AnimationResource anim in animations)
        {
            MathHelper.GetAnimationTime(anim.clip, morton ? 1 : anim.sampleRate, out float animationTimeStep, out uint keyFrameCnt);
            totalFrame += keyFrameCnt + ATLAS_PADDING;
        }

        int size = 1;
        while (size < totalFrame)
        {
            size *= 2;
        }

        // Allocate texture: NUM_TEXTURE * [MAXIMUM_BONE, totalFrame]
        Texture2D[] animTexture = new Texture2D[NUM_TEXTURE];
        Color[][] pixels = new Color[NUM_TEXTURE][];

        for (int i = 0; i < NUM_TEXTURE; i++)
        {
            animTexture[i] = new Texture2D(
                size, size, TextureFormat.RGBAHalf, false, false);
            pixels[i] = animTexture[i].GetPixels();
        }

        // Bake for every available animation clip
        uint frameOffset = 0;
        foreach (AnimationResource anim in animations)
        {
            AnimationClip clip = anim.clip;
            MathHelper.GetAnimationTime(clip, morton ? 1 : anim.sampleRate, out float animationTimeStep, out uint keyFrameCnt);

            for (uint frame = 0; frame < keyFrameCnt + ATLAS_PADDING; frame++)
            {
                // Evaluate a frame
                animator.Play(clip.name, -1, (float)frame / keyFrameCnt);
                animator.Update(animationTimeStep);
                FetchBoneMatrices(rootBone, globalTransform);

                // Store the bone animation to an array of textures
                for (uint bone = 0; bone < MAXIMUM_BONE; bone++)
                {
                    DualQuaternion dq = boneTransforms[bone];
                    if (dq == null)
                    {
                        continue;
                    }

                    uint y = frameOffset + frame;
                    uint x = bone;
                    var row0 = dq.real.ToVector4();
                    var row1 = dq.dual.ToVector4();

                    if (morton)
                    {
                        uint z = MathHelper.EncodeMorton(x, y);
                        pixels[0][z] = row0;
                        pixels[1][z] = row1;
                    }
                    else
                    {
                        uint z = y * (uint)size + x;
                        pixels[0][z] = row0;
                        pixels[1][z] = row1;
                    }
                }
            }

            frameOffset += keyFrameCnt + ATLAS_PADDING;
        }

        for (int i = 0; i < NUM_TEXTURE; i++)
        {
            animTexture[i].SetPixels(pixels[i]);
            animTexture[i].Apply();
        }

        // Save assets
        if (!AssetDatabase.IsValidFolder("Assets/Resources/BakedAnimations"))
        {
            AssetDatabase.CreateFolder("Assets/Resources", "BakedAnimations");
        }

        for (int i = 0; i < NUM_TEXTURE; i++)
        {
            if (morton)
            {
                string assetPath = string.Format("Assets/Resources/BakedAnimations/{0}_BakedAnimation_Morton{1}.asset", go.name, i);
                AssetDatabase.DeleteAsset(assetPath);
                AssetDatabase.CreateAsset(animTexture[i], assetPath);
            }
            else
            {
                string assetPath = string.Format("Assets/Resources/BakedAnimations/{0}_BakedAnimation{1}.asset", go.name, i);
                AssetDatabase.DeleteAsset(assetPath);
                AssetDatabase.CreateAsset(animTexture[i], assetPath);
            }
        }

        Debug.Log(string.Format("Animation {0} baked to Assets/Resources/BakedAnimations/", go.name));
    }
}
