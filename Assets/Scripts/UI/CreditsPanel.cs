using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Credits, including every soundtrack piece with its artist, licence and source (CC BY music has to be
// credited in-game). The music list comes straight from the MusicLibrary, so new tracks appear here too.
public class CreditsPanel : MonoBehaviour
{
    private System.Action onClose;

    public static bool IsOpen { get; private set; }

    public static CreditsPanel Open(Transform canvas, System.Action onClose)
    {
        RectTransform root = UIKit.Fill("Credits", canvas);
        CreditsPanel panel = root.gameObject.AddComponent<CreditsPanel>();
        panel.onClose = onClose;
        panel.Build(root);
        IsOpen = true;
        return panel;
    }

    public void Close()
    {
        IsOpen = false;
        onClose?.Invoke();
        Destroy(gameObject);
    }

    private void OnDestroy()
    {
        IsOpen = false;
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape)) Close();
    }

    private void Build(RectTransform root)
    {
        UIKit.Image(root, UIKit.Dim, true);

        RectTransform window = UIKit.Rect("Window", root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1100f, 820f));
        UIKit.Image(window, UIKit.PanelColor, true);

        RectTransform header = UIKit.Rect("Header", window, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 90f));
        TMP_Text title = UIKit.Text(header, "CREDITS", 44f, UIKit.TextColor);
        title.rectTransform.offsetMin = new Vector2(40f, 0f);
        title.characterSpacing = 14f;
        title.fontStyle = FontStyles.Bold;

        RectTransform rule = UIKit.Rect("Rule", window, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -90f), new Vector2(-80f, 2f));
        UIKit.Image(rule, new Color(UIKit.Accent.r, UIKit.Accent.g, UIKit.Accent.b, 0.5f));

        RectTransform viewport = UIKit.Rect("Viewport", window, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        viewport.offsetMin = new Vector2(40f, 100f);
        viewport.offsetMax = new Vector2(-40f, -110f);
        UIKit.Image(viewport, new Color(0f, 0f, 0f, 0.01f), true);
        viewport.gameObject.AddComponent<RectMask2D>();

        RectTransform content = UIKit.Rect("Content", viewport, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, Vector2.zero);
        content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        TextMeshProUGUI text = content.gameObject.AddComponent<TextMeshProUGUI>();
        text.text = CreditsText();
        text.fontSize = 24f;
        text.color = UIKit.TextColor;
        text.alignment = TextAlignmentOptions.TopLeft;
        text.textWrappingMode = TextWrappingModes.Normal;
        text.lineSpacing = 8f;
        text.raycastTarget = false;

        ScrollRect scroll = viewport.gameObject.AddComponent<ScrollRect>();
        scroll.content = content;
        scroll.viewport = viewport;
        scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 40f;

        RectTransform footer = UIKit.Rect("Footer", window, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(24f, 24f), new Vector2(260f, 60f));
        Button back = UIKit.MenuButton(footer, "BACK", 32f, new Vector2(260f, 60f), Close);
        RectTransform backRect = (RectTransform)back.transform;
        backRect.anchorMin = backRect.anchorMax = backRect.pivot = Vector2.zero;
    }

    private static string CreditsText()
    {
        string accent = "#" + ColorUtility.ToHtmlStringRGB(UIKit.Accent);
        string muted = "#" + ColorUtility.ToHtmlStringRGB(UIKit.MutedColor);

        System.Text.StringBuilder text = new System.Text.StringBuilder();
        text.AppendLine($"<size=130%><b>{Application.productName}</b></size>");
        text.AppendLine($"<color={muted}>Created by Logan Hobson</color>");
        text.AppendLine();
        text.AppendLine($"<color={accent}><b>MUSIC</b></color>");

        MusicLibrary library = Resources.Load<MusicLibrary>("Music/MusicLibrary");
        if (library != null)
        {
            foreach (MusicLibrary.Track track in library.AllTracks())
            {
                text.AppendLine($"<b>\"{track.title}\"</b> by {track.artist}");
                text.AppendLine($"<color={muted}>{track.license}  ·  {track.source}</color>");
                text.AppendLine();
            }
        }

        text.AppendLine($"<color={accent}><b>SOUND EFFECTS</b></color>");
        text.AppendLine("Kenney  ·  Sci-Fi Sounds and Impact Sounds");
        text.AppendLine($"<color={muted}>CC0  ·  kenney.nl</color>");
        text.AppendLine();
        text.AppendLine("dimapain  ·  Avanti laser cannon");
        text.AppendLine($"<color={muted}>CC0  ·  freesound.org</color>");
        text.AppendLine();

        text.AppendLine($"<color={accent}><b>BUILT WITH</b></color>");
        text.AppendLine("Unity");
        return text.ToString();
    }
}
