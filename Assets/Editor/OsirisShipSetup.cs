using System.IO;
using UnityEditor;
using UnityEngine;

public static class OsirisShipSetup
{
    const string Root = "Assets/Art/Models/Osiris";

    [MenuItem("Osiris/Set Up Ship Materials")]
    public static void SetUpAll()
    {
        if (!AssetDatabase.IsValidFolder(Root))
        {
            Debug.LogError($"[Osiris] Folder not found: {Root}");
            return;
        }
        int n = 0;
        foreach (string fbx in Directory.GetFiles(Root, "*.fbx", SearchOption.AllDirectories))
            if (SetUp(fbx.Replace('\\', '/'))) n++;
        AssetDatabase.SaveAssets();
        Debug.Log($"[Osiris] Set up {n} units.");
    }

    static bool SetUp(string fbx)
    {
        string dir = Path.GetDirectoryName(fbx).Replace('\\', '/');
        string name = Path.GetFileNameWithoutExtension(fbx);
        string tex = $"{dir}/Textures/{name}_";
        if (!File.Exists(tex + "BaseColor.png")) return false;

        Material mat = AssetDatabase.LoadAssetAtPath<Material>($"{dir}/{name}.mat");
        if (mat == null)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(mat, $"{dir}/{name}.mat");
        }
        mat.SetTexture("_BaseMap", Tex(tex + "BaseColor.png", true));
        mat.SetColor("_BaseColor", Color.white);
        mat.SetTexture("_BumpMap", Tex(tex + "Normal.png", false, true));
        mat.EnableKeyword("_NORMALMAP");
        mat.SetTexture("_MetallicGlossMap", Tex(tex + "MetallicSmoothness.png", false));
        mat.SetFloat("_Smoothness", 1f);
        mat.SetFloat("_SmoothnessTextureChannel", 0f);
        mat.EnableKeyword("_METALLICSPECGLOSSMAP");
        mat.SetTexture("_OcclusionMap", Tex(tex + "Occlusion.png", false));
        mat.EnableKeyword("_OCCLUSIONMAP");
        mat.SetTexture("_EmissionMap", Tex(tex + "Emission.png", true));
        mat.SetColor("_EmissionColor", Color.white * (name.StartsWith("Avanti") ? 2.5f : 1.8f));
        mat.EnableKeyword("_EMISSION");
        mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        EditorUtility.SetDirty(mat);

        var importer = (ModelImporter)AssetImporter.GetAtPath(fbx);
        importer.bakeAxisConversion = false;
        importer.importNormals = ModelImporterNormals.Import;
        importer.importTangents = ModelImporterTangents.CalculateMikk;
        importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
        importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), name), mat);
        importer.SaveAndReimport();
        Debug.Log($"[Osiris] {name}: material set up and remapped.");
        return true;
    }

    static Texture2D Tex(string path, bool srgb, bool normalMap = false)
    {
        var ti = AssetImporter.GetAtPath(path) as TextureImporter;
        if (ti == null) { Debug.LogWarning($"[Osiris] Missing texture: {path}"); return null; }
        bool dirty = false;
        if (normalMap && ti.textureType != TextureImporterType.NormalMap) { ti.textureType = TextureImporterType.NormalMap; dirty = true; }
        if (!normalMap && ti.sRGBTexture != srgb) { ti.sRGBTexture = srgb; dirty = true; }
        if (dirty) ti.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }
}
