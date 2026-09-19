using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Grows deciduous trees: a recursively branching skeleton rendered as tapered
/// bark tubes (sub-mesh 0) with clusters of alpha-cutout leaf cards at the tips
/// (sub-mesh 1). Deterministic per seed so variants can be reused with instancing.
/// </summary>
public static class TreeGenerator
{
    public class Params
    {
        public int seed = 1;
        public float height = 7f;
        public float trunkRadius = 0.22f;
        public int levels = 4;
        public float leafSize = 0.7f;
        public int cardsPerTip = 3;
        public float spreadDeg = 40f;
        public float droop = 0.15f;
        public float crownStart = 0.45f; // fraction of trunk where branching starts
    }

    class Ctx
    {
        public System.Random rng;
        public Params p;
        public List<(Mesh, Matrix4x4)> bark = new List<(Mesh, Matrix4x4)>();
        public List<(Mesh, Matrix4x4)> leaves = new List<(Mesh, Matrix4x4)>();
        public Mesh leafCard;
        public float crownRadius;
    }

    public static Mesh Generate(Params p, string name, out float crownRadius)
    {
        var c = new Ctx { rng = new System.Random(p.seed), p = p, leafCard = LeafCard() };
        Grow(c, Vector3.zero, Vector3.up, p.height * 0.55f, p.trunkRadius, 0, true);
        crownRadius = c.crownRadius;
        var mesh = MeshBuilder.CombineSubmeshes(new[] { c.bark, c.leaves }, name);
        return mesh;
    }

    static float R(Ctx c, float a, float b) => a + (float)c.rng.NextDouble() * (b - a);

    static void Grow(Ctx c, Vector3 start, Vector3 dir, float length, float radius, int level, bool isTrunk)
    {
        var p = c.p;
        int steps = level == 0 ? 6 : 4;
        var path = new List<Vector3> { start };
        var radii = new List<float> { radius };
        Vector3 d = dir;
        Vector3 pos = start;
        float endRadius = radius * (level >= p.levels - 1 ? 0.25f : 0.55f);
        for (int i = 1; i <= steps; i++)
        {
            float t = i / (float)steps;
            var jitter = new Vector3(R(c, -1f, 1f), R(c, -1f, 1f), R(c, -1f, 1f)) * (isTrunk ? 0.08f : 0.22f);
            Vector3 gravity = Vector3.down * (isTrunk ? 0f : p.droop * level * 0.5f);
            Vector3 photo = Vector3.up * (isTrunk ? 0.05f : 0.08f);
            d = (d + jitter + gravity + photo).normalized;
            pos += d * (length / steps);
            path.Add(pos);
            radii.Add(Mathf.Lerp(radius, endRadius, t));
        }

        int segs = level == 0 ? 10 : level == 1 ? 7 : 5;
        var tube = MeshBuilder.Tube(path, radii, segs, "Branch", 1.5f / Mathf.Max(radius * 6f, 0.3f), capEnds: level >= p.levels - 1);
        c.bark.Add((tube, Matrix4x4.identity));

        bool terminal = level >= p.levels - 1;
        if (terminal)
        {
            AddLeafCluster(c, pos, d, p.cardsPerTip, 1f);
            return;
        }

        // Children along the upper part of this branch
        int count = level == 0 ? c.rng.Next(4, 7) : c.rng.Next(2, 4);
        float tMin = isTrunk ? p.crownStart : 0.35f;
        for (int k = 0; k < count; k++)
        {
            float t = R(c, tMin, 0.98f);
            Vector3 at = Sample(path, t);
            Vector3 tangent = Tangent(path, t);
            Vector3 perp = Vector3.Cross(tangent, new Vector3(R(c, -1, 1), R(c, -1, 1), R(c, -1, 1))).normalized;
            float spread = p.spreadDeg * R(c, 0.7f, 1.3f);
            Vector3 cd = Quaternion.AngleAxis(spread, perp) * tangent;
            cd = Quaternion.AngleAxis(R(c, 0f, 360f), tangent) * cd;
            float cl = length * R(c, 0.6f, 0.78f) * (isTrunk ? 1.1f : 1f);
            float cr = Mathf.Lerp(radius, endRadius, t) * R(c, 0.5f, 0.65f);
            Grow(c, at, cd.normalized, cl, cr, level + 1, false);
        }
        // Trunk continues upward as the leader
        if (isTrunk)
            Grow(c, pos, (d + Vector3.up * 0.3f).normalized, length * 0.75f, endRadius, level + 1, false);
        else
        {
            // Foliage also grows along the branch, not only at the tips, so the crown fills in.
            AddLeafCluster(c, Sample(path, 0.55f), d, Mathf.Max(2, p.cardsPerTip - 2), 0.85f);
            AddLeafCluster(c, Sample(path, 0.85f), d, Mathf.Max(2, p.cardsPerTip - 2), 0.9f);
        }
    }

    static void AddLeafCluster(Ctx c, Vector3 at, Vector3 dir, int cards, float scale)
    {
        var p = c.p;
        for (int i = 0; i < cards; i++)
        {
            float s = p.leafSize * scale * R(c, 0.8f, 1.25f);
            Vector3 offset = new Vector3(R(c, -1, 1), R(c, -0.6f, 1), R(c, -1, 1)) * s * 0.35f + dir * s * 0.3f;
            var rot = Quaternion.Euler(R(c, -50f, 50f), R(c, 0f, 360f), R(c, -50f, 50f));
            var m = Matrix4x4.TRS(at + offset, rot, Vector3.one * s);
            c.leaves.Add((c.leafCard, m));
            float r = new Vector2(at.x + offset.x, at.z + offset.z).magnitude + s * 0.5f;
            if (r > c.crownRadius) c.crownRadius = r;
        }
    }

    /// <summary>A unit quad, double sided, with normals blended towards "up" so the leaves shade softly.</summary>
    static Mesh LeafCard()
    {
        var b = new MeshBuilder.Builder();
        for (int side = 0; side < 2; side++)
        {
            Vector3 n = (side == 0 ? Vector3.forward : Vector3.back) * 0.4f + Vector3.up * 0.8f;
            n.Normalize();
            int i0 = b.Add(new Vector3(-0.5f, -0.5f, 0), n, new Vector2(0, 0));
            int i1 = b.Add(new Vector3(0.5f, -0.5f, 0), n, new Vector2(1, 0));
            int i2 = b.Add(new Vector3(0.5f, 0.5f, 0), n, new Vector2(1, 1));
            int i3 = b.Add(new Vector3(-0.5f, 0.5f, 0), n, new Vector2(0, 1));
            if (side == 0) b.Quad(i0, i3, i2, i1); else b.Quad(i0, i1, i2, i3);
        }
        return b.ToMesh("LeafCard");
    }

    static Vector3 Sample(List<Vector3> path, float t)
    {
        float f = t * (path.Count - 1);
        int i = Mathf.Clamp(Mathf.FloorToInt(f), 0, path.Count - 2);
        return Vector3.Lerp(path[i], path[i + 1], f - i);
    }

    static Vector3 Tangent(List<Vector3> path, float t)
    {
        float f = t * (path.Count - 1);
        int i = Mathf.Clamp(Mathf.FloorToInt(f), 0, path.Count - 2);
        return (path[i + 1] - path[i]).normalized;
    }
}
