using UnityEngine;

/// <summary>A physics block that is part of a demolition structure; remembers where it started.</summary>
[RequireComponent(typeof(Rigidbody))]
public class DemoBlock : MonoBehaviour
{
    Vector3 startPos;
    Quaternion startRot;
    bool captured;
    Rigidbody rb;

    void Awake() => Capture();

    /// <summary>Records the built position the first time it is called (works even while the object is still inactive).</summary>
    void Capture()
    {
        if (captured) return;
        startPos = transform.position;
        startRot = transform.rotation;
        captured = true;
    }

    /// <summary>Metres the block has shifted from where it was built.</summary>
    public float Drift => Vector3.Distance(transform.position, startPos);
    /// <summary>Degrees the block has tipped from how it was built.</summary>
    public float Tilt => Vector3.Angle(transform.up, startRot * Vector3.up);

    public bool IsToppled => Drift > 0.8f || Tilt > 40f;

    public void ResetBlock()
    {
        Capture();
        if (rb == null) rb = GetComponent<Rigidbody>();
        rb.SetVelocity(Vector3.zero);
        rb.angularVelocity = Vector3.zero;
        transform.SetPositionAndRotation(startPos, startRot);
        if (gameObject.activeInHierarchy) rb.Sleep();
    }
}
