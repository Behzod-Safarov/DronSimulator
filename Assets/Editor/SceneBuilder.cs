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
        BuildTrainer(drone, cams, course, terrain);
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
        // 0.0009 rather than 0.0012: the horizon ridge now sits ~1.1 km out instead of 435 m, where
        // the old density washed it to 13% of its own colour. This keeps it hazed but legible.
        RenderSettings.fogDensity = 0.0009f;
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

    /// <summary>
    /// Alpha-cutout foliage material on DroneTrainer/FoliageSway, so the crown bends and flutters
    /// with the live wind instead of standing rigid. Built here rather than through <see cref="Mat"/>
    /// because that helper forces the Standard shader and returns early for anything else.
    /// </summary>
    public static Material FoliageMat(string name, Color color, Texture tex, float glossiness, float swayScale = 1f)
    {
        string path = $"{MatDir}/{name}.mat";
        var shader = Shader.Find("DroneTrainer/FoliageSway");
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, path);
        }
        else if (mat.shader != shader)
        {
            mat.shader = shader;
        }
        mat.color = color;
        mat.mainTexture = tex;
        mat.SetFloat("_Cutoff", 0.45f);
        mat.SetFloat("_Glossiness", glossiness);
        mat.SetFloat("_Metallic", 0f);
        mat.SetFloat("_SwayScale", swayScale);
        mat.renderQueue = (int)RenderQueue.AlphaTest;
        mat.enableInstancing = true;
        EditorUtility.SetDirty(mat);
        return mat;
    }

    // ------------------------------------------------------------------ terrain

    const float TerrainSize = 1000f;
    // Raised from 80 m so the horizon ring can be actual mountains: normalised heights are
    // fractions of this, so every legacy term below is scaled by LegacyScale to keep its metres.
    const float TerrainHeight = 170f;
    const float LegacyScale = 80f / TerrainHeight;
    // Headroom for everything carved BELOW the field, not a vertical offset: world height works
    // out to h * TerrainHeight regardless of this value, because the terrain object is placed at
    // -GroundLevel and the heightmap base is +GroundLevel/TerrainHeight. What it buys is room
    // before Unity clamps the normalised heightmap at zero.
    //
    // It was 1 m against a 3.2 m pond basin and a 4.5 m quarry basin, so both were being clipped
    // wherever local hills did not happen to lift the ground first - the pond ended up with no
    // basin at all once it was levelled, and the quarry rim was losing its floor in places.
    const float GroundLevel = 12f;

    static Terrain BuildTerrain()
    {
        string path = $"{MatDir}/Terrain.asset";
        var data = AssetDatabase.LoadAssetAtPath<TerrainData>(path);
        if (data == null)
        {
            data = new TerrainData();
            AssetDatabase.CreateAsset(data, path);
        }
        // 1025, not 513: over a 1 km terrain that halves the sample spacing from 1.95 m to 0.98 m,
        // so the smallest representable ground feature drops from ~3.9 m to ~2.0 m. This is a sim
        // whose whole syllabus is flown 1-3 m off the deck, where metre-scale relief is the main
        // visual speed cue - at 513 the ground simply could not hold any of it.
        const int res = 1025;
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
        // Talus halved with the sample spacing: it is compared against the height difference
        // between neighbouring cells, so keeping 0.010 at twice the resolution would have doubled
        // the slope the erosion tolerates and washed the ridgelines out. 0.005 over a 0.98 m cell
        // is the same ~41 deg repose angle 0.010 gave over a 1.95 m one.
        ThermalErode(hillsRaw, res, 25, 0.005f, 0.5f);

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
                // Metre-scale surface grain, the detail the finer heightmap exists to carry: about
                // +/-0.15 m over a 4 m wavelength, which is ordinary pasture undulation and reads
                // as texture underneath you at low level. Four samples per wavelength at 0.98 m.
                float grain = (Mathf.PerlinNoise(8200f + wx * 0.25f, 5400f + wz * 0.25f) - 0.5f) * (0.30f / TerrainHeight);
                float flat = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((r - 110f) / 150f));
                float far = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((r - 300f) / 200f));
                float fieldMask = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((r - 30f) / 60f));
                // Fade all relief out to nothing by the terrain boundary. The horizon is now carried
                // by the BuildBackdrop ring mesh, which starts just outside this edge at y = 0, so the
                // terrain has to arrive there flat for the two to meet without a step - including at
                // the corners, which reach r = 707 m and so fade out completely.
                float rim = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((r - 380f) / 120f));
                float h = (hills * (0.06f + 0.25f * far) * flat * rim + micro * fieldMask + rock * flat * rim) * (1f - VenueBuilder.Flat(wx, wz)) * LegacyScale
                        + grain * flat * (1f - VenueBuilder.Flat(wx, wz));
                h -= VenueBuilder.Pond(wx, wz) * 0.04f * LegacyScale; // carve the farm pond basin (~3.2 m deep)
                h += VenueBuilder.HeightOffset(wx, wz) / TerrainHeight; // biome shaping: mesa, dunes, rubble mounds, quarry basin
                heights[y, x] = GroundLevel / TerrainHeight + h;
            }
        // Guard against silent clamping: Unity stores heights normalised to 0..1, so anything
        // carved deeper than GroundLevel is quietly flattened instead of erroring.
        float hMin = 1f, hMax = 0f;
        for (int y = 0; y < res; y++)
            for (int x = 0; x < res; x++) { hMin = Mathf.Min(hMin, heights[y, x]); hMax = Mathf.Max(hMax, heights[y, x]); }
        Debug.Log($"<b>Drone Trainer</b>: terrain spans {(hMin * TerrainHeight - GroundLevel):0.00} m to " +
                  $"{(hMax * TerrainHeight - GroundLevel):0.00} m relative to the field" +
                  (hMin <= 0f ? "  <color=#FF8080>(CLAMPED at the bottom - raise GroundLevel)</color>" : ""));
        data.SetHeights(0, 0, heights);

        // Layers from the Poly Haven texture sets
        var turf = ProceduralTextures.GreenGrade("Assets/External/PolyHaven/leafy_grass_Diffuse.jpg", "Turf_Diffuse");
        var grass = TerrainLayerAsset("GrassLayer", "leafy_grass", 3f, turf);
        // Larger tiles on the photo-scanned aerial sets: at 4-5 m the repeat was obvious from
        // any low, grazing chase/FPV angle (diagonal streak pattern marching across the yard).
        var dry = TerrainLayerAsset("DryGrassLayer", "aerial_grass_rock", 7f);
        var dirt = TerrainLayerAsset("DirtLayer", "dirt_aerial_02", 6.5f);
        var gravel = TerrainLayerAsset("GravelLayer", "gravel_road", 3f);
        // Biome grounds: warm sand for the desert plateau, grey stone for the quarry and the
        // urban wasteland - both recoloured from the same Poly Haven sets.
        var sandTex = ProceduralTextures.GreenGrade("Assets/External/PolyHaven/dirt_aerial_02_Diffuse.jpg", "Sand_Diffuse", 0.1f, 0.85f, 1.1f, 1.02f);
        var stoneTex = ProceduralTextures.GreenGrade("Assets/External/PolyHaven/aerial_grass_rock_Diffuse.jpg", "Stone_Diffuse", 0.09f, 0.9f, 0.14f, 0.78f);
        var sand = TerrainLayerAsset("SandLayer", "dirt_aerial_02", 7f, sandTex);
        var stone = TerrainLayerAsset("StoneLayer", "aerial_grass_rock", 6f, stoneTex);
        data.terrainLayers = new[] { grass, dirt, gravel, dry, sand, stone };

        int ares = 512;
        data.alphamapResolution = ares;
        var alpha = new float[ares, ares, 6];
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
                // The horizon rock now lives on the backdrop mesh, not in the heightmap, so all this
                // band still has to do is ease the outermost ground towards the backdrop's tone so the
                // two meet without a colour seam. It used to paint bare stone from 420 m out to match
                // mountains that were carved into the terrain itself.
                float alpine = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((r - 400f) / 100f));
                float stoneW = Mathf.Clamp01(alpine * (0.45f + 0.4f * slopeExposure));
                // Muddy shore around the pond (the water plane itself covers the flooded centre).
                d = Mathf.Max(d, Mathf.Clamp01(VenueBuilder.Pond(wx, wz) * 1.5f) * 0.5f);
                float veg = (1f - d) * (1f - g);
                // Meadow mix (the original look), then blended toward each biome ground.
                float w0 = veg * (1f - dryW), w1 = d * (1f - g), w2 = g, w3 = veg * dryW, w4 = 0f, w5 = 0f;
                float desert = VenueBuilder.Desert(wx, wz), urban = VenueBuilder.Urban(wx, wz), quarryW = VenueBuilder.Quarry(wx, wz);
                if (desert > 0f)
                {
                    // sand everywhere, gravel kept where the track is, stone on the steep mesa faces
                    float st = Mathf.Max(slopeExposure, 0f);
                    Blend(ref w0, ref w1, ref w2, ref w3, ref w4, ref w5, desert, 0f, 0.1f, g, 0f, 0.75f - 0.55f * (1f - st) + 0.15f * st, 0.15f + 0.5f * st);
                }
                if (urban > 0f)
                    Blend(ref w0, ref w1, ref w2, ref w3, ref w4, ref w5, urban, 0f, 0.15f, Mathf.Max(g, 0.3f), 0.04f, 0f, 0.55f);
                if (quarryW > 0f)
                    Blend(ref w0, ref w1, ref w2, ref w3, ref w4, ref w5, quarryW, 0f, 0.15f, Mathf.Max(g, 0.25f), 0f, 0.05f, 0.62f);
                if (stoneW > 0f)
                    Blend(ref w0, ref w1, ref w2, ref w3, ref w4, ref w5, stoneW, w0 * 0.25f, 0.12f, 0.1f, w3 * 0.3f, 0.05f, 0.78f);
                alpha[y, x, 0] = w0;
                alpha[y, x, 1] = w1;
                alpha[y, x, 2] = w2;
                alpha[y, x, 3] = w3;
                alpha[y, x, 4] = w4;
                alpha[y, x, 5] = w5;
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
            // Taller than wide: squat quads read as blocks however good the texture is.
            minWidth = 0.16f, maxWidth = 0.28f,
            minHeight = 0.3f, maxHeight = 0.55f,
            healthyColor = new Color(0.19f, 0.31f, 0.12f),
            dryColor = new Color(0.4f, 0.36f, 0.18f),
            // Wider colour spread and a coarser noise scale than the default so grass patches read
            // as natural mottled variation instead of a uniform stamped carpet from typical altitude.
            noiseSpread = 0.55f,
            density = 1f,
        };
        // Three grass types instead of one carpet: lush tufts, bleached dry tufts and sparse
        // flowering weeds. Real ground cover is a mixture, and the eye reads the mixture as detail.
        var dryProto = new DetailPrototype
        {
            prototypeTexture = tuft,
            renderMode = DetailRenderMode.GrassBillboard,
            usePrototypeMesh = false,
            minWidth = 0.14f, maxWidth = 0.24f,
            minHeight = 0.22f, maxHeight = 0.4f,
            healthyColor = new Color(0.44f, 0.43f, 0.24f),
            dryColor = new Color(0.55f, 0.5f, 0.29f),
            noiseSpread = 0.6f,
            density = 1f,
        };
        var flowerProto = new DetailPrototype
        {
            prototypeTexture = tuft,
            renderMode = DetailRenderMode.GrassBillboard,
            usePrototypeMesh = false,
            minWidth = 0.1f, maxWidth = 0.17f,
            minHeight = 0.18f, maxHeight = 0.3f,
            healthyColor = new Color(0.85f, 0.85f, 0.55f),
            dryColor = new Color(0.9f, 0.78f, 0.82f),
            noiseSpread = 0.9f,
            density = 1f,
        };
        data.detailPrototypes = new[] { proto, dryProto, flowerProto };
        int dres = data.detailResolution;
        var map = new int[dres, dres];
        var dryMap = new int[dres, dres];
        var flowerMap = new int[dres, dres];
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
                float dens = 3.4f + patch * 2.8f;   // enough to overlap into a carpet without becoming a mass
                if (r > 200f) dens *= 0.5f;
                float meadow = VenueBuilder.Meadow(wx, wz);
                float desert = VenueBuilder.Desert(wx, wz);
                float open = 1f - track;
                // lush tufts in the meadow, thinning where the ground is already dry
                float dryN = Mathf.PerlinNoise(7000f + wx * 0.008f, 100f + wz * 0.008f);
                map[y, x] = Mathf.FloorToInt((dens * meadow * (1f - 0.22f * dryN) + (float)rng.NextDouble() * 0.9f) * open);
                // bleached tufts: the dry patches of the meadow, and sparse scrub out on the sand
                float dryDens = dens * 0.28f * meadow * Mathf.Clamp01(dryN - 0.35f) + 1.4f * desert * Mathf.Clamp01(patch - 0.35f);
                dryMap[y, x] = Mathf.FloorToInt((dryDens + (float)rng.NextDouble() * 0.6f) * open);
                // flowering weeds: rare, clumped, meadow only
                float bloom = Mathf.PerlinNoise(1500f + wx * 0.09f, 2400f + wz * 0.09f);
                flowerMap[y, x] = bloom > 0.74f ? Mathf.FloorToInt(meadow * (1f + (float)rng.NextDouble() * 1.6f) * open) : 0;
            }
        data.SetDetailLayer(0, 0, 0, map);
        data.SetDetailLayer(0, 0, 1, dryMap);
        data.SetDetailLayer(0, 0, 2, flowerMap);
        data.wavingGrassSpeed = 0.6f;
        data.wavingGrassAmount = 0.35f;
        data.wavingGrassStrength = 0.6f;
        data.wavingGrassTint = new Color(0.78f, 0.84f, 0.7f);

        var go = Terrain.CreateTerrainGameObject(data);
        go.name = "Terrain";
        go.transform.position = new Vector3(-TerrainSize * 0.5f, -GroundLevel, -TerrainSize * 0.5f);
        var terrain = go.GetComponent<Terrain>();
        // Tightened with the finer heightmap: at 3 the terrain LOD threw away most of the extra
        // detail before it reached the screen.
        terrain.heightmapPixelError = 2f;
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

    // ------------------------------------------------------------------ horizon backdrop

    const float BackdropInner = 505f;   // just outside the terrain's 500 m axis edge
    const float BackdropOuter = 1450f;  // inside every camera's 1500 m far clip
    // The terrain is square, so while its edges are 500 m out its corners reach 707 m. The
    // backdrop stays flat and slightly sunk until past that, or the rising relief would cut
    // through the flat terrain corners along four visible straight lines.
    const float BackdropClear = 710f;

    /// <summary>
    /// Height in metres above the field (y = 0) of the horizon relief, for the backdrop ring only -
    /// the terrain itself now fades flat to its boundary and contributes nothing out here.
    ///
    /// Two overlapping ridge lines, because one reads as a wall and two read as distance: foothills
    /// cresting 60 m up at ~950 m (3.5 deg, just above the 2.6 deg tree line, so the two overlap
    /// instead of leaving a strip of empty haze between them) and a range reaching 200 m by 1250 m
    /// (9.0 deg). Both hold their height out to the edge so the pilot can never see over the back.
    ///
    /// This replaced a ring that put a 165 m crest only 435 m away - 20.8 deg of elevation on a
    /// 65 deg face, standing right on the out-of-bounds radius, which read as a crater rim. Real
    /// ranges a kilometre off a field subtend 3-10 deg on 15-25 deg faces.
    /// </summary>
    static float HorizonProfile(float wx, float wz, float r)
    {
        float foot = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((r - BackdropClear) / 240f));
        float band = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((r - 850f) / 400f));
        if (foot <= 0f) return 0f;

        float ang = Mathf.Atan2(wz, wx);
        // A few big massifs around the compass, each broken up by three noise octaves. The
        // frequencies are high enough to put visible spurs and gullies on the faces - at this
        // distance almost everything is fog, so the silhouette and the shading are all there is.
        float ridges = 0.55f + 0.45f * Mathf.Sin(ang * 3f + 0.7f) * Mathf.Cos(ang * 2f - 1.3f);
        float coarse = Mathf.PerlinNoise(900f + wx * 0.0022f, 120f + wz * 0.0022f);
        float mid = Mathf.PerlinNoise(2500f + wx * 0.0075f, 400f + wz * 0.0075f);
        float fine = Mathf.PerlinNoise(6100f + wx * 0.019f, 250f + wz * 0.019f);
        float peaks = Mathf.Clamp01(ridges * (0.45f + 0.75f * coarse) + 0.22f * mid + 0.08f * fine - 0.15f);

        // Foothills carry their own, gentler noise so the two ridge lines never share a silhouette.
        float footNoise = 0.55f + 0.45f * Mathf.PerlinNoise(400f + wx * 0.005f, 800f + wz * 0.005f);

        // Sharpened: a smooth power curve gives dunes, a steeper one gives ridges and valleys.
        return foot * footNoise * 60f + band * Mathf.Pow(peaks, 1.35f) * 140f;
    }

    /// <summary>
    /// The horizon itself: one ring mesh from just outside the terrain boundary out to 1450 m,
    /// carrying the ground continuation and both ridge lines from <see cref="HorizonProfile"/>.
    ///
    /// It exists because a 1 km terrain cannot hold a believable horizon - anything tall enough to
    /// close the sky has to sit within 500 m of the pilot, where it can only look like a crater
    /// wall. Renderer only: no collider, and the whole thing lies outside the 470 m flyable radius.
    /// </summary>
    static void BuildBackdrop(Transform env)
    {
        const int angular = 288;   // 1.25 deg per step: ~27 m quads at the far crest
        const int radial = 44;

        var b = new MeshBuilder.Builder();

        // Radii biased outwards so the triangles cluster where the relief actually is.
        var radii = new float[radial + 1];
        for (int i = 0; i <= radial; i++)
        {
            float t = i / (float)radial;
            radii[i] = Mathf.Lerp(BackdropInner, BackdropOuter, Mathf.Pow(t, 0.8f));
        }

        for (int i = 0; i <= radial; i++)
        {
            float rr = radii[i];
            // Hold the whole terrain-overlap band 0.3 m low. The terrain there is flat at y = 0
            // after the rim fade, so it wins the depth test everywhere the two meet instead of
            // z-fighting a coplanar surface; past the corners the sink eases back out. The step
            // this leaves at the terrain edge is 0.3 m seen from 500 m away - about 0.03 deg.
            float sink = rr < BackdropClear ? -0.3f : -0.3f * Mathf.Clamp01(1f - (rr - BackdropClear) / 60f);
            for (int j = 0; j <= angular; j++)
            {
                float a = j / (float)angular * Mathf.PI * 2f;
                float wx = Mathf.Cos(a) * rr, wz = Mathf.Sin(a) * rr;
                float y = HorizonProfile(wx, wz, rr) + sink;
                // Planar UVs: one texture repeat per 60 m.
                b.Add(new Vector3(wx, y, wz), Vector3.up, new Vector2(wx / 60f, wz / 60f));
            }
        }

        int stride = angular + 1;
        for (int i = 0; i < radial; i++)
            for (int j = 0; j < angular; j++)
            {
                int v0 = i * stride + j;
                b.Quad(v0, v0 + stride, v0 + stride + 1, v0 + 1);
            }

        var mesh = GeneratedAssets.SaveMesh(b.ToMesh("Horizon", recalculateNormals: true), "Horizon");
        // Warm, dry tint rather than a cold grey: the terrain it has to sit behind is tan, and fog
        // is already pulling everything out here towards the blue-grey of the sky.
        var mat = GeneratedAssets.PbrMaterial("HorizonRock", "aerial_grass_rock", 1f, 0.06f, new Color(0.74f, 0.68f, 0.56f));
        var go = MeshObject("Horizon", env, mesh, mat, Vector3.zero);
        go.isStatic = true;
        var mr = go.GetComponent<Renderer>();
        // Everything here is past QualitySettings.shadowDistance (420 m), so it could never cast
        // into the flyable area anyway - keep it out of the shadow pass entirely.
        mr.shadowCastingMode = ShadowCastingMode.Off;
    }

    /// <summary>Eases the six terrain layer weights toward a biome target by k.</summary>
    static void Blend(ref float w0, ref float w1, ref float w2, ref float w3, ref float w4, ref float w5,
                      float k, float t0, float t1, float t2, float t3, float t4, float t5)
    {
        w0 = Mathf.Lerp(w0, t0, k); w1 = Mathf.Lerp(w1, t1, k); w2 = Mathf.Lerp(w2, t2, k);
        w3 = Mathf.Lerp(w3, t3, k); w4 = Mathf.Lerp(w4, t4, k); w5 = Mathf.Lerp(w5, t5, k);
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
        var paint = Mat("PaintWhite", new Color(0.72f, 0.72f, 0.7f), glossiness: 0.25f);
        var steel = Mat("Steel", new Color(0.55f, 0.56f, 0.58f), glossiness: 0.45f, metallic: 0.8f);
        var netMat = Mat("SafetyNet", new Color(0.05f, 0.05f, 0.05f, 0.4f), transparent: true);
        var wall = GeneratedAssets.WeatheredConcrete("Wall", new Color(0.66f, 0.61f, 0.54f), 1.5f, glossiness: 0.08f, seed: 8); // rendered-plaster farm walls
        var roof = GeneratedAssets.CorrugatedMetal("Roof", new Color(0.38f, 0.34f, 0.32f), 8f, glossiness: 0.2f, metallic: 0.05f);

        // Pit: asphalt pad (PBR, 1 texture repeat per 4 m) with a painted start circle
        var pitMesh = GeneratedAssets.SaveMesh(MeshBuilder.Box(new Vector3(24f, 0.1f, 18f), "PitAsphalt", 0.25f), "PitAsphalt");
        var pit = MeshObject("Pit asphalt", env, pitMesh, asphalt, new Vector3(0f, -0.03f, -3f));
        pit.AddComponent<BoxCollider>();
        pit.isStatic = true;
        // A painted RING flush with the asphalt: the drone is small and dark, so it reads much
        // better against the asphalt inside the ring than silhouetted on white paint.
        PaintRing("Start ring", env, paint, asphalt, new Vector3(0f, 0.02f, 0f), 0.46f, 0.575f);

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

        BuildBackdrop(env);
        BuildWindsock(env, terrain, new Vector3(-10.5f, 0f, -9f), steel);
        BuildTrees(env, terrain);
        BuildPond(env, terrain);
        VenueBuilder.BuildLandscapes(env, terrain);

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
            if (VenueBuilder.Keepout(x, z, 16f) || VenueBuilder.Meadow(x, z) < 0.7f) continue;
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

    /// <summary>
    /// A cheaper version of a tree spec for the distance LOD: one branch level and most of the
    /// leaf cards gone, with the remaining cards enlarged so the crown keeps roughly the same
    /// visual mass. Same seed, so the distant mesh is recognisably the tree it stands in for.
    /// </summary>
    static TreeGenerator.Params Simplified(TreeGenerator.Params p) => new TreeGenerator.Params
    {
        seed = p.seed,
        height = p.height,
        trunkRadius = p.trunkRadius,
        levels = Mathf.Max(2, p.levels - 1),
        leafSize = p.leafSize * 1.45f,
        cardsPerTip = 2,
        spreadDeg = p.spreadDeg,
        droop = p.droop,
        crownStart = p.crownStart,
    };

    /// <summary>One LOD level of a tree: a child renderer holding the mesh for that distance band.</summary>
    static Renderer LodRenderer(Transform parent, string name, Mesh mesh, Material[] mats)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterials = mats;
        mr.shadowCastingMode = ShadowCastingMode.On;
        return mr;
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
        // A stand of real trees is never one green: deep shade, olive, sun-bleached, blue-green
        // and a few turning/dead ones. Uniform foliage is what makes generated scenery read as fake.
        var leafTints = new[]
        {
            new Color(0.62f, 0.78f, 0.55f), new Color(0.82f, 0.9f, 0.62f), new Color(0.5f, 0.66f, 0.5f),
            new Color(0.72f, 0.84f, 0.72f), new Color(0.9f, 0.78f, 0.45f), new Color(0.66f, 0.5f, 0.32f),
        };
        var leafMats = new Material[leafTints.Length];
        for (int i = 0; i < leafTints.Length; i++)
        {
            leafMats[i] = FoliageMat($"Leaves_{i}", leafTints[i], leafTex, 0.18f);
        }
        var barkDark = GeneratedAssets.PbrMaterial("BarkDark", "bark_willow_02", 1f, 0.7f, new Color(0.72f, 0.68f, 0.62f));
        barkDark.enableInstancing = true;

        // Two meshes per variant: the full tree, and a reduced one for distance. A tree line dense
        // enough to actually be opaque needs ~700 instances, which is only affordable with LOD -
        // and the project had no LODGroup anywhere, so every tree was drawn at full branch detail
        // even at 400 m.
        var variants = new List<(Mesh near, Mesh far, float height, float crown)>();
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
            var near = TreeGenerator.Generate(specs[i], $"Tree_{i}", out float crown);
            var far = TreeGenerator.Generate(Simplified(specs[i]), $"Tree_{i}_far", out _);
            variants.Add((GeneratedAssets.SaveMesh(near, $"Tree_{i}"),
                          GeneratedAssets.SaveMesh(far, $"Tree_{i}_far"),
                          specs[i].height, crown));
        }

        var trees = new GameObject("Trees").transform;
        trees.SetParent(env);
        var rng = new System.Random(4321);
        float Rand(float lo, float hi) => lo + (float)rng.NextDouble() * (hi - lo);

        void Place(int variant, float x, float z, float scale, bool bush = false)
        {
            var (near, far, height, crown) = variants[variant];
            float y = GroundHeight(terrain, x, z) - 0.1f;
            var go = new GameObject(bush ? "Bush" : "Tree");
            go.transform.SetParent(trees);
            go.transform.position = new Vector3(x, y, z);
            // a few degrees of lean, and trunks are never perfectly round in plan
            go.transform.rotation = Quaternion.Euler(Rand(-4f, 4f), Rand(0f, 360f), Rand(-4f, 4f));
            go.transform.localScale = new Vector3(scale * Rand(0.9f, 1.12f), scale * Rand(0.85f, 1.25f), scale * Rand(0.9f, 1.12f));

            // weighted so most trees are ordinary green and only a few have turned
            double pick = rng.NextDouble();
            int tint = pick < 0.34 ? 0 : pick < 0.58 ? 1 : pick < 0.78 ? 2 : pick < 0.9 ? 3 : pick < 0.97 ? 4 : 5;
            var mats = new[] { rng.NextDouble() < 0.35 ? barkDark : bark, leafMats[tint] };

            // Deliberately NOT marked static any more. Occlusion culling is not baked in this
            // project (m_OcclusionCullingData is null), so isStatic only ever bought static
            // batching - and for ~1000 instances of ten shared meshes, GPU instancing, which
            // static batching suppresses, is both faster and far cheaper in memory. The sway
            // weights live in UV2, so they survive either path.
            var near0 = LodRenderer(go.transform, "LOD0", near, mats);
            var far1 = LodRenderer(go.transform, "LOD1", far, mats);
            var lods = go.AddComponent<LODGroup>();
            // Cull threshold left very low on purpose: a tree line that popped out of existence at
            // range would be worse than the see-through one it replaces.
            lods.SetLODs(new[]
            {
                new LOD(0.05f, new[] { near0 }),
                new LOD(0.004f, new[] { far1 }),
            });
            lods.RecalculateBounds();

            // Unity scales a capsule/sphere radius by the LARGEST axis of the transform, and these
            // trees have a deliberately non-uniform scale - without dividing it back out the
            // colliders bulge past the foliage and the drone "hits a tree" while still in clear air.
            var s = go.transform.localScale;
            float radiusFix = 1f / Mathf.Max(0.0001f, Mathf.Max(s.x, Mathf.Max(s.y, s.z)) / Mathf.Max(0.0001f, s.x));
            var trunk = go.AddComponent<CapsuleCollider>();
            trunk.center = new Vector3(0f, height * 0.4f, 0f);
            trunk.radius = (bush ? 0.22f : 0.26f) * radiusFix;
            trunk.height = height * 0.8f;
            // Foliage is mostly air: a collider on the crown should cover the dense middle only,
            // not the full leaf-card spread, or gaps between branches become invisible walls.
            var canopy = go.AddComponent<SphereCollider>();
            canopy.center = new Vector3(0f, height * 0.78f, 0f);
            canopy.radius = Mathf.Max(0.35f, crown * 0.42f) * radiusFix;
        }

        // Scattered trees around the field (course kept clear)
        int placed = 0;
        for (int i = 0; i < 2000 && placed < 190; i++)
        {
            float ang = Rand(0f, Mathf.PI * 2f);
            float r = Rand(40f, 300f);
            float x = Mathf.Cos(ang) * r, z = Mathf.Sin(ang) * r;
            if (VenueBuilder.Keepout(x, z, 6f) || VenueBuilder.Meadow(x, z) < 0.6f) continue;
            float n = Mathf.PerlinNoise(x * 0.01f + 50f, z * 0.01f + 50f);
            if (rng.NextDouble() > n * 1.6f) continue;   // clumps
            Place(rng.Next(0, 4), x, z, Rand(0.85f, 1.2f));
            placed++;
        }
        // Tree line ringing the far field. This was 160 trees spread over 2.2 km of circumference
        // in a 90 m-deep band - one every 14.7 m, so you could see straight between the trunks to
        // the horizon. A real woodland edge is opaque. 700 in a narrower 55 m band puts about
        // 2.2 crown-widths of foliage across every line of sight out of the field.
        const int lineTrees = 700;
        for (int i = 0; i < lineTrees; i++)
        {
            float ang = i / (float)lineTrees * Mathf.PI * 2f + Rand(-0.006f, 0.006f);
            float r = Rand(330f, 385f);
            Place(rng.Next(0, 3), Mathf.Cos(ang) * r, Mathf.Sin(ang) * r, Rand(1.1f, 1.5f));
        }
        // Deadfall: logs and broken branches under the tree clumps. Woodland floors are never bare.
        var logMesh = GeneratedAssets.SaveMesh(MeshBuilder.Lathe(new[]
        {
            new Vector2(0.16f, 0f), new Vector2(0.19f, 0.25f), new Vector2(0.15f, 1.6f),
            new Vector2(0.17f, 2.6f), new Vector2(0.12f, 3.4f), new Vector2(0.03f, 3.6f),
        }, 10, "FallenLog"), "FallenLog");
        for (int i = 0; i < 90; i++)
        {
            float ang = Rand(0f, Mathf.PI * 2f), rr = Rand(45f, 260f);
            float x = Mathf.Cos(ang) * rr, z = Mathf.Sin(ang) * rr;
            if (VenueBuilder.Keepout(x, z, 8f) || VenueBuilder.Meadow(x, z) < 0.6f) continue;
            float n2 = Mathf.PerlinNoise(x * 0.01f + 50f, z * 0.01f + 50f);   // same clumping as the trees
            if (rng.NextDouble() > n2 * 1.3f) continue;
            float y = GroundHeight(terrain, x, z);
            var log = MeshObject("Fallen log", trees, logMesh, rng.NextDouble() < 0.5 ? bark : barkDark, new Vector3(x, y + Rand(0.1f, 0.16f), z));
            log.transform.rotation = Quaternion.Euler(90f + Rand(-6f, 6f), Rand(0f, 360f), Rand(0f, 360f));
            log.transform.localScale = new Vector3(Rand(0.7f, 1.3f), Rand(0.6f, 1.25f), Rand(0.7f, 1.3f));
            log.isStatic = true;
        }

        // Desert scrub: the bush variant in a bleached palette, sparse, on the sand only
        // Stiff, low desert scrub moves far less than a tree crown in the same wind.
        var scrub = FoliageMat("DesertScrub", new Color(0.72f, 0.66f, 0.42f), leafTex, 0.12f, swayScale: 0.45f);
        for (int i = 0; i < 130; i++)
        {
            float ang = Rand(0f, Mathf.PI * 2f), r = Rand(30f, 100f);
            float x = VenueBuilder.Race.x + Mathf.Cos(ang) * r, z = VenueBuilder.Race.y + Mathf.Sin(ang) * r;
            if (VenueBuilder.Keepout(x, z, 3f) || VenueBuilder.Desert(x, z) < 0.5f) continue;
            // Knee-high scrub seen from tens of metres away - the reduced mesh is all it ever needs.
            var mesh = variants[4].far;
            float y = GroundHeight(terrain, x, z) - 0.1f;
            var go = new GameObject("Scrub");
            go.transform.SetParent(trees);
            go.transform.position = new Vector3(x, y, z);
            go.transform.rotation = Quaternion.Euler(0f, Rand(0f, 360f), 0f);
            go.transform.localScale = new Vector3(Rand(0.5f, 1.1f), Rand(0.35f, 0.7f), Rand(0.5f, 1.1f));
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = new[] { bark, scrub };
            go.isStatic = true;
        }

        // Bushes near the pit and along the track
        for (int i = 0; i < 45; i++)
        {
            float ang = Rand(0f, Mathf.PI * 2f);
            float r = Rand(30f, 120f);
            float x = Mathf.Cos(ang) * r, z = Mathf.Sin(ang) * r;
            if (VenueBuilder.Keepout(x, z, 3f) || VenueBuilder.Meadow(x, z) < 0.6f) continue;
            Place(4, x, z, Rand(0.7f, 1.3f), true);
        }
    }

    /// <summary>
    /// Aviation windsock on a pole by the pit. The scene had no wind indicator at all, and no way
    /// to see wind *direction* anywhere (the OSD showed only a magnitude) - so a crosswind that the
    /// physics was applying the whole time was invisible until it pushed the drone off line.
    ///
    /// Built as a nested chain of five open frustums so <see cref="WindVane"/> can ripple it from
    /// mouth to tail. Deliberately not marked static: it moves every frame.
    /// </summary>
    static void BuildWindsock(Transform env, Terrain terrain, Vector3 at, Material steel)
    {
        float ground = GroundHeight(terrain, at.x, at.z);
        const float poleHeight = 4.2f;
        const int segCount = 5;
        const float segLength = 0.48f;

        var root = new GameObject("Windsock").transform;
        root.SetParent(env);
        root.position = new Vector3(at.x, ground, at.z);

        Primitive(PrimitiveType.Cylinder, "Sock pole", root, new Vector3(0f, poleHeight * 0.5f, 0f),
                  new Vector3(0.08f, poleHeight * 0.5f, 0.08f), steel);
        Primitive(PrimitiveType.Cylinder, "Sock base", root, new Vector3(0f, 0.06f, 0f),
                  new Vector3(0.5f, 0.06f, 0.5f), steel);

        var orange = Mat("SockOrange", new Color(0.95f, 0.33f, 0.04f), glossiness: 0.12f);
        var white = Mat("SockWhite", new Color(0.93f, 0.93f, 0.9f), glossiness: 0.12f);

        // The chain hangs off a pivot at the top of the pole.
        var pivot = new GameObject("Sock pivot").transform;
        pivot.SetParent(root, false);
        pivot.localPosition = new Vector3(0f, poleHeight, 0f);

        // Swivel hoop at the mouth, so the sock visibly hangs from something.
        Primitive(PrimitiveType.Cylinder, "Sock hoop", pivot, Vector3.zero, new Vector3(0.9f, 0.02f, 0.9f), steel, collider: false);

        var segments = new Transform[segCount];
        Transform parent = pivot;
        for (int i = 0; i < segCount; i++)
        {
            var seg = new GameObject($"Sock seg {i}").transform;
            seg.SetParent(parent, false);
            seg.localPosition = i == 0 ? Vector3.zero : new Vector3(0f, 0f, segLength);
            segments[i] = seg;
            parent = seg;

            // Open frustum, tapering along the sock. Lathed about +Y, then turned to lie along +Z
            // (Quaternion.Euler(90,0,0) maps +Y onto +Z) so the chain runs downwind.
            float r0 = Mathf.Lerp(0.42f, 0.20f, i / (float)segCount);
            float r1 = Mathf.Lerp(0.42f, 0.20f, (i + 1) / (float)segCount);
            var mesh = GeneratedAssets.SaveMesh(MeshBuilder.Lathe(new[]
            {
                new Vector2(r0, 0f), new Vector2(r1, segLength),
            }, 14, $"SockSeg{i}", smooth: true, capBottom: false, capTop: false), $"SockSeg{i}");

            var vis = MeshObject($"Sock skin {i}", seg, mesh, i % 2 == 0 ? orange : white, Vector3.zero);
            vis.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var r = vis.GetComponent<Renderer>();
            r.shadowCastingMode = ShadowCastingMode.TwoSided;
        }

        // segments[0] sits at the pivot with no offset, so WindVane yawing it swings the whole
        // chain round the pole; the pivot itself just holds the hoop.
        var vane = root.gameObject.AddComponent<WindVane>();
        vane.kind = WindVane.Kind.Sock;
        vane.segments = segments;
    }

    /// <summary>A small farm pond nestled in the hills - a reflective plane sitting in the basin
    /// carved into the heightmap by BuildTerrain (VenueBuilder.Pond), plus the trigger volume that
    /// makes touching it fatal (see <see cref="WaterHazard"/>).</summary>
    static void BuildPond(Transform env, Terrain terrain)
    {
        var water = Mat("Water", new Color(0.08f, 0.2f, 0.26f), glossiness: 0.92f, metallic: 0.05f);

        // Water level from the basin's real geometry rather than one probe on its edge. A single
        // sample put the surface *below* the basin floor the moment the pond sat on any slope -
        // the disc ended up buried in the hillside and a drone flown into the pond passed over
        // the water entirely and landed on dry mud. Lowest point of the shore ring, dropped a
        // little so the water sits inside its banks.
        float shoreMin = float.MaxValue;
        float floorMax = float.MinValue;
        const int probes = 24;
        for (int i = 0; i < probes; i++)
        {
            float a = i / (float)probes * Mathf.PI * 2f;
            shoreMin = Mathf.Min(shoreMin, GroundHeight(terrain,
                VenueBuilder.PondCenter.x + Mathf.Cos(a) * (VenueBuilder.PondRadius + 3f),
                VenueBuilder.PondCenter.y + Mathf.Sin(a) * (VenueBuilder.PondRadius + 3f)));
            floorMax = Mathf.Max(floorMax, GroundHeight(terrain,
                VenueBuilder.PondCenter.x + Mathf.Cos(a) * (VenueBuilder.PondRadius * 0.55f),
                VenueBuilder.PondCenter.y + Mathf.Sin(a) * (VenueBuilder.PondRadius * 0.55f)));
        }
        float surfaceY = shoreMin - 0.35f;
        float centreFloor = GroundHeight(terrain, VenueBuilder.PondCenter.x, VenueBuilder.PondCenter.y);
        Debug.Log($"<b>Drone Trainer</b>: pond shore={shoreMin:0.00} floor(centre)={centreFloor:0.00} " +
                  $"floor(max)={floorMax:0.00} surface={surfaceY:0.00} depth={surfaceY - centreFloor:0.00} m");

        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = "Pond";
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(env, false);
        go.transform.position = new Vector3(VenueBuilder.PondCenter.x, surfaceY, VenueBuilder.PondCenter.y);
        go.transform.localScale = new Vector3(VenueBuilder.PondRadius * 1.6f, 0.02f, VenueBuilder.PondRadius * 1.6f);
        var mr = go.GetComponent<Renderer>();
        mr.sharedMaterial = water;
        mr.shadowCastingMode = ShadowCastingMode.Off;
        go.isStatic = true;

        // The kill volume: the water body itself, from the surface down past the basin floor. A
        // trigger on the 4 cm visual disc would be tunnelled straight through at flying speed.
        float depth = Mathf.Max(1f, surfaceY - centreFloor + 0.6f);
        var vol = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        vol.name = "Pond water volume";
        vol.transform.SetParent(env, false);
        vol.transform.position = new Vector3(VenueBuilder.PondCenter.x, surfaceY - depth * 0.5f, VenueBuilder.PondCenter.y);
        // Unity's cylinder primitive is 1 unit across and 2 units tall.
        vol.transform.localScale = new Vector3(VenueBuilder.PondRadius * 1.9f, depth * 0.5f, VenueBuilder.PondRadius * 1.9f);
        // The cylinder primitive ships a CapsuleCollider, which degenerates to a sphere at these
        // wide-and-shallow proportions - replace it with a convex hull of the cylinder itself,
        // which MeshCollider takes from the MeshFilter left in place.
        Object.DestroyImmediate(vol.GetComponent<Collider>());
        Object.DestroyImmediate(vol.GetComponent<MeshRenderer>());
        var col = vol.AddComponent<MeshCollider>();
        col.convex = true;
        col.isTrigger = true;
        vol.AddComponent<WaterHazard>().surfaceY = surfaceY;
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
        go.AddComponent<RcLink>();
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
        // The FPV camera runs the FPV-only post profile, which has no Vignette override, so the
        // shader's fisheye-shaped vignette is the only one darkening these corners and can carry
        // its full solo-use weight (it was held at 0.15 back when the shared profile's ~0.18
        // vignette stacked on top of it).
        fx.vignette = 0.35f;
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
        var losView = los.AddComponent<LineOfSightCamera>();
        losView.target = drone;
        // The pilot stands ~8 m from the pad, which is where the zoom used to only begin - a
        // 0.35 m quad was a speck on screen. Start zooming at 4 m so it stays readable.
        losView.zoomStartDistance = 4f;

        var fpvCam = drone.GetComponentInChildren<Camera>();
        fpvCam.GetComponent<FpvCameraEffects>().pilot = los.transform;
        // Same pilot position drives the control link, but with its own, much longer range: RC
        // carries far further than 5.8 GHz analog video.
        var rcLink = drone.GetComponent<RcLink>();
        if (rcLink != null) rcLink.pilot = los.transform;

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
        // Two profiles, because the FPV feed must not get the cinematic-camera finish.
        //
        // Both carry the "scene look" every view has to agree on (ambient occlusion, bloom, ACES
        // grading). Only the cinematic one adds motion blur, vignette, grain and chromatic
        // aberration. FpvAnalog.shader already supplies its own, stronger grain / fisheye vignette /
        // chromatic fringe, so those were being applied twice on the drone camera - and the motion
        // blur was outright wrong there: a 200-degree shutter smears the frame in proportion to how
        // fast the camera rotates, and an FPV camera rotates at acro rates. A few hundred deg/s at
        // 60 fps is ~30 px of smear every frame at 1080p, which is why the picture turned to mush
        // exactly when you started flying it - a real FPV camera is global-shutter and
        // short-exposure, i.e. sharp. See also the aliased-vibration fix in
        // FpvCameraEffects.LateUpdate, which the blur was amplifying.
        var cinematicProfile = BuildPostProfile($"{MatDir}/PostProcess.asset", cinematicFinish: true);
        var fpvProfile = BuildPostProfile($"{MatDir}/PostProcessFpv.asset", cinematicFinish: false);

        PostProcessResources resources = null;
        foreach (var guid in AssetDatabase.FindAssets("t:PostProcessResources"))
        {
            resources = AssetDatabase.LoadAssetAtPath<PostProcessResources>(AssetDatabase.GUIDToAssetPath(guid));
            if (resources != null) break;
        }

        // A PostProcessLayer cannot drop individual effects - it takes whole volumes, selected by
        // the volume GameObject's layer - so the FPV-only profile needs a layer to itself.
        int fpvLayer = EnsureLayer("FpvPost");

        var volumeGo = new GameObject("Post-process volume");
        var volume = volumeGo.AddComponent<PostProcessVolume>();
        volume.isGlobal = true;
        volume.priority = 1f;
        volume.sharedProfile = cinematicProfile;

        if (fpvLayer > 0)
        {
            var fpvVolumeGo = new GameObject("Post-process volume (FPV)") { layer = fpvLayer };
            var fpvVolume = fpvVolumeGo.AddComponent<PostProcessVolume>();
            fpvVolume.isGlobal = true;
            fpvVolume.priority = 1f;
            fpvVolume.sharedProfile = fpvProfile;
        }

        foreach (var cam in cams)
        {
            bool isFpv = cam.GetComponent<FpvCameraEffects>() != null;
            var layer = cam.gameObject.AddComponent<PostProcessLayer>();
            if (resources != null) layer.Init(resources);
            layer.volumeTrigger = cam.transform;
            layer.volumeLayer = isFpv && fpvLayer > 0 ? 1 << fpvLayer : 1; // 1 = Default layer
            layer.antialiasingMode = PostProcessLayer.Antialiasing.SubpixelMorphologicalAntialiasing;
            layer.fog.enabled = true;
            layer.fog.excludeSkybox = true;
        }
#else
        Debug.LogWarning("Post Processing package not present - skipping post effects.");
#endif
    }

#if UNITY_POST_PROCESSING_STACK_V2
    /// <summary>
    /// Writes a PostProcessProfile asset. <paramref name="cinematicFinish"/> adds the film-camera
    /// layer (motion blur, vignette, grain, chromatic aberration) on top of the shared scene look;
    /// the FPV feed leaves it off and uses FpvAnalog.shader's analog-video look instead.
    /// </summary>
    static PostProcessProfile BuildPostProfile(string profilePath, bool cinematicFinish)
    {
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
        ao.intensity.Override(1.05f);
        ao.thicknessModifier.Override(1.6f);
        var bloom = Add<Bloom>();
        // Restrained: at 1.3 / 0.9 the white spawn circle in direct sun bloomed into a pool of
        // glare that swallowed the drone standing on it. Only real highlights should bloom now.
        bloom.intensity.Override(0.55f);
        bloom.threshold.Override(1.15f);
        bloom.softKnee.Override(0.6f);
        var cg = Add<ColorGrading>();
        cg.tonemapper.Override(Tonemapper.ACES);
        // Outdoor scenes read as flat and washed when the shadows are lifted and the midtones sit
        // high: pull the exposure down slightly, deepen the curve, and tint the shadows cool so
        // they separate from the warm sunlight instead of going grey.
        cg.postExposure.Override(-0.12f);
        cg.saturation.Override(13f);
        cg.contrast.Override(26f);
        cg.temperature.Override(5f);
        cg.tint.Override(-2f);
        cg.lift.Override(new Vector4(0.96f, 0.99f, 1.06f, -0.015f));
        cg.gamma.Override(new Vector4(1f, 1f, 1f, -0.04f));
        cg.gain.Override(new Vector4(1.02f, 1f, 0.97f, 0.02f));
        // Cheap cinematic-camera finish for chase/LOS/showcase only - the FPV feed gets its own
        // analog-video look from FpvAnalog.shader and would otherwise be double-graded and, worse,
        // motion-blurred in proportion to its own rotation rate.
        if (cinematicFinish)
        {
            var mb = Add<MotionBlur>();
            mb.shutterAngle.Override(200f);
            mb.sampleCount.Override(10);
            var vig = Add<Vignette>();
            vig.intensity.Override(0.18f);
            vig.smoothness.Override(0.5f);
            var grain = Add<Grain>();
            grain.intensity.Override(0.18f);
            grain.size.Override(1.3f);
            grain.lumContrib.Override(0.8f);
            var ca = Add<ChromaticAberration>();
            ca.intensity.Override(0.07f); // kept low: edge fringing hurts the precise visual feedback a flight trainer needs
        }
        EditorUtility.SetDirty(profile);
        return profile;
    }
#endif

    /// <summary>
    /// Returns the index of the named layer, claiming a free user slot (8-31) for it if it does not
    /// exist yet. Returns 0 (Default) when every slot is taken, so callers can fall back.
    /// </summary>
    static int EnsureLayer(string name)
    {
        var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
        if (assets == null || assets.Length == 0) return 0;
        var so = new SerializedObject(assets[0]);
        var layers = so.FindProperty("layers");
        if (layers == null) return 0;

        for (int i = 0; i < layers.arraySize; i++)
            if (layers.GetArrayElementAtIndex(i).stringValue == name) return i;

        // 0-7 are Unity's builtin slots; only 8 and up are ours to name.
        for (int i = 8; i < layers.arraySize; i++)
        {
            var slot = layers.GetArrayElementAtIndex(i);
            if (!string.IsNullOrEmpty(slot.stringValue)) continue;
            slot.stringValue = name;
            so.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
            return i;
        }
        Debug.LogWarning($"Drone Trainer: no free layer slot for \"{name}\" - falling back to the Default layer.");
        return 0;
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

        // Laid out in the yard's open middle: clear of the warehouses (z >= 106), the
        // container stacks (x ~29) and the car lot (x +-6.5, z 78.5-85.5).
        CrateTower(ox - 22f, oz + 30f, 3, 5);
        CrateTower(ox - 20f, oz + 40f, 2, 6);
        BarrelPyramid(ox + 16f, oz + 30f);
        Wall(ox + 10f, oz + 40f, 20f);
        Wall(ox - 14f, oz + 14f, -35f);

        // Target cars: parked in a real asphalt lot with painted bays, each marked with a
        // spinning orange diamond - the exact, unmistakable targets, easy to find and reach
        // right after take-off (the lot sits in the open centre of the gate loop).
        float lotCenterX = ox, lotCenterZ = oz + 22f;
        // Cars must stand ON the slab: their wheels touch y = 0 locally, so spawning them at
        // terrain height would bury the wheels 2 cm in the asphalt - the depenetration kick
        // snaps the breakaway wheel joints and flips the car before the drill even starts.
        float lotSurface = BuildParkingLot(parent, terrain, lotCenterX, lotCenterZ);
        var carColors = new[] { new Color(0.82f, 0.07f, 0.05f), new Color(0.08f, 0.32f, 0.82f), new Color(0.92f, 0.78f, 0.05f) };
        const int carWeight = 8; // each target car counts as several crates towards the demolition score
        for (int i = 0; i < 3; i++)
        {
            float x = lotCenterX + (i - 1) * 4f;
            float z = lotCenterZ;
            var carBlock = CarBuilder.Build(parent, new Vector3(x, lotSurface + 0.01f, z), 180f, carColors[i], $"Target car {i + 1}");
            CarBuilder.AddTargetMarker(carBlock.transform);
            builtTargetCars.Add(carBlock);
            for (int k = 0; k < carWeight; k++) list.Add(carBlock);
        }

        foreach (var b in list) b.gameObject.SetActive(false);
        return list.ToArray();
    }

    /// <summary>A small asphalt lot with 3 painted parking bays for the target cars. Returns the surface height cars must sit on.</summary>
    static float BuildParkingLot(Transform parent, Terrain terrain, float centerX, float centerZ)
    {
        var asphalt = GeneratedAssets.PbrMaterial("Asphalt", "asphalt_pit_lane", 1f, 0.9f);
        var paint = Mat("PaintWhite", new Color(0.72f, 0.72f, 0.7f), glossiness: 0.25f);

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
        return g + 0.02f; // top face of the 0.1 m slab centred at g - 0.03
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
            float x, z;
            int guard = 0;
            do
            {
                x = ox - 24f + (float)rng.NextDouble() * 48f;
                z = oz + 10f + (float)rng.NextDouble() * 32f;
            }
            while (guard++ < 30 && Mathf.Abs(x - ox) < 9f && z > oz + 16f && z < oz + 28f); // keep off the car lot
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
        var positions = new[] { new Vector3(ox - 18f, 0f, oz + 16f), new Vector3(ox + 16f, 0f, oz + 18f), new Vector3(ox, 0f, oz + 40f) };
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
        var paint = Mat("PaintWhite", new Color(0.72f, 0.72f, 0.7f), glossiness: 0.25f);
        // Flat painted pad + H, laid on the asphalt (a scaled cylinder here showed a faceted lip).
        var pad = PaintDisc("LandingPad", root, padMat, new Vector3(8f, 0.02f, 1f), 1.1f);
        Primitive(PrimitiveType.Cube, "H1", pad.transform, new Vector3(-0.24f, 0.0012f, 0f), new Vector3(0.09f, 0.0016f, 0.55f), paint, collider: false);
        Primitive(PrimitiveType.Cube, "H2", pad.transform, new Vector3(0.24f, 0.0012f, 0f), new Vector3(0.09f, 0.0016f, 0.55f), paint, collider: false);
        Primitive(PrimitiveType.Cube, "H3", pad.transform, new Vector3(0f, 0.0012f, 0f), new Vector3(0.39f, 0.0016f, 0.09f), paint, collider: false);
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

    static void BuildTrainer(GameObject drone, Camera[] cams, Course course, Terrain terrain)
    {
        var physics = drone.GetComponent<DronePhysics>();
        var fc = drone.GetComponent<FlightController>();
        var crash = drone.GetComponent<CrashDetector>();
        var input = drone.GetComponent<PilotInput>();

        var trainer = new GameObject("Trainer");
        trainer.AddComponent<SimBootstrap>();
        var wind = trainer.AddComponent<Wind>();
        // Pushes the live wind into the foliage shader and the terrain grass. Without this the
        // wind existed only in the drone physics and the OSD number.
        var windVisuals = trainer.AddComponent<WindVisuals>();
        windVisuals.wind = wind;
        windVisuals.terrain = terrain;

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
            "The drone flies the mission by itself: take-off, the gate loop at 6 m, return and land. Watch from any camera (C). Move the sticks to take over; Backspace restarts the agent.", FlightMode.Angle, 120f);
        a1.gates = course.gates;
        a1.altitude = 6f; // gate tops are at 3.1 m; 4 m left no margin for the sag while banking

        // ---- Student briefings: goal, numbered steps, the skill, the usual mistake, the keys ----
        void Brief(Lesson l, int difficulty, string goal, string skill, string tip, string controls, params string[] steps)
        {
            l.difficulty = difficulty;
            l.goal = goal;
            l.skill = skill;
            l.tip = tip;
            l.controls = controls;
            l.steps = steps;
        }

        Brief(b1, 1, "Lift off, then keep the drone inside the yellow box for 10 seconds.",
            "Throttle control - holding a steady height is the first thing every pilot must own.",
            "Students push the throttle too far and shoot past the box. Move it a little, then wait a moment to see the result.",
            "W / S = throttle (up / down),   arrow keys = tilt,   SPACE = arm",
            "Throttle at the bottom, then press SPACE to arm. The props spin slowly.",
            "Raise the throttle gently with W until the drone leaves the ground, at about 35%.",
            "Nudge the arrow keys to slide across into the yellow box.",
            "Hold it inside for 10 seconds, correcting early with tiny inputs.");

        Brief(b2, 1, "Climb above 3 m, then land softly on the orange H pad and stay still for 2 seconds.",
            "Controlled descent and a soft touch-down - the part of every flight that breaks props.",
            "Cutting the throttle to zero over the pad drops the drone. Sink slowly, under 2 m/s, and keep a little throttle right to the ground.",
            "W / S = throttle,   arrow keys = tilt,   SPACE = arm",
            "Arm and climb above 3 m.",
            "Fly over the orange H pad with small arrow-key nudges.",
            "Lower the throttle slightly and let it sink slowly - slower than 2 m/s.",
            "Touch down and hold still for 2 seconds.");

        Brief(b3, 2, "Fly 2 full circles around the striped pylon, staying 3-8 m from it and above 1 m.",
            "Coordinating yaw with roll - the turn that lets you follow a subject or a track.",
            "Rolling alone makes you drift away from the pylon. Add a little yaw all the way round so the nose keeps pointing at it.",
            "A / D = yaw (turn),   arrow keys = tilt,   W / S = throttle",
            "Arm, climb to about 3 m and fly out to one side of the pylon.",
            "Roll gently to start moving sideways around it.",
            "At the same time hold a little yaw so the nose stays on the pylon.",
            "Keep the distance between 3 and 8 m for two full laps.");

        Brief(r1, 2, "Fly through all 8 gates in order. The next gate glows green.",
            "Reading a course and lining up on a target - aim at the gate before you reach it.",
            "Turning at the last moment clips the posts. Point the nose at the next gate while you are still far away.",
            "arrow keys = tilt,   A / D = yaw,   W / S = throttle",
            "Arm and climb to gate height, about 2 m.",
            "Find the gate glowing green - that is your next one.",
            "Yaw to point at it, then tilt forward to fly through the middle.",
            "Straight after passing, turn towards the next one.");

        Brief(r2, 4, "Fly two laps of the same course in ACRO, where the drone does not level itself.",
            "Flying without self-levelling - the mode real freestyle and race pilots use.",
            "In ACRO the drone keeps whatever tilt you give it. After every input return the stick to centre and level the drone yourself.",
            "M = switch mode,   arrow keys = tilt,   A / D = yaw,   W / S = throttle",
            "Press M until the top-left corner reads ACRO.",
            "Arm and lift off with small inputs - it will drift, that is normal.",
            "Tap the arrows instead of holding them, and level out after each tap.",
            "Complete two laps of the gates.");

        Brief(r3, 4, "Wait for the 3-2-1-GO countdown, then fly three timed laps.",
            "Racing under pressure and starting cleanly - your best lap is saved as a record.",
            "Crossing a gate before GO is a false start and restarts the countdown. Hold your position until you see GO.",
            "arrow keys = tilt,   A / D = yaw,   W / S = throttle",
            "Arm and hover behind the start arch.",
            "Wait for GO - do not cross a gate early.",
            "Fly three laps, carrying your speed through the gates.",
            "Brake by tilting back, not by cutting the throttle.");

        Brief(r4, 5, "Beat 22 seconds on a single lap. You get two laps to try.",
            "Flying a racing line: wide entry, tight exit, no wasted movement.",
            "Full throttle everywhere is slower. Ease off before a turn and add it again as you come out.",
            "M = mode,   arrow keys = tilt,   A / D = yaw,   W / S = throttle",
            "Wait for GO, then build speed along the straight.",
            "Enter each turn from the outside and cut close to the inside gate.",
            "Keep the drone smooth - every wobble costs time.",
            "The better of your two laps counts.");

        Brief(f1, 4, "In ACRO, above 6 m, do three full rolls.",
            "Throwing and catching a roll - the basic freestyle move.",
            "Students catch it too late and lose height. Add throttle first, then centre the stick the moment the drone is level again.",
            "M = mode,   left / right arrows = roll,   W / S = throttle",
            "Press M for ACRO and climb above 6 m.",
            "Add a little throttle to gain height before you start.",
            "Push the roll stick fully to one side and hold it.",
            "The instant the drone is level again, centre the stick and add throttle.");

        Brief(f2, 4, "In ACRO, above 6 m, do three full flips.",
            "The flip - the same catch as a roll, but around the other axis and with more height loss.",
            "A flip drops you further than a roll. Start higher than you think you need and punch the throttle just before you begin.",
            "M = mode,   up / down arrows = pitch,   W / S = throttle",
            "ACRO mode, climb above 6 m.",
            "Punch the throttle for a moment to gain height.",
            "Push the pitch stick fully forward or back and hold it.",
            "Catch it level, centre the stick, add throttle.");

        Brief(f3, 5, "Do two power loops: dive for speed, then loop up and over the top.",
            "Trading height for speed and back again - the core of freestyle flying.",
            "Without enough speed at the bottom you will stall on your back at the top. Dive first, then pull.",
            "M = mode,   up / down arrows = pitch,   W / S = throttle",
            "ACRO, climb high and dive to build speed.",
            "Pull back steadily and keep pulling over the top.",
            "You must gain 2.5 m or more at the highest point.",
            "Complete the circle and fly out level.");

        Brief(d1, 2, "Fly through all 14 balloons tied around the yard.",
            "Planning an efficient route and hitting a small target in three dimensions.",
            "Chasing balloons one at a time wastes the clock. Look ahead and link them into a route.",
            "arrow keys = tilt,   A / D = yaw,   W / S = throttle",
            "Arm and take off from the pad.",
            "Pick the nearest group of balloons, not just the nearest balloon.",
            "Fly through each one - touching it pops it.",
            "Keep moving: your time is scored.");

        Brief(d2, 3, "Drop water balloons onto the three bullseyes. You carry 6.",
            "Leading a target - a released load keeps your speed and falls behind you.",
            "Pressing F right over the target always overshoots. Release before you get there, or slow down first.",
            "F = release a balloon,   arrow keys = tilt,   W / S = throttle",
            "Arm and take off - the balloons make the drone heavier.",
            "Fly over a bullseye at a steady height.",
            "Press F slightly BEFORE you are above the centre.",
            "Watch where it lands and correct the next drop. 10 / 5 / 2 points by ring.");

        Brief(d3, 2, "Fly into the RED car only. You have 3 drones.",
            "Committing to a chosen target and hitting it accurately.",
            "Hitting anything else wastes a drone. Identify the red car by its spinning orange marker before you accelerate.",
            "arrow keys = tilt,   A / D = yaw,   W / S = throttle",
            "Arm and climb a few metres.",
            "Find the RED car - the one with the spinning orange marker.",
            "Line up on it from a distance and fly straight in.",
            "Fewer drones used = more stars.");

        Brief(d4, 3, "Hit the BLUE car at 12 m/s or faster. You have 3 drones.",
            "Building and holding speed on an approach, and reading your own speed display.",
            "Lifting the nose just before impact kills your speed. Stay low, stay committed, and watch SPD in the top-left corner.",
            "arrow keys = tilt,   W / S = throttle,   SPD shows your speed",
            "Arm and fly well away from the car to make room.",
            "Turn back and accelerate along a long straight line.",
            "Keep the SPD readout above 12 m/s.",
            "Hold that line into the blue car without flaring.");

        Brief(d5, 4, "Demolish 60% of the yard using 5 drones.",
            "Choosing the target that gives the most result - the same judgement as picking a landing spot.",
            "One drone into one crate is wasted. Aim at the base of a tall stack so the whole thing comes down.",
            "arrow keys = tilt,   A / D = yaw,   W / S = throttle",
            "Look over the yard: cars, crate towers, barrel pyramids and walls.",
            "Pick the target where a single hit topples the most.",
            "Hit it low, at the base, with speed.",
            "Repeat with your remaining drones until you pass 60%.");

        Brief(a1, 1, "Watch the drone fly the whole mission by itself, then take over whenever you like.",
            "What a smooth, correct flight looks like - copy the heights, speeds and turns you see.",
            "Do not just watch the drone move. Watch where it slows down and how wide it turns, then fly the same line yourself.",
            "C = change camera,   move any stick = take over,   Backspace = restart the agent",
            "Start the drill - the drone arms and takes off on its own.",
            "Press C to watch from the chase, pilot and FPV cameras.",
            "Move a stick at any time to take control.",
            "Press Backspace to hand the mission back to the agent.");

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
        menu.flightController = fc;
        menu.osd = osd;
        osd.menu = menu;

        // Picture-in-picture feed from the drone camera (V), so the drone view is available
        // without giving up the chase / line-of-sight view.
        var monitor = trainer.AddComponent<FpvMonitor>();
        monitor.cameras = switcher;
        monitor.input = input;
        monitor.monitorIndex = 2;   // starts on the drone camera; clicking the inset swaps it
        osd.fpvMonitor = monitor;

        // Per-venue weather / time of day
        var atmo = trainer.AddComponent<VenueAtmosphere>();
        atmo.sun = GameObject.Find("Directional Light")?.GetComponent<Light>();
        atmo.sky = RenderSettings.skybox;
        atmo.presets = new[]
        {
            new VenueAtmosphere.Preset { venue = "Pit field", sunEuler = new Vector3(52f, -58f, 0f), sunColor = new Color(1f, 0.96f, 0.9f),
                sunIntensity = 1.6f, ambient = 1f, fogColor = new Color(0.72f, 0.78f, 0.86f), fogDensity = 0.0009f, skyExposure = 1f, skyRotation = 0f, skyTint = Color.white },
            new VenueAtmosphere.Preset { venue = "Race track", sunEuler = new Vector3(22f, -120f, 0f), sunColor = new Color(1f, 0.87f, 0.68f),
                sunIntensity = 1.45f, ambient = 0.95f, fogColor = new Color(0.84f, 0.77f, 0.63f), fogDensity = 0.0022f, skyExposure = 1f, skyRotation = 120f, skyTint = new Color(1f, 0.93f, 0.8f) },
            new VenueAtmosphere.Preset { venue = "Freestyle park", sunEuler = new Vector3(64f, 40f, 0f), sunColor = new Color(0.86f, 0.89f, 0.95f),
                sunIntensity = 0.95f, ambient = 0.85f, fogColor = new Color(0.6f, 0.63f, 0.69f), fogDensity = 0.0028f, skyExposure = 0.8f, skyRotation = 210f, skyTint = new Color(0.82f, 0.85f, 0.92f) },
            new VenueAtmosphere.Preset { venue = "Demolition yard", sunEuler = new Vector3(13f, 155f, 0f), sunColor = new Color(1f, 0.72f, 0.45f),
                sunIntensity = 1.35f, ambient = 0.85f, fogColor = new Color(0.7f, 0.57f, 0.45f), fogDensity = 0.003f, skyExposure = 0.9f, skyRotation = 300f, skyTint = new Color(1f, 0.85f, 0.7f) },
        };
        manager.atmosphere = atmo;

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
    /// <summary>
    /// Flat painted disc laid on a surface, built with the same Extrude path as the asphalt pads
    /// (smooth 72-gon, 1.5 mm thick). A scaled primitive cylinder stood several mm proud of the
    /// ground and its faceted lip cut across the drone's arms from a low angle.
    /// </summary>
    public static GameObject PaintDisc(string name, Transform parent, Material mat, Vector3 centreOnSurface, float radius, float lift = 0.0015f)
    {
        // Reuse the asset if it already exists: GeneratedAssets.SaveMesh deletes and recreates the
        // asset, so building a second marking of the same radius would pull the mesh out from under
        // the first one and leave it with a dangling reference (an invisible ring).
        string key = $"PaintDisc_{radius:0.000}";
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>($"{GeneratedAssets.Dir}/Meshes/{key}.asset");
        if (mesh == null) mesh = GeneratedAssets.SaveMesh(MeshBuilder.Extrude(MeshBuilder.Circle(radius, 72), 0.0015f, key, 1.5f), key);
        var go = MeshObject(name, parent, mesh, mat, centreOnSurface + new Vector3(0f, lift, 0f));
        go.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
        go.isStatic = true;
        return go;
    }

    /// <summary>Painted ring flush with the ground: a paint disc with the ground material laid back
    /// over its middle, 0.6 mm higher - close enough that no lip is visible from any angle.</summary>
    public static GameObject PaintRing(string name, Transform parent, Material paintMat, Material groundMat, Vector3 centreOnSurface, float rInner, float rOuter)
    {
        var ring = PaintDisc(name, parent, paintMat, centreOnSurface, rOuter);
        PaintDisc(name + " inner", ring.transform.parent, groundMat, centreOnSurface, rInner, 0.0021f);
        return ring;
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
