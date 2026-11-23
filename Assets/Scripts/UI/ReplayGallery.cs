using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Main menu > Replays: every saved battle replay, grouped by campaign (named after its latest save) and game
// version, newest first. Watch one, mark it to keep (never pruned), or delete it.
public class ReplayGallery : MonoBehaviour
{
    public static bool IsOpen { get; private set; }

    private System.Action onClose;
    private RectTransform content;
    private TMP_Text summary;

    public static ReplayGallery Open(Transform canvas, System.Action onClose)
    {
        RectTransform root = UIKit.Fill("Replays", canvas);
        ReplayGallery gallery = root.gameObject.AddComponent<ReplayGallery>();
        gallery.onClose = onClose;
        gallery.Build(root);
        IsOpen = true;
        return gallery;
    }

    public void Close()
    {
        IsOpen = false;
        onClose?.Invoke();
        Destroy(gameObject);
    }

    private void OnDestroy() => IsOpen = false;

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape)) Close();
    }

    private void Build(RectTransform root)
    {
        UIKit.Image(root, UIKit.Dim, true);
        RectTransform window = UIKit.Rect("Window", root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1400f, 880f));
        UIKit.Image(window, UIKit.PanelColor, true);

        TMP_Text title = UIKit.Text(window, "<b>BATTLE REPLAYS</b>", 40f, UIKit.Accent);
        Place(title.rectTransform, 40f, -24f, 800f, 56f);
        summary = UIKit.Text(window, "", 20f, UIKit.MutedColor, TextAlignmentOptions.MidlineRight);
        Place(summary.rectTransform, 700f, -30f, 660f, 44f);

        // A scrolling list.
        RectTransform view = UIKit.Rect("View", window, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        view.offsetMin = new Vector2(30f, 100f);
        view.offsetMax = new Vector2(-30f, -96f);
        UIKit.Image(view, new Color(0f, 0f, 0f, 0.2f), true);
        view.gameObject.AddComponent<RectMask2D>();
        content = UIKit.Rect("Content", view, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, Vector2.zero);
        VerticalLayoutGroup layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 6f;
        layout.padding = new RectOffset(12, 12, 12, 12);
        layout.childControlHeight = true;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = false;
        content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        ScrollRect scroll = view.gameObject.AddComponent<ScrollRect>();
        scroll.content = content;
        scroll.horizontal = false;
        scroll.scrollSensitivity = 40f;
        scroll.movementType = ScrollRect.MovementType.Clamped;

        RectTransform footer = UIKit.Rect("Footer", window, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(30f, 24f), new Vector2(240f, 60f));
        UIKit.MenuButton(footer, "BACK", 32f, new Vector2(240f, 60f), Close);
        TMP_Text where = UIKit.Text(window, $"Saved in {ReplayFile.Folder}   -   Settings > Display > Saved Replays sets how many are kept", 16f, UIKit.MutedColor, TextAlignmentOptions.MidlineRight);
        Place(where.rectTransform, 300f, -830f, 1070f, 30f);

        Refresh();
    }

    private void Refresh()
    {
        foreach (Transform child in content) Destroy(child.gameObject);
        List<ReplayHeader> all = ReplayFile.List();
        long total = all.Sum(h => h.bytes);
        summary.text = $"{all.Count} replays, {total / 1073741824f:0.0} GB";
        if (all.Count == 0)
        {
            Row(34f).GetComponentInChildren<Image>().color = Color.clear;
            TMP_Text none = UIKit.Text(Row(60f), "No replays yet. Battles are saved here when they end (Settings > Display > Battle Replays).", 24f, UIKit.MutedColor, TextAlignmentOptions.Center);
            return;
        }

        // Campaigns are named after their latest save.
        Dictionary<string, string> campaignNames = new Dictionary<string, string>();
        foreach (SaveSummary save in SaveSystem.List())
        {
            string id = save.data != null ? save.data.campaignId : null;
            if (!string.IsNullOrEmpty(id) && !campaignNames.ContainsKey(id)) campaignNames[id] = save.data.displayName;
        }

        foreach (var campaign in all.GroupBy(h => h.skirmish ? "skirmish" : h.campaignId ?? "?"))
        {
            ReplayHeader first = campaign.First();
            string name = campaign.Key == "skirmish" ? "SKIRMISHES"
                : campaignNames.TryGetValue(campaign.Key, out string saved) ? $"CAMPAIGN - {saved}"
                : $"CAMPAIGN STARTED {Date(first.campaignStarted)}";
            Heading(name, 30f, UIKit.Accent);
            foreach (var version in campaign.GroupBy(h => h.gameVersion))
            {
                Heading($"Version {version.Key}{(version.Key != Application.version ? "   <size=80%><color=#FF9C5A>(older version - ships may have changed since)</color></size>" : "")}", 22f, UIKit.TextColor);
                foreach (ReplayHeader h in version) Entry(h);
            }
        }
    }

    private RectTransform Row(float height)
    {
        RectTransform row = UIKit.Rect("Row", content, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, height));
        row.gameObject.AddComponent<LayoutElement>().preferredHeight = height;
        UIKit.Image(row, UIKit.RowColor);
        return row;
    }

    private void Heading(string text, float size, Color color)
    {
        RectTransform row = Row(size + 22f);
        row.GetComponent<Image>().color = Color.clear;
        TMP_Text label = UIKit.Text(row, text, size, color);
        label.rectTransform.offsetMin = new Vector2(8f, 0f);
    }

    private void Entry(ReplayHeader h)
    {
        RectTransform row = Row(64f);
        string result = string.IsNullOrEmpty(h.winner) ? "" : h.playerWon ? "  <color=#7DFC7D>VICTORY</color>" : "  <color=#FF6161>DEFEAT</color>";
        TMP_Text text = UIKit.Text(row, $"<b>{h.system}</b>{result}\n<size=75%><color=#FFFFFF88>{h.when:d MMM yyyy  HH:mm}   {h.attacker} vs {h.defender}   {Minutes(h.duration)}   {h.bytes / 1048576f:0} MB</color></size>", 22f, UIKit.TextColor);
        text.rectTransform.offsetMin = new Vector2(20f, 0f);
        text.rectTransform.offsetMax = new Vector2(-420f, 0f);
        text.textWrappingMode = TextWrappingModes.Normal;

        float x = -24f;
        Button(row, ref x, 120f, "DELETE", new Color(1f, 0.4f, 0.4f), () => { ReplayFile.Delete(h); Refresh(); });
        Button(row, ref x, 130f, h.Kept ? "★ KEPT" : "☆ KEEP", h.Kept ? UIKit.Accent : UIKit.TextColor, () => { ReplayFile.SetKept(h, !h.Kept); Refresh(); });
        Button(row, ref x, 130f, "WATCH", UIKit.Accent, () => ReplayLaunch.Watch(h));
    }

    private static void Button(RectTransform row, ref float right, float width, string text, Color color, UnityEngine.Events.UnityAction onClick)
    {
        RectTransform rect = UIKit.Rect(text, row, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(right, 0f), new Vector2(width, 44f));
        Image image = UIKit.Image(rect, new Color(1f, 1f, 1f, 0.06f), true);
        Button button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        ColorBlock colors = button.colors;
        colors.highlightedColor = new Color(1f, 0.85f, 0.6f, 1f);
        button.colors = colors;
        button.onClick.AddListener(onClick);
        UIKit.Text(rect, text, 20f, color, TextAlignmentOptions.Center);
        right -= width + 10f;
    }

    private static string Minutes(float seconds)
    {
        int s = Mathf.RoundToInt(seconds);
        return $"{s / 60}:{s % 60:00}";
    }

    private static string Date(string iso)
    {
        return System.DateTime.TryParse(iso, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out System.DateTime d)
            ? d.ToString("d MMM yyyy").ToUpperInvariant() : "";
    }

    private static void Place(RectTransform rect, float x, float y, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(x, y);
        rect.sizeDelta = new Vector2(width, height);
    }
}
