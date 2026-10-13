using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;

public static class DestructionSetup
{
    private const string Folder = "Assets/Data/Destruction";
    private const string ProfilePath = Folder + "/DestructionProfile.asset";
    private const string InteriorPath = Folder + "/WreckInterior.mat";
    private const string AlbedoPath = Folder + "/WreckInterior_Albedo.png";
    private const string EmissionPath = Folder + "/WreckInterior_Emission.png";

    [MenuItem("Osiris/Destruction/Set Up Ships")]
    public static void SetUpShips()
    {
        EnsureWreckLayer();
        DestructionProfile profile = EnsureProfile();
        HashSet<string> models = new HashSet<string>();
        List<string> changed = new List<string>();

        List<string> paths = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs" })
            .Select(AssetDatabase.GUIDToAssetPath)
            .OrderBy(path => PrefabUtility.GetPrefabAssetType(AssetDatabase.LoadAssetAtPath<GameObject>(path)) == PrefabAssetType.Variant ? 1 : 0)
            .ToList();

        foreach (string path in paths)
        {
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (!IsShip(asset)) continue;

            CollectModels(asset, models);
            if (Configure(path, profile)) changed.Add(Path.GetFileNameWithoutExtension(path));
        }

        int madeReadable = MakeReadable(models);
        AssetDatabase.SaveAssets();

        Debug.Log($"Destruction setup: configured {changed.Count} ship prefabs ({string.Join(", ", changed)}), made {madeReadable} models readable. Profile: {ProfilePath}");
        Selection.activeObject = profile;
    }

    [MenuItem("Osiris/Destruction/Select Profile")]
    private static void SelectProfile()
    {
        Selection.activeObject = EnsureProfile();
    }

    private static void EnsureWreckLayer()
    {
        SerializedObject tags = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        SerializedProperty layers = tags.FindProperty("layers");

        for (int i = 8; i < layers.arraySize; i++)
        {
            if (layers.GetArrayElementAtIndex(i).stringValue == "Wreck") return;
        }

        for (int i = 8; i < layers.arraySize; i++)
        {
            SerializedProperty layer = layers.GetArrayElementAtIndex(i);
            if (!string.IsNullOrEmpty(layer.stringValue)) continue;
            layer.stringValue = "Wreck";
            tags.ApplyModifiedPropertiesWithoutUndo();
            Debug.Log($"Destruction setup: added 'Wreck' layer at index {i}.");
            return;
        }

        Debug.LogWarning("Destruction setup: no free layer for 'Wreck'; wrecks will not block lasers.");
    }

    private static bool IsShip(GameObject asset)
    {
        return asset != null
            && asset.GetComponent<UnitHealthManager>() != null
            && asset.GetComponent<Squadron>() == null;
    }

    private static bool Configure(string path, DestructionProfile profile)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            bool dirty = false;
            Transform gfx = root.transform.Find("GFX");

            HullDamage hull = root.GetComponent<HullDamage>();
            if (hull == null)
            {
                hull = root.AddComponent<HullDamage>();
                dirty = true;
            }
            if (hull.profile == null)
            {
                hull.profile = profile;
                dirty = true;
            }
            if (hull.gfxRoot == null && gfx != null)
            {
                hull.gfxRoot = gfx;
                dirty = true;
            }

            if (root.GetComponent<ShipDestruction>() == null)
            {
                root.AddComponent<ShipDestruction>();
                dirty = true;
            }

            UnitHealthManager health = root.GetComponent<UnitHealthManager>();
            if (gfx != null)
            {
                for (int i = health.onDie.GetPersistentEventCount() - 1; i >= 0; i--)
                {
                    if (health.onDie.GetPersistentTarget(i) == gfx.gameObject && health.onDie.GetPersistentMethodName(i) == "SetActive")
                    {
                        UnityEventTools.RemovePersistentListener(health.onDie, i);
                        dirty = true;
                    }
                }
            }

            if (dirty) PrefabUtility.SaveAsPrefabAsset(root, path);
            return dirty;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void CollectModels(GameObject asset, HashSet<string> models)
    {
        Transform gfx = asset.transform.Find("GFX");
        Transform scope = gfx != null ? gfx : asset.transform;

        foreach (MeshFilter filter in scope.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.sharedMesh == null) continue;
            string modelPath = AssetDatabase.GetAssetPath(filter.sharedMesh);
            if (AssetImporter.GetAtPath(modelPath) is ModelImporter) models.Add(modelPath);
        }
    }

    private static int MakeReadable(IEnumerable<string> models)
    {
        int count = 0;
        foreach (string modelPath in models)
        {
            ModelImporter importer = AssetImporter.GetAtPath(modelPath) as ModelImporter;
            if (importer == null || importer.isReadable) continue;

            importer.isReadable = true;
            importer.SaveAndReimport();
            count++;
        }
        return count;
    }

    private static DestructionProfile EnsureProfile()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Data")) AssetDatabase.CreateFolder("Assets", "Data");
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Data", "Destruction");

        DestructionProfile profile = AssetDatabase.LoadAssetAtPath<DestructionProfile>(ProfilePath);
        if (profile != null)
        {
            profile.interiorMaterial = EnsureInterior();
            EditorUtility.SetDirty(profile);
            return profile;
        }

        Dictionary<string, string> effects = Index("t:Prefab", "Assets/JMO Assets", "Assets/UnityTechnologies");
        Dictionary<string, string> clips = Index("t:AudioClip", "Assets/Art/Audio");

        profile = ScriptableObject.CreateInstance<DestructionProfile>();
        profile.smallExplosion = Effect(effects, "WFX_Explosion Small");
        profile.mediumExplosion = Effect(effects, "WFX_Explosion");
        profile.breakupExplosions = Effects(effects, "WFX_ExplosiveSmoke Big Alt", "WFX_Explosion StarSmoke");
        profile.hullFires = Effects(effects, "WFX_Fire Natural Broad (Black Smoke)", "WFX_Fire SmallFlame (Black Smoke)", "WFX_GazFire (Black Smoke)");
        profile.breachVent = Effect(effects, "WFX_GazFireBig (Black Smoke)");
        profile.smallBlastClips = Clips(clips, "medium-explosion-40472");
        profile.largeBlastClips = Clips(clips, "bomb-explosion-type-04-265508");
        profile.interiorMaterial = EnsureInterior();

        AssetDatabase.CreateAsset(profile, ProfilePath);
        AssetDatabase.SaveAssets();
        return profile;
    }

    private static Material EnsureInterior()
    {
        Texture2D albedo = EnsureTexture(AlbedoPath, false);
        Texture2D emission = EnsureTexture(EmissionPath, true);

        Material material = AssetDatabase.LoadAssetAtPath<Material>(InteriorPath);
        if (material == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            material = new Material(shader);
            material.name = "WreckInterior";
            AssetDatabase.CreateAsset(material, InteriorPath);
        }

        material.SetTexture("_BaseMap", albedo);
        material.SetTexture("_MainTex", albedo);
        material.SetColor("_BaseColor", Color.white);
        material.SetColor("_Color", Color.white);
        material.SetFloat("_Smoothness", 0.28f);
        material.SetFloat("_Metallic", 0.55f);
        material.SetTexture("_EmissionMap", emission);
        material.SetColor("_EmissionColor", new Color(1f, 0.45f, 0.15f) * 1.6f);
        material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        BaseShaderGUI.SetMaterialKeywords(material);
        material.EnableKeyword("_EMISSION");
        EditorUtility.SetDirty(material);
        return material;
    }

    private static Texture2D EnsureTexture(string path, bool emission)
    {
        if (!File.Exists(path))
        {
            Texture2D generated = InteriorTexture.Generate(512, emission);
            File.WriteAllBytes(path, generated.EncodeToPNG());
            Object.DestroyImmediate(generated);
            AssetDatabase.ImportAsset(path);

            TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.sRGBTexture = true;
            importer.mipmapEnabled = true;
            importer.anisoLevel = 4;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    private static Dictionary<string, string> Index(string filter, params string[] folders)
    {
        Dictionary<string, string> index = new Dictionary<string, string>();
        string[] valid = folders.Where(AssetDatabase.IsValidFolder).ToArray();
        if (valid.Length == 0) return index;

        foreach (string guid in AssetDatabase.FindAssets(filter, valid))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            string key = Path.GetFileNameWithoutExtension(path);
            if (!index.ContainsKey(key)) index[key] = path;
        }
        return index;
    }

    private static ParticleSystem Effect(Dictionary<string, string> index, string name)
    {
        if (!index.TryGetValue(name, out string path))
        {
            Debug.LogWarning($"Destruction setup: effect '{name}' not found.");
            return null;
        }

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        ParticleSystem system = prefab != null ? prefab.GetComponent<ParticleSystem>() : null;
        if (system == null) Debug.LogWarning($"Destruction setup: '{name}' has no ParticleSystem on its root.");
        return system;
    }

    private static ParticleSystem[] Effects(Dictionary<string, string> index, params string[] names)
    {
        return names.Select(name => Effect(index, name)).Where(system => system != null).ToArray();
    }

    private static AudioClip[] Clips(Dictionary<string, string> index, params string[] names)
    {
        List<AudioClip> clips = new List<AudioClip>();
        foreach (string name in names)
        {
            if (index.TryGetValue(name, out string path))
            {
                AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                if (clip != null) clips.Add(clip);
            }
        }
        return clips.ToArray();
    }
}
