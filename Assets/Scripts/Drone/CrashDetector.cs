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
    [Tooltip("Impact thresholds are multiplied by this on soil and grass - the terrain absorbs far more than asphalt")]
    [Range(1f, 3f)] public float softGroundFactor = 1.7f;
    [Tooltip("Multiplier for branches and leaves, which slow an impact rather than stopping it")]
    [Range(1f, 3f)] public float foliageFactor = 1.15f;
    [Tooltip("Seconds resting upside down before it counts as a crash")]
    public float upsideDownTimeout = 1.5f;
    public bool autoReset = true;
    public float autoResetDelay = 1.5f;
    [Tooltip("Must stay inside the PhysX broadphase world bounds (ProjectSettings/DynamicsManager, 600 m)")]
    public float worldRadius = 470f;
    [Tooltip("Warn this many metres before the boundary instead of just crashing at it")]
    public float boundaryWarning = 45f;

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
    string lastHit;
    float nextBoundaryWarn;

    void Awake()
    {
        drone = GetComponent<DronePhysics>();
        fc = GetComponent<FlightController>();
    }

    void OnCollisionEnter(Collision c)
    {
        float speed = c.relativeVelocity.magnitude;
        if (Time.time - respawnedAt < 0.5f) return;   // settling onto the pad after a respawn is not an impact
        // Remember what was hit so the crash can say so - "you hit a tree" beats a bare CRASH.
        var root = c.collider.transform.root;
        lastHit = root != null && root != transform ? root.name : c.collider.name;
        OnImpact?.Invoke(speed);
        if (Crashed || suppressCrashes) return;

        // What you hit matters as much as how fast. 5 m/s into long grass is a hard landing a real
        // quad walks away from; the same 5 m/s into a concrete wall is over. The thresholds were
        // flat before, so grass was as lethal as asphalt.
        float tough = SurfaceToughness(c.collider);
        if (speed > crashSpeed * tough)
            Crash($"CRASH  ({speed:0.0} m/s)");
        else if (propDamage && speed > propStrikeSpeed * tough && !drone.Damaged && fc.Armed)
        {
            // A clipped gate or a hard landing: the corner nearest the actual impact takes the hit.
            Vector3 impact = c.contactCount > 0 ? c.GetContact(0).point : transform.position;
            int motor = NearestMotor(impact);
            drone.DamageMotor(motor, UnityEngine.Random.Range(0.25f, 0.45f));
            Fx.Burst(impact, new Color(1f, 0.6f, 0.2f), 25, 4f, 0.03f, 0.6f);
            ShowMessage($"PROP STRIKE ({speed:0.0} m/s) - land and press R");
        }
    }

    /// <summary>
    /// Multiplier on both impact thresholds for whatever was struck. 1 = the hard baseline
    /// (asphalt, concrete, steel, cars, crates), which is what the tuned numbers assume.
    /// </summary>
    float SurfaceToughness(Collider hit)
    {
        // Ground: soil and grass absorb an enormous amount. A 5" quad dropped into a meadow at
        // 7 m/s usually needs nothing more than the props straightening.
        if (hit is TerrainCollider) return softGroundFactor;

        // Foliage: thin branches and leaves snap and slow you rather than stopping you dead, but
        // a trunk at speed is still a trunk.
        var root = hit.transform.root;
        if (root != null && (root.name == "Tree" || root.name == "Bush")) return foliageFactor;

        return 1f;
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
        float fromHome = new Vector2(p.x, p.z).magnitude;
        if (p.y < -5f || fromHome > worldRadius) Crash("Out of bounds");
        else if (fromHome > worldRadius - boundaryWarning && Time.time > nextBoundaryWarn)
        {
            nextBoundaryWarn = Time.time + 2f;
            ShowMessage("Turn back - edge of the flying area");
        }
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
        if (why.StartsWith("CRASH") && !string.IsNullOrEmpty(lastHit)) why += "  -  " + lastHit;
        lastHit = null;
        Crashed = true;
        crashedAt = Time.time;
        CrashCount++;
        fc.Disarm();
        ShowMessage(why);
        OnCrashed?.Invoke();
    }

    /// <summary>
    /// Register a crash from outside - hazards that kill a drone without a survivable impact
    /// (ditching in the pond, an RC-link failsafe at height) rather than a collision speed.
    /// </summary>
    public void ForceCrash(string why)
    {
        if (Crashed || suppressCrashes) return;
        Crash(why);
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
