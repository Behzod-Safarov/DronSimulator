using UnityEngine;

/// <summary>Game: pop every balloon on the field by flying through it, as fast as you can.</summary>
public class BalloonPopLesson : Lesson
{
    public Balloon[] balloons;
    int popped;

    public override string Status => Completed
        ? $"All {balloons.Length} balloons popped!"
        : $"Balloons {popped} / {balloons.Length}   {(balloons.Length - popped)} to go";

    public override void Begin(DronePhysics drone, FlightController fc, CrashDetector crash)
    {
        base.Begin(drone, fc, crash);
        popped = 0;
        foreach (var b in balloons)
        {
            b.Popped -= OnPopped;
            b.Popped += OnPopped;
            b.Restore();
        }
    }

    void OnPopped(Balloon b)
    {
        if (Completed) return;
        popped++;
        if (popped >= balloons.Length)
        {
            Complete();
            Fx.PlayDing(drone.transform.position);
        }
    }

    public override void End()
    {
        foreach (var b in balloons)
        {
            b.Popped -= OnPopped;
            b.gameObject.SetActive(false);
        }
    }
}
