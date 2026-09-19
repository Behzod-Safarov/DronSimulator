using UnityEngine;

/// <summary>Synthesised impact thumps, prop-strike rattle and arm/disarm beeps.</summary>
[RequireComponent(typeof(AudioSource))]
public class ImpactAudio : MonoBehaviour
{
    public CrashDetector crashDetector;
    public FlightController flightController;
    [Range(0f, 1f)] public float volume = 0.9f;

    AudioSource source;
    AudioClip thump, rattle, beepHigh, beepLow;
    bool lastArmed;

    void Awake()
    {
        source = GetComponent<AudioSource>();
        source.spatialBlend = 1f;
        source.minDistance = 2f;
        source.maxDistance = 300f;
        source.playOnAwake = false;

        int sr = AudioSettings.outputSampleRate;
        thump = Synth("Thump", sr, 0.35f, (t, r) => (Mathf.Sin(2f * Mathf.PI * 70f * t * (1f + 4f * Mathf.Exp(-t * 30f))) * 0.8f + r * 0.6f) * Mathf.Exp(-t * 14f));
        rattle = Synth("Rattle", sr, 0.6f, (t, r) => r * Mathf.Exp(-t * 6f) * (0.5f + 0.5f * Mathf.Abs(Mathf.Sin(2f * Mathf.PI * 38f * t))));
        beepHigh = Synth("BeepHigh", sr, 0.09f, (t, r) => Mathf.Sin(2f * Mathf.PI * 1800f * t) * 0.5f * (t < 0.08f ? 1f : 0f));
        beepLow = Synth("BeepLow", sr, 0.16f, (t, r) => Mathf.Sin(2f * Mathf.PI * 900f * t) * 0.5f * (t < 0.15f ? 1f : 0f));
    }

    void OnEnable()
    {
        if (crashDetector != null) crashDetector.OnImpact += OnImpact;
    }

    void OnDisable()
    {
        if (crashDetector != null) crashDetector.OnImpact -= OnImpact;
    }

    void Update()
    {
        if (flightController == null) return;
        if (flightController.Armed != lastArmed)
        {
            lastArmed = flightController.Armed;
            source.pitch = 1f;
            source.PlayOneShot(lastArmed ? beepHigh : beepLow, volume * 0.6f);
        }
    }

    void OnImpact(float speed)
    {
        if (speed < 0.6f) return;
        float v = Mathf.Clamp01(speed / 8f);
        source.pitch = Random.Range(0.85f, 1.15f);
        source.PlayOneShot(thump, volume * v);
        if (speed > 3f) source.PlayOneShot(rattle, volume * v * 0.8f);
    }

    static AudioClip Synth(string name, int sr, float seconds, System.Func<float, float, float> f)
    {
        int n = Mathf.CeilToInt(sr * seconds);
        var data = new float[n];
        var rng = new System.Random(name.GetHashCode());
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)sr;
            float r = (float)(rng.NextDouble() * 2.0 - 1.0);
            data[i] = Mathf.Clamp(f(t, r), -1f, 1f);
        }
        var clip = AudioClip.Create(name, n, 1, sr, false);
        clip.SetData(data, 0);
        return clip;
    }
}
