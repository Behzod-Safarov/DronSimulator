using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Runs the training curriculum. Drills are the child components of this object in
/// hierarchy order; consecutive drills with the same <see cref="Lesson.discipline"/>
/// form a discipline. Number keys pick a discipline (starting at its first drill
/// without 3 stars), N moves to the next drill, Backspace retries, 0 = free flight.
/// </summary>
public class LessonManager : MonoBehaviour
{
    public DronePhysics drone;
    public FlightController flightController;
    public CrashDetector crashDetector;
    public PilotInput input;
    [Tooltip("Per-venue weather / time of day")]
    public VenueAtmosphere atmosphere;
    [Tooltip("Line-of-sight camera: moved next to each venue spawn")]
    public Transform pilotCamera;
    Transform defaultSpawn;
    [Tooltip("Start on the first drill instead of free flight")]
    public bool autoStartFirstLesson = true;

    public class Discipline
    {
        public string name;
        public readonly List<int> lessons = new List<int>();
    }

    Lesson[] lessonsCache;
    readonly List<Discipline> disciplines = new List<Discipline>();

    public Lesson[] Lessons
    {
        get
        {
            if (lessonsCache == null || lessonsCache.Length == 0) lessonsCache = GetComponentsInChildren<Lesson>(true);
            return lessonsCache;
        }
    }

    /// <summary>Consecutive drills with the same discipline name, in hierarchy order (rebuilt after a script reload).</summary>
    public List<Discipline> Disciplines
    {
        get
        {
            if (disciplines.Count == 0)
                for (int i = 0; i < Lessons.Length; i++)
                {
                    var d = disciplines.Count > 0 && disciplines[disciplines.Count - 1].name == Lessons[i].discipline ? disciplines[disciplines.Count - 1] : null;
                    if (d == null) { d = new Discipline { name = Lessons[i].discipline }; disciplines.Add(d); }
                    d.lessons.Add(i);
                }
            return disciplines;
        }
    }

    public int CurrentIndex { get; private set; } = -1;
    public Lesson Current => CurrentIndex >= 0 && CurrentIndex < Lessons.Length ? Lessons[CurrentIndex] : null;
    public Discipline CurrentDiscipline => Current == null ? null : Disciplines.Find(d => d.lessons.Contains(CurrentIndex));
    public bool Paused { get; private set; }
    public string Toast { get; private set; } = "";
    public float ToastTime { get; private set; }

    /// <summary>Snapshot of a finished drill, for the results card.</summary>
    public struct Result { public string title, discipline; public int score, stars, bestStars, crashes; public float elapsed; public bool newBest; }
    public Result Last { get; private set; }
    /// <summary>Increments every time a drill result is recorded.</summary>
    public int ResultSerial { get; private set; }
    /// <summary>Set by the front end: the 0 key opens the menu instead of dropping to free flight.</summary>
    public System.Action OnMenuRequested;
    /// <summary>Set by the front end when it owns the pause key.</summary>
    public bool pauseHandledExternally;

    bool recorded;

    void Awake()
    {
        if (crashDetector != null) defaultSpawn = crashDetector.spawnPoint;
        foreach (var l in Lessons) l.End();
        _ = Disciplines;
    }

    void Start()
    {
        if (crashDetector != null) crashDetector.OnCrashed += () => Current?.OnCrash();
        if (autoStartFirstLesson && Lessons.Length > 0) StartLesson(0);
    }

    void Update()
    {
        if (input != null)
        {
            if (input.PausePressed && !pauseHandledExternally) TogglePause();
            if (input.NextLessonPressed) NextDrill();
            if (input.RestartLessonPressed) RestartLesson();
            if (input.ResetPressed && crashDetector != null) crashDetector.Respawn();
            if (input.ScenarioKey == 0) { if (OnMenuRequested != null) OnMenuRequested(); else StartLesson(-1); }
            // Digits jump straight into a discipline only when no front end is present;
            // with the menu the player goes through it (so they get the briefing and the fade).
            else if (!pauseHandledExternally && input.ScenarioKey > 0 && input.ScenarioKey - 1 < Disciplines.Count) StartDiscipline(input.ScenarioKey - 1);
        }

        if (!Paused && Current != null)
        {
            Current.Tick(Time.deltaTime);
            if (Current.Completed && !recorded)
            {
                recorded = true;
                int before = Current.BestStars, beforeScore = Current.BestScore;
                Current.RecordResult();
                Last = new Result { title = Current.title, discipline = Current.discipline, score = Current.Score, stars = Current.Stars, bestStars = Current.BestStars,
                    crashes = Current.Crashes, elapsed = Current.Elapsed, newBest = Current.Stars > before || Current.Score > beforeScore };
                ResultSerial++;
                string stars = new string('★', Current.Stars) + new string('☆', 3 - Current.Stars);
                ShowToast(Last.newBest ? $"{stars}  New best!  Score {Current.Score}" : $"{stars}  Score {Current.Score}");
            }
        }
    }

    /// <summary>Starts a discipline at its first drill that doesn't have 3 stars yet (or its first drill).</summary>
    public void StartDiscipline(int d)
    {
        if (d < 0 || d >= Disciplines.Count) return;
        var list = Disciplines[d].lessons;
        int pick = list[0];
        foreach (var i in list) if (Lessons[i].BestStars < 3) { pick = i; break; }
        StartLesson(pick);
    }

    /// <summary>Next drill inside the current discipline; at the end, back to free flight.</summary>
    public void NextDrill()
    {
        var d = CurrentDiscipline;
        if (d == null) { StartLesson(Lessons.Length > 0 ? 0 : -1); return; }
        int pos = d.lessons.IndexOf(CurrentIndex);
        if (pos + 1 < d.lessons.Count) StartLesson(d.lessons[pos + 1]);
        else { ShowToast($"{d.name} finished"); StartLesson(-1); }
    }

    public void StartLesson(int index)
    {
        Current?.End();
        CurrentIndex = Mathf.Clamp(index, -1, Lessons.Length - 1);
        recorded = false;
        if (crashDetector != null)
        {
            var sp = Current != null && Current.spawn != null ? Current.spawn : defaultSpawn;
            crashDetector.spawnPoint = sp;
            if (pilotCamera != null && sp != null) pilotCamera.position = sp.position + sp.rotation * new Vector3(3f, 1.7f, -8f);
            crashDetector.Respawn();
        }
        if (atmosphere != null) atmosphere.Apply(Current != null ? Current.venue : "Pit field");
        if (Current != null && flightController != null) flightController.mode = Current.recommendedMode;
        Current?.Begin(drone, flightController, crashDetector);
    }

    public void RestartLesson() => StartLesson(CurrentIndex);

    public void TogglePause()
    {
        Paused = !Paused;
        Time.timeScale = Paused ? 0f : 1f;
    }

    public void ShowToast(string text)
    {
        Toast = text;
        ToastTime = Time.time;
    }

    /// <summary>Total stars earned / possible for a discipline.</summary>
    public (int earned, int possible) Progress(Discipline d)
    {
        int e = 0;
        foreach (var i in d.lessons) e += Lessons[i].BestStars;
        return (e, d.lessons.Count * 3);
    }
}
