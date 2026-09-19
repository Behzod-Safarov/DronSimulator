using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Procedural mesh primitives used by the drone and tree generators:
/// lathe (surfaces of revolution), extruded 2-D shapes, tubes along paths,
/// twisted airfoil blades and mesh combining.
/// </summary>
public static class MeshBuilder
{
    public class Builder
    {
        public readonly List<Vector3> V = new List<Vector3>();
        public readonly List<Vector3> N = new List<Vector3>();
        public readonly List<Vector2> UV = new List<Vector2>();
        public readonly List<int> T = new List<int>();

        public int Add(Vector3 v, Vector3 n, Vector2 uv)
        {
            V.Add(v); N.Add(n); UV.Add(uv);
            return V.Count - 1;
        }

        public void Tri(int a, int b, int c) { T.Add(a); T.Add(b); T.Add(c); }
        public void Quad(int a, int b, int c, int d) { Tri(a, b, c); Tri(a, c, d); }

        public void Append(Mesh m, Matrix4x4 xf)
        {
            int baseIdx = V.Count;
            var mv = m.vertices; var mn = m.normals; var muv = m.uv;
            var nxf = xf.inverse.transpose;
            for (int i = 0; i < mv.Length; i++)
            {
                V.Add(xf.MultiplyPoint3x4(mv[i]));
                N.Add(nxf.MultiplyVector(mn.Length > i ? mn[i] : Vector3.up).normalized);
                UV.Add(muv.Length > i ? muv[i] : Vector2.zero);
            }
            var mt = m.triangles;
            for (int i = 0; i < mt.Length; i++) T.Add(mt[i] + baseIdx);
        }

        public Mesh ToMesh(string name, bool recalculateNormals = false)
        {
            var m = new Mesh { name = name };
            m.indexFormat = V.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            m.SetVertices(V);
            m.SetUVs(0, UV);
            m.SetTriangles(T, 0);
            if (recalculateNormals) m.RecalculateNormals(); else m.SetNormals(N);
            m.RecalculateTangents();
            m.RecalculateBounds();
            return m;
        }
    }

    // ------------------------------------------------------------------ lathe

    /// <summary>Surface of revolution around Y. Profile = (radius, height) pairs from bottom to top.</summary>
    public static Mesh Lathe(Vector2[] profile, int segments, string name = "Lathe", bool smooth = true, bool capBottom = true, bool capTop = true)
    {
        var b = new Builder();
        int rings = profile.Length;
        for (int r = 0; r < rings; r++)
        {
            // Normal from neighbouring profile points
            Vector2 prev = profile[Mathf.Max(0, r - 1)], next = profile[Mathf.Min(rings - 1, r + 1)];
            Vector2 tangent = (next - prev).normalized;
            Vector2 n2 = new Vector2(tangent.y, -tangent.x);
            for (int s = 0; s <= segments; s++)
            {
                float a = s / (float)segments * Mathf.PI * 2f;
                float ca = Mathf.Cos(a), sa = Mathf.Sin(a);
                var p = new Vector3(profile[r].x * ca, profile[r].y, profile[r].x * sa);
                var n = new Vector3(n2.x * ca, n2.y, n2.x * sa).normalized;
                b.Add(p, n, new Vector2(s / (float)segments, r / (float)(rings - 1)));
            }
        }
        for (int r = 0; r < rings - 1; r++)
            for (int s = 0; s < segments; s++)
            {
                int i0 = r * (segments + 1) + s, i1 = i0 + 1, i2 = i0 + segments + 1, i3 = i2 + 1;
                b.Quad(i0, i2, i3, i1);
            }
        if (capBottom && profile[0].x > 0f) Cap(b, profile[0], segments, false);
        if (capTop && profile[rings - 1].x > 0f) Cap(b, profile[rings - 1], segments, true);
        return b.ToMesh(name, !smooth);
    }

    static void Cap(Builder b, Vector2 ring, int segments, bool up)
    {
        var n = up ? Vector3.up : Vector3.down;
        int c = b.Add(new Vector3(0f, ring.y, 0f), n, new Vector2(0.5f, 0.5f));
        int first = b.V.Count;
        for (int s = 0; s <= segments; s++)
        {
            float a = s / (float)segments * Mathf.PI * 2f;
            b.Add(new Vector3(ring.x * Mathf.Cos(a), ring.y, ring.x * Mathf.Sin(a)), n, new Vector2(0.5f + 0.5f * Mathf.Cos(a), 0.5f + 0.5f * Mathf.Sin(a)));
        }
        for (int s = 0; s < segments; s++)
        {
            if (up) b.Tri(c, first + s + 1, first + s);
            else b.Tri(c, first + s, first + s + 1);
        }
    }

    // ------------------------------------------------------------------ 2-D shapes & extrusion

    public static Vector2[] RoundedRect(float w, float h, float r, int cornerSegments = 4)
    {
        var pts = new List<Vector2>();
        r = Mathf.Min(r, Mathf.Min(w, h) * 0.5f);
        float hw = w * 0.5f - r, hh = h * 0.5f - r;
        Vector2[] centers = { new Vector2(hw, hh), new Vector2(-hw, hh), new Vector2(-hw, -hh), new Vector2(hw, -hh) };
        for (int c = 0; c < 4; c++)
            for (int s = 0; s <= cornerSegments; s++)
            {
                float a = (c * 90f + s * 90f / cornerSegments) * Mathf.Deg2Rad;
                pts.Add(centers[c] + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r);
            }
        return pts.ToArray();
    }

    public static Vector2[] Circle(float r, int segments)
    {
        var pts = new Vector2[segments];
        for (int i = 0; i < segments; i++)
        {
            float a = i / (float)segments * Mathf.PI * 2f;
            pts[i] = new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r);
        }
        return pts;
    }

    /// <summary>Extrudes a convex CCW polygon (x,z) along Y by 'thickness' (centred). UVs are planar for the caps.</summary>
    public static Mesh Extrude(Vector2[] shape, float thickness, string name = "Extrude", float uvScale = 10f)
    {
        var b = new Builder();
        float y0 = -thickness * 0.5f, y1 = thickness * 0.5f;
        int n = shape.Length;

        // caps (fan)
        for (int side = 0; side < 2; side++)
        {
            float y = side == 0 ? y0 : y1;
            var nrm = side == 0 ? Vector3.down : Vector3.up;
            int start = b.V.Count;
            for (int i = 0; i < n; i++)
                b.Add(new Vector3(shape[i].x, y, shape[i].y), nrm, shape[i] * uvScale);
            for (int i = 1; i < n - 1; i++)
            {
                if (side == 0) b.Tri(start, start + i, start + i + 1);
                else b.Tri(start, start + i + 1, start + i);
            }
        }
        // sides (hard edges)
        float u = 0f;
        for (int i = 0; i < n; i++)
        {
            Vector2 a = shape[i], c = shape[(i + 1) % n];
            Vector2 e = c - a;
            var nrm = new Vector3(e.y, 0f, -e.x).normalized;
            float len = e.magnitude * uvScale;
            int i0 = b.Add(new Vector3(a.x, y0, a.y), nrm, new Vector2(u, 0f));
            int i1 = b.Add(new Vector3(c.x, y0, c.y), nrm, new Vector2(u + len, 0f));
            int i2 = b.Add(new Vector3(c.x, y1, c.y), nrm, new Vector2(u + len, thickness * uvScale));
            int i3 = b.Add(new Vector3(a.x, y1, a.y), nrm, new Vector2(u, thickness * uvScale));
            b.Quad(i0, i3, i2, i1);
            u += len;
        }
        return b.ToMesh(name);
    }

    /// <summary>Axis-aligned box with per-face UVs (sizes in metres).</summary>
    public static Mesh Box(Vector3 size, string name = "Box", float uvScale = 10f)
    {
        return Extrude(new[] { new Vector2(-size.x / 2, -size.z / 2), new Vector2(size.x / 2, -size.z / 2), new Vector2(size.x / 2, size.z / 2), new Vector2(-size.x / 2, size.z / 2) }, size.y, name, uvScale);
    }

    // ------------------------------------------------------------------ tubes

    /// <summary>Tube along a polyline with per-point radius, using parallel-transport frames. UV.v runs along the length.</summary>
    public static Mesh Tube(IList<Vector3> path, IList<float> radii, int segments, string name = "Tube", float uvPerMetre = 2f, bool capEnds = true)
    {
        var b = new Builder();
        int count = path.Count;
        Vector3 up = Vector3.up;
        Vector3 prevT = (path[1] - path[0]).normalized;
        Vector3 nrm = Vector3.Cross(prevT, Mathf.Abs(Vector3.Dot(prevT, up)) > 0.9f ? Vector3.right : up).normalized;
        float v = 0f;
        for (int i = 0; i < count; i++)
        {
            Vector3 t = i < count - 1 ? (path[i + 1] - path[i]).normalized : (path[i] - path[i - 1]).normalized;
            // parallel transport of the frame
            Vector3 axis = Vector3.Cross(prevT, t);
            if (axis.sqrMagnitude > 1e-8f)
            {
                float ang = Mathf.Asin(Mathf.Clamp(axis.magnitude, -1f, 1f)) * Mathf.Rad2Deg;
                nrm = Quaternion.AngleAxis(ang, axis.normalized) * nrm;
            }
            prevT = t;
            Vector3 bin = Vector3.Cross(t, nrm).normalized;
            if (i > 0) v += Vector3.Distance(path[i], path[i - 1]) * uvPerMetre;
            for (int s = 0; s <= segments; s++)
            {
                float a = s / (float)segments * Mathf.PI * 2f;
                Vector3 dir = nrm * Mathf.Cos(a) + bin * Mathf.Sin(a);
                b.Add(path[i] + dir * radii[i], dir, new Vector2(s / (float)segments, v));
            }
        }
        for (int i = 0; i < count - 1; i++)
            for (int s = 0; s < segments; s++)
            {
                int i0 = i * (segments + 1) + s, i1 = i0 + 1, i2 = i0 + segments + 1, i3 = i2 + 1;
                b.Quad(i0, i1, i3, i2);
            }
        if (capEnds)
        {
            // simple end caps
            for (int end = 0; end < 2; end++)
            {
                int ring = end == 0 ? 0 : count - 1;
                Vector3 c = path[ring];
                Vector3 tn = end == 0 ? -(path[1] - path[0]).normalized : (path[count - 1] - path[count - 2]).normalized;
                int ci = b.Add(c, tn, new Vector2(0.5f, 0.5f));
                int first = ring * (segments + 1);
                for (int s = 0; s < segments; s++)
                {
                    if (end == 0) b.Tri(ci, first + s, first + s + 1);
                    else b.Tri(ci, first + s + 1, first + s);
                }
            }
        }
        return b.ToMesh(name);
    }

    // ------------------------------------------------------------------ propeller blade

    /// <summary>
    /// A propeller blade along +X (root at x=rootStart, tip at x=length), chord along Z, thickness along Y.
    /// Cambered plate section with linear pitch twist from root to tip (degrees, positive = leading edge up towards +Z).
    /// </summary>
    public static Mesh Blade(float rootStart, float length, float rootChord, float tipChord, float rootPitch, float tipPitch,
        float thickness, float camber, int spanSegs = 12, int chordSegs = 6, string name = "Blade")
    {
        var b = new Builder();
        int cols = chordSegs + 1;
        for (int i = 0; i <= spanSegs; i++)
        {
            float s = i / (float)spanSegs;
            float x = Mathf.Lerp(rootStart, length, s);
            // planform: elliptical-ish tip
            float chord = Mathf.Lerp(rootChord, tipChord, s) * Mathf.Sqrt(1f - Mathf.Pow(Mathf.Max(0f, s - 0.85f) / 0.15f, 2f) * 0.999f);
            float pitch = Mathf.Lerp(rootPitch, tipPitch, s) * Mathf.Deg2Rad;
            float thick = thickness * (1f - 0.6f * s);
            for (int surface = 0; surface < 2; surface++)
            {
                float sign = surface == 0 ? 1f : -1f;
                for (int j = 0; j < cols; j++)
                {
                    float t = j / (float)chordSegs;               // 0 = leading edge, 1 = trailing edge
                    float z = (0.3f - t) * chord;                   // leading edge at +0.3c
                    float y = camber * chord * Mathf.Sin(t * Mathf.PI) + sign * thick * 0.5f * Mathf.Sin(t * Mathf.PI) + sign * 0.0001f;
                    // rotate section about X by pitch
                    float zr = z * Mathf.Cos(pitch) - y * Mathf.Sin(pitch);
                    float yr = z * Mathf.Sin(pitch) + y * Mathf.Cos(pitch);
                    b.Add(new Vector3(x, yr, zr), Vector3.up * sign, new Vector2(t, s));
                }
            }
        }
        int stride = cols * 2;
        for (int i = 0; i < spanSegs; i++)
            for (int j = 0; j < chordSegs; j++)
            {
                int a = i * stride + j, c = a + stride;
                b.Quad(a, c, c + 1, a + 1);                       // top
                int a2 = a + cols, c2 = c + cols;
                b.Quad(a2, a2 + 1, c2 + 1, c2);                   // bottom
            }
        // close leading & trailing edges
        for (int i = 0; i < spanSegs; i++)
        {
            int top0 = i * stride, bot0 = top0 + cols, top1 = top0 + stride, bot1 = bot0 + stride;
            b.Quad(top0, bot0, bot1, top1);
            int te = chordSegs;
            b.Quad(top0 + te, top1 + te, bot1 + te, bot0 + te);
        }
        return b.ToMesh(name, true);
    }

    // ------------------------------------------------------------------ combine

    public static Mesh Combine(IEnumerable<(Mesh mesh, Matrix4x4 xf)> parts, string name)
    {
        var b = new Builder();
        foreach (var (mesh, xf) in parts) b.Append(mesh, xf);
        return b.ToMesh(name);
    }

    /// <summary>Combine several groups into one mesh with one sub-mesh per group (for multi-material objects).</summary>
    public static Mesh CombineSubmeshes(IList<List<(Mesh mesh, Matrix4x4 xf)>> groups, string name)
    {
        var verts = new List<Vector3>(); var norms = new List<Vector3>(); var uvs = new List<Vector2>();
        var subTris = new List<int[]>();
        foreach (var group in groups)
        {
            var b = new Builder();
            foreach (var (mesh, xf) in group) b.Append(mesh, xf);
            int baseIdx = verts.Count;
            verts.AddRange(b.V); norms.AddRange(b.N); uvs.AddRange(b.UV);
            var t = new int[b.T.Count];
            for (int i = 0; i < t.Length; i++) t[i] = b.T[i] + baseIdx;
            subTris.Add(t);
        }
        var m = new Mesh { name = name, indexFormat = verts.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
        m.SetVertices(verts); m.SetNormals(norms); m.SetUVs(0, uvs);
        m.subMeshCount = subTris.Count;
        for (int i = 0; i < subTris.Count; i++) m.SetTriangles(subTris[i], i);
        m.RecalculateTangents();
        m.RecalculateBounds();
        return m;
    }
}
