using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Fly through every gate in order. The next gate glows green. With laps > 1 it is a
/// race: lap times are recorded and the best lap is remembered between sessions.
/// </summary>
public class GateCourseLesson : Lesson
{
    public Gate[] gates;
    public int laps = 1;
    [Tooltip("PlayerPrefs key for the best lap (leave empty to not record)")]
    public string bestLapKey = "";

    int next;
    int lap;
    float lapStart;
    readonly List<float> lapTimes = new List<float>();

    public float BestLap => string.IsNullOrEmpty(bestLapKey) ? 0f : PlayerPrefs.GetFloat(bestLapKey, 0f);
    public float CurrentLapTime => Completed ? 0f : Elapsed - lapStart;

    public override string Status
    {
        get
        {
            if (Completed)
            {
                string s = laps > 1 ? $"Race finished in {Elapsed:0.0} s" : "Course complete!";
                if (lapTimes.Count > 0) s += $"   best lap {Min(lapTimes):0.00} s";
                return s;
            }
            string status = $"Gate {next + 1} / {gates.Length}";
            if (laps > 1)
            {
                status += $"   Lap {lap + 1} / {laps}   {CurrentLapTime:0.0} s";
                if (lapTimes.Count > 0) status += $"   last {lapTimes[lapTimes.Count - 1]:0.00}";
                if (BestLap > 0f) status += $"   record {BestLap:0.00}";
            }
            return status;
        }
    }

    public override void Begin(DronePhysics drone, FlightController fc, CrashDetector crash)
    {
        base.Begin(drone, fc, crash);
        next = 0;
        lap = 0;
        lapStart = 0f;
        lapTimes.Clear();
        foreach (var g in gates)
        {
            g.Passed -= OnGatePassed;
            g.Passed += OnGatePassed;
        }
        Paint();
    }

    void OnGatePassed(Gate g)
    {
        if (Completed || gates.Length == 0 || g != gates[next]) return;
        Fx.PlayDing(g.transform.position);

        next++;
        if (next >= gates.Length)
        {
            float lapTime = Elapsed - lapStart;
            lapTimes.Add(lapTime);
            lapStart = Elapsed;
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
