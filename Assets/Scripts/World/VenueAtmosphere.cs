using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Gives every venue its own weather and time of day: sun angle/colour/intensity,
/// ambient level, fog colour/density and sky exposure/rotation. LessonManager calls
/// <see cref="Apply"/> with the drill's venue name; the change is eased in so a
/// mid-session switch doesn't snap.
/// </summary>
public class VenueAtmosphere : MonoBehaviour
{
    [System.Serializable]
    public class Preset
    {
        public string venue = "Pit field";
        public Vector3 sunEuler = new Vector3(48f, -60f, 0f);
        public Color sunColor = new Color(1f, 0.95f, 0.88f);
        public float sunIntensity = 1.6f;
        public float ambient = 1f;
        public Color fogColor = new Color(0.72f, 0.78f, 0.86f);
        public float fogDensity = 0.0012f;
        public float skyExposure = 1f;
        public float skyRotation;
        public Color skyTint = Color.white;
    }

    public Preset[] presets;
    public float blendSeconds = 1.2f;
    public Light sun;
    public Material sky;

    readonly Dictionary<string, Preset> byVenue = new Dictionary<string, Preset>();
    Preset from, to;
    float t = 1f;

    void Awake()
    {
        if (presets == null) return;
        foreach (var p in presets) byVenue[p.venue] = p;
        if (presets.Length > 0) { from = to = presets[0]; t = 1f; Write(to); }
    }

    /// <summary>Switch to a venue's atmosphere (unknown names fall back to the first preset).</summary>
    public void Apply(string venue, bool instant = false)
    {
        if (presets == null || presets.Length == 0) return;
        var next = byVenue.TryGetValue(venue ?? "", out var p) ? p : presets[0];
        if (next == to && t >= 1f) return;
        from = Snapshot();
        to = next;
        t = instant ? 1f : 0f;
        if (instant) Write(to);
    }

    void Update()
    {
        if (t >= 1f || to == null) return;
        t = Mathf.Min(1f, t + Time.unscaledDeltaTime / Mathf.Max(0.01f, blendSeconds));
        Write(Lerp(from, to, Mathf.SmoothStep(0f, 1f, t)));
    }

    Preset Snapshot()
    {
        var s = new Preset();
        if (sun != null) { s.sunEuler = sun.transform.rotation.eulerAngles; s.sunColor = sun.color; s.sunIntensity = sun.intensity; }
        s.ambient = RenderSettings.ambientIntensity;
        s.fogColor = RenderSettings.fogColor;
        s.fogDensity = RenderSettings.fogDensity;
        if (sky != null)
        {
            if (sky.HasProperty("_Exposure")) s.skyExposure = sky.GetFloat("_Exposure");
            if (sky.HasProperty("_Rotation")) s.skyRotation = sky.GetFloat("_Rotation");
            if (sky.HasProperty("_Tint")) s.skyTint = sky.GetColor("_Tint");
        }
        return s;
    }

    static Preset Lerp(Preset a, Preset b, float k) => new Preset
    {
        sunEuler = new Vector3(Mathf.LerpAngle(a.sunEuler.x, b.sunEuler.x, k), Mathf.LerpAngle(a.sunEuler.y, b.sunEuler.y, k), Mathf.LerpAngle(a.sunEuler.z, b.sunEuler.z, k)),
        sunColor = Color.Lerp(a.sunColor, b.sunColor, k),
        sunIntensity = Mathf.Lerp(a.sunIntensity, b.sunIntensity, k),
        ambient = Mathf.Lerp(a.ambient, b.ambient, k),
        fogColor = Color.Lerp(a.fogColor, b.fogColor, k),
        fogDensity = Mathf.Lerp(a.fogDensity, b.fogDensity, k),
        skyExposure = Mathf.Lerp(a.skyExposure, b.skyExposure, k),
        skyRotation = Mathf.LerpAngle(a.skyRotation, b.skyRotation, k),
        skyTint = Color.Lerp(a.skyTint, b.skyTint, k),
    };

    void Write(Preset p)
    {
        if (sun != null)
        {
            sun.transform.rotation = Quaternion.Euler(p.sunEuler);
            sun.color = p.sunColor;
            sun.intensity = p.sunIntensity;
        }
        RenderSettings.ambientIntensity = p.ambient;
        RenderSettings.fogColor = p.fogColor;
        RenderSettings.fogDensity = p.fogDensity;
        if (sky != null)
        {
            if (sky.HasProperty("_Exposure")) sky.SetFloat("_Exposure", p.skyExposure);
            if (sky.HasProperty("_Rotation")) sky.SetFloat("_Rotation", p.skyRotation);
            if (sky.HasProperty("_Tint")) sky.SetColor("_Tint", p.skyTint);
        }
    }
}
