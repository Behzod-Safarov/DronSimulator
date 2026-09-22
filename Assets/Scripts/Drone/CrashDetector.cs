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
    [Tooltip("Impacts between this and crashSpeed break a prop instead: the quad keeps flying, badly, until you reset")]
    public float propStrikeSpeed = 2.4f;
    public bool propDamage = true;
    [Tooltip("Seconds resting upside down before it counts as a crash")]
    public float upsideDownTimeout = 1.5f;
    public bool autoReset = true;
    public float autoResetDelay = 1.5f;
    [Tooltip("Kept inside the 256m PhysX broadphase world bounds (ProjectSettings/DynamicsManager)")]
    public float worldRadius = 250f;

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
    float respawnedAt = -10f;

    void Awake()
    {
        drone = GetComponent<DronePhysics>();
        fc = GetComponent<FlightController>();
    }

    void OnCollisionEnter(Collision c)
    {
        float speed = c.relativeVelocity.magnitude;
        if (Time.time - respawnedAt < 0.5f) return; // settling onto the pad after a respawn is not an impact
        OnImpact?.Invoke(speed);
        if (Crashed || suppressCrashes) return;
        if (speed > crashSpeed)
            Crash($"CRASH  ({speed:0.0} m/s)");
        else if (propDamage && speed > propStrikeSpeed && !drone.Damaged && fc.Armed)
        {
            // A clipped gate or a hard landing: the corner nearest the actual impact takes the hit.
            Vector3 impact = c.contactCount > 0 ? c.GetContact(0).point : transform.position;
            int motor = NearestMotor(impact);
            drone.DamageMotor(motor, UnityEngine.Random.Range(0.25f, 0.45f));
            Fx.Burst(impact, new Color(1f, 0.6f, 0.2f), 25, 4f, 0.03f, 0.6f);
            ShowMessage($"PROP STRIKE ({speed:0.0} m/s) - land and press R");
        }
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

    int NearestMotor(Vector3 worldPoint)
    {
        int best = 0;
        float bestDist = float.MaxValue;
        var local = drone.MotorLocalPositions;
        for (int i = 0; i < local.Length; i++)
        {
            float d = Vector3.SqrMagnitude(transform.TransformPoint(local[i]) - worldPoint);
            if (d < bestDist) { bestDist = d; best = i; }
        }
        return best;
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
        respawnedAt = Time.time;
        ShowMessage("Reset - press arm to fly");
    }

    public void ShowMessage(string text)
    {
        Message = text;
        MessageTime = Time.time;
    }
}
