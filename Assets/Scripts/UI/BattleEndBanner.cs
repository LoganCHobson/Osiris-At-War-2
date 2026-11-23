using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class BattleEndBanner : MonoBehaviour
{
    public static BattleEndBanner Instance { get; private set; }

    public GameObject banner;
    public TMP_Text titleText;
    public TMP_Text subtitleText;
    public Color victoryColor = new Color(0.49f, 0.99f, 0.49f);
    public Color defeatColor = new Color(1f, 0.38f, 0.38f);

    // The battle's replay, offered under the banner (button or R).
    public bool ReplayRequested { get; private set; }
    private Canvas replayOffer;

    private void Awake()
    {
        Instance = this;
        banner.SetActive(false);
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    public void Show(bool playerWon, string subtitle = null, bool offerReplay = false)
    {
        titleText.text = playerWon ? "VICTORY" : "DEFEAT";
        titleText.color = playerWon ? victoryColor : defeatColor;
        subtitleText.text = string.IsNullOrEmpty(subtitle) ? "Returning to the galactic map..." : $"{subtitle}  -  returning to the galactic map...";
        banner.SetActive(true);
        if (offerReplay) OfferReplay();
    }

    public void Hide()
    {
        banner.SetActive(false);
        if (replayOffer != null) Destroy(replayOffer.gameObject);
    }

    private void OfferReplay()
    {
        if (replayOffer != null) return;
        replayOffer = UIKit.Canvas("ReplayOffer", 950);
        RectTransform box = UIKit.Rect("Watch Replay", replayOffer.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -170f), new Vector2(380f, 64f));
        Image image = UIKit.Image(box, UIKit.PanelColor, true);
        Button button = box.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        ColorBlock colors = button.colors;
        colors.highlightedColor = new Color(1f, 0.85f, 0.6f, 1f);
        button.colors = colors;
        button.onClick.AddListener(() => ReplayRequested = true);
        UIKit.Text(box, "<b>WATCH REPLAY</b>  <size=70%>(R)</size>", 28f, UIKit.Accent, TextAlignmentOptions.Center);
    }

    private void Update()
    {
        if (replayOffer != null && Input.GetKeyDown(KeyCode.R)) ReplayRequested = true;
    }
}
