using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Agent mode: the drone flies the gate loop by itself (take-off, waypoints, return,
/// landing) while you watch from any camera. Touch the sticks to take over; Backspace
/// hands the mission back to the agent.
/// </summary>
public class AutopilotAgentLesson : Lesson
{
    public Gate[] gates;
    public float altitude = 4f;
    public int laps = 1;

    Autopilot ap;

    public override string Status
    {
        get
        {
            if (ap == null) return "";
            if (Completed) return "Mission complete - press Backspace to fly it again";
            return ap.State == Autopilot.Phase.Manual ? $"{ap.Message}   (Backspace = agent flies again)" : $"AGENT: {ap.Message}";
        }
    }

    public override void Begin(DronePhysics drone, FlightController fc, CrashDetector crash)
    {
        base.Begin(drone, fc, crash);
        ap = drone.GetComponent<Autopilot>();
        if (ap == null)
        {
            ap = drone.gameObject.AddComponent<Autopilot>();
            ap.drone = drone;
            ap.fc = fc;
            ap.input = drone.GetComponent<PilotInput>();
        }
        var route = new List<Vector3>();
        for (int l = 0; l < laps; l++)
            foreach (var g in gates)
                route.Add(new Vector3(g.transform.position.x, altitude, g.transform.position.z));
        foreach (var g in gates) g.SetState(Gate.State.Idle);
        ap.waypointRadius = 2.5f;
        ap.StartMission(route, altitude);
    }

    public override void Tick(float dt)
    {
        base.Tick(dt);
        if (Completed || ap == null) return;
        for (int i = 0; i < gates.Length; i++)
            gates[i].SetState(ap.State == Autopilot.Phase.Route && ap.WaypointIndex % gates.Length == i ? Gate.State.Next : Gate.State.Idle);
        if (ap.State == Autopilot.Phase.Done) Complete();
    }

    public override void OnCrash()
    {
        base.OnCrash();
        ap?.Abort("Crashed - agent stopped");
    }

    public override void End()
    {
        ap?.Stop();
        if (gates != null) foreach (var g in gates) g.SetState(Gate.State.Idle);
    }
}
