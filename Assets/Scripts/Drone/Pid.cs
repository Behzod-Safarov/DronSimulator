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
    [Tooltip("Absolute clamp on the integral contribution")]
    public float iLimit = 0.3f;
    [Tooltip("Cut-off frequency of the derivative low-pass filter")]
    public float dFilterHz = 80f;

    float integral;
    float lastMeasurement;
    float dFiltered;
    bool first = true;

    public Pid(float p, float i, float d)
    {
        this.p = p;
        this.i = i;
        this.d = d;
    }

    public float Update(float setpoint, float measurement, float dt)
    {
        float error = setpoint - measurement;
        integral = Mathf.Clamp(integral + error * i * dt, -iLimit, iLimit);

        float derivative = first ? 0f : -(measurement - lastMeasurement) / dt;
        first = false;
        lastMeasurement = measurement;

        float alpha = dt / (dt + 1f / (2f * Mathf.PI * Mathf.Max(1f, dFilterHz)));
        dFiltered += (derivative - dFiltered) * alpha;

        return Mathf.Clamp(error * p + integral + dFiltered * d, -1f, 1f);
    }

    public void Reset()
    {
        integral = 0f;
        lastMeasurement = 0f;
        dFiltered = 0f;
        first = true;
    }
}
