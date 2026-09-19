using UnityEngine;

/// <summary>Continuously rotates the object — used for the floating target markers.</summary>
public class SpinMarker : MonoBehaviour
{
    public float degPerSec = 90f;
    public Vector3 axis = Vector3.up;

    void Update() => transform.Rotate(axis, degPerSec * Time.deltaTime, Space.World);
}
