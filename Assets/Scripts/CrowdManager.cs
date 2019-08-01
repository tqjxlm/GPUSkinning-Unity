using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using System;


// Status of the animation playback and transition
struct PlayStatus
{
    public int src;
    public int dst;     // If there is no transition, dst is the current animation
    public float srcTime;
    public float dstTime;
    public float srcSpd;
    public float dstSpd;

    public PlayStatus(int beginID, float startTime)
    {
        dst = beginID;
        dstTime = startTime;
        src = 0;
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
    readonly Vector3 up = new Vector3(0, 1, 0);
    readonly Vector3 front = new Vector3(0, 0, 1);

    #region static attributes

    // Total crowd count to be spawned
    public int Count = 1000;

    // The radius of the allowed spawning region
    public float spawnRadius = 50;

    // Transition duration, in second
    public float transitionDuration = 0.3f;

    // All possible animation clips
    public List<AnimationResource> Animations  = new List<AnimationResource>();

    #endregion

    #region runtime attributes

    // Radius of the capsule collider
    public float Radius { get; private set; }

    // Texture frame length in fraction
    [HideInInspector] public float[] FrameLength;

    // Texture frame offset in fraction
    [HideInInspector] public float[] FrameOffset;

    // Status definition of all animations
    AnimationState[] stateDefinition;

    bool isAgentMoving = false;

    bool mortonSort = true;

    Camera mainCamera;

    SceneGrid grid;

    Vector3 cameraPosition2D;

    GPUSkinning gpuSkinning;

    WeaponManager weaponMgr;

    #endregion

    #region runtime status

    // World transform of all instances
    public Matrix4x4[] Transforms { get; private set; }

    // World position of all instances
    public Vector3[] Positions { get; private set; }

    // Animation status transfered to GPU: [frame_dst, weight_dst, frame_src, weight_src]
    public Vector4[] AnimationStatusGPU { get; private set; }

    // Weapons of all instances
    public int[] Weapons { get; private set; }

    // Animation transitions maintained in CPU
    PlayStatus[] AnimationStatusCPU;

    // Velocity of all instances
    Vector3[] Velocities;

    // Rotation of all instances
    Quaternion[] Rotations;

    // Cell id in the grid of all instances
    int[] GridID;

    #endregion

    void Awake()
    {
        weaponMgr = GetComponent<WeaponManager>();
        gpuSkinning = GetComponent<GPUSkinning>();
        grid = GetComponent<SceneGrid>();
        grid.center = new Vector2(spawnRadius, spawnRadius);

        // Runtime states
        Transforms = new Matrix4x4[Count];
        Positions = new Vector3[Count];
        AnimationStatusGPU = new Vector4[Count];
        Velocities = new Vector3[Count];
        AnimationStatusCPU = new PlayStatus[Count];
        Rotations = new Quaternion[Count];
        GridID = new int[Count];
        Weapons = new int[Count];

        // State machine definitions
        stateDefinition = new AnimationState[Animations.Count];
        stateDefinition[0] = new AnimationState(spd => spd < 0.1 ? 0 : 1);
        stateDefinition[1] = new AnimationState(spd => spd < 0.1 ? 0 : (spd >= 9 ? 2 : 1), spd => spd / 9 + 0.3f);
        stateDefinition[2] = new AnimationState(spd => spd < 9 ? 1 : 2);

        // Agent moving toggle
        GameObject AgentMovingToggleComponent = GameObject.Find("AgentMovingToggle");
        Toggle AgentMovingToggle = AgentMovingToggleComponent.GetComponent<Toggle>();
        AgentMovingToggle.onValueChanged.AddListener(delegate {
            isAgentMoving = AgentMovingToggle.isOn;
        });
        isAgentMoving = AgentMovingToggle.isOn;
    }

    void Start()
    {
        mortonSort = HardwareAdapter.MortonSortEnabled;
        mainCamera = Camera.main;
    }

    // Spawn a crowd
    public void Spawn()
    {
        CapsuleCollider collider = GetComponent<CapsuleCollider>();
        Radius = Math.Max(collider.radius, collider.height / 2);
        grid.sqrRadius = (float)Math.Pow(collider.radius * 2, 2);

        UnityEngine.Random.InitState(10);

        for (int i = 0; i < Count; i++)
        {
            Vector3 pos = new Vector3(UnityEngine.Random.Range(0, spawnRadius * 2), 0, UnityEngine.Random.Range(0, spawnRadius * 2));
            Quaternion rot = Quaternion.Euler(0, UnityEngine.Random.Range(0, 360), 0);

            float randomStart = UnityEngine.Random.Range(0, 1.0f);
            AnimationStatusGPU[i] = new Vector4(0, 1, 0, 0);
            AnimationStatusCPU[i] = new PlayStatus(0, randomStart);

            Positions[i] = pos;
            Rotations[i] = rot;
            Transforms[i] = Matrix4x4.TRS(pos, rot, Vector3.one);
            GridID[i] = -1;

            Weapons[i] = i;
            weaponMgr.Equip(i, i);
        }
    }

    // Update is called once per frame
    void Update()
    {
        float deltaTime = Time.deltaTime;
        cameraPosition2D = mainCamera.transform.position;
        cameraPosition2D.y = 0;

        for (int i = 0; i < Count; i++)
        {
            ref PlayStatus transition = ref AnimationStatusCPU[i];
            ref Vector4 state = ref AnimationStatusGPU[i];

            if (isAgentMoving)
            {
                UpdateVelocity(i, deltaTime);
                UpdateTransform(i, deltaTime);
            }
            UpdateStatus(ref transition, ref state, Velocities[i].sqrMagnitude, deltaTime);
            UpdateAnimation(ref transition, ref state, deltaTime);
        }
    }

    void UpdateVelocity(int i, float deltaTime)
    {
        const float MIN_DISTANCE = 8;
        const float MAX_DISTANCE = 16;
        const float ATTRACTION_RATE = 0.5f;
        const float FRICTION_RATE = 2.0f;
        const float PRESSURE_RATE = 1.8f;

        Vector3 v = Velocities[i];
        Vector3 direction = v.normalized;

        // Attraction
        Vector3 toCamera = cameraPosition2D - Positions[i];
        float distance = toCamera.magnitude;
        float attractionRate = distance < MIN_DISTANCE ? 0 : (distance > MAX_DISTANCE ? MAX_DISTANCE : distance) / distance * ATTRACTION_RATE;
        Vector3 attraction = toCamera * attractionRate;

        // Friction
        float speed = v.magnitude;
        float frictionRate = FRICTION_RATE;
        Vector3 friction = -v * frictionRate;

        // Pressure
        int[] neighbours = grid.GetNeighbours(GridID[i], v);
        float pressureRate = 0.0f;
        foreach(int j in neighbours)
        {
            if (j < 0)
            {
                break;
            }
            if (j != i)
            {
                Vector3 toNeighbour = Positions[j] - Positions[i];
                float dist = toNeighbour.magnitude;
                float dot = Vector3.Dot(toNeighbour, direction) / dist;
                if (dot > 0)
                {
                   pressureRate += dot * PRESSURE_RATE / (dist - Radius * 2);
                }
            }
            if (pressureRate >= 1.0f)
            {
                pressureRate = 1.0f;
                break;
            }
        }
        //Vector3 pressure = -direction * pressureRate;
        Vector3 acceleration = attraction * (1 - pressureRate) + friction;

        Velocities[i] = Vector3.ClampMagnitude(v + acceleration * deltaTime, 4);
    }

    void UpdateTransform(int i, float deltaTime)
    {
        Vector3 target = Positions[i] + Velocities[i] * deltaTime;
        int newID = grid.Move(i, GridID[i], target, Positions);
        if (newID >= 0)
        {
            Positions[i] = target;
            GridID[i] = newID;
        }
        else
        {
            Velocities[i] *= 0.1f;
        }

        if (Velocities[i].sqrMagnitude > 0.1)
        {
            Rotations[i] = Quaternion.LookRotation(Velocities[i], up);
        }
        Transforms[i] = Matrix4x4.TRS(
            Positions[i], Rotations[i], Vector3.one);
    }

    void UpdateStatus(ref PlayStatus play, ref Vector4 state, float sqrSpeed, float deltaTime)
    {
        // Check if a transition should happen
        int current = play.dst;
        int next = stateDefinition[current].nextState(sqrSpeed);
        if (stateDefinition[play.dst].speed != null)
        {
            play.dstSpd = stateDefinition[play.dst].speed(sqrSpeed);
        }

        if (next != current)
        {
            BeginAnimTransition(ref play, ref state, sqrSpeed, next);
        }
    }

    void UpdateAnimation(ref PlayStatus play, ref Vector4 state, float deltaTime)
    {
        // Play the first animation
        int id = play.dst;
        play.dstTime += deltaTime / Animations[id].clip.length * play.dstSpd;

        // Play the second animation if in transition
        if (state[3] > 0)
        {
            UpdateAnimTransition(ref state, deltaTime);
            id = play.src;
            play.srcTime += deltaTime / Animations[id].clip.length * play.srcSpd;
        }

        // Frame index suited for use in GPU.
        // Formula: global_frame_index = normalized_time * length_scale + global_offset
        float inSrc = (play.srcTime - (int)play.srcTime) * FrameLength[play.src];
        float inDst = (play.dstTime - (int)play.dstTime) * FrameLength[play.dst];
        float beforeSrc = FrameOffset[play.src];
        float beforeDst = FrameOffset[play.dst];

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

    void BeginAnimTransition(ref PlayStatus play, ref Vector4 state, float sqrSpeed, int endAnim)
    {
        if (state[3] < float.Epsilon)
        {
            // Set current animation as src, endAnim as dst
            play.src = play.dst;
            play.dst = endAnim;
            play.srcTime = play.dstTime;
            play.dstTime = 0;
            play.srcSpd = play.dstSpd;
            if (stateDefinition[play.dst].speed != null)
            {
                play.dstSpd = stateDefinition[play.dst].speed(sqrSpeed);
            }
            else
            {
                play.dstSpd = 1;
            }

            // Starting blend weight
            state[3] = 1;           // Weight of begin animation
            state[1] = 0;           // Weight of end animation
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
