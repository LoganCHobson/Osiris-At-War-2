using UnityEngine;
using UnityEngine.EventSystems;

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

        if (fleet == null) return;

        foreach (Ship ship in fleet.roster)
        {
            ShipIconDragHandler icon = Instantiate(iconPrefab, iconContainer);
            icon.Bind(ship, this);
        }
    }

    public void OnDrop(PointerEventData eventData)
    {
        if (eventData.pointerDrag == null) return;

        ShipIconDragHandler dragged = eventData.pointerDrag.GetComponent<ShipIconDragHandler>();
        if (dragged == null || dragged.SourceRow.Fleet == null) return;

        GalacticFleet sourceFleet = dragged.SourceRow.Fleet;
        GalacticFleet targetFleet = Fleet;

        if (targetFleet == null)
        {
            targetFleet = panel.CreateFleetInSlot(planet, slot);
            if (targetFleet == null) return;
        }

        if (sourceFleet.TransferShipTo(dragged.Ship, targetFleet))
        {
            panel.Refresh();
        }
    }
}
