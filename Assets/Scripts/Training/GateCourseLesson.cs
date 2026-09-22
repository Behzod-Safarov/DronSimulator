using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Fly through every gate in order. The next gate glows green. With laps > 1 it is a
/// race: an optional 3-2-1-GO countdown (passing a gate early is a false start), lap
/// times are recorded, the best lap is remembered between sessions, and a target lap
/// time turns it into a time trial that only scores well when you beat the target.
/// </summary>
public class GateCourseLesson : Lesson
{
    public Gate[] gates;
    public int laps = 1;
    [Tooltip("PlayerPrefs key for the best lap (leave empty to not record)")]
    public string bestLapKey = "";
    [Tooltip("Seconds of 3-2-1-GO before the timer starts (0 = none)")]
    public float countdownSeconds = 0f;
    [Tooltip("Time trial: best lap must be under this to score full points (0 = no target)")]
    public float targetLapTime = 0f;

    int next;
    int lap;
    float lapStart;
    float countdown;
    float goElapsed;
    bool falseStart;
    readonly List<float> lapTimes = new List<float>();
    string banner;
    float bannerUntil;

    public float BestLap => string.IsNullOrEmpty(bestLapKey) ? 0f : PlayerPrefs.GetFloat(bestLapKey, 0f);
    public float RaceTime => Mathf.Max(0f, Elapsed - goElapsed);
    public float CurrentLapTime => Completed ? 0f : RaceTime - lapStart;
    public bool Racing => countdown <= 0f;
    float BestLapThisRun => lapTimes.Count == 0 ? 0f : Min(lapTimes);

    public override int Score
    {
        get
        {
            if (!Completed) return 0;
            int s = 100 - Crashes * pointsPerCrash;
            if (targetLapTime > 0f) s -= Mathf.RoundToInt(Mathf.Max(0f, BestLapThisRun - targetLapTime) * 8f);
            else s -= Mathf.RoundToInt(Mathf.Max(0f, RaceTime - parTime) * pointsPerSecondOverPar);
            return Mathf.Clamp(s, 0, 100);
        }
    }

    public override string Banner => Time.time < bannerUntil ? banner : null;

    public override string Status
    {
        get
        {
            if (Completed)
            {
                string s = laps > 1 ? $"Race finished in {RaceTime:0.0} s" : "Course complete!";
                if (lapTimes.Count > 0) s += $"   best lap {BestLapThisRun:0.00} s";
                if (targetLapTime > 0f) s += BestLapThisRun <= targetLapTime ? "   TARGET BEATEN" : $"   target was {targetLapTime:0.0} s";
                return s;
            }
            if (!Racing) return falseStart ? "FALSE START - wait for GO" : $"Get ready... {Mathf.CeilToInt(countdown)}";
            string status = $"Gate {next + 1} / {gates.Length}";
            if (laps > 1)
            {
                status += $"   Lap {lap + 1} / {laps}   {CurrentLapTime:0.0} s";
                if (lapTimes.Count > 0) status += $"   last {lapTimes[lapTimes.Count - 1]:0.00}";
                if (BestLap > 0f) status += $"   record {BestLap:0.00}";
            }
            if (targetLapTime > 0f) status += $"   target {targetLapTime:0.0} s";
            return status;
        }
    }

    public override void Begin(DronePhysics drone, FlightController fc, CrashDetector crash)
    {
        base.Begin(drone, fc, crash);
        next = 0;
        lap = 0;
        lapStart = 0f;
        countdown = countdownSeconds;
        goElapsed = 0f;
        falseStart = false;
        lapTimes.Clear();
        if (countdownSeconds > 0f) ShowBanner(Mathf.CeilToInt(countdownSeconds).ToString(), 0.9f);
        foreach (var g in gates)
        {
            g.Passed -= OnGatePassed;
            g.Passed += OnGatePassed;
        }
        Paint();
    }

    public override void Tick(float dt)
    {
        base.Tick(dt);
        if (Completed) return;
        if (countdown > 0f)
        {
            int before = Mathf.CeilToInt(countdown);
            countdown -= dt;
            int after = Mathf.CeilToInt(countdown);
            if (countdown <= 0f) { goElapsed = Elapsed; ShowBanner("GO!", 1.2f); Fx.PlayDing(drone.transform.position); }
            else if (after != before) ShowBanner(after.ToString(), 0.9f);
        }
    }

    void ShowBanner(string text, float seconds)
    {
        banner = text;
        bannerUntil = Time.time + seconds;
    }

    void OnGatePassed(Gate g)
    {
        if (Completed || gates.Length == 0) return;
        if (!Racing)
        {
            // Jumped the start: restart the countdown.
            falseStart = true;
            countdown = countdownSeconds;
            ShowBanner("FALSE START", 1.5f);
            return;
        }
        if (g != gates[next]) return;
        Fx.PlayDing(g.transform.position);

        next++;
        if (next >= gates.Length)
        {
            float lapTime = RaceTime - lapStart;
            lapTimes.Add(lapTime);
            lapStart = RaceTime;
            ShowBanner($"LAP {lapTime:0.00}", 1.5f);
            if (!string.IsNullOrEmpty(bestLapKey) && (BestLap <= 0f || lapTime < BestLap))
            {
                PlayerPrefs.SetFloat(bestLapKey, lapTime);
                PlayerPrefs.Save();
            }
            lap++;
            if (lap >= laps)
            {
                Complete();
                Paint();
                return;
            }
            next = 0;
        }
        Paint();
    }

    static float Min(List<float> v)
    {
        float m = float.MaxValue;
        foreach (var x in v) if (x < m) m = x;
        return m;
    }

    void Paint()
    {
        for (int i = 0; i < gates.Length; i++)
        {
            Gate.State s;
            if (Completed) s = Gate.State.Passed;
            else if (i == next) s = Gate.State.Next;
            else if (i < next) s = Gate.State.Passed;
            else s = Gate.State.Idle;
            gates[i].SetState(s);
        }
    }

    public override void End()
    {
        foreach (var g in gates)
        {
            g.Passed -= OnGatePassed;
            g.SetState(Gate.State.Idle);
        }
    }
}
