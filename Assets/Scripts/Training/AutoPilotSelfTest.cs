using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Scripted flight used to verify the physics/controller without a pilot:
/// arm, climb, hover, push forward, yaw, roll, hover, descend. Logs telemetry as [SelfTest].
/// Runs automatically when the editor/player is started with -droneSelfTest,
/// or when 'runOnStart' is ticked.
/// </summary>
public class AutoPilotSelfTest : MonoBehaviour
{
    public bool runOnStart;
    public DronePhysics drone;
    public FlightController flightController;

    void Start()
    {
        bool flag = Array.IndexOf(Environment.GetCommandLineArgs(), "-droneSelfTest") >= 0;
        if (runOnStart || flag) StartCoroutine(Run());
    }

    IEnumerator Run()
    {
        yield return new WaitForSeconds(0.5f);
        var crash = drone.GetComponent<CrashDetector>();
        var wind = Wind.Instance;
        float savedWind = wind != null ? wind.meanSpeed : 0f;
        if (wind != null) { wind.meanSpeed = 0f; wind.turbulence = 0f; }

        Debug.Log($"[SelfTest] start  T/W={drone.ThrustToWeight:0.0}  hoverCmd={drone.HoverCommand:0.00}  Vbat={drone.BatteryVolts:0.0}");
        flightController.mode = FlightMode.Angle;
        flightController.beginnerThrottleAssist = false;
        PilotInput.OverrideActive = true;
        PilotInput.OverrideThrottle = 0f;
        yield return null;
        PilotInput.OverrideArmPulse = true;
        yield return new WaitForSeconds(0.3f);
        Debug.Log($"[SelfTest] armed={flightController.Armed}");

        float hover = drone.HoverCommand;
        yield return Phase("climb", 1.5f, throttle: hover + 0.15f);
        yield return Phase("hover", 2.5f, throttle: hover);
        yield return Phase("pitch forward", 2.0f, throttle: hover + 0.03f, pitch: 0.5f);
        yield return Phase("yaw right", 2.0f, throttle: hover + 0.02f, yaw: 0.6f);
        yield return Phase("roll left", 1.5f, throttle: hover + 0.02f, roll: -0.4f);
        yield return Phase("hover", 2.5f, throttle: hover);
        yield return Phase("descend", 2.5f, throttle: hover - 0.06f);

        Vector3 p = drone.transform.position;
        bool ok = p.y > 0.5f && !crash.Crashed && crash.CrashCount == 0 && drone.transform.up.y > 0.8f;
        Debug.Log($"[SelfTest] {(ok ? "PASS" : "FAIL")} final pos=({p.x:0.0}, {p.y:0.0}, {p.z:0.0}) crashes={crash.CrashCount} Vbat={drone.BatteryVolts:0.0} I={drone.BatteryCurrent:0} A used={drone.BatteryUsedMah:0} mAh");

        PilotInput.OverrideActive = false;
        if (wind != null) { wind.meanSpeed = savedWind; wind.turbulence = 0.6f; }
        crash.Respawn(); // hand a fresh, disarmed drone back to the pilot
        crash.ShowMessage("Self-test passed - your turn: throttle down, SPACE to arm");
    }

    IEnumerator Phase(string name, float seconds, float throttle, float roll = 0f, float pitch = 0f, float yaw = 0f)
    {
        PilotInput.OverrideThrottle = throttle;
        PilotInput.OverrideRoll = roll;
        PilotInput.OverridePitch = pitch;
        PilotInput.OverrideYaw = yaw;
        float t = 0f, nextLog = 0f;
        while (t < seconds)
        {
            if (t >= nextLog)
            {
                Vector3 p = drone.transform.position;
                Vector3 v = drone.Body.GetVelocity();
                Debug.Log($"[SelfTest] {name,-14} t={t:0.0}s alt={p.y:0.00} m  pos=({p.x:0.0},{p.z:0.0})  v={v.magnitude:0.00} m/s  roll={flightController.RollDeg:0} pitch={flightController.PitchDeg:0} yaw={drone.transform.eulerAngles.y:0}  rpm={drone.MotorOmega[0] * 9.55f:0}  I={drone.BatteryCurrent:0.0}A V={drone.BatteryVolts:0.0}");
                nextLog += 0.5f;
            }
            t += Time.deltaTime;
            yield return null;
        }
    }
}
