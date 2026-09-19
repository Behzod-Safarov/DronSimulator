using UnityEngine;

/// <summary>
/// Procedural quad sound. Each motor contributes a blade-pass harmonic series
/// (RPM × blades), a higher electrical whine (RPM × pole pairs) and RPM-shaped
/// turbulence noise. Because the four motors never run at exactly the same speed,
/// the mix beats and "talks" the way a real quad does. Rendered through a 3D
/// AudioSource so the line-of-sight view gets distance attenuation and Doppler.
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class DroneAudio : MonoBehaviour
{
    public DronePhysics drone;
    [Range(0f, 1f)] public float volume = 0.8f;
    public int polePairs = 7;
    public float bladeGain = 1.0f;
    public float whineGain = 0.25f;
    public float noiseGain = 0.55f;

    AudioSource source;
    int sampleRate;
    float omegaMax;

    // Written on the main thread, read on the audio thread (atomic float writes).
    readonly float[] omega = new float[4];
    readonly float[] detune = { 1.000f, 1.004f, 0.997f, 1.002f };

    readonly double[] bladePhase = new double[4];
    readonly double[] whinePhase = new double[4];
    readonly float[] noiseState = new float[4];
    readonly float[] omegaSmooth = new float[4];
    System.Random rng = new System.Random(7);

    void Awake()
    {
        source = GetComponent<AudioSource>();
        sampleRate = AudioSettings.outputSampleRate;
        omegaMax = drone != null ? drone.OmegaMaxAtFullBattery : 3000f;

        var clip = AudioClip.Create("MotorSynth", sampleRate, 1, sampleRate, true, OnRead, OnSetPosition);
        source.clip = clip;
        source.loop = true;
        source.spatialBlend = 1f;
        source.dopplerLevel = 1f;
        source.rolloffMode = AudioRolloffMode.Logarithmic;
        source.minDistance = 1.5f;
        source.maxDistance = 400f;
        source.volume = volume;
        source.playOnAwake = false;
        source.Play();
    }

    void Update()
    {
        if (drone == null) return;
        for (int i = 0; i < 4; i++) omega[i] = drone.MotorOmega[i];
        source.volume = volume;
    }

    void OnSetPosition(int _) { }

    void OnRead(float[] data)
    {
        float dt = 1f / sampleRate;
        float smooth = 1f - Mathf.Exp(-dt * 40f); // 40 Hz tracking of RPM changes
        for (int n = 0; n < data.Length; n++)
        {
            float mix = 0f;
            for (int m = 0; m < 4; m++)
            {
                omegaSmooth[m] += (omega[m] - omegaSmooth[m]) * smooth;
                float w = omegaSmooth[m];
                if (w < 20f) continue;

                float norm = w / omegaMax;
                float loud = Mathf.Pow(norm, 2.2f);

                float bladeHz = w / (2f * Mathf.PI) * (drone != null ? drone.propBlades : 3) * detune[m];
                bladePhase[m] += bladeHz * dt;
                if (bladePhase[m] > 1e6) bladePhase[m] -= 1e6;
                float ph = (float)(bladePhase[m] * 2.0 * Mathf.PI);
                float blade = Mathf.Sin(ph) + 0.55f * Mathf.Sin(2f * ph) + 0.35f * Mathf.Sin(3f * ph) + 0.2f * Mathf.Sin(4f * ph) + 0.12f * Mathf.Sin(5f * ph);

                float whineHz = w / (2f * Mathf.PI) * polePairs * detune[m];
                whinePhase[m] += whineHz * dt;
                if (whinePhase[m] > 1e6) whinePhase[m] -= 1e6;
                float whine = Mathf.Sin((float)(whinePhase[m] * 2.0 * Mathf.PI));

                // RPM-shaped noise: white noise through a one-pole low-pass whose cut-off rises with RPM.
                float white = (float)(rng.NextDouble() * 2.0 - 1.0);
                float cut = 1f - Mathf.Exp(-dt * 2f * Mathf.PI * (250f + 3500f * norm));
                noiseState[m] += (white - noiseState[m]) * cut;

                mix += loud * (bladeGain * blade * 0.35f + whineGain * whine + noiseGain * noiseState[m] * 2.5f);
            }
            mix *= 0.28f;
            data[n] = (float)System.Math.Tanh(mix); // soft clip
        }
    }
}
