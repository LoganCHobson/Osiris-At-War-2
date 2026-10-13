using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public static class ShieldSetup
{
    private const string Folder = "Assets/Data/Shields";
    private const string MaterialPath = Folder + "/ShieldRipple.mat";
    private const string FillMaterialPath = Folder + "/ShieldBarFill.mat";
    private const string BackgroundMaterialPath = Folder + "/ShieldBarBackground.mat";
    private const string FactionFolder = "Assets/Prefabs/Osiris/Avanti";
    private static readonly Color BarColor = new Color(0.08f, 0.45f, 1f);
    private static readonly Color BackgroundColor = new Color(0.04f, 0.1f, 0.16f, 0.85f);

    [MenuItem("Osiris/Shields/Set Up Avanti Shields")]
    public static void SetUp()
    {
        EnsureLayer("Shield");
        Material material = EnsureMaterial();
        List<string> configured = new List<string>();

        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { FactionFolder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset == null || asset.GetComponent<UnitHealthManager>() == null || asset.GetComponent<Squadron>() != null) continue;

            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                if (Configure(root, material))
                {
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    configured.Add(Path.GetFileNameWithoutExtension(path));
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"Shield setup: configured {configured.Count} prefab(s): {string.Join(", ", configured)}");
    }

    private static bool Configure(GameObject root, Material material)
    {
        bool dirty = false;

        ShieldGenerator shield = root.GetComponent<ShieldGenerator>();
        if (shield == null)
        {
            shield = root.AddComponent<ShieldGenerator>();
            dirty = true;
        }
        if (shield.shieldMaterial != material)
        {
            shield.shieldMaterial = material;
            dirty = true;
        }

        if (shield.shieldSlider == null)
        {
            Slider bar = CreateBar(root.GetComponent<UnitHealthManager>().healthSlider);
            if (bar != null)
            {
                shield.shieldSlider = bar;
                dirty = true;
            }
        }

        if (shield.shieldSlider != null && Style(shield.shieldSlider)) dirty = true;

        if (dirty) EditorUtility.SetDirty(shield);
        return dirty;
    }

    private static Slider CreateBar(Slider health)
    {
        if (health == null) return null;

        Transform existing = health.transform.Find("ShieldBar");
        if (existing != null) return existing.GetComponent<Slider>();

        GameObject copy = Object.Instantiate(health.gameObject, health.transform);
        copy.name = "ShieldBar";
        copy.SetActive(true);

        RectTransform rect = copy.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.sizeDelta = new Vector2(0f, Mathf.Max(1f, health.GetComponent<RectTransform>().rect.height * 0.6f));
        rect.anchoredPosition = new Vector2(0f, 1.5f);
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;

        Slider slider = copy.GetComponent<Slider>();
        slider.interactable = false;
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.value = 1f;

        foreach (Transform child in copy.GetComponentsInChildren<Transform>(true))
        {
            if (child != copy.transform && child.name == "ShieldBar") Object.DestroyImmediate(child.gameObject);
        }

        return slider;
    }

    private static bool Style(Slider slider)
    {
        bool changed = false;

        RectTransform rect = (RectTransform)slider.transform;
        RectTransform parent = rect.parent as RectTransform;
        float height = parent != null ? parent.rect.height : 0.24f;
        Vector2 size = new Vector2(0f, height * 0.75f);
        Vector2 offset = new Vector2(0f, height * 0.25f);
        if (rect.sizeDelta != size || rect.anchoredPosition != offset)
        {
            rect.sizeDelta = size;
            rect.anchoredPosition = offset;
            EditorUtility.SetDirty(rect);
            changed = true;
        }

        foreach (Image image in slider.GetComponentsInChildren<Image>(true))
        {
            bool isFill = slider.fillRect != null && image.transform == slider.fillRect;
            Color color = isFill ? BarColor : BackgroundColor;
            Material material = BarMaterial(image.material, isFill ? FillMaterialPath : BackgroundMaterialPath, color, isFill ? 0.9f : 0.3f);
            if (image.sprite == null && image.color == color && image.material == material) continue;
            image.sprite = null;
            image.color = color;
            image.material = material;
            EditorUtility.SetDirty(image);
            changed = true;
        }
        return changed;
    }

    private static Material BarMaterial(Material source, string path, Color color, float glow)
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        bool created = material == null;
        if (created)
        {
            if (source == null || source == Canvas.GetDefaultCanvasMaterial()) return null;
            material = new Material(source);
            material.name = Path.GetFileNameWithoutExtension(path);
        }
        foreach (string property in new[] { "_BaseColor", "_Color", "_TintColor" })
        {
            if (material.HasProperty(property)) material.SetColor(property, color);
        }
        if (material.HasProperty("_EmissionColor")) material.SetColor("_EmissionColor", color * glow);
        if (created) AssetDatabase.CreateAsset(material, path);
        else EditorUtility.SetDirty(material);
        return material;
    }

    private static Material EnsureMaterial()
    {
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Data", "Shields");

        Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material == null)
        {
            material = new Material(Shader.Find("Osiris/ShieldRipple"));
            material.name = "ShieldRipple";
            AssetDatabase.CreateAsset(material, MaterialPath);
        }

        material.SetColor("_ShieldColor", new Color(0.7f, 1.6f, 3.4f, 1f));
        EditorUtility.SetDirty(material);
        return material;
    }

    private static void EnsureLayer(string layerName)
    {
        SerializedObject tags = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        SerializedProperty layers = tags.FindProperty("layers");

        for (int i = 8; i < layers.arraySize; i++)
        {
            if (layers.GetArrayElementAtIndex(i).stringValue == layerName) return;
        }

        for (int i = 8; i < layers.arraySize; i++)
        {
            SerializedProperty layer = layers.GetArrayElementAtIndex(i);
            if (!string.IsNullOrEmpty(layer.stringValue)) continue;
            layer.stringValue = layerName;
            tags.ApplyModifiedPropertiesWithoutUndo();
            Debug.Log($"Shield setup: added '{layerName}' layer at index {i}.");
            return;
        }
    }
}
