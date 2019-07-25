using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;

public static class VectorExtensions
{
    public static float MinComponent(this Vector4 v)
    {
        return Math.Min(Math.Min(Math.Min(v.x, v.y), v.z), v.w);
    }

    public static float MaxComponent(this Vector4 v)
    {
        return Math.Max(Math.Max(Math.Max(v.x, v.y), v.z), v.w);
    }
}
