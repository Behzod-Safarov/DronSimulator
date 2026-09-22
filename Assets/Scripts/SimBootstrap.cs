using UnityEngine;

/// <summary>Global simulation settings applied at scene start.</summary>
public class SimBootstrap : MonoBehaviour
{
    [Tooltip("Physics/controller loop rate. 250 Hz keeps the rate PIDs crisp.")]
    public int physicsHz = 250;
    public bool vSync = true;
    [Tooltip("420m covers farm buildings (~260m) and the far tree-line ring (~330-420m); the persisted Ultra quality preset alone only reaches 150m.")]
    public float shadowDistance = 420f;

    void Awake()
    {
        Time.fixedDeltaTime = 1f / Mathf.Max(50, physicsHz);
        Time.timeScale = 1f;
        Application.runInBackground = true;
        QualitySettings.vSyncCount = vSync ? 1 : 0;
        Application.targetFrameRate = vSync ? -1 : 240;
        QualitySettings.shadowDistance = shadowDistance;
        QualitySettings.shadowCascades = 4;
        QualitySettings.shadowResolution = ShadowResolution.VeryHigh;
        QualitySettings.antiAliasing = 4;
        QualitySettings.anisotropicFiltering = AnisotropicFiltering.ForceEnable;
        QualitySettings.lodBias = 2f;
    }

    void Start()
    {
        // Ambient light and reflections come from the HDRI skybox; refresh the probes once at start.
        DynamicGI.UpdateEnvironment();
    }
}
