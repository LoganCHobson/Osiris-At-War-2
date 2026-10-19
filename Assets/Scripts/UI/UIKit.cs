using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Small builders for the runtime-made menus (pause menu, settings) so they share one look with the
// main menu: dark glass panels, pale text, amber accents.
public static class UIKit
{
    public static readonly Color PanelColor = new Color(0.04f, 0.06f, 0.1f, 0.94f);
    public static readonly Color RowColor = new Color(1f, 1f, 1f, 0.04f);
    public static readonly Color TextColor = new Color(0.86f, 0.9f, 0.96f);
    public static readonly Color MutedColor = new Color(0.86f, 0.9f, 0.96f, 0.45f);
    public static readonly Color Accent = new Color(1f, 0.74f, 0.32f);
    public static readonly Color Dim = new Color(0f, 0f, 0f, 0.6f);

    public static Canvas Canvas(string name, int sortingOrder, Transform parent = null)
    {
        GameObject go = new GameObject(name, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        if (parent != null) go.transform.SetParent(parent, false);
        Canvas canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;
        CanvasScaler scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        EnsureEventSystem();
        return canvas;
    }

    public static void EnsureEventSystem()
    {
        if (EventSystem.current != null || Object.FindFirstObjectByType<EventSystem>() != null) return;
        new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
    }

    public static RectTransform Rect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 position, Vector2 size)
    {
        RectTransform rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        return rect;
    }

    public static RectTransform Fill(string name, Transform parent)
    {
        RectTransform rect = Rect(name, parent, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        return rect;
    }

    public static Image Image(RectTransform rect, Color color, bool raycast = false)
    {
        Image image = rect.gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = raycast;
        return image;
    }

    public static TMP_Text Text(Transform parent, string text, float size, Color color, TextAlignmentOptions alignment = TextAlignmentOptions.MidlineLeft)
    {
        RectTransform rect = Fill("Text", parent);
        TextMeshProUGUI label = rect.gameObject.AddComponent<TextMeshProUGUI>();
        label.text = text;
        label.fontSize = size;
        label.color = color;
        label.alignment = alignment;
        label.raycastTarget = false;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        return label;
    }

    // Menu-style button: pale label that brightens and slides right with an amber bar on hover.
    public static Button MenuButton(Transform parent, string text, float fontSize, Vector2 size, UnityAction onClick, bool enabled = true)
    {
        RectTransform rect = Rect(text, parent, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, size);
        Image(rect, new Color(0f, 0f, 0f, 0f), true);
        rect.gameObject.AddComponent<LayoutElement>().preferredHeight = size.y;

        RectTransform bar = Rect("Accent", rect, new Vector2(0f, 0.2f), new Vector2(0f, 0.8f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(4f, 0f));
        Image(bar, Accent);

        TMP_Text label = Text(rect, text, fontSize, enabled ? TextColor : MutedColor);
        label.rectTransform.offsetMin = new Vector2(24f, 0f);
        label.characterSpacing = 6f;

        Button button = rect.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.interactable = enabled;
        if (onClick != null) button.onClick.AddListener(onClick);

        rect.gameObject.AddComponent<MenuButtonFeel>().Bind(label, bar, enabled, TextColor, Accent);
        return button;
    }

    // A labelled row (label on the left, control area on the right) inside a vertical list.
    public static RectTransform Row(Transform parent, string label, float height = 58f)
    {
        RectTransform row = Rect(label, parent, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, height));
        Image(row, RowColor);
        row.gameObject.AddComponent<LayoutElement>().preferredHeight = height;

        TMP_Text text = Text(row, label, 26f, TextColor);
        text.rectTransform.anchorMax = new Vector2(0.45f, 1f);
        text.rectTransform.offsetMin = new Vector2(24f, 0f);

        RectTransform control = Rect("Control", row, new Vector2(0.45f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        control.offsetMin = new Vector2(0f, 8f);
        control.offsetMax = new Vector2(-24f, -8f);
        return control;
    }

    public static Slider Slider(RectTransform area, float min, float max, float value, UnityAction<float> onChange, System.Func<float, string> format)
    {
        RectTransform track = Rect("Track", area, new Vector2(0f, 0.5f), new Vector2(0.78f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(0f, 6f));
        Image(track, new Color(1f, 1f, 1f, 0.15f), true);

        RectTransform fillArea = Rect("Fill Area", track, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        RectTransform fill = Rect("Fill", fillArea, Vector2.zero, new Vector2(0f, 1f), new Vector2(0f, 0.5f), Vector2.zero, Vector2.zero);
        Image(fill, Accent);

        RectTransform handleArea = Rect("Handle Area", track, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        RectTransform handle = Rect("Handle", handleArea, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(18f, 30f));
        Image handleImage = Image(handle, TextColor, true);

        Slider slider = track.gameObject.AddComponent<Slider>();
        slider.fillRect = fill;
        slider.handleRect = handle;
        slider.targetGraphic = handleImage;
        slider.direction = UnityEngine.UI.Slider.Direction.LeftToRight;
        slider.minValue = min;
        slider.maxValue = max;
        slider.value = value;

        TMP_Text readout = Text(area, format(value), 24f, MutedColor, TextAlignmentOptions.MidlineRight);
        readout.rectTransform.anchorMin = new Vector2(0.8f, 0f);
        slider.onValueChanged.AddListener(v =>
        {
            readout.text = format(v);
            onChange(v);
        });
        return slider;
    }

    // "<  Option  >" picker - simpler and tidier than a dropdown for short lists.
    public static void Selector(RectTransform area, IList<string> options, int index, UnityAction<int> onChange)
    {
        int current = Mathf.Clamp(index, 0, Mathf.Max(0, options.Count - 1));

        TMP_Text value = Text(area, options.Count > 0 ? options[current] : "-", 24f, TextColor, TextAlignmentOptions.Center);
        value.rectTransform.offsetMin = new Vector2(56f, 0f);
        value.rectTransform.offsetMax = new Vector2(-56f, 0f);

        void Step(int direction)
        {
            if (options.Count == 0) return;
            current = (current + direction + options.Count) % options.Count;
            value.text = options[current];
            onChange(current);
        }

        ArrowButton(area, "<", new Vector2(0f, 0.5f), () => Step(-1));
        ArrowButton(area, ">", new Vector2(1f, 0.5f), () => Step(1));
    }

    private static void ArrowButton(RectTransform area, string glyph, Vector2 anchor, UnityAction onClick)
    {
        RectTransform rect = Rect(glyph, area, anchor, anchor, anchor, Vector2.zero, new Vector2(48f, 40f));
        Image background = Image(rect, new Color(1f, 1f, 1f, 0.06f), true);
        TMP_Text label = Text(rect, glyph, 26f, Accent, TextAlignmentOptions.Center);
        Button button = rect.gameObject.AddComponent<Button>();
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1f, 1f, 1f, 2.5f);
        colors.pressedColor = new Color(1f, 1f, 1f, 4f);
        button.colors = colors;
        button.targetGraphic = background;
        button.onClick.AddListener(onClick);
        label.raycastTarget = false;
    }

    public static VerticalLayoutGroup List(RectTransform rect, float spacing)
    {
        VerticalLayoutGroup layout = rect.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.spacing = spacing;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        return layout;
    }
}
