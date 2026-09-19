using UnityEngine;

/// <summary>Third-person camera that follows behind the drone's heading.</summary>
public class ChaseCamera : MonoBehaviour
{
    public Transform target;
    public float distance = 4f;
    public float height = 1.5f;
    public float positionSmoothing = 8f;
    public float rotationSmoothing = 10f;

    Vector3 lastFlatForward = Vector3.forward;

    void LateUpdate()
    {
        if (target == null) return;

        Vector3 flat = Vector3.ProjectOnPlane(target.forward, Vector3.up);
        if (flat.sqrMagnitude > 0.05f) lastFlatForward = flat.normalized;

        Vector3 desired = target.position - lastFlatForward * distance + Vector3.up * height;
        if (desired.y < 0.3f) desired.y = 0.3f;

        transform.position = Vector3.Lerp(transform.position, desired, 1f - Mathf.Exp(-positionSmoothing * Time.deltaTime));
        Quaternion look = Quaternion.LookRotation(target.position + Vector3.up * 0.2f - transform.position, Vector3.up);
        transform.rotation = Quaternion.Slerp(transform.rotation, look, 1f - Mathf.Exp(-rotationSmoothing * Time.deltaTime));
    }
}
