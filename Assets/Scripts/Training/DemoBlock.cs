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

    public bool IsToppled => Vector3.Distance(transform.position, startPos) > 0.8f || Vector3.Angle(transform.up, startRot * Vector3.up) > 40f;

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
