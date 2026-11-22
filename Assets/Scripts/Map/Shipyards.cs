using UnityEngine;

// Shipyard tiers. A yard is built as a Light Frigate Yard and upgraded in place; each tier builds everything
// the tiers below it do:
//   1  Light Frigate Yard   fighters (every strike craft squadron) and corvettes
//   2  Heavy Cruiser Yard   + destroyers and cruisers
//   3  Capital Shipyard     + carriers and battleships (the Avanti Capital among them)
// Planet.ShipyardTier says what a planet has. In battle a higher yard has more guns (its own model, per faction,
// Faction.ShipyardPrefab) and more hull (HealthScale), and counts for more (Strength).
public static class Shipyards
{
    public const int MaxTier = 3;
    // A yard from before tiers (a scene-placed yard or an old save) is a Heavy Cruiser Yard - the old yard model.
    public const int LegacyTier = 2;

    private static readonly string[] names = { "No shipyard", "Light Frigate Yard", "Heavy Cruiser Yard", "Capital Shipyard" };
    private static readonly string[] builds = { "", "Fighters, corvettes", "+ Destroyers, cruisers", "+ Carriers, battleships" };
    private static readonly int[] costs = { 0, 400, 600, 900 };     // to build tier 1, then to upgrade to 2, to 3
    private static readonly float[] healthScale = { 0f, 0.65f, 1f, 1.6f };

    public static string Name(int tier) => names[Mathf.Clamp(tier, 0, MaxTier)];
    public static string Builds(int tier) => builds[Mathf.Clamp(tier, 0, MaxTier)];
    public static int BuildCost => costs[1];
    // What it costs to go from this tier to the next (0 at the top).
    public static int UpgradeCost(int fromTier) => fromTier >= MaxTier ? 0 : costs[Mathf.Clamp(fromTier + 1, 1, MaxTier)];
    public static float HealthScale(int tier) => healthScale[Mathf.Clamp(tier, 1, MaxTier)];
    public static float Power(int tier) => tier <= 0 ? 0f : Strength.ShipyardPower * HealthScale(tier);

    // The yard tier a ship needs.
    public static int RequiredTier(Ship ship)
    {
        if (ship == null) return 1;
        if (ship.fleetRole == FleetRole.Fighter || ship.fleetRole == FleetRole.Interceptor || ship.fleetRole == FleetRole.Bomber) return 1;
        if (ship.prefab != null && ship.prefab.TryGetComponent(out SpaceUnit unit))
        {
            switch (unit.shipType)
            {
                case ShipType.Fighter:
                case ShipType.Corvette: return 1;
                case ShipType.Destroyer:
                case ShipType.Cruiser: return 2;
                case ShipType.Carrier:
                case ShipType.Battleship:
                case ShipType.Station: return 3;
            }
        }
        return ship.fleetRole == FleetRole.Carrier ? 3 : 2;
    }

    public static bool CanBuild(Planet planet, Ship ship) => planet != null && planet.ShipyardTier >= RequiredTier(ship);
}
