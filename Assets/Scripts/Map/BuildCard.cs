using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// One ship class in the build menu: icon, name, role and price. Click to queue one (shift-click for five);
// hovering shows the ship's stat sheet. Greyed out with the reason when it can't be built right now.
public class BuildCard : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    private static readonly Color Idle = new Color(1f, 1f, 1f, 0.05f);
    private static readonly Color Hover = new Color(1f, 1f, 1f, 0.11f);
    private static readonly Color Warning = new Color(1f, 0.45f, 0.4f);

    public Ship Ship { get; private set; }

    private PlanetBuildPanel panel;
    private Image background;
    private RectTransform hoverBar;
    private CanvasGroup body;
    private TMP_Text role;
    private TMP_Text price;
    private string roleText;
    private bool buildable;
    private bool hovered;

    public static BuildCard Create(Transform parent, Ship ship, PlanetBuildPanel panel)
    {
        RectTransform rect = UIKit.Rect(ship.name, parent, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, new Vector2(184f, 0f));
        LayoutElement layout = rect.gameObject.AddComponent<LayoutElement>();
        layout.preferredWidth = 184f;
        layout.flexibleHeight = 1f;

        BuildCard card = rect.gameObject.AddComponent<BuildCard>();
        card.Ship = ship;
        card.panel = panel;
        card.background = UIKit.Image(rect, Idle, true);

        RectTransform content = UIKit.Fill("Body", rect);
        card.body = content.gameObject.AddComponent<CanvasGroup>();
        card.body.blocksRaycasts = false;

        RectTransform iconArea = UIKit.Rect("Icon", content, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -10f), new Vector2(-24f, 112f));
        Image icon = UIKit.Image(iconArea, Color.white);
        icon.sprite = ship.IconSprite;
        icon.preserveAspect = true;
        icon.enabled = icon.sprite != null;

        TMP_Text name = UIKit.Text(content, ShipInfo.Name(ship), 22f, UIKit.TextColor);
        name.fontStyle = FontStyles.Bold;
        Place(name, 136f, 30f);
        name.enableAutoSizing = true;
        name.fontSizeMin = 15f;
        name.fontSizeMax = 22f;

        card.roleText = ShipInfo.Role(ship);
        card.role = UIKit.Text(content, card.roleText, 15f, UIKit.MutedColor);
        Place(card.role, 166f, 22f);
        card.role.textWrappingMode = TextWrappingModes.Normal;
        card.role.rectTransform.sizeDelta = new Vector2(card.role.rectTransform.sizeDelta.x, 40f);
        card.role.alignment = TextAlignmentOptions.TopLeft;

        RectTransform strip = UIKit.Rect("Price", content, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(0f, 40f));
        UIKit.Image(strip, new Color(0f, 0f, 0f, 0.25f));
        card.price = UIKit.Text(strip, "", 17f, UIKit.TextColor);
        card.price.rectTransform.offsetMin = new Vector2(12f, 0f);
        card.price.rectTransform.offsetMax = new Vector2(-12f, 0f);
        card.price.richText = true;

        card.hoverBar = UIKit.Rect("Hover", rect, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(0f, 3f));
        UIKit.Image(card.hoverBar, UIKit.Accent);
        card.hoverBar.gameObject.SetActive(false);
        return card;
    }

    // Lays a label across the card, its top this far below the card's top.
    private static void Place(TMP_Text text, float top, float height)
    {
        RectTransform rect = text.rectTransform;
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, -top);
        rect.sizeDelta = new Vector2(-24f, height);
    }

    // reason: why it can't be built (null if it can). Prices it can't meet show in red.
    public void Refresh(string reason, float buildTime, bool creditsShort, bool manpowerShort)
    {
        buildable = reason == null;
        body.alpha = buildable ? 1f : 0.55f;
        role.text = buildable ? roleText : reason;
        role.color = buildable ? UIKit.MutedColor : Warning;

        string accent = "#" + ColorUtility.ToHtmlStringRGB(UIKit.Accent);
        string warn = "#" + ColorUtility.ToHtmlStringRGB(Warning);
        string cost = $"<color={(creditsShort ? warn : accent)}>${Ship.cost}</color>";
        string manpower = manpowerShort ? $"<color={warn}>{Ship.populationCost} MP</color>" : $"{Ship.populationCost} MP";
        price.text = $"{cost}<pos=36%>{manpower}<pos=70%>{ShipInfo.Time(buildTime)}";
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        hovered = true;
        background.color = Hover;
        hoverBar.gameObject.SetActive(buildable);
        panel.ShowTooltip(this);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        hovered = false;
        background.color = Idle;
        hoverBar.gameObject.SetActive(false);
        panel.HideTooltip(this);
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left) return;
        bool batch = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        panel.Queue(Ship, batch ? 5 : 1);
    }

    private void OnDisable()
    {
        if (hovered) panel.HideTooltip(this);
        hovered = false;
    }
}
