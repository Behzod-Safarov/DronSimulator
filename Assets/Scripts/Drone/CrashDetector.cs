using System;
using UnityEngine;

/// <summary>
/// Detects crashes (hard impacts, resting upside down, leaving the world) and
/// respawns the drone at its spawn point.
/// </summary>
[RequireComponent(typeof(DronePhysics), typeof(FlightController))]
public class CrashDetector : MonoBehaviour
{
    [Tooltip("Impact speed (m/s) above which a collision counts as a crash")]
    public float crashSpeed = 5f;
    [Tooltip("Seconds resting upside down before it counts as a crash")]
    public float upsideDownTimeout = 1.5f;
    public bool autoReset = true;
    public float autoResetDelay = 1.5f;
    public float worldRadius = 300f;

    public Transform spawnPoint;
    /// <summary>Scenarios that handle impacts themselves (demolition) turn crash detection off.</summary>
    public bool suppressCrashes;

    public event Action OnCrashed;
    /// <summary>Fired on every collision with the impact speed in m/s (also gentle touch-downs).</summary>
    public event Action<float> OnImpact;

    public int CrashCount { get; private set; }
    public bool Crashed { get; private set; }
    public string Message { get; private set; } = "";
    public float MessageTime { get; private set; }

    DronePhysics drone;
    FlightController fc;
    float upsideDownSince = -1f;
    float crashedAt;

    void Awake()
    {
        drone = GetComponent<DronePhysics>();
        fc = GetComponent<FlightController>();
    }

    void OnCollisionEnter(Collision c)
    {
        float speed = c.relativeVelocity.magnitude;
        OnImpact?.Invoke(speed);
        if (!Crashed && !suppressCrashes && speed > crashSpeed)
            Crash($"CRASH  ({speed:0.0} m/s)");
    }

    void Update()
    {
        if (Crashed)
        {
            if (autoReset && Time.time - crashedAt > autoResetDelay) Respawn();
            return;
        }
        if (suppressCrashes) return;

        if (transform.up.y < 0f && drone.IsGrounded)
        {
            if (upsideDownSince < 0f) upsideDownSince = Time.time;
            else if (Time.time - upsideDownSince > upsideDownTimeout) Crash("Flipped over");
        }
        else
        {
            upsideDownSince = -1f;
        }

        Vector3 p = transform.position;
        if (p.y < -5f || new Vector2(p.x, p.z).magnitude > worldRadius)
            Crash("Out of bounds");
    }

    void Crash(string why)
    {
        Crashed = true;
        crashedAt = Time.time;
        CrashCount++;
        fc.Disarm();
        ShowMessage(why);
        OnCrashed?.Invoke();
    }

    public void Respawn()
    {
        Vector3 pos = spawnPoint != null ? spawnPoint.position : Vector3.up * 0.1f;
        Quaternion rot = spawnPoint != null ? Quaternion.Euler(0f, spawnPoint.eulerAngles.y, 0f) : Quaternion.identity;
        fc.Disarm();
        drone.ResetTo(pos, rot);
        drone.ResetBattery();
        GetComponent<PilotInput>()?.ResetKeyboardThrottle();
        Crashed = false;
        upsideDownSince = -1f;
        ShowMessage("Reset - press arm to fly");
    }

    public void ShowMessage(string text)
    {
        Message = text;
        MessageTime = Time.time;
    }
}
