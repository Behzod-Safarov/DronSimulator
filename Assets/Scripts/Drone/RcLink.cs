using UnityEngine;

/// <summary>
/// The control link, and Betaflight-style failsafe when it drops.
///
/// The sim modelled the *video* link in detail - <see cref="FpvCameraEffects"/> degrades the
/// picture with range and occlusion and breaks it up completely by 450 m - but control authority
/// was absolute everywhere. You could fly to a screen of pure static and still have perfect
/// sticks, which is backwards: on a real quad losing the link means the flight controller stops
/// hearing the radio and runs its failsafe procedure.
///
/// Two differences from the video model, both real:
///  - the RC link (900 MHz / 2.4 GHz) carries much further than 5.8 GHz analog video, so it is
///    still solid at the 470 m out-of-bounds radius. Range alone will not fail it inside the
///    flying area, which is why <see cref="rangeNoSignal"/> is well past the boundary.
///  - what kills it in practice is putting something solid between you and the quad. Leaves and
///    branches attenuate a little and are ignored here; concrete does not, so the farm buildings
///    and the bando finally mean something.
///
/// Failsafe procedure is Betaflight's default, DROP: cut the motors. Nothing clever, because
/// nothing clever is what a real quad does without GPS rescue configured.
/// </summary>
[RequireComponent(typeof(DronePhysics), typeof(FlightController))]
public class RcLink : MonoBehaviour
{
    [Tooltip("Turn the whole model off (control then never drops, the old behaviour)")]
    public bool enableFailsafe = true;

    [Tooltip("Pilot / transmitter position - the line-of-sight camera")]
    public Transform pilot;

    [Tooltip("Link quality is 100% inside this range, in metres")]
    public float rangeFullSignal = 350f;
    [Tooltip("Range at which the link is gone entirely. Deliberately past the flying area.")]
    public float rangeNoSignal = 1200f;
    [Tooltip("How much of the link a solid obstacle between pilot and drone takes away")]
    [Range(0f, 1f)] public float occlusionLoss = 0.85f;

    [Tooltip("Link quality below which failsafe starts counting down")]
    [Range(0f, 1f)] public float failsafeQuality = 0.15f;
    [Tooltip("Betaflight failsafe_delay: seconds of lost link before the motors are cut")]
    public float failsafeDelay = 1.0f;
    [Tooltip("Quality below which the OSD starts warning, before anything is actually cut")]
    [Range(0f, 1f)] public float warnQuality = 0.45f;

    /// <summary>0..1 link quality, the LQ figure a real OSD shows.</summary>
    public float Quality { get; private set; } = 1f;
    public bool Warning => enableFailsafe && Quality < warnQuality;
    public bool FailsafeTripped { get; private set; }

    DronePhysics drone;
    FlightController fc;
    CrashDetector crash;
    SerialRc serial;
    float occlusion;
    float lowSince = -1f;

    void Awake()
    {
        drone = GetComponent<DronePhysics>();
        fc = GetComponent<FlightController>();
        crash = GetComponent<CrashDetector>();
        serial = GetComponent<SerialRc>();
    }

    void Update()
    {
        if (!enableFailsafe) { Quality = 1f; FailsafeTripped = false; return; }

        // A transmitter wired in over UART that goes quiet is a lost link too: the flight
        // controller stops hearing the radio for the same reason it does at the edge of RF range,
        // so it runs the same procedure. It only counts once that link has actually carried
        // frames, or flying from the keyboard with nothing plugged in would failsafe instantly.
        bool serialLost = serial != null && serial.EverHadSignal && !serial.HasSignal;

        Quality = serialLost ? 0f : RfQuality();

        // Only a flying quad can be failsafed, and the countdown has to be sustained: a momentary
        // flicker as you pass behind a pole is not a lost link.
        if (!fc.Armed || Quality >= failsafeQuality)
        {
            lowSince = -1f;
            return;
        }
        if (lowSince < 0f) lowSince = Time.time;
        else if (Time.time - lowSince > failsafeDelay)
        {
            lowSince = -1f;
            FailsafeTripped = true;
            fc.Disarm();
            // Airborne, this is a crash - there is no recovering a quad whose motors just stopped.
            // On the ground it is only an unpleasant surprise, so let it stand as a message.
            if (crash != null)
            {
                if (drone.HeightAboveGround > 1.5f) crash.ForceCrash("FAILSAFE - lost RC link");
                else crash.ShowMessage("FAILSAFE - lost RC link");
            }
        }
    }

    /// <summary>Link quality from range and whatever is standing in the way, 0..1.</summary>
    float RfQuality()
    {
        if (pilot == null) return 1f;

        float d = Vector3.Distance(pilot.position, transform.position);
        float rangeQ = 1f - Mathf.Clamp01((d - rangeFullSignal) / Mathf.Max(1f, rangeNoSignal - rangeFullSignal));

        // Trees do not block 2.4 GHz meaningfully; walls and terrain do. Skip straight past
        // foliage hits rather than counting them, or the tree line alone would failsafe the quad.
        bool blocked = false;
        var hits = Physics.RaycastAll(pilot.position, (transform.position - pilot.position).normalized,
                                      d, ~0, QueryTriggerInteraction.Ignore);
        foreach (var h in hits)
        {
            var root = h.transform.root;
            if (root == transform.root) continue;
            if (root != null && (root.name == "Tree" || root.name == "Bush")) continue;
            blocked = true;
            break;
        }
        occlusion = Mathf.MoveTowards(occlusion, blocked ? 1f : 0f, Time.deltaTime * (blocked ? 2f : 4f));

        return Mathf.Clamp01(rangeQ * (1f - occlusionLoss * occlusion));
    }
}
