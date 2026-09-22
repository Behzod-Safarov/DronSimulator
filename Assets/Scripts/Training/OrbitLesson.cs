using UnityEngine;

/// <summary>
/// Basics drill: fly circles around a pylon, staying inside a distance band, for N laps.
/// Progress only accumulates while inside the band and above the minimum height, so
/// wandering off resets nothing but stops the lap counter.
/// </summary>
public class OrbitLesson : Lesson
{
    public Transform pylon;
    public float minRadius = 3f;
    public float maxRadius = 8f;
    public float minHeight = 1f;
    public float laps = 2f;

    float sweptDeg;
    float lastAngle;
    bool hadAngle;
    bool inBand;

    public float LapsDone => Mathf.Abs(sweptDeg) / 360f;

    public override string Status => Completed
        ? $"Orbit complete - {LapsDone:0.0} laps"
        : inBand ? $"Laps {LapsDone:0.0} / {laps:0}   keep {minRadius:0}-{maxRadius:0} m from the pylon" : $"Laps {LapsDone:0.0} / {laps:0}   get {minRadius:0}-{maxRadius:0} m from the pylon, above {minHeight:0} m";

    public override void Begin(DronePhysics drone, FlightController fc, CrashDetector crash)
    {
        base.Begin(drone, fc, crash);
        sweptDeg = 0f;
        hadAngle = false;
        inBand = false;
        if (pylon != null) pylon.gameObject.SetActive(true);
    }

    public override void Tick(float dt)
    {
        base.Tick(dt);
        if (Completed || pylon == null) return;
        Vector3 p = drone.transform.position;
        Vector2 d = new Vector2(p.x - pylon.position.x, p.z - pylon.position.z);
        float r = d.magnitude;
        inBand = r >= minRadius && r <= maxRadius && p.y - pylon.position.y > minHeight;
        float angle = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
        if (inBand && hadAngle) sweptDeg += Mathf.DeltaAngle(lastAngle, angle);
        lastAngle = angle;
        hadAngle = true;
        if (LapsDone >= laps) { Complete(); Fx.PlayDing(p); }
    }

    public override void OnCrash()
    {
        base.OnCrash();
        hadAngle = false;
    }

    public override void End()
    {
        if (pylon != null) pylon.gameObject.SetActive(false);
    }
}
