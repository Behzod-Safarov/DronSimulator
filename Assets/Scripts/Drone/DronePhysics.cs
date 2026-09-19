using UnityEngine;

/// <summary>
/// Quadrotor rigid-body model, parameterised as a 5-inch freestyle quad
/// (2306 motors, 5.1" tri-blade props, 6S Li-Po).
///
/// Per motor:  ESC command -> target RPM (limited by battery voltage) -> first-order
/// spool-up -> thrust = kT·ω² (reduced by axial inflow, boosted by ground effect),
/// drag torque = kQ·ω², plus prop-inertia reaction torque and gyroscopic coupling.
/// Body: anisotropic quadratic drag, linear rotor drag (H-force), all relative to the wind.
/// Battery: open-circuit voltage from state of charge minus internal-resistance sag,
/// capacity depletes with the current drawn.
///
/// Motor order everywhere in the project: 0 = FR, 1 = FL, 2 = RL, 3 = RR.
/// Spin direction (viewed from above): FR CCW, FL CW, RL CCW, RR CW (Betaflight "props in").
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class DronePhysics : MonoBehaviour
{
    [Header("Airframe (5\" freestyle)")]
    [Tooltip("All-up weight in kg including battery")]
    public float mass = 0.72f;
    [Tooltip("Motor-to-centre distance in metres")]
    public float armLength = 0.115f;
    [Tooltip("Body inertia about X (pitch), Y (yaw), Z (roll) in kg·m²")]
    public Vector3 inertia = new Vector3(0.0035f, 0.0060f, 0.0035f);

    [Header("Motors & props")]
    [Tooltip("Motor KV (RPM per volt)")]
    public float motorKv = 1750f;
    [Tooltip("Fraction of the no-load speed the motor reaches at full throttle under prop load")]
    [Range(0.4f, 1f)] public float loadedSpeedFraction = 0.65f;
    [Tooltip("Thrust coefficient: thrust [N] = kT · ω² (ω in rad/s)")]
    public float kT = 1.71e-6f;
    [Tooltip("Torque coefficient: drag torque [N·m] = kQ · ω²")]
    public float kQ = 4.0e-8f;
    [Tooltip("Prop + bell rotational inertia (kg·m²)")]
    public float propInertia = 6.5e-6f;
    public float propRadius = 0.0648f;
    public int propBlades = 3;
    public float spoolUpTimeConstant = 0.025f;
    public float spoolDownTimeConstant = 0.045f;
    [Tooltip("Motor+ESC electrical efficiency at the operating point")]
    [Range(0.4f, 1f)] public float electricalEfficiency = 0.75f;
    [Tooltip("Inflow coefficient: thrust falls as the prop moves into the air, thrust *= 1 - vAxial/(kInflow·ω)")]
    public float kInflow = 0.0145f;
    public bool groundEffect = true;

    [Header("Battery (6S 1100 mAh)")]
    public int cells = 6;
    public float capacityMah = 1100f;
    [Tooltip("Whole-pack internal resistance in ohms")]
    public float internalResistance = 0.018f;
    public float cellFullVolts = 4.20f;
    public float cellEmptyVolts = 3.30f;
    [Tooltip("Never drain the battery (training)")]
    public bool infiniteBattery;

    [Header("Aerodynamics")]
    [Tooltip("Cd·A per body axis (X right, Y up, Z forward) in m²")]
    public Vector3 bodyDragArea = new Vector3(0.010f, 0.035f, 0.009f);
    [Tooltip("Rotor H-force coefficient: F = -k · Σω · v_inplane")]
    public float rotorDragCoefficient = 5e-5f;
    public float angularDamping = 0.02f;
    public float airDensity = 1.225f;

    [Header("Payload")]
    [Tooltip("Extra mass carried (kg) - set by scenarios that hang things under the drone")]
    public float payloadMass;

    [Header("Visuals (optional)")]
    public Transform[] propellers = new Transform[4];

    public static readonly float[] SpinDirection = { +1f, -1f, +1f, -1f };

    /// <summary>ESC commands 0..1 written by the flight controller every physics step.</summary>
    public readonly float[] MotorCommand = new float[4];
    /// <summary>Motor speed in rad/s.</summary>
    public readonly float[] MotorOmega = new float[4];
    /// <summary>Thrust in newtons produced this step.</summary>
    public readonly float[] MotorThrust = new float[4];

    public bool MotorsEnabled { get; set; }
    public bool IsGrounded => Time.fixedTime - lastGroundContact < 0.05f;
    public Rigidbody Body => rb;
    public float HeightAboveGround { get; private set; } = 10f;
    public Vector3 AirVelocity { get; private set; }   // body velocity relative to the air (world frame)
    public Vector3 WindVelocity { get; private set; }

    // Battery telemetry
    public float BatteryVolts { get; private set; }
    public float BatteryCurrent { get; private set; }
    public float BatteryUsedMah { get; private set; }
    public float BatteryFraction => infiniteBattery ? 1f : Mathf.Clamp01(1f - BatteryUsedMah / capacityMah);
    public bool BatteryDead => !infiniteBattery && BatteryFraction <= 0f;
    public float FlightSeconds { get; private set; }

    public float OmegaMaxAtFullBattery => loadedSpeedFraction * motorKv * RpmToRad * cells * cellFullVolts;
    public float MaxThrustPerMotor => kT * OmegaMaxAtFullBattery * OmegaMaxAtFullBattery;
    public float ThrustToWeight => 4f * MaxThrustPerMotor / (mass * Physics.gravity.magnitude);
    /// <summary>ESC command that hovers at the current battery voltage when level.</summary>
    public float HoverCommand
    {
        get
        {
            float omegaHover = Mathf.Sqrt((mass + Mathf.Max(0f, payloadMass)) * Physics.gravity.magnitude / 4f / kT);
            float omegaMax = loadedSpeedFraction * motorKv * RpmToRad * Mathf.Max(1f, BatteryVolts);
            return Mathf.Clamp01(omegaHover / omegaMax);
        }
    }

    const float RpmToRad = 2f * Mathf.PI / 60f;

    Rigidbody rb;
    Vector3[] motorLocalPositions;
    float lastGroundContact = -10f;
    float openCircuitVolts;

    public Vector3[] MotorLocalPositions
    {
        get
        {
            if (motorLocalPositions == null)
            {
                float d = armLength * 0.70710678f;
                motorLocalPositions = new[]
                {
                    new Vector3(+d, 0f, +d), // FR
                    new Vector3(-d, 0f, +d), // FL
                    new Vector3(-d, 0f, -d), // RL
                    new Vector3(+d, 0f, -d), // RR
                };
            }
            return motorLocalPositions;
        }
    }

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.mass = mass;
        rb.inertiaTensor = inertia;
        rb.inertiaTensorRotation = Quaternion.identity;
        rb.SetDamping(0f, angularDamping);
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
        rb.maxAngularVelocity = 60f; // rad/s (~3400 deg/s)
        rb.useGravity = true;
        ResetBattery();
    }

    public void ResetBattery()
    {
        BatteryUsedMah = 0f;
        openCircuitVolts = cells * cellFullVolts;
        BatteryVolts = openCircuitVolts;
        BatteryCurrent = 0f;
        FlightSeconds = 0f;
    }

    void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;
        float totalMass = mass + Mathf.Max(0f, payloadMass);
        if (!Mathf.Approximately(rb.mass, totalMass)) rb.mass = totalMass;

        // --- Environment -------------------------------------------------
        WindVelocity = Wind.Sample(transform.position, Time.fixedTime);
        Vector3 v = rb.GetVelocity();
        AirVelocity = v - WindVelocity;
        Vector3 airLocal = transform.InverseTransformDirection(AirVelocity);
        Vector3 omegaBody = transform.InverseTransformDirection(rb.angularVelocity);

        if (Physics.Raycast(transform.position, Vector3.down, out var hit, 5f, ~0, QueryTriggerInteraction.Ignore))
            HeightAboveGround = hit.distance;
        else
            HeightAboveGround = 5f;

        // Ground effect (Cheeseman–Bennett), capped so it can't explode on contact.
        float ge = 1f;
        if (groundEffect)
        {
            float h = Mathf.Max(HeightAboveGround, propRadius * 0.5f);
            float r = propRadius / (4f * h);
            ge = Mathf.Min(1.35f, 1f / Mathf.Max(0.05f, 1f - r * r));
        }

        // --- Battery ----------------------------------------------------
        float soc = BatteryFraction;
        // Li-Po discharge curve: flat middle, knee at the end.
        float cellV = Mathf.Lerp(cellEmptyVolts, cellFullVolts, Mathf.Pow(soc, 0.35f));
        openCircuitVolts = cells * cellV;
        // Use last step's current for the sag (avoids an algebraic loop).
        BatteryVolts = Mathf.Max(0f, openCircuitVolts - BatteryCurrent * internalResistance);
        float omegaMax = loadedSpeedFraction * motorKv * RpmToRad * BatteryVolts;
        bool motorsOn = MotorsEnabled && !BatteryDead;

        // --- Motors -------------------------------------------------------
        float yawTorque = 0f;
        float mechPower = 0f;
        float sumOmegaSigned = 0f, sumOmega = 0f;

        for (int i = 0; i < 4; i++)
        {
            float target = motorsOn ? Mathf.Clamp01(MotorCommand[i]) * omegaMax : 0f;
            float tau = target > MotorOmega[i] ? spoolUpTimeConstant : spoolDownTimeConstant;
            float prev = MotorOmega[i];
            MotorOmega[i] += (target - MotorOmega[i]) * (dt / (tau + dt));
            float omega = MotorOmega[i];
            float omegaDot = (omega - prev) / dt;

            // Thrust with inflow loss: moving up through the air (or air blowing up through
            // the disc) reduces thrust, descending increases it slightly.
            float thrust = kT * omega * omega;
            if (omega > 50f)
            {
                float inflow = 1f - airLocal.y / (kInflow * omega);
                thrust *= Mathf.Clamp(inflow, 0.15f, 1.3f);
            }
            thrust *= ge;
            MotorThrust[i] = thrust;

            rb.AddForceAtPosition(transform.up * thrust, transform.TransformPoint(MotorLocalPositions[i]));

            // Reaction torques: aerodynamic drag torque + prop angular acceleration.
            yawTorque += SpinDirection[i] * (kQ * omega * omega + propInertia * omegaDot);

            mechPower += kQ * omega * omega * omega;
            sumOmegaSigned += SpinDirection[i] * omega;
            sumOmega += omega;
        }
        rb.AddRelativeTorque(0f, yawTorque, 0f);

        // Gyroscopic coupling: rotor angular momentum H (about body Y) reacting to body rates.
        // CCW-from-above props spin about -Y in Unity's left-handed frame.
        Vector3 rotorH = new Vector3(0f, -propInertia * sumOmegaSigned, 0f);
        Vector3 gyro = -Vector3.Cross(omegaBody, rotorH);
        rb.AddRelativeTorque(gyro);

        // --- Battery current ----------------------------------------------
        float electricalPower = mechPower / electricalEfficiency + 0.6f /* FC, VTX, camera */;
        BatteryCurrent = BatteryVolts > 1f ? electricalPower / BatteryVolts : 0f;
        if (!infiniteBattery) BatteryUsedMah += BatteryCurrent * dt / 3.6f;
        if (motorsOn) FlightSeconds += dt;

        // --- Aerodynamic drag ------------------------------------------------
        Vector3 dragLocal = new Vector3(
            -0.5f * airDensity * bodyDragArea.x * Mathf.Abs(airLocal.x) * airLocal.x,
            -0.5f * airDensity * bodyDragArea.y * Mathf.Abs(airLocal.y) * airLocal.y,
            -0.5f * airDensity * bodyDragArea.z * Mathf.Abs(airLocal.z) * airLocal.z);
        // Rotor drag: props act like a linear damper in their plane.
        dragLocal.x -= rotorDragCoefficient * sumOmega * airLocal.x;
        dragLocal.z -= rotorDragCoefficient * sumOmega * airLocal.z;
        rb.AddRelativeForce(dragLocal);
    }

    void Update()
    {
        float dt = Time.deltaTime;
        for (int i = 0; i < 4; i++)
        {
            if (propellers[i] == null) continue;
            // Visual RPM is aliased on purpose (looks like a spinning prop on video).
            propellers[i].Rotate(0f, -SpinDirection[i] * MotorOmega[i] * Mathf.Rad2Deg * dt * 0.07f, 0f, Space.Self);
        }
    }

    void OnCollisionStay(Collision _) => lastGroundContact = Time.fixedTime;
    void OnCollisionEnter(Collision _) => lastGroundContact = Time.fixedTime;

    /// <summary>Teleport the drone and stop all motion (used for respawn).</summary>
    public void ResetTo(Vector3 position, Quaternion rotation)
    {
        rb.SetVelocity(Vector3.zero);
        rb.angularVelocity = Vector3.zero;
        rb.position = position;
        rb.rotation = rotation;
        transform.SetPositionAndRotation(position, rotation);
        for (int i = 0; i < 4; i++)
        {
            MotorCommand[i] = 0f;
            MotorOmega[i] = 0f;
            MotorThrust[i] = 0f;
        }
        lastGroundContact = -10f;
    }
}
