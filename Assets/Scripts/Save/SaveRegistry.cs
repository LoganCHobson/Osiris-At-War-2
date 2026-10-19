using System.Collections.Generic;
using UnityEngine;

// Turns factions and ships into save-file names and back. Lists every faction (their rosters give the
// ships); lives at Resources/Save/SaveRegistry.
[CreateAssetMenu(fileName = "SaveRegistry", menuName = "Osiris/Save Registry")]
public class SaveRegistry : ScriptableObject
{
    public List<Faction> factions = new List<Faction>();

    private static SaveRegistry instance;
    private static Dictionary<string, Faction> factionsByName;
    private static Dictionary<string, Ship> shipsByName;

    private static SaveRegistry Instance
    {
        get
        {
            if (instance == null) instance = Resources.Load<SaveRegistry>("Save/SaveRegistry");
            return instance;
        }
    }

    public static string Id(Faction faction) => faction != null ? faction.name : null;
    public static string Id(Ship ship) => ship != null ? ship.name : null;

    public static Faction FindFaction(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        Build();
        if (factionsByName.TryGetValue(id, out Faction faction)) return faction;

        // Not registered - look through whatever factions are loaded (scene planets and fleets use them).
        foreach (Faction loaded in Resources.FindObjectsOfTypeAll<Faction>())
        {
            if (loaded.name == id)
            {
                factionsByName[id] = loaded;
                return loaded;
            }
        }
        Debug.LogWarning($"Save: unknown faction '{id}'.");
        return null;
    }

    public static Ship FindShip(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        Build();
        if (shipsByName.TryGetValue(id, out Ship ship)) return ship;

        foreach (Ship loaded in Resources.FindObjectsOfTypeAll<Ship>())
        {
            if (loaded.name == id && !loaded.gameObject.scene.IsValid()) // A prefab, not a live ship.
            {
                shipsByName[id] = loaded;
                return loaded;
            }
        }
        Debug.LogWarning($"Save: unknown ship '{id}'.");
        return null;
    }

    // Remembers factions/ships seen in the live game so they can be found again on load.
    public static void Remember(Faction faction)
    {
        if (faction == null) return;
        Build();
        factionsByName[faction.name] = faction;
        foreach (Ship ship in faction.roster) Remember(ship);
    }

    public static void Remember(Ship ship)
    {
        if (ship == null) return;
        Build();
        shipsByName[ship.name] = ship;
    }

    private static void Build()
    {
        if (factionsByName != null) return;

        factionsByName = new Dictionary<string, Faction>();
        shipsByName = new Dictionary<string, Ship>();
        if (Instance == null) return;

        foreach (Faction faction in Instance.factions)
        {
            if (faction == null) continue;
            factionsByName[faction.name] = faction;
            foreach (Ship ship in faction.roster)
            {
                if (ship != null) shipsByName[ship.name] = ship;
            }
        }
    }
}
