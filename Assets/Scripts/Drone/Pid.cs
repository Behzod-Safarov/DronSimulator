using UnityEngine;

/// <summary>
/// PID with anti-windup and a low-pass filtered derivative computed on the
/// measurement (not the error) so setpoint steps don't kick the D term — the
/// same structure real flight controllers use. Output is clamped to [-1, 1].
/// </summary>
[System.Serializable]
public class Pid
{
    public float p;
    public float i;
    public float d;
    [Tooltip("Feedforward: adds ff * d(setpoint)/dt straight through (not filtered), like Betaflight FF - the main thing that makes fast flicks/stops feel snappy instead of lagged")]
    public float ff;
    [Tooltip("Absolute clamp on the integral contribution")]
    public float iLimit = 0.3f;
    [Tooltip("Cut-off frequency of the derivative low-pass filter")]
    public float dFilterHz = 80f;

    float integral;
    float lastMeasurement;
    float dFiltered;
    bool first = true;
    float lastSetpoint;
    bool firstSetpoint = true;

    public Pid(float p, float i, float d, float ff = 0f)
    {
        this.p = p;
        this.i = i;
        this.d = d;
        this.ff = ff;
    }

    /// <summary>gainScale multiplies P and D only (not I or FF) - used for throttle PID attenuation (TPA).</summary>
    public float Update(float setpoint, float measurement, float dt, float gainScale = 1f)
    {
        float error = setpoint - measurement;
        integral = Mathf.Clamp(integral + error * i * dt, -iLimit, iLimit);

        float derivative = first ? 0f : -(measurement - lastMeasurement) / dt;
        first = false;
        lastMeasurement = measurement;

        float alpha = dt / (dt + 1f / (2f * Mathf.PI * Mathf.Max(1f, dFilterHz)));
        dFiltered += (derivative - dFiltered) * alpha;

        float feedforward = firstSetpoint ? 0f : ff * (setpoint - lastSetpoint) / dt;
        firstSetpoint = false;
        lastSetpoint = setpoint;

        return Mathf.Clamp(error * p * gainScale + integral + dFiltered * d * gainScale + feedforward, -1f, 1f);
    }

    /// <summary>Clears only the integral (anti-windup) - use this to stop I building up without
    /// also wiping the D filter/feedforward state, which Reset() does.</summary>
    public void ResetIntegral() => integral = 0f;

    public void Reset()
    {
        integral = 0f;
        lastMeasurement = 0f;
        dFiltered = 0f;
        first = true;
        lastSetpoint = 0f;
        firstSetpoint = true;
    }
}
