using UnityEngine;

/// <summary>Lesson 1: take off and hold the drone inside a floating box for N seconds continuously.</summary>
public class HoverBoxLesson : Lesson
{
    [Tooltip("Cube whose position/scale define the target volume")]
    public Transform zone;
    public float requiredSeconds = 10f;
    public Color idleColor = new Color(1f, 0.85f, 0.2f, 0.25f);
    public Color insideColor = new Color(0.2f, 1f, 0.3f, 0.35f);

    float insideFor;
    bool inside;
    Renderer zoneRenderer;
    MaterialPropertyBlock block;

    public override string Status => Completed
        ? "Done!"
        : inside ? $"Hold... {insideFor:0.0} / {requiredSeconds:0} s" : "Fly into the box";

    public override void Begin(DronePhysics drone, FlightController fc, CrashDetector crash)
    {
        base.Begin(drone, fc, crash);
        insideFor = 0f;
        inside = false;
        if (zone != null)
        {
            zoneRenderer = zone.GetComponent<Renderer>();
            block ??= new MaterialPropertyBlock();
            zone.gameObject.SetActive(true);
        }
        Paint();
    }

    public override void Tick(float dt)
    {
        base.Tick(dt);
        if (Completed || zone == null) return;

        Vector3 local = zone.InverseTransformPoint(drone.transform.position);
        bool nowInside = Mathf.Abs(local.x) <= 0.5f && Mathf.Abs(local.y) <= 0.5f && Mathf.Abs(local.z) <= 0.5f;

        if (nowInside) insideFor += dt;
        else insideFor = 0f;

        if (nowInside != inside)
        {
            inside = nowInside;
            Paint();
        }

        if (insideFor >= requiredSeconds) Complete();
    }

    public override void OnCrash()
    {
        base.OnCrash();
        insideFor = 0f;
    }

    public override void End()
    {
        if (zone != null) zone.gameObject.SetActive(false);
    }

    void Paint()
    {
        if (zoneRenderer == null) return;
        zoneRenderer.GetPropertyBlock(block);
        block.SetColor("_Color", inside ? insideColor : idleColor);
        zoneRenderer.SetPropertyBlock(block);
    }
}
