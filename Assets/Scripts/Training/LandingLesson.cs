using UnityEngine;

/// <summary>Lesson 3: climb above a target altitude, then land gently on the pad and stay still.</summary>
public class LandingLesson : Lesson
{
    public Transform pad;
    public float padRadius = 1f;
    public float climbAltitude = 3f;
    [Tooltip("Touch-down speed above this counts as a hard landing (retry)")]
    public float hardLandingSpeed = 2f;
    public float settleSeconds = 2f;

    enum Phase { Climb, Land, Settle }
    Phase phase;
    float settledFor;
    float lastTouchdownSpeed;

    public override string Status => Completed ? "Landed!" : phase switch
    {
        Phase.Climb => $"Climb above {climbAltitude:0} m  (alt {drone.transform.position.y:0.0} m)",
        Phase.Land => "Now land gently on the orange pad",
        _ => $"Hold still... {settledFor:0.0} / {settleSeconds:0} s",
    };

    public override void Begin(DronePhysics drone, FlightController fc, CrashDetector crash)
    {
        base.Begin(drone, fc, crash);
        phase = Phase.Climb;
        settledFor = 0f;
        crash.OnImpact -= OnImpact;
        crash.OnImpact += OnImpact;
    }

    void OnImpact(float speed)
    {
        if (Completed || phase == Phase.Climb) return;
        lastTouchdownSpeed = speed;
        if (speed > hardLandingSpeed)
        {
            Crashes++;
            crash.ShowMessage($"Hard landing ({speed:0.0} m/s) - go around and try again");
            phase = Phase.Climb;
        }
    }

    public override void Tick(float dt)
    {
        base.Tick(dt);
        if (Completed) return;

        Vector3 p = drone.transform.position;
        switch (phase)
        {
            case Phase.Climb:
                if (p.y >= climbAltitude) phase = Phase.Land;
                break;

            case Phase.Land:
                if (drone.IsGrounded && OnPad(p)) { phase = Phase.Settle; settledFor = 0f; }
                break;

            case Phase.Settle:
                bool still = drone.IsGrounded && OnPad(p) && drone.Body.GetVelocity().magnitude < 0.3f && drone.transform.up.y > 0.9f;
                if (!still) { phase = Phase.Land; break; }
                settledFor += dt;
                if (settledFor >= settleSeconds) Complete();
                break;
        }
    }

    bool OnPad(Vector3 p)
    {
        if (pad == null) return true;
        Vector2 d = new Vector2(p.x - pad.position.x, p.z - pad.position.z);
        return d.magnitude <= padRadius;
    }

    public override void OnCrash()
    {
        base.OnCrash();
        phase = Phase.Climb;
    }

    public override void End()
    {
        if (crash != null) crash.OnImpact -= OnImpact;
    }
}
