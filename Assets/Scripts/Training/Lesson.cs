using UnityEngine;

/// <summary>
/// Base class for a scored training drill. LessonManager drives Begin/Tick, forwards
/// crash events and records the result (stars, best score) between sessions.
/// Drills are grouped into disciplines (Basics, Racing, Freestyle, ...) by the
/// <see cref="discipline"/> string, in hierarchy order.
/// </summary>
public abstract class Lesson : MonoBehaviour
{
    public string title = "Lesson";
    [TextArea(2, 5)] public string instructions = "";
    [Tooltip("Training discipline this drill belongs to (drills of one discipline run in order)")]
    public string discipline = "Basics";
    [Header("Student briefing")]
    [Tooltip("One sentence: what the student must achieve")]
    public string goal = "";
    [Tooltip("The steps to follow, in order (3-4 short lines)")]
    public string[] steps = new string[0];
    [Tooltip("What skill this drill builds, and why it matters on a real quad")]
    public string skill = "";
    [Tooltip("The mistake students usually make here")]
    public string tip = "";
    [Tooltip("Controls the student needs for this drill")]
    public string controls = "";
    [Range(1, 5)] [Tooltip("1 = first flight, 5 = advanced")] public int difficulty = 1;

    [Tooltip("Where the drone starts for this drill (null = the pit)")]
    public Transform spawn;
    [Tooltip("Venue name shown in the menus")]
    public string venue = "Pit field";
    [Tooltip("Flight mode suggested for this drill (shown in the OSD)")]
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

    /// <summary>0-3 stars for the current run.</summary>
    public int Stars => !Completed ? 0 : Score >= 90 ? 3 : Score >= 65 ? 2 : Score >= 35 ? 1 : 0;

    public abstract string Status { get; }

    /// <summary>Title / briefing in the current UI language (the English title stays the save key).</summary>
    public string DisplayTitle => Loc.T(title);
    public string DisplayInstructions => Loc.T(instructions);
    public string DisplayGoal => Loc.T(goal);
    public string DisplaySkill => Loc.T(skill);
    public string DisplayTip => Loc.T(tip);
    public string DisplayControls => Loc.T(controls);

    /// <summary>Optional big centre-screen text (countdowns, trick call-outs). Null = nothing.</summary>
    public virtual string Banner => null;

    // ---- persistent progress ----------------------------------------------------
    string Key => "drill." + title.Replace(' ', '_');
    public int BestStars => PlayerPrefs.GetInt(Key + ".stars", 0);
    public int BestScore => PlayerPrefs.GetInt(Key + ".score", 0);
    public int Attempts => PlayerPrefs.GetInt(Key + ".attempts", 0);

    public void RecordResult()
    {
        if (!Completed) return;
        if (Stars > BestStars) PlayerPrefs.SetInt(Key + ".stars", Stars);
        if (Score > BestScore) PlayerPrefs.SetInt(Key + ".score", Score);
        PlayerPrefs.SetInt(Key + ".attempts", Attempts + 1);
        PlayerPrefs.Save();
    }

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
