using UnityEngine;

/// <summary>
/// Fixed "pilot standing on the field" camera that tracks the drone.
/// Optional auto-zoom keeps the drone readable on a monitor at distance.
/// </summary>
public class LineOfSightCamera : MonoBehaviour
{
    public Transform target;
    public float rotationSmoothing = 12f;
    public bool autoZoom = true;
    public float baseFov = 60f;
    public float minFov = 12f;
    public float zoomStartDistance = 8f;

    Camera cam;

    void Awake() => cam = GetComponent<Camera>();

    void LateUpdate()
    {
        if (target == null) return;

        Quaternion look = Quaternion.LookRotation(target.position - transform.position, Vector3.up);
        transform.rotation = Quaternion.Slerp(transform.rotation, look, 1f - Mathf.Exp(-rotationSmoothing * Time.deltaTime));

        if (cam != null && autoZoom)
        {
            float d = Vector3.Distance(target.position, transform.position);
            float fov = d <= zoomStartDistance ? baseFov : baseFov * zoomStartDistance / d;
            cam.fieldOfView = Mathf.Clamp(fov, minFov, baseFov);
        }
    }
}
