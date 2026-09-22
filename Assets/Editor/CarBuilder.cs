using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Builds an exact, recognisable car model to use as a demolition target: a sedan
/// body (lower hull + cabin), raked windshield/rear window, side windows, mirrors,
/// door trim, exhaust, licence plates, alloy-style wheels, and breakaway bumpers -
/// on a hard hit the wheels and bumpers detach on breakable joints and the glass
/// shatters (via CarWreckage), independent of the body's own explosion physics.
/// </summary>
public static class CarBuilder
{
    public const float Length = 4.2f;
    public const float Width = 1.78f;
    public const float WheelRadius = 0.33f;
    const float WheelWidth = 0.24f;
    const float Clearance = 0.30f;
    const float LowerHeight = 0.5f;
    const float CabinHeight = 0.5f;
    const float CabinWidth = 1.55f;
    public const float TopHeight = Clearance + LowerHeight + CabinHeight;

    class Mats
    {
        public Material glass, bumper, tire, rim, headlight, taillight, marker, chrome, plate, trim;
    }

    static Mats mats;
    static readonly Dictionary<string, Mesh> cache = new Dictionary<string, Mesh>();

    public static void ResetCache() => cache.Clear();

    static Mesh Cached(Mesh m, string name)
    {
        if (!cache.TryGetValue(name, out var saved) || saved == null)
        {
            saved = GeneratedAssets.SaveMesh(m, name);
            cache[name] = saved;
        }
        return saved;
    }

    static Mats MakeMats()
    {
        return new Mats
        {
            glass = SceneBuilder.Mat("CarGlass", new Color(0.05f, 0.08f, 0.12f, 0.5f), transparent: true, glossiness: 0.95f, metallic: 0.2f),
            bumper = SceneBuilder.Mat("CarBumper", new Color(0.08f, 0.08f, 0.09f), glossiness: 0.35f),
            tire = SceneBuilder.Mat("CarTire", new Color(0.035f, 0.035f, 0.035f), glossiness: 0.25f),
            rim = SceneBuilder.Mat("CarRim", new Color(0.72f, 0.72f, 0.75f), glossiness: 0.75f, metallic: 0.85f),
            headlight = SceneBuilder.Mat("CarHeadlight", Color.white, emissive: new Color(3f, 3f, 2.6f), glossiness: 0.9f),
            taillight = SceneBuilder.Mat("CarTaillight", new Color(0.6f, 0.05f, 0.05f), emissive: new Color(2f, 0.1f, 0.1f), glossiness: 0.7f),
            marker = SceneBuilder.Mat("TargetMarker", new Color(1f, 0.25f, 0.05f), emissive: new Color(3f, 0.6f, 0.05f), glossiness: 0.6f),
            chrome = SceneBuilder.Mat("CarChrome", new Color(0.8f, 0.8f, 0.82f), glossiness: 0.85f, metallic: 0.9f),
            plate = SceneBuilder.Mat("CarPlate", new Color(0.9f, 0.9f, 0.85f), glossiness: 0.3f),
            trim = SceneBuilder.Mat("CarTrim", new Color(0.05f, 0.05f, 0.055f), glossiness: 0.4f),
        };
    }

    /// <summary>Builds one target car at ground position, facing yaw degrees. Returns the body's DemoBlock.</summary>
    public static DemoBlock Build(Transform parent, Vector3 groundPos, float yaw, Color paint, string name)
    {
        mats ??= MakeMats();
        var paintMat = SceneBuilder.Mat($"CarPaint_{ColorUtility.ToHtmlStringRGB(paint)}", paint, glossiness: 0.85f, metallic: 0.35f);

        var root = new GameObject(name);
        root.transform.SetParent(parent, false);
        root.transform.position = groundPos;
        root.transform.rotation = Quaternion.Euler(0f, yaw, 0f);

        var visual = new GameObject("Body").transform;
        visual.SetParent(root.transform, false);

        // Body = two convex side-profile extrusions (Extrude needs convex polygons for its cap
        // fans, and a full sedan silhouette is concave at the base of each window): a lower hull
        // with a rising hood and falling trunk, plus a trapezoid cabin whose sloped front/back
        // faces are the windshield and rear window. Profiles are (length z, height y); the
        // extrusion runs across the width. Coordinates are in the car's own frame (+Z forward).
        const float belt = Clearance + LowerHeight;          // beltline
        const float roofY = belt + CabinHeight;
        const float wsBaseZ = Length * 0.5f - 1.35f;         // windshield meets the hood
        const float rwBaseZ = -Length * 0.5f + 0.95f;        // rear window meets the trunk
        const float roofFrontZ = wsBaseZ - 0.75f, roofRearZ = rwBaseZ + 0.55f;
        var hullProfile = new[]
        {
            new Vector2(-Length * 0.5f, Clearance),           // rear bottom
            new Vector2(Length * 0.5f, Clearance),            // front bottom
            new Vector2(Length * 0.5f, Clearance + 0.36f),    // hood tip
            new Vector2(wsBaseZ, belt),                       // hood back
            new Vector2(rwBaseZ, belt),                       // trunk front
            new Vector2(-Length * 0.5f, Clearance + 0.38f),   // trunk back
        };
        var cabinProfile = new[]
        {
            new Vector2(rwBaseZ, belt),
            new Vector2(wsBaseZ, belt),
            new Vector2(roofFrontZ, roofY),
            new Vector2(roofRearZ, roofY),
        };
        // Extrude builds (x, z)-profiles along Y; this permutation maps profile x -> world Z,
        // profile z -> world Y and the extrusion (local Y) -> world X (a proper rotation, det +1).
        var sideToWorld = new Matrix4x4(new Vector4(0f, 0f, 1f, 0f), new Vector4(1f, 0f, 0f, 0f), new Vector4(0f, 1f, 0f, 0f), new Vector4(0f, 0f, 0f, 1f));
        var bodyMesh = MeshBuilder.Combine(new[]
        {
            (MeshBuilder.Extrude(hullProfile, Width, "CarLower", 1.2f), sideToWorld),
            (MeshBuilder.Extrude(cabinProfile, CabinWidth, "CarCabin", 1.2f), sideToWorld),
        }, "CarBody");
        SceneBuilder.MeshObject("Hull", visual, Cached(bodyMesh, "CarBody"), paintMat, Vector3.zero);

        // Glass lies on the cabin's sloped faces, pushed a hair out along each face normal.
        Vector2 wsDir = new Vector2(roofFrontZ - wsBaseZ, roofY - belt);   // (dz, dy) up the windshield
        Vector2 rwDir = new Vector2(roofRearZ - rwBaseZ, roofY - belt);    // (dz, dy) up the rear window
        float wsLen = wsDir.magnitude, rwLen = rwDir.magnitude;
        float wsAngle = Mathf.Atan2(wsDir.y, -wsDir.x) * Mathf.Rad2Deg;  // slope from horizontal
        float rwAngle = Mathf.Atan2(rwDir.y, rwDir.x) * Mathf.Rad2Deg;
        Vector3 wsMid = new Vector3(0f, belt + wsDir.y * 0.5f, wsBaseZ + wsDir.x * 0.5f) + new Vector3(0f, wsDir.x / wsLen * -1f, wsDir.y / wsLen) * 0.03f;
        Vector3 rwMid = new Vector3(0f, belt + rwDir.y * 0.5f, rwBaseZ + rwDir.x * 0.5f) + new Vector3(0f, rwDir.x / rwLen, -rwDir.y / rwLen) * 0.03f;
        var wind = SceneBuilder.MeshObject("Windshield", visual, Cached(MeshBuilder.Box(new Vector3(CabinWidth - 0.1f, 0.05f, wsLen), "CarWindshield"), "CarWindshield"), mats.glass, wsMid);
        wind.transform.localRotation = Quaternion.Euler(wsAngle, 0f, 0f);
        var rear = SceneBuilder.MeshObject("RearWindow", visual, Cached(MeshBuilder.Box(new Vector3(CabinWidth - 0.15f, 0.05f, rwLen), "CarRearWindow"), "CarRearWindow"), mats.glass, rwMid);
        rear.transform.localRotation = Quaternion.Euler(-rwAngle, 0f, 0f);

        // Side windows sit in the cabin's vertical flanks; mirrors hang off the A-pillar base.
        var glassRenderers = new List<Renderer> { wind.GetComponent<Renderer>(), rear.GetComponent<Renderer>() };
        float sideWinZ = (wsBaseZ + rwBaseZ + roofFrontZ + roofRearZ) * 0.25f;
        foreach (var side in new[] { -1f, 1f })
        {
            var sw = SceneBuilder.MeshObject("SideWindow", visual, Cached(MeshBuilder.Box(new Vector3(0.03f, 0.3f, 1.2f), "CarSideWindow"), "CarSideWindow"),
                mats.glass, new Vector3(side * (CabinWidth * 0.5f + 0.005f), belt + CabinHeight * 0.5f, sideWinZ));
            glassRenderers.Add(sw.GetComponent<Renderer>());

            var mirror = SceneBuilder.MeshObject("Mirror", visual, Cached(MeshBuilder.Box(new Vector3(0.05f, 0.11f, 0.16f), "CarMirror"), "CarMirror"),
                mats.trim, new Vector3(side * (CabinWidth * 0.5f + 0.08f), belt + 0.12f, wsBaseZ - 0.12f));
            mirror.transform.localRotation = Quaternion.Euler(0f, side * 20f, 0f);

            // Door trim strips (front and rear door edges), kept within the hull's flank height.
            foreach (var z in new[] { 0.35f, -0.45f })
                SceneBuilder.MeshObject("DoorTrim", visual, Cached(MeshBuilder.Box(new Vector3(0.015f, 0.42f, 0.02f), "CarDoorTrim"), "CarDoorTrim"),
                    mats.trim, new Vector3(side * (Width * 0.5f + 0.006f), Clearance + 0.29f, z));
        }

        // Head/tail lights, inside the front/rear faces of the hull.
        foreach (var side in new[] { -1f, 1f })
        {
            SceneBuilder.MeshObject("Headlight", visual, Cached(MeshBuilder.Box(new Vector3(0.28f, 0.12f, 0.06f), "CarHeadlight"), "CarHeadlight"),
                mats.headlight, new Vector3(side * (Width * 0.5f - 0.2f), Clearance + 0.27f, Length * 0.5f - 0.01f));
            SceneBuilder.MeshObject("Taillight", visual, Cached(MeshBuilder.Box(new Vector3(0.22f, 0.12f, 0.06f), "CarTaillight"), "CarTaillight"),
                mats.taillight, new Vector3(side * (Width * 0.5f - 0.2f), Clearance + 0.28f, -(Length * 0.5f - 0.01f)));
        }

        // Licence plates, on the bumpers' outer faces.
        const float bumperY = Clearance + 0.09f, bumperFace = Length * 0.5f - 0.05f + 0.09f + 0.006f;
        SceneBuilder.MeshObject("PlateFront", visual, Cached(MeshBuilder.Box(new Vector3(0.34f, 0.11f, 0.01f), "CarPlate"), "CarPlate"),
            mats.plate, new Vector3(0f, bumperY, bumperFace));
        SceneBuilder.MeshObject("PlateRear", visual, Cached(MeshBuilder.Box(new Vector3(0.34f, 0.11f, 0.01f), "CarPlate"), "CarPlate"),
            mats.plate, new Vector3(0f, bumperY, -bumperFace));

        // Exhaust pipe.
        var exhaust = SceneBuilder.Primitive(PrimitiveType.Cylinder, "Exhaust", visual, new Vector3(Width * 0.28f, Clearance * 0.45f, -(Length * 0.5f + 0.08f)), new Vector3(0.09f, 0.13f, 0.09f), mats.chrome, collider: false);
        exhaust.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

        // Body physics: two box colliders (hull + cabin) on the root, one rigidbody, one DemoBlock.
        var bodyRb = root.AddComponent<Rigidbody>();
        bodyRb.mass = 150f;
        bodyRb.interpolation = RigidbodyInterpolation.Interpolate;
        var col1 = root.AddComponent<BoxCollider>();
        col1.center = new Vector3(0f, Clearance + LowerHeight * 0.5f, 0f);
        col1.size = new Vector3(Width, LowerHeight, Length);
        var col2 = root.AddComponent<BoxCollider>();
        col2.center = new Vector3(0f, belt + CabinHeight * 0.5f, (wsBaseZ + rwBaseZ) * 0.5f);
        col2.size = new Vector3(CabinWidth, CabinHeight, (wsBaseZ - rwBaseZ + roofFrontZ - roofRearZ) * 0.5f); // box approximating the trapezoid cabin
        var block = root.AddComponent<DemoBlock>();

        var wreck = root.AddComponent<CarWreckage>();
        wreck.glassRenderers = glassRenderers.ToArray();

        // Wheels: two-tone alloy look (dark tire + bright rim + lug nuts), each its own rigidbody
        // joined to the body with a breakable fixed joint so a hard hit pops it free.
        Vector3[] wheelLocal =
        {
            new Vector3(Width * 0.5f - 0.04f, WheelRadius, Length * 0.5f - 0.75f),
            new Vector3(-(Width * 0.5f - 0.04f), WheelRadius, Length * 0.5f - 0.75f),
            new Vector3(Width * 0.5f - 0.04f, WheelRadius, -(Length * 0.5f - 0.7f)),
            new Vector3(-(Width * 0.5f - 0.04f), WheelRadius, -(Length * 0.5f - 0.7f)),
        };
        foreach (var lp in wheelLocal)
            BuildWheel(root.transform, bodyRb, lp);

        // Bumpers: separate breakaway pieces (same joint mechanism as the wheels).
        BuildBumper(root.transform, bodyRb, "FrontBumper", new Vector3(0f, bumperY, Length * 0.5f - 0.05f), new Vector3(Width + 0.05f, 0.22f, 0.18f));
        BuildBumper(root.transform, bodyRb, "RearBumper", new Vector3(0f, bumperY, -(Length * 0.5f - 0.05f)), new Vector3(Width + 0.05f, 0.22f, 0.18f));

        return block;
    }

    static void BuildWheel(Transform parent, Rigidbody bodyRb, Vector3 lp)
    {
        bool rightSide = lp.x > 0f;
        var wheel = SceneBuilder.Primitive(PrimitiveType.Cylinder, "Wheel", parent, lp, new Vector3(WheelRadius * 2f, WheelWidth * 0.5f, WheelRadius * 2f), mats.tire);
        wheel.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);

        // Local +Y on the wheel maps to world -X after the 90 deg Z rotation, regardless of which
        // side the wheel is on - so the sign that puts the rim on the OUTER (visible) face flips
        // between the left and right wheels. Child transforms under a scaled primitive are
        // expressed as a FRACTION of that primitive's own half-extent (Y=1.0 sits exactly on its
        // flat face), not in metres - so faceY/lug positions below are fractions, not distances.
        float outward = rightSide ? -1f : 1f;
        var rim = SceneBuilder.Primitive(PrimitiveType.Cylinder, "Rim", wheel.transform, new Vector3(0f, outward * 0.92f, 0f), new Vector3(0.82f, 0.14f, 0.82f), mats.rim, collider: false);
        for (int i = 0; i < 5; i++)
        {
            float a = i * 72f * Mathf.Deg2Rad;
            SceneBuilder.Primitive(PrimitiveType.Cylinder, "LugNut", rim.transform, new Vector3(Mathf.Cos(a) * 0.5f, 1.0f, Mathf.Sin(a) * 0.5f), new Vector3(0.09f, 0.1f, 0.09f), mats.chrome, collider: false);
        }

        var wrb = wheel.AddComponent<Rigidbody>();
        wrb.mass = 14f;
        wrb.interpolation = RigidbodyInterpolation.Interpolate;
        var joint = wheel.AddComponent<FixedJoint>();
        joint.connectedBody = bodyRb;
        joint.breakForce = 9000f;
        joint.breakTorque = 9000f;
        joint.enableCollision = false;
    }

    static void BuildBumper(Transform parent, Rigidbody bodyRb, string name, Vector3 localPos, Vector3 size)
    {
        var go = SceneBuilder.MeshObject(name, parent, Cached(MeshBuilder.Box(size, "CarBumperShape"), "CarBumperShape"), mats.bumper, localPos);
        var col = go.AddComponent<BoxCollider>();
        col.size = size;
        var rb = go.AddComponent<Rigidbody>();
        rb.mass = 9f;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        var joint = go.AddComponent<FixedJoint>();
        joint.connectedBody = bodyRb;
        joint.breakForce = 5500f;
        joint.breakTorque = 5500f;
        joint.enableCollision = false;
    }

    /// <summary>A bright spinning marker above the car so it reads unmistakably as the target.</summary>
    public static void AddTargetMarker(Transform carRoot)
    {
        mats ??= MakeMats();
        SceneBuilder.Primitive(PrimitiveType.Cylinder, "MarkerPole", carRoot, new Vector3(0f, TopHeight + 0.9f, 0f), new Vector3(0.02f, 0.9f, 0.02f), mats.marker, collider: false);
        var diamond = SceneBuilder.Primitive(PrimitiveType.Cube, "MarkerDiamond", carRoot, new Vector3(0f, TopHeight + 1.9f, 0f), new Vector3(0.35f, 0.35f, 0.35f), mats.marker, collider: false);
        diamond.transform.localRotation = Quaternion.Euler(45f, 45f, 0f);
        diamond.AddComponent<SpinMarker>().degPerSec = 130f;
    }
}
