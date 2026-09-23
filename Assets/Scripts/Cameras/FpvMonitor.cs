using UnityEngine;

/// <summary>
/// Picture-in-picture second view, like the monitor a spotter watches while the pilot flies
/// line-of-sight. Whichever camera is NOT the main view renders into a RenderTexture and
/// <see cref="Osd"/> draws it in a corner; clicking the inset swaps it with the main view.
/// Toggle with V (remembered between sessions).
/// </summary>
public class FpvMonitor : MonoBehaviour
{
    public CameraSwitcher cameras;
    public PilotInput input;
    [Tooltip("Which camera the inset shows (swapped with the main view when you click it)")]
    public int monitorIndex = 2;
    [Range(160, 960)] public int width = 480;

    public bool Enabled { get; private set; } = true;
    /// <summary>The live feed, or null when the FPV camera is the full-screen view (nothing to inset).</summary>
    public RenderTexture Feed => showing ? feed : null;

    RenderTexture feed;
    bool showing;
    Camera rendering;

    Camera MonitorCamera => cameras != null && monitorIndex >= 0 && monitorIndex < cameras.cameras.Length ? cameras.cameras[monitorIndex] : null;

    /// <summary>Name of the view currently in the inset.</summary>
    public string MonitorName => cameras != null && monitorIndex < cameras.names.Length ? cameras.names[monitorIndex] : "";

    /// <summary>Swap the inset with the main view - the point of clicking it.</summary>
    public void SwapWithMain()
    {
        if (cameras == null) return;
        int wasMain = cameras.current;
        Release();
        cameras.Select(monitorIndex);
        monitorIndex = wasMain;
    }

    void Start()
    {
        Enabled = PlayerPrefs.GetInt("fpv.monitor", 1) == 1;
        monitorIndex = PlayerPrefs.GetInt("fpv.monitorIndex", monitorIndex);
    }

    void OnDestroy()
    {
        if (feed != null) { feed.Release(); Destroy(feed); }
    }

    public void Toggle()
    {
        Enabled = !Enabled;
        PlayerPrefs.SetInt("fpv.monitor", Enabled ? 1 : 0);
        PlayerPrefs.SetInt("fpv.monitorIndex", monitorIndex);
        PlayerPrefs.Save();
    }

    /// <summary>Stop driving whatever camera we were rendering into the inset.</summary>
    void Release()
    {
        if (rendering == null) return;
        if (rendering.targetTexture == feed) rendering.targetTexture = null;
        if (cameras == null || cameras.current != System.Array.IndexOf(cameras.cameras, rendering)) rendering.enabled = false;
        rendering = null;
        showing = false;
    }

    void LateUpdate()
    {
        if (input != null && input.MonitorPressed) Toggle();

        // If the main view has taken over the camera we were insetting, show a different one
        // rather than going blank: the drone camera if that is free, otherwise the chase.
        if (cameras != null && cameras.current == monitorIndex && cameras.cameras.Length > 1)
        {
            monitorIndex = cameras.current == 2 ? 0 : 2;
            Release();
        }

        var cam = MonitorCamera;
        bool isMain = cameras != null && cameras.current == monitorIndex;
        bool want = Enabled && cam != null && !isMain && (cameras == null || !cameras.suppressed);

        if (!want) { Release(); return; }

        if (feed == null)
        {
            feed = new RenderTexture(width, Mathf.RoundToInt(width * 9f / 16f), 24, RenderTextureFormat.DefaultHDR)
            {
                name = "View feed",
                antiAliasing = 1,
                filterMode = FilterMode.Bilinear,
            };
            feed.Create();
        }
        if (rendering != null && rendering != cam) Release();

        // Render to the inset without stealing the screen or the audio listener.
        cam.targetTexture = feed;
        cam.enabled = true;
        rendering = cam;
        showing = true;
    }
}
