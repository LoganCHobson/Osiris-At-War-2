using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static class PostProcessingSetup
{
    private const string Folder = "Assets/Settings/PostProcessing";
    private const string BattlePath = Folder + "/BattleLook.asset";
    private const string CinematicPath = Folder + "/CinematicLook.asset";
    private const string ImpactPath = Folder + "/ImpactFlash.asset";
    private const string DirtPath = Folder + "/LensDirt.png";
    private const string ScenePath = "Assets/Scenes/BattleScene.unity";
    private const float LaserGlow = 3f;

    [MenuItem("Osiris/Post Processing/Apply Battle Look")]
    public static void Apply()
    {
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Settings", "PostProcessing");

        Texture2D dirt = EnsureDirt();
        EnhanceLasers();
        VolumeProfile battle = EnsureProfile(BattlePath, profile => BuildBattle(profile, dirt));
        VolumeProfile cinematic = EnsureProfile(CinematicPath, BuildCinematic);
        VolumeProfile impact = EnsureProfile(ImpactPath, BuildImpact);

        if (GraphicsSettings.defaultRenderPipeline is UniversalRenderPipelineAsset pipeline && pipeline.msaaSampleCount < 4)
        {
            pipeline.msaaSampleCount = 4;
            EditorUtility.SetDirty(pipeline);
        }

        UnityEngine.SceneManagement.Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        foreach (Volume volume in Object.FindObjectsByType<Volume>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (volume.name == "Global Volume")
            {
                volume.sharedProfile = battle;
                EditorUtility.SetDirty(volume);
            }
        }

        Volume cinematicVolume = EnsureVolume("CinematicVolume", cinematic, 10f);
        Volume impactVolume = EnsureVolume("ImpactVolume", impact, 20f);
        ImpactFlash flash = impactVolume.GetComponent<ImpactFlash>();
        if (flash == null) flash = impactVolume.gameObject.AddComponent<ImpactFlash>();
        flash.maxWeight = 0.5f;
        EditorUtility.SetDirty(flash);

        CinematicCamera cinematicCamera = Object.FindFirstObjectByType<CinematicCamera>(FindObjectsInactive.Include);
        if (cinematicCamera != null)
        {
            cinematicCamera.cinematicVolume = cinematicVolume;
            EditorUtility.SetDirty(cinematicCamera);
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log($"Post processing applied: battle={BattlePath} cinematic={CinematicPath} impact={ImpactPath} cinematicWired={cinematicCamera != null}");
    }

    private static Volume EnsureVolume(string name, VolumeProfile profile, float priority)
    {
        GameObject existing = GameObject.Find(name);
        GameObject host = existing != null ? existing : new GameObject(name);
        Volume volume = host.GetComponent<Volume>();
        if (volume == null) volume = host.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = priority;
        volume.weight = 0f;
        volume.sharedProfile = profile;
        EditorUtility.SetDirty(volume);
        return volume;
    }

    private static VolumeProfile EnsureProfile(string path, System.Action<VolumeProfile> build)
    {
        VolumeProfile existing = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
        if (existing != null) return existing;

        VolumeProfile profile = ScriptableObject.CreateInstance<VolumeProfile>();
        AssetDatabase.CreateAsset(profile, path);
        build(profile);
        foreach (VolumeComponent component in profile.components)
        {
            component.name = component.GetType().Name;
            component.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
            AssetDatabase.AddObjectToAsset(component, profile);
        }
        EditorUtility.SetDirty(profile);
        AssetDatabase.SaveAssets();
        return profile;
    }

    private static void BuildBattle(VolumeProfile profile, Texture2D dirt)
    {
        Tonemapping tonemapping = profile.Add<Tonemapping>();
        tonemapping.mode.Override(TonemappingMode.ACES);

        Bloom bloom = profile.Add<Bloom>();
        bloom.threshold.Override(0.92f);
        bloom.intensity.Override(1.15f);
        bloom.scatter.Override(0.76f);
        bloom.tint.Override(new Color(1f, 0.93f, 0.85f));
        bloom.highQualityFiltering.Override(true);
        if (dirt != null)
        {
            bloom.dirtTexture.Override(dirt);
            bloom.dirtIntensity.Override(3f);
        }

        ScreenSpaceLensFlare flare = profile.Add<ScreenSpaceLensFlare>();
        flare.intensity.Override(0f);
        flare.tintColor.Override(new Color(0.6f, 0.78f, 1f));
        flare.firstFlareIntensity.Override(0f);
        flare.secondaryFlareIntensity.Override(0f);
        flare.warpedFlareIntensity.Override(0f);
        flare.streaksIntensity.Override(2.6f);
        flare.streaksLength.Override(0.72f);
        flare.streaksOrientation.Override(0f);
        flare.streaksThreshold.Override(0.12f);

        ColorAdjustments adjustments = profile.Add<ColorAdjustments>();
        adjustments.postExposure.Override(0.4f);
        adjustments.contrast.Override(16f);
        adjustments.saturation.Override(20f);

        ColorCurves curves = profile.Add<ColorCurves>();
        curves.hueVsSat.Override(new TextureCurve(new[] { new Keyframe(0f, 0.6f), new Keyframe(0.05f, 0.9f), new Keyframe(0.11f, 0.9f), new Keyframe(0.18f, 0.55f), new Keyframe(0.5f, 0.5f), new Keyframe(0.62f, 0.53f), new Keyframe(0.75f, 0.5f) }, 0.5f, true, new Vector2(0f, 1f)));
        curves.lumVsSat.Override(new TextureCurve(new[] { new Keyframe(0f, 0.5f), new Keyframe(0.55f, 0.55f), new Keyframe(1f, 0.66f) }, 0.5f, false, new Vector2(0f, 1f)));

        WhiteBalance whiteBalance = profile.Add<WhiteBalance>();
        whiteBalance.temperature.Override(-3f);
        whiteBalance.tint.Override(2f);

        ShadowsMidtonesHighlights tones = profile.Add<ShadowsMidtonesHighlights>();
        tones.shadows.Override(new Vector4(0.97f, 0.99f, 1.04f, 0f));
        tones.highlights.Override(new Vector4(1.08f, 0.99f, 0.9f, 0f));

        LiftGammaGain liftGammaGain = profile.Add<LiftGammaGain>();
        liftGammaGain.lift.Override(new Vector4(0.98f, 1f, 1.03f, -0.01f));
        liftGammaGain.gain.Override(new Vector4(1.03f, 1f, 0.97f, 0f));

        SplitToning splitToning = profile.Add<SplitToning>();
        splitToning.shadows.Override(new Color(0.4f, 0.55f, 0.66f));
        splitToning.highlights.Override(new Color(1f, 0.8f, 0.62f));
        splitToning.balance.Override(-10f);

        Vignette vignette = profile.Add<Vignette>();
        vignette.intensity.Override(0.22f);
        vignette.smoothness.Override(0.5f);

        ChromaticAberration chromatic = profile.Add<ChromaticAberration>();
        chromatic.intensity.Override(0.05f);

        FilmGrain grain = profile.Add<FilmGrain>();
        grain.type.Override(FilmGrainLookup.Thin1);
        grain.intensity.Override(0.08f);
        grain.response.Override(0.8f);
    }

    private static void EnhanceLasers()
    {
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab Laser", new[] { "Assets/Prefabs" }))
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
            if (prefab == null || prefab.GetComponent<Laser>() == null) continue;

            foreach (Renderer renderer in prefab.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer is ParticleSystemRenderer) continue;
                foreach (Material material in renderer.sharedMaterials)
                {
                    if (material == null || !material.HasProperty("_EmissionColor") || !material.HasProperty("_BaseColor")) continue;

                    Color baseColor = material.GetColor("_BaseColor");
                    float peak = Mathf.Max(baseColor.r, Mathf.Max(baseColor.g, baseColor.b));
                    Color hue = peak > 0f ? baseColor / peak : Color.white;
                    hue = new Color(hue.r * hue.r * hue.r, hue.g * hue.g * hue.g, hue.b * hue.b * hue.b);
                    material.SetColor("_EmissionColor", hue * LaserGlow);
                    material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                    material.EnableKeyword("_EMISSION");
                    EditorUtility.SetDirty(material);
                }
            }
        }
    }

    private static void BuildCinematic(VolumeProfile profile)
    {
        DepthOfField depth = profile.Add<DepthOfField>();
        depth.mode.Override(DepthOfFieldMode.Gaussian);
        depth.gaussianStart.Override(60f);
        depth.gaussianEnd.Override(200f);
        depth.gaussianMaxRadius.Override(0.55f);
        depth.highQualitySampling.Override(true);

        Vignette vignette = profile.Add<Vignette>();
        vignette.intensity.Override(0.3f);
        vignette.smoothness.Override(0.5f);

        ChromaticAberration chromatic = profile.Add<ChromaticAberration>();
        chromatic.intensity.Override(0.1f);

        FilmGrain grain = profile.Add<FilmGrain>();
        grain.type.Override(FilmGrainLookup.Medium1);
        grain.intensity.Override(0.14f);
        grain.response.Override(0.75f);

        Bloom bloom = profile.Add<Bloom>();
        bloom.intensity.Override(1.5f);
        bloom.scatter.Override(0.78f);
    }

    private static void BuildImpact(VolumeProfile profile)
    {
        ColorAdjustments adjustments = profile.Add<ColorAdjustments>();
        adjustments.postExposure.Override(0.28f);
        adjustments.colorFilter.Override(new Color(1f, 0.9f, 0.78f));

        Bloom bloom = profile.Add<Bloom>();
        bloom.intensity.Override(1.6f);
        bloom.scatter.Override(0.85f);

        ChromaticAberration chromatic = profile.Add<ChromaticAberration>();
        chromatic.intensity.Override(0.3f);
    }

    private static Texture2D EnsureDirt()
    {
        if (!File.Exists(DirtPath))
        {
            Texture2D generated = GenerateDirt(1024);
            File.WriteAllBytes(DirtPath, generated.EncodeToPNG());
            Object.DestroyImmediate(generated);
            AssetDatabase.ImportAsset(DirtPath);
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(DirtPath);
    }

    private static Texture2D GenerateDirt(int size)
    {
        System.Random random = new System.Random(2209);
        float[] field = new float[size * size];

        for (int s = 0; s < 70; s++)
        {
            float cx = (float)random.NextDouble() * size;
            float cy = (float)random.NextDouble() * size;
            float radius = Mathf.Lerp(size * 0.015f, size * 0.09f, Mathf.Pow((float)random.NextDouble(), 2f));
            float strength = Mathf.Lerp(0.15f, 0.6f, (float)random.NextDouble());
            bool ring = random.NextDouble() < 0.3;
            Stamp(field, size, cx, cy, radius, strength, ring);
        }

        for (int s = 0; s < 6; s++)
        {
            float x = (float)random.NextDouble() * size;
            float y = (float)random.NextDouble() * size;
            float angle = (float)random.NextDouble() * Mathf.PI * 2f;
            float length = size * Mathf.Lerp(0.1f, 0.3f, (float)random.NextDouble());
            for (float d = 0f; d < length; d += 3f)
            {
                Stamp(field, size, x + Mathf.Cos(angle) * d, y + Mathf.Sin(angle) * d, size * 0.012f, 0.05f, false);
            }
        }

        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, true);
        Color[] pixels = new Color[size * size];
        for (int i = 0; i < pixels.Length; i++)
        {
            float v = Mathf.Clamp01(field[i]);
            pixels[i] = new Color(v, v * 0.97f, v * 0.92f, 1f);
        }
        texture.SetPixels(pixels);
        texture.Apply();
        return texture;
    }

    private static void Stamp(float[] field, int size, float cx, float cy, float radius, float strength, bool ring)
    {
        int minX = Mathf.Max(0, Mathf.FloorToInt(cx - radius));
        int maxX = Mathf.Min(size - 1, Mathf.CeilToInt(cx + radius));
        int minY = Mathf.Max(0, Mathf.FloorToInt(cy - radius));
        int maxY = Mathf.Min(size - 1, Mathf.CeilToInt(cy + radius));

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) / radius;
                if (d > 1f) continue;
                float value = ring ? Mathf.Exp(-Mathf.Pow((d - 0.85f) * 7f, 2f)) * 0.8f + (1f - d) * 0.2f : (1f - d * d) * (1f - d * d);
                field[y * size + x] += value * strength;
            }
        }
    }

    public static void ApplyAndQuit()
    {
        Apply();
        EditorApplication.Exit(0);
    }
}
