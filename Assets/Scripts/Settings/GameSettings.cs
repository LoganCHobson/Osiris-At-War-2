using System.Collections.Generic;
using UnityEngine;

public enum WindowModeSetting
{
    BorderlessFullscreen = 0,
    Fullscreen = 1,
    Windowed = 2
}

// Player options, saved in PlayerPrefs and applied on startup. The settings menu edits these and calls
// Apply(); anything that cares (music, camera) reads them or listens for Changed.
public static class GameSettings
{
    public static float MasterVolume = 1f;
    public static float MusicVolume = 0.7f;
    public static float EffectsVolume = 1f;

    public static WindowModeSetting WindowMode = WindowModeSetting.BorderlessFullscreen;
    public static int ResolutionWidth;
    public static int ResolutionHeight;
    public static bool VSync = true;
    public static int FrameCap; // 0 = unlimited.
    public static int QualityLevel = -1; // -1 = leave the project default.

    public static bool EdgeScrolling = true;
    public static float CameraSpeed = 1f;
    public static bool ConfineCursor = true;

    public static readonly int[] FrameCapOptions = { 0, 30, 60, 120, 144, 165, 240 };

    public static event System.Action Changed;

    private const string Prefix = "osiris.settings.";
    private static bool loaded;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Startup()
    {
        Load();
        Apply();
    }

    public static void Load()
    {
        MasterVolume = PlayerPrefs.GetFloat(Prefix + "masterVolume", 1f);
        MusicVolume = PlayerPrefs.GetFloat(Prefix + "musicVolume", 0.7f);
        EffectsVolume = PlayerPrefs.GetFloat(Prefix + "effectsVolume", 1f);

        WindowMode = (WindowModeSetting)PlayerPrefs.GetInt(Prefix + "windowMode", (int)WindowModeSetting.BorderlessFullscreen);
        ResolutionWidth = PlayerPrefs.GetInt(Prefix + "resolutionWidth", 0);
        ResolutionHeight = PlayerPrefs.GetInt(Prefix + "resolutionHeight", 0);
        VSync = PlayerPrefs.GetInt(Prefix + "vsync", 1) == 1;
        FrameCap = PlayerPrefs.GetInt(Prefix + "frameCap", 0);
        QualityLevel = PlayerPrefs.GetInt(Prefix + "quality", -1);

        EdgeScrolling = PlayerPrefs.GetInt(Prefix + "edgeScrolling", 1) == 1;
        CameraSpeed = PlayerPrefs.GetFloat(Prefix + "cameraSpeed", 1f);
        ConfineCursor = PlayerPrefs.GetInt(Prefix + "confineCursor", 1) == 1;
        loaded = true;
    }

    public static void Save()
    {
        PlayerPrefs.SetFloat(Prefix + "masterVolume", MasterVolume);
        PlayerPrefs.SetFloat(Prefix + "musicVolume", MusicVolume);
        PlayerPrefs.SetFloat(Prefix + "effectsVolume", EffectsVolume);

        PlayerPrefs.SetInt(Prefix + "windowMode", (int)WindowMode);
        PlayerPrefs.SetInt(Prefix + "resolutionWidth", ResolutionWidth);
        PlayerPrefs.SetInt(Prefix + "resolutionHeight", ResolutionHeight);
        PlayerPrefs.SetInt(Prefix + "vsync", VSync ? 1 : 0);
        PlayerPrefs.SetInt(Prefix + "frameCap", FrameCap);
        PlayerPrefs.SetInt(Prefix + "quality", QualityLevel);

        PlayerPrefs.SetInt(Prefix + "edgeScrolling", EdgeScrolling ? 1 : 0);
        PlayerPrefs.SetFloat(Prefix + "cameraSpeed", CameraSpeed);
        PlayerPrefs.SetInt(Prefix + "confineCursor", ConfineCursor ? 1 : 0);
        PlayerPrefs.Save();
    }

    // Audio, graphics and frame pacing. Display mode/resolution only change in a built game.
    public static void Apply()
    {
        if (!loaded) Load();

        // Effects ride on the listener volume; music sources ignore it and use master * music instead,
        // so the two sliders are independent.
        AudioListener.volume = Mathf.Clamp01(MasterVolume * EffectsVolume);

        if (QualityLevel >= 0 && QualityLevel < QualitySettings.names.Length && QualitySettings.GetQualityLevel() != QualityLevel)
        {
            QualitySettings.SetQualityLevel(QualityLevel, true);
        }

        QualitySettings.vSyncCount = VSync ? 1 : 0;
        Application.targetFrameRate = VSync || FrameCap <= 0 ? -1 : FrameCap;

#if !UNITY_EDITOR
        FullScreenMode mode = WindowMode switch
        {
            WindowModeSetting.Fullscreen => FullScreenMode.ExclusiveFullScreen,
            WindowModeSetting.Windowed => FullScreenMode.Windowed,
            _ => FullScreenMode.FullScreenWindow
        };

        int width = ResolutionWidth > 0 ? ResolutionWidth : Screen.currentResolution.width;
        int height = ResolutionHeight > 0 ? ResolutionHeight : Screen.currentResolution.height;
        if (Screen.width != width || Screen.height != height || Screen.fullScreenMode != mode)
        {
            Screen.SetResolution(width, height, mode);
        }
#endif

        Changed?.Invoke();
    }

    public static float MusicOutput => Mathf.Clamp01(MasterVolume * MusicVolume);

    // Distinct resolutions the display supports, largest first.
    public static List<Vector2Int> AvailableResolutions()
    {
        List<Vector2Int> sizes = new List<Vector2Int>();
        foreach (Resolution resolution in Screen.resolutions)
        {
            Vector2Int size = new Vector2Int(resolution.width, resolution.height);
            if (!sizes.Contains(size)) sizes.Add(size);
        }
        if (sizes.Count == 0) sizes.Add(new Vector2Int(Screen.width, Screen.height));
        sizes.Sort((a, b) => (b.x * b.y).CompareTo(a.x * a.y));
        return sizes;
    }
}
