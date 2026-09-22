using UnityEngine;

/// <summary>
/// Makes the on-board camera look like a real FPV feed: wide fisheye lens,
/// grain, vignette, video-link latency (frames are shown ~30 ms late) and
/// analog breakup that grows with distance from the pilot and when buildings
/// block the line of sight. Also adds throttle-dependent vibration ("jello").
/// </summary>
[RequireComponent(typeof(Camera))]
public class FpvCameraEffects : MonoBehaviour
{
    [Header("Lens")]
    public Shader shader;
    public float distortion = 0.35f;
    [Tooltip("Vertical field of view of the final (distorted) image, degrees. A 2.1 mm FPV lens is ~95-100.")]
    public float effectiveVerticalFov = 96f;
    public float chromatic = 0.004f;
    public float vignette = 0.35f;
    float zoom = 1.3f;
    Camera cam;

    [Header("Video link")]
    public bool analogLook = true;
    [Range(0f, 0.3f)] public float baseNoise = 0.06f;
    [Range(0f, 1f)] public float scanlines = 0.12f;
    public float latencyMs = 28f;
    [Tooltip("Pilot position used for range/occlusion (the line-of-sight camera)")]
    public Transform pilot;
    public float rangeFullSignal = 150f;
    public float rangeNoSignal = 450f;

    [Header("Vibration")]
    public DronePhysics drone;
    public float jelloDegrees = 0.12f;

    const int MaxRingFrames = 12;
    Material mat;
    RenderTexture[] ring;
    float[] stamps;
    int head;
    float occlusion;      // 0..1 smoothed
    float breakupFlicker;
    Quaternion baseLocalRotation;

    void Start()
    {
        cam = GetComponent<Camera>();
        if (shader == null) shader = Shader.Find("DroneTrainer/FpvAnalog");
        if (shader != null) mat = new Material(shader);
        baseLocalRotation = transform.localRotation;
    }

    void OnPreCull()
    {
        // The barrel distortion samples outside the frame at the corners; zoom just enough that the
        // corners stay filled for this aspect ratio, and widen the render FOV so the visible FOV matches.
        float a = cam.aspect;
        float r2 = 0.25f * a * a + 0.25f;
        zoom = 1f + distortion * r2 + distortion * 0.5f * r2 * r2;
        float half = Mathf.Tan(effectiveVerticalFov * 0.5f * Mathf.Deg2Rad) * zoom;
        cam.fieldOfView = Mathf.Clamp(2f * Mathf.Atan(half) * Mathf.Rad2Deg, 30f, 170f);
    }

    void OnDestroy()
    {
        ReleaseRing();
        if (mat != null) Destroy(mat);
    }

    void ReleaseRing()
    {
        if (ring == null) return;
        foreach (var rt in ring) if (rt != null) rt.Release();
        ring = null;
    }

    void LateUpdate()
    {
        if (drone == null) return;
        float thr = 0f;
        for (int i = 0; i < 4; i++) thr += drone.MotorOmega[i];
        thr /= 4f * Mathf.Max(1f, drone.OmegaMaxAtFullBattery);
        float amp = jelloDegrees * thr * thr * 4f;
        float t = Time.time;
        var jitter = Quaternion.Euler(
            Mathf.Sin(t * 173f) * amp,
            Mathf.Sin(t * 211f + 1f) * amp * 0.6f,
            Mathf.Sin(t * 149f + 2f) * amp);
        transform.localRotation = baseLocalRotation * jitter;
    }

    void OnRenderImage(RenderTexture src, RenderTexture dst)
    {
        if (mat == null)
        {
            Graphics.Blit(src, dst);
            return;
        }

        // --- Latency: keep a fixed-size ring of past frames and show the one that is ~latency old.
        // The ring is sized once to the max clamp and only reallocated on an actual resolution
        // change - resizing it every time the frame-count estimate jitters across an integer
        // boundary (which happens constantly under normal frame-time noise) would tear down and
        // rebuild every RenderTexture and invalidate every stamp, momentarily collapsing the
        // simulated latency to ~0 right when it just reset. LDR (ARGB32) is plenty since the
        // analog pass immediately desaturates/degrades the image anyway.
        if (ring == null || ring[0].width != src.width || ring[0].height != src.height)
        {
            ReleaseRing();
            ring = new RenderTexture[MaxRingFrames];
            stamps = new float[MaxRingFrames];
            for (int i = 0; i < MaxRingFrames; i++)
            {
                ring[i] = new RenderTexture(src.width, src.height, 0, RenderTextureFormat.ARGB32);
                ring[i].Create();
                stamps[i] = -1f;
            }
            head = 0;
        }
        int frames = Mathf.Clamp(Mathf.CeilToInt(latencyMs / 1000f / Mathf.Max(1f / 240f, Time.unscaledDeltaTime)) + 1, 1, MaxRingFrames);
        Graphics.Blit(src, ring[head]);
        stamps[head] = Time.unscaledTime;

        float want = Time.unscaledTime - latencyMs / 1000f;
        int pick = head;
        for (int k = 0; k < frames; k++)
        {
            int idx = (head - k + MaxRingFrames) % MaxRingFrames;
            if (stamps[idx] < 0f) break;
            pick = idx;
            if (stamps[idx] <= want) break;
        }
        head = (head + 1) % MaxRingFrames;

        // --- Signal quality from range + occlusion.
        float breakup = 0f, noise = baseNoise;
        if (analogLook && pilot != null)
        {
            float d = Vector3.Distance(pilot.position, transform.position);
            float range = Mathf.Clamp01((d - rangeFullSignal) / Mathf.Max(1f, rangeNoSignal - rangeFullSignal));
            bool blocked = Physics.Linecast(pilot.position, transform.position, out var hit, ~0, QueryTriggerInteraction.Ignore)
                           && hit.transform.root != transform.root;
            occlusion = Mathf.MoveTowards(occlusion, blocked ? 1f : 0f, Time.unscaledDeltaTime * (blocked ? 1.5f : 3f));
            float quality = Mathf.Clamp01(1f - range) * (1f - 0.7f * occlusion);
            breakupFlicker = Mathf.Lerp(breakupFlicker, Random.value, 0.3f);
            breakup = Mathf.Clamp01((1f - quality) * (0.6f + 0.6f * breakupFlicker));
            noise += (1f - quality) * 0.35f;
        }

        mat.SetFloat("_Distortion", distortion);
        mat.SetFloat("_Zoom", zoom);
        mat.SetFloat("_Chroma", chromatic);
        mat.SetFloat("_Vignette", vignette);
        mat.SetFloat("_Noise", analogLook ? noise : 0f);
        mat.SetFloat("_Breakup", breakup);
        mat.SetFloat("_Scanline", analogLook ? scanlines : 0f);
        mat.SetFloat("_Seed", Time.unscaledTime % 100f);
        Graphics.Blit(ring[pick], dst, mat);
    }
}
