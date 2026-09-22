using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_POST_PROCESSING_STACK_V2
using UnityEngine.Rendering.PostProcessing;
#endif

/// <summary>
/// Builds the complete training scene from code:  menu  Drone Trainer > Build Training Scene.
/// Uses the CC0 Poly Haven textures/HDRI in Assets/External plus generated meshes and textures.
/// </summary>
public static class SceneBuilder
{
    const string ScenePath = "Assets/Scenes/Training.unity";
    const string MatDir = "Assets/Materials";
    const string HdriPath = "Assets/External/PolyHaven/sky_kloofendal_48d_2k.hdr";
    static readonly Color Accent = new Color(0.1f, 0.75f, 0.95f);

    [MenuItem("Drone Trainer/Build Training Scene")]
    public static void Build()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        EnsureFolder("Assets/Scenes");
        EnsureFolder(MatDir);
        DroneModelBuilder.ResetCache();
        CarBuilder.ResetCache();

        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        var defaultCam = GameObject.Find("Main Camera");
        if (defaultCam != null) Object.DestroyImmediate(defaultCam);

        var light = GameObject.Find("Directional Light");
        if (light == null) { light = new GameObject("Directional Light"); light.AddComponent<Light>().type = LightType.Directional; }
        var l = light.GetComponent<Light>();
        l.shadows = LightShadows.Soft;
        l.intensity = 1.6f;
        l.color = new Color(1f, 0.95f, 0.88f);
        l.shadowStrength = 0.9f;
        l.shadowBias = 0.03f;
        l.shadowNormalBias = 0.3f;

        SetupSkyAndFog(l);

        var terrain = BuildTerrain();
        BuildEnvironment(terrain);

        var spawn = new GameObject("SpawnPoint").transform;
        spawn.position = new Vector3(0f, 0.06f, 0f);

        var drone = BuildDrone(spawn);
        var cams = BuildCameras(drone.transform);
        var course = BuildCourse(terrain);
        BuildTrainer(drone, cams, course);
        BuildPostProcessing(cams);

        DynamicGI.UpdateEnvironment();
        EditorSceneManager.SaveScene(scene, ScenePath);
        AddToBuildSettings();
        ConfigureProject();
        AssetDatabase.SaveAssets();

        Debug.Log("<b>Drone Trainer</b>: scene built at " + ScenePath + ". Press Play, throttle down, SPACE to arm.");
    }

    [MenuItem("Drone Trainer/Play Training Scene")]
    public static void Play()
    {
        if (!System.IO.File.Exists(ScenePath)) Build();
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        EnterPlayWhenReady();
    }

    static double playReadyAt;

    /// <summary>Enters Play mode once the editor has finished importing/compiling (delayCall alone can fire too early on startup).</summary>
    static void EnterPlayWhenReady()
    {
        playReadyAt = EditorApplication.timeSinceStartup + 2.0;
        EditorApplication.update -= TryEnterPlay;
        EditorApplication.update += TryEnterPlay;
    }

    static void TryEnterPlay()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.timeSinceStartup < playReadyAt) return;
        EditorApplication.update -= TryEnterPlay;
        if (!EditorApplication.isPlayingOrWillChangePlaymode) EditorApplication.EnterPlaymode();
    }

    [MenuItem("Drone Trainer/Rebuild and Play")]
    public static void BuildAndPlay()
    {
        Build();
        EnterPlayWhenReady();
    }

    /// <summary>Builds a standalone Windows player into Build/DroneTrainer.exe (menu or -executeMethod).</summary>
    [MenuItem("Drone Trainer/Build Windows .exe")]
    public static void BuildPlayer()
    {
        if (!System.IO.File.Exists(ScenePath)) Build();
        string outDir = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath), "Build");
        System.IO.Directory.CreateDirectory(outDir);
        var options = new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = System.IO.Path.Combine(outDir, "DroneTrainer.exe"),
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None,
        };
        var report = BuildPipeline.BuildPlayer(options);
        Debug.Log($"<b>Drone Trainer</b>: player build {report.summary.result} -> {options.locationPathName} ({report.summary.totalSize / 1048576} MB, {report.summary.totalErrors} errors)");
    }

    // ------------------------------------------------------------------ sky

    static void SetupSkyAndFog(Light sun)
    {
        var hdri = AssetDatabase.LoadAssetAtPath<Texture>(HdriPath);
        if (hdri is Cubemap)
        {
            var sky = Mat("Sky", Color.white, shaderName: "Skybox/Cubemap");
            sky.SetTexture("_Tex", hdri);
            sky.SetFloat("_Exposure", 1.0f);
            sky.SetFloat("_Rotation", 0f);
            RenderSettings.skybox = sky;
            var sunDir = FindSunDirection();
            sun.transform.rotation = Quaternion.LookRotation(-sunDir, Vector3.up);
            Debug.Log($"Sun direction from HDRI: {sunDir}  (elevation {Mathf.Asin(sunDir.y) * Mathf.Rad2Deg:0}°)");
        }
        else
        {
            Debug.LogWarning("HDRI not imported as a cubemap - falling back to the procedural sky.");
            var sky = Mat("Sky", Color.white, shaderName: "Skybox/Procedural");
            RenderSettings.skybox = sky;
            sun.transform.rotation = Quaternion.Euler(48f, -60f, 0f);
        }
        RenderSettings.sun = sun;
        RenderSettings.ambientMode = AmbientMode.Skybox;
        RenderSettings.ambientIntensity = 1.0f;
        RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
        RenderSettings.defaultReflectionResolution = 512;
        RenderSettings.reflectionIntensity = 1f;
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogDensity = 0.0012f;
        RenderSettings.fogColor = new Color(0.72f, 0.78f, 0.86f);
    }

    /// <summary>Direction towards the brightest texel of the lat-long HDRI (temporarily imported as a readable 2-D texture).</summary>
    static Vector3 FindSunDirection()
    {
        var imp = (TextureImporter)AssetImporter.GetAtPath(HdriPath);
        PbrTextureImporter.SkipHdrRule = true;
        imp.textureShape = TextureImporterShape.Texture2D;
        imp.isReadable = true;
        imp.maxTextureSize = 1024;
        imp.SaveAndReimport();
        PbrTextureImporter.SkipHdrRule = false;
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(HdriPath);
        if (tex == null)
        {
            Debug.LogWarning("Could not read the HDRI as a 2-D texture; using a default sun direction.");
            imp.textureShape = TextureImporterShape.TextureCube;
            imp.generateCubemap = TextureImporterGenerateCubemap.Cylindrical;
            imp.isReadable = false;
            imp.maxTextureSize = 2048;
            imp.SaveAndReimport();
            return new Vector3(0.5f, 0.74f, -0.45f).normalized;
        }
        var px = tex.GetPixels();
        int w = tex.width, h = tex.height, best = 0;
        float bestL = -1f;
        for (int i = 0; i < px.Length; i++)
        {
            float lum = px[i].r + px[i].g + px[i].b;
            if (lum > bestL) { bestL = lum; best = i; }
        }
        float u = (best % w + 0.5f) / w, v = (best / w + 0.5f) / h;
        // Unity's cylindrical (lat-long) cubemap: u = 0.5 faces +Z... measured empirically the centre column looks down -Z? Use the
        // convention of the Skybox/Cubemap shader: longitude increases towards +X from the centre column which faces +Z.
        float lon = (u - 0.5f) * 2f * Mathf.PI;
        float lat = (v - 0.5f) * Mathf.PI;
        var dir = new Vector3(Mathf.Sin(lon) * Mathf.Cos(lat), Mathf.Sin(lat), Mathf.Cos(lon) * Mathf.Cos(lat));

        imp.textureShape = TextureImporterShape.TextureCube;
        imp.generateCubemap = TextureImporterGenerateCubemap.Cylindrical;
        imp.isReadable = false;
        imp.maxTextureSize = 2048;
        imp.SaveAndReimport();
        return dir.normalized;
    }

    // ------------------------------------------------------------------ materials

    public static Material Mat(string name, Color color, bool transparent = false, Color? emissive = null,
        float glossiness = 0.25f, float metallic = 0f, string shaderName = "Standard", bool cutout = false)
    {
        string path = $"{MatDir}/{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        var shader = Shader.Find(shaderName);
        if (mat == null)
        {
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, path);
        }
        else if (mat.shader != shader)
        {
            mat.shader = shader;
        }
        if (shaderName != "Standard") { EditorUtility.SetDirty(mat); return mat; }

        mat.color = color;
        mat.SetFloat("_Glossiness", glossiness);
        mat.SetFloat("_Metallic", metallic);
        if (transparent)
        {
            mat.SetFloat("_Mode", 2f); // Fade
            mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.renderQueue = (int)RenderQueue.Transparent;
        }
        else if (cutout)
        {
            mat.SetFloat("_Mode", 1f);
            mat.SetInt("_SrcBlend", (int)BlendMode.One);
            mat.SetInt("_DstBlend", (int)BlendMode.Zero);
            mat.SetInt("_ZWrite", 1);
            mat.EnableKeyword("_ALPHATEST_ON");
            mat.DisableKeyword("_ALPHABLEND_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.SetFloat("_Cutoff", 0.45f);
            mat.renderQueue = (int)RenderQueue.AlphaTest;
        }
        if (emissive.HasValue)
        {
            mat.EnableKeyword("_EMISSION");
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            mat.SetColor("_EmissionColor", emissive.Value);
        }
        EditorUtility.SetDirty(mat);
        return mat;
    }

    // ------------------------------------------------------------------ terrain

    const float TerrainSize = 1000f;
    const float TerrainHeight = 80f;

    static Terrain BuildTerrain()
    {
        string path = $"{MatDir}/Terrain.asset";
        var data = AssetDatabase.LoadAssetAtPath<TerrainData>(path);
        if (data == null)
        {
            data = new TerrainData();
            AssetDatabase.CreateAsset(data, path);
        }
        const int res = 513;
        data.heightmapResolution = res;
        data.size = new Vector3(TerrainSize, TerrainHeight, TerrainSize);

        var heights = new float[res, res];
        var hillsRaw = new float[res, res];
        for (int y = 0; y < res; y++)
            for (int x = 0; x < res; x++)
            {
                float wx = (x / (float)(res - 1) - 0.5f) * TerrainSize;
                float wz = (y / (float)(res - 1) - 0.5f) * TerrainSize;
                hillsRaw[y, x] = Mathf.PerlinNoise(1000f + wx * 0.004f, 500f + wz * 0.004f) * 0.7f
                               + Mathf.PerlinNoise(2000f + wx * 0.015f, 700f + wz * 0.015f) * 0.3f;
            }
        // Thermal erosion on the large-scale hill shape, before any fine detail is added - pure
        // Perlin hills read as smooth rounded blobs; a few relaxation passes (move height downhill
        // wherever the local slope exceeds a talus threshold) carve believable ridgelines and
        // drainage gullies without a hand-authored heightmap.
        ThermalErode(hillsRaw, res, 25, 0.010f, 0.5f);

        for (int y = 0; y < res; y++)
            for (int x = 0; x < res; x++)
            {
                float wx = (x / (float)(res - 1) - 0.5f) * TerrainSize;
                float wz = (y / (float)(res - 1) - 0.5f) * TerrainSize;
                float r = Mathf.Sqrt(wx * wx + wz * wz);
                float hills = hillsRaw[y, x];
                float micro = (Mathf.PerlinNoise(3000f + wx * 0.08f, 900f + wz * 0.08f) - 0.5f) * 0.004f; // gentle undulation in the field
                // Rock-scale roughness added AFTER erosion (it stands for already-weathered surface
                // grain, not something erosion should itself smooth away). Rides everywhere `flat` allows.
                float rock = (Mathf.PerlinNoise(4000f + wx * 0.05f, 300f + wz * 0.05f) - 0.5f) * 0.012f;
                float flat = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((r - 110f) / 150f));
                float far = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((r - 300f) / 200f));
                float fieldMask = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((r - 30f) / 60f));
                float h = (hills * (0.06f + 0.25f * far) * flat + micro * fieldMask + rock * flat) * (1f - VenueBuilder.Flat(wx, wz));
                h -= VenueBuilder.Pond(wx, wz) * 0.04f; // carve the farm pond's basin (~3.2 m deep)
                heights[y, x] = 0.0125f + h;
            }
        data.SetHeights(0, 0, heights);

        // Layers from the Poly Haven texture sets
        var turf = ProceduralTextures.GreenGrade("Assets/External/PolyHaven/leafy_grass_Diffuse.jpg", "Turf_Diffuse");
        var grass = TerrainLayerAsset("GrassLayer", "leafy_grass", 3f, turf);
        // Larger tiles on the photo-scanned aerial sets: at 4-5 m the repeat was obvious from
        // any low, grazing chase/FPV angle (diagonal streak pattern marching across the yard).
        var dry = TerrainLayerAsset("DryGrassLayer", "aerial_grass_rock", 7f);
        var dirt = TerrainLayerAsset("DirtLayer", "dirt_aerial_02", 6.5f);
        var gravel = TerrainLayerAsset("GravelLayer", "gravel_road", 3f);
        data.terrainLayers = new[] { grass, dirt, gravel, dry };

        int ares = 512;
        data.alphamapResolution = ares;
        var alpha = new float[ares, ares, 4];
        for (int y = 0; y < ares; y++)
            for (int x = 0; x < ares; x++)
            {
                float wx = (x / (float)(ares - 1) - 0.5f) * TerrainSize;
                float wz = (y / (float)(ares - 1) - 0.5f) * TerrainSize;
                float r = Mathf.Sqrt(wx * wx + wz * wz);
                float n = Mathf.PerlinNoise(3000f + wx * 0.02f, 900f + wz * 0.02f);
                float d = Mathf.Clamp01((n - 0.62f) * 5f) * 0.8f;
                float g = Mathf.Max(PitApron(wx, wz), VenueBuilder.Gravel(wx, wz));
                d = Mathf.Max(d, VenueBuilder.Dirt(wx, wz) * 0.95f);
                // lush mown turf on the flying field, drier grass further out
                float dryN = Mathf.PerlinNoise(7000f + wx * 0.008f, 100f + wz * 0.008f);
                float dryW = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((r - 90f) / 120f)) * (0.5f + 0.5f * dryN) + Mathf.Clamp01((dryN - 0.7f) * 3f) * 0.6f;
                dryW = Mathf.Clamp01(dryW);
                // Steep ground exposes bare dirt/gravel instead of staying grassed over, like real terrain.
                float slopeDeg = data.GetSteepness(x / (float)(ares - 1), y / (float)(ares - 1));
                float slopeExposure = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((slopeDeg - 22f) / 20f));
                d = Mathf.Max(d, slopeExposure * 0.75f);
                g = Mathf.Max(g, slopeExposure * 0.35f);
                // Muddy shore around the pond (the water plane itself covers the flooded centre).
                d = Mathf.Max(d, Mathf.Clamp01(VenueBuilder.Pond(wx, wz) * 1.5f) * 0.5f);
                float veg = (1f - d) * (1f - g);
                alpha[y, x, 0] = veg * (1f - dryW);
                alpha[y, x, 3] = veg * dryW;
                alpha[y, x, 1] = d * (1f - g);
                alpha[y, x, 2] = g;
            }
        data.SetAlphamaps(0, 0, alpha);

        // Detail grass billboards
        var tuft = ProceduralTextures.GrassTuft();
        data.SetDetailScatterMode(DetailScatterMode.InstanceCountMode);
        data.SetDetailResolution(1024, 32);
        var proto = new DetailPrototype
        {
            prototypeTexture = tuft,
            renderMode = DetailRenderMode.GrassBillboard,
            usePrototypeMesh = false,
            minWidth = 0.3f, maxWidth = 0.55f,
            minHeight = 0.18f, maxHeight = 0.36f,
            healthyColor = new Color(0.22f, 0.38f, 0.13f),
            dryColor = new Color(0.46f, 0.42f, 0.2f),
            // Wider colour spread and a coarser noise scale than the default so grass patches read
            // as natural mottled variation instead of a uniform stamped carpet from typical altitude.
            noiseSpread = 0.55f,
            density = 1f,
        };
        data.detailPrototypes = new[] { proto };
        int dres = data.detailResolution;
        var map = new int[dres, dres];
        var rng = new System.Random(77);
        for (int y = 0; y < dres; y++)
            for (int x = 0; x < dres; x++)
            {
                float wx = (x / (float)(dres - 1) - 0.5f) * TerrainSize;
                float wz = (y / (float)(dres - 1) - 0.5f) * TerrainSize;
                float r = Mathf.Sqrt(wx * wx + wz * wz);
                if (r > 320f) { map[y, x] = 0; continue; }
                float track = Mathf.Max(Mathf.Max(PitApron(wx, wz), VenueBuilder.Gravel(wx, wz)), VenueBuilder.Dirt(wx, wz));
                float patch = Mathf.PerlinNoise(500f + wx * 0.05f, 800f + wz * 0.05f);
                float dens = 2.8f + patch * 2.4f;
                if (r > 200f) dens *= 0.5f;
                map[y, x] = Mathf.FloorToInt((dens + (float)rng.NextDouble() * 0.9f) * (1f - track));
            }
        data.SetDetailLayer(0, 0, 0, map);
        data.wavingGrassSpeed = 0.6f;
        data.wavingGrassAmount = 0.35f;
        data.wavingGrassStrength = 0.6f;
        data.wavingGrassTint = new Color(0.9f, 0.95f, 0.8f);

        var go = Terrain.CreateTerrainGameObject(data);
        go.name = "Terrain";
        go.transform.position = new Vector3(-TerrainSize * 0.5f, -TerrainHeight * 0.0125f, -TerrainSize * 0.5f);
        var terrain = go.GetComponent<Terrain>();
        terrain.heightmapPixelError = 3f;
        terrain.basemapDistance = 800f;
        terrain.detailObjectDistance = 140f;
        terrain.detailObjectDensity = 1f;
        terrain.treeDistance = 0f;
        EditorUtility.SetDirty(data);
        return terrain;
    }

    /// <summary>Simple thermal-erosion relaxation: wherever a cell is more than `talus` above its
    /// lowest 4-neighbour, carry a `rate` fraction of the excess downhill. Cheap and in-place -
    /// good enough to turn a pure-Perlin heightfield into something with ridgelines and drainage.</summary>
    static void ThermalErode(float[,] h, int res, int iterations, float talus, float rate)
    {
        for (int it = 0; it < iterations; it++)
            for (int y = 1; y < res - 1; y++)
                for (int x = 1; x < res - 1; x++)
                {
                    float hc = h[y, x];
                    int bx = x, by = y;
                    float lowest = hc;
                    if (h[y, x - 1] < lowest) { lowest = h[y, x - 1]; bx = x - 1; by = y; }
                    if (h[y, x + 1] < lowest) { lowest = h[y, x + 1]; bx = x + 1; by = y; }
                    if (h[y - 1, x] < lowest) { lowest = h[y - 1, x]; bx = x; by = y - 1; }
                    if (h[y + 1, x] < lowest) { lowest = h[y + 1, x]; bx = x; by = y + 1; }
                    float diff = hc - lowest;
                    if (diff <= talus) continue;
                    float move = (diff - talus) * rate;
                    h[y, x] -= move;
                    h[by, bx] += move;
                }
    }

    static float PitApron(float x, float z)
    {
        // gravel around the asphalt pad
        float dx = Mathf.Max(0f, Mathf.Abs(x) - 12f), dz = Mathf.Max(0f, Mathf.Abs(z + 3f) - 9f);
        float d = Mathf.Sqrt(dx * dx + dz * dz);
        return 1f - Mathf.Clamp01((d - 1.5f) / 1.2f);
    }

    static TerrainLayer TerrainLayerAsset(string name, string asset, float tile, Texture2D diffuseOverride = null)
    {
        string path = $"{MatDir}/{name}.terrainlayer";
        var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
        if (layer == null)
        {
            layer = new TerrainLayer();
            AssetDatabase.CreateAsset(layer, path);
        }
        layer.diffuseTexture = diffuseOverride != null ? diffuseOverride : GeneratedAssets.LoadExternal($"{asset}_Diffuse.jpg");
        layer.normalMapTexture = GeneratedAssets.LoadExternal($"{asset}_nor_gl.jpg");
        layer.normalScale = 1.25f;
        layer.tileSize = new Vector2(tile, tile);
        layer.smoothness = 0.08f;
        layer.metallic = 0f;
        EditorUtility.SetDirty(layer);
        return layer;
    }

    public static float GroundHeight(Terrain t, float x, float z)
    {
        return t.SampleHeight(new Vector3(x, 0f, z)) + t.transform.position.y;
    }

    // ------------------------------------------------------------------ environment

    static void BuildEnvironment(Terrain terrain)
    {
        var env = new GameObject("Environment").transform;

        var asphalt = GeneratedAssets.PbrMaterial("Asphalt", "asphalt_pit_lane", 1f, 0.9f);
        var paint = Mat("PaintWhite", new Color(0.9f, 0.9f, 0.88f), glossiness: 0.3f);
        var steel = Mat("Steel", new Color(0.55f, 0.56f, 0.58f), glossiness: 0.45f, metallic: 0.8f);
        var netMat = Mat("SafetyNet", new Color(0.05f, 0.05f, 0.05f, 0.4f), transparent: true);
        var wall = GeneratedAssets.WeatheredConcrete("Wall", new Color(0.66f, 0.61f, 0.54f), 1.5f, glossiness: 0.08f, seed: 8); // rendered-plaster farm walls
        var roof = GeneratedAssets.CorrugatedMetal("Roof", new Color(0.38f, 0.34f, 0.32f), 8f, glossiness: 0.2f, metallic: 0.05f);

        // Pit: asphalt pad (PBR, 1 texture repeat per 4 m) with a painted start circle
        var pitMesh = GeneratedAssets.SaveMesh(MeshBuilder.Box(new Vector3(24f, 0.1f, 18f), "PitAsphalt", 0.25f), "PitAsphalt");
        var pit = MeshObject("Pit asphalt", env, pitMesh, asphalt, new Vector3(0f, -0.03f, -3f));
        pit.AddComponent<BoxCollider>();
        pit.isStatic = true;
        Primitive(PrimitiveType.Cylinder, "Start circle", env, new Vector3(0f, 0.022f, 0f), new Vector3(0.7f, 0.002f, 0.7f), paint, collider: false);
        Primitive(PrimitiveType.Cylinder, "Start dot", env, new Vector3(0f, 0.025f, 0f), new Vector3(0.25f, 0.002f, 0.25f), asphalt, collider: false);

        // Pilot tent
        var tent = new GameObject("Pit tent").transform;
        tent.SetParent(env);
        tent.position = new Vector3(6f, 0f, -9f);
        foreach (var sx in new[] { -1.5f, 1.5f })
            foreach (var sz in new[] { -1.5f, 1.5f })
                Primitive(PrimitiveType.Cylinder, "Leg", tent, new Vector3(sx, 1.1f, sz), new Vector3(0.05f, 1.1f, 0.05f), steel);
        Primitive(PrimitiveType.Cube, "Canopy", tent, new Vector3(0f, 2.3f, 0f), new Vector3(3.4f, 0.08f, 3.4f), Mat("Canopy", new Color(0.75f, 0.12f, 0.12f), glossiness: 0.15f));
        Primitive(PrimitiveType.Cube, "Table", tent, new Vector3(0f, 0.75f, 0f), new Vector3(1.6f, 0.04f, 0.7f), roof);
        for (int i = 0; i < 4; i++)
            Primitive(PrimitiveType.Cylinder, "Table leg", tent, new Vector3((i % 2 - 0.5f) * 1.4f, 0.37f, (i / 2 - 0.5f) * 0.6f), new Vector3(0.03f, 0.37f, 0.03f), steel);

        // Safety net
        for (int i = -3; i <= 3; i++)
            Primitive(PrimitiveType.Cylinder, $"Net pole {i}", env, new Vector3(i * 4f, 1.5f, -12f), new Vector3(0.07f, 1.5f, 0.07f), steel);
        var net = Primitive(PrimitiveType.Cube, "Safety net", env, new Vector3(0f, 1.5f, -12f), new Vector3(24f, 3f, 0.02f), netMat, collider: false);
        net.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;

        BuildTrees(env, terrain);
        BuildPond(env, terrain);

        var rng = new System.Random(1234);
        float Rand(float lo, float hi) => lo + (float)rng.NextDouble() * (hi - lo);

        // Farm buildings on the terrain, with windows and a door for a less "blank box" look.
        var buildings = new GameObject("Buildings").transform;
        buildings.SetParent(env);
        var windowMat = Mat("BuildingWindow", new Color(0.15f, 0.22f, 0.28f), glossiness: 0.7f, metallic: 0.2f);
        var doorMat = Mat("BuildingDoor", new Color(0.32f, 0.2f, 0.12f), glossiness: 0.2f);
        var trimMat = Mat("BuildingTrim", new Color(0.95f, 0.94f, 0.9f), glossiness: 0.15f);
        for (int i = 0; i < 14; i++)
        {
            float ang = Rand(0f, Mathf.PI * 2f);
            float r = Rand(80f, 260f);
            float x = Mathf.Cos(ang) * r, z = Mathf.Sin(ang) * r;
            if (VenueBuilder.Keepout(x, z, 16f)) continue;
            var size = new Vector3(Rand(8f, 18f), Rand(4f, 8f), Rand(8f, 16f));
            float y = GroundHeight(terrain, x, z);
            var b = new GameObject($"Building {i}").transform;
            b.SetParent(buildings);
            b.position = new Vector3(x, y, z);
            b.rotation = Quaternion.Euler(0f, Rand(0f, 360f), 0f);
            float wallBase = -0.5f;
            Primitive(PrimitiveType.Cube, "Walls", b, new Vector3(0f, size.y * 0.5f + wallBase, 0f), size, wall).isStatic = true;
            Primitive(PrimitiveType.Cube, "Roof", b, new Vector3(0f, size.y + wallBase + 0.3f, 0f), new Vector3(size.x + 1f, 0.6f, size.z + 1f), roof).isStatic = true;

            // Door centred on the front face (+Z), a row of windows either side.
            float doorH = Mathf.Min(2.2f, size.y * 0.5f);
            Primitive(PrimitiveType.Cube, "Door", b, new Vector3(0f, wallBase + doorH * 0.5f, size.z * 0.5f + 0.02f), new Vector3(1.1f, doorH, 0.05f), doorMat, collider: false);
            Primitive(PrimitiveType.Cube, "DoorTrim", b, new Vector3(0f, wallBase + doorH + 0.08f, size.z * 0.5f + 0.015f), new Vector3(1.3f, 0.12f, 0.04f), trimMat, collider: false);

            int windowsPerSide = Mathf.Clamp(Mathf.RoundToInt(size.x / 4f), 1, 4);
            float winY = wallBase + Mathf.Min(size.y * 0.6f, size.y - 1.2f);
            foreach (var faceSign in new[] { 1f, -1f }) // front and back faces
                for (int w = 0; w < windowsPerSide; w++)
                {
                    float wx = (w + 0.5f) / windowsPerSide * size.x - size.x * 0.5f;
                    if (Mathf.Abs(wx) < 0.8f && faceSign > 0f) continue; // leave a gap for the door
                    var win = Primitive(PrimitiveType.Cube, "Window", b, new Vector3(wx, winY, faceSign * (size.z * 0.5f + 0.02f)), new Vector3(0.9f, 1.0f, 0.05f), windowMat, collider: false);
                    Primitive(PrimitiveType.Cube, "WindowSill", win.transform, new Vector3(0f, -0.6f, 0.3f), new Vector3(1.15f, 0.12f, 0.3f), trimMat, collider: false);
                }
            // A couple of side windows too, for buildings viewed from the side.
            for (int w = 0; w < Mathf.Clamp(Mathf.RoundToInt(size.z / 5f), 1, 3); w++)
            {
                float wz = (w + 0.5f) / Mathf.Max(1, Mathf.RoundToInt(size.z / 5f)) * size.z - size.z * 0.5f;
                foreach (var sideSign in new[] { 1f, -1f })
                    Primitive(PrimitiveType.Cube, "SideWindow", b, new Vector3(sideSign * (size.x * 0.5f + 0.02f), winY, wz), new Vector3(0.05f, 1.0f, 0.9f), windowMat, collider: false);
            }
        }

        // Hay bales & a container inside the field
        var bale = Mat("Hay", new Color(0.72f, 0.6f, 0.3f), glossiness: 0.05f);
        bale.mainTexture = GeneratedAssets.LoadExternal("dirt_aerial_02_Diffuse.jpg");
        bale.mainTextureScale = new Vector2(2f, 2f);
        for (int i = 0; i < 6; i++)
        {
            float x = Rand(-30f, 30f), z = Rand(24f, 56f);
            var hb = Primitive(PrimitiveType.Cylinder, $"Hay bale {i}", env, new Vector3(x, 0.75f, z), new Vector3(1.5f, 0.6f, 1.5f), bale);
            hb.transform.rotation = Quaternion.Euler(0f, Rand(0f, 360f), 90f);
        }
        var container = Primitive(PrimitiveType.Cube, "Container", env, new Vector3(-34f, 1.3f, 38f), new Vector3(2.4f, 2.6f, 12f), GeneratedAssets.CorrugatedMetal("Container", new Color(0.12f, 0.3f, 0.5f), 5f));
        container.transform.rotation = Quaternion.Euler(0f, 20f, 0f);
    }

    static void BuildTrees(Transform env, Terrain terrain)
    {
        var bark = GeneratedAssets.PbrMaterial("Bark", "bark_willow_02", 1f, 0.8f);
        bark.enableInstancing = true;
        var leafTex = ProceduralTextures.LeafCluster();
        var leaves = Mat("Leaves", Color.white, cutout: true, glossiness: 0.2f);
        leaves.mainTexture = leafTex;
        leaves.enableInstancing = true;
        var leaves2 = Mat("Leaves2", new Color(0.85f, 0.95f, 0.7f), cutout: true, glossiness: 0.2f);
        leaves2.mainTexture = leafTex;
        leaves2.enableInstancing = true;

        // Variants: (mesh, height, crown radius)
        var variants = new List<(Mesh mesh, float height, float crown)>();
        var specs = new[]
        {
            new TreeGenerator.Params { seed = 11, height = 9f, trunkRadius = 0.28f, levels = 4, leafSize = 1.15f, cardsPerTip = 5 },
            new TreeGenerator.Params { seed = 23, height = 7.5f, trunkRadius = 0.22f, levels = 4, leafSize = 1.05f, cardsPerTip = 5, spreadDeg = 48f },
            new TreeGenerator.Params { seed = 37, height = 11f, trunkRadius = 0.34f, levels = 4, leafSize = 1.3f, cardsPerTip = 5, spreadDeg = 35f },
            new TreeGenerator.Params { seed = 41, height = 5.5f, trunkRadius = 0.16f, levels = 3, leafSize = 1.1f, cardsPerTip = 5, spreadDeg = 50f },
            new TreeGenerator.Params { seed = 53, height = 2.6f, trunkRadius = 0.07f, levels = 3, leafSize = 0.7f, cardsPerTip = 5, spreadDeg = 60f, crownStart = 0.2f }, // bush
        };
        for (int i = 0; i < specs.Length; i++)
        {
            var mesh = TreeGenerator.Generate(specs[i], $"Tree_{i}", out float crown);
            variants.Add((GeneratedAssets.SaveMesh(mesh, $"Tree_{i}"), specs[i].height, crown));
        }

        var trees = new GameObject("Trees").transform;
        trees.SetParent(env);
        var rng = new System.Random(4321);
        float Rand(float lo, float hi) => lo + (float)rng.NextDouble() * (hi - lo);

        void Place(int variant, float x, float z, float scale, bool bush = false)
        {
            var (mesh, height, crown) = variants[variant];
            float y = GroundHeight(terrain, x, z) - 0.1f;
            var go = new GameObject(bush ? "Bush" : "Tree");
            go.isStatic = true;
            go.transform.SetParent(trees);
            go.transform.position = new Vector3(x, y, z);
            go.transform.rotation = Quaternion.Euler(0f, Rand(0f, 360f), 0f);
            go.transform.localScale = Vector3.one * scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = new[] { bark, rng.NextDouble() < 0.5 ? leaves : leaves2 };
            mr.shadowCastingMode = ShadowCastingMode.On;
            var trunk = go.AddComponent<CapsuleCollider>();
            trunk.center = new Vector3(0f, height * 0.4f, 0f);
            trunk.radius = bush ? 0.3f : 0.35f;
            trunk.height = height * 0.8f;
            var canopy = go.AddComponent<SphereCollider>();
            canopy.center = new Vector3(0f, height * 0.78f, 0f);
            canopy.radius = Mathf.Max(0.5f, crown * 0.75f);
        }

        // Scattered trees around the field (course kept clear)
        int placed = 0;
        for (int i = 0; i < 2000 && placed < 190; i++)
        {
            float ang = Rand(0f, Mathf.PI * 2f);
            float r = Rand(40f, 300f);
            float x = Mathf.Cos(ang) * r, z = Mathf.Sin(ang) * r;
            if (VenueBuilder.Keepout(x, z, 6f)) continue;
            float n = Mathf.PerlinNoise(x * 0.01f + 50f, z * 0.01f + 50f);
            if (rng.NextDouble() > n * 1.6f) continue;   // clumps
            Place(rng.Next(0, 4), x, z, Rand(0.85f, 1.2f));
            placed++;
        }
        // Tree line ringing the far field
        for (int i = 0; i < 160; i++)
        {
            float ang = i / 160f * Mathf.PI * 2f + Rand(-0.02f, 0.02f);
            float r = Rand(330f, 420f);
            Place(rng.Next(0, 3), Mathf.Cos(ang) * r, Mathf.Sin(ang) * r, Rand(1.1f, 1.5f));
        }
        // Bushes near the pit and along the track
        for (int i = 0; i < 45; i++)
        {
            float ang = Rand(0f, Mathf.PI * 2f);
            float r = Rand(30f, 120f);
            float x = Mathf.Cos(ang) * r, z = Mathf.Sin(ang) * r;
            if (VenueBuilder.Keepout(x, z, 3f)) continue;
            Place(4, x, z, Rand(0.7f, 1.3f), true);
        }
    }

    /// <summary>A small farm pond nestled in the hills - a reflective plane sitting in the basin
    /// carved into the heightmap by BuildTerrain (VenueBuilder.Pond).</summary>
    static void BuildPond(Transform env, Terrain terrain)
    {
        var water = Mat("Water", new Color(0.08f, 0.2f, 0.26f), glossiness: 0.92f, metallic: 0.05f);
        float rim = GroundHeight(terrain, VenueBuilder.PondCenter.x + VenueBuilder.PondRadius + 4f, VenueBuilder.PondCenter.y);
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = "Pond";
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(env, false);
        go.transform.position = new Vector3(VenueBuilder.PondCenter.x, rim - 0.6f, VenueBuilder.PondCenter.y);
        go.transform.localScale = new Vector3(VenueBuilder.PondRadius * 1.6f, 0.02f, VenueBuilder.PondRadius * 1.6f);
        var mr = go.GetComponent<Renderer>();
        mr.sharedMaterial = water;
        mr.shadowCastingMode = ShadowCastingMode.Off;
        go.isStatic = true;
    }

    // ------------------------------------------------------------------ drone

    static GameObject BuildDrone(Transform spawn)
    {
        var go = new GameObject("Drone");
        go.transform.position = spawn.position;

        var rb = go.AddComponent<Rigidbody>();
        var col = go.AddComponent<BoxCollider>();
        col.size = new Vector3(0.30f, 0.075f, 0.30f);
        col.center = new Vector3(0f, 0.02f, 0f);
#if UNITY_6000_0_OR_NEWER
        var pm = new PhysicsMaterial("DronePhysMat");
#else
        var pm = new PhysicMaterial("DronePhysMat");
#endif
        pm.bounciness = 0.08f;
        pm.dynamicFriction = 0.55f;
        pm.staticFriction = 0.7f;
        AssetDatabase.CreateAsset(pm, $"{MatDir}/DronePhysMat.asset");
        col.material = pm;

        var physics = go.AddComponent<DronePhysics>();
        go.AddComponent<PilotInput>();
        var fc = go.AddComponent<FlightController>();
        var crash = go.AddComponent<CrashDetector>();
        crash.spawnPoint = spawn;
        var autopilot = go.AddComponent<Autopilot>();
        autopilot.drone = physics;
        autopilot.fc = fc;
        autopilot.input = go.GetComponent<PilotInput>();
        go.AddComponent<PayloadRack>();
        var tricks = go.AddComponent<TrickDetector>();
        tricks.drone = physics;
        tricks.fc = fc;

        var model = DroneModelBuilder.Build(go.transform, physics, Accent);
        physics.propellers = model.props;
        var pv = go.AddComponent<PropVisuals>();
        pv.drone = physics;
        pv.blades = model.blades;
        pv.discs = model.discs;

        var motorAudio = new GameObject("Motor audio");
        motorAudio.transform.SetParent(go.transform, false);
        motorAudio.AddComponent<AudioSource>();
        motorAudio.AddComponent<DroneAudio>().drone = physics;

        // Own AudioSource - sharing one with DroneAudio let each component's Awake() silently
        // overwrite the other's min/maxDistance, and DroneAudio.Update() rewrites source.volume
        // every frame, which would otherwise compound with ImpactAudio's own volume on every hit.
        var impactAudio = new GameObject("Impact audio");
        impactAudio.transform.SetParent(go.transform, false);
        impactAudio.AddComponent<AudioSource>();
        var impact = impactAudio.AddComponent<ImpactAudio>();
        impact.crashDetector = crash;
        impact.flightController = fc;

        var fpv = new GameObject("FPV Camera");
        fpv.transform.SetParent(model.cameraMount, false);
        fpv.transform.localPosition = new Vector3(0f, 0f, 0.012f);
        var fpvCam = fpv.AddComponent<Camera>();
        fpvCam.fieldOfView = 112f;
        fpvCam.nearClipPlane = 0.035f;
        fpvCam.farClipPlane = 1500f;
        fpvCam.allowHDR = true;
        var fx = fpv.AddComponent<FpvCameraEffects>();
        fx.shader = Shader.Find("DroneTrainer/FpvAnalog");
        fx.drone = physics;
        // The shared PPv2 profile already darkens every camera's corners (Vignette, ~0.18); the
        // shader's own fisheye-shaped vignette stacks on top of that for the FPV camera only, so
        // tone it down from its old solo-use default to avoid double-darkening its corners.
        fx.vignette = 0.15f;
        fpv.AddComponent<AudioSource>();
        var windAudio = fpv.AddComponent<WindAudio>();
        windAudio.drone = physics;
        windAudio.fpvCamera = fpvCam;

        return go;
    }

    // ------------------------------------------------------------------ cameras

    static Camera[] BuildCameras(Transform drone)
    {
        var camsRoot = new GameObject("Cameras").transform;

        var chase = new GameObject("Chase Camera");
        chase.transform.SetParent(camsRoot);
        chase.transform.position = drone.position + new Vector3(0f, 1.5f, -4f);
        var chaseCam = chase.AddComponent<Camera>();
        chaseCam.fieldOfView = 60f;
        chaseCam.farClipPlane = 1500f;
        chaseCam.allowHDR = true;
        chase.AddComponent<ChaseCamera>().target = drone;

        var los = new GameObject("Line of Sight Camera");
        los.transform.SetParent(camsRoot);
        los.transform.position = new Vector3(3f, 1.7f, -8f);
        var losCam = los.AddComponent<Camera>();
        losCam.fieldOfView = 60f;
        losCam.farClipPlane = 1500f;
        losCam.allowHDR = true;
        los.AddComponent<LineOfSightCamera>().target = drone;

        var fpvCam = drone.GetComponentInChildren<Camera>();
        fpvCam.GetComponent<FpvCameraEffects>().pilot = los.transform;

        // Cinematic camera behind the menus (index 3; not part of the in-flight camera cycle)
        var show = new GameObject("Showcase Camera");
        show.transform.SetParent(camsRoot);
        show.transform.position = new Vector3(10f, 5f, -10f);
        var showCam = show.AddComponent<Camera>();
        showCam.fieldOfView = 55f;
        showCam.farClipPlane = 1500f;
        showCam.allowHDR = true;
        showCam.enabled = false;
        show.AddComponent<AudioListener>().enabled = false;
        show.AddComponent<ShowcaseCamera>();
        return new[] { chaseCam, losCam, fpvCam, showCam };
    }

    // ------------------------------------------------------------------ post-processing

    static void BuildPostProcessing(Camera[] cams)
    {
#if UNITY_POST_PROCESSING_STACK_V2
        string profilePath = $"{MatDir}/PostProcess.asset";
        var profile = AssetDatabase.LoadAssetAtPath<PostProcessProfile>(profilePath);
        if (profile != null) AssetDatabase.DeleteAsset(profilePath);
        profile = ScriptableObject.CreateInstance<PostProcessProfile>();
        AssetDatabase.CreateAsset(profile, profilePath);

        T Add<T>() where T : PostProcessEffectSettings
        {
            var s = profile.AddSettings<T>();
            AssetDatabase.AddObjectToAsset(s, profile);
            return s;
        }

        var ao = Add<AmbientOcclusion>();
        ao.mode.Override(AmbientOcclusionMode.MultiScaleVolumetricObscurance);
        ao.intensity.Override(0.7f);
        ao.thicknessModifier.Override(1.3f);
        var bloom = Add<Bloom>();
        bloom.intensity.Override(1.3f);
        bloom.threshold.Override(0.9f);
        bloom.softKnee.Override(0.6f);
        var cg = Add<ColorGrading>();
        cg.tonemapper.Override(Tonemapper.ACES);
        cg.postExposure.Override(0.1f);
        cg.saturation.Override(12f);
        cg.contrast.Override(18f);
        cg.temperature.Override(4f);
        var mb = Add<MotionBlur>();
        mb.shutterAngle.Override(200f);
        mb.sampleCount.Override(10);
        var vig = Add<Vignette>();
        vig.intensity.Override(0.18f);
        vig.smoothness.Override(0.5f);
        // Cheap cinematic-camera finish for chase/LOS/showcase (the FPV feed already has its own,
        // stronger analog-video look via FpvAnalog.shader, so keep these subtle here).
        var grain = Add<Grain>();
        grain.intensity.Override(0.18f);
        grain.size.Override(1.3f);
        grain.lumContrib.Override(0.8f);
        var ca = Add<ChromaticAberration>();
        ca.intensity.Override(0.07f); // kept low: edge fringing hurts the precise visual feedback a flight trainer needs
        EditorUtility.SetDirty(profile);

        var volumeGo = new GameObject("Post-process volume");
        var volume = volumeGo.AddComponent<PostProcessVolume>();
        volume.isGlobal = true;
        volume.priority = 1f;
        volume.sharedProfile = profile;

        PostProcessResources resources = null;
        foreach (var guid in AssetDatabase.FindAssets("t:PostProcessResources"))
        {
            resources = AssetDatabase.LoadAssetAtPath<PostProcessResources>(AssetDatabase.GUIDToAssetPath(guid));
            if (resources != null) break;
        }
        foreach (var cam in cams)
        {
            var layer = cam.gameObject.AddComponent<PostProcessLayer>();
            if (resources != null) layer.Init(resources);
            layer.volumeTrigger = cam.transform;
            layer.volumeLayer = 1; // Default layer
            layer.antialiasingMode = PostProcessLayer.Antialiasing.SubpixelMorphologicalAntialiasing;
            layer.fog.enabled = true;
            layer.fog.excludeSkybox = true;
        }
#else
        Debug.LogWarning("Post Processing package not present - skipping post effects.");
#endif
    }

    // ------------------------------------------------------------------ course

    class Course
    {
        public Transform hoverZone;
        public Gate[] gates;
        public Transform pad;
        public Balloon[] balloons;
        public Transform[] dropTargets;
        public DemoBlock[] demoBlocks;
        public DemoBlock[] targetCars;
        public Transform pylon;
        public Transform spawnRace, spawnPark, spawnYard, focusRace, focusPark, focusYard;
    }

    static readonly List<DemoBlock> builtTargetCars = new List<DemoBlock>();

    static DemoBlock[] BuildDemolition(Transform root, Terrain terrain, float ox, float oz)
    {
        builtTargetCars.Clear();
        var parent = new GameObject("Demolition").transform;
        parent.SetParent(root);
        var crate = Mat("Crate", new Color(0.62f, 0.44f, 0.22f), glossiness: 0.2f);
        crate.mainTexture = GeneratedAssets.LoadExternal("bark_willow_02_Diffuse.jpg");
        crate.mainTextureScale = new Vector2(0.5f, 0.5f);
        var barrelBlue = Mat("BarrelBlue", new Color(0.15f, 0.35f, 0.75f), glossiness: 0.5f, metallic: 0.5f);
        var barrelRed = Mat("BarrelRed", new Color(0.8f, 0.15f, 0.1f), glossiness: 0.5f, metallic: 0.5f);
        var block = Mat("Block", new Color(0.85f, 0.82f, 0.75f), glossiness: 0.15f);
        var list = new List<DemoBlock>();

        DemoBlock Add(PrimitiveType type, Vector3 pos, Vector3 size, Material m, float mass, Quaternion? rot = null)
        {
            var go = Primitive(type, "Block", parent, pos, size, m);
            if (rot.HasValue) go.transform.rotation = rot.Value;
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = mass;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            var b = go.AddComponent<DemoBlock>();
            list.Add(b);
            return b;
        }

        // Crate tower: 3 x 3 footprint, 5 high
        void CrateTower(float x, float z, int w, int h)
        {
            float g = GroundHeight(terrain, x, z);
            for (int ly = 0; ly < h; ly++)
                for (int ix = 0; ix < w; ix++)
                    for (int iz = 0; iz < w; iz++)
                        Add(PrimitiveType.Cube, new Vector3(x + (ix - (w - 1) * 0.5f) * 0.82f, g + 0.41f + ly * 0.82f, z + (iz - (w - 1) * 0.5f) * 0.82f), Vector3.one * 0.8f, crate, 4f);
        }
        // Barrel pyramid: standing barrels, rows 4-3-2-1
        void BarrelPyramid(float x, float z)
        {
            float g = GroundHeight(terrain, x, z);
            for (int row = 0; row < 4; row++)
            {
                int n = 4 - row;
                for (int i = 0; i < n; i++)
                    Add(PrimitiveType.Cylinder, new Vector3(x + (i - (n - 1) * 0.5f) * 0.62f, g + 0.45f + row * 0.9f, z), new Vector3(0.6f, 0.45f, 0.6f), (i + row) % 2 == 0 ? barrelBlue : barrelRed, 6f);
            }
        }
        // Block wall: 6 wide, 4 high, staggered
        void Wall(float x, float z, float yaw)
        {
            float g = GroundHeight(terrain, x, z);
            var rot = Quaternion.Euler(0f, yaw, 0f);
            for (int ly = 0; ly < 4; ly++)
                for (int i = 0; i < 6; i++)
                {
                    float off = (i - 2.5f + (ly % 2) * 0.5f) * 1.0f;
                    var local = new Vector3(off, g + 0.3f + ly * 0.6f, 0f);
                    var pos = new Vector3(x, 0f, z) + rot * new Vector3(local.x, 0f, 0f) + new Vector3(0f, local.y, 0f);
                    Add(PrimitiveType.Cube, pos, new Vector3(0.98f, 0.58f, 0.5f), block, 5f, rot);
                }
        }

        CrateTower(ox - 24f, oz + 36f, 3, 5);
        CrateTower(ox + 24f, oz + 34f, 2, 6);
        BarrelPyramid(ox, oz + 52f);
        Wall(ox + 12f, oz + 46f, 20f);
        Wall(ox - 10f, oz + 48f, -35f);

        // Target cars: parked in a real asphalt lot with painted bays, each marked with a
        // spinning orange diamond - the exact, unmistakable targets, easy to find and reach
        // right after take-off (the lot sits in the open centre of the gate loop).
        float lotCenterX = ox, lotCenterZ = oz + 22f;
        BuildParkingLot(parent, terrain, lotCenterX, lotCenterZ);
        var carColors = new[] { new Color(0.82f, 0.07f, 0.05f), new Color(0.08f, 0.32f, 0.82f), new Color(0.92f, 0.78f, 0.05f) };
        const int carWeight = 8; // each target car counts as several crates towards the demolition score
        for (int i = 0; i < 3; i++)
        {
            float x = lotCenterX + (i - 1) * 4f;
            float z = lotCenterZ;
            float g = GroundHeight(terrain, x, z);
            var carBlock = CarBuilder.Build(parent, new Vector3(x, g, z), 180f, carColors[i], $"Target car {i + 1}");
            CarBuilder.AddTargetMarker(carBlock.transform);
            builtTargetCars.Add(carBlock);
            for (int k = 0; k < carWeight; k++) list.Add(carBlock);
        }

        foreach (var b in list) b.gameObject.SetActive(false);
        return list.ToArray();
    }

    /// <summary>A small asphalt lot with 3 painted parking bays for the target cars.</summary>
    static void BuildParkingLot(Transform parent, Terrain terrain, float centerX, float centerZ)
    {
        var asphalt = GeneratedAssets.PbrMaterial("Asphalt", "asphalt_pit_lane", 1f, 0.9f);
        var paint = Mat("PaintWhite", new Color(0.9f, 0.9f, 0.88f), glossiness: 0.3f);

        const float lotWidth = 13f, lotDepth = 7f;
        float g = GroundHeight(terrain, centerX, centerZ);
        var lotMesh = GeneratedAssets.SaveMesh(MeshBuilder.Box(new Vector3(lotWidth, 0.1f, lotDepth), "ParkingLotAsphalt", 0.3f), "ParkingLotAsphalt");
        var lot = MeshObject("Parking lot", parent, lotMesh, asphalt, new Vector3(centerX, g - 0.03f, centerZ));
        lot.AddComponent<BoxCollider>();
        lot.isStatic = true;

        // Bay dividers between the 3 spaces (cars sit at x offsets -4, 0, 4).
        foreach (var x in new[] { -2f, 2f })
            Primitive(PrimitiveType.Cube, "Bay line", parent, new Vector3(centerX + x, g + 0.025f, centerZ), new Vector3(0.08f, 0.002f, lotDepth - 0.6f), paint, collider: false);
        // Outer boundary lines.
        foreach (var x in new[] { -6f, 6f })
            Primitive(PrimitiveType.Cube, "Bound line", parent, new Vector3(centerX + x, g + 0.025f, centerZ), new Vector3(0.08f, 0.002f, lotDepth - 0.6f), paint, collider: false);
    }

    static Balloon[] BuildBalloons(Transform root, Terrain terrain, float ox, float oz)
    {
        var parent = new GameObject("Balloons").transform;
        parent.SetParent(root);
        var colors = new[] { new Color(1f, 0.15f, 0.1f), new Color(1f, 0.8f, 0.1f), new Color(0.2f, 0.5f, 1f), new Color(0.2f, 0.9f, 0.3f), new Color(1f, 0.3f, 0.8f) };
        var mats = new Material[colors.Length];
        for (int i = 0; i < colors.Length; i++) mats[i] = Mat($"Balloon{i}", colors[i], glossiness: 0.75f);
        var stringMat = Mat("BalloonString", new Color(0.9f, 0.9f, 0.9f), glossiness: 0.2f);
        var rng = new System.Random(99);
        var list = new List<Balloon>();
        for (int i = 0; i < 14; i++)
        {
            float x = ox - 26f + (float)rng.NextDouble() * 52f;
            float z = oz + 10f + (float)rng.NextDouble() * 34f;
            float h = 1.5f + (float)rng.NextDouble() * 9f;
            float ground = GroundHeight(terrain, x, z);
            var go = new GameObject($"Balloon {i + 1}");
            go.transform.SetParent(parent);
            go.transform.position = new Vector3(x, ground + h, z);
            var col = go.AddComponent<SphereCollider>();
            col.radius = 0.45f;
            col.isTrigger = true;
            int ci = rng.Next(colors.Length);
            var body = Primitive(PrimitiveType.Sphere, "Body", go.transform, Vector3.zero, new Vector3(0.7f, 0.82f, 0.7f), mats[ci], collider: false);
            Primitive(PrimitiveType.Sphere, "Knot", go.transform, new Vector3(0f, -0.43f, 0f), new Vector3(0.08f, 0.08f, 0.08f), mats[ci], collider: false);
            Primitive(PrimitiveType.Cylinder, "String", go.transform, new Vector3(0f, -h * 0.5f, 0f), new Vector3(0.006f, h * 0.5f, 0.006f), stringMat, collider: false);
            Primitive(PrimitiveType.Cube, "Anchor", go.transform, new Vector3(0f, -h + 0.05f, 0f), new Vector3(0.2f, 0.1f, 0.2f), stringMat);
            var b = go.AddComponent<Balloon>();
            b.body = body.GetComponent<Renderer>();
            b.color = colors[ci];
            go.SetActive(false);
            list.Add(b);
        }
        return list.ToArray();
    }

    static Transform[] BuildDropTargets(Transform root, Terrain terrain, float ox, float oz)
    {
        var parent = new GameObject("Drop targets").transform;
        parent.SetParent(root);
        var white = Mat("TargetWhite", new Color(0.95f, 0.95f, 0.92f), glossiness: 0.15f);
        var red = Mat("TargetRed", new Color(0.9f, 0.12f, 0.08f), glossiness: 0.15f);
        var positions = new[] { new Vector3(ox - 16f, 0f, oz + 28f), new Vector3(ox + 14f, 0f, oz + 26f), new Vector3(ox - 20f, 0f, oz + 46f) };
        var list = new List<Transform>();
        foreach (var p in positions)
        {
            float y = GroundHeight(terrain, p.x, p.z) + 0.02f;
            var t = new GameObject("Target").transform;
            t.SetParent(parent);
            t.position = new Vector3(p.x, y, p.z);
            float[] radii = { 2f, 1.2f, 0.5f, 0.25f };
            for (int i = 0; i < radii.Length; i++)
                Primitive(PrimitiveType.Cylinder, $"Ring {i}", t, new Vector3(0f, i * 0.004f, 0f), new Vector3(radii[i] * 2f, 0.002f, radii[i] * 2f), i % 2 == 0 ? white : red, collider: false);
            t.gameObject.SetActive(false);
            list.Add(t);
        }
        return list.ToArray();
    }

    static Course BuildCourse(Terrain terrain)
    {
        var root = new GameObject("Course").transform;
        var c = new Course();

        var hoverZoneMat = Mat("HoverZone", new Color(1f, 0.85f, 0.2f, 0.25f), transparent: true);
        var zone = Primitive(PrimitiveType.Cube, "HoverZone", root, new Vector3(0f, 2.5f, 7f), new Vector3(3f, 2f, 3f), hoverZoneMat, collider: false);
        zone.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
        c.hoverZone = zone.transform;
        zone.SetActive(false);

        var padMat = Mat("LandingPad", new Color(1f, 0.42f, 0.05f), glossiness: 0.3f);
        var paint = Mat("PaintWhite", new Color(0.9f, 0.9f, 0.88f), glossiness: 0.3f);
        var pad = Primitive(PrimitiveType.Cylinder, "LandingPad", root, new Vector3(8f, 0.024f, 1f), new Vector3(2.2f, 0.002f, 2.2f), padMat, collider: false);
        Primitive(PrimitiveType.Cube, "H1", pad.transform, new Vector3(-0.22f, 1f, 0f), new Vector3(0.08f, 1.2f, 0.5f), paint, collider: false);
        Primitive(PrimitiveType.Cube, "H2", pad.transform, new Vector3(0.22f, 1f, 0f), new Vector3(0.08f, 1.2f, 0.5f), paint, collider: false);
        Primitive(PrimitiveType.Cube, "H3", pad.transform, new Vector3(0f, 1f, 0f), new Vector3(0.36f, 1.2f, 0.08f), paint, collider: false);
        c.pad = pad.transform;

        // Venues: race track (east), freestyle park (west), demolition yard (north)
        var race = VenueBuilder.BuildRaceTrack(root, terrain);
        c.gates = race.gates;
        c.spawnRace = race.spawn;
        c.focusRace = race.focus;
        var park = VenueBuilder.BuildFreestylePark(root, terrain);
        c.spawnPark = park.spawn;
        c.focusPark = park.focus;
        var yard = VenueBuilder.BuildDemolitionYard(root, terrain);
        c.spawnYard = yard.spawn;
        c.focusYard = yard.focus;
        float yx = VenueBuilder.Yard.x, yz = VenueBuilder.Yard.y - 32f; // old props were laid out for z = 6..52
        c.balloons = BuildBalloons(root, terrain, yx, yz);
        c.dropTargets = BuildDropTargets(root, terrain, yx, yz);
        c.demoBlocks = BuildDemolition(root, terrain, yx, yz);
        c.targetCars = builtTargetCars.ToArray();

        // Orbit pylon: a tall striped pole off to the side of the pit.
        var pylon = new GameObject("Pylon").transform;
        pylon.SetParent(root);
        pylon.position = new Vector3(18f, GroundHeight(terrain, 18f, 4f), 4f);
        var stripeA = Mat("PylonRed", new Color(0.9f, 0.1f, 0.05f), glossiness: 0.3f);
        var stripeB = Mat("PylonWhite", new Color(0.95f, 0.95f, 0.92f), glossiness: 0.3f);
        for (int i = 0; i < 8; i++)
            Primitive(PrimitiveType.Cylinder, "Stripe", pylon, new Vector3(0f, 0.5f + i * 1f, 0f), new Vector3(0.3f, 0.5f, 0.3f), i % 2 == 0 ? stripeA : stripeB);
        Primitive(PrimitiveType.Sphere, "Top", pylon, new Vector3(0f, 8.4f, 0f), Vector3.one * 0.6f, stripeA, collider: false);
        pylon.gameObject.SetActive(false);
        c.pylon = pylon;
        return c;
    }

    public static Gate BuildGate(Transform parent, int number, Vector3 groundPos, Vector3 forward)
    {
        const float width = 2.4f, height = 2.4f, bottom = 0.7f, tubeR = 0.05f;
        var tube = Mat("GateTube", new Color(0.88f, 0.88f, 0.9f), glossiness: 0.45f);
        var flag = Mat("GateFlag", new Color(1f, 0.42f, 0.05f), glossiness: 0.08f);
        var feet = Mat("GateFeet", new Color(0.12f, 0.12f, 0.12f), glossiness: 0.1f);

        var go = new GameObject($"Gate {number}");
        go.transform.SetParent(parent);
        go.transform.position = groundPos + new Vector3(0f, bottom + height * 0.5f, 0f);
        go.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);

        var trigger = go.AddComponent<BoxCollider>();
        trigger.isTrigger = true;
        trigger.size = new Vector3(width - 0.2f, height - 0.2f, 0.5f);

        var gate = go.AddComponent<Gate>();
        float top = height * 0.5f;
        float postH = bottom + height;
        Primitive(PrimitiveType.Cylinder, "PostL", go.transform, new Vector3(-width * 0.5f, top - postH * 0.5f, 0f), new Vector3(tubeR * 2f, postH * 0.5f, tubeR * 2f), tube);
        Primitive(PrimitiveType.Cylinder, "PostR", go.transform, new Vector3(width * 0.5f, top - postH * 0.5f, 0f), new Vector3(tubeR * 2f, postH * 0.5f, tubeR * 2f), tube);
        Primitive(PrimitiveType.Cube, "TopBar", go.transform, new Vector3(0f, top, 0f), new Vector3(width + tubeR * 2f, tubeR * 2f, tubeR * 2f), tube);
        Primitive(PrimitiveType.Cube, "BottomBar", go.transform, new Vector3(0f, -top, 0f), new Vector3(width + tubeR * 2f, tubeR * 2f, tubeR * 2f), tube);
        var f1 = Primitive(PrimitiveType.Cube, "FlagTop", go.transform, new Vector3(0f, top - 0.2f, 0f), new Vector3(width - 0.1f, 0.3f, 0.02f), flag, collider: false);
        var f2 = Primitive(PrimitiveType.Cube, "FlagL", go.transform, new Vector3(-width * 0.5f + 0.15f, 0f, 0f), new Vector3(0.25f, height - 0.1f, 0.02f), flag, collider: false);
        var f3 = Primitive(PrimitiveType.Cube, "FlagR", go.transform, new Vector3(width * 0.5f - 0.15f, 0f, 0f), new Vector3(0.25f, height - 0.1f, 0.02f), flag, collider: false);
        Primitive(PrimitiveType.Cube, "FootL", go.transform, new Vector3(-width * 0.5f, -top - bottom + 0.05f, 0f), new Vector3(0.3f, 0.1f, 0.8f), feet);
        Primitive(PrimitiveType.Cube, "FootR", go.transform, new Vector3(width * 0.5f, -top - bottom + 0.05f, 0f), new Vector3(0.3f, 0.1f, 0.8f), feet);
        var board = Primitive(PrimitiveType.Cube, "Number", go.transform, new Vector3(0f, top + 0.35f, 0f), new Vector3(0.5f, 0.4f, 0.03f), feet, collider: false);

        gate.markers = new[] { f1.GetComponent<Renderer>(), f2.GetComponent<Renderer>(), f3.GetComponent<Renderer>(), board.GetComponent<Renderer>() };
        gate.idleColor = new Color(1f, 0.42f, 0.05f);
        gate.nextColor = new Color(0.2f, 1f, 0.3f);
        gate.passedColor = new Color(0.6f, 0.6f, 0.6f);
        return gate;
    }

    // ------------------------------------------------------------------ trainer / lessons

    static void BuildTrainer(GameObject drone, Camera[] cams, Course course)
    {
        var physics = drone.GetComponent<DronePhysics>();
        var fc = drone.GetComponent<FlightController>();
        var crash = drone.GetComponent<CrashDetector>();
        var input = drone.GetComponent<PilotInput>();

        var trainer = new GameObject("Trainer");
        trainer.AddComponent<SimBootstrap>();
        trainer.AddComponent<Wind>();

        var switcher = trainer.AddComponent<CameraSwitcher>();
        switcher.cameras = new[] { cams[0], cams[1], cams[2] };
        switcher.names = new[] { "Chase", "Line of sight", "FPV" };
        switcher.input = input;

        var lessonsRoot = new GameObject("Lessons");
        lessonsRoot.transform.SetParent(trainer.transform);
        var manager = trainer.AddComponent<LessonManager>();
        manager.drone = physics;
        manager.flightController = fc;
        manager.crashDetector = crash;
        manager.input = input;
        manager.autoStartFirstLesson = false; // the main menu starts drills

        // ---- Curriculum: consecutive drills with the same discipline form a discipline ----
        T Drill<T>(string name, string discipline, string title, string text, FlightMode mode, float par) where T : Lesson
        {
            var l = new GameObject(name).AddComponent<T>();
            l.transform.SetParent(lessonsRoot.transform);
            l.discipline = discipline;
            l.title = title;
            l.instructions = text;
            l.recommendedMode = mode;
            l.parTime = par;
            return l;
        }

        // Basics
        var b1 = Drill<HoverBoxLesson>("B1 Hover", "Basics", "Hover in the box",
            "Arm, lift off and hold the drone inside the yellow box for 10 seconds without leaving it. A 5\" quad hovers at ~35% throttle - small stick inputs!", FlightMode.Angle, 30f);
        b1.zone = course.hoverZone;
        var b2 = Drill<LandingLesson>("B2 Landing", "Basics", "Precision landing",
            "Climb above 3 m, then land on the orange H pad softly (under 2 m/s) and stay still for 2 seconds. Hard landings break props!", FlightMode.Angle, 40f);
        b2.pad = course.pad;
        b2.padRadius = 1.2f;
        var b3 = Drill<OrbitLesson>("B3 Orbit", "Basics", "Orbit the pylon",
            "Fly 2 full circles around the red-and-white pylon, staying 3-8 m from it and above 1 m. Yaw to keep the nose on the pylon while rolling around it.", FlightMode.Angle, 60f);
        b3.pylon = course.pylon;
        b3.laps = 2f;

        // Racing
        var r1 = Drill<GateCourseLesson>("R1 Gates", "Racing", "Gate course",
            "Fly through the gates in order. The next gate glows green. Use yaw to turn and pitch to move forward.", FlightMode.Angle, 60f);
        r1.gates = course.gates;
        var r2 = Drill<GateCourseLesson>("R2 Acro gates", "Racing", "Gates in ACRO",
            "Same course in ACRO (press M). No self-levelling: you must actively level the drone. Two laps.", FlightMode.Acro, 120f);
        r2.gates = course.gates;
        r2.laps = 2;
        var r3 = Drill<GateCourseLesson>("R3 Race", "Racing", "Race - 3 laps",
            "Wait for the 3-2-1-GO countdown (crossing a gate early is a false start), then three timed laps. Your best lap is remembered.", FlightMode.Acro, 150f);
        r3.gates = course.gates;
        r3.laps = 3;
        r3.countdownSeconds = 3f;
        r3.bestLapKey = "race.bestlap";
        var r4 = Drill<GateCourseLesson>("R4 Time trial", "Racing", "Time trial - beat 22 s",
            "Two laps after the countdown. Full marks only if your best lap is under 22 seconds - carry speed through the gates, brake with pitch, not throttle.", FlightMode.Acro, 90f);
        r4.gates = course.gates;
        r4.laps = 2;
        r4.countdownSeconds = 3f;
        r4.targetLapTime = 22f;
        r4.bestLapKey = "race.bestlap";

        // Freestyle
        var f1 = Drill<TrickLesson>("F1 Rolls", "Freestyle", "Rolls x3",
            "Switch to ACRO (M), climb above 6 m, then do three full rolls (roll stick all the way, hold, centre when level). Catch each one before it drops.", FlightMode.Acro, 60f);
        f1.rollsRequired = 3;
        var f2 = Drill<TrickLesson>("F2 Flips", "Freestyle", "Flips x3",
            "ACRO, above 6 m: three full flips (pitch stick fully forward or back, hold, centre when level). Punch the throttle just before you start.", FlightMode.Acro, 60f);
        f2.flipsRequired = 3;
        var f3 = Drill<TrickLesson>("F3 Power loops", "Freestyle", "Power loops x2",
            "ACRO: dive to gain speed, pull up hard and keep pulling over the top so you gain 2.5 m or more at the apex, then complete the loop. Two of them.", FlightMode.Acro, 90f);
        f3.loopsRequired = 2;

        // Demolition (kamikaze game)
        var d1 = Drill<BalloonPopLesson>("D1 Balloons", "Demolition", "Balloon pop",
            "14 balloons are tied around the field at different heights. Fly through every one of them - fastest time wins.", FlightMode.Angle, 90f);
        d1.balloons = course.balloons;
        var d2 = Drill<WaterBalloonDropLesson>("D2 Drop", "Demolition", "Water-balloon drop",
            "You carry 6 water balloons (they weigh you down). Fly over a bullseye and press F to release. The balloon keeps your speed and falls with drag, so lead the target. 10 / 5 / 2 points by ring.", FlightMode.Angle, 120f);
        d2.targets = course.dropTargets;
        var d3 = Drill<DemolitionLesson>("D3 Marked car", "Demolition", "Hit the marked car",
            "Only the RED target car counts (leftmost bay, spinning marker). Fly into it. Hitting anything else wastes a drone. Fewer drones used = more stars.", FlightMode.Angle, 60f);
        d3.blocks = course.demoBlocks;
        d3.requiredTarget = course.targetCars.Length > 0 ? course.targetCars[0] : null;
        d3.drones = 3;
        var d4 = Drill<DemolitionLesson>("D4 Speed hit", "Demolition", "Speed hit - 12 m/s",
            "Hit the BLUE car (middle bay) at 12 m/s or faster. Build speed on a long straight approach, stay low, and don't flare before impact.", FlightMode.Angle, 60f);
        d4.blocks = course.demoBlocks;
        d4.requiredTarget = course.targetCars.Length > 1 ? course.targetCars[1] : null;
        d4.minImpactSpeed = 12f;
        d4.drones = 3;
        var d5 = Drill<DemolitionLesson>("D5 Demolition", "Demolition", "Kamikaze demolition",
            "Cars, crate towers, barrel pyramids and walls. Demolish 60% of everything with 5 drones - pick targets where one blast topples the most.", FlightMode.Angle, 180f);
        d5.blocks = course.demoBlocks;
        d5.drones = 5;
        d5.passPercent = 60;

        // Agent
        var a1 = Drill<AutopilotAgentLesson>("A1 Agent", "Agent", "Autopilot agent",
            "The drone flies the mission by itself: take-off, the gate loop at 4 m, return and land. Watch from any camera (C). Move the sticks to take over; Backspace restarts the agent.", FlightMode.Angle, 120f);
        a1.gates = course.gates;
        a1.altitude = 4f;

        // Venues: each discipline starts at its own spawn point
        foreach (var l in new Lesson[] { r1, r2, r3, r4, a1 }) { l.spawn = course.spawnRace; l.venue = "Race track"; }
        foreach (var l in new Lesson[] { f1, f2, f3 }) { l.spawn = course.spawnPark; l.venue = "Freestyle park"; }
        foreach (var l in new Lesson[] { d1, d2, d3, d4, d5 }) { l.spawn = course.spawnYard; l.venue = "Demolition yard"; }
        manager.pilotCamera = cams[1].transform;
        var selfTest = trainer.AddComponent<AutoPilotSelfTest>();
        selfTest.drone = physics;
        selfTest.flightController = fc;

        var osd = trainer.AddComponent<Osd>();
        osd.drone = physics;
        osd.flightController = fc;
        osd.crashDetector = crash;
        osd.input = input;
        osd.lessons = manager;
        osd.cameras = switcher;

        // ---- Front end: title, discipline/drill selection, pause, results ----
        var showcase = cams[3].GetComponent<ShowcaseCamera>();
        var shots = new List<ShowcaseCamera.Shot>();
        shots.Add(new ShowcaseCamera.Shot { target = drone.transform, radius = 2.2f, height = 0.9f, degreesPerSecond = 9f, seconds = 10f, lookHeight = 0.15f });
        if (course.focusRace != null) shots.Add(new ShowcaseCamera.Shot { target = course.focusRace, offset = new Vector3(0f, 0f, 14f), radius = 34f, height = 11f, degreesPerSecond = 5f, seconds = 12f, lookHeight = 2f });
        if (course.focusPark != null) shots.Add(new ShowcaseCamera.Shot { target = course.focusPark, radius = 26f, height = 9f, degreesPerSecond = 6f, seconds = 11f, lookHeight = 4f });
        if (course.focusYard != null) shots.Add(new ShowcaseCamera.Shot { target = course.focusYard, offset = new Vector3(0f, 0f, 26f), radius = 40f, height = 14f, degreesPerSecond = 5f, seconds = 12f, lookHeight = 2f });
        if (course.gates != null && course.gates.Length > 0)
        {
            var mid = course.gates[course.gates.Length / 2].transform;
            shots.Add(new ShowcaseCamera.Shot { target = mid, radius = 26f, height = 9f, degreesPerSecond = 5f, seconds = 12f, lookHeight = 1.5f });
            shots.Add(new ShowcaseCamera.Shot { target = course.gates[0].transform, radius = 7f, height = 2.5f, degreesPerSecond = 8f, seconds = 9f, lookHeight = 1.8f });
        }
        if (course.targetCars != null && course.targetCars.Length > 0)
            shots.Add(new ShowcaseCamera.Shot { target = course.targetCars[0].transform, radius = 9f, height = 3f, degreesPerSecond = 7f, seconds = 10f, lookHeight = 0.8f });
        if (course.pylon != null)
            shots.Add(new ShowcaseCamera.Shot { target = course.pylon, radius = 14f, height = 5f, degreesPerSecond = 6f, seconds = 9f, lookHeight = 2f });
        showcase.shots = shots.ToArray();

        var menu = trainer.AddComponent<MainMenu>();
        menu.lessons = manager;
        menu.cameras = switcher;
        menu.showcaseCamera = cams[3];
        menu.input = input;
        menu.drone = physics;
        menu.flightController = fc;
        menu.crashDetector = crash;
        menu.osd = osd;
        osd.menu = menu;

        var remote = trainer.AddComponent<DevRemote>();
        remote.drone = physics;
        remote.flightController = fc;
        remote.crashDetector = crash;
        remote.cameras = switcher;
        remote.lessons = manager;
        remote.osd = osd;
        remote.menu = menu;
    }

    // ------------------------------------------------------------------ helpers

    public static GameObject MeshObject(string name, Transform parent, Mesh mesh, Material mat, Vector3 localPos)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        return go;
    }

    public static GameObject Primitive(PrimitiveType type, string name, Transform parent, Vector3 localPos, Vector3 localScale, Material mat, bool collider = true)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = localScale;
        go.GetComponent<Renderer>().sharedMaterial = mat;
        if (!collider)
        {
            var c = go.GetComponent<Collider>();
            if (c != null) Object.DestroyImmediate(c);
        }
        return go;
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int slash = path.LastIndexOf('/');
        AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
    }

    static void AddToBuildSettings()
    {
        var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        if (!scenes.Exists(s => s.path == ScenePath))
        {
            scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }

    static void ConfigureProject()
    {
        PlayerSettings.productName = "Drone Trainer";
        PlayerSettings.companyName = "DroneTrainer";
        PlayerSettings.colorSpace = ColorSpace.Linear;

        var settings = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
        if (settings != null && settings.Length > 0)
        {
            var so = new SerializedObject(settings[0]);
            var prop = so.FindProperty("activeInputHandler");
            if (prop != null && prop.intValue != 1 && prop.intValue != 2)
            {
                prop.intValue = 2;
                so.ApplyModifiedProperties();
                Debug.LogWarning("Drone Trainer: enabled the Input System backend. Restart the Unity editor once (File > Exit, reopen) before pressing Play.");
            }
        }
    }
}
