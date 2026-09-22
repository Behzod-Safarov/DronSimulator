using UnityEngine;

/// <summary>Freestyle drill: perform a number of rolls / flips / power loops without crashing.</summary>
public class TrickLesson : Lesson
{
    public int rollsRequired;
    public int flipsRequired;
    public int loopsRequired;
    [Tooltip("Minimum altitude to attempt tricks (safety floor shown in the status)")]
    public float minAltitude = 6f;

    TrickDetector detector;
    string banner;
    float bannerUntil;
    FlightMode previousMode;

    public override string Banner => Time.time < bannerUntil ? banner : null;

    public override string Status
    {
        get
        {
            if (detector == null) return "";
            if (Completed) return "All tricks landed!";
            var s = "";
            if (rollsRequired > 0) s += $"Rolls {Mathf.Min(detector.Rolls, rollsRequired)}/{rollsRequired}   ";
            if (flipsRequired > 0) s += $"Flips {Mathf.Min(detector.Flips, flipsRequired)}/{flipsRequired}   ";
            if (loopsRequired > 0) s += $"Power loops {Mathf.Min(detector.PowerLoops, loopsRequired)}/{loopsRequired}   ";
            float alt = drone.transform.position.y;
            s += alt < minAltitude ? $"climb above {minAltitude:0} m first" : "go!";
            return s;
        }
    }

    public override void Begin(DronePhysics drone, FlightController fc, CrashDetector crash)
    {
        base.Begin(drone, fc, crash);
        detector = drone.GetComponent<TrickDetector>() ?? drone.gameObject.AddComponent<TrickDetector>();
        detector.drone = drone;
        detector.fc = fc;
        detector.minAltitude = minAltitude;
        detector.ResetCounts();
        detector.TrickDone -= OnTrick;
        detector.TrickDone += OnTrick;
        previousMode = fc.mode;
        fc.mode = recommendedMode;
    }

    void OnTrick(string name)
    {
        banner = name + "!";
        bannerUntil = Time.time + 1.2f;
        Fx.PlayDing(drone.transform.position);
    }

    public override void Tick(float dt)
    {
        base.Tick(dt);
        if (Completed || detector == null) return;
        if (detector.Rolls >= rollsRequired && detector.Flips >= flipsRequired && detector.PowerLoops >= loopsRequired)
            Complete();
    }

    public override void End()
    {
        if (detector != null) detector.TrickDone -= OnTrick;
        // recommendedMode is documented as a suggestion everywhere else (Lesson.recommendedMode);
        // this drill was the one place that silently forced it and never gave it back.
        if (fc != null) fc.mode = previousMode;
    }
}
