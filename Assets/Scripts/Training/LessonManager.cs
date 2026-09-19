using UnityEngine;

/// <summary>
/// Runs the lesson sequence. Lessons are the child components of this object,
/// in hierarchy order. Index -1 is free flight.
/// </summary>
public class LessonManager : MonoBehaviour
{
    public DronePhysics drone;
    public FlightController flightController;
    public CrashDetector crashDetector;
    public PilotInput input;
    [Tooltip("Start on the first lesson instead of free flight")]
    public bool autoStartFirstLesson = true;

    public Lesson[] Lessons { get; private set; }
    public int CurrentIndex { get; private set; } = -1;
    public Lesson Current => CurrentIndex >= 0 && CurrentIndex < Lessons.Length ? Lessons[CurrentIndex] : null;
    public bool Paused { get; private set; }

    void Awake()
    {
        Lessons = GetComponentsInChildren<Lesson>(true);
        foreach (var l in Lessons) l.End();
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
            if (input.PausePressed) TogglePause();
            if (input.NextLessonPressed) StartLesson(CurrentIndex + 1 >= Lessons.Length ? -1 : CurrentIndex + 1);
            if (input.RestartLessonPressed) RestartLesson();
            if (input.ResetPressed && crashDetector != null) crashDetector.Respawn();
            if (input.ScenarioKey >= 0 && input.ScenarioKey - 1 < Lessons.Length) StartLesson(input.ScenarioKey - 1);
        }

        if (!Paused) Current?.Tick(Time.deltaTime);
    }

    public void StartLesson(int index)
    {
        Current?.End();
        CurrentIndex = Mathf.Clamp(index, -1, Lessons.Length - 1);
        if (crashDetector != null) crashDetector.Respawn();
        Current?.Begin(drone, flightController, crashDetector);
    }

    public void RestartLesson() => StartLesson(CurrentIndex);

    public void TogglePause()
    {
        Paused = !Paused;
        Time.timeScale = Paused ? 0f : 1f;
    }
}
