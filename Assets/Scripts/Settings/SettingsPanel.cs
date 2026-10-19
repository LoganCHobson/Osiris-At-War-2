using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The settings window, shared by the main menu and the in-game pause menu. Every change applies
// immediately and is saved.
public class SettingsPanel : MonoBehaviour
{
    private enum Tab { Audio, Display, Controls }

    private System.Action onClose;
    private RectTransform content;
    private readonly Dictionary<Tab, MenuTabFeel> tabs = new Dictionary<Tab, MenuTabFeel>();
    private Tab current;

    public static bool IsOpen { get; private set; }

    public static SettingsPanel Open(Transform canvas, System.Action onClose)
    {
        RectTransform root = UIKit.Fill("Settings", canvas);
        SettingsPanel panel = root.gameObject.AddComponent<SettingsPanel>();
        panel.onClose = onClose;
        panel.Build(root);
        IsOpen = true;
        return panel;
    }

    public void Close()
    {
        GameSettings.Save();
        IsOpen = false;
        onClose?.Invoke();
        Destroy(gameObject);
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape)) Close();
    }

    private void OnDestroy()
    {
        IsOpen = false;
    }

    private void Build(RectTransform root)
    {
        UIKit.Image(root, UIKit.Dim, true); // Blocks clicks to whatever is behind.

        RectTransform window = UIKit.Rect("Window", root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(980f, 700f));
        UIKit.Image(window, UIKit.PanelColor, true);

        RectTransform header = UIKit.Rect("Header", window, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 90f));
        TMP_Text title = UIKit.Text(header, "SETTINGS", 44f, UIKit.TextColor);
        title.rectTransform.offsetMin = new Vector2(40f, 0f);
        title.characterSpacing = 14f;
        title.fontStyle = FontStyles.Bold;

        RectTransform accent = UIKit.Rect("Rule", window, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -90f), new Vector2(-80f, 2f));
        UIKit.Image(accent, new Color(UIKit.Accent.r, UIKit.Accent.g, UIKit.Accent.b, 0.5f));

        RectTransform tabRow = UIKit.Rect("Tabs", window, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -104f), new Vector2(-80f, 50f));
        HorizontalLayoutGroup tabLayout = tabRow.gameObject.AddComponent<HorizontalLayoutGroup>();
        tabLayout.spacing = 12f;
        tabLayout.childControlWidth = true;
        tabLayout.childForceExpandWidth = false;
        AddTab(tabRow, Tab.Audio, "AUDIO");
        AddTab(tabRow, Tab.Display, "DISPLAY");
        AddTab(tabRow, Tab.Controls, "CONTROLS");

        content = UIKit.Rect("Content", window, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        content.offsetMin = new Vector2(40f, 100f);
        content.offsetMax = new Vector2(-40f, -170f);
        UIKit.List(content, 10f).childAlignment = TextAnchor.UpperCenter;

        RectTransform footer = UIKit.Rect("Footer", window, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(24f, 24f), new Vector2(260f, 60f));
        Button back = UIKit.MenuButton(footer, "BACK", 32f, new Vector2(260f, 60f), Close);
        ((RectTransform)back.transform).anchorMin = ((RectTransform)back.transform).anchorMax = Vector2.zero;
        ((RectTransform)back.transform).pivot = Vector2.zero;

        Show(Tab.Audio);
    }

    private void AddTab(RectTransform row, Tab tab, string label)
    {
        RectTransform rect = UIKit.Rect(label, row, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(200f, 50f));
        rect.gameObject.AddComponent<LayoutElement>().preferredWidth = 200f;
        Image background = UIKit.Image(rect, new Color(1f, 1f, 1f, 0.05f), true);
        TMP_Text text = UIKit.Text(rect, label, 24f, UIKit.MutedColor, TextAlignmentOptions.Center);
        text.characterSpacing = 8f;
        Button button = rect.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.onClick.AddListener(() => Show(tab));
        tabs[tab] = rect.gameObject.AddComponent<MenuTabFeel>();
        tabs[tab].Bind(background, text);
    }

    private void Show(Tab tab)
    {
        current = tab;
        foreach (KeyValuePair<Tab, MenuTabFeel> entry in tabs) entry.Value.SetActive(entry.Key == tab);

        for (int i = content.childCount - 1; i >= 0; i--) Destroy(content.GetChild(i).gameObject);

        switch (tab)
        {
            case Tab.Audio: BuildAudio(); break;
            case Tab.Display: BuildDisplay(); break;
            case Tab.Controls: BuildControls(); break;
        }
    }

    private void BuildAudio()
    {
        VolumeRow("Master Volume", GameSettings.MasterVolume, v => GameSettings.MasterVolume = v);
        VolumeRow("Music", GameSettings.MusicVolume, v => GameSettings.MusicVolume = v);
        VolumeRow("Effects", GameSettings.EffectsVolume, v => GameSettings.EffectsVolume = v);
    }

    private void VolumeRow(string label, float value, System.Action<float> set)
    {
        UIKit.Slider(UIKit.Row(content, label), 0f, 1f, value, v =>
        {
            set(v);
            GameSettings.Apply();
        }, v => $"{Mathf.RoundToInt(v * 100f)}%");
    }

    private void BuildDisplay()
    {
        string[] modes = { "Borderless Fullscreen", "Fullscreen", "Windowed" };
        UIKit.Selector(UIKit.Row(content, "Window Mode"), modes, (int)GameSettings.WindowMode, i =>
        {
            GameSettings.WindowMode = (WindowModeSetting)i;
            GameSettings.Apply();
        });

        List<Vector2Int> sizes = GameSettings.AvailableResolutions();
        List<string> sizeNames = sizes.ConvertAll(s => $"{s.x} x {s.y}");
        int currentSize = sizes.FindIndex(s => s.x == (GameSettings.ResolutionWidth > 0 ? GameSettings.ResolutionWidth : Screen.width)
                                                && s.y == (GameSettings.ResolutionHeight > 0 ? GameSettings.ResolutionHeight : Screen.height));
        UIKit.Selector(UIKit.Row(content, "Resolution"), sizeNames, Mathf.Max(0, currentSize), i =>
        {
            GameSettings.ResolutionWidth = sizes[i].x;
            GameSettings.ResolutionHeight = sizes[i].y;
            GameSettings.Apply();
        });

        UIKit.Selector(UIKit.Row(content, "VSync"), new[] { "Off", "On" }, GameSettings.VSync ? 1 : 0, i =>
        {
            GameSettings.VSync = i == 1;
            GameSettings.Apply();
        });

        List<string> caps = new List<string>();
        foreach (int cap in GameSettings.FrameCapOptions) caps.Add(cap <= 0 ? "Unlimited" : $"{cap} FPS");
        int capIndex = System.Array.IndexOf(GameSettings.FrameCapOptions, GameSettings.FrameCap);
        UIKit.Selector(UIKit.Row(content, "Frame Cap (VSync off)"), caps, Mathf.Max(0, capIndex), i =>
        {
            GameSettings.FrameCap = GameSettings.FrameCapOptions[i];
            GameSettings.Apply();
        });

        string[] qualities = QualitySettings.names;
        int quality = GameSettings.QualityLevel >= 0 ? GameSettings.QualityLevel : QualitySettings.GetQualityLevel();
        UIKit.Selector(UIKit.Row(content, "Graphics Quality"), qualities, quality, i =>
        {
            GameSettings.QualityLevel = i;
            GameSettings.Apply();
        });
    }

    private void BuildControls()
    {
        UIKit.Selector(UIKit.Row(content, "Edge Scrolling"), new[] { "Off", "On" }, GameSettings.EdgeScrolling ? 1 : 0, i =>
        {
            GameSettings.EdgeScrolling = i == 1;
            GameSettings.Apply();
        });

        UIKit.Slider(UIKit.Row(content, "Camera Speed"), 0.25f, 2.5f, GameSettings.CameraSpeed, v =>
        {
            GameSettings.CameraSpeed = v;
            GameSettings.Apply();
        }, v => $"{v:0.0}x");

        UIKit.Selector(UIKit.Row(content, "Keep Cursor In Window"), new[] { "Off", "On" }, GameSettings.ConfineCursor ? 1 : 0, i =>
        {
            GameSettings.ConfineCursor = i == 1;
            GameSettings.Apply();
        });

        RectTransform keys = UIKit.Rect("Hotkeys", content, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 150f));
        keys.gameObject.AddComponent<LayoutElement>().preferredHeight = 150f;
        TMP_Text text = UIKit.Text(keys, "<b>HOTKEYS</b>\n" +
            "Space  pause / play      1 / 2 / 3  game speed      Esc  menu\n" +
            "WASD / arrows  pan (Shift = faster)      Middle mouse  drag camera\n" +
            "F  priority move      G  guard      C  cinematic camera", 22f, UIKit.MutedColor, TextAlignmentOptions.TopLeft);
        text.textWrappingMode = TextWrappingModes.Normal;
        text.lineSpacing = 18f;
        text.rectTransform.offsetMin = new Vector2(24f, 0f);
    }
}
