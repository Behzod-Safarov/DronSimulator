using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Import rules for downloaded PBR textures and helpers that persist generated
/// meshes / textures / materials as assets so the built scene keeps them.
/// </summary>
public class PbrTextureImporter : AssetPostprocessor
{
    /// <summary>Set while the scene builder temporarily imports the HDRI as a readable 2-D texture.</summary>
    public static bool SkipHdrRule;

    void OnPreprocessTexture()
    {
        if (SkipHdrRule && assetPath.EndsWith(".hdr")) return;
        bool external = assetPath.Contains("/External/");
        bool generated = assetPath.Contains("/Generated/");
        if (!external && !generated) return;

        var imp = (TextureImporter)assetImporter;
        string name = Path.GetFileNameWithoutExtension(assetPath).ToLowerInvariant();
        imp.maxTextureSize = 2048;
        imp.anisoLevel = 8;
        imp.mipmapEnabled = true;
        imp.streamingMipmaps = true;

        if (assetPath.EndsWith(".hdr"))
        {
            // Skybox: lat-long panorama -> cubemap
            imp.textureShape = TextureImporterShape.TextureCube;
            imp.generateCubemap = TextureImporterGenerateCubemap.Cylindrical;
            imp.sRGBTexture = false;
            imp.mipmapEnabled = true;
            return;
        }
        if (name.EndsWith("_nor_gl") || name.EndsWith("_normal"))
        {
            imp.textureType = TextureImporterType.NormalMap;
            imp.sRGBTexture = false;
        }
        else if (name.EndsWith("_rough") || name.EndsWith("_mask") || name.EndsWith("_metallicsmooth"))
        {
            imp.textureType = TextureImporterType.Default;
            imp.sRGBTexture = false;
            imp.isReadable = name.EndsWith("_rough");
        }
        else if (name.EndsWith("_alpha") || name.EndsWith("_cutout"))
        {
            imp.alphaIsTransparency = true;
            imp.mipmapEnabled = true;
        }
    }
}

public static class GeneratedAssets
{
    public const string Dir = "Assets/Generated";

    static void Ensure(string sub)
    {
        if (!AssetDatabase.IsValidFolder(Dir)) AssetDatabase.CreateFolder("Assets", "Generated");
        if (!AssetDatabase.IsValidFolder($"{Dir}/{sub}")) AssetDatabase.CreateFolder(Dir, sub);
    }

    public static Mesh SaveMesh(Mesh mesh, string name)
    {
        Ensure("Meshes");
        string path = $"{Dir}/Meshes/{name}.asset";
        if (AssetDatabase.LoadAssetAtPath<Mesh>(path) != null) AssetDatabase.DeleteAsset(path);
        mesh.name = name;
        AssetDatabase.CreateAsset(mesh, path);
        return mesh;
    }

    /// <summary>Writes a PNG and imports it (suffix decides the import type, see PbrTextureImporter).</summary>
    public static Texture2D SaveTexture(Texture2D tex, string name)
    {
        Ensure("Textures");
        string path = $"{Dir}/Textures/{name}.png";
        File.WriteAllBytes(path, tex.EncodeToPNG());
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        Object.DestroyImmediate(tex);
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    /// <summary>Builds a Standard-shader metallic/smoothness map (A = 1 - roughness) from a Poly Haven roughness map.</summary>
    public static Texture2D MetallicSmoothFromRoughness(Texture2D rough, string name, float metallic = 0f, float smoothnessScale = 1f)
    {
        Ensure("Textures");
        string path = $"{Dir}/Textures/{name}_MetallicSmooth.png";
        var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if (existing != null) return existing;

        int w = Mathf.Min(rough.width, 1024), h = Mathf.Min(rough.height, 1024);
        var src = rough.GetPixels(0);
        int sw = rough.width;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false, true);
        var px = new Color[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float r = src[(y * rough.height / h) * sw + x * sw / w].r;
                px[y * w + x] = new Color(metallic, metallic, metallic, Mathf.Clamp01((1f - r) * smoothnessScale));
            }
        tex.SetPixels(px);
        tex.Apply();
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    public static Texture2D LoadExternal(string file) => AssetDatabase.LoadAssetAtPath<Texture2D>($"Assets/External/PolyHaven/{file}");

    /// <summary>Standard-shader PBR material from a Poly Haven texture set (diffuse / nor_gl / rough).</summary>
    public static Material PbrMaterial(string matName, string asset, float tiling, float smoothnessScale = 1f, Color? tint = null)
    {
        var mat = SceneBuilder.Mat(matName, tint ?? Color.white, glossiness: 0.5f);
        var diffuse = LoadExternal($"{asset}_Diffuse.jpg");
        var normal = LoadExternal($"{asset}_nor_gl.jpg");
        var rough = LoadExternal($"{asset}_Rough.jpg");
        mat.mainTexture = diffuse;
        mat.mainTextureScale = Vector2.one * tiling;
        if (normal != null)
        {
            mat.SetTexture("_BumpMap", normal);
            mat.EnableKeyword("_NORMALMAP");
            mat.SetFloat("_BumpScale", 1f);
        }
        if (rough != null)
        {
            var ms = MetallicSmoothFromRoughness(rough, asset, 0f, smoothnessScale);
            mat.SetTexture("_MetallicGlossMap", ms);
            mat.EnableKeyword("_METALLICGLOSSMAP");
            mat.SetFloat("_GlossMapScale", 1f);
        }
        EditorUtility.SetDirty(mat);
        return mat;
    }
}
