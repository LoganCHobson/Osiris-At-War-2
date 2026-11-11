using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// The galactic map's production screen (P, the PRODUCTION button, or BUILD & SEND HERE on a planet):
//   left   - your saved fleet designs
//   centre - the fleet designer: name it, set how many of each ship class
//   right  - order the design: where it should go, which shipyard(s) build it, and every shipyard's
//            current work at a glance so you never lose track of what's being built where.
// Ordering spreads the ships over the chosen yards (soonest finish first) and each finished batch flies
// to the destination on its own - see ProductionOrders.
public class ProductionWindow : MonoBehaviour
{
    private const float Width = 1260f;
    private const float Height = 720f;
    private const float RefreshInterval = 0.25f;

    private static ProductionWindow instance;

    public static bool IsOpen => instance != null && instance.window != null && instance.window.gameObject.activeSelf;

    private Faction faction;
    private List<FleetDesign> designs = new List<FleetDesign>();
    private int selected = -1;
    private FleetDesign draft = new FleetDesign();

    private List<Planet> destinations = new List<Planet>();
    private List<Planet> yards = new List<Planet>();
    private Planet destination;
    private Planet onlyYard;

    private RectTransform window;
    private RectTransform designList;
    private TMP_InputField nameField;
    private RectTransform shipList;
    private TMP_Text totals;
    private UIBits.PillButton destinationButton;
    private static bool PickingDestination => GalacticMapManager.Instance != null && GalacticMapManager.Instance.IsPickingPlanet;
    private UIBits.Cycler yardCycler;
    private UIBits.PillButton orderButton;
    private TMP_Text orderResult;
    private TMP_Text standingLabel;
    private RectTransform yardList;
    private readonly List<(Ship ship, TMP_Text count)> shipRows = new List<(Ship, TMP_Text)>();
    private float nextRefreshAt;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Hook()
    {
        SceneManager.sceneLoaded += (scene, _) => AttachTo(scene);
        AttachTo(SceneManager.GetActiveScene());
    }

    private static void AttachTo(Scene scene)
    {
        if (scene.name != "GalacticMap" || instance != null) return;
        instance = new GameObject("ProductionWindow").AddComponent<ProductionWindow>();
    }

    // destination: preselect where the fleet goes. yard: preselect building only at this shipyard.
    public static void Open(Planet destination = null, Planet yard = null)
    {
        if (instance == null) instance = new GameObject("ProductionWindow").AddComponent<ProductionWindow>();
        instance.Show(destination, yard);
    }

    public static void Close()
    {
        if (instance != null && instance.window != null) instance.window.gameObject.SetActive(false);
        UIBits.ModalOpen = DefensePlanner.IsOpen;
    }

    private void Awake()
    {
        Canvas canvas = UIKit.Canvas("ProductionCanvas", 80, transform);
        BuildHudButton(canvas.transform);
        BuildWindow(canvas.transform);
        window.gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
        UIBits.ModalOpen = false;
    }

    private void Update()
    {
        if (Keybinds.Down(KeyAction.Production) && !TypingInField())
        {
            if (IsOpen) Close();
            else Show(null, null);
        }
        if (IsOpen && Input.GetKeyDown(KeyCode.Escape))
        {
            UIBits.EscapeConsumedFrame = Time.frameCount;
            Close();
        }
        UIBits.ModalOpen = IsOpen || DefensePlanner.IsOpen || PickingDestination;

        if (!IsOpen || Time.unscaledTime < nextRefreshAt) return;
        nextRefreshAt = Time.unscaledTime + RefreshInterval;
        RefreshYards();
        RefreshTotals();
    }

    private static bool TypingInField()
    {
        GameObject focused = UnityEngine.EventSystems.EventSystem.current != null ? UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject : null;
        return focused != null && focused.GetComponent<TMP_InputField>() != null;
    }

    private static Faction PlayerFaction()
    {
        Planet owned = Planet.All.Find(p => p != null && p.owner != null && p.owner.isPlayerFaction);
        if (owned != null) return owned.owner;
        GalacticFleet fleet = GalacticFleet.All.Find(f => f != null && f.faction != null && f.faction.isPlayerFaction);
        return fleet != null ? fleet.faction : null;
    }

    private void Show(Planet preferredDestination, Planet preferredYard)
    {
        faction = PlayerFaction();
        if (faction == null) return;

        designs = FleetDesigns.Load(faction);
        if (selected >= designs.Count) selected = -1;
        if (selected < 0) draft = designs.Count > 0 ? designs[0].Clone() : new FleetDesign();
        if (selected < 0 && designs.Count > 0) selected = 0;

        destinations = Planet.All.Where(p => p != null && p.owner == faction).OrderBy(p => p.planetName).ToList();
        yards = ProductionOrders.Shipyards(faction);

        destination = preferredDestination != null && destinations.Contains(preferredDestination) ? preferredDestination
            : destination != null && destinations.Contains(destination) ? destination
            : destinations.FirstOrDefault(p => p.hasCapitalShipyard) ?? destinations.FirstOrDefault();
        onlyYard = preferredYard != null && yards.Contains(preferredYard) ? preferredYard : null;

        window.gameObject.SetActive(true);
        UIBits.ModalOpen = true;
        orderResult.text = "";
        RebuildDesignList();
        RebuildShipList();
        RefreshOrderControls();
        RefreshYards();
        RefreshTotals();
    }

    // ---------------- Designs ----------------

    private void RebuildDesignList()
    {
        UIBits.Clear(designList);
        for (int i = 0; i < designs.Count; i++)
        {
            int index = i;
            FleetDesign design = designs[i];
            UIBits.PillButton button = UIBits.Button(designList, $"{design.name}  ({design.ShipCount})", 17f, new Vector2(0f, 40f), () => SelectDesign(index));
            button.label.alignment = TextAlignmentOptions.MidlineLeft;
            button.label.rectTransform.offsetMin = new Vector2(14f, 0f);
            button.label.color = UIKit.TextColor;
            button.background.color = index == selected ? UIBits.Selected : UIKit.RowColor;
        }
    }

    private void SelectDesign(int index)
    {
        selected = index;
        draft = designs[index].Clone();
        orderResult.text = "";
        RebuildDesignList();
        RebuildShipList();
        RefreshTotals();
    }

    private void NewDesign()
    {
        selected = -1;
        draft = new FleetDesign { name = $"Fleet {designs.Count + 1}" };
        RebuildDesignList();
        RebuildShipList();
        RefreshTotals();
    }

    private void SaveDesign()
    {
        if (draft.ShipCount == 0)
        {
            orderResult.text = "Add some ships to the design first.";
            return;
        }

        if (selected >= 0 && selected < designs.Count) designs[selected] = draft.Clone();
        else
        {
            designs.Add(draft.Clone());
            selected = designs.Count - 1;
        }
        FleetDesigns.Save(faction, designs);
        orderResult.text = $"Saved \"{draft.name}\".";
        RebuildDesignList();
    }

    private void DeleteDesign()
    {
        if (selected < 0 || selected >= designs.Count) return;
        designs.RemoveAt(selected);
        FleetDesigns.Save(faction, designs);
        selected = designs.Count > 0 ? 0 : -1;
        draft = selected >= 0 ? designs[0].Clone() : new FleetDesign();
        RebuildDesignList();
        RebuildShipList();
        RefreshTotals();
    }

    private void RebuildShipList()
    {
        nameField.SetTextWithoutNotify(draft.name);
        UIBits.Clear(shipList);
        shipRows.Clear();
        if (faction == null) return;

        foreach (Ship ship in faction.roster.Where(s => s != null))
        {
            RectTransform row = UIKit.Rect(ship.name, shipList, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 52f));
            UIKit.Image(row, UIKit.RowColor);
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = 52f;

            RectTransform iconRect = UIKit.Rect("Icon", row, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(8f, 0f), new Vector2(44f, 44f));
            Image icon = UIKit.Image(iconRect, Color.white);
            icon.sprite = ship.IconSprite;
            icon.preserveAspect = true;
            icon.enabled = icon.sprite != null;

            TMP_Text name = UIKit.Text(row, $"{ShipInfo.Name(ship)}\n<size=13><color=#{ColorUtility.ToHtmlStringRGBA(UIKit.MutedColor)}>${ship.cost}  ·  {ship.populationCost} MP  ·  {ShipInfo.Time(faction.BuildTime(ship))}</color></size>", 17f, UIKit.TextColor, TextAlignmentOptions.MidlineLeft);
            name.rectTransform.offsetMin = new Vector2(62f, 0f);
            name.rectTransform.offsetMax = new Vector2(-150f, 0f);

            RectTransform controls = UIKit.Rect("Count", row, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-8f, 0f), new Vector2(136f, 36f));
            HorizontalLayoutGroup layout = controls.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 4f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;

            Ship captured = ship;
            UIBits.Button(controls, "-", 20f, new Vector2(36f, 36f), () => Adjust(captured, -1));
            RectTransform countRect = UIKit.Rect("Value", controls, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(56f, 36f));
            countRect.gameObject.AddComponent<LayoutElement>().preferredWidth = 56f;
            TMP_Text count = UIKit.Text(countRect, "0", 20f, UIKit.TextColor, TextAlignmentOptions.Center);
            UIBits.Button(controls, "+", 20f, new Vector2(36f, 36f), () => Adjust(captured, 1));

            shipRows.Add((ship, count));
        }
        RefreshCounts();
    }

    private void Adjust(Ship ship, int delta)
    {
        int step = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) ? 5 : 1;
        draft.SetCount(ship, Mathf.Max(0, draft.Count(ship) + delta * step));
        RefreshCounts();
        RefreshTotals();
    }

    private void RefreshCounts()
    {
        foreach ((Ship ship, TMP_Text count) in shipRows)
        {
            int n = draft.Count(ship);
            count.text = n.ToString();
            count.color = n > 0 ? UIKit.Accent : UIKit.MutedColor;
        }
    }

    private void RefreshTotals()
    {
        if (faction == null) return;
        int credits = GalacticState.Instance != null ? GalacticState.Instance.GetCurrency(faction) : 0;
        int freeManpower = Manpower.Free(faction);
        string Warn(bool bad, string text) => bad ? $"<color=#FF7766>{text}</color>" : text;
        totals.text = $"{draft.ShipCount} ships   ·   {Warn(draft.Cost > credits, $"${draft.Cost:#,0}")} of ${credits:#,0}   ·   {Warn(draft.Manpower > freeManpower, $"{draft.Manpower} MP")} of {freeManpower} free";

        bool canOrder = draft.ShipCount > 0 && destination != null && (onlyYard != null || yards.Count > 0);
        orderButton.Set(onlyYard != null ? $"BUILD AT {onlyYard.planetName.ToUpperInvariant()}" : "ORDER FLEET", canOrder);
    }

    // ---------------- Ordering ----------------

    private void RefreshOrderControls()
    {
        string where = destination != null ? destination.planetName + (destination.hasCapitalShipyard ? "  (shipyard)" : "") : "none";
        destinationButton.Set($"DELIVER TO:  {where}   -   PICK ON MAP", true);

        List<string> yardOptions = new List<string> { $"All shipyards ({yards.Count})" };
        yardOptions.AddRange(yards.Select(y => y.planetName));
        yardCycler.SetOptions(yardOptions, onlyYard != null ? yards.IndexOf(onlyYard) + 1 : 0);
    }

    // Hide the window and let the player click the destination planet on the map; come back either way.
    private void PickDestinationOnMap()
    {
        GalacticMapManager map = GalacticMapManager.Instance;
        if (map == null) return;
        window.gameObject.SetActive(false);
        map.BeginPlanetPick("Click a planet to deliver to:", p => p.owner == faction, picked =>
        {
            destination = picked;
            if (!destinations.Contains(picked)) destinations.Add(picked);
            ReopenAfterPick();
        }, ReopenAfterPick);
    }

    private void ReopenAfterPick()
    {
        window.gameObject.SetActive(true);
        UIBits.ModalOpen = true;
        RefreshOrderControls();
        RefreshTotals();
    }

    private void Order()
    {
        if (faction == null || destination == null || draft.ShipCount == 0) return;

        List<Planet> building = onlyYard != null ? new List<Planet> { onlyYard } : null;
        ProductionOrders.Result result = ProductionOrders.Order(faction, draft, destination, building);

        string where = onlyYard != null ? onlyYard.planetName : $"{yards.Count} shipyard{(yards.Count == 1 ? "" : "s")}";
        orderResult.text = result.queued == result.requested
            ? $"Ordered {result.queued} ships across {where}, delivering to {destination.planetName}."
            : $"Ordered {result.queued} of {result.requested} ships - {result.stoppedBecause}.";
        RefreshYards();
        RefreshTotals();
    }

    // A standing order keeps the chosen destination topped up to the current design, for as long as you
    // hold the planet (see StandingOrders).
    private void SetStandingOrder()
    {
        if (destination == null || draft.ShipCount == 0)
        {
            orderResult.text = "Pick a destination and put some ships in the design first.";
            return;
        }
        StandingOrders.Set(destination, draft);
        orderResult.text = $"{destination.planetName} will be kept topped up to '{draft.name}' from every shipyard.";
        RefreshStanding();
    }

    private void ClearStandingOrder()
    {
        if (destination == null) return;
        StandingOrders.Clear(destination);
        orderResult.text = $"Standing order for {destination.planetName} cleared.";
        RefreshStanding();
    }

    private void RefreshStanding()
    {
        if (standingLabel == null) return;
        FleetDesign standing = StandingOrders.For(destination);
        standingLabel.text = standing == null ? "No standing order here"
            : $"Standing order: <color=#8EE59A>{standing.name}</color> ({standing.ShipCount} ships)";
    }

    private void RefreshYards()
    {
        RefreshStanding();
        if (faction == null) return;
        UIBits.Clear(yardList);

        foreach (Planet yard in ProductionOrders.Shipyards(faction))
        {
            RectTransform row = UIKit.Rect(yard.planetName, yardList, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 58f));
            UIKit.Image(row, yard == onlyYard ? UIBits.Selected : UIKit.RowColor);
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = 58f;

            List<ShipBuildOrder> queue = yard.shipBuildQueue;
            string muted = "#" + ColorUtility.ToHtmlStringRGBA(UIKit.MutedColor);
            string now = queue.Count == 0 ? $"<color={muted}>Idle</color>" : $"{ShipInfo.Name(queue[0].ship)}  {ShipInfo.Time(queue[0].remainingTime)}";
            string deliveries = string.Join(", ", queue.Where(o => !string.IsNullOrEmpty(o.deliverTo)).GroupBy(o => o.deliverTo).Select(g => $"{g.Count()} to {g.Key}"));
            string detail = queue.Count <= 1 ? "" : $"  ·  +{queue.Count - 1} queued, {ShipInfo.Time(ProductionOrders.QueueTime(yard))} total";
            if (deliveries.Length > 0) detail += $"  ·  {deliveries}";

            TMP_Text text = UIKit.Text(row, $"<b>{yard.planetName}</b>   {now}\n<size=13><color={muted}>{detail.TrimStart(' ', '·')}</color></size>", 16f, UIKit.TextColor, TextAlignmentOptions.MidlineLeft);
            text.rectTransform.offsetMin = new Vector2(12f, 0f);
            text.rectTransform.offsetMax = new Vector2(-8f, 0f);
            text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Ellipsis;
        }

        if (yardList.childCount == 0)
        {
            TMP_Text none = UIBits.Heading(yardList, "NO SHIPYARDS - BUILD A CAPITAL SHIPYARD FIRST");
            none.color = new Color(1f, 0.45f, 0.4f);
        }
    }

    // ---------------- Layout ----------------

    private void BuildHudButton(Transform canvas)
    {
        UIBits.PillButton button = UIBits.Button(canvas, "PRODUCTION  [P]", 16f, new Vector2(220f, 44f), () =>
        {
            if (IsOpen) Close();
            else Show(null, null);
        });
        RectTransform rect = (RectTransform)button.button.transform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1f, 1f);
        rect.anchoredPosition = new Vector2(-16f, -16f);
        button.background.color = UIKit.PanelColor;
    }

    private void BuildWindow(Transform canvas)
    {
        RectTransform shade = UIKit.Fill("Shade", canvas);
        UIKit.Image(shade, new Color(0f, 0f, 0f, 0.45f), true);
        window = shade;

        RectTransform panel = UIKit.Rect("Production", shade, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(Width, Height));
        UIKit.Image(panel, UIKit.PanelColor, true);
        RectTransform rule = UIKit.Rect("Rule", panel, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 2f));
        UIKit.Image(rule, UIKit.Accent);

        TMP_Text title = UIKit.Text(panel, "PRODUCTION", 28f, UIKit.TextColor, TextAlignmentOptions.TopLeft);
        title.characterSpacing = 10f;
        title.rectTransform.offsetMin = new Vector2(28f, 0f);
        title.rectTransform.offsetMax = new Vector2(0f, -18f);

        UIBits.PillButton close = UIBits.Button(panel, "CLOSE  [ESC]", 15f, new Vector2(150f, 38f), Close);
        RectTransform closeRect = (RectTransform)close.button.transform;
        closeRect.anchorMin = closeRect.anchorMax = closeRect.pivot = new Vector2(1f, 1f);
        closeRect.anchoredPosition = new Vector2(-20f, -16f);

        // Left: designs.
        RectTransform left = UIBits.Column(panel, 28f, 270f, 76f, 24f, 8f);
        UIBits.Heading(left, "FLEET DESIGNS");
        designList = UIBits.ScrollList(UIBits.Flexible(left, 200f), 6f);
        UIBits.Button(left, "NEW DESIGN", 15f, new Vector2(0f, 40f), NewDesign);
        UIBits.Button(left, "DELETE DESIGN", 15f, new Vector2(0f, 40f), DeleteDesign);

        // Centre: designer.
        RectTransform centre = UIBits.Column(panel, 322f, 470f, 76f, 24f, 8f);
        UIBits.Heading(centre, "DESIGNER");
        nameField = UIBits.InputField(centre, "Fleet name", 42f, value =>
        {
            draft.name = string.IsNullOrWhiteSpace(value) ? "Unnamed Fleet" : value.Trim();
        });
        shipList = UIBits.ScrollList(UIBits.Flexible(centre, 260f), 4f);
        totals = UIBits.Heading(centre, "");
        totals.characterSpacing = 0f;
        totals.fontSize = 15f;
        totals.richText = true;
        UIBits.Button(centre, "SAVE DESIGN", 15f, new Vector2(0f, 40f), SaveDesign);

        // Right: order + shipyards.
        RectTransform right = UIBits.Column(panel, 816f, 416f, 76f, 24f, 8f);
        UIBits.Heading(right, "ORDER");
        // Picked on the map, not from a list of names: the window steps aside, the player clicks a planet.
        destinationButton = UIBits.Button(right, "DELIVER TO", 15f, new Vector2(0f, 48f), PickDestinationOnMap);
        yardCycler = UIBits.CyclerRow(right, "BUILD AT", 48f, i =>
        {
            onlyYard = i > 0 && i - 1 < yards.Count ? yards[i - 1] : null;
            RefreshYards();
            RefreshTotals();
        });
        orderButton = UIBits.Button(right, "ORDER FLEET", 17f, new Vector2(0f, 48f), Order);

        RectTransform standingRow = UIKit.Rect("Standing", right, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 38f));
        standingRow.gameObject.AddComponent<LayoutElement>().preferredHeight = 38f;
        HorizontalLayoutGroup standingLayout = standingRow.gameObject.AddComponent<HorizontalLayoutGroup>();
        standingLayout.spacing = 6f;
        standingLayout.childControlWidth = true;
        standingLayout.childControlHeight = true;
        standingLayout.childForceExpandWidth = true;
        UIBits.Button(standingRow, "KEEP TOPPED UP", 14f, new Vector2(205f, 38f), SetStandingOrder);
        UIBits.Button(standingRow, "CLEAR STANDING", 14f, new Vector2(205f, 38f), ClearStandingOrder);
        standingLabel = UIBits.Heading(right, "");
        standingLabel.characterSpacing = 0f;
        standingLabel.fontSize = 13f;
        standingLabel.richText = true;
        orderResult = UIBits.Heading(right, "");
        orderResult.characterSpacing = 0f;
        orderResult.fontSize = 14f;
        orderResult.color = UIKit.TextColor;
        orderResult.textWrappingMode = TextWrappingModes.Normal;
        ((RectTransform)orderResult.transform.parent).GetComponent<LayoutElement>().preferredHeight = 40f;

        UIBits.Heading(right, "SHIPYARDS");
        yardList = UIBits.ScrollList(UIBits.Flexible(right, 160f), 4f);
    }
}
