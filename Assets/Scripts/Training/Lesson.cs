using UnityEngine;

/// <summary>
/// Base class for a scored training exercise. LessonManager drives Begin/Tick
/// and forwards crash events.
/// </summary>
public abstract class Lesson : MonoBehaviour
{
    public string title = "Lesson";
    [TextArea(2, 5)] public string instructions = "";
    [Tooltip("Flight mode suggested for this lesson (shown in the OSD)")]
    public FlightMode recommendedMode = FlightMode.Angle;
    [Tooltip("Time in seconds under which no time penalty applies")]
    public float parTime = 30f;
    public float pointsPerSecondOverPar = 0.5f;
    public int pointsPerCrash = 15;

    public bool Completed { get; protected set; }
    public float Elapsed { get; protected set; }
    public int Crashes { get; protected set; }

    protected DronePhysics drone;
    protected FlightController fc;
    protected CrashDetector crash;

    public virtual int Score => Completed
        ? Mathf.Clamp(Mathf.RoundToInt(100 - Crashes * pointsPerCrash - Mathf.Max(0f, Elapsed - parTime) * pointsPerSecondOverPar), 0, 100)
        : 0;

    public abstract string Status { get; }

    public virtual void Begin(DronePhysics drone, FlightController fc, CrashDetector crash)
    {
        this.drone = drone;
        this.fc = fc;
        this.crash = crash;
        Completed = false;
        Elapsed = 0f;
        Crashes = 0;
    }

    public virtual void Tick(float dt)
    {
        if (!Completed) Elapsed += dt;
    }

    public virtual void OnCrash() => Crashes++;

    public virtual void End() { }

    protected void Complete() => Completed = true;
}
