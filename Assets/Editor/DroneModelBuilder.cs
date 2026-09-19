using System.Collections.Generic;
using UnityEngine;
using static MeshBuilder;

/// <summary>
/// Builds a 5-inch freestyle quad from generated meshes: true-X carbon arms with
/// a woven-carbon PBR material, stacked plates on anodised standoffs, FC/ESC stack
/// with capacitor, brushed motor bells with cooling slots, twisted tri-blade props
/// (plus blur discs), battery with strap and XT60, wiring, TPU camera cage with a
/// tilted FPV camera, an HD action camera, VTX and receiver antennas and LED bar.
/// </summary>
public static class DroneModelBuilder
{
    public struct Result
    {
        public Transform[] props;
        public GameObject[] blades;
        public Renderer[] discs;
        public Transform cameraMount;
    }

    class Mats
    {
        public Material carbon, anodized, motorBlack, silver, tpu, tpuAccent, prop, disc, battery, strap, wire, wireRed, xt60, lens, led, capacitor, pcb, white, slot;
    }

    static Mats mats;
    static int partCounter;

    public static Result Build(Transform root, DronePhysics physics, Color accent)
    {
        partCounter = 0;
        mats = MakeMaterials(accent);

        var vis = new GameObject("Model").transform;
        vis.SetParent(root, false);

        BuildFrame(vis, physics);
        BuildStack(vis);
        BuildBattery(vis);
        var camMount = BuildCameras(vis);
        BuildAntennas(vis);

        var result = new Result
        {
            props = new Transform[4],
            blades = new GameObject[4],
            discs = new Renderer[4],
            cameraMount = camMount,
        };
        string[] names = { "FR", "FL", "RL", "RR" };
        for (int i = 0; i < 4; i++)
        {
            Vector3 p = physics.MotorLocalPositions[i];
            BuildMotor(vis, names[i], p);
            BuildMotorWire(vis, names[i], p);
            BuildProp(vis, physics, names[i], p, DronePhysics.SpinDirection[i] > 0f, out result.props[i], out result.blades[i], out result.discs[i]);
        }
        return result;
    }

    // ------------------------------------------------------------------ materials

    static Mats MakeMaterials(Color accent)
    {
        var (carbonA, carbonN) = ProceduralTextures.CarbonWeave();
        var brushed = ProceduralTextures.BrushedMetalNormal();

        var carbon = SceneBuilder.Mat("Carbon", Color.white, glossiness: 0.62f, metallic: 0.05f);
        carbon.mainTexture = carbonA;
        carbon.mainTextureScale = new Vector2(6f, 6f);
        carbon.SetTexture("_BumpMap", carbonN);
        carbon.SetFloat("_BumpScale", 0.9f);
        carbon.EnableKeyword("_NORMALMAP");

        Material Metal(string name, Color c, float smooth, float metal, bool brushedNormal)
        {
            var m = SceneBuilder.Mat(name, c, glossiness: smooth, metallic: metal);
            if (brushedNormal)
            {
                m.SetTexture("_BumpMap", brushed);
                m.SetFloat("_BumpScale", 0.35f);
                m.EnableKeyword("_NORMALMAP");
                m.mainTextureScale = new Vector2(4f, 1f);
            }
            return m;
        }

        return new Mats
        {
            carbon = carbon,
            anodized = Metal("Anodized", accent, 0.72f, 0.85f, true),
            motorBlack = Metal("MotorBlack", new Color(0.09f, 0.09f, 0.1f), 0.6f, 0.8f, true),
            silver = Metal("Silver", new Color(0.62f, 0.62f, 0.64f), 0.75f, 0.9f, false),
            tpu = SceneBuilder.Mat("Tpu", new Color(0.05f, 0.05f, 0.055f), glossiness: 0.28f),
            tpuAccent = SceneBuilder.Mat("TpuAccent", accent * 0.9f, glossiness: 0.3f),
            prop = SceneBuilder.Mat("PropBlade", new Color(1f, 0.5f, 0.06f), glossiness: 0.55f),
            disc = SceneBuilder.Mat("PropDisc", new Color(1f, 0.55f, 0.08f, 0.25f), transparent: true),
            battery = SceneBuilder.Mat("Battery", new Color(0.03f, 0.035f, 0.06f), glossiness: 0.4f),
            strap = SceneBuilder.Mat("Strap", new Color(0.08f, 0.08f, 0.08f), glossiness: 0.12f),
            wire = SceneBuilder.Mat("Wire", new Color(0.03f, 0.03f, 0.03f), glossiness: 0.5f),
            wireRed = SceneBuilder.Mat("WireRed", new Color(0.7f, 0.05f, 0.03f), glossiness: 0.5f),
            xt60 = SceneBuilder.Mat("Xt60", new Color(1f, 0.82f, 0.1f), glossiness: 0.45f),
            lens = SceneBuilder.Mat("Lens", new Color(0.02f, 0.03f, 0.08f), glossiness: 0.98f, metallic: 0.2f),
            led = SceneBuilder.Mat("Led", new Color(1f, 0.1f, 0.1f), emissive: new Color(3f, 0.15f, 0.15f)),
            capacitor = SceneBuilder.Mat("Capacitor", new Color(0.05f, 0.06f, 0.35f), glossiness: 0.5f, metallic: 0.3f),
            pcb = SceneBuilder.Mat("Pcb", new Color(0.08f, 0.22f, 0.1f), glossiness: 0.5f),
            white = SceneBuilder.Mat("Antenna", new Color(0.92f, 0.92f, 0.9f), glossiness: 0.35f),
            slot = SceneBuilder.Mat("MotorSlot", new Color(0.01f, 0.01f, 0.01f), glossiness: 0f),
        };
    }

    // ------------------------------------------------------------------ frame

    static void BuildFrame(Transform vis, DronePhysics physics)
    {
        float armLen = 0.135f;
        var armMesh = Extrude(RoundedRect(armLen, 0.016f, 0.008f, 5), 0.005f, "Arm", 6f);
        float armCenter = armLen * 0.5f - 0.015f;
        for (int i = 0; i < 4; i++)
        {
            float ang = 45f + i * 90f;
            var dir = Quaternion.Euler(0f, ang, 0f) * Vector3.right;
            Part(vis, $"Arm {i}", armMesh, mats.carbon, dir * armCenter, Quaternion.Euler(0f, ang, 0f));
            // arm bolts
            for (int b = 0; b < 2; b++)
                Part(vis, "Bolt", Hex(0.0022f, 0.0012f), mats.silver, dir * (0.018f + b * 0.012f) + Vector3.up * 0.0031f, Quaternion.identity);
        }

        Part(vis, "Brace plate", Extrude(RoundedRect(0.07f, 0.09f, 0.01f, 5), 0.002f, "BracePlate", 6f), mats.carbon, new Vector3(0f, -0.0035f, 0f), Quaternion.identity);
        Part(vis, "Bottom plate", Extrude(RoundedRect(0.085f, 0.13f, 0.012f, 6), 0.0025f, "BottomPlate", 6f), mats.carbon, new Vector3(0f, 0.00375f, 0f), Quaternion.identity);
        Part(vis, "Top plate", Extrude(RoundedRect(0.085f, 0.13f, 0.012f, 6), 0.0025f, "TopPlate", 6f), mats.carbon, new Vector3(0f, 0.03625f, 0f), Quaternion.identity);

        var sidePlate = Extrude(RoundedRect(0.03f, 0.09f, 0.006f, 4), 0.0025f, "SidePlate", 6f);
        Part(vis, "Side plate L", sidePlate, mats.carbon, new Vector3(-0.043f, 0.02f, 0f), Quaternion.Euler(0f, 0f, 90f));
        Part(vis, "Side plate R", sidePlate, mats.carbon, new Vector3(0.043f, 0.02f, 0f), Quaternion.Euler(0f, 0f, 90f));

        var standoff = Hex(0.0035f, 0.03f);
        foreach (var sx in new[] { -0.031f, 0.031f })
            foreach (var sz in new[] { -0.05f, 0.05f })
            {
                Part(vis, "Standoff", standoff, mats.anodized, new Vector3(sx, 0.005f, sz), Quaternion.identity);
                Part(vis, "Bolt", Hex(0.0022f, 0.0012f), mats.silver, new Vector3(sx, 0.0375f, sz), Quaternion.identity);
                Part(vis, "Bolt", Hex(0.0022f, 0.0012f), mats.silver, new Vector3(sx, 0.0013f, sz), Quaternion.Euler(180f, 0f, 0f));
            }

        Part(vis, "LED bar", Box(new Vector3(0.05f, 0.004f, 0.003f), "LedBar"), mats.led, new Vector3(0f, -0.006f, -0.058f), Quaternion.identity);
    }

    static void BuildStack(Transform vis)
    {
        var board = Extrude(RoundedRect(0.032f, 0.032f, 0.004f, 3), 0.0015f, "Board", 20f);
        Part(vis, "ESC", board, mats.pcb, new Vector3(0f, 0.011f, 0f), Quaternion.identity);
        Part(vis, "FC", board, mats.pcb, new Vector3(0f, 0.022f, 0f), Quaternion.identity);
        var post = Hex(0.0022f, 0.026f);
        foreach (var sx in new[] { -0.0125f, 0.0125f })
            foreach (var sz in new[] { -0.0125f, 0.0125f })
                Part(vis, "Stack post", post, mats.anodized, new Vector3(sx, 0.006f, sz), Quaternion.identity);
        // MCU / gyro chips and USB
        Part(vis, "MCU", Box(new Vector3(0.007f, 0.0012f, 0.007f), "Chip"), mats.tpu, new Vector3(0.003f, 0.0233f, 0.002f), Quaternion.identity);
        Part(vis, "Gyro", Box(new Vector3(0.003f, 0.001f, 0.003f), "ChipS"), mats.tpu, new Vector3(-0.006f, 0.0232f, -0.004f), Quaternion.identity);
        Part(vis, "USB", Box(new Vector3(0.008f, 0.003f, 0.006f), "Usb"), mats.silver, new Vector3(-0.012f, 0.0245f, 0.013f), Quaternion.identity);
        // ESC capacitor at the rear
        Part(vis, "Capacitor", Lathe(new[] { new Vector2(0.005f, 0f), new Vector2(0.005f, 0.02f), new Vector2(0.0035f, 0.0215f) }, 16, "Cap"), mats.capacitor,
            new Vector3(0f, 0.013f, -0.03f), Quaternion.Euler(90f, 0f, 0f));
        Part(vis, "Cap lead", Tube(new[] { new Vector3(0f, 0.012f, -0.028f), new Vector3(0f, 0.012f, -0.017f) }, new[] { 0.0008f, 0.0008f }, 6, "CapLead"), mats.wireRed, Vector3.zero, Quaternion.identity);
    }

    static void BuildBattery(Transform vis)
    {
        var center = new Vector3(0f, 0.0555f, -0.006f);
        Part(vis, "Battery", Extrude(RoundedRect(0.036f, 0.076f, 0.005f, 5), 0.034f, "Battery", 8f), mats.battery, center, Quaternion.identity);
        // label band
        Part(vis, "Battery label", Extrude(RoundedRect(0.0365f, 0.03f, 0.005f, 5), 0.0342f, "BatteryLabel", 8f), mats.tpuAccent, center + new Vector3(0f, 0f, -0.012f), Quaternion.identity);
        // strap over battery and under the top plate
        float y0 = 0.0345f, y1 = 0.0735f;
        Part(vis, "Strap top", Box(new Vector3(0.042f, 0.0018f, 0.016f), "StrapTop"), mats.strap, new Vector3(0f, y1, 0.004f), Quaternion.identity);
        Part(vis, "Strap L", Box(new Vector3(0.0018f, y1 - y0, 0.016f), "StrapSide"), mats.strap, new Vector3(-0.021f, (y0 + y1) * 0.5f, 0.004f), Quaternion.identity);
        Part(vis, "Strap R", Box(new Vector3(0.0018f, y1 - y0, 0.016f), "StrapSide"), mats.strap, new Vector3(0.021f, (y0 + y1) * 0.5f, 0.004f), Quaternion.identity);
        Part(vis, "Buckle", Box(new Vector3(0.006f, 0.003f, 0.018f), "Buckle"), mats.tpu, new Vector3(0.019f, y1 + 0.001f, 0.004f), Quaternion.identity);
        // XT60 + leads to the ESC
        Part(vis, "XT60", Box(new Vector3(0.016f, 0.008f, 0.012f), "Xt60"), mats.xt60, new Vector3(0f, 0.045f, 0.038f), Quaternion.identity);
        var leadPath = new[] { new Vector3(0f, 0.045f, 0.044f), new Vector3(0f, 0.04f, 0.052f), new Vector3(0f, 0.026f, 0.05f), new Vector3(0f, 0.014f, 0.03f), new Vector3(0f, 0.012f, 0.017f) };
        var r = new[] { 0.0014f, 0.0014f, 0.0014f, 0.0014f, 0.0014f };
        Part(vis, "Lead red", Tube(Offset(leadPath, 0.0022f), r, 8, "LeadRed"), mats.wireRed, Vector3.zero, Quaternion.identity);
        Part(vis, "Lead black", Tube(Offset(leadPath, -0.0022f), r, 8, "LeadBlack"), mats.wire, Vector3.zero, Quaternion.identity);
    }

    static Transform BuildCameras(Transform vis)
    {
        // TPU cage with the FPV camera
        var cage = Extrude(RoundedRect(0.028f, 0.022f, 0.006f, 4), 0.0025f, "CamCage", 20f);
        Part(vis, "Cam cage L", cage, mats.tpu, new Vector3(-0.0135f, 0.022f, 0.044f), Quaternion.Euler(0f, 0f, 90f));
        Part(vis, "Cam cage R", cage, mats.tpu, new Vector3(0.0135f, 0.022f, 0.044f), Quaternion.Euler(0f, 0f, 90f));
        Part(vis, "Cam cage top", Box(new Vector3(0.03f, 0.0025f, 0.018f), "CageTop"), mats.tpu, new Vector3(0f, 0.0355f, 0.044f), Quaternion.identity);

        var mount = new GameObject("Camera mount").transform;
        mount.SetParent(vis, false);
        mount.localPosition = new Vector3(0f, 0.024f, 0.05f);
        mount.localRotation = Quaternion.Euler(-25f, 0f, 0f);
        Part(mount, "Cam body", Box(new Vector3(0.019f, 0.019f, 0.02f), "CamBody", 20f), mats.tpu, Vector3.zero, Quaternion.identity);
        var barrel = Lathe(new[] { new Vector2(0.0045f, 0f), new Vector2(0.0068f, 0.0015f), new Vector2(0.0068f, 0.008f), new Vector2(0.0058f, 0.0095f), new Vector2(0.0035f, 0.0095f) }, 20, "LensBarrel");
        Part(mount, "Lens barrel", barrel, mats.tpu, new Vector3(0f, 0f, 0.01f), Quaternion.Euler(90f, 0f, 0f));
        Part(mount, "Lens glass", Lathe(new[] { new Vector2(0.0034f, 0f), new Vector2(0.0034f, 0.001f) }, 16, "LensGlass"), mats.lens, new Vector3(0f, 0f, 0.0185f), Quaternion.Euler(90f, 0f, 0f));
        Part(mount, "Cam pivot L", Hex(0.002f, 0.001f), mats.silver, new Vector3(-0.0105f, 0f, 0f), Quaternion.Euler(0f, 0f, 90f));
        Part(mount, "Cam pivot R", Hex(0.002f, 0.001f), mats.silver, new Vector3(0.0105f, 0f, 0f), Quaternion.Euler(0f, 0f, -90f));

        // HD action camera on a TPU mount in front of the battery
        var hd = new GameObject("HD camera").transform;
        hd.SetParent(vis, false);
        hd.localPosition = new Vector3(0f, 0.053f, 0.045f);
        hd.localRotation = Quaternion.Euler(-25f, 0f, 0f);
        Part(hd, "HD body", Extrude(RoundedRect(0.026f, 0.014f, 0.003f, 4), 0.018f, "HdBody", 20f), mats.tpu, Vector3.zero, Quaternion.identity);
        Part(hd, "HD lens ring", Lathe(new[] { new Vector2(0.004f, 0f), new Vector2(0.006f, 0.0008f), new Vector2(0.006f, 0.003f), new Vector2(0.0045f, 0.0035f) }, 20, "HdLensRing"), mats.silver, new Vector3(0.005f, 0.003f, 0.007f), Quaternion.Euler(90f, 0f, 0f));
        Part(hd, "HD lens", Lathe(new[] { new Vector2(0.0035f, 0f), new Vector2(0.0035f, 0.001f) }, 16, "HdLens"), mats.lens, new Vector3(0.005f, 0.003f, 0.0105f), Quaternion.Euler(90f, 0f, 0f));
        Part(vis, "HD mount", Box(new Vector3(0.018f, 0.012f, 0.012f), "HdMount"), mats.tpuAccent, new Vector3(0f, 0.043f, 0.045f), Quaternion.identity);
        return mount;
    }

    static void BuildAntennas(Transform vis)
    {
        // VTX antenna (lollipop) on a TPU mount at the rear
        Part(vis, "VTX mount", Box(new Vector3(0.012f, 0.01f, 0.012f), "VtxMount"), mats.tpuAccent, new Vector3(0f, 0.0425f, -0.058f), Quaternion.identity);
        var a0 = new Vector3(0f, 0.045f, -0.058f);
        var dir = Quaternion.Euler(-35f, 0f, 0f) * Vector3.up; // up and back... rotate about X: negative = tilt towards -Z
        dir = new Vector3(0f, Mathf.Cos(35f * Mathf.Deg2Rad), -Mathf.Sin(35f * Mathf.Deg2Rad));
        var a1 = a0 + dir * 0.07f;
        Part(vis, "VTX antenna", Tube(new[] { a0, a0 + dir * 0.035f, a1 }, new[] { 0.003f, 0.003f, 0.003f }, 12, "VtxAntenna"), mats.white, Vector3.zero, Quaternion.identity);
        Part(vis, "VTX antenna head", Lathe(new[] { new Vector2(0.003f, 0f), new Vector2(0.0075f, 0.004f), new Vector2(0.0075f, 0.012f), new Vector2(0.003f, 0.016f), new Vector2(0f, 0.017f) }, 16, "AntennaHead"),
            mats.white, a1, Quaternion.FromToRotation(Vector3.up, dir));
        // receiver antennas: two thin wires sticking out at the back
        foreach (var side in new[] { -1f, 1f })
        {
            var s0 = new Vector3(side * 0.018f, 0.008f, -0.056f);
            var d = new Vector3(side * 0.5f, 0.35f, -0.8f).normalized;
            Part(vis, "RX antenna", Tube(new[] { s0, s0 + d * 0.03f, s0 + d * 0.06f }, new[] { 0.0008f, 0.0008f, 0.0008f }, 6, $"RxAntenna_{side}"), mats.white, Vector3.zero, Quaternion.identity);
            Part(vis, "RX antenna tip", Tube(new[] { s0 + d * 0.045f, s0 + d * 0.06f }, new[] { 0.0011f, 0.0011f }, 6, $"RxTip_{side}"), mats.tpu, Vector3.zero, Quaternion.identity);
        }
    }

    // ------------------------------------------------------------------ motors & props

    static void BuildMotor(Transform vis, string name, Vector3 p)
    {
        Part(vis, $"Motor base {name}", Lathe(new[] { new Vector2(0.0135f, 0.0025f), new Vector2(0.0135f, 0.0045f), new Vector2(0.0105f, 0.0045f) }, 24, "MotorBase"), mats.silver, p, Quaternion.identity);
        Part(vis, $"Stator {name}", Lathe(new[] { new Vector2(0.0108f, 0.0045f), new Vector2(0.0108f, 0.011f) }, 24, "Stator", capBottom: false), mats.motorBlack, p, Quaternion.identity);
        Part(vis, $"Bell {name}", Lathe(new[] { new Vector2(0.0145f, 0.0095f), new Vector2(0.0148f, 0.02f), new Vector2(0.0142f, 0.0235f), new Vector2(0.0115f, 0.0245f), new Vector2(0.0075f, 0.025f) }, 32, "Bell", capBottom: false), mats.motorBlack, p, Quaternion.identity);
        Part(vis, $"Bell cap {name}", Lathe(new[] { new Vector2(0.0075f, 0.025f), new Vector2(0.0075f, 0.0265f), new Vector2(0.0045f, 0.027f) }, 24, "BellCap"), mats.anodized, p, Quaternion.identity);
        Part(vis, $"Shaft {name}", Lathe(new[] { new Vector2(0.0025f, 0.027f), new Vector2(0.0025f, 0.0315f) }, 12, "Shaft"), mats.silver, p, Quaternion.identity);
        Part(vis, $"Prop nut {name}", Hex(0.004f, 0.005f), mats.anodized, p + new Vector3(0f, 0.0375f, 0f), Quaternion.identity);
        // cooling slots in the bell
        var slot = Box(new Vector3(0.0016f, 0.008f, 0.0012f), "Slot");
        for (int s = 0; s < 12; s++)
        {
            float a = s * 30f;
            var rot = Quaternion.Euler(0f, a, 0f);
            Part(vis, "Slot", slot, mats.slot, p + rot * new Vector3(0f, 0.0165f, 0.0146f), rot);
        }
        // motor bolts on the arm
        for (int b = 0; b < 4; b++)
        {
            var rot = Quaternion.Euler(0f, 45f + b * 90f, 0f);
            Part(vis, "Motor bolt", Hex(0.0015f, 0.0008f), mats.silver, p + rot * new Vector3(0f, -0.0029f, 0.008f), Quaternion.Euler(180f, 0f, 0f));
        }
    }

    static void BuildMotorWire(Transform vis, string name, Vector3 motor)
    {
        var dir = new Vector3(motor.x, 0f, motor.z).normalized;
        var p0 = dir * 0.02f + new Vector3(0f, 0.0075f, 0f);
        var p1 = dir * 0.05f + new Vector3(0f, 0.0045f, 0f);
        var p2 = dir * 0.1f + new Vector3(0f, 0.004f, 0f);
        var p3 = motor + new Vector3(0f, 0.005f, 0f) - dir * 0.012f;
        var side = Vector3.Cross(Vector3.up, dir) * 0.0016f;
        for (int w = -1; w <= 1; w++)
        {
            var path = new[] { p0 + side * w, p1 + side * w, p2 + side * w, p3 + side * w * 0.3f };
            Part(vis, "Motor wire", Tube(path, new[] { 0.0007f, 0.0007f, 0.0007f, 0.0007f }, 6, $"MotorWire_{name}_{w + 1}"), mats.wire, Vector3.zero, Quaternion.identity);
        }
    }

    static void BuildProp(Transform vis, DronePhysics physics, string name, Vector3 p, bool ccw, out Transform propT, out GameObject bladesGo, out Renderer disc)
    {
        propT = new GameObject($"Prop {name}").transform;
        propT.SetParent(vis, false);
        propT.localPosition = p + new Vector3(0f, 0.0315f, 0f);

        Part(propT, "Hub", Lathe(new[] { new Vector2(0.0065f, 0f), new Vector2(0.0065f, 0.0055f), new Vector2(0.0045f, 0.006f) }, 20, "PropHub"), mats.tpu, Vector3.zero, Quaternion.identity);

        bladesGo = new GameObject("Blades");
        bladesGo.transform.SetParent(propT, false);
        bladesGo.transform.localPosition = new Vector3(0f, 0.003f, 0f);
        float pitchSign = ccw ? 1f : -1f;
        var bladeMesh = Blade(0.0055f, physics.propRadius, 0.019f, 0.011f, 30f * pitchSign, 13f * pitchSign, 0.0016f, 0.045f, 14, 7, ccw ? "BladeCCW" : "BladeCW");
        for (int b = 0; b < physics.propBlades; b++)
            Part(bladesGo.transform, $"Blade {b}", bladeMesh, mats.prop, Vector3.zero, Quaternion.Euler(0f, b * 360f / physics.propBlades, 0f));

        var discGo = Part(propT, "Blur disc", Lathe(new[] { new Vector2(0.006f, 0f), new Vector2(physics.propRadius, 0f), new Vector2(physics.propRadius, 0.0006f), new Vector2(0.006f, 0.0006f) }, 40, "BlurDisc", capBottom: false, capTop: false),
            mats.disc, new Vector3(0f, 0.003f, 0f), Quaternion.identity);
        disc = discGo.GetComponent<Renderer>();
        disc.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        disc.enabled = false;
    }

    // ------------------------------------------------------------------ helpers

    static Mesh Hex(float r, float h) => Lathe(new[] { new Vector2(r, 0f), new Vector2(r, h) }, 6, $"Hex_{r * 1000f:0.0}x{h * 1000f:0.0}", smooth: false);

    static Vector3[] Offset(Vector3[] path, float dx)
    {
        var o = new Vector3[path.Length];
        for (int i = 0; i < path.Length; i++) o[i] = path[i] + new Vector3(dx, 0f, 0f);
        return o;
    }

    static readonly Dictionary<string, Mesh> savedMeshes = new Dictionary<string, Mesh>();

    static GameObject Part(Transform parent, string name, Mesh mesh, Material mat, Vector3 localPos, Quaternion localRot)
    {
        // Persist each distinct generated mesh once as an asset (meshes may be reused for several parts).
        Mesh saved;
        if (UnityEditor.AssetDatabase.Contains(mesh))
        {
            saved = mesh;
        }
        else
        {
            string key = mesh.name;
            if (!savedMeshes.TryGetValue(key, out saved) || saved == null)
            {
                saved = GeneratedAssets.SaveMesh(mesh, "Drone_" + key);
                savedMeshes[key] = saved;
            }
        }
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localRotation = localRot;
        go.AddComponent<MeshFilter>().sharedMesh = saved;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        partCounter++;
        return go;
    }

    public static void ResetCache() => savedMeshes.Clear();
}
