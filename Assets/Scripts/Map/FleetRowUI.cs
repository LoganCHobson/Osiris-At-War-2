using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// One fleet lane in the fleet breakdown: a header (fleet, owner, size, strength, what it's doing), then the
// lead ship on its own and identical ships condensed into stacks. Ships can be dropped anywhere on the lane;
// an empty lane takes them as a new fleet.
public class FleetRowUI : MonoBehaviour, IDropHandler
{
    public GalacticFleet Fleet { get; private set; }

    private FleetPanel panel;
    private Planet planet;
    private int slot;
    private RectTransform tiles;
    private TMP_Text status;
    private float nextStatusAt;

    public static FleetRowUI Create(Transform parent, GalacticFleet fleet, Planet forPlanet, int forSlot, FleetPanel owningPanel, bool canSplitInto)
    {
        RectTransform lane = UIKit.Rect($"Lane {forSlot + 1}", parent, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
        lane.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        UIKit.Image(lane, new Color(0f, 0f, 0f, 0.01f), true); // Catches drops on the lane's empty space.

        FleetRowUI row = lane.gameObject.AddComponent<FleetRowUI>();
        row.Fleet = fleet;
        row.planet = forPlanet;
        row.slot = forSlot;
        row.panel = owningPanel;
        row.Build(lane, canSplitInto);
        return row;
    }

    private void Build(RectTransform lane, bool canSplitInto)
    {
        TMP_Text heading = UIKit.Text(lane, $"FLEET {slot + 1}", 15f, UIKit.MutedColor, TextAlignmentOptions.TopLeft);
        heading.characterSpacing = 8f;
        heading.rectTransform.offsetMin = new Vector2(20f, 0f);
        heading.rectTransform.offsetMax = new Vector2(-20f, -14f);

        if (Fleet == null || Fleet.roster.Count == 0)
        {
            TMP_Text empty = UIKit.Text(lane, canSplitInto ? "Empty lane\n<size=15><color=#FFFFFF66>Drag ships here to form a new fleet</color></size>" : "Empty lane",
                                        19f, UIKit.MutedColor, TextAlignmentOptions.Center);
            empty.rectTransform.offsetMax = new Vector2(0f, -20f);
            return;
        }

        Faction faction = Fleet.faction;
        TMP_Text owner = UIKit.Text(lane, faction == null ? "" : (string.IsNullOrEmpty(faction.factionName) ? faction.name : faction.factionName).ToUpperInvariant(),
                                    14f, faction != null ? Color.Lerp(faction.color, Color.white, 0.25f) : UIKit.MutedColor, TextAlignmentOptions.TopRight);
        owner.characterSpacing = 4f;
        owner.rectTransform.offsetMin = new Vector2(20f, 0f);
        owner.rectTransform.offsetMax = new Vector2(-20f, -15f);

        int ships = Fleet.roster.Count(s => s != null);
        float strength = Fleet.roster.Where(s => s != null).Sum(s => s.combatPower);
        TMP_Text summary = UIKit.Text(lane, $"{ships} ship{(ships == 1 ? "" : "s")}  <color=#FFFFFF59>·</color>  {strength:0} strength", 18f, UIKit.TextColor, TextAlignmentOptions.TopLeft);
        summary.richText = true;
        summary.rectTransform.offsetMin = new Vector2(20f, 0f);
        summary.rectTransform.offsetMax = new Vector2(-20f, -38f);

        status = UIKit.Text(lane, Status(), 15f, UIKit.MutedColor, TextAlignmentOptions.TopRight);
        status.rectTransform.offsetMin = new Vector2(20f, 0f);
        status.rectTransform.offsetMax = new Vector2(-20f, -40f);

        RectTransform viewport = UIKit.Rect("Ships", lane, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        viewport.offsetMin = new Vector2(16f, 12f);
        viewport.offsetMax = new Vector2(-16f, -72f);
        viewport.gameObject.AddComponent<RectMask2D>();

        tiles = UIKit.Rect("Tiles", viewport, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, Vector2.zero);
        GridLayoutGroup grid = tiles.gameObject.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(ShipIconDragHandler.Width, ShipIconDragHandler.Height);
        grid.spacing = new Vector2(8f, 8f);
        tiles.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        ScrollRect scroll = viewport.gameObject.AddComponent<ScrollRect>();
        scroll.content = tiles;
        scroll.viewport = viewport;
        scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 30f;

        Spawn(Fleet.roster[0], new List<int> { 0 }, true);

        List<Ship> stackShips = new List<Ship>();
        List<List<int>> stackIndices = new List<List<int>>();
        for (int i = 1; i < Fleet.roster.Count; i++)
        {
            int stack = stackShips.IndexOf(Fleet.roster[i]);
            if (stack < 0)
            {
                stackShips.Add(Fleet.roster[i]);
                stackIndices.Add(new List<int>());
                stack = stackShips.Count - 1;
            }
            stackIndices[stack].Add(i);
        }

        for (int s = 0; s < stackShips.Count; s++)
        {
            Spawn(stackShips[s], stackIndices[s], false);
        }
    }

    private void Update()
    {
        if (status == null || Fleet == null || Time.unscaledTime < nextStatusAt) return;
        nextStatusAt = Time.unscaledTime + 0.5f;
        status.text = Status();
    }

    private string Status()
    {
        if (Fleet.IsTraveling) return Fleet.Destination != null ? $"Under way to {Fleet.Destination.planetName}" : "Under way";
        if (Fleet.AwaitingBattle) return "Battle pending";
        if (Fleet.IsHolding) return $"Regrouping  {ShipInfo.Time(Fleet.HoldTimeRemaining)}";
        return "In orbit";
    }

    private void Spawn(Ship ship, List<int> indices, bool isLead)
    {
        if (ship == null) return;
        ShipIconDragHandler.Create(tiles, ship, this, indices, isLead);
    }

    // Dropped on the row's empty space - the ships join at the back of the line.
    public void OnDrop(PointerEventData eventData)
    {
        if (eventData.pointerDrag == null) return;

        ShipIconDragHandler dragged = eventData.pointerDrag.GetComponent<ShipIconDragHandler>();
        if (dragged != null)
        {
            PlaceShips(dragged, null);
        }
    }

    // Moves the dragged ship (or the whole stack, with Shift held) to sit in front of the target icon,
    // or at the back of the fleet when target is null. Dropping onto the lead slot swaps in a new lead.
    public void PlaceShips(ShipIconDragHandler dragged, ShipIconDragHandler target)
    {
        if (dragged == null || dragged.SourceRow == null || dragged.SourceRow.Fleet == null) return;

        GalacticFleet sourceFleet = dragged.SourceRow.Fleet;
        bool wholeStack = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        List<int> moving = dragged.TakeIndices(wholeStack && (target == null || !target.IsLead));
        int index = target == null ? -1 : target.FirstIndex;

        GalacticFleet targetFleet = Fleet;
        bool newFleet = targetFleet == null;
        if (newFleet)
        {
            targetFleet = panel.CreateFleetInSlot(planet, slot);
            if (targetFleet == null) return;
            targetFleet.faction = sourceFleet.faction; // The template's faction may be anyone's.
        }

        if (sourceFleet.MoveShips(moving, targetFleet, index))
        {
            panel.Refresh();
        }
        else if (newFleet)
        {
            planet.ReleaseSlot(targetFleet); // Move refused: don't leave an empty fleet behind.
            Destroy(targetFleet.gameObject);
        }
    }
}
