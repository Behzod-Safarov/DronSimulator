using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Game: carry water balloons (they add real mass) and release them (F) onto the
/// bullseye targets painted on the field. The balloon keeps the drone's velocity and
/// falls with drag, so the drop has to be led. Points by ring: 10 / 5 / 2.
/// </summary>
public class WaterBalloonDropLesson : Lesson
{
    public Transform[] targets;
    public float targetRadius = 2f;
    public int balloonsPerRun = 6;
    public float balloonMass = 0.12f;
    [Tooltip("Where balloons hang, in drone body space")]
    public Vector3 rackOffset = new Vector3(0f, -0.06f, 0f);

    public int Remaining { get; private set; }
    public int Points { get; private set; }
    public int Hits { get; private set; }

    PilotInput input;
    PayloadRack rack;
    readonly List<string> log = new List<string>();
    string last = "";

    public override string Status => Completed
        ? $"Done: {Points} points, {Hits} hits"
        : $"Balloons {Remaining}   Points {Points}   {last}";

    public override void Begin(DronePhysics drone, FlightController fc, CrashDetector crash)
    {
        base.Begin(drone, fc, crash);
        input = drone.GetComponent<PilotInput>();
        rack = drone.GetComponent<PayloadRack>() ?? drone.gameObject.AddComponent<PayloadRack>();
        Remaining = balloonsPerRun;
        Points = 0;
        Hits = 0;
        last = "Press F over a target to drop";
        drone.payloadMass = Remaining * balloonMass;
        rack.Show(Remaining, rackOffset);
        foreach (var t in targets) t.gameObject.SetActive(true);
    }

    public override void Tick(float dt)
    {
        base.Tick(dt);
        if (Completed) return;
        if (input != null && input.DropPressed && Remaining > 0 && fc.Armed)
        {
            Remaining--;
            drone.payloadMass = Remaining * balloonMass;
            rack.Show(Remaining, rackOffset);
            Vector3 pos = drone.transform.TransformPoint(rackOffset + new Vector3(0f, -0.05f, 0f));
            var b = WaterBalloon.Spawn(pos, drone.Body.GetVelocity(), balloonMass);
            b.Splashed += OnSplash;
        }
    }

    void OnSplash(WaterBalloon b, Vector3 p)
    {
        float best = float.MaxValue;
        foreach (var t in targets)
        {
            float d = new Vector2(p.x - t.position.x, p.z - t.position.z).magnitude;
            if (d < best) best = d;
        }
        int pts = best < targetRadius * 0.25f ? 10 : best < targetRadius * 0.6f ? 5 : best < targetRadius ? 2 : 0;
        Points += pts;
        if (pts > 0) { Hits++; Fx.PlayDing(p); }
        last = pts > 0 ? $"HIT {pts} pts ({best:0.0} m)" : $"miss ({best:0.0} m)";
        if (Remaining == 0 && !Completed) Complete();
    }

    public override void End()
    {
        if (drone != null) drone.payloadMass = 0f;
        if (rack != null) rack.Show(0, rackOffset);
        foreach (var t in targets) t.gameObject.SetActive(false);
    }
}

/// <summary>Draws the remaining balloons hanging under the drone.</summary>
public class PayloadRack : MonoBehaviour
{
    readonly List<GameObject> items = new List<GameObject>();

    public void Show(int count, Vector3 offset)
    {
        while (items.Count < count)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(go.GetComponent<Collider>());
            go.name = "Rack balloon";
            go.transform.SetParent(transform, false);
            go.transform.localScale = Vector3.one * 0.09f;
            var m = new Material(Shader.Find("Standard")) { color = new Color(0.35f, 0.6f, 1f) };
            m.SetFloat("_Glossiness", 0.85f);
            go.GetComponent<Renderer>().material = m;
            items.Add(go);
        }
        for (int i = 0; i < items.Count; i++)
        {
            bool on = i < count;
            items[i].SetActive(on);
            if (on)
            {
                float x = (i % 3 - 1) * 0.05f, z = (i / 3 - 0.5f) * 0.05f;
                items[i].transform.localPosition = offset + new Vector3(x, 0f, z);
            }
        }
    }
}
