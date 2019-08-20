using System.Collections.Generic;
using UnityEngine;
using System;

// Status of the animation playback and transition
public struct PlayStatus
{
    public int dstID;     // If there is no transition, dst is the current animation
    public int srcID;
    public float dstTime;
    public float srcTime;
    public float dstSpd;
    public float srcSpd;

    public PlayStatus(int beginID, float startTime)
    {
        dstID = beginID;
        dstTime = startTime;
        srcID = 0;
        srcTime = 0;
        dstSpd = 1;
        srcSpd = 1;
    }
}

// State definition for the animation state machine
struct AnimationState
{
    public Func<float, int> nextState;
    public Func<float, float> speed;

    public AnimationState(Func<float, int> transitionFunction, Func<float, float> speedFunction = null)
    {
        nextState = transitionFunction;
        speed = speedFunction;
    }
}

[Serializable]
public class AnimationResource
{
    public AnimationClip clip;
    public float sampleRate = 1.0f;
}

public class CrowdManager : MonoBehaviour
{
    // Total crowd count to be spawned
    public int Count { get; private set; }

    // Transition duration, in second
    public float transitionDuration = 0.3f;

    // All possible animation clips
    public List<AnimationResource> Animations  = new List<AnimationResource>();

    // Texture frame length in fraction
    [HideInInspector] public float[] FrameLength;

    // Texture frame offset in fraction
    [HideInInspector] public float[] FrameOffset;

    // Radius of the capsule collider
    public float Radius { get; private set; }

    // World position of all instances
    public Vector3[] Positions { get; private set; }

    // World transform of all instances
    public Matrix4x4[] Transforms { get; private set; }

    // Animation status transfered to GPU: [frame_dst, weight_dst, frame_src, weight_src]
    public Vector4[] AnimationStatusGPU { get; private set; }

    // Animation transitions maintained in CPU
    public PlayStatus[] AnimationStatusCPU { get; private set; }

    bool mortonSort = true;

    GPUSkinning gpuSkinning;

    void Awake()
    {
        gpuSkinning = GetComponent<GPUSkinning>();

        CapsuleCollider collider = GetComponent<CapsuleCollider>();
        Radius = Math.Max(collider.radius, collider.height / 2);
    }

    void Start()
    {
        mortonSort = HardwareAdapter.MortonSortEnabled;
    }

    // Update is called once per frame
    void Update()
    {
        float deltaTime = Time.deltaTime;

        for (int i = 0; i < Count; i++)
        {
            UpdateAnimation(ref AnimationStatusCPU[i], ref AnimationStatusGPU[i], deltaTime);
        }
    }

    // Spawn a crowd
    public void Spawn()
    {
        UnityEngine.Random.InitState(10);

        for (int i = 0; i < Count; i++)
        {
            float randomStart = UnityEngine.Random.Range(0, 1.0f);
            AnimationStatusGPU[i] = new Vector4(0, 1, 0, 0);
            AnimationStatusCPU[i] = new PlayStatus(0, randomStart);
        }
    }

    public void SetNumInstances(int CrowdCount)
    {
        Count = CrowdCount;

        // Runtime states
        Transforms = new Matrix4x4[Count];
        Positions = new Vector3[Count];
        AnimationStatusGPU = new Vector4[Count];
        AnimationStatusCPU = new PlayStatus[Count];
    }

    public void SetTransform(int ID, Vector3 pos, Quaternion rot)
    {
        Positions[ID] = pos;
        Transforms[ID] = Matrix4x4.TRS(pos, rot, Vector3.one);
    }

    public void BeginAnimTransition(int ID, float endSpeed, int endAnim)
    {
        ref PlayStatus play = ref AnimationStatusCPU[ID];
        ref Vector4 state = ref AnimationStatusGPU[ID];

        if (state[3] < float.Epsilon)
        {
            // Set current animation as src, endAnim as dst
            play.srcID = play.dstID;
            play.dstID = endAnim;
            play.srcTime = play.dstTime;
            play.dstTime = 0;
            play.srcSpd = play.dstSpd;
            play.dstSpd = endSpeed;

            // Start blending weight
            state[3] = 1;           // Weight of begin animation
            state[1] = 0;           // Weight of end animation
        }
    }

    public void SetPlaySpeed(int ID, float speed)
    {
        AnimationStatusCPU[ID].dstSpd = speed;
    }

    void UpdateAnimation(ref PlayStatus play, ref Vector4 state, float deltaTime)
    {
        // Play the first animation
        int id = play.dstID;
        play.dstTime += deltaTime / Animations[id].clip.length * play.dstSpd;

        // Play the second animation if in transition
        if (state[3] > 0)
        {
            UpdateAnimTransition(ref state, deltaTime);
            id = play.srcID;
            play.srcTime += deltaTime / Animations[id].clip.length * play.srcSpd;
        }

        // Frame index suited for use in GPU.
        // Formula: global_frame_index = normalized_time * length_scale + global_offset
        float inSrc = (play.srcTime - (int)play.srcTime) * FrameLength[play.srcID];
        float inDst = (play.dstTime - (int)play.dstTime) * FrameLength[play.dstID];
        float beforeSrc = FrameOffset[play.srcID];
        float beforeDst = FrameOffset[play.dstID];

        if (mortonSort)
        {
            // For Morton Sort, the index decoded in the shader
            state[2] = MathHelper.EncodeMorton((uint)inSrc + (uint)beforeSrc);
            state[0] = MathHelper.EncodeMorton((uint)inDst + (uint)beforeDst);
        }
        else
        {
            // For XY indexing, the index is unfolded in the shader
            state[2] = inSrc + beforeSrc;
            state[0] = inDst + beforeDst;
        }
    }

    void UpdateAnimTransition(ref Vector4 state, float deltaTime)
    {
        // Decrease src weight and increase dst weight
        float normalizedTransitionStep = deltaTime / transitionDuration;
        state[3] = state[3] < normalizedTransitionStep ? 0 : state[3] - normalizedTransitionStep;
        state[1] = 1 - state[3];
    }
}
