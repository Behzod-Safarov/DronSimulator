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
    public Pid rollRate = new Pid(0.0009f, 0.0016f, 0.000022f);
    public Pid pitchRate = new Pid(0.0010f, 0.0018f, 0.000025f);
    public Pid yawRate = new Pid(0.0060f, 0.0080f, 0f);

    [Header("Simulated gyro")]
    public float gyroNoiseDegPerSec = 0.35f;
    public float gyroLowPassHz = 150f;
    [Tooltip("Extra control-loop latency in physics steps (ESC/DShot + filtering)")]
    [Range(0, 3)] public int loopDelaySteps = 1;

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
            else if (input.Throttle < 0.1f || (beginnerThrottleAssist && mode != FlightMode.Acro)) Arm();
        }

        if (input.ModePressed)
            mode = (FlightMode)(((int)mode + 1) % 3);
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
        float acroPitch = pitchRates.Apply(input.Pitch);
        float acroRoll = -rollRates.Apply(input.Roll); // roll right = -Z
        float targetYawRate = yawRates.Apply(input.Yaw);
        float targetPitchRate, targetRollRate;

        switch (mode)
        {
            case FlightMode.Angle:
                targetPitchRate = LevelRate(input.Pitch * angleLimitDeg, PitchDeg);
                targetRollRate = LevelRate(-input.Roll * angleLimitDeg, RollDeg);
                break;

            case FlightMode.Horizon:
            {
                float stick = Mathf.Max(Mathf.Abs(input.Pitch), Mathf.Abs(input.Roll));
                float level = Mathf.Clamp01(1f - stick / horizonTransition);
                targetPitchRate = LevelRate(input.Pitch * angleLimitDeg, PitchDeg) * level + acroPitch * (1f - level);
                targetRollRate = LevelRate(-input.Roll * angleLimitDeg, RollDeg) * level + acroRoll * (1f - level);
                break;
            }

            default:
                targetPitchRate = acroPitch;
                targetRollRate = acroRoll;
                break;
        }
        TargetRates = new Vector3(targetPitchRate, targetYawRate, targetRollRate);

        // --- Throttle -----------------------------------------------------
        float t = input.Throttle;
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

        // --- Rate PIDs ----------------------------------------------------
        float outX, outY, outZ;
        if (restingOnGround && !airMode)
        {
            rollRate.Reset(); pitchRate.Reset(); yawRate.Reset();
            outX = outY = outZ = 0f;
        }
        else
        {
            outX = pitchRate.Update(targetPitchRate, w.x, dt);
            outY = yawRate.Update(targetYawRate, w.y, dt);
            outZ = rollRate.Update(targetRollRate, w.z, dt);
            if (restingOnGround)
            {
                // Air mode keeps the loop alive but don't wind up against the ground.
                rollRate.Reset(); pitchRate.Reset(); yawRate.Reset();
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

    float LevelRate(float targetAngle, float currentAngle)
    {
        float max = Mathf.Max(rollRates.maxRate, pitchRates.maxRate);
        return Mathf.Clamp((targetAngle - currentAngle) * angleP, -max, max);
    }

    float Gauss(int axis)
    {
        // Sum of three uniforms ≈ Gaussian, cheap and good enough for sensor noise.
        return (Random.value + Random.value + Random.value - 1.5f) * 1.63f;
    }
}
