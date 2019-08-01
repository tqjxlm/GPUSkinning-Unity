using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public static class QuaternionExtensions
{
    public static Quaternion Scale(this Quaternion q, float scale)
    {
        return new Quaternion(q.x * scale, q.y * scale, q.z * scale, q.w * scale);
    }

    public static Quaternion Add(this Quaternion lhs, Quaternion rhs)
    {
        return new Quaternion(lhs.x + rhs.x, lhs.y + rhs.y, lhs.z + rhs.z, lhs.w + rhs.w);
    }

    public static Quaternion Conjugate(this Quaternion q)
    {
        return new Quaternion(-q.x, -q.y, -q.z, q.w);
    }

    public static Vector4 ToVector4(this Quaternion q)
    {
        return new Vector4(q.x, q.y, q.z, q.w);
    }

    public static void FromColor(ref this Quaternion q, Color c)
    {
        q.x = c.r;
        q.y = c.g;
        q.z = c.b;
        q.w = c.a;
    }
}