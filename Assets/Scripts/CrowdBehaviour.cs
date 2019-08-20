using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using System;

public class CrowdBehaviour : MonoBehaviour
{
    readonly Vector3 up = new Vector3(0, 1, 0);
    readonly Vector3 front = new Vector3(0, 0, 1);

    // Total number of instances
    public int Count = 1000;

    // The radius of the allowed spawning region
    public float spawnRadius = 50;

    SceneGrid grid;

    Camera mainCamera;

    CrowdManager crowdMgr;

    WeaponManager weaponMgr;

    Vector3 cameraPosition2D;

    bool isAgentMoving = false;

    // World position of all instances
    Vector3[] Positions;

    // Velocity of all instances
    Vector3[] Velocities;

    // Rotation of all instances
    Quaternion[] Rotations;

    // Status definition of all animations
    AnimationState[] stateDefinition;

    // Weapons of all instances
    int[] Weapons;

    // Cell id in the grid of all instances
    int[] GridID;

    void Awake()
    {
        crowdMgr = GetComponent<CrowdManager>();
        crowdMgr.SetNumInstances(Count);

        weaponMgr = GetComponent<WeaponManager>();

        grid = gameObject.AddComponent<SceneGrid>();
        grid.center = new Vector2(spawnRadius, spawnRadius);

        GridID = new int[Count];
        Velocities = new Vector3[Count];
        Rotations = new Quaternion[Count];
        Positions = new Vector3[Count];
        Weapons = new int[Count];

        // State machine definitions
        stateDefinition = new AnimationState[3];
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

    // Start is called before the first frame update
    void Start()
    {
        mainCamera = Camera.main;

        grid.sqrRadius = (float)Math.Pow(crowdMgr.Radius * 2, 2);

        for (int i = 0; i < Count; i++)
        {
            weaponMgr.Equip(i, i);
            Weapons[i] = i;
            GridID[i] = -1;

            Vector3 pos = new Vector3(UnityEngine.Random.Range(0, spawnRadius * 2), 0, UnityEngine.Random.Range(0, spawnRadius * 2));
            Quaternion rot = Quaternion.Euler(0, UnityEngine.Random.Range(0, 360), 0);
            Positions[i] = pos;
            Rotations[i] = rot;
            crowdMgr.SetTransform(i, pos, rot);
        }
    }

    // Update is called once per frame
    void Update()
    {
        cameraPosition2D = mainCamera.transform.position;
        cameraPosition2D.y = 0;

        float deltaTime = Time.deltaTime;

        for (int i = 0; i < Count; i++)
        {
            if (isAgentMoving)
            {
                UpdateVelocity(i, deltaTime);
                UpdateTransform(i, deltaTime);
            }
            UpdateStatus(i, Velocities[i].sqrMagnitude, deltaTime);
        }
    }

    void UpdateStatus(int i, float sqrSpeed, float deltaTime)
    {
        // Check if a transition should happen
        int current = crowdMgr.AnimationStatusCPU[i].dstID;
        int next = stateDefinition[current].nextState(sqrSpeed);

        // Update animation play speed if needed
        if (stateDefinition[current].speed != null)
        {
            crowdMgr.SetPlaySpeed(i, stateDefinition[current].speed(sqrSpeed));
        }

        // Begin a transition
        if (next != current)
        {
            float playSpeed = 1.0f;
            if (stateDefinition[next].speed != null)
            {
                playSpeed = stateDefinition[next].speed(sqrSpeed);
            }
            crowdMgr.BeginAnimTransition(i, playSpeed, next);
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
        foreach (int j in neighbours)
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
                    pressureRate += dot * PRESSURE_RATE / (dist - crowdMgr.Radius * 2);
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

        crowdMgr.SetTransform(i, Positions[i], Rotations[i]);
    }
}
