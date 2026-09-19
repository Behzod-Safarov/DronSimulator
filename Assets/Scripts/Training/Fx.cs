using UnityEngine;

/// <summary>One-shot game effects: particle bursts, ground splats and synthesised sounds.</summary>
public static class Fx
{
    static Material particleMat;
    static AudioClip pop, splash, ding, boom;

    static Material ParticleMaterial()
    {
        if (particleMat == null)
        {
            var shader = Shader.Find("Particles/Standard Unlit") ?? Shader.Find("Legacy Shaders/Particles/Alpha Blended");
            particleMat = new Material(shader);
        }
        return particleMat;
    }

    /// <summary>Spawns a burst of small particles at a point and destroys it afterwards.</summary>
    public static void Burst(Vector3 pos, Color color, int count = 60, float speed = 6f, float size = 0.06f, float life = 1.2f, bool gravity = true)
    {
        var go = new GameObject("Burst");
        go.transform.position = pos;
        var ps = go.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.startColor = color;
        main.startSize = size;
        main.startSpeed = speed;
        main.startLifetime = life;
        main.gravityModifier = gravity ? 1f : 0f;
        main.loop = false;
        main.playOnAwake = false;
        main.maxParticles = count;
        var emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.05f;
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(new[] { new GradientColorKey(color, 0f), new GradientColorKey(color, 1f) },
                     new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
        col.color = grad;
        ps.GetComponent<ParticleSystemRenderer>().material = ParticleMaterial();
        ps.Play();
        Object.Destroy(go, life + 0.5f);
    }

    /// <summary>A flat dark disc on the ground (wet spot) that fades away.</summary>
    public static void Splat(Vector3 pos, float radius, Color color, float seconds = 25f)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = "Splat";
        Object.Destroy(go.GetComponent<Collider>());
        go.transform.position = pos + Vector3.up * 0.015f;
        go.transform.localScale = new Vector3(radius * 2f, 0.002f, radius * 2f);
        var mr = go.GetComponent<Renderer>();
        var m = new Material(Shader.Find("Standard"));
        m.SetFloat("_Mode", 2f);
        m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        m.SetInt("_ZWrite", 0);
        m.EnableKeyword("_ALPHABLEND_ON");
        m.renderQueue = 3000;
        m.color = color;
        mr.material = m;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        go.AddComponent<FadeAndDie>().seconds = seconds;
    }

    public static void PlayPop(Vector3 pos) => AudioSource.PlayClipAtPoint(pop ??= Synth("Pop", 0.25f, (t, r) => (r * 0.9f + Mathf.Sin(2f * Mathf.PI * 420f * t) * 0.3f) * Mathf.Exp(-t * 28f)), pos, 0.9f);
    public static void PlaySplash(Vector3 pos) => AudioSource.PlayClipAtPoint(splash ??= Synth("Splash", 0.7f, (t, r) => r * Mathf.Exp(-t * 5f) * (t < 0.03f ? t / 0.03f : 1f) * 0.8f), pos, 0.9f);
    public static void PlayDing(Vector3 pos) => AudioSource.PlayClipAtPoint(ding ??= Synth("Ding", 0.6f, (t, r) => (Mathf.Sin(2f * Mathf.PI * 1320f * t) + 0.5f * Mathf.Sin(2f * Mathf.PI * 2640f * t)) * 0.4f * Mathf.Exp(-t * 6f)), pos, 0.7f);

    public static void PlayBoom(Vector3 pos) => AudioSource.PlayClipAtPoint(boom ??= Synth("Boom", 1.6f, (t, r) => (r * 0.7f * Mathf.Exp(-t * 3.5f) + Mathf.Sin(2f * Mathf.PI * 48f * t * (1f + 2f * Mathf.Exp(-t * 6f))) * 0.6f * Mathf.Exp(-t * 2.2f)) * (t < 0.01f ? t / 0.01f : 1f)), pos, 1f);

    static AudioClip Synth(string name, float seconds, System.Func<float, float, float> f)
    {
        int sr = AudioSettings.outputSampleRate;
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

/// <summary>Fades a renderer's material alpha to zero over 'seconds' then destroys the object.</summary>
public class FadeAndDie : MonoBehaviour
{
    public float seconds = 20f;
    float t;
    Renderer r;
    Color c;

    void Start()
    {
        r = GetComponent<Renderer>();
        c = r.material.color;
    }

    void Update()
    {
        t += Time.deltaTime;
        float a = Mathf.Clamp01(1f - t / seconds);
        r.material.color = new Color(c.r, c.g, c.b, c.a * a);
        if (t >= seconds) Destroy(gameObject);
    }
}
