using UnityEngine;

/// <summary>
/// Unity 6 renamed Rigidbody.velocity/drag/angularDrag. These helpers keep the
/// rest of the code compiling cleanly on both Unity 2022.3 LTS and Unity 6.
/// </summary>
public static class RigidbodyCompat
{
    public static Vector3 GetVelocity(this Rigidbody rb)
    {
#if UNITY_6000_0_OR_NEWER
        return rb.linearVelocity;
#else
        return rb.velocity;
#endif
    }

    public static void SetVelocity(this Rigidbody rb, Vector3 v)
    {
#if UNITY_6000_0_OR_NEWER
        rb.linearVelocity = v;
#else
        rb.velocity = v;
#endif
    }

    public static void SetDamping(this Rigidbody rb, float linear, float angular)
    {
#if UNITY_6000_0_OR_NEWER
        rb.linearDamping = linear;
        rb.angularDamping = angular;
#else
        rb.drag = linear;
        rb.angularDrag = angular;
#endif
    }
}
