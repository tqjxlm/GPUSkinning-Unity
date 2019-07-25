using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Ref: https://answers.unity.com/questions/402280/how-to-decompose-a-trs-matrix.html
public static class Matrix4x4Extensions
{
    public static Quaternion ExtractRotation(this Matrix4x4 m)
    {
        return Quaternion.LookRotation(
            m.GetColumn(2),
            m.GetColumn(1)
        );
    }

    public static Vector3 ExtractPosition(this Matrix4x4 m)
    {
        return m.GetColumn(3);
    }

    public static Vector3 ExtractScale(this Matrix4x4 m)
    {
        return new Vector3(
            m.GetColumn(0).magnitude,
            m.GetColumn(1).magnitude,
            m.GetColumn(2).magnitude
        );
    }
}
