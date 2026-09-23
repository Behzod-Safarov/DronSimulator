using UnityEngine;

/// <summary>
/// Publishes the live wind to the renderer so the world reacts to the same air the drone is
/// flying in. Before this existed, <see cref="Wind"/> fed only <see cref="DronePhysics"/> and the
/// OSD: the pilot could dial the wind to 12 m/s in the settings panel and every tree, flag and
/// blade of grass stayed perfectly still, which is the single loudest tell that the scene is fake.
///
/// Two jobs:
///  - global shader vectors read by DroneTrainer/FoliageSway (every tree crown)
///  - Unity's built-in terrain grass waving, which is otherwise a constant baked at build time
///
/// Unity's waving-grass shader has no direction input - only amount/speed/strength - so the grass
/// can follow the wind's *strength* but not its heading. The foliage and the windsocks carry the
/// direction cue instead.
/// </summary>
public class WindVisuals : MonoBehaviour
{
    public Wind wind;
    public Terrain terrain;

    [Tooltip("Wind speed (m/s) at which foliage is fully bent over. ~12 m/s is a strong breeze.")]
    public float fullEffectSpeed = 12f;
    [Tooltip("Crown deflection in metres at full strength")]
    public float maxBendMetres = 0.55f;
    [Tooltip("Height above ground the visual wind is sampled at (so gusts match what the drone feels)")]
    public float sampleHeight = 3f;

    [Header("Terrain grass")]
    public bool driveTerrainGrass = true;
    public float grassStrengthCalm = 0.08f;
    public float grassStrengthFull = 0.85f;
    public float grassSpeedCalm = 0.25f;
    public float grassSpeedFull = 1.4f;

    static readonly int WindDirId = Shader.PropertyToID("_DtWindDir");
    static readonly int WindParamsId = Shader.PropertyToID("_DtWindParams");

    /// <summary>Smoothed wind vector the visuals are currently showing (also used by the OSD arrow).</summary>
    public Vector3 SmoothedWind { get; private set; }
    public float Strength01 { get; private set; }

    Vector3 shownDir = Vector3.forward;
    float lastGrassWrite = -1f;

    void Start()
    {
        if (wind == null) wind = Wind.Instance;
        // Publish once immediately so nothing renders a frame with an unset (zero) wind direction,
        // which would collapse every crown to its rest pose and then pop.
        Publish(Vector3.forward, 0f, 0f);
    }

    void Update()
    {
        if (wind == null) wind = Wind.Instance;
        Vector3 v = wind != null ? wind.VelocityAt(transform.position, Time.time, sampleHeight) : Vector3.zero;
        v.y = 0f;   // foliage responds to the horizontal flow

        // Ease the shown wind: the raw field has fast turbulence in it, and a whole stand of trees
        // snapping between turbulence samples looks like a glitch rather than a gust.
        SmoothedWind = Vector3.Lerp(SmoothedWind, v, 1f - Mathf.Exp(-Time.deltaTime * 1.5f));
        float speed = SmoothedWind.magnitude;
        if (speed > 0.01f) shownDir = Vector3.Lerp(shownDir, SmoothedWind / speed, 1f - Mathf.Exp(-Time.deltaTime * 2f)).normalized;

        Strength01 = Mathf.Clamp01(speed / Mathf.Max(0.01f, fullEffectSpeed));
        // Gust term: how far above the mean this moment is, which is what makes a stand "breathe".
        float mean = wind != null ? wind.meanSpeed : 0f;
        float gust = Mathf.Clamp01((speed - mean) / Mathf.Max(0.5f, mean * 0.6f + 0.5f));

        Publish(shownDir, Strength01, gust);

        if (driveTerrainGrass && terrain != null && terrain.terrainData != null)
        {
            // Only write when it actually moved: these are fields on a TerrainData *asset*, and
            // assigning them every frame dirties the asset and re-pushes grass state needlessly.
            if (Mathf.Abs(Strength01 - lastGrassWrite) > 0.02f)
            {
                lastGrassWrite = Strength01;
                var d = terrain.terrainData;
                d.wavingGrassStrength = Mathf.Lerp(grassStrengthCalm, grassStrengthFull, Strength01);
                d.wavingGrassSpeed = Mathf.Lerp(grassSpeedCalm, grassSpeedFull, Strength01);
                d.wavingGrassAmount = Mathf.Lerp(0.15f, 0.6f, Strength01);
            }
        }
    }

    void Publish(Vector3 dir, float strength, float gust)
    {
        Shader.SetGlobalVector(WindDirId, new Vector4(dir.x, dir.y, dir.z, strength));
        // x = bend metres, y = sway Hz, z = flutter Hz, w = gust. Frequencies rise with the wind:
        // a calm tree has a slow 0.35 Hz nod, a windy one a fast, tighter thrash.
        Shader.SetGlobalVector(WindParamsId, new Vector4(
            maxBendMetres,
            Mathf.Lerp(0.35f, 1.1f, strength),
            Mathf.Lerp(1.1f, 3.2f, strength),
            gust));
    }
}
