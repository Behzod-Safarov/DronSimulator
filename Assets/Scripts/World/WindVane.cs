using UnityEngine;

/// <summary>
/// Drives a windsock or a flag from the live <see cref="Wind"/> field. This is the instrument a
/// real pilot actually reads before launching, and the only thing in the scene that shows wind
/// *direction* - the OSD only ever had a magnitude.
///
/// <see cref="segments"/> is a nested chain of transforms from the pole outwards. The first one
/// yaws the whole assembly downwind; every segment then lifts from hanging to streaming as the
/// wind builds, with a phase lag down the chain so the sock ripples from mouth to tail instead
/// of moving as one rigid cone.
/// </summary>
public class WindVane : MonoBehaviour
{
    public enum Kind
    {
        /// <summary>Aviation windsock: hangs limp when calm, fully extended at 15 kt.</summary>
        Sock,
        /// <summary>Flag on a pole: swings round the pole and flutters, but never lifts above horizontal.</summary>
        Flag,
    }

    public Kind kind = Kind.Sock;
    [Tooltip("Nested chain from the pole outwards; [0] also carries the yaw")]
    public Transform[] segments;

    [Tooltip("Wind speed (m/s) at which the sock is fully extended. The aviation standard is 15 kt = 7.7 m/s.")]
    public float fullExtensionSpeed = 7.7f;
    [Tooltip("Droop of the first segment in still air, degrees below horizontal")]
    public float restDroopDeg = 78f;
    [Tooltip("Ripple amplitude in degrees at full extension")]
    public float rippleDeg = 9f;
    public float rippleHz = 1.6f;
    [Tooltip("Extra yaw wander in degrees, so it never sits perfectly steady")]
    public float yawWanderDeg = 7f;

    Wind wind;
    Quaternion[] rest;
    float yaw;
    float phase;

    void Start()
    {
        wind = Wind.Instance;
        if (segments == null) return;
        rest = new Quaternion[segments.Length];
        for (int i = 0; i < segments.Length; i++)
            if (segments[i] != null) rest[i] = segments[i].localRotation;
        phase = Random.value * 10f;
    }

    void Update()
    {
        if (segments == null || segments.Length == 0 || rest == null) return;
        if (wind == null) { wind = Wind.Instance; if (wind == null) return; }

        Vector3 v = wind.VelocityAt(transform.position, Time.time, 4f);
        v.y = 0f;
        float speed = v.magnitude;
        float ext = Mathf.Clamp01(speed / Mathf.Max(0.01f, fullExtensionSpeed));

        // Yaw downwind. Chase the target rather than snapping to it: a real sock has mass and
        // lags the gust it is responding to.
        if (speed > 0.05f)
        {
            float target = Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg
                         + Mathf.Sin((Time.time + phase) * 0.7f) * yawWanderDeg * ext;
            yaw = Mathf.LerpAngle(yaw, target, 1f - Mathf.Exp(-Time.deltaTime * (1.2f + 2.5f * ext)));
        }

        // Limp when calm, horizontal when fully extended. A flag is hemmed to its pole and only
        // ever swings in the ground plane, so it gets no lift term.
        float droop = kind == Kind.Sock ? Mathf.Lerp(restDroopDeg, 0f, ext) : 0f;
        float perSegment = droop / segments.Length;
        float amp = rippleDeg * (0.25f + 0.75f * ext);

        for (int i = 0; i < segments.Length; i++)
        {
            var s = segments[i];
            if (s == null) continue;

            // Travelling wave: each segment lags the one before it, which is what turns a rigid
            // cone into something that visibly whips.
            float lag = i * 0.55f;
            float ripple = Mathf.Sin((Time.time * rippleHz * Mathf.PI * 2f) - lag + phase);
            float lateral = Mathf.Cos((Time.time * rippleHz * Mathf.PI * 2f * 0.8f) - lag + phase);

            var local = Quaternion.Euler(perSegment + ripple * amp, lateral * amp * 0.6f, 0f);
            s.localRotation = i == 0
                ? Quaternion.Euler(0f, yaw, 0f) * rest[0] * local
                : rest[i] * local;
        }
    }
}
