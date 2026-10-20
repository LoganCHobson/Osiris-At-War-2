using System.Linq;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The battle's ship info blade: slides in from the right edge when a ship has been hovered for a few
// seconds (see ShipHover), so you know what you're looking at. Shows the ship's name, side and role, live
// hull, shields, hardpoints left and what it's shooting at, and its stat sheet.
public class ShipBlade : MonoBehaviour
{
    public const float HoverDelay = 3f;

    private const float Width = 400f;
    private const float SlideTime = 0.18f;
    private const float RefreshInterval = 0.2f;
    private static readonly Color HullColor = new Color(0.55f, 0.9f, 0.6f);
    private static readonly Color ShieldColor = new Color(0.45f, 0.75f, 1f);

    private static ShipBlade instance;

    private RectTransform panel;
    private TMP_Text title;
    private TMP_Text subtitle;
    private RectTransform hullRow;
    private RectTransform hullFill;
    private TMP_Text hullText;
    private RectTransform shieldRow;
    private RectTransform shieldFill;
    private TMP_Text shieldText;
    private TMP_Text body;

    private Ship ship;
    private UnitHealthManager live;
    private ShieldGenerator shield;
    private int startingHardpoints;
    private float shownAmount;
    private bool wanted;
    private float nextRefreshAt;

    public static void Show(Ship ship, UnitHealthManager live = null)
    {
        if (ship == null) return;
        if (instance == null) instance = Create();
        instance.Bind(ship, live);
    }

    public static void Hide()
    {
        if (instance != null) instance.wanted = false;
    }

    private static ShipBlade Create()
    {
        Canvas canvas = UIKit.Canvas("ShipBladeCanvas", 60); // Lives in the current scene.
        ShipBlade blade = canvas.gameObject.AddComponent<ShipBlade>();
        blade.Build(canvas.transform);
        return blade;
    }

    private void Bind(Ship newShip, UnitHealthManager newLive)
    {
        if (newShip != ship || newLive != live)
        {
            ship = newShip;
            live = newLive;
            shield = live != null ? live.GetComponent<ShieldGenerator>() : null;
            HardpointManager hardpoints = live != null ? live.GetComponent<HardpointManager>() : null;
            startingHardpoints = hardpoints != null ? Mathf.Max(hardpoints.hardpoints.Count, ship.prefab.GetComponentsInChildren<HardpointHealth>(true).Length) : 0;
            Refresh();
        }
        wanted = true;
    }

    private void Update()
    {
        shownAmount = Mathf.MoveTowards(shownAmount, wanted ? 1f : 0f, Time.unscaledDeltaTime / SlideTime);
        float eased = 1f - (1f - shownAmount) * (1f - shownAmount);
        panel.anchoredPosition = new Vector2(Mathf.Lerp(Width + 24f, -16f, eased), 0f);
        panel.gameObject.SetActive(shownAmount > 0f);

        if (wanted && Time.unscaledTime >= nextRefreshAt)
        {
            nextRefreshAt = Time.unscaledTime + RefreshInterval;
            Refresh();
        }
    }

    private void Refresh()
    {
        if (ship == null) return;

        title.text = ShipInfo.Name(ship);
        Faction faction = FactionOf();
        string side = faction != null ? (string.IsNullOrEmpty(faction.factionName) ? faction.name : faction.factionName).ToUpperInvariant() : "";
        string sideColor = faction != null ? "#" + ColorUtility.ToHtmlStringRGB(Color.Lerp(faction.color, Color.white, 0.25f)) : "#FFFFFF";
        subtitle.text = (side.Length > 0 ? $"<color={sideColor}>{side}</color>   " : "") + $"{ShipInfo.CategoryName(ShipInfo.CategoryOf(ship))}  ·  {ShipInfo.Role(ship)}";

        bool inBattle = live != null;
        hullRow.gameObject.SetActive(inBattle);
        shieldRow.gameObject.SetActive(inBattle && shield != null);
        if (inBattle)
        {
            float hull = live.maxHealth > 0f ? Mathf.Clamp01(live.currentHealth / live.maxHealth) : 0f;
            SetBar(hullFill, hullText, hull, live.IsDead ? "Destroyed" : $"{live.currentHealth:#,0} / {live.maxHealth:#,0}");
            if (shield != null)
            {
                SetBar(shieldFill, shieldText, shield.Fraction, shield.IsUp ? $"{shield.Current:#,0} / {shield.Capacity:#,0}" : "Down");
            }
        }

        body.text = Sheet();
    }

    private string Sheet()
    {
        ShipInfo.Sheet s = ShipInfo.Stats(ship);
        string muted = "#" + ColorUtility.ToHtmlStringRGBA(UIKit.MutedColor);
        StringBuilder text = new StringBuilder();
        void Stat(string label, string value) => text.AppendLine($"<color={muted}>{label}</color><pos=46%>{value}");

        if (live != null)
        {
            if (live.TryGetComponent(out Squadron squadron))
            {
                Stat("Craft left", $"{squadron.fighters.Count(f => f != null && f.Alive)} / {s.craft}");
                if (squadron.CurrentTarget != null) Stat("Engaging", UnitName(squadron.CurrentTarget));
            }
            else if (live.TryGetComponent(out HardpointManager hardpoints))
            {
                Stat("Hardpoints", $"{hardpoints.hardpoints.Count} / {startingHardpoints}");
                UnitHealthManager target = CurrentTarget(live);
                if (target != null) Stat("Engaging", UnitName(target));
            }
            text.AppendLine("<size=8> </size>");
        }

        if (s.craft > 0 && live == null) Stat("Craft", s.craft.ToString());
        Stat("Hull", s.hull.ToString("#,0"));
        if (s.shields > 0f) Stat("Shields", s.shields.ToString("#,0"));
        Stat("Firepower", s.firepower.ToString("#,0"));
        if (s.range > 0f) Stat("Range", s.range.ToString("0"));
        Stat("Speed", s.speed.ToString("0.#"));
        if (!string.IsNullOrEmpty(s.missiles)) Stat(s.missiles.Contains("torpedo") ? "Torpedoes" : "Missiles", s.missiles);
        if (s.pointDefence > 0) Stat("Point defence", s.pointDefence + (ship.GetComponent<ShieldGenerator>() != null ? " beams" : " CIWS"));
        if (s.squadrons > 0) Stat("Hangar", s.squadrons + " squadrons");
        text.AppendLine("<size=8> </size>");
        Stat("Cost", $"${ship.cost}  ·  {ship.populationCost} MP");
        return text.ToString().TrimEnd();
    }

    private static string UnitName(UnitHealthManager unit)
    {
        if (unit.isBattleStation) return "Battle Station";
        if (unit.isShipyardBonus) return "Shipyard";
        return unit.sourceShip != null ? ShipInfo.Name(unit.sourceShip) : unit.name.Replace("(Clone)", "");
    }

    // The ship its main guns are aiming at, if any.
    private static UnitHealthManager CurrentTarget(UnitHealthManager unit)
    {
        foreach (TurretController gun in unit.GetComponentsInChildren<TurretController>())
        {
            if (gun.targetFilter == TurretController.TargetFilter.FightersOnly || gun.target == null) continue;
            UnitHealthManager target = gun.target.GetComponentInParent<UnitHealthManager>();
            if (target != null && !target.IsDead) return target;
        }
        return null;
    }

    private Faction FactionOf()
    {
        BattleContext context = BattleContext.Instance;
        if (live != null && context != null && context.hasPendingBattle)
        {
            return live.isAttackerSide ? context.attackerFaction : context.defenderFaction;
        }
        return null;
    }

    private static void SetBar(RectTransform fill, TMP_Text label, float fraction, string text)
    {
        fill.anchorMax = new Vector2(Mathf.Clamp01(fraction), 1f);
        label.text = text;
    }

    // ---------------- Layout ----------------

    private void Build(Transform canvas)
    {
        panel = UIKit.Rect("ShipBlade", canvas, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(Width + 24f, 0f), new Vector2(Width, 0f));
        UIKit.Image(panel, UIKit.PanelColor);
        RectTransform edge = UIKit.Rect("Edge", panel, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(3f, 0f));
        UIKit.Image(edge, UIKit.Accent);

        VerticalLayoutGroup layout = panel.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(26, 22, 20, 22);
        layout.spacing = 8f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandHeight = false;
        panel.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        title = Label(panel, 30f, UIKit.TextColor);
        title.fontStyle = FontStyles.Bold;
        subtitle = Label(panel, 15f, UIKit.MutedColor);
        subtitle.textWrappingMode = TextWrappingModes.Normal;

        hullRow = Bar(panel, "HULL", HullColor, out hullFill, out hullText);
        shieldRow = Bar(panel, "SHIELDS", ShieldColor, out shieldFill, out shieldText);

        body = Label(panel, 18f, UIKit.TextColor);
        body.lineSpacing = 6f;
        body.textWrappingMode = TextWrappingModes.Normal;

        panel.gameObject.SetActive(false);
    }

    private static TMP_Text Label(RectTransform parent, float size, Color color)
    {
        RectTransform rect = UIKit.Rect("Label", parent, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        TextMeshProUGUI text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        text.fontSize = size;
        text.color = color;
        text.richText = true;
        text.raycastTarget = false;
        return text;
    }

    private static RectTransform Bar(RectTransform parent, string label, Color color, out RectTransform fill, out TMP_Text value)
    {
        RectTransform row = UIKit.Rect(label, parent, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        row.gameObject.AddComponent<LayoutElement>().preferredHeight = 40f;

        TMP_Text name = UIKit.Text(row, label, 13f, UIKit.MutedColor, TextAlignmentOptions.TopLeft);
        name.characterSpacing = 6f;
        value = UIKit.Text(row, "", 15f, UIKit.TextColor, TextAlignmentOptions.TopRight);

        RectTransform track = UIKit.Rect("Track", row, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 4f), new Vector2(0f, 8f));
        UIKit.Image(track, new Color(1f, 1f, 1f, 0.1f));
        fill = UIKit.Rect("Fill", track, Vector2.zero, new Vector2(1f, 1f), new Vector2(0f, 0.5f), Vector2.zero, Vector2.zero);
        UIKit.Image(fill, color);
        return row;
    }
}
