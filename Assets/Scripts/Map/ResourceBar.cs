using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Always-on galactic map readout of the player's credits (with income per tax tick) and manpower.
// Built at runtime, so the scene needs no setup - GalacticMapManager creates one if none exists.
public class ResourceBar : MonoBehaviour
{
    public Vector2 screenOffset = new Vector2(-16f, -16f);
    public float fontSize = 26f;
    public float refreshInterval = 0.25f;
    public Color backgroundColor = new Color(0.04f, 0.06f, 0.1f, 0.8f);

    private TMP_Text label;
    private Faction player;
    private float nextRefreshAt;

    private void Start()
    {
        Build();
        Refresh();
    }

    private void Build()
    {
        Canvas canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50;

        CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        GameObject panel = new GameObject("Panel", typeof(RectTransform), typeof(Image), typeof(HorizontalLayoutGroup), typeof(ContentSizeFitter));
        panel.transform.SetParent(transform, false);

        RectTransform panelRect = (RectTransform)panel.transform;
        panelRect.anchorMin = panelRect.anchorMax = panelRect.pivot = new Vector2(1f, 1f);
        panelRect.anchoredPosition = screenOffset;

        Image background = panel.GetComponent<Image>();
        background.color = backgroundColor;
        background.raycastTarget = false;

        HorizontalLayoutGroup layout = panel.GetComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(18, 18, 8, 8);
        layout.childControlWidth = true;
        layout.childControlHeight = true;

        ContentSizeFitter fitter = panel.GetComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        GameObject textObject = new GameObject("Text", typeof(RectTransform));
        textObject.transform.SetParent(panel.transform, false);

        TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
        text.fontSize = fontSize;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.raycastTarget = false;
        label = text;
    }

    private void Update()
    {
        if (Time.unscaledTime < nextRefreshAt) return;
        Refresh();
    }

    private void Refresh()
    {
        nextRefreshAt = Time.unscaledTime + refreshInterval;
        if (label == null) return;

        if (player == null) player = FindPlayerFaction();
        if (player == null)
        {
            label.text = "";
            return;
        }

        int credits = GalacticState.Instance != null ? GalacticState.Instance.GetCurrency(player) : 0;
        int cap = GalacticState.Instance != null ? GalacticState.Instance.currencyCap : int.MaxValue;
        int income = Income(player);
        float tick = EconomyManager.Instance != null ? EconomyManager.Instance.taxTickInterval : 20f;

        int manpowerUsed = Manpower.Used(player);
        int manpowerCap = Manpower.Cap(player);
        string manpowerColor = manpowerUsed >= manpowerCap ? MapText.Bad : "#FFFFFF";
        string creditsColor = credits >= cap ? MapText.Warning : "#FFFFFF";

        label.text =
            $"{Caption("CREDITS")} <b>{MapText.Tint($"${credits:N0}", creditsColor)}</b> " +
            $"<size=75%>{MapText.Tint($"+{income} / {MapText.RealTime(tick)}", MapText.Good)}</size>" +
            $"      {Caption("MANPOWER")} <b>{MapText.Tint($"{manpowerUsed} / {manpowerCap}", manpowerColor)}</b>";
    }

    private static string Caption(string text)
    {
        return $"<size=65%>{MapText.Tint(text, MapText.Muted)}</size>";
    }

    private static int Income(Faction faction)
    {
        int income = 0;
        foreach (Planet planet in Planet.All)
        {
            if (planet.owner != faction) continue;
            income += Planet.BaseIncome + (planet.hasTaxOffice ? Planet.TaxOfficeIncome : 0);
        }
        return income;
    }

    private static Faction FindPlayerFaction()
    {
        foreach (Planet planet in Planet.All)
        {
            if (Strength.IsPlayer(planet.owner)) return planet.owner;
        }
        foreach (GalacticFleet fleet in GalacticFleet.All)
        {
            if (Strength.IsPlayer(fleet.faction)) return fleet.faction;
        }
        return null;
    }
}
