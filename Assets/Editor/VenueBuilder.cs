using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// The discipline venues spread over the flat part of the field:
///   pit (origin)   - Basics: asphalt pad, hover box, landing pad, pylon (built by SceneBuilder)
///   race track     - Racing / Agent: gravel oval with 8 gates, start arch, tyre walls, grandstand
///   freestyle park - Freestyle: concrete pad with a skeleton building, window wall, ramps, pillars
///   demolition yard- Demolition: fenced dirt yard with warehouses, containers, the parking lot and props
/// Also exposes the ground masks the terrain painter and the tree/building scatter use.
/// </summary>
public static class VenueBuilder
{
    // Far enough apart that each biome (radius BiomeR) owns its own ground, vegetation and
    // horizon: at the old 80-90 m spacing the desert and urban masks bled over the pit and
    // left the meadow at 6 percent.
    public static readonly Vector2 Race = new Vector2(170f, 40f);
    public static readonly Vector2 Park = new Vector2(-170f, 40f);
    public static readonly Vector2 Yard = new Vector2(0f, 185f);
    public const float RaceRx = 24f, RaceRz = 14f;
    public const float ParkW = 46f, ParkD = 38f;
    public const float YardW = 66f, YardD = 56f;
    public static readonly Vector2 PondCenter = new Vector2(-150f, -130f);
    public const float PondRadius = 13f;

    // ------------------------------------------------------------------ ground masks

    static float SoftRect(float x, float z, Vector2 c, float w, float d, float edge)
    {
        float dx = Mathf.Max(0f, Mathf.Abs(x - c.x) - w * 0.5f), dz = Mathf.Max(0f, Mathf.Abs(z - c.y) - d * 0.5f);
        return 1f - Mathf.Clamp01(Mathf.Sqrt(dx * dx + dz * dz) / edge);
    }

    /// <summary>1 on gravel: the race oval band, its start pad apron and the park apron.</summary>
    public static float Gravel(float x, float z)
    {
        float ex = (x - Race.x) / RaceRx, ez = (z - Race.y) / RaceRz;
        float e = Mathf.Sqrt(ex * ex + ez * ez);
        float distOval = Mathf.Abs(e - 1f) * (RaceRx + RaceRz) * 0.5f;
        float oval = 1f - Mathf.Clamp01((distOval - 2.2f) / 1.4f);
        float pad = SoftRect(x, z, new Vector2(Race.x, Race.y - 6f), 12f, 9f, 1.5f);
        float park = SoftRect(x, z, Park, ParkW + 2f, ParkD + 2f, 2f);
        return Mathf.Max(oval, Mathf.Max(pad, park));
    }

    /// <summary>1 where the terrain must be flat (under each venue), soft 10 m edge.</summary>
    public static float Flat(float x, float z)
    {
        float ex = (x - Race.x) / (RaceRx + 12f), ez = (z - Race.y) / (RaceRz + 12f);
        float race = 1f - Mathf.Clamp01((Mathf.Sqrt(ex * ex + ez * ez) - 1f) * 4f);
        float park = SoftRect(x, z, Park, ParkW + 6f, ParkD + 6f, 10f);
        float yard = SoftRect(x, z, Yard, YardW + 6f, YardD + 6f, 10f);
        // The pond belongs here too: its basin is carved to a fixed depth, so it has to sit on
        // level ground. On a slope the "basin" comes out tilted, one side of the floor ends up
        // above any sensible water level, and the water disc gets buried in the hillside.
        float pond = Falloff(x, z, PondCenter, PondRadius + 14f, 12f);
        return Mathf.Max(Mathf.Max(race, pond), Mathf.Max(park, yard));
    }

    /// <summary>1 on bare dirt: the demolition yard.</summary>
    public static float Dirt(float x, float z) => SoftRect(x, z, Yard, YardW, YardD, 3f);

    /// <summary>1 inside the farm pond, soft 5 m edge - carves the basin and keeps the shore muddy.</summary>
    public static float Pond(float x, float z) => 1f - Mathf.Clamp01((Vector2.Distance(new Vector2(x, z), PondCenter) - PondRadius) / 5f);

    /// <summary>True inside any venue (plus margin) - used to keep trees and farm buildings out.</summary>
    public static bool Keepout(float x, float z, float margin)
    {
        if (Mathf.Abs(x) < 26f + margin && z > -16f - margin && z < 16f + margin) return true; // pit
        float ex = (x - Race.x) / (RaceRx + 8f + margin), ez = (z - Race.y) / (RaceRz + 8f + margin);
        if (ex * ex + ez * ez < 1f) return true;
        if (Mathf.Abs(x - Park.x) < ParkW * 0.5f + margin && Mathf.Abs(z - Park.y) < ParkD * 0.5f + margin) return true;
        if (Mathf.Abs(x - Yard.x) < YardW * 0.5f + margin && Mathf.Abs(z - Yard.y) < YardD * 0.5f + margin) return true;
        if (Vector2.Distance(new Vector2(x, z), PondCenter) < PondRadius + margin) return true;
        return false;
    }


    // ==================================================================== landscapes
    // Each venue sits in its own biome: meadow (pit), desert plateau (race), urban
    // wasteland (park) and quarry basin (yard). These masks drive the terrain height,
    // the ground textures, the detail grass and where trees may grow.

    public const float BiomeR = 105f;   // radius of a biome around its venue centre

    static float Falloff(float x, float z, Vector2 c, float r, float edge)
    {
        float d = Vector2.Distance(new Vector2(x, z), c);
        return 1f - Mathf.Clamp01((d - (r - edge)) / edge);
    }

    /// <summary>Desert plateau around the race track.</summary>
    public static float Desert(float x, float z) => Falloff(x, z, Race, BiomeR, 45f);
    /// <summary>Urban wasteland around the freestyle park.</summary>
    public static float Urban(float x, float z) => Falloff(x, z, Park, BiomeR, 45f);
    /// <summary>Quarry basin around the demolition yard.</summary>
    public static float Quarry(float x, float z) => Falloff(x, z, Yard, BiomeR - 15f, 40f);
    /// <summary>Green meadow around the pit (what is left over).</summary>
    public static float Meadow(float x, float z) => Mathf.Clamp01(1f - Desert(x, z) - Urban(x, z) - Quarry(x, z));

    /// <summary>Extra terrain height (metres) that shapes each biome, blended to 0 under the venues.</summary>
    public static float HeightOffset(float x, float z)
    {
        float h = 0f;

        // Meadow: rolling hillocks near the pit, and a shallow basin holding the pond.
        float meadow = Meadow(x, z);
        if (meadow > 0f)
        {
            // (the pond basin is carved separately by BuildTerrain via Pond())
            float roll = (Mathf.PerlinNoise(400f + x * 0.012f, 900f + z * 0.012f) - 0.5f) * 5.5f;
            h += roll * meadow;
        }

        // Desert: a mesa ridge east of the track, plus long dune swells.
        float desert = Desert(x, z);
        if (desert > 0f)
        {
            float mesa = 16f * Falloff(x, z, new Vector2(Race.x + 74f, Race.y + 18f), 58f, 26f);
            float ridge = 9f * Falloff(x, z, new Vector2(Race.x + 30f, Race.y + 66f), 52f, 30f);
            float dunes = Mathf.PerlinNoise(70f + x * 0.02f, 30f + z * 0.02f) * 3.2f;
            h += (mesa + ridge + dunes) * desert;
        }

        // Urban: rubble mounds ringing the concrete, otherwise dead flat.
        float urban = Urban(x, z);
        if (urban > 0f)
        {
            float ring = Mathf.Clamp01((Vector2.Distance(new Vector2(x, z), Park) - 34f) / 22f);
            float mounds = ring * (1.5f + Mathf.PerlinNoise(220f + x * 0.05f, 610f + z * 0.05f) * 4.5f);
            h += mounds * urban;
        }

        // Quarry: the yard sits in a dug-out basin with a stepped rim.
        float quarry = Quarry(x, z);
        if (quarry > 0f)
        {
            float d = Vector2.Distance(new Vector2(x, z), Yard);
            float rim = Mathf.Clamp01((d - 44f) / 26f);
            float bench = Mathf.Floor(rim * 3f) / 3f;                       // terraced benches
            h += (-4.5f + bench * 13f + Mathf.PerlinNoise(11f + x * 0.06f, 5f + z * 0.06f) * 1.8f * rim) * quarry;
        }

        return h * (1f - Flat(x, z));
    }

    // ------------------------------------------------------------------ shared bits

    // tiling=1.2 combined with ParkConcrete's mesh uv=0.2 repeats the texture every ~3.3m -
    // tiling=0.35 (the old value) combined the same way repeated only every ~14m and read as smeared/blurry.
    static Material Concrete() => GeneratedAssets.PbrMaterial("Concrete", "asphalt_pit_lane", 1.2f, 0.4f, new Color(2.1f, 2.05f, 1.9f));
    static Material Asphalt() => GeneratedAssets.PbrMaterial("Asphalt", "asphalt_pit_lane", 1f, 0.9f);

    static Transform Spawn(Transform parent, string name, Vector3 pos, float yaw)
    {
        var s = new GameObject(name).transform;
        s.SetParent(parent);
        s.position = pos;
        s.rotation = Quaternion.Euler(0f, yaw, 0f);
        return s;
    }

    static GameObject Pad(string name, Transform parent, Terrain terrain, Material mat, float cx, float cz, float w, float d, float uv = 0.25f)
    {
        float g = SceneBuilder.GroundHeight(terrain, cx, cz);
        var mesh = GeneratedAssets.SaveMesh(MeshBuilder.Box(new Vector3(w, 0.1f, d), name, uv), name);
        var go = SceneBuilder.MeshObject(name, parent, mesh, mat, new Vector3(cx, g - 0.03f, cz));
        go.AddComponent<BoxCollider>();
        go.isStatic = true;
        return go;
    }

    static Mesh ConeMesh()
    {
        return GeneratedAssets.SaveMesh(MeshBuilder.Lathe(new[] { new Vector2(0.22f, 0f), new Vector2(0.2f, 0.04f), new Vector2(0.05f, 0.55f), new Vector2(0.03f, 0.56f) }, 16, "Cone"), "Cone");
    }

    // ------------------------------------------------------------------ race track

    public static (Gate[] gates, Transform spawn, Transform focus) BuildRaceTrack(Transform root, Terrain terrain)
    {
        var v = new GameObject("Venue - Race track").transform;
        v.SetParent(root);
        var steel = SceneBuilder.Mat("Steel", new Color(0.55f, 0.56f, 0.58f), glossiness: 0.45f, metallic: 0.8f);
        var paint = SceneBuilder.Mat("PaintWhite", new Color(0.72f, 0.72f, 0.7f), glossiness: 0.25f);
        var tyre = SceneBuilder.Mat("Tyre", new Color(0.06f, 0.06f, 0.06f), glossiness: 0.08f);
        var tyreW = SceneBuilder.Mat("TyreWhite", new Color(0.9f, 0.9f, 0.86f), glossiness: 0.3f);
        var checkerA = SceneBuilder.Mat("CheckerBlack", new Color(0.05f, 0.05f, 0.05f), glossiness: 0.2f);
        var cone = SceneBuilder.Mat("Cone", new Color(1f, 0.4f, 0.05f), glossiness: 0.35f);
        var bench = SceneBuilder.Mat("Bench", new Color(0.2f, 0.45f, 0.8f), glossiness: 0.3f);
        var roof = GeneratedAssets.CorrugatedMetal("Roof", new Color(0.38f, 0.34f, 0.32f), 8f, glossiness: 0.2f, metallic: 0.05f);
        var wall = GeneratedAssets.WeatheredConcrete("Wall", new Color(0.66f, 0.61f, 0.54f), 1.5f, glossiness: 0.08f, seed: 8);
        var glass = SceneBuilder.Mat("BuildingWindow", new Color(0.15f, 0.22f, 0.28f), glossiness: 0.7f, metallic: 0.2f);
        var flagMats = new[] { SceneBuilder.Mat("FlagRed", new Color(0.9f, 0.1f, 0.1f), glossiness: 0.1f), SceneBuilder.Mat("FlagBlue", new Color(0.1f, 0.3f, 0.9f), glossiness: 0.1f), SceneBuilder.Mat("FlagYellow", new Color(1f, 0.85f, 0.1f), glossiness: 0.1f) };

        // Gates around the oval; gate 1 on the south straight is the start/finish
        var gatesRoot = new GameObject("Gates").transform;
        gatesRoot.SetParent(v);
        const int count = 8;
        var gates = new List<Gate>();
        for (int i = 0; i < count; i++)
        {
            float t = Mathf.PI + (i / (float)count) * Mathf.PI * 2f;
            var pos = new Vector3(Race.x + Mathf.Sin(t) * RaceRx, 0f, Race.y + Mathf.Cos(t) * RaceRz);
            pos.y = SceneBuilder.GroundHeight(terrain, pos.x, pos.z);
            var tangent = new Vector3(Mathf.Cos(t) * RaceRx, 0f, -Mathf.Sin(t) * RaceRz).normalized;
            gates.Add(SceneBuilder.BuildGate(gatesRoot, i + 1, pos, tangent));
        }

        // Start/finish arch over gate 1 with a chequered banner
        var g1 = gates[0].transform.position;
        float ground = SceneBuilder.GroundHeight(terrain, g1.x, g1.z);
        var arch = new GameObject("Start arch").transform;
        arch.SetParent(v);
        arch.position = new Vector3(g1.x, ground, g1.z);
        // Gate 1 sits on the south straight where the track runs along X, so the arch must
        // span across X (local right = world X, i.e. forward = +Z). Facing it along Z would
        // plant a post at (80, 15.5) - right in the climb-out from the start pad.
        arch.rotation = Quaternion.LookRotation(Vector3.forward, Vector3.up);
        foreach (var sx in new[] { -3.5f, 3.5f })
            SceneBuilder.Primitive(PrimitiveType.Cylinder, "Post", arch, new Vector3(sx, 2.8f, 0f), new Vector3(0.22f, 2.8f, 0.22f), steel);
        SceneBuilder.Primitive(PrimitiveType.Cube, "Beam", arch, new Vector3(0f, 5.5f, 0f), new Vector3(7.4f, 0.3f, 0.3f), steel);
        for (int cx = 0; cx < 14; cx++)
            for (int cy = 0; cy < 2; cy++)
                SceneBuilder.Primitive(PrimitiveType.Cube, "Check", arch, new Vector3(-3.25f + cx * 0.5f, 4.55f + cy * 0.5f, 0f), new Vector3(0.5f, 0.5f, 0.04f), (cx + cy) % 2 == 0 ? checkerA : paint, collider: false);
        SceneBuilder.Primitive(PrimitiveType.Cube, "Start line", v, new Vector3(g1.x, ground + 0.03f, g1.z), new Vector3(6f, 0.004f, 0.35f), paint, collider: false);

        // Start pad inside the oval, where the pilots launch
        Pad("RaceStartPad", v, terrain, Asphalt(), Race.x, Race.y - 6f, 10f, 7f);
        float padY = SceneBuilder.GroundHeight(terrain, Race.x, Race.y - 6f);
        SceneBuilder.PaintRing("Start ring", v, paint, Asphalt(), new Vector3(Race.x, padY + 0.02f, Race.y - 6f), 0.46f, 0.575f);
        var spawn = Spawn(v, "SpawnPoint Race", new Vector3(Race.x, padY + 0.06f, Race.y - 6f), -90f);

        // Tyre walls on the outside of the four "corners"
        var coneMesh = ConeMesh();
        var rng = new System.Random(808);
        foreach (var ang in new[] { 40f, 140f, 220f, 320f })
        {
            for (int k = 0; k < 5; k++)
            {
                float a = (ang + (k - 2) * 7f) * Mathf.Deg2Rad;
                float x = Race.x + Mathf.Sin(a) * (RaceRx + 4.5f), z = Race.y + Mathf.Cos(a) * (RaceRz + 4.5f);
                float gy = SceneBuilder.GroundHeight(terrain, x, z);
                for (int s = 0; s < 3; s++)
                    SceneBuilder.Primitive(PrimitiveType.Cylinder, "Tyre", v, new Vector3(x, gy + 0.11f + s * 0.22f, z), new Vector3(0.75f, 0.11f, 0.75f), (k + s) % 2 == 0 ? tyre : tyreW, collider: true);
            }
        }
        // Cones lining the inside of the track
        for (int i = 0; i < 24; i++)
        {
            float a = i / 24f * Mathf.PI * 2f;
            float x = Race.x + Mathf.Sin(a) * (RaceRx - 3.2f), z = Race.y + Mathf.Cos(a) * (RaceRz - 3.2f);
            var c = SceneBuilder.MeshObject("Cone", v, coneMesh, cone, new Vector3(x, SceneBuilder.GroundHeight(terrain, x, z), z));
            var cCol = c.AddComponent<CapsuleCollider>();
            cCol.radius = 0.15f;
            cCol.height = 0.56f; // match ConeMesh()'s Lathe profile, which spans local Y in [0, 0.56]
            cCol.center = new Vector3(0f, 0.28f, 0f);
        }
        // Flag poles outside the track
        for (int i = 0; i < 8; i++)
        {
            float a = (i / 8f + 0.06f) * Mathf.PI * 2f;
            float x = Race.x + Mathf.Sin(a) * (RaceRx + 8f), z = Race.y + Mathf.Cos(a) * (RaceRz + 8f);
            float gy = SceneBuilder.GroundHeight(terrain, x, z);
            SceneBuilder.Primitive(PrimitiveType.Cylinder, "Flag pole", v, new Vector3(x, gy + 3f, z), new Vector3(0.06f, 3f, 0.06f), steel);
            // Two-segment flag streaming downwind, driven by WindVane. It used to be a single cube
            // frozen at a random yaw, which read as a painted signboard and - worse - showed a
            // wind direction that had nothing to do with the one the drone was flying in.
            var flagRoot = new GameObject("Flag").transform;
            flagRoot.SetParent(v);
            flagRoot.position = new Vector3(x, gy + 5.4f, z);
            var flagSegs = new Transform[2];
            Transform fp = flagRoot;
            for (int s = 0; s < 2; s++)
            {
                var seg = new GameObject($"Flag seg {s}").transform;
                seg.SetParent(fp, false);
                seg.localPosition = s == 0 ? Vector3.zero : new Vector3(0f, 0f, 0.6f);
                flagSegs[s] = seg;
                fp = seg;
                // Thin in X, 0.7 m tall, 0.6 m of length per segment running downwind (+Z).
                SceneBuilder.Primitive(PrimitiveType.Cube, "Cloth", seg, new Vector3(0f, 0f, 0.3f),
                                       new Vector3(0.02f, 0.7f, 0.6f), flagMats[i % 3], collider: false);
            }
            var fvane = flagRoot.gameObject.AddComponent<WindVane>();
            fvane.kind = WindVane.Kind.Flag;
            fvane.segments = flagSegs;
            fvane.rippleDeg = 13f;
            fvane.rippleHz = 2.1f + (float)rng.NextDouble() * 0.6f;
        }

        // Grandstand along the south straight, facing the track
        var stand = new GameObject("Grandstand").transform;
        stand.SetParent(v);
        float sz = Race.y - RaceRz - 11f;
        float sg = SceneBuilder.GroundHeight(terrain, Race.x, sz);
        stand.position = new Vector3(Race.x, sg, sz);
        for (int row = 0; row < 4; row++)
        {
            SceneBuilder.Primitive(PrimitiveType.Cube, "Tier", stand, new Vector3(0f, 0.3f + row * 0.6f, -row * 1.1f), new Vector3(16f, 0.6f, 1.1f), GeneratedAssets.WeatheredConcrete("Concrete2", new Color(0.7f, 0.7f, 0.68f), 2.5f)).isStatic = true;
            for (int s = 0; s < 12; s++)
                SceneBuilder.Primitive(PrimitiveType.Cube, "Seat", stand, new Vector3(-7.15f + s * 1.3f, 0.75f + row * 0.6f, -row * 1.1f - 0.2f), new Vector3(1.1f, 0.3f, 0.5f), bench, collider: false);
        }
        foreach (var sx in new[] { -7.5f, 0f, 7.5f })
            SceneBuilder.Primitive(PrimitiveType.Cylinder, "Roof post", stand, new Vector3(sx, 2.6f, -4.2f), new Vector3(0.12f, 2.6f, 0.12f), steel);
        var canopy = SceneBuilder.Primitive(PrimitiveType.Cube, "Canopy", stand, new Vector3(0f, 5.3f, -2.2f), new Vector3(17f, 0.15f, 5.5f), roof);
        canopy.transform.rotation = Quaternion.Euler(-8f, 0f, 0f);

        // Timing tower by the start line
        var tower = new GameObject("Timing tower").transform;
        tower.SetParent(v);
        float tx = Race.x + 9f, tz = Race.y - RaceRz - 6f;
        tower.position = new Vector3(tx, SceneBuilder.GroundHeight(terrain, tx, tz), tz);
        SceneBuilder.Primitive(PrimitiveType.Cube, "Base", tower, new Vector3(0f, 2f, 0f), new Vector3(3f, 4f, 3f), wall).isStatic = true;
        SceneBuilder.Primitive(PrimitiveType.Cube, "Cabin", tower, new Vector3(0f, 5.2f, 0f), new Vector3(3.6f, 2.4f, 3.6f), wall).isStatic = true;
        SceneBuilder.Primitive(PrimitiveType.Cube, "Glass", tower, new Vector3(0f, 5.4f, 0f), new Vector3(3.65f, 1.2f, 3.65f), glass, collider: false);
        SceneBuilder.Primitive(PrimitiveType.Cube, "Roof", tower, new Vector3(0f, 6.5f, 0f), new Vector3(4f, 0.2f, 4f), roof);

        return (gates.ToArray(), spawn, arch);
    }

    // ------------------------------------------------------------------ freestyle park

    public static (Transform spawn, Transform focus) BuildFreestylePark(Transform root, Terrain terrain)
    {
        var v = new GameObject("Venue - Freestyle park").transform;
        v.SetParent(root);
        var concrete = Concrete();
        var raw = GeneratedAssets.WeatheredConcrete("RawConcrete", new Color(0.74f, 0.73f, 0.7f), 2f, seed: 5);
        var rebar = SceneBuilder.Mat("Rust", new Color(0.45f, 0.25f, 0.12f), glossiness: 0.2f);
        var container = GeneratedAssets.CorrugatedMetal("Container", new Color(0.12f, 0.3f, 0.5f), 5f);
        var container2 = GeneratedAssets.CorrugatedMetal("Container2", new Color(0.6f, 0.2f, 0.1f), 5f);
        var paint = SceneBuilder.Mat("PaintWhite", new Color(0.72f, 0.72f, 0.7f), glossiness: 0.25f);
        var accent = SceneBuilder.Mat("ParkAccent", new Color(0.8f, 0.3f, 0.9f), glossiness: 0.3f);

        Pad("ParkConcrete", v, terrain, concrete, Park.x, Park.y, ParkW, ParkD, 0.2f);
        float g = SceneBuilder.GroundHeight(terrain, Park.x, Park.y);
        float y0 = g + 0.02f;

        // Skeleton building ("bando"): 3 open floors on columns, a solid stair core
        var bando = new GameObject("Bando").transform;
        bando.SetParent(v);
        bando.position = new Vector3(Park.x + 9f, y0, Park.y + 5f);
        const float fw = 15f, fd = 11f, fh = 3.4f;
        for (int floor = 1; floor <= 3; floor++)
            SceneBuilder.Primitive(PrimitiveType.Cube, $"Slab {floor}", bando, new Vector3(0f, floor * fh, 0f), new Vector3(fw, 0.3f, fd), raw).isStatic = true;
        for (int ix = 0; ix < 3; ix++)
            for (int iz = 0; iz < 3; iz++)
            {
                if (ix == 1 && iz == 1) continue; // open centre
                SceneBuilder.Primitive(PrimitiveType.Cube, "Column", bando, new Vector3((ix - 1) * (fw * 0.5f - 0.3f), fh * 1.5f, (iz - 1) * (fd * 0.5f - 0.3f)), new Vector3(0.5f, fh * 3f, 0.5f), raw).isStatic = true;
            }
        SceneBuilder.Primitive(PrimitiveType.Cube, "Stair core", bando, new Vector3(fw * 0.5f - 1.6f, fh * 1.5f, -fd * 0.5f + 1.6f), new Vector3(3f, fh * 3f, 3f), raw).isStatic = true;
        // Parapet on the roof and a few rebar stubs
        SceneBuilder.Primitive(PrimitiveType.Cube, "Parapet N", bando, new Vector3(0f, fh * 3f + 0.5f, fd * 0.5f - 0.1f), new Vector3(fw, 0.7f, 0.2f), raw);
        SceneBuilder.Primitive(PrimitiveType.Cube, "Parapet S", bando, new Vector3(0f, fh * 3f + 0.5f, -fd * 0.5f + 0.1f), new Vector3(fw, 0.7f, 0.2f), raw);
        for (int i = 0; i < 6; i++)
            SceneBuilder.Primitive(PrimitiveType.Cylinder, "Rebar", bando, new Vector3(-fw * 0.5f + 1f + i * 2.5f, fh * 3f + 0.6f, 0f), new Vector3(0.03f, 0.5f, 0.03f), rebar, collider: false);

        // Window wall: 3 openings to thread through
        var wallT = new GameObject("Window wall").transform;
        wallT.SetParent(v);
        wallT.position = new Vector3(Park.x - 12f, y0, Park.y - 9f);
        wallT.rotation = Quaternion.Euler(0f, 20f, 0f);
        SceneBuilder.Primitive(PrimitiveType.Cube, "Sill band", wallT, new Vector3(0f, 0.7f, 0f), new Vector3(12f, 1.4f, 0.4f), raw).isStatic = true;
        SceneBuilder.Primitive(PrimitiveType.Cube, "Lintel band", wallT, new Vector3(0f, 4.45f, 0f), new Vector3(12f, 1.1f, 0.4f), raw).isStatic = true;
        for (int i = 0; i < 4; i++)
            SceneBuilder.Primitive(PrimitiveType.Cube, "Pier", wallT, new Vector3(-5.4f + i * 3.6f, 2.65f, 0f), new Vector3(1.2f, 2.5f, 0.4f), raw).isStatic = true;
        for (int i = 0; i < 3; i++)
            SceneBuilder.Primitive(PrimitiveType.Cube, "Frame", wallT, new Vector3(-3.6f + i * 3.6f, 1.45f, 0f), new Vector3(2.4f, 0.1f, 0.42f), accent, collider: false);

        // Launch ramps (wedges) and a row of pillars for gaps
        var ramp = GeneratedAssets.SaveMesh(MeshBuilder.Extrude(new[] { new Vector2(0f, 0f), new Vector2(5f, 0f), new Vector2(5f, 2.2f) }, 5f, "Ramp", 0.3f), "Ramp");
        foreach (var (rx, rz, yaw) in new[] { (-6f, 10f, 0f), (12f, -12f, 200f) })
        {
            var r = SceneBuilder.MeshObject("Ramp", v, ramp, concrete, new Vector3(Park.x + rx, y0, Park.y + rz));
            r.transform.rotation = Quaternion.Euler(-90f, yaw, 0f);
            r.AddComponent<MeshCollider>().convex = true;
            r.isStatic = true;
        }
        for (int i = 0; i < 6; i++)
        {
            float h = 2.5f + (i % 3) * 1.6f;
            SceneBuilder.Primitive(PrimitiveType.Cube, "Pillar", v, new Vector3(Park.x - 16f + i * 3f, y0 + h * 0.5f, Park.y + 12f), new Vector3(0.8f, h, 0.8f), raw).isStatic = true;
        }
        // Containers: two on the ground, one across the top
        var cA = SceneBuilder.Primitive(PrimitiveType.Cube, "Container A", v, new Vector3(Park.x + 14f, y0 + 1.3f, Park.y - 3f), new Vector3(2.4f, 2.6f, 12f), container);
        var cB = SceneBuilder.Primitive(PrimitiveType.Cube, "Container B", v, new Vector3(Park.x + 18.5f, y0 + 1.3f, Park.y - 3f), new Vector3(2.4f, 2.6f, 12f), container2);
        var cC = SceneBuilder.Primitive(PrimitiveType.Cube, "Container C", v, new Vector3(Park.x + 16.2f, y0 + 3.9f, Park.y - 1f), new Vector3(2.4f, 2.6f, 12f), container);
        cC.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
        cA.isStatic = cB.isStatic = cC.isStatic = true;

        // Painted centre mark = spawn
        SceneBuilder.PaintRing("Centre ring", v, paint, concrete, new Vector3(Park.x, y0, Park.y - 2f), 0.46f, 0.575f);
        var spawn = Spawn(v, "SpawnPoint Park", new Vector3(Park.x, y0 + 0.06f, Park.y - 2f), 0f);
        return (spawn, bando);
    }

    // ------------------------------------------------------------------ demolition yard

    public static (Transform spawn, Transform focus) BuildDemolitionYard(Transform root, Terrain terrain)
    {
        var v = new GameObject("Venue - Demolition yard").transform;
        v.SetParent(root);
        var steel = SceneBuilder.Mat("Steel", new Color(0.55f, 0.56f, 0.58f), glossiness: 0.45f, metallic: 0.8f);
        var fence = SceneBuilder.Mat("Fence", new Color(0.42f, 0.42f, 0.44f), cutout: true, glossiness: 0.3f);
        fence.mainTexture = ProceduralTextures.ChainLink();
        fence.mainTextureScale = new Vector2(40f, 5f);
        var wall = GeneratedAssets.CorrugatedMetal("WarehouseWall", new Color(0.55f, 0.58f, 0.62f), 6f);
        var roof = GeneratedAssets.CorrugatedMetal("Roof", new Color(0.38f, 0.34f, 0.32f), 8f, glossiness: 0.2f, metallic: 0.05f);
        var door = GeneratedAssets.CorrugatedMetal("RollDoor", new Color(0.3f, 0.32f, 0.35f), 3f, glossiness: 0.5f, metallic: 0.25f);
        var stripe = SceneBuilder.Mat("HazardStripe", new Color(1f, 0.75f, 0.05f), glossiness: 0.3f);
        var black = SceneBuilder.Mat("CheckerBlack", new Color(0.05f, 0.05f, 0.05f), glossiness: 0.2f);
        var container = GeneratedAssets.CorrugatedMetal("Container", new Color(0.12f, 0.3f, 0.5f), 5f);
        var container2 = GeneratedAssets.CorrugatedMetal("Container2", new Color(0.6f, 0.2f, 0.1f), 5f);
        var jersey = GeneratedAssets.WeatheredConcrete("Concrete2", new Color(0.7f, 0.7f, 0.68f), 2.5f);

        float hw = YardW * 0.5f, hd = YardD * 0.5f;
        // Chain-link fence with a gap (the gate) in the middle of the south side
        void FenceRun(Vector3 a, Vector3 b)
        {
            float len = Vector3.Distance(a, b);
            int posts = Mathf.Max(2, Mathf.RoundToInt(len / 4f) + 1);
            for (int i = 0; i < posts; i++)
            {
                var p = Vector3.Lerp(a, b, i / (float)(posts - 1));
                float gy = SceneBuilder.GroundHeight(terrain, p.x, p.z);
                SceneBuilder.Primitive(PrimitiveType.Cylinder, "Fence post", v, new Vector3(p.x, gy + 1.2f, p.z), new Vector3(0.08f, 1.2f, 0.08f), steel);
            }
            var mid = (a + b) * 0.5f;
            float gm = SceneBuilder.GroundHeight(terrain, mid.x, mid.z);
            var mesh = SceneBuilder.Primitive(PrimitiveType.Cube, "Fence mesh", v, new Vector3(mid.x, gm + 1.2f, mid.z), new Vector3(len, 2.3f, 0.03f), fence);
            mesh.transform.rotation = Quaternion.LookRotation(Vector3.Cross(b - a, Vector3.up), Vector3.up);
            mesh.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            var rail = SceneBuilder.Primitive(PrimitiveType.Cube, "Fence rail", v, new Vector3(mid.x, gm + 2.35f, mid.z), new Vector3(len, 0.06f, 0.06f), steel, collider: false);
            rail.transform.rotation = mesh.transform.rotation;
        }
        var sw = new Vector3(Yard.x - hw, 0f, Yard.y - hd);
        var se = new Vector3(Yard.x + hw, 0f, Yard.y - hd);
        var ne = new Vector3(Yard.x + hw, 0f, Yard.y + hd);
        var nw = new Vector3(Yard.x - hw, 0f, Yard.y + hd);
        FenceRun(sw, new Vector3(Yard.x - 5f, 0f, Yard.y - hd));
        FenceRun(new Vector3(Yard.x + 5f, 0f, Yard.y - hd), se);
        FenceRun(se, ne);
        FenceRun(ne, nw);
        FenceRun(nw, sw);
        // Gate pillars with hazard stripes
        foreach (var gx in new[] { Yard.x - 5f, Yard.x + 5f })
        {
            float gy = SceneBuilder.GroundHeight(terrain, gx, Yard.y - hd);
            for (int s = 0; s < 6; s++)
                SceneBuilder.Primitive(PrimitiveType.Cube, "Gate pillar", v, new Vector3(gx, gy + 0.25f + s * 0.5f, Yard.y - hd), new Vector3(0.5f, 0.5f, 0.5f), s % 2 == 0 ? stripe : black);
        }

        // Warehouses along the north side
        void Warehouse(float cx, float cz, Vector3 size, float yaw)
        {
            var w = new GameObject("Warehouse").transform;
            w.SetParent(v);
            w.position = new Vector3(cx, SceneBuilder.GroundHeight(terrain, cx, cz), cz);
            w.rotation = Quaternion.Euler(0f, yaw, 0f);
            SceneBuilder.Primitive(PrimitiveType.Cube, "Walls", w, new Vector3(0f, size.y * 0.5f, 0f), size, wall).isStatic = true;
            SceneBuilder.Primitive(PrimitiveType.Cube, "Roof", w, new Vector3(0f, size.y + 0.25f, 0f), new Vector3(size.x + 0.8f, 0.5f, size.z + 0.8f), roof).isStatic = true;
            SceneBuilder.Primitive(PrimitiveType.Cube, "Roll door", w, new Vector3(-size.x * 0.25f, 2f, size.z * 0.5f + 0.03f), new Vector3(4.5f, 4f, 0.06f), door, collider: false);
            SceneBuilder.Primitive(PrimitiveType.Cube, "Roll door 2", w, new Vector3(size.x * 0.25f, 2f, size.z * 0.5f + 0.03f), new Vector3(4.5f, 4f, 0.06f), door, collider: false);
            SceneBuilder.Primitive(PrimitiveType.Cube, "Stripe", w, new Vector3(0f, size.y - 0.6f, size.z * 0.5f + 0.03f), new Vector3(size.x, 0.4f, 0.04f), stripe, collider: false);
        }
        Warehouse(Yard.x - 20f, Yard.y + hd - 8f, new Vector3(18f, 7f, 12f), 0f);
        Warehouse(Yard.x + 21f, Yard.y + hd - 7f, new Vector3(14f, 6f, 10f), 0f);

        // Floodlight masts at the corners
        foreach (var c in new[] { sw, se, ne, nw })
        {
            var p = Vector3.Lerp(c, new Vector3(Yard.x, 0f, Yard.y), 0.08f);
            float gy = SceneBuilder.GroundHeight(terrain, p.x, p.z);
            SceneBuilder.Primitive(PrimitiveType.Cylinder, "Mast", v, new Vector3(p.x, gy + 6f, p.z), new Vector3(0.18f, 6f, 0.18f), steel);
            var head = SceneBuilder.Primitive(PrimitiveType.Cube, "Floodlights", v, new Vector3(p.x, gy + 12.2f, p.z), new Vector3(1.6f, 0.5f, 0.5f), black, collider: false);
            head.transform.LookAt(new Vector3(Yard.x, gy + 12.2f, Yard.y));
        }

        // Container stacks along the east fence, jersey barriers by the entrance
        for (int i = 0; i < 3; i++)
        {
            float cx = Yard.x + hw - 4f, cz = Yard.y - 14f + i * 13f;
            float gy = SceneBuilder.GroundHeight(terrain, cx, cz);
            SceneBuilder.Primitive(PrimitiveType.Cube, "Container", v, new Vector3(cx, gy + 1.3f, cz), new Vector3(2.4f, 2.6f, 12f), i % 2 == 0 ? container : container2).isStatic = true;
            if (i == 1) SceneBuilder.Primitive(PrimitiveType.Cube, "Container top", v, new Vector3(cx, gy + 3.9f, cz), new Vector3(2.4f, 2.6f, 12f), container).isStatic = true;
        }
        for (int i = 0; i < 6; i++)
        {
            float bx = Yard.x - 14f + i * 2.2f, bz = Yard.y - hd + 6f;
            float gy = SceneBuilder.GroundHeight(terrain, bx, bz);
            SceneBuilder.Primitive(PrimitiveType.Cube, "Jersey barrier", v, new Vector3(bx, gy + 0.45f, bz), new Vector3(2.1f, 0.9f, 0.55f), jersey).isStatic = true;
        }

        // Launch pad just inside the gate
        Pad("YardPad", v, terrain, Asphalt(), Yard.x, Yard.y - hd + 3f, 8f, 5f);
        float py = SceneBuilder.GroundHeight(terrain, Yard.x, Yard.y - hd + 3f);
        var spawn = Spawn(v, "SpawnPoint Yard", new Vector3(Yard.x, py + 0.06f, Yard.y - hd + 3f), 0f);
        return (spawn, spawn);
    }

    // ==================================================================== biome dressing

    /// <summary>Rocks, city blocks and spoil heaps that make each biome read as its own place.</summary>
    public static void BuildLandscapes(Transform root, Terrain terrain)
    {
        var v = new GameObject("Landscape").transform;
        v.SetParent(root);
        var rocks = RockGenerator.Variants(4, 0.32f, "Rock");
        var slabs = RockGenerator.Variants(3, 0.18f, "Slab");
        var rockMat = GeneratedAssets.PbrMaterial("RockFace", "aerial_grass_rock", 0.5f, 0.35f, new Color(1.05f, 0.98f, 0.88f));
        var duskRock = GeneratedAssets.PbrMaterial("QuarryRock", "gravel_road", 0.7f, 0.3f, new Color(0.92f, 0.9f, 0.9f));
        var dirtMat = GeneratedAssets.PbrMaterial("SpoilDirt", "dirt_aerial_02", 0.6f, 0.3f);
        var rng = new System.Random(20260922);
        float Rand(float lo, float hi) => lo + (float)rng.NextDouble() * (hi - lo);

        void Rock(Transform parent, Mesh[] set, Material mat, float x, float z, float scale, float sink = 0.25f)
        {
            float g = SceneBuilder.GroundHeight(terrain, x, z);
            var go = SceneBuilder.MeshObject("Rock", parent, set[rng.Next(set.Length)], mat, new Vector3(x, g - scale * sink, z));
            go.transform.rotation = Quaternion.Euler(Rand(-12f, 12f), Rand(0f, 360f), Rand(-12f, 12f));
            go.transform.localScale = new Vector3(scale * Rand(0.85f, 1.3f), scale * Rand(0.7f, 1.1f), scale * Rand(0.85f, 1.3f));
            var col = go.AddComponent<SphereCollider>();
            col.radius = 0.85f;
            go.isStatic = true;
        }

        // ---- desert: boulder fields, a mesa cliff of stacked slabs, dry scrub ----
        var desert = new GameObject("Desert").transform;
        desert.SetParent(v);
        for (int i = 0; i < 90; i++)
        {
            float a = Rand(0f, Mathf.PI * 2f), r = Rand(38f, 100f);
            float x = Race.x + Mathf.Cos(a) * r, z = Race.y + Mathf.Sin(a) * r;
            if (Keepout(x, z, 8f) || Desert(x, z) < 0.45f) continue;
            Rock(desert, rocks, rockMat, x, z, Rand(0.6f, 3.4f));
        }
        // cliff band along the mesa edge
        for (int i = 0; i < 26; i++)
        {
            float a = Mathf.Lerp(-1.15f, 1.15f, i / 25f);
            float x = Race.x + 74f + Mathf.Cos(a) * 34f, z = Race.y + 18f + Mathf.Sin(a) * 34f;
            Rock(desert, slabs, rockMat, x, z, Rand(4f, 8.5f), 0.45f);
        }

        // ---- meadow: stones and small boulders worked up through the grass ----
        var field = new GameObject("Field stones").transform;
        field.SetParent(v);
        var fieldRock = GeneratedAssets.PbrMaterial("FieldStone", "gravel_road", 0.8f, 0.3f, new Color(0.95f, 0.94f, 0.9f));
        for (int i = 0; i < 220; i++)
        {
            float a = Rand(0f, Mathf.PI * 2f), rr = Rand(22f, 150f);
            float x = Mathf.Cos(a) * rr, z = Mathf.Sin(a) * rr;
            if (Keepout(x, z, 6f) || Meadow(x, z) < 0.6f) continue;
            if (rng.NextDouble() > 0.55) continue;
            Rock(field, rocks, fieldRock, x, z, Rand(0.12f, 0.55f), 0.35f);
        }

        // ---- urban: a block of derelict towers, lamp posts and kerbs ----
        var urban = new GameObject("Urban").transform;
        urban.SetParent(v);
        var wall = GeneratedAssets.PbrMaterial("CityWall", "asphalt_pit_lane", 0.3f, 0.4f, new Color(1f, 0.98f, 0.94f));
        var glass = SceneBuilder.Mat("BuildingWindow", new Color(0.15f, 0.22f, 0.28f), glossiness: 0.7f, metallic: 0.2f);
        var steel = SceneBuilder.Mat("Steel", new Color(0.55f, 0.56f, 0.58f), glossiness: 0.45f, metallic: 0.8f);
        for (int i = 0; i < 9; i++)
        {
            float a = i / 9f * Mathf.PI * 2f + 0.35f;
            float r = Rand(42f, 70f);
            float x = Park.x + Mathf.Cos(a) * r, z = Park.y + Mathf.Sin(a) * r;
            if (Keepout(x, z, 8f)) continue;
            float w = Rand(9f, 15f), d = Rand(9f, 14f), h = Rand(10f, 24f);
            float g = SceneBuilder.GroundHeight(terrain, x, z);
            var b = new GameObject("Tower").transform;
            b.SetParent(urban);
            b.position = new Vector3(x, g, z);
            b.rotation = Quaternion.Euler(0f, Rand(0f, 360f), 0f);
            SceneBuilder.Primitive(PrimitiveType.Cube, "Shell", b, new Vector3(0f, h * 0.5f, 0f), new Vector3(w, h, d), wall).isStatic = true;
            // window bands on all four faces
            int floors = Mathf.Max(3, Mathf.RoundToInt(h / 3.2f));
            for (int fl = 1; fl < floors; fl++)
            {
                float y = fl * (h / floors);
                SceneBuilder.Primitive(PrimitiveType.Cube, "Band", b, new Vector3(0f, y, 0f), new Vector3(w + 0.06f, 1.3f, d - 1.6f), glass, collider: false);
                SceneBuilder.Primitive(PrimitiveType.Cube, "Band", b, new Vector3(0f, y, 0f), new Vector3(w - 1.6f, 1.3f, d + 0.06f), glass, collider: false);
            }
            SceneBuilder.Primitive(PrimitiveType.Cube, "Parapet", b, new Vector3(0f, h + 0.4f, 0f), new Vector3(w + 0.5f, 0.8f, d + 0.5f), wall);
        }
        for (int i = 0; i < 14; i++)
        {
            float a = i / 14f * Mathf.PI * 2f;
            float x = Park.x + Mathf.Cos(a) * 30f, z = Park.y + Mathf.Sin(a) * 26f;
            if (Keepout(x, z, 1f)) continue;
            float g = SceneBuilder.GroundHeight(terrain, x, z);
            SceneBuilder.Primitive(PrimitiveType.Cylinder, "Lamp post", urban, new Vector3(x, g + 3.2f, z), new Vector3(0.12f, 3.2f, 0.12f), steel);
            SceneBuilder.Primitive(PrimitiveType.Cube, "Lamp head", urban, new Vector3(x, g + 6.5f, z), new Vector3(0.9f, 0.18f, 0.35f), steel, collider: false);
        }

        // ---- quarry: boulder piles on the benches and conical spoil heaps ----
        var quarry = new GameObject("Quarry").transform;
        quarry.SetParent(v);
        for (int i = 0; i < 120; i++)
        {
            float a = Rand(0f, Mathf.PI * 2f), r = Rand(36f, 92f);
            float x = Yard.x + Mathf.Cos(a) * r, z = Yard.y + Mathf.Sin(a) * r;
            // leave the approach corridor south of the yard gate clear of boulders
            if (Keepout(x, z, 10f) || Quarry(x, z) < 0.45f) continue;
            if (Mathf.Abs(x - Yard.x) < 14f && z < Yard.y - YardD * 0.5f) continue;
            Rock(quarry, rocks, duskRock, x, z, Rand(0.5f, 3f));
        }
        var heap = GeneratedAssets.SaveMesh(MeshBuilder.Lathe(new[] { new Vector2(1f, 0f), new Vector2(0.92f, 0.18f), new Vector2(0.55f, 0.62f), new Vector2(0.12f, 0.95f), new Vector2(0f, 1f) }, 20, "SpoilHeap"), "SpoilHeap");
        foreach (var (hx, hz, hs) in new[] { (-58f, 34f, 9f), (52f, 40f, 7f), (-22f, 152f, 11f), (30f, 148f, 8f) })
        {
            float x = Yard.x + hx, z = hz;
            if (Keepout(x, z, 6f)) continue;
            float g = SceneBuilder.GroundHeight(terrain, x, z);
            var go = SceneBuilder.MeshObject("Spoil heap", quarry, heap, dirtMat, new Vector3(x, g - 0.3f, z));
            go.transform.localScale = new Vector3(hs, hs * Rand(0.4f, 0.6f), hs);
            go.AddComponent<MeshCollider>();
            go.isStatic = true;
        }
    }
}
