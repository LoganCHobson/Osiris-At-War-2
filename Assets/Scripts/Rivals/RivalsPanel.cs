using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The rivals dossier on the galactic map: each warlord's admiral, their record against you, what they
// have noticed about how you fight, and the last thing they said. Opened with the RIVALS button under
// the credits readout, or F2. GalacticMapManager adds it to the map.
public class RivalsPanel : MonoBehaviour
{
    public static bool IsOpen => instance != null && instance.window != null && instance.window.gameObject.activeSelf;
    private static RivalsPanel instance;

    private Canvas canvas;
    private RectTransform window;

    private void Awake() => instance = this;

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    private void Start()
    {
        UIKit.EnsureEventSystem();
        canvas = UIKit.Canvas("RivalsCanvas", 60, transform);

        // The button, just under the credits readout (ResourceBar sits at -96 from the top-right).
        RectTransform holder = UIKit.Rect("RivalsButton", canvas.transform, Vector2.one, Vector2.one, Vector2.one, new Vector2(-20f, -160f), new Vector2(170f, 44f));
        UIKit.Image(holder, new Color(0.05f, 0.07f, 0.1f, 0.88f), true);
        Button button = UIKit.MenuButton(holder, "RIVALS", 22f, new Vector2(170f, 44f), Toggle);
        RectTransform rect = (RectTransform)button.transform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = Vector2.zero;
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.F2)) Toggle();
        if (IsOpen && Input.GetKeyDown(KeyCode.Escape)) Close();
    }

    private void Toggle()
    {
        if (IsOpen) Close();
        else Open();
    }

    private void Close()
    {
        if (window != null) Destroy(window.parent.gameObject);
        window = null;
    }

    private void Open()
    {
        RectTransform shade = UIKit.Fill("Rivals", canvas.transform);
        UIKit.Image(shade, UIKit.Dim, true);
        window = UIKit.Rect("Window", shade, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1320f, 980f));
        UIKit.Image(window, UIKit.PanelColor, true);

        RectTransform header = UIKit.Rect("Header", window, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 90f));
        TMP_Text heading = UIKit.Text(header, "RIVALS", 40f, UIKit.TextColor, TextAlignmentOptions.Center);
        heading.characterSpacing = 12f;
        heading.fontStyle = FontStyles.Bold;

        RectTransform list = UIKit.Rect("List", window, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        list.offsetMin = new Vector2(40f, 110f);
        list.offsetMax = new Vector2(-40f, -96f);
        UIKit.List(list, 8f);

        foreach (RivalAdmiral rival in RivalAdmiral.All)
        {
            Row(list, rival);
        }

        RectTransform footer = UIKit.Rect("Footer", window, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 20f), new Vector2(-80f, 70f));
        string note = Difficulty.Current == DifficultyLevel.GrandAdmiral
            ? "At Grand Admiral the warlords' fleets are commanded by the learning AI, which adapts its tactics to how you fight."
            : "Each admiral remembers your battles against them. At Grand Admiral difficulty they also adapt their tactics to you.";
        TMP_Text footnote = UIKit.Text(footer, note, 20f, UIKit.MutedColor);
        footnote.rectTransform.offsetMax = new Vector2(-220f, 0f);
        footnote.textWrappingMode = TextWrappingModes.Normal;
        Button close = UIKit.MenuButton(footer, "CLOSE", 28f, new Vector2(200f, 56f), Close);
        RectTransform closeRect = (RectTransform)close.transform;
        closeRect.anchorMin = closeRect.anchorMax = closeRect.pivot = new Vector2(1f, 0.5f);
        closeRect.anchoredPosition = Vector2.zero;
    }

    private static void Row(RectTransform list, RivalAdmiral rival)
    {
        Rivals.Dossier d = Rivals.Of(rival);
        Faction faction = SaveRegistry.Factions.FirstOrDefault(f => f != null && f.factionName == rival.faction);
        string colour = faction != null ? ColorUtility.ToHtmlStringRGB(Color.Lerp(faction.color, Color.white, 0.25f)) : "FFFFFF";
        string muted = ColorUtility.ToHtmlStringRGBA(UIKit.MutedColor);

        RectTransform row = UIKit.Rect(rival.name, list, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 120f));
        UIKit.Image(row, UIKit.RowColor);
        row.gameObject.AddComponent<LayoutElement>().preferredHeight = 120f;
        RectTransform stripe = UIKit.Rect("Stripe", row, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(5f, 0f));
        UIKit.Image(stripe, faction != null ? faction.color : UIKit.Accent);

        // Left: who they are.
        TMP_Text who = UIKit.Text(row, $"<b>{rival.FullName}</b>  <size=75%><color=#{colour}>{rival.faction.ToUpperInvariant()}</color></size>\n"
                                       + $"<size=70%><color=#{muted}>{rival.epithet}</color></size>", 26f, UIKit.TextColor, TextAlignmentOptions.TopLeft);
        who.rectTransform.anchorMax = new Vector2(0.52f, 1f);
        who.rectTransform.offsetMin = new Vector2(24f, 10f);
        who.rectTransform.offsetMax = new Vector2(0f, -12f);
        who.textWrappingMode = TextWrappingModes.Normal;

        // Right: the record, what they've noticed, their last word.
        string body;
        if (d.battles == 0)
        {
            body = $"<color=#{muted}>Has not faced you yet.</color>";
        }
        else
        {
            var noticed = Rivals.Tendencies(d);
            string seen = noticed.Count == 0 ? "Still studying you." : "Has noticed: " + string.Join("; ", noticed.Take(3).Select(t => Rivals.Describe(t).ToLowerInvariant()));
            string last = string.IsNullOrEmpty(d.lastLine) ? "" : $"\n<i><color=#{muted}>\"{d.lastLine}\"</color></i>";
            body = $"<b>{Rivals.Record(d)}</b>\n<size=85%>{seen}</size>{last}";
        }
        TMP_Text info = UIKit.Text(row, body, 21f, UIKit.TextColor, TextAlignmentOptions.TopLeft);
        info.rectTransform.anchorMin = new Vector2(0.54f, 0f);
        info.rectTransform.offsetMin = new Vector2(0f, 10f);
        info.rectTransform.offsetMax = new Vector2(-20f, -12f);
        info.textWrappingMode = TextWrappingModes.Normal;
    }
}
