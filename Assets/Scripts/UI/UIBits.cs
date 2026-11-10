using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

// Small runtime-built widgets in the UIKit style, shared by the production and defense-layout windows.
public static class UIBits
{
    // Set while a full-screen window (production, defense layout) is up, and for the frame one is closed
    // with Escape, so the pause menu and the map don't also react to that Escape.
    public static bool ModalOpen;
    public static int EscapeConsumedFrame = -1;
    public static bool EscapeTaken => ModalOpen || HelpOverlay.IsOpen || Time.frameCount == EscapeConsumedFrame;

    public static readonly Color ButtonIdle = new Color(UIKit.Accent.r, UIKit.Accent.g, UIKit.Accent.b, 0.12f);
    public static readonly Color ButtonOff = new Color(1f, 1f, 1f, 0.04f);
    public static readonly Color Selected = new Color(UIKit.Accent.r, UIKit.Accent.g, UIKit.Accent.b, 0.28f);

    public class PillButton
    {
        public Button button;
        public Image background;
        public TMP_Text label;

        public void Set(string text, bool interactable)
        {
            label.text = text;
            button.interactable = interactable;
            label.color = interactable ? UIKit.Accent : UIKit.MutedColor;
            background.color = interactable ? ButtonIdle : ButtonOff;
        }
    }

    // A flat button with an amber label; sized by a LayoutElement so it also works inside layout groups.
    public static PillButton Button(Transform parent, string text, float fontSize, Vector2 size, UnityAction onClick)
    {
        RectTransform rect = UIKit.Rect(text, parent, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, size);
        Image background = UIKit.Image(rect, ButtonIdle, true);
        LayoutElement layout = rect.gameObject.AddComponent<LayoutElement>();
        layout.preferredWidth = size.x;
        layout.preferredHeight = size.y;

        TMP_Text label = UIKit.Text(rect, text, fontSize, UIKit.Accent, TextAlignmentOptions.Center);
        label.characterSpacing = 3f;

        Button button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = background;
        ColorBlock colors = button.colors;
        colors.highlightedColor = new Color(1.6f, 1.6f, 1.6f, 1.6f);
        colors.pressedColor = new Color(2.2f, 2.2f, 2.2f, 2.2f);
        colors.disabledColor = Color.white;
        button.colors = colors;
        if (onClick != null) button.onClick.AddListener(onClick);
        return new PillButton { button = button, background = background, label = label };
    }

    // "<  value  >" whose options can change while it's open.
    public class Cycler
    {
        public TMP_Text value;
        public List<string> options = new List<string>();
        public int index;
        public UnityAction<int> onChange;

        public void SetOptions(List<string> newOptions, int newIndex)
        {
            options = newOptions;
            index = options.Count == 0 ? 0 : Mathf.Clamp(newIndex, 0, options.Count - 1);
            value.text = options.Count > 0 ? options[index] : "-";
        }

        public void Step(int direction)
        {
            if (options.Count == 0) return;
            index = (index + direction + options.Count) % options.Count;
            value.text = options[index];
            onChange?.Invoke(index);
        }
    }

    public static Cycler CyclerRow(Transform parent, string label, float height, UnityAction<int> onChange)
    {
        RectTransform row = UIKit.Rect(label, parent, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, height));
        UIKit.Image(row, UIKit.RowColor);
        row.gameObject.AddComponent<LayoutElement>().preferredHeight = height;

        TMP_Text title = UIKit.Text(row, label, 15f, UIKit.MutedColor, TextAlignmentOptions.MidlineLeft);
        title.characterSpacing = 4f;
        title.rectTransform.offsetMin = new Vector2(14f, 0f);
        title.rectTransform.anchorMax = new Vector2(0.36f, 1f);

        RectTransform area = UIKit.Rect("Area", row, new Vector2(0.36f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        area.offsetMin = new Vector2(0f, 6f);
        area.offsetMax = new Vector2(-8f, -6f);

        Cycler cycler = new Cycler { onChange = onChange };
        cycler.value = UIKit.Text(area, "-", 18f, UIKit.TextColor, TextAlignmentOptions.Center);
        cycler.value.rectTransform.offsetMin = new Vector2(40f, 0f);
        cycler.value.rectTransform.offsetMax = new Vector2(-40f, 0f);
        cycler.value.enableAutoSizing = true;
        cycler.value.fontSizeMin = 12f;
        cycler.value.fontSizeMax = 18f;

        Arrow(area, "<", new Vector2(0f, 0.5f), () => cycler.Step(-1));
        Arrow(area, ">", new Vector2(1f, 0.5f), () => cycler.Step(1));
        return cycler;
    }

    private static void Arrow(RectTransform area, string glyph, Vector2 anchor, UnityAction onClick)
    {
        RectTransform rect = UIKit.Rect(glyph, area, anchor, anchor, anchor, Vector2.zero, new Vector2(36f, 0f));
        rect.anchorMin = new Vector2(anchor.x, 0f);
        rect.anchorMax = new Vector2(anchor.x, 1f);
        Image background = UIKit.Image(rect, new Color(1f, 1f, 1f, 0.06f), true);
        UIKit.Text(rect, glyph, 20f, UIKit.Accent, TextAlignmentOptions.Center);
        Button button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = background;
        button.onClick.AddListener(onClick);
    }

    // A vertically scrolling list filling `area`. Returns the content to add rows to.
    public static RectTransform ScrollList(RectTransform area, float spacing)
    {
        ScrollRect scroll = area.gameObject.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.scrollSensitivity = 30f;
        scroll.movementType = ScrollRect.MovementType.Clamped;

        RectTransform viewport = UIKit.Fill("Viewport", area);
        UIKit.Image(viewport, new Color(0f, 0f, 0f, 0.001f), true);
        viewport.gameObject.AddComponent<RectMask2D>();

        RectTransform content = UIKit.Rect("Content", viewport, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, Vector2.zero);
        UIKit.List(content, spacing);
        content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scroll.viewport = viewport;
        scroll.content = content;
        return content;
    }

    public static TMP_InputField InputField(Transform parent, string placeholder, float height, UnityAction<string> onEdit)
    {
        RectTransform rect = UIKit.Rect("Input", parent, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, height));
        Image background = UIKit.Image(rect, new Color(1f, 1f, 1f, 0.07f), true);
        rect.gameObject.AddComponent<LayoutElement>().preferredHeight = height;

        RectTransform viewport = UIKit.Fill("Text Area", rect);
        viewport.offsetMin = new Vector2(12f, 4f);
        viewport.offsetMax = new Vector2(-12f, -4f);
        viewport.gameObject.AddComponent<RectMask2D>();

        TMP_Text hint = UIKit.Text(viewport, placeholder, 18f, UIKit.MutedColor, TextAlignmentOptions.MidlineLeft);
        TMP_Text text = UIKit.Text(viewport, "", 18f, UIKit.TextColor, TextAlignmentOptions.MidlineLeft);

        TMP_InputField field = rect.gameObject.AddComponent<TMP_InputField>();
        field.targetGraphic = background;
        field.textViewport = viewport;
        field.textComponent = text;
        field.placeholder = hint;
        field.lineType = TMP_InputField.LineType.SingleLine;
        field.characterLimit = 32;
        if (onEdit != null) field.onEndEdit.AddListener(onEdit);
        return field;
    }

    public static TMP_Text Heading(Transform parent, string text)
    {
        RectTransform rect = UIKit.Rect(text, parent, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 26f));
        rect.gameObject.AddComponent<LayoutElement>().preferredHeight = 26f;
        TMP_Text heading = UIKit.Text(rect, text, 15f, UIKit.MutedColor, TextAlignmentOptions.BottomLeft);
        heading.characterSpacing = 8f;
        return heading;
    }

    // A column inside a window: fixed position/width, vertical list of children.
    public static RectTransform Column(RectTransform window, float left, float width, float top, float bottom, float spacing)
    {
        RectTransform column = UIKit.Rect("Column", window, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), Vector2.zero, Vector2.zero);
        column.offsetMin = new Vector2(left, bottom);
        column.offsetMax = new Vector2(left + width, -top);
        VerticalLayoutGroup list = UIKit.List(column, spacing);
        list.childForceExpandHeight = false;
        return column;
    }

    // A flexible-height slot inside a Column (for scroll lists).
    public static RectTransform Flexible(Transform column, float minHeight)
    {
        RectTransform rect = UIKit.Rect("Flexible", column, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, Vector2.zero);
        LayoutElement layout = rect.gameObject.AddComponent<LayoutElement>();
        layout.minHeight = minHeight;
        layout.flexibleHeight = 1f;
        return rect;
    }

    public static void Clear(Transform content)
    {
        for (int i = content.childCount - 1; i >= 0; i--)
        {
            Object.Destroy(content.GetChild(i).gameObject);
        }
    }
}
