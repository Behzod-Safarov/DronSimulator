using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Autonomous agent: flies a waypoint mission (take-off, route, return, land) by
/// generating stick inputs for the normal flight controller in Angle mode, the way
/// a GPS autopilot sits on top of a rate controller. Any real stick movement hands
/// control straight back to the pilot.
/// </summary>
public class Autopilot : MonoBehaviour
{
    public enum Phase { Idle, Takeoff, Route, Return, Land, Done, Manual }

    public DronePhysics drone;
    public FlightController fc;
    public PilotInput input;

    [Header("Limits")]
    public float cruiseSpeed = 7f;
    public float maxTiltDeg = 28f;
    public float maxAccel = 6f;
    public float waypointRadius = 2.5f;
    public float climbRate = 2.5f;
    public float descentRate = 1.2f;

    [Header("Gains")]
    public float positionP = 0.9f;
    public float velocityP = 1.4f;
    public float altitudeP = 1.2f;
    public float vertVelP = 0.10f;
    public float yawP = 1f / 80f;

    public Phase State { get; private set; } = Phase.Idle;
    public int WaypointIndex { get; private set; }
    public string Message { get; private set; } = "";
    public bool Engaged => State != Phase.Idle && State != Phase.Done && State != Phase.Manual;

    readonly List<Vector3> route = new List<Vector3>();
    Vector3 home;
    float cruiseAlt;
    float takeoverThrottleRef;
    float altIntegral;
    float landTimer;
    Vector3 windIntegral;   // steady-state lean into the wind
    float nearTimer;

    public void StartMission(IEnumerable<Vector3> waypoints, float altitude)
    {
        route.Clear();
        route.AddRange(waypoints);
        cruiseAlt = altitude;
        home = drone.transform.position;
        WaypointIndex = 0;
        altIntegral = 0f;
        landTimer = 0f;
        windIntegral = Vector3.zero;
        nearTimer = 0f;
        fc.mode = FlightMode.Angle;
        takeoverThrottleRef = input.PhysicalRaw.z;
        PilotInput.OverrideActive = true;
        PilotInput.OverrideRoll = PilotInput.OverridePitch = PilotInput.OverrideYaw = 0f;
        PilotInput.OverrideThrottle = 0f;
        if (!fc.Armed) PilotInput.OverrideArmPulse = true;
        State = Phase.Takeoff;
        Message = "Taking off";
    }

    public void Abort(string why)
    {
        if (!Engaged) return;
        PilotInput.OverrideActive = false;
        State = Phase.Manual;
        Message = why;
    }

    public void Stop()
    {
        if (Engaged) PilotInput.OverrideActive = false;
        State = Phase.Idle;
        Message = "";
    }

    void Update()
    {
        if (!Engaged || input == null) return;
        var p = input.PhysicalRaw;
        bool sticks = Mathf.Abs(p.x) > 0.35f || Mathf.Abs(p.y) > 0.35f || Mathf.Abs(p.w) > 0.35f || Mathf.Abs(p.z - takeoverThrottleRef) > 0.2f;
        if (sticks) Abort("Manual - pilot took over");
    }

    void FixedUpdate()
    {
        if (!Engaged) return;
        if (!fc.Armed)
        {
            // Wait for the arm request to go through (arming needs throttle at zero).
            PilotInput.OverrideThrottle = 0f;
            PilotInput.OverrideRoll = PilotInput.OverridePitch = PilotInput.OverrideYaw = 0f;
            return;
        }
        var rb = drone.Body;
        Vector3 pos = drone.transform.position;
        Vector3 vel = rb.GetVelocity();
        float dt = Time.fixedDeltaTime;

        // --- Where are we going? ------------------------------------------
        Vector3 target;
        float targetAlt;
        switch (State)
        {
            case Phase.Takeoff:
                target = new Vector3(home.x, 0f, home.z);
                targetAlt = home.y + cruiseAlt;
                if (pos.y > home.y + cruiseAlt - 0.6f)
                {
                    State = route.Count > 0 ? Phase.Route : Phase.Return;
                    Message = State == Phase.Route ? $"Waypoint 1 / {route.Count}" : "Returning home";
                }
                break;

            case Phase.Route:
            {
                var wp = route[WaypointIndex];
                target = wp;
                targetAlt = wp.y;
                float dWp = new Vector2(pos.x - wp.x, pos.z - wp.z).magnitude;
                // Advance when inside the radius, or when parked close to it (wind can hold the last metre)
                nearTimer = dWp < waypointRadius * 2.5f && vel.magnitude < 0.6f ? nearTimer + dt : 0f;
                if (dWp < waypointRadius || nearTimer > 2f)
                {
                    nearTimer = 0f;
                    WaypointIndex++;
                    if (WaypointIndex >= route.Count) { State = Phase.Return; Message = "Returning home"; }
                    else Message = $"Waypoint {WaypointIndex + 1} / {route.Count}";
                }
                break;
            }

            case Phase.Return:
                target = home;
                targetAlt = home.y + cruiseAlt;
                if (new Vector2(pos.x - home.x, pos.z - home.z).magnitude < 1.0f && vel.magnitude < 1.5f)
                {
                    State = Phase.Land;
                    Message = "Landing";
                }
                break;

            default: // Land
                target = home;
                targetAlt = home.y - 1f; // keep asking to go down
                break;
        }

        // --- Horizontal: position -> velocity -> acceleration -> tilt ----------
        Vector3 toT = new Vector3(target.x - pos.x, 0f, target.z - pos.z);
        float dist = toT.magnitude;
        float speed = Mathf.Min(cruiseSpeed, dist * positionP);
        if (State == Phase.Land) speed = Mathf.Min(speed, 0.8f);
        Vector3 vDes = dist > 0.01f ? toT / dist * speed : Vector3.zero;
        Vector3 velH = new Vector3(vel.x, 0f, vel.z);
        Vector3 vErr = vDes - velH;
        windIntegral = Vector3.ClampMagnitude(windIntegral + vErr * dt * 0.25f, 2.5f);
        Vector3 accel = Vector3.ClampMagnitude(vErr * velocityP + windIntegral, maxAccel);

        // required tilt: lean into the acceleration (plus a little into the wind by construction, since we use ground velocity)
        float g = Physics.gravity.magnitude;
        Vector3 fwd = Vector3.ProjectOnPlane(drone.transform.forward, Vector3.up).normalized;
        Vector3 right = Vector3.Cross(Vector3.up, fwd);
        float aF = Vector3.Dot(accel, fwd), aR = Vector3.Dot(accel, right);
        float tiltF = Mathf.Clamp(Mathf.Atan2(aF, g) * Mathf.Rad2Deg, -maxTiltDeg, maxTiltDeg);
        float tiltR = Mathf.Clamp(Mathf.Atan2(aR, g) * Mathf.Rad2Deg, -maxTiltDeg, maxTiltDeg);
        PilotInput.OverridePitch = tiltF / fc.angleLimitDeg;
        PilotInput.OverrideRoll = tiltR / fc.angleLimitDeg;

        // --- Yaw: face the direction of travel -------------------------------
        float yawStick = 0f;
        if (velH.magnitude > 1.5f && State != Phase.Land)
        {
            float heading = Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg;
            float want = Mathf.Atan2(velH.x, velH.z) * Mathf.Rad2Deg;
            float err = Mathf.DeltaAngle(heading, want);
            yawStick = Mathf.Clamp(err * yawP, -0.6f, 0.6f);
        }
        PilotInput.OverrideYaw = yawStick;

        // --- Vertical: altitude -> climb rate -> throttle around hover -------------
        float tilt = Vector3.Angle(drone.transform.up, Vector3.up);
        float hover = drone.HoverCommand / Mathf.Cos(Mathf.Clamp(tilt, 0f, 60f) * Mathf.Deg2Rad);
        float vzDes = Mathf.Clamp((targetAlt - pos.y) * altitudeP, -descentRate, climbRate);
        if (State == Phase.Land) vzDes = drone.HeightAboveGround < 1.2f ? -0.5f : -descentRate;
        float vzErr = vzDes - vel.y;
        altIntegral = Mathf.Clamp(altIntegral + vzErr * dt * 0.03f, -0.08f, 0.08f);
        float throttle = hover + vzErr * vertVelP + altIntegral;

        if (State == Phase.Land)
        {
            if (drone.IsGrounded && Mathf.Abs(vel.y) < 0.3f)
            {
                landTimer += dt;
                throttle = Mathf.Lerp(hover * 0.6f, 0f, landTimer / 0.8f);
                if (landTimer > 0.8f)
                {
                    fc.Disarm();
                    PilotInput.OverrideActive = false;
                    State = Phase.Done;
                    Message = "Landed - mission complete";
                    return;
                }
            }
            else landTimer = 0f;
        }
        PilotInput.OverrideThrottle = Mathf.Clamp(throttle, 0.05f, 0.9f);
    }
}
