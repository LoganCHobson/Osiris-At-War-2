using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

// One fleet in the fleet breakdown: the lead ship on its own, then identical ships condensed into stacks.
public class FleetRowUI : MonoBehaviour, IDropHandler
{
    public Transform iconContainer;
    public ShipIconDragHandler iconPrefab;

    public GalacticFleet Fleet { get; private set; }
    private FleetPanel panel;
    private Planet planet;
    private int slot;

    public void Bind(GalacticFleet fleet, Planet forPlanet, int forSlot, FleetPanel owningPanel)
    {
        Fleet = fleet;
        planet = forPlanet;
        slot = forSlot;
        panel = owningPanel;

        if (fleet == null || fleet.roster.Count == 0) return;

        Spawn(fleet.roster[0], new List<int> { 0 }, true);

        List<Ship> stackShips = new List<Ship>();
        List<List<int>> stackIndices = new List<List<int>>();
        for (int i = 1; i < fleet.roster.Count; i++)
        {
            int stack = stackShips.IndexOf(fleet.roster[i]);
            if (stack < 0)
            {
                stackShips.Add(fleet.roster[i]);
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

    private void Spawn(Ship ship, List<int> indices, bool isLead)
    {
        ShipIconDragHandler icon = Instantiate(iconPrefab, iconContainer);
        icon.Bind(ship, this, indices, isLead);
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
        if (targetFleet == null)
        {
            targetFleet = panel.CreateFleetInSlot(planet, slot);
            if (targetFleet == null) return;
        }

        if (sourceFleet.MoveShips(moving, targetFleet, index))
        {
            panel.Refresh();
        }
    }
}
