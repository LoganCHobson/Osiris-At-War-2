using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.Video;

// The main menu: a looping battle clip behind the title and buttons. The whole menu is built at
// runtime - drop the clip into the slot below (or Resources/Menu/MenuBackdrop). Music comes from
// MusicManager's menu playlist.
public class MainMenuController : MonoBehaviour
{
    [Header("Media")]
    public VideoClip backdrop;

    [Header("Look")]
    public string title = "OSIRIS AT WAR";
    [Tooltip("Optional line under the title (leave empty for none).")]
    public string subtitle = "";
    public Color accent = new Color(1f, 0.74f, 0.32f);
    public Color textColor = new Color(0.86f, 0.9f, 0.96f);
    public Color disabledColor = new Color(0.86f, 0.9f, 0.96f, 0.3f);

    [Header("Timing")]
    public float fadeInTime = 1.6f;
    public float loopFadeTime = 0.6f;
    public float exitFadeTime = 1.2f;
    public string campaignScene = "GalacticMap";

    private Canvas menuCanvas;
    private RectTransform picker;
    private VideoPlayer player;
    private RenderTexture videoTexture;
    private Image fade;
    private CanvasGroup menuGroup;
    private bool leaving;
    private float screenFade = 1f;

    private void Start()
    {
        if (backdrop == null) backdrop = Resources.Load<VideoClip>("Menu/MenuBackdrop");

        EnsureEventSystem();
        BuildInterface();
        StartVideo();
    }

    // ---------------- Interface ----------------

    private void BuildInterface()
    {
        Canvas canvas = new GameObject("MenuCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster)).GetComponent<Canvas>();
        menuCanvas = canvas;
        canvas.transform.SetParent(transform, false);
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        // The battle clip, cropped to fill any screen shape.
        RawImage video = Stretch(new GameObject("Backdrop", typeof(RawImage)), canvas.transform).GetComponent<RawImage>();
        video.color = Color.black;
        video.raycastTarget = false;
        AspectRatioFitter fitter = video.gameObject.AddComponent<AspectRatioFitter>();
        fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
        fitter.aspectRatio = 16f / 9f;
        videoTexture = new RenderTexture(1920, 1080, 0) { name = "MenuBackdrop" };
        video.texture = videoTexture;

        // Darken the left side and bottom so the title and buttons read over the action.
        Gradient(canvas.transform, "SideShade", true, 0.82f, 0.5f);
        Gradient(canvas.transform, "BottomShade", false, 0.6f, 0.35f);

        GameObject menu = Stretch(new GameObject("Menu", typeof(CanvasGroup)), canvas.transform);
        menuGroup = menu.GetComponent<CanvasGroup>();

        TMP_Text titleText = Label(menu.transform, "Title", title, 112f, textColor, new Vector2(140f, -170f));
        titleText.fontStyle = FontStyles.Bold;
        titleText.characterSpacing = 18f;
        bool hasSubtitle = !string.IsNullOrEmpty(subtitle);
        float shift = hasSubtitle ? 0f : 90f; // Close the gap when there's no subtitle.
        if (hasSubtitle)
        {
            TMP_Text subtitleText = Label(menu.transform, "Subtitle", subtitle, 64f, accent, new Vector2(146f, -300f));
            subtitleText.characterSpacing = 30f;
        }

        RectTransform rule = new GameObject("Rule", typeof(Image)).GetComponent<RectTransform>();
        rule.SetParent(menu.transform, false);
        Anchor(rule, new Vector2(150f, -390f + shift), new Vector2(420f, 2f));
        rule.GetComponent<Image>().color = new Color(accent.r, accent.g, accent.b, 0.6f);

        float y = -470f + shift;
        AddButton(menu.transform, "NEW CAMPAIGN", ref y, true, ChooseDifficulty);
        SaveSummary latest = SaveSystem.Latest();
        AddButton(menu.transform, "CONTINUE", ref y, latest != null, () => ContinueFrom(latest));
        AddButton(menu.transform, "LOAD GAME", ref y, SaveSystem.List().Count > 0, OpenLoad);
        AddButton(menu.transform, "OPTIONS", ref y, true, OpenOptions);
        AddButton(menu.transform, "CREDITS", ref y, false, null);
        y -= 24f;
        AddButton(menu.transform, "QUIT", ref y, true, Quit);

        TMP_Text version = Label(menu.transform, "Version", $"v{Application.version}", 22f, disabledColor, new Vector2(150f, 0f));
        RectTransform versionRect = version.rectTransform;
        versionRect.anchorMin = versionRect.anchorMax = versionRect.pivot = new Vector2(0f, 0f);
        versionRect.anchoredPosition = new Vector2(150f, 40f);

        fade = Stretch(new GameObject("Fade", typeof(Image)), canvas.transform).GetComponent<Image>();
        fade.color = Color.black;
        fade.raycastTarget = false;
    }

    private void AddButton(Transform parent, string text, ref float y, bool enabled, UnityEngine.Events.UnityAction onClick)
    {
        GameObject root = new GameObject(text, typeof(RectTransform), typeof(Image), typeof(Button), typeof(MenuButtonFeel));
        root.transform.SetParent(parent, false);
        Anchor((RectTransform)root.transform, new Vector2(150f, y), new Vector2(520f, 64f));

        Image hitArea = root.GetComponent<Image>();
        hitArea.color = new Color(0f, 0f, 0f, 0f); // Invisible, just catches the mouse.

        RectTransform bar = new GameObject("Accent", typeof(Image)).GetComponent<RectTransform>();
        bar.SetParent(root.transform, false);
        bar.anchorMin = new Vector2(0f, 0.2f);
        bar.anchorMax = new Vector2(0f, 0.8f);
        bar.pivot = new Vector2(0f, 0.5f);
        bar.sizeDelta = new Vector2(4f, 0f);
        bar.anchoredPosition = Vector2.zero;
        bar.GetComponent<Image>().color = accent;

        TMP_Text label = Label(root.transform, "Label", text, 40f, enabled ? textColor : disabledColor, Vector2.zero);
        RectTransform labelRect = label.rectTransform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(24f, 0f);
        labelRect.offsetMax = Vector2.zero;
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.characterSpacing = 8f;

        if (!enabled)
        {
            TMP_Text soon = Label(root.transform, "Soon", "SOON", 18f, new Color(accent.r, accent.g, accent.b, 0.55f), Vector2.zero);
            RectTransform soonRect = soon.rectTransform;
            soonRect.anchorMin = soonRect.anchorMax = new Vector2(0f, 0.5f);
            soonRect.pivot = new Vector2(0f, 0.5f);
            soonRect.anchoredPosition = new Vector2(24f + label.GetPreferredValues(text).x + 16f, 0f);
        }

        Button button = root.GetComponent<Button>();
        button.interactable = enabled;
        button.transition = Selectable.Transition.None;
        if (onClick != null) button.onClick.AddListener(onClick);

        root.GetComponent<MenuButtonFeel>().Bind(label, bar, enabled, textColor, accent);
        y -= 74f;
    }

    // ---------------- Media ----------------

    private void StartVideo()
    {
        if (backdrop == null) return; // No clip yet - the menu sits over black.

        player = gameObject.AddComponent<VideoPlayer>();
        player.playOnAwake = false;
        player.isLooping = true;
        player.renderMode = VideoRenderMode.RenderTexture;
        player.targetTexture = videoTexture;
        player.audioOutputMode = VideoAudioOutputMode.None;
        player.clip = backdrop;
        player.prepareCompleted += prepared =>
        {
            prepared.Play();
            RawImage video = GetComponentInChildren<RawImage>();
            if (video != null) video.color = Color.white;
        };
        player.Prepare();
    }

    private void Update()
    {
        float target = leaving ? 1f : LoopFade();
        float speed = leaving ? 1f / exitFadeTime : 1f / (screenFade > target ? fadeInTime : loopFadeTime);
        screenFade = Mathf.MoveTowards(screenFade, target, Time.unscaledDeltaTime * speed);
        if (fade != null) fade.color = new Color(0f, 0f, 0f, screenFade);

        if (menuGroup != null && !leaving && !SettingsPanel.IsOpen && !SaveLoadPanel.IsOpen && picker == null)
        {
            menuGroup.alpha = Mathf.MoveTowards(menuGroup.alpha, 1f, Time.unscaledDeltaTime / fadeInTime);
        }

    }

    // Dips to black for a moment around the clip's loop point so the restart isn't a hard cut.
    private float LoopFade()
    {
        if (player == null || !player.isPlaying || backdrop == null) return backdrop == null ? 0f : 1f;

        double length = backdrop.length;
        double time = player.time;
        if (length - time < loopFadeTime) return 0.85f;
        return 0f;
    }

    // ---------------- Actions ----------------

    private void ContinueFrom(SaveSummary save)
    {
        if (leaving || save == null) return;
        leaving = true;
        menuGroup.interactable = false;
        if (!SaveSystem.Load(save.slot, out string error))
        {
            leaving = false;
            menuGroup.interactable = true;
            Toast.Show(error);
        }
    }

    private void OpenLoad()
    {
        if (leaving || SaveLoadPanel.IsOpen) return;
        menuGroup.alpha = 0f;
        menuGroup.interactable = false;
        SaveLoadPanel.Open(menuCanvas.transform, SaveLoadPanel.Mode.Load, false, () =>
        {
            menuGroup.interactable = true;
        });
    }

    private void OpenOptions()
    {
        if (leaving || SettingsPanel.IsOpen) return;
        menuGroup.alpha = 0f;
        menuGroup.interactable = false;
        SettingsPanel.Open(menuCanvas.transform, () =>
        {
            menuGroup.interactable = true;
        });
    }

    // New Campaign asks how sharp the enemy should be first.
    private void ChooseDifficulty()
    {
        if (leaving || picker != null) return;
        menuGroup.alpha = 0f;
        menuGroup.interactable = false;

        picker = UIKit.Fill("Difficulty", menuCanvas.transform);
        UIKit.Image(picker, UIKit.Dim, true);

        RectTransform window = UIKit.Rect("Window", picker, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(900f, 640f));
        UIKit.Image(window, UIKit.PanelColor, true);

        RectTransform header = UIKit.Rect("Header", window, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 100f));
        TMP_Text heading = UIKit.Text(header, "CHOOSE DIFFICULTY", 42f, UIKit.TextColor, TextAlignmentOptions.Center);
        heading.characterSpacing = 12f;
        heading.fontStyle = FontStyles.Bold;

        RectTransform list = UIKit.Rect("Levels", window, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        list.offsetMin = new Vector2(60f, 110f);
        list.offsetMax = new Vector2(-60f, -120f);
        UIKit.List(list, 18f);

        foreach (DifficultyLevel level in new[] { DifficultyLevel.Cadet, DifficultyLevel.Captain, DifficultyLevel.Admiral })
        {
            DifficultyLevel chosen = level;
            UIKit.MenuButton(list, level.ToString().ToUpperInvariant() + (level == Difficulty.Current ? "   <size=60%><color=#FFBC52>LAST USED</color></size>" : ""), 38f, new Vector2(780f, 58f), () =>
            {
                Difficulty.Current = chosen;
                StartCampaign();
            });

            RectTransform note = UIKit.Rect("Note", list, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(780f, 30f));
            note.gameObject.AddComponent<LayoutElement>().preferredHeight = 30f;
            TMP_Text description = UIKit.Text(note, Difficulty.Describe(level), 22f, UIKit.MutedColor);
            description.rectTransform.offsetMin = new Vector2(24f, 0f);
        }

        RectTransform footer = UIKit.Rect("Footer", window, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(30f, 24f), new Vector2(240f, 60f));
        Button back = UIKit.MenuButton(footer, "BACK", 32f, new Vector2(240f, 60f), CloseDifficulty);
        RectTransform backRect = (RectTransform)back.transform;
        backRect.anchorMin = backRect.anchorMax = backRect.pivot = Vector2.zero;
    }

    private void CloseDifficulty()
    {
        if (picker != null) Destroy(picker.gameObject);
        picker = null;
        menuGroup.interactable = true;
    }

    private void StartCampaign()
    {
        SaveSystem.ClearCampaign(); // A new campaign never inherits anything from a previous one.
        if (picker != null) picker.gameObject.SetActive(false);
        if (leaving) return;
        StartCoroutine(LeaveTo(campaignScene));
    }

    private void Quit()
    {
        if (leaving) return;
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private IEnumerator LeaveTo(string scene)
    {
        leaving = true;
        menuGroup.interactable = false;
        AsyncOperation load = SceneManager.LoadSceneAsync(scene);
        load.allowSceneActivation = false;

        for (float t = 0f; t < exitFadeTime; t += Time.unscaledDeltaTime)
        {
            menuGroup.alpha = 1f - t / exitFadeTime;
            yield return null;
        }

        load.allowSceneActivation = true;
    }

    private void OnDestroy()
    {
        if (videoTexture != null) videoTexture.Release();
    }

    // ---------------- Helpers ----------------

    private static void EnsureEventSystem()
    {
        if (EventSystem.current != null) return;
        new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
    }

    private static GameObject Stretch(GameObject go, Transform parent)
    {
        RectTransform rect = go.GetComponent<RectTransform>();
        if (rect == null) rect = go.AddComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        return go;
    }

    private static void Anchor(RectTransform rect, Vector2 position, Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    private static TMP_Text Label(Transform parent, string name, string text, float size, Color color, Vector2 position)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        TextMeshProUGUI label = go.AddComponent<TextMeshProUGUI>();
        label.text = text;
        label.fontSize = size;
        label.color = color;
        label.raycastTarget = false;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        Anchor(label.rectTransform, position, new Vector2(1200f, size * 1.4f));
        return label;
    }

    // A soft black gradient across one side of the screen (horizontal = from the left edge).
    private static void Gradient(Transform parent, string name, bool horizontal, float strength, float coverage)
    {
        const int size = 64;
        Texture2D texture = new Texture2D(horizontal ? size : 1, horizontal ? 1 : size, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            name = name
        };
        for (int i = 0; i < size; i++)
        {
            float t = i / (size - 1f);
            Color color = new Color(0f, 0f, 0f, strength * Mathf.Pow(1f - t, 1.6f));
            if (horizontal) texture.SetPixel(i, 0, color);
            else texture.SetPixel(0, i, color);
        }
        texture.Apply();

        RawImage shade = new GameObject(name, typeof(RawImage)).GetComponent<RawImage>();
        shade.transform.SetParent(parent, false);
        shade.texture = texture;
        shade.raycastTarget = false;
        RectTransform rect = shade.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = horizontal ? new Vector2(coverage, 1f) : new Vector2(1f, coverage);
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }
}
