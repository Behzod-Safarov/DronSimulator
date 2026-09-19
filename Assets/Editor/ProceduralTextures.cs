using UnityEditor;
using UnityEngine;

/// <summary>Procedurally painted textures: carbon-fibre weave, leaf clusters, grass blades, anodised metal grain.</summary>
public static class ProceduralTextures
{
    static Texture2D Existing(string name, string ext = "png") => AssetDatabase.LoadAssetAtPath<Texture2D>($"{GeneratedAssets.Dir}/Textures/{name}.{ext}");

    /// <summary>2x2 twill carbon weave: returns (albedo, normal).</summary>
    public static (Texture2D albedo, Texture2D normal) CarbonWeave(int size = 512)
    {
        var a = Existing("Carbon_Albedo");
        var n = Existing("Carbon_Normal");
        if (a != null && n != null) return (a, n);

        int tow = size / 16;                 // width of one fibre bundle in pixels
        var height = new float[size * size];
        var albedo = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var px = new Color[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                int tx = x / tow, ty = y / tow;
                // twill: warp on top where (tx - ty) mod 4 in {0,1}
                bool warpOnTop = ((tx - ty) % 4 + 4) % 4 < 2;
                float fx = (x % tow) / (float)tow, fy = (y % tow) / (float)tow;
                // rounded tow profile across its width, fibre striations along its length
                float across = warpOnTop ? fy : fx;
                float along = warpOnTop ? fx : fy;
                float bump = Mathf.Sin(across * Mathf.PI);
                float stria = 0.5f + 0.5f * Mathf.Sin(across * Mathf.PI * 18f + along * 3f);
                float h = bump * 0.8f + stria * 0.08f;
                height[y * size + x] = h;
                // anisotropic sheen: warp tows read brighter at one angle than weft
                float sheen = warpOnTop ? 0.12f + 0.16f * bump : 0.06f + 0.10f * bump;
                float v = sheen + stria * 0.03f;
                px[y * size + x] = new Color(v * 0.95f, v * 0.97f, v * 1.0f, 1f);
            }
        albedo.SetPixels(px);
        albedo.Apply();
        var normal = NormalFromHeight(height, size, 2.2f);
        var savedA = GeneratedAssets.SaveTexture(albedo, "Carbon_Albedo");
        var savedN = GeneratedAssets.SaveTexture(normal, "Carbon_Normal");
        return (savedA, savedN);
    }

    /// <summary>Brushed / cast metal grain normal map for motor bells.</summary>
    public static Texture2D BrushedMetalNormal(int size = 256)
    {
        var e = Existing("Brushed_Normal");
        if (e != null) return e;
        var height = new float[size * size];
        var rng = new System.Random(5);
        for (int y = 0; y < size; y++)
        {
            float line = (float)rng.NextDouble();
            for (int x = 0; x < size; x++)
                height[y * size + x] = line * 0.5f + (float)rng.NextDouble() * 0.1f + Mathf.PerlinNoise(x * 0.05f, y * 0.3f) * 0.4f;
        }
        return GeneratedAssets.SaveTexture(NormalFromHeight(height, size, 0.6f), "Brushed_Normal");
    }

    /// <summary>Cluster of leaves on a transparent background, for leaf cards (RGBA cut-out).</summary>
    public static Texture2D LeafCluster(int size = 512, int seed = 1, string name = "Leaves_Cutout")
    {
        var e = Existing(name);
        if (e != null) return e;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var px = new Color[size * size];
        for (int i = 0; i < px.Length; i++) px[i] = new Color(0.1f, 0.25f, 0.08f, 0f);
        var rng = new System.Random(seed);
        int leaves = 150;
        for (int l = 0; l < leaves; l++)
        {
            // leaves hang from a stem near the top-centre and fan outwards
            float cx = (float)rng.NextDouble() * 0.9f + 0.05f, cy = (float)rng.NextDouble() * 0.9f + 0.05f;
            float len = 0.08f + (float)rng.NextDouble() * 0.1f, wid = len * (0.4f + (float)rng.NextDouble() * 0.25f);
            float ang = (float)rng.NextDouble() * Mathf.PI * 2f;
            float shade = 0.55f + (float)rng.NextDouble() * 0.5f;
            var col = new Color(0.18f * shade + 0.05f, 0.42f * shade + 0.08f, 0.10f * shade + 0.02f, 1f);
            var colBack = col * 0.8f; colBack.a = 1f;
            float ca = Mathf.Cos(ang), sa = Mathf.Sin(ang);
            int minx = Mathf.Max(0, (int)((cx - len) * size)), maxx = Mathf.Min(size - 1, (int)((cx + len) * size));
            int miny = Mathf.Max(0, (int)((cy - len) * size)), maxy = Mathf.Min(size - 1, (int)((cy + len) * size));
            for (int y = miny; y <= maxy; y++)
                for (int x = minx; x <= maxx; x++)
                {
                    float dx = x / (float)size - cx, dy = y / (float)size - cy;
                    float u = dx * ca + dy * sa, v = -dx * sa + dy * ca;   // along / across the leaf
                    if (u < 0f || u > len) continue;
                    float t = u / len;
                    float halfW = wid * Mathf.Sin(Mathf.Pow(t, 0.7f) * Mathf.PI) * 0.5f;
                    if (Mathf.Abs(v) > halfW) continue;
                    float vein = Mathf.Abs(v) < halfW * 0.06f ? 0.85f : 1f;
                    float edge = 1f - Mathf.Pow(Mathf.Abs(v) / Mathf.Max(halfW, 1e-4f), 6f) * 0.3f;
                    var c = col * vein * edge;
                    c.a = 1f;
                    px[y * size + x] = c;
                }
        }
        tex.SetPixels(px);
        tex.Apply();
        return GeneratedAssets.SaveTexture(tex, name);
    }

    /// <summary>Tuft of grass blades for terrain detail billboards.</summary>
    public static Texture2D GrassTuft(int size = 512, string name = "GrassTuft_Cutout")
    {
        var e = Existing(name);
        if (e != null) return e;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var px = new Color[size * size];
        for (int i = 0; i < px.Length; i++) px[i] = new Color(0.2f, 0.4f, 0.1f, 0f);
        var rng = new System.Random(9);
        for (int b = 0; b < 40; b++)
        {
            float x0 = 0.3f + (float)rng.NextDouble() * 0.4f;                 // root near bottom centre
            float lean = ((float)rng.NextDouble() - 0.5f) * 0.9f;
            float h = 0.55f + (float)rng.NextDouble() * 0.45f;
            float w = 0.008f + (float)rng.NextDouble() * 0.008f;
            float shade = 0.6f + (float)rng.NextDouble() * 0.5f;
            for (int y = 0; y < size * h; y++)
            {
                float t = y / (size * h);
                float cx = x0 + lean * t * t;
                float half = w * (1f - t) * size;
                int xc = (int)(cx * size);
                for (int x = (int)(xc - half); x <= xc + half; x++)
                {
                    if (x < 0 || x >= size) continue;
                    float g = (0.35f + 0.45f * t) * shade;
                    px[y * size + x] = new Color(g * 0.55f, g, g * 0.25f, 1f);
                }
            }
        }
        tex.SetPixels(px);
        tex.Apply();
        return GeneratedAssets.SaveTexture(tex, name);
    }

    /// <summary>
    /// Colour-grades a photo-scanned ground texture towards lush turf: hues in the yellow-green
    /// band are pulled towards green and slightly saturated. Reads the JPG from disk so the
    /// importer settings don't matter.
    /// </summary>
    public static Texture2D GreenGrade(string jpgPath, string name, float hueTarget = 0.28f, float pull = 0.65f, float satBoost = 1.2f, float valueScale = 0.85f)
    {
        var e = Existing(name);
        if (e != null) return e;
        var src = new Texture2D(2, 2, TextureFormat.RGB24, false);
        src.LoadImage(System.IO.File.ReadAllBytes(jpgPath));
        int w = Mathf.Min(src.width, 2048), h = Mathf.Min(src.height, 2048);
        var px = src.GetPixels();
        var dst = new Texture2D(w, h, TextureFormat.RGB24, false);
        var outPx = new Color[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var c = px[(y * src.height / h) * src.width + x * src.width / w];
                Color.RGBToHSV(c, out float hue, out float s, out float v);
                if (hue > 0.08f && hue < 0.42f)
                {
                    float wgt = 1f - Mathf.Abs(hue - 0.17f) / 0.25f; // strongest on yellow-olive tones
                    wgt = Mathf.Clamp01(wgt) * pull;
                    hue = Mathf.Lerp(hue, hueTarget, wgt);
                    s = Mathf.Clamp01(s * Mathf.Lerp(1f, satBoost, wgt));
                }
                v *= valueScale;
                outPx[y * w + x] = Color.HSVToRGB(hue, s, v);
            }
        dst.SetPixels(outPx);
        dst.Apply();
        Object.DestroyImmediate(src);
        return GeneratedAssets.SaveTexture(dst, name);
    }

    static Texture2D NormalFromHeight(float[] h, int size, float strength)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false, true);
        var px = new Color[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float l = h[y * size + (x - 1 + size) % size], r = h[y * size + (x + 1) % size];
                float d = h[((y - 1 + size) % size) * size + x], u = h[((y + 1) % size) * size + x];
                var n = new Vector3((l - r) * strength, (d - u) * strength, 1f).normalized;
                px[y * size + x] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f);
            }
        tex.SetPixels(px);
        tex.Apply();
        return tex;
    }
}
