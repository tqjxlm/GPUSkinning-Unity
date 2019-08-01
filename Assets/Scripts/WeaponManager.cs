using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class WeaponManager : MonoBehaviour
{
    public List<Mesh> AvailableWeapons;
    public Material WeaponMaterial;

    public int Count { get; private set; }

    public Vector3[] Positions { get; private set; }

    public Matrix4x4[] Transforms { get; private set; }

    public int[] Types { get; private set; }

    public float[] Radius { get; private set; }

    public int[] Users { get; private set; }

    CrowdManager crowd;

    public Vector4 NullAnimStatus { get; private set; }

    void Awake()
    {
        crowd = GetComponent<CrowdManager>();

        Radius = new float[AvailableWeapons.Count];
        for (int i = 0; i < AvailableWeapons.Count; i++)
        {
            Radius[i] = AvailableWeapons[i].bounds.extents.magnitude;
        }

        Positions = new Vector3[crowd.Count];
        Transforms = new Matrix4x4[crowd.Count];
        Types = new int[crowd.Count];
        Users = new int[crowd.Count];

        Count = crowd.Count;

        for (int i = 0; i < crowd.Count; i++)
        {
            Types[i] = Random.Range(0, AvailableWeapons.Count);
            Users[i] = -1;
        }
    }

    void Update()
    {
        for (int i = 0; i < crowd.Count; i++)
        {
            int userID = Users[i];
            if (userID != -1)
            {
                Positions[i] = crowd.Positions[userID];
                Transforms[i] = crowd.Transforms[userID];
            }
        }
    }

    public void Unequip(int weaponID, int agentID)
    {
        Users[weaponID] = -1;
    }

    public bool Equip(int weaponID, int agentID)
    {
        if (Users[weaponID] == -1)
        {
            Users[weaponID] = agentID;
            return true;
        }

        return false;
    }
}
