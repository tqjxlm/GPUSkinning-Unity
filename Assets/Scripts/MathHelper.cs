using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Ref: Ben Kenwright. "A Beginners Guide to Dual-Quaternions".
// https://cs.gmu.edu/~jmlien/teaching/cs451/uploads/Main/dual-quaternion.pdf
public class DualQuaternion
{
    public Quaternion real;
    public Quaternion dual;

    public DualQuaternion()
    {
        real = new Quaternion(0, 0, 0, 1);
        dual = new Quaternion(0, 0, 0, 0);
    }

    public DualQuaternion(Quaternion r, Quaternion d)
    {
        real = r.normalized;
        dual = d;
    }

    public DualQuaternion(Quaternion r, Vector3 t)
    {
        real = r.normalized;
        dual = (new Quaternion(t.x, t.y, t.z, 0) * real).Scale(0.5f);
    }

    public DualQuaternion(Transform transform) : this(transform.localRotation, transform.localPosition)
    {
    }

    public DualQuaternion(Vector3 v)
    {
        real = new Quaternion(0, 0, 0, 1);
        dual = new Quaternion(v.x, v.y, v.z, 0);
    }

    public static DualQuaternion Normalize(DualQuaternion q)
    {
        float mag = Quaternion.Dot(q.real, q.real);
        return q * (1.0f / mag);
    }

    public static DualQuaternion operator +(DualQuaternion lhs, DualQuaternion rhs)
    {
        return new DualQuaternion(lhs.real.Add(rhs.real), lhs.dual.Add(rhs.dual));
    }

    public static DualQuaternion operator *(DualQuaternion q, float scale)
    {
        return new DualQuaternion(q.real.Scale(scale), q.dual.Scale(scale));
    }

    public static DualQuaternion operator *(DualQuaternion lhs, DualQuaternion rhs)
    {
        return new DualQuaternion(lhs.real * rhs.real, (lhs.real * rhs.dual).Add(lhs.dual * rhs.real));
    }

    public static DualQuaternion Conjugate(DualQuaternion q)
    {
        return new DualQuaternion(q.real.Conjugate(), q.dual.Conjugate());
    }
}

public class MathHelper
{
    // http://www.graphics.stanford.edu/~seander/bithacks.html#InterleaveBMN
    // Interleave lower 16 bits of x and y, so the bits of x
    // are in the even positions and bits from y in the odd;
    // z gets the resulting 32-bit Morton Number.  
    // x and y must initially be less than 65536.
    static public uint EncodeMorton(uint x, uint y)
    {
        x = EncodeMorton(x);
        y = EncodeMorton(y);

        return (x | (y << 1));
    }

    // Ref: http://www.graphics.stanford.edu/~seander/bithacks.html#InterleaveBMN
    static public uint EncodeMorton(uint x)
    {
        x = (x | (x << 8)) & 0x00FF00FF;
        x = (x | (x << 4)) & 0x0F0F0F0F;
        x = (x | (x << 2)) & 0x33333333;
        x = (x | (x << 1)) & 0x55555555;
        return x;
    }

    static public void GetAnimationTime(AnimationClip clip, float scale, out float animationTimeStep, out uint keyFrameCnt)
    {
        float animationLength = clip.length;
        animationTimeStep = 1.0f / clip.frameRate / scale;
        keyFrameCnt = (uint)(animationLength / animationTimeStep + 0.5);
    }
}
