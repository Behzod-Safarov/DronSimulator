using UnityEngine;

/// <summary>
/// Airflow noise heard by the on-board FPV microphone, driven by airspeed.
/// Attached to the FPV camera; silent while another camera is active.
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class WindAudio : MonoBehaviour
{
    public DronePhysics drone;
    public Camera fpvCamera;
    [Range(0f, 1f)] public float volume = 0.6f;
    public float fullVolumeSpeed = 28f;

    AudioSource source;
    int sampleRate;
    float airspeed;      // main -> audio thread
    float lp1, lp2;
    readonly System.Random rng = new System.Random(3);

    void Awake()
    {
        source = GetComponent<AudioSource>();
        sampleRate = AudioSettings.outputSampleRate;
        var clip = AudioClip.Create("WindSynth", sampleRate, 1, sampleRate, true, OnRead, _ => { });
        source.clip = clip;
        source.loop = true;
        source.spatialBlend = 0f;
        source.playOnAwake = false;
        source.Play();
    }

    void Update()
    {
        bool active = fpvCamera == null || fpvCamera.enabled;
        airspeed = drone != null && active ? drone.AirVelocity.magnitude : 0f;
        source.volume = volume;
    }

    void OnRead(float[] data)
    {
        float dt = 1f / sampleRate;
        float norm = Mathf.Clamp01(airspeed / fullVolumeSpeed);
        float loud = Mathf.Pow(norm, 1.6f);
        float cut = 1f - Mathf.Exp(-dt * 2f * Mathf.PI * (120f + 900f * norm));
        for (int n = 0; n < data.Length; n++)
        {
            float white = (float)(rng.NextDouble() * 2.0 - 1.0);
            lp1 += (white - lp1) * cut;
            lp2 += (lp1 - lp2) * cut;
            data[n] = lp2 * loud * 3.5f;
        }
    }
}
