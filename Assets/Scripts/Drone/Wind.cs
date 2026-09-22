using UnityEngine;

/// <summary>
/// Global wind field: a steady wind, slow Perlin gusts and fast small-scale turbulence.
/// Anything aerodynamic asks <see cref="VelocityAt"/> for the local air velocity.
/// </summary>
public class Wind : MonoBehaviour
{
    public static Wind Instance { get; private set; }

    [Tooltip("Mean wind speed in m/s (0 = calm)")]
    public float meanSpeed = 2.5f;
    [Tooltip("Direction the wind blows TOWARDS, degrees compass (0 = +Z)")]
    public float directionDeg = 30f;
    [Tooltip("Gust strength relative to the mean (0.5 = gusts from 50% to 150%)")]
    [Range(0f, 1f)] public float gustiness = 0.5f;
    public float gustPeriodSeconds = 8f;
    [Tooltip("Fast small-scale turbulence in m/s")]
    public float turbulence = 0.6f;
    [Tooltip("Wind speed multiplier near the ground (boundary layer)")]
    public float groundShearHeight = 3f;

    float seed;

    void Awake()
    {
        Instance = this;
        seed = Random.Range(0f, 1000f);
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public Vector3 MeanVector
    {
        get
        {
            float a = directionDeg * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * meanSpeed;
        }
    }

    public Vector3 VelocityAt(Vector3 position, float time, float heightAboveGround)
    {
        if (meanSpeed <= 0f && turbulence <= 0f) return Vector3.zero;

        float t = time / Mathf.Max(0.1f, gustPeriodSeconds);
        float gust = 1f + gustiness * (Mathf.PerlinNoise(seed + t, seed * 0.5f + position.x * 0.01f) * 2f - 1f);
        Vector3 v = MeanVector * gust;

        // Small-scale turbulence changes with position and time.
        float tt = time * 1.7f;
        float px = position.x * 0.2f, pz = position.z * 0.2f;
        v.x += (Mathf.PerlinNoise(seed + px + tt, pz) - 0.5f) * 2f * turbulence;
        v.y += (Mathf.PerlinNoise(seed + 11f + px, pz + tt) - 0.5f) * 2f * turbulence * 0.5f;
        v.z += (Mathf.PerlinNoise(seed + 23f + pz + tt, px) - 0.5f) * 2f * turbulence;

        // Boundary layer: calmer close to the ground - height above the actual terrain, not raw
        // world Y, so this stays correct on the project's hilly, non-flat ground (full-strength
        // wind at a hilltop's ground level, calm wind low over a valley, not the other way round).
        float shear = Mathf.Clamp01(heightAboveGround / Mathf.Max(0.1f, groundShearHeight));
        return v * Mathf.Lerp(0.3f, 1f, shear);
    }

    public static Vector3 Sample(Vector3 position, float time, float heightAboveGround)
    {
        return Instance != null ? Instance.VelocityAt(position, time, heightAboveGround) : Vector3.zero;
    }
}
