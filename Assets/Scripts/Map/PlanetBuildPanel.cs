using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The planet's build menu, docked along the bottom of the galactic map under the fleet panel:
//   left   - planet, owner and its structures (tax office, capital shipyard, battle station)
//   centre - the faction's ships as cards, filtered by category tabs, scrolling sideways however long
//            the roster grows; hover a card for its stat sheet
//   right  - the build queue with progress, time left and cancel (full refund)
// Built at runtime in the shared UIKit style. The old scene-built panel is hidden on start.
public class PlanetBuildPanel : MonoBehaviour
{
    public static PlanetBuildPanel Instance;

    [Header("Old scene panel (hidden; the menu now builds itself)")]
    public GameObject panelRoot;
    public TMP_Text planetNameText;
    public TMP_Text currencyText;

    [Header("Debug")]
    public bool debugBuildOnAnyPlanet = false;

    private const float DockHeight = 336f;
    private const float LeftWidth = 340f;
    private const float RightWidth = 380f;
    private const float RefreshInterval = 0.2f;
    private static readonly Color Warning = new Color(1f, 0.45f, 0.4f);
    private static readonly Color Good = new Color(0.55f, 0.9f, 0.6f);

    public bool IsOpen => dock != null && dock.gameObject.activeSelf;

    private Planet currentPlanet;
    private Faction shownRoster;
    private ShipInfo.Category? filter;
    private float nextRefreshAt;
    private int shownQueueCount = -1;

    private Canvas canvas;
    private RectTransform dock;
    private TMP_Text planetName;
    private TMP_Text ownerName;
    private readonly List<StructureRow> structures = new List<StructureRow>();
    private RectTransform tabBar;
    private readonly List<(ShipInfo.Category? category, TMP_Text label, RectTransform underline)> tabs = new List<(ShipInfo.Category?, TMP_Text, RectTransform)>();
    private RectTransform cardContent;
    private ScrollRect cardScroll;
    private TMP_Text shipyardNotice;
    private TMP_Text shipyardHint;
    private readonly List<BuildCard> cards = new List<BuildCard>();
    private TMP_Text queueSummary;
    private RectTransform queueContent;
    private TMP_Text queueNotice;
    private readonly List<QueueRow> queueRows = new List<QueueRow>();
    private RectTransform tooltip;
    private TMP_Text tooltipText;
    private BuildCard tooltipCard;

    private class StructureRow
    {
        public TMP_Text status;
        public Button button;
        public TMP_Text buttonLabel;
        public Image buttonBackground;
    }

    private class QueueRow
    {
        public RectTransform root;
        public Image icon;
        public TMP_Text name;
        public TMP_Text time;
        public RectTransform progressTrack;
        public RectTransform progressFill;
    }

    private Faction Builder => currentPlanet != null ? currentPlanet.owner : null;

    private void Awake()
    {
        Instance = this;

        // Retire the old hand-laid scene panel and its loose labels.
        if (panelRoot != null) panelRoot.SetActive(false);
        if (planetNameText != null) planetNameText.gameObject.SetActive(false);
        if (currencyText != null) currencyText.gameObject.SetActive(false);

        Build();
        dock.gameObject.SetActive(false);
    }

    private void Update()
    {
        if (!IsOpen || currentPlanet == null) return;
        if (Time.unscaledTime < nextRefreshAt && currentPlanet.shipBuildQueue.Count == shownQueueCount) return;
        nextRefreshAt = Time.unscaledTime + RefreshInterval;
        Refresh();
    }

    public void Show(Planet planet)
    {
        currentPlanet = planet;
        dock.gameObject.SetActive(true);
        shownQueueCount = -1;
        if (shownRoster != Builder) RebuildCards();
        Refresh();
    }

    public void Hide()
    {
        currentPlanet = null;
        HideTooltip(tooltipCard);
        if (dock != null) dock.gameObject.SetActive(false);
    }

    // ---------------- Actions ----------------

    private bool CanBuildHere()
    {
        if (currentPlanet == null) return false;
        if (debugBuildOnAnyPlanet) return true;
        return currentPlanet.owner != null && currentPlanet.owner.isPlayerFaction;
    }

    private int Credits => !CanBuildHere() ? 0
        : debugBuildOnAnyPlanet ? int.MaxValue
        : GalacticState.Instance != null ? GalacticState.Instance.GetCurrency(currentPlanet.owner) : 0;

    public void BuildTaxOffice()
    {
        if (currentPlanet == null || currentPlanet.hasTaxOffice || !TrySpend(Planet.TaxOfficeCost)) return;
        currentPlanet.hasTaxOffice = true;
        Refresh();
    }

    public void BuildCapitalShipyard()
    {
        if (currentPlanet == null || !currentPlanet.canBuildCapitalShipyard || currentPlanet.hasCapitalShipyard) return;
        if (!TrySpend(Planet.CapitalShipyardCost)) return;
        currentPlanet.hasCapitalShipyard = true;
        Refresh();
    }

    public void BuildBattleStation()
    {
        if (currentPlanet == null || currentPlanet.hasBattleStation) return;
        if (currentPlanet.owner != null && Planet.BattleStationLimitReached(currentPlanet.owner)) return;
        if (!TrySpend(Planet.BattleStationCost)) return;
        currentPlanet.hasBattleStation = true;
        Refresh();
    }

    public void DemolishBattleStation()
    {
        if (!CanBuildHere() || !currentPlanet.hasBattleStation) return;
        currentPlanet.DemolishBattleStation();
        Refresh();
    }

    private bool TrySpend(int amount)
    {
        if (!CanBuildHere()) return false;
        if (debugBuildOnAnyPlanet) return true;
        return GalacticState.Instance != null && GalacticState.Instance.TrySpend(currentPlanet.owner, amount);
    }

    public void Queue(Ship ship, int count)
    {
        for (int i = 0; i < count; i++)
        {
            if (WhyNot(ship) != null) break;
            if (!TrySpend(ship.cost)) break;
            currentPlanet.shipBuildQueue.Add(new ShipBuildOrder { ship = ship, remainingTime = BuildTime(ship) });
        }
        Refresh();
    }

    // Cancels an order and refunds it in full.
    private void Cancel(int index)
    {
        if (!CanBuildHere() || index < 0 || index >= currentPlanet.shipBuildQueue.Count) return;
        ShipBuildOrder order = currentPlanet.shipBuildQueue[index];
        currentPlanet.shipBuildQueue.RemoveAt(index);
        if (!debugBuildOnAnyPlanet && order.ship != null) GalacticState.Instance?.AddCurrency(currentPlanet.owner, order.ship.cost);
        Refresh();
    }

    private float BuildTime(Ship ship) => Builder != null ? Builder.BuildTime(ship) : ship.buildTime;

    // Why this ship can't be queued right now, or null if it can.
    private string WhyNot(Ship ship)
    {
        if (!CanBuildHere()) return "Not your planet";
        if (!currentPlanet.hasCapitalShipyard) return currentPlanet.canBuildCapitalShipyard ? "Needs a capital shipyard" : "No shipyard site";
        if (Credits < ship.cost) return "Not enough credits";
        if (!debugBuildOnAnyPlanet && ship.populationCost > Manpower.Free(currentPlanet.owner)) return "Manpower limit reached";
        return null;
    }

    // ---------------- Refresh ----------------

    private void Refresh()
    {
        if (currentPlanet == null) return;
        bool mine = CanBuildHere();
        bool known = mine || FogOfWar.CanSee(currentPlanet);

        planetName.text = currentPlanet.planetName;
        Faction owner = currentPlanet.owner;
        ownerName.text = owner == null ? "UNCLAIMED" : (string.IsNullOrEmpty(owner.factionName) ? owner.name : owner.factionName).ToUpperInvariant();
        ownerName.color = owner != null ? Color.Lerp(owner.color, Color.white, 0.25f) : UIKit.MutedColor;

        RefreshStructures(mine, known);
        RefreshCards(mine);
        RefreshQueue(mine);
    }

    private void RefreshStructures(bool mine, bool known)
    {
        int credits = Credits;
        Planet p = currentPlanet;

        // On someone else's planet: what's there (if scouted), no buttons.
        string Seen(bool has, string detail) => !known ? "Unknown" : has ? detail : "Not built";

        SetStructure(structures[0], mine,
            mine ? "+" + Planet.TaxOfficeIncome + " credits a cycle" : Seen(p.hasTaxOffice, "Built"),
            p.hasTaxOffice ? "BUILT" : $"BUILD ${Planet.TaxOfficeCost}",
            mine && !p.hasTaxOffice && credits >= Planet.TaxOfficeCost, p.hasTaxOffice);

        bool site = p.canBuildCapitalShipyard || p.hasCapitalShipyard;
        SetStructure(structures[1], mine,
            mine ? (site ? "Builds ships  ·  +" + Manpower.PerCapitalShipyard + " MP" : "No shipyard site here") : Seen(p.hasCapitalShipyard, "Built"),
            p.hasCapitalShipyard ? "BUILT" : site ? $"BUILD ${Planet.CapitalShipyardCost}" : "NO SITE",
            mine && site && !p.hasCapitalShipyard && credits >= Planet.CapitalShipyardCost, p.hasCapitalShipyard);

        bool limit = mine && p.owner != null && !p.hasBattleStation && Planet.BattleStationLimitReached(p.owner);
        SetStructure(structures[2], mine,
            mine ? (limit ? $"Limit reached ({p.owner.maxBattleStations})" : "Defends in battle") : Seen(p.hasBattleStation, "Built"),
            p.hasBattleStation ? $"DEMOLISH +${Planet.BattleStationRefund}" : limit ? "LIMIT" : $"BUILD ${Planet.BattleStationCost}",
            mine && (p.hasBattleStation || (!limit && credits >= Planet.BattleStationCost)), false);
    }

    private static void SetStructure(StructureRow row, bool mine, string status, string action, bool interactable, bool built)
    {
        row.status.text = status;
        row.button.gameObject.SetActive(mine);
        row.buttonLabel.text = action;
        row.button.interactable = interactable;
        row.buttonLabel.color = built ? Good : interactable ? UIKit.Accent : UIKit.MutedColor;
        row.buttonBackground.color = interactable ? new Color(UIKit.Accent.r, UIKit.Accent.g, UIKit.Accent.b, 0.12f) : new Color(1f, 1f, 1f, 0.04f);
    }

    private void RefreshCards(bool mine)
    {
        int credits = Credits;
        int freeManpower = debugBuildOnAnyPlanet || !mine ? int.MaxValue : Manpower.Free(currentPlanet.owner);
        foreach (BuildCard card in cards)
        {
            card.Refresh(WhyNot(card.Ship), BuildTime(card.Ship), mine && credits < card.Ship.cost, card.Ship.populationCost > freeManpower);
        }

        shipyardNotice.gameObject.SetActive(!mine || cards.Count == 0);
        shipyardNotice.text = cards.Count == 0 ? "No ships to build" : currentPlanet.owner == null ? "Unclaimed world" : "Enemy territory";
        cardScroll.gameObject.SetActive(mine && cards.Count > 0);
        if (!cardScroll.gameObject.activeSelf) cardScroll.horizontalScrollbar.gameObject.SetActive(false); // Auto-hide only runs while scrolling is live.
        tabBar.gameObject.SetActive(mine);
        shipyardHint.gameObject.SetActive(mine);
    }

    private void RefreshQueue(bool mine)
    {
        List<ShipBuildOrder> queue = currentPlanet.shipBuildQueue;
        if (queue.Count != shownQueueCount) RebuildQueueRows();

        float total = 0f;
        for (int i = 0; i < queue.Count && i < queueRows.Count; i++)
        {
            ShipBuildOrder order = queue[i];
            QueueRow row = queueRows[i];
            float full = order.ship != null ? BuildTime(order.ship) : 1f;
            total += order.remainingTime;

            bool building = i == 0;
            row.progressTrack.gameObject.SetActive(building);
            row.progressFill.anchorMax = new Vector2(Mathf.Clamp01(1f - order.remainingTime / Mathf.Max(0.01f, full)), 1f);
            row.time.text = building ? ShipInfo.Time(order.remainingTime) : "Queued";
            row.time.color = building ? UIKit.TextColor : UIKit.MutedColor;
        }

        queueSummary.text = queue.Count == 0 ? "" : $"{queue.Count}  ·  {ShipInfo.Time(total)}";
        queueNotice.gameObject.SetActive(!mine || queue.Count == 0);
        queueNotice.text = mine ? "Nothing in production" : "Not your planet";
    }

    // ---------------- Cards ----------------

    private void RebuildCards()
    {
        foreach (BuildCard card in cards) Destroy(card.gameObject);
        cards.Clear();
        shownRoster = Builder;

        IEnumerable<Ship> roster = Builder != null ? Builder.roster.Where(s => s != null) : Enumerable.Empty<Ship>();
        List<Ship> ships = roster.OrderBy(s => (int)ShipInfo.CategoryOf(s)).ThenByDescending(s => s.cost).ToList();
        foreach (Ship ship in ships)
        {
            cards.Add(BuildCard.Create(cardContent, ship, this));
        }

        // Tabs only for the categories this faction actually has.
        foreach (var tab in tabs)
        {
            bool has = tab.category == null || ships.Any(s => ShipInfo.CategoryOf(s) == tab.category);
            tab.label.transform.parent.gameObject.SetActive(has);
        }
        if (filter != null && !ships.Any(s => ShipInfo.CategoryOf(s) == filter)) filter = null;
        ApplyFilter();
    }

    private void SetFilter(ShipInfo.Category? category)
    {
        filter = category;
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        foreach (BuildCard card in cards) card.gameObject.SetActive(filter == null || ShipInfo.CategoryOf(card.Ship) == filter);
        foreach (var tab in tabs)
        {
            bool active = tab.category == filter;
            tab.label.color = active ? UIKit.TextColor : UIKit.MutedColor;
            tab.underline.gameObject.SetActive(active);
        }
        cardScroll.horizontalNormalizedPosition = 0f;
    }

    // ---------------- Tooltip ----------------

    public void ShowTooltip(BuildCard card)
    {
        tooltipCard = card;
        Ship ship = card.Ship;
        ShipInfo.Sheet s = ShipInfo.Stats(ship);
        string muted = "#" + ColorUtility.ToHtmlStringRGBA(UIKit.MutedColor);

        System.Text.StringBuilder text = new System.Text.StringBuilder();
        text.AppendLine($"<size=26><b>{ShipInfo.Name(ship)}</b></size>");
        text.AppendLine($"<color={muted}>{ShipInfo.CategoryName(ShipInfo.CategoryOf(ship))}  ·  {ShipInfo.Role(ship)}</color>");
        text.AppendLine("<size=8> </size>");
        void Stat(string label, string value) => text.AppendLine($"<color={muted}>{label}</color><pos=48%>{value}");
        if (s.craft > 0) Stat("Craft", s.craft.ToString());
        Stat("Hull", s.hull.ToString("#,0"));
        if (s.shields > 0f) Stat("Shields", s.shields.ToString("#,0"));
        Stat("Firepower", s.firepower.ToString("#,0"));
        if (s.range > 0f) Stat("Range", s.range.ToString("0"));
        Stat("Speed", s.speed.ToString("0.#"));
        if (!string.IsNullOrEmpty(s.missiles)) Stat(s.missiles.Contains("torpedo") ? "Torpedoes" : "Missiles", s.missiles);
        if (s.pointDefence > 0) Stat("Point defence", s.pointDefence + (ship.GetComponent<ShieldGenerator>() != null ? " beams" : " CIWS"));
        if (s.squadrons > 0) Stat("Hangar", s.squadrons + " squadrons");
        text.AppendLine("<size=8> </size>");
        Stat("Cost", $"${ship.cost}");
        Stat("Manpower", ship.populationCost.ToString());
        Stat("Build time", ShipInfo.Time(BuildTime(ship)));
        tooltipText.text = text.ToString().TrimEnd();

        // Sit above the menu, lined up with the card, kept on screen.
        RectTransform canvasRect = (RectTransform)canvas.transform;
        Vector3[] corners = new Vector3[4];
        ((RectTransform)card.transform).GetWorldCorners(corners);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, (corners[1] + corners[2]) / 2f, null, out Vector2 cardTop);
        dock.GetWorldCorners(corners);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, corners[1], null, out Vector2 dockTop);
        tooltip.gameObject.SetActive(true);
        LayoutRebuilder.ForceRebuildLayoutImmediate(tooltip);
        float halfWidth = tooltip.rect.width / 2f;
        float edge = canvasRect.rect.width / 2f - 12f;
        tooltip.anchoredPosition = new Vector2(Mathf.Clamp(cardTop.x, -edge + halfWidth, edge - halfWidth), dockTop.y + 10f);
    }

    public void HideTooltip(BuildCard card)
    {
        if (card != tooltipCard || tooltip == null) return;
        tooltipCard = null;
        tooltip.gameObject.SetActive(false);
    }

    // ---------------- Queue ----------------

    private void RebuildQueueRows()
    {
        foreach (QueueRow row in queueRows) Destroy(row.root.gameObject);
        queueRows.Clear();

        List<ShipBuildOrder> queue = currentPlanet.shipBuildQueue;
        shownQueueCount = queue.Count;
        for (int i = 0; i < queue.Count; i++)
        {
            if (queue[i].ship == null) continue;
            queueRows.Add(QueueRowFor(queue[i].ship, i));
        }
    }

    private QueueRow QueueRowFor(Ship ship, int index)
    {
        QueueRow row = new QueueRow();
        row.root = UIKit.Rect("Order", queueContent, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 50f));
        UIKit.Image(row.root, UIKit.RowColor);
        row.root.gameObject.AddComponent<LayoutElement>().preferredHeight = 50f;

        RectTransform iconRect = UIKit.Rect("Icon", row.root, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(8f, 0f), new Vector2(44f, 40f));
        row.icon = UIKit.Image(iconRect, Color.white);
        row.icon.sprite = ship.IconSprite;
        row.icon.preserveAspect = true;
        row.icon.enabled = row.icon.sprite != null;

        row.name = UIKit.Text(row.root, ShipInfo.Name(ship), 19f, UIKit.TextColor, TextAlignmentOptions.TopLeft);
        row.name.rectTransform.offsetMin = new Vector2(62f, 0f);
        row.name.rectTransform.offsetMax = new Vector2(-52f, -6f);

        row.time = UIKit.Text(row.root, "", 16f, UIKit.MutedColor, TextAlignmentOptions.BottomRight);
        row.time.rectTransform.offsetMin = new Vector2(62f, 6f);
        row.time.rectTransform.offsetMax = new Vector2(-52f, 0f);

        row.progressTrack = UIKit.Rect("Progress", row.root, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 0f), new Vector2(62f, 11f), new Vector2(-62f - 52f - 70f, 6f));
        UIKit.Image(row.progressTrack, new Color(1f, 1f, 1f, 0.12f));
        row.progressFill = UIKit.Rect("Fill", row.progressTrack, Vector2.zero, new Vector2(0f, 1f), new Vector2(0f, 0.5f), Vector2.zero, Vector2.zero);
        UIKit.Image(row.progressFill, UIKit.Accent);

        RectTransform cancel = UIKit.Rect("Cancel", row.root, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-8f, 0f), new Vector2(36f, 36f));
        Image cancelBackground = UIKit.Image(cancel, new Color(1f, 1f, 1f, 0.06f), true);
        UIKit.Text(cancel, "×", 26f, Warning, TextAlignmentOptions.Center);
        Button button = cancel.gameObject.AddComponent<Button>();
        button.targetGraphic = cancelBackground;
        ColorBlock colors = button.colors;
        colors.highlightedColor = new Color(1f, 1f, 1f, 3f);
        button.colors = colors;
        int captured = index;
        button.onClick.AddListener(() => Cancel(captured));
        return row;
    }

    // ---------------- Layout ----------------

    private void Build()
    {
        canvas = UIKit.Canvas("BuildMenuCanvas", 40, transform);

        dock = UIKit.Rect("BuildMenu", canvas.transform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 16f), new Vector2(-40f, DockHeight));
        UIKit.Image(dock, UIKit.PanelColor, true);
        RectTransform rule = UIKit.Rect("Rule", dock, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 2f));
        UIKit.Image(rule, new Color(UIKit.Accent.r, UIKit.Accent.g, UIKit.Accent.b, 0.55f));

        BuildPlanetColumn();
        BuildShipyard();
        BuildQueueColumn();
        BuildTooltip();
    }

    // A full-height column pinned to the dock's left or right edge.
    private RectTransform SideColumn(string name, bool right, float width)
    {
        float x = right ? 1f : 0f;
        return UIKit.Rect(name, dock, new Vector2(x, 0f), new Vector2(x, 1f), new Vector2(x, 0.5f), Vector2.zero, new Vector2(width, 0f));
    }

    // A label spanning a column, its top this far below the column's top.
    private static TMP_Text TopLabel(Transform parent, float size, Color color, float top, float height)
    {
        TMP_Text label = UIKit.Text(parent, "", size, color, TextAlignmentOptions.TopLeft);
        RectTransform rect = label.rectTransform;
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(4f, -top);
        rect.sizeDelta = new Vector2(-40f, height);
        return label;
    }

    private static TMP_Text Heading(Transform parent, string text, Vector2 position)
    {
        TMP_Text heading = UIKit.Text(parent, text, 16f, UIKit.MutedColor, TextAlignmentOptions.TopLeft);
        heading.characterSpacing = 8f;
        RectTransform rect = heading.rectTransform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = position;
        rect.sizeDelta = new Vector2(300f, 24f);
        return heading;
    }

    private static RectTransform Divider(RectTransform parent, bool rightEdge)
    {
        float x = rightEdge ? 1f : 0f;
        RectTransform line = UIKit.Rect("Divider", parent, new Vector2(x, 0f), new Vector2(x, 1f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1f, -32f));
        UIKit.Image(line, new Color(1f, 1f, 1f, 0.08f));
        return line;
    }

    private void BuildPlanetColumn()
    {
        RectTransform column = SideColumn("Planet", false, LeftWidth);
        Divider(column, true);

        planetName = TopLabel(column, 32f, UIKit.TextColor, 14f, 44f);
        planetName.fontStyle = FontStyles.Bold;
        planetName.enableAutoSizing = true;
        planetName.fontSizeMin = 20f;
        planetName.fontSizeMax = 32f;

        ownerName = TopLabel(column, 16f, UIKit.MutedColor, 60f, 24f);
        ownerName.characterSpacing = 6f;

        Heading(column, "STRUCTURES", new Vector2(24f, -100f));

        RectTransform list = UIKit.Rect("Structures", column, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -128f), new Vector2(-40f, 3 * 58f + 2 * 6f));
        UIKit.List(list, 6f);
        structures.Add(StructureRowFor(list, "Tax Office", BuildTaxOffice));
        structures.Add(StructureRowFor(list, "Capital Shipyard", BuildCapitalShipyard));
        structures.Add(StructureRowFor(list, "Battle Station", () =>
        {
            if (currentPlanet != null && currentPlanet.hasBattleStation) DemolishBattleStation();
            else BuildBattleStation();
        }));
    }

    private StructureRow StructureRowFor(RectTransform list, string title, UnityEngine.Events.UnityAction action)
    {
        RectTransform row = UIKit.Rect(title, list, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 58f));
        UIKit.Image(row, UIKit.RowColor);
        row.gameObject.AddComponent<LayoutElement>().preferredHeight = 58f;

        TMP_Text name = UIKit.Text(row, title, 20f, UIKit.TextColor, TextAlignmentOptions.TopLeft);
        name.rectTransform.offsetMin = new Vector2(14f, 0f);
        name.rectTransform.offsetMax = new Vector2(-128f, -7f);

        TMP_Text status = UIKit.Text(row, "", 14f, UIKit.MutedColor, TextAlignmentOptions.BottomLeft);
        status.rectTransform.offsetMin = new Vector2(14f, 8f);
        status.rectTransform.offsetMax = new Vector2(-128f, 0f);
        status.textWrappingMode = TextWrappingModes.NoWrap;
        status.overflowMode = TextOverflowModes.Ellipsis;

        RectTransform buttonRect = UIKit.Rect("Action", row, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-8f, 0f), new Vector2(112f, 40f));
        Image background = UIKit.Image(buttonRect, Color.clear, true);
        TMP_Text label = UIKit.Text(buttonRect, "", 15f, UIKit.Accent, TextAlignmentOptions.Center);
        label.fontStyle = FontStyles.Bold;
        label.enableAutoSizing = true;
        label.fontSizeMin = 11f;
        label.fontSizeMax = 15f;
        label.rectTransform.offsetMin = new Vector2(6f, 0f);
        label.rectTransform.offsetMax = new Vector2(-6f, 0f);
        Button button = buttonRect.gameObject.AddComponent<Button>();
        button.targetGraphic = background;
        ColorBlock colors = button.colors;
        colors.highlightedColor = new Color(1f, 1f, 1f, 2.2f);
        colors.disabledColor = Color.white;
        button.colors = colors;
        button.onClick.AddListener(action);

        return new StructureRow { status = status, button = button, buttonLabel = label, buttonBackground = background };
    }

    private void BuildShipyard()
    {
        RectTransform column = UIKit.Fill("Shipyard", dock);
        column.offsetMin = new Vector2(LeftWidth, 0f);
        column.offsetMax = new Vector2(-RightWidth, 0f);

        Heading(column, "SHIPYARD", new Vector2(24f, -16f));
        shipyardHint = UIKit.Text(column, "Click to build  ·  Shift-click for five", 14f, UIKit.MutedColor, TextAlignmentOptions.TopRight);
        shipyardHint.rectTransform.offsetMax = new Vector2(-24f, -18f);

        // Category tabs: only the ones the faction has are shown (see RebuildCards).
        tabBar = UIKit.Rect("Tabs", column, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(150f, -8f), new Vector2(700f, 36f));
        HorizontalLayoutGroup tabLayout = tabBar.gameObject.AddComponent<HorizontalLayoutGroup>();
        tabLayout.spacing = 4f;
        tabLayout.childControlWidth = true;
        tabLayout.childControlHeight = true;
        tabLayout.childForceExpandWidth = false;
        AddTab(null, "ALL");
        foreach (ShipInfo.Category category in ShipInfo.Categories) AddTab(category, ShipInfo.CategoryName(category).ToUpperInvariant());

        RectTransform viewport = UIKit.Rect("Cards", column, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        viewport.offsetMin = new Vector2(20f, 14f);
        viewport.offsetMax = new Vector2(-20f, -56f);
        UIKit.Image(viewport, new Color(0f, 0f, 0f, 0.01f), true);
        viewport.gameObject.AddComponent<RectMask2D>();

        cardContent = UIKit.Rect("Content", viewport, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), Vector2.zero, Vector2.zero);
        HorizontalLayoutGroup cardLayout = cardContent.gameObject.AddComponent<HorizontalLayoutGroup>();
        cardLayout.spacing = 12f;
        cardLayout.childControlWidth = true;
        cardLayout.childControlHeight = true;
        cardLayout.childForceExpandWidth = false;
        cardLayout.childForceExpandHeight = true;
        cardContent.gameObject.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

        cardScroll = viewport.gameObject.AddComponent<ScrollRect>();
        cardScroll.content = cardContent;
        cardScroll.viewport = viewport;
        cardScroll.horizontal = true;
        cardScroll.vertical = false;
        cardScroll.movementType = ScrollRect.MovementType.Clamped;
        cardScroll.scrollSensitivity = 60f;

        // A thin bar under the cards, shown only when the roster is wider than the panel.
        RectTransform track = UIKit.Rect("Scrollbar", column, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 5f), new Vector2(-40f, 4f));
        UIKit.Image(track, new Color(1f, 1f, 1f, 0.06f), true);
        RectTransform slidingArea = UIKit.Fill("Sliding Area", track);
        RectTransform handle = UIKit.Fill("Handle", slidingArea);
        Image handleImage = UIKit.Image(handle, new Color(UIKit.Accent.r, UIKit.Accent.g, UIKit.Accent.b, 0.6f), true);
        Scrollbar scrollbar = track.gameObject.AddComponent<Scrollbar>();
        scrollbar.handleRect = handle;
        scrollbar.targetGraphic = handleImage;
        scrollbar.direction = Scrollbar.Direction.LeftToRight;
        cardScroll.horizontalScrollbar = scrollbar;
        cardScroll.horizontalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;

        shipyardNotice = UIKit.Text(column, "", 24f, UIKit.MutedColor, TextAlignmentOptions.Center);
        shipyardNotice.rectTransform.offsetMax = new Vector2(0f, -40f);
        shipyardNotice.characterSpacing = 6f;
    }

    private void AddTab(ShipInfo.Category? category, string title)
    {
        RectTransform rect = UIKit.Rect(title, tabBar, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
        LayoutElement layout = rect.gameObject.AddComponent<LayoutElement>();
        layout.preferredWidth = 24f + title.Length * 11.5f;
        Image hit = UIKit.Image(rect, new Color(0f, 0f, 0f, 0.01f), true);

        TMP_Text label = UIKit.Text(rect, title, 15f, UIKit.MutedColor, TextAlignmentOptions.Center);
        label.characterSpacing = 4f;
        label.fontStyle = FontStyles.Bold;

        RectTransform underline = UIKit.Rect("Underline", rect, new Vector2(0.15f, 0f), new Vector2(0.85f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 2f), new Vector2(0f, 2f));
        UIKit.Image(underline, UIKit.Accent);
        underline.gameObject.SetActive(false);

        Button button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = hit;
        button.transition = Selectable.Transition.None;
        button.onClick.AddListener(() => SetFilter(category));
        tabs.Add((category, label, underline));
    }

    private void BuildQueueColumn()
    {
        RectTransform column = SideColumn("Queue", true, RightWidth);
        Divider(column, false);

        Heading(column, "BUILD QUEUE", new Vector2(24f, -16f));
        queueSummary = UIKit.Text(column, "", 16f, UIKit.TextColor, TextAlignmentOptions.TopRight);
        queueSummary.rectTransform.offsetMax = new Vector2(-24f, -16f);

        RectTransform viewport = UIKit.Rect("Orders", column, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        viewport.offsetMin = new Vector2(20f, 14f);
        viewport.offsetMax = new Vector2(-20f, -50f);
        UIKit.Image(viewport, new Color(0f, 0f, 0f, 0.01f), true);
        viewport.gameObject.AddComponent<RectMask2D>();

        queueContent = UIKit.Rect("Content", viewport, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, Vector2.zero);
        UIKit.List(queueContent, 6f);
        queueContent.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        ScrollRect scroll = viewport.gameObject.AddComponent<ScrollRect>();
        scroll.content = queueContent;
        scroll.viewport = viewport;
        scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 40f;

        queueNotice = UIKit.Text(column, "", 18f, UIKit.MutedColor, TextAlignmentOptions.Center);
        queueNotice.rectTransform.offsetMax = new Vector2(0f, -40f);
    }

    private void BuildTooltip()
    {
        tooltip = UIKit.Rect("Tooltip", canvas.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(400f, 0f));
        UIKit.Image(tooltip, new Color(0.03f, 0.05f, 0.08f, 0.97f));
        RectTransform edge = UIKit.Rect("Edge", tooltip, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(3f, 0f));
        UIKit.Image(edge, UIKit.Accent);

        VerticalLayoutGroup layout = tooltip.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(22, 20, 16, 18);
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        tooltip.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        RectTransform textRect = UIKit.Rect("Text", tooltip, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        tooltipText = textRect.gameObject.AddComponent<TextMeshProUGUI>();
        tooltipText.fontSize = 18f;
        tooltipText.color = UIKit.TextColor;
        tooltipText.lineSpacing = 6f;
        tooltipText.raycastTarget = false;
        tooltipText.richText = true;
        tooltip.gameObject.SetActive(false);
    }
}
