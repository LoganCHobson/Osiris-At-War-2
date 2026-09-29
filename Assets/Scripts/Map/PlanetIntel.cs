using System.Collections.Generic;

public class PlanetIntel
{
    public class FleetSighting
    {
        public Faction faction;
        public int ships;
        public float power;
    }

    public float seenAt;
    public Faction owner;
    public bool hasTaxOffice;
    public bool hasCapitalShipyard;
    public bool hasBattleStation;
    public readonly List<FleetSighting> fleets = new List<FleetSighting>();

    public void Record(Planet planet, float time)
    {
        seenAt = time;
        owner = planet.owner;
        hasTaxOffice = planet.hasTaxOffice;
        hasCapitalShipyard = planet.hasCapitalShipyard;
        hasBattleStation = planet.hasBattleStation;

        fleets.Clear();
        for (int slot = 0; slot < Planet.FleetSlotCount; slot++)
        {
            GalacticFleet fleet = planet.GetFleetInSlot(slot);
            if (fleet == null || fleet.faction == null || !fleet.IsPresentAt(planet)) continue;

            FleetSighting sighting = fleets.Find(f => f.faction == fleet.faction);
            if (sighting == null)
            {
                sighting = new FleetSighting { faction = fleet.faction };
                fleets.Add(sighting);
            }

            sighting.ships += fleet.roster.Count;
            sighting.power += Strength.Of(fleet);
        }
    }
}
