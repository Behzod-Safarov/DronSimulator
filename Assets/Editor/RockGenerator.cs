using UnityEngine;

/// <summary>
/// Procedural boulders: a UV sphere pushed around by layered value noise, flattened a
/// little and sunk so it reads as rock sitting in the ground. Used for the desert
/// plateau, the quarry walls and the spoil heaps.
/// </summary>
public static class RockGenerator
{
    /// <summary>One boulder mesh. Radius 1 before scaling; <paramref name="lumpiness"/> 0.1 = smooth, 0.45 = jagged.</summary>
    public static Mesh Generate(int seed, string name, int rings = 14, int segments = 20, float lumpiness = 0.3f)
    {
        var b = new MeshBuilder.Builder();
        var rng = new System.Random(seed);
        // three noise lobes, each a random direction with its own frequency and weight
        var dirs = new Vector3[5];
        var freq = new float[5];
        var amp = new float[5];
        for (int i = 0; i < dirs.Length; i++)
        {
            dirs[i] = new Vector3((float)rng.NextDouble() * 2f - 1f, (float)rng.NextDouble() * 2f - 1f, (float)rng.NextDouble() * 2f - 1f).normalized;
            freq[i] = 1.2f + (float)rng.NextDouble() * 3.5f;
            amp[i] = lumpiness * (0.35f + (float)rng.NextDouble() * 0.65f) / (1f + i * 0.6f);
        }

        float Displace(Vector3 n)
        {
            float d = 1f;
            for (int i = 0; i < dirs.Length; i++) d += Mathf.Sin(Vector3.Dot(n, dirs[i]) * freq[i] * Mathf.PI) * amp[i];
            return Mathf.Max(0.35f, d);
        }

        for (int r = 0; r <= rings; r++)
        {
            float v = r / (float)rings;
            float phi = v * Mathf.PI;
            for (int s = 0; s <= segments; s++)
            {
                float u = s / (float)segments;
                float theta = u * Mathf.PI * 2f;
                var n = new Vector3(Mathf.Sin(phi) * Mathf.Cos(theta), Mathf.Cos(phi), Mathf.Sin(phi) * Mathf.Sin(theta));
                var p = n * Displace(n);
                p.y *= 0.72f;                       // boulders are wider than they are tall
                b.Add(p, n, new Vector2(u * 2f, v * 2f));
            }
        }
        for (int r = 0; r < rings; r++)
            for (int s = 0; s < segments; s++)
            {
                int i0 = r * (segments + 1) + s, i1 = i0 + 1, i2 = i0 + segments + 1, i3 = i2 + 1;
                b.Quad(i0, i2, i3, i1);
            }
        return b.ToMesh(name, true);   // recalculate normals: the displacement changed the surface
    }

    /// <summary>A set of boulder meshes to pick from when scattering.</summary>
    public static Mesh[] Variants(int count = 4, float lumpiness = 0.3f, string prefix = "Rock")
    {
        var list = new Mesh[count];
        for (int i = 0; i < count; i++)
            list[i] = GeneratedAssets.SaveMesh(Generate(7919 + i * 131, $"{prefix}_{i}", lumpiness: lumpiness), $"{prefix}_{i}");
        return list;
    }
}
