using UnityEngine;

public enum FlightMode
{
    /// <summary>Stick = tilt angle, the drone self-levels. Beginner.</summary>
    Angle,
    /// <summary>Self-levels near stick centre, rate mode at the edges (flips possible).</summary>
    Horizon,
    /// <summary>Stick = rotation rate, no self-levelling. The mode real pilots learn.</summary>
    Acro,
}

/// <summary>Betaflight "Actual Rates": centre sensitivity + max rate + expo, in deg/s.</summary>
[System.Serializable]
public class ActualRates
{
    [Tooltip("deg/s per full stick near centre")]
    public float centerSensitivity = 70f;
    [Tooltip("deg/s at full stick")]
    public float maxRate = 670f;
    [Range(0f, 1f)] public float expo = 0.54f;

    public float Apply(float stick)
    {
        float a = Mathf.Abs(stick);
        float s5 = stick * stick * stick * stick * stick;
        float expof = a * (s5 * expo + stick * (1f - expo));
        float stickMovement = Mathf.Max(0f, maxRate - centerSensitivity);
        return stick * centerSensitivity + stickMovement * expof;
    }
}

/// <summary>
/// Betaflight-style flight controller: simulated gyro (noise, low-pass, latency) ->
/// (optional) angle loop -> rate PIDs -> air-mode mixer -> ESC commands.
///
/// Body-axis conventions (Unity, left-handed):
///   +X rotation = nose down (pitch forward)
///   +Y rotation = yaw right
///   +Z rotation = roll LEFT (right side up)
/// Stick conventions: roll right = +1, pitch forward = +1, yaw right = +1.
/// </summary>
[RequireComponent(typeof(DronePhysics))]
public class FlightController : MonoBehaviour
{
    [Header("Mode")]
    public FlightMode mode = FlightMode.Angle;
    public bool Armed { get; private set; }

    [Header("Rates (Betaflight Actual Rates)")]
    public ActualRates rollRates = new ActualRates();
    public ActualRates pitchRates = new ActualRates();
    public ActualRates yawRates = new ActualRates();

    [Header("Angle / Horizon")]
    [Tooltip("Maximum tilt in Angle mode (Betaflight angle_limit)")]
    public float angleLimitDeg = 60f;
    [Tooltip("Outer loop gain: deg/s of rate per degree of angle error")]
    public float angleP = 6.5f;
    [Tooltip("Horizon: stick deflection at which levelling is fully off")]
    [Range(0.3f, 1f)] public float horizonTransition = 0.75f;

    [Header("Rate PIDs (output per deg/s)")]
    public Pid rollRate = new Pid(0.0009f, 0.0016f, 0.000022f, 0.00003f);
    public Pid pitchRate = new Pid(0.0010f, 0.0018f, 0.000025f, 0.00003f);
    public Pid yawRate = new Pid(0.0060f, 0.0080f, 0f, 0.00018f);

    [Header("TPA (throttle PID attenuation)")]
    [Tooltip("Throttle fraction above which rate-PID P/D gain rolls off, like Betaflight tpa_breakpoint")]
    [Range(0f, 1f)] public float tpaBreakpoint = 0.55f;
    [Tooltip("How much P/D is reduced at full throttle above the breakpoint (0 = none, 1 = fully to zero)")]
    [Range(0f, 1f)] public float tpaRate = 0.25f;

    [Header("Simulated gyro")]
    public float gyroNoiseDegPerSec = 0.35f;
    public float gyroLowPassHz = 150f;
    [Tooltip("Extra control-loop latency in physics steps (ESC/DShot + filtering)")]
    [Range(0, 3)] public int loopDelaySteps = 1;

    [Header("RC smoothing")]
    [Tooltip("Betaflight rc_smoothing: cut-off of the setpoint smoothing filter. The sticks are sampled once per rendered frame but this loop runs at 250 Hz, so the raw stick value arrives as a staircase - see the note in FixedUpdate")]
    [Range(0f, 120f)] public float rcSmoothingHz = 30f;

    [Header("Throttle")]
    [Tooltip("Idle motor speed while armed (Betaflight dshot_idle 5.5%)")]
    [Range(0f, 0.2f)] public float motorIdle = 0.055f;
    public bool airMode = true;
    [Tooltip("BEGINNER AID (not realistic): throttle stick centre = hover thrust in Angle/Horizon")]
    public bool beginnerThrottleAssist;

    // Telemetry for the OSD
    public float RollDeg { get; private set; }
    public float PitchDeg { get; private set; }
    public float ThrottleOut { get; private set; }
    public Vector3 GyroDegPerSec => gyroFiltered;
    public Vector3 TargetRates { get; private set; }

    DronePhysics drone;
    PilotInput input;
    Vector3 gyroFiltered;
    // Two cascaded one-pole stages (PT2), the closest cheap equivalent of Betaflight's PT3
    // setpoint smoothing. Order: roll, pitch, yaw, throttle.
    Vector4 rcStage1, rcStage2;
    bool rcPrimed;
    readonly Vector3[] gyroDelay = new Vector3[4];
    int delayHead;
    float noiseSeed;

    void Awake()
    {
        drone = GetComponent<DronePhysics>();
        input = GetComponent<PilotInput>();
        noiseSeed = Random.value * 100f;
    }

    void Update()
    {
        if (input == null) return;

        if (input.ArmPressed)
        {
            if (Armed) Disarm();
            // Always require raw stick low to arm, even with beginnerThrottleAssist on - that
            // assist only reshapes the throttle curve after arming, it must never let the
            // motors spin up to near-full thrust the instant they start (real quads don't).
            else if (input.Throttle < 0.1f) Arm();
        }

        if (input.ModePressed)
            mode = (FlightMode)(((int)mode + 1) % 3);

        // A radio drives these as switches, not as key presses.
        if (input.RcDisarmRequested && Armed) Disarm();
        if (input.RcModeChanged) mode = input.RcMode;
    }

    public void Arm()
    {
        Armed = true;
        rollRate.Reset();
        pitchRate.Reset();
        yawRate.Reset();
        drone.MotorsEnabled = true;
    }

    public void Disarm()
    {
        Armed = false;
        drone.MotorsEnabled = false;
        for (int i = 0; i < 4; i++) drone.MotorCommand[i] = 0f;
    }

    void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;
        var rb = drone.Body;

        // --- Setpoint smoothing (Betaflight rc_smoothing) ---------------------
        // PilotInput samples the sticks once per rendered frame; this loop runs at 250 Hz. The raw
        // stick value is therefore a staircase that steps once every 2-4 physics steps, and the
        // feedforward term differentiates it by dt: a 0.05 yaw-stick move used to produce
        // ff * 33.5/0.004 = 1.51, i.e. saturated full motor authority for one step and then nothing
        // for the next three. Real flight controllers filter the setpoint for exactly this reason.
        if (input != null)
        {
            var raw = new Vector4(input.Roll, input.Pitch, input.Yaw, input.Throttle);
            if (!rcPrimed) { rcStage1 = rcStage2 = raw; rcPrimed = true; }
            else if (rcSmoothingHz <= 0f) { rcStage1 = rcStage2 = raw; }
            else
            {
                float k = dt / (dt + 1f / (2f * Mathf.PI * rcSmoothingHz));
                rcStage1 += (raw - rcStage1) * k;
                rcStage2 += (rcStage1 - rcStage2) * k;
            }
        }
        float stickRoll = rcStage2.x, stickPitch = rcStage2.y, stickYaw = rcStage2.z, stickThrottle = rcStage2.w;

        // Attitude from the world-up vector expressed in body frame.
        Vector3 upB = transform.InverseTransformDirection(Vector3.up);
        PitchDeg = Mathf.Atan2(-upB.z, upB.y) * Mathf.Rad2Deg; // + = nose down
        RollDeg = Mathf.Atan2(upB.x, upB.y) * Mathf.Rad2Deg;   // + = roll left

        // --- Simulated gyro ---------------------------------------------------
        Vector3 wTrue = transform.InverseTransformDirection(rb.angularVelocity) * Mathf.Rad2Deg;
        Vector3 noise = new Vector3(Gauss(0), Gauss(1), Gauss(2)) * gyroNoiseDegPerSec;
        // Motor vibration shows up in the gyro as a small high-frequency ripple.
        float vib = 0f;
        for (int i = 0; i < 4; i++) vib += drone.MotorOmega[i];
        vib *= 0.00002f;
        noise += new Vector3(Mathf.Sin(Time.fixedTime * 900f), 0f, Mathf.Cos(Time.fixedTime * 730f)) * vib;
        float alpha = dt / (dt + 1f / (2f * Mathf.PI * Mathf.Max(10f, gyroLowPassHz)));
        gyroFiltered += (wTrue + noise - gyroFiltered) * alpha;

        gyroDelay[delayHead] = gyroFiltered;
        int readIdx = (delayHead - loopDelaySteps + gyroDelay.Length) % gyroDelay.Length;
        Vector3 w = gyroDelay[readIdx];
        delayHead = (delayHead + 1) % gyroDelay.Length;

        if (!Armed || input == null)
        {
            ThrottleOut = 0f;
            return;
        }

        // --- Target rates -------------------------------------------------
        float acroPitch = pitchRates.Apply(stickPitch);
        float acroRoll = -rollRates.Apply(stickRoll); // roll right = -Z
        float targetYawRate = yawRates.Apply(stickYaw);
        float targetPitchRate, targetRollRate;

        switch (mode)
        {
            case FlightMode.Angle:
                targetPitchRate = LevelRate(stickPitch * angleLimitDeg, PitchDeg, pitchRates.maxRate);
                targetRollRate = LevelRate(-stickRoll * angleLimitDeg, RollDeg, rollRates.maxRate);
                break;

            case FlightMode.Horizon:
            {
                float stick = Mathf.Max(Mathf.Abs(stickPitch), Mathf.Abs(stickRoll));
                float level = Mathf.Clamp01(1f - stick / horizonTransition);
                targetPitchRate = LevelRate(stickPitch * angleLimitDeg, PitchDeg, pitchRates.maxRate) * level + acroPitch * (1f - level);
                targetRollRate = LevelRate(-stickRoll * angleLimitDeg, RollDeg, rollRates.maxRate) * level + acroRoll * (1f - level);
                break;
            }

            default:
                targetPitchRate = acroPitch;
                targetRollRate = acroRoll;
                break;
        }
        TargetRates = new Vector3(targetPitchRate, targetYawRate, targetRollRate);

        // --- Throttle -----------------------------------------------------
        float t = stickThrottle;
        float throttle;
        if (beginnerThrottleAssist && mode != FlightMode.Acro)
        {
            float h = drone.HoverCommand;
            throttle = t < 0.5f ? h * (t / 0.5f) : h + (1f - h) * ((t - 0.5f) / 0.5f);
        }
        else
        {
            throttle = t;
        }

        bool restingOnGround = drone.IsGrounded && t < 0.03f;

        // TPA: roll P/D off above a throttle breakpoint, the way Betaflight tames D-term
        // oscillation/prop-wash near full throttle instead of needing the whole loop detuned
        // for the worst-case high-throttle case.
        float tpa = throttle > tpaBreakpoint ? 1f - (throttle - tpaBreakpoint) / (1f - tpaBreakpoint) * tpaRate : 1f;

        // --- Rate PIDs ----------------------------------------------------
        float outX, outY, outZ;
        if (restingOnGround && !airMode)
        {
            rollRate.Reset(); pitchRate.Reset(); yawRate.Reset();
            outX = outY = outZ = 0f;
        }
        else
        {
            outX = pitchRate.Update(targetPitchRate, w.x, dt, tpa);
            outY = yawRate.Update(targetYawRate, w.y, dt, tpa);
            outZ = rollRate.Update(targetRollRate, w.z, dt, tpa);
            if (restingOnGround)
            {
                // Air mode keeps the loop alive but don't wind up against the ground - only the
                // integral needs clearing here; wiping the whole Pid (old Reset() call) also
                // zeroed the D filter every single frame at idle, killing damping right when the
                // loop is most likely to be twitchy.
                rollRate.ResetIntegral(); pitchRate.ResetIntegral(); yawRate.ResetIntegral();
                outX *= 0.2f; outY *= 0.2f; outZ *= 0.2f;
            }
        }

        // --- Air-mode mixer (Betaflight style) -------------------------------
        //  +outX (nose down)  -> less thrust front, more rear
        //  +outY (yaw right)  -> more thrust on CCW props (FR, RL)
        //  +outZ (roll left)  -> more thrust right, less left
        float mFR = +outZ - outX + outY;
        float mFL = -outZ - outX - outY;
        float mRL = -outZ + outX + outY;
        float mRR = +outZ + outX - outY;

        float mixMax = Mathf.Max(Mathf.Max(mFR, mFL), Mathf.Max(mRL, mRR));
        float mixMin = Mathf.Min(Mathf.Min(mFR, mFL), Mathf.Min(mRL, mRR));
        float range = mixMax - mixMin;
        if (range > 1f)
        {
            mFR /= range; mFL /= range; mRL /= range; mRR /= range;
            mixMax /= range; mixMin /= range;
        }

        // Keep the whole mix inside [idle, 1] by sliding the throttle (that's what "air mode" is).
        float low = motorIdle;
        throttle = Mathf.Clamp(throttle, low - mixMin, 1f - mixMax);
        if (!airMode && t < 0.01f) throttle = low; // classic behaviour: idle when throttle is closed
        ThrottleOut = throttle;

        float[] m = drone.MotorCommand;
        m[0] = Mathf.Clamp01(throttle + mFR);
        m[1] = Mathf.Clamp01(throttle + mFL);
        m[2] = Mathf.Clamp01(throttle + mRL);
        m[3] = Mathf.Clamp01(throttle + mRR);
    }

    float LevelRate(float targetAngle, float currentAngle, float maxRate)
    {
        return Mathf.Clamp((targetAngle - currentAngle) * angleP, -maxRate, maxRate);
    }

    float Gauss(int axis)
    {
        // Sum of three uniforms ≈ Gaussian, cheap and good enough for sensor noise.
        return (Random.value + Random.value + Random.value - 1.5f) * 1.63f;
    }
}
