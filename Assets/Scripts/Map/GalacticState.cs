using System.Collections.Generic;
using UnityEngine;

public class GalacticState : MonoBehaviour
{
    public static GalacticState Instance { get; private set; }

    [System.Serializable]
    public class PlanetSnapshot
    {
        public string planetName;
        public Faction owner;
        public bool hasTaxOffice;
        public bool hasCapitalShipyard;
        public bool hasBattleStation;
        public List<ShipBuildOrder> shipBuildQueue = new List<ShipBuildOrder>();
    }

    [System.Serializable]
    public class FleetSnapshot
    {
        public string fleetName;
        public Faction faction;
        public string planetName;
        public List<Ship> roster = new List<Ship>();
    }

    [System.Serializable]
    public class TreasurySnapshot
    {
        public Faction faction;
        public int currency;
    }

    public bool initialized;
    public List<PlanetSnapshot> planetSnapshots = new List<PlanetSnapshot>();
    public List<FleetSnapshot> fleetSnapshots = new List<FleetSnapshot>();
    public List<TreasurySnapshot> treasuries = new List<TreasurySnapshot>();
    public readonly Dictionary<string, PlanetIntel> playerIntel = new Dictionary<string, PlanetIntel>();

    [Header("Economy")]
    public int startingCurrency = 500;
    public int currencyCap = 10000;

    [Header("Fleet Reconstruction")]
    public GameObject fleetPrefab;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public void CaptureFromScene()
    {
        planetSnapshots.Clear();
        foreach (Planet planet in FindObjectsByType<Planet>(FindObjectsSortMode.None))
        {
            planetSnapshots.Add(new PlanetSnapshot
            {
                planetName = planet.planetName,
                owner = planet.owner,
                hasTaxOffice = planet.hasTaxOffice,
                hasCapitalShipyard = planet.hasCapitalShipyard,
                hasBattleStation = planet.hasBattleStation,
                shipBuildQueue = planet.shipBuildQueue.ConvertAll(o => new ShipBuildOrder { ship = o.ship, remainingTime = o.remainingTime })
            });
        }

        fleetSnapshots.Clear();
        foreach (GalacticFleet fleet in FindObjectsByType<GalacticFleet>(FindObjectsSortMode.None))
        {
            if (fleet.currentPlanet == null) continue;

            fleetSnapshots.Add(new FleetSnapshot
            {
                fleetName = fleet.gameObject.name,
                faction = fleet.faction,
                planetName = fleet.currentPlanet.planetName,
                roster = new List<Ship>(fleet.roster)
            });
        }

        initialized = true;
    }

    public void ApplyToScene()
    {
        if (!initialized) return;

        Dictionary<string, Planet> planetsByName = new Dictionary<string, Planet>();
        foreach (Planet planet in FindObjectsByType<Planet>(FindObjectsSortMode.None))
        {
            planetsByName[planet.planetName] = planet;
        }

        foreach (PlanetSnapshot snapshot in planetSnapshots)
        {
            if (planetsByName.TryGetValue(snapshot.planetName, out Planet planet))
            {
                planet.SetOwnership(snapshot.owner);
                planet.hasTaxOffice = snapshot.hasTaxOffice;
                planet.hasCapitalShipyard = snapshot.hasCapitalShipyard;
                planet.hasBattleStation = snapshot.hasBattleStation;
                planet.shipBuildQueue = snapshot.shipBuildQueue.ConvertAll(o => new ShipBuildOrder { ship = o.ship, remainingTime = o.remainingTime });
            }
        }

        Dictionary<string, GalacticFleet> fleetsByName = new Dictionary<string, GalacticFleet>();
        foreach (GalacticFleet fleet in FindObjectsByType<GalacticFleet>(FindObjectsSortMode.None))
        {
            fleetsByName[fleet.gameObject.name] = fleet;
        }

        HashSet<string> keptFleets = new HashSet<string>();

        foreach (FleetSnapshot snapshot in fleetSnapshots)
        {
            keptFleets.Add(snapshot.fleetName);

            if (!planetsByName.TryGetValue(snapshot.planetName, out Planet planet)) continue;

            if (!fleetsByName.TryGetValue(snapshot.fleetName, out GalacticFleet fleet))
            {
                fleet = SpawnFleet(snapshot.fleetName, planet);
                if (fleet == null) continue;
                fleetsByName[snapshot.fleetName] = fleet;
            }

            fleet.roster = new List<Ship>(snapshot.roster);
            fleet.faction = snapshot.faction;
            fleet.PlaceAt(planet);
        }

        foreach (KeyValuePair<string, GalacticFleet> entry in fleetsByName)
        {
            if (!keptFleets.Contains(entry.Key))
            {
                Destroy(entry.Value.gameObject);
            }
        }
    }

    private GalacticFleet SpawnFleet(string fleetName, Planet planet)
    {
        if (fleetPrefab == null)
        {
            Debug.LogWarning($"GalacticState has no fleetPrefab assigned - can't recreate fleet '{fleetName}'.");
            return null;
        }

        GameObject obj = Instantiate(fleetPrefab, planet.transform.position, Quaternion.identity);
        obj.name = fleetName;

        GalacticFleet fleet = obj.GetComponent<GalacticFleet>();
        if (fleet == null)
        {
            Destroy(obj);
            return null;
        }

        return fleet;
    }

    public int GetCurrency(Faction faction)
    {
        if (faction == null) return 0;
        return FindTreasury(faction).currency;
    }

    public bool TrySpend(Faction faction, int amount)
    {
        if (faction == null) return false;

        TreasurySnapshot treasury = FindTreasury(faction);
        if (treasury.currency < amount) return false;

        treasury.currency -= amount;
        return true;
    }

    public void AddCurrency(Faction faction, int amount)
    {
        if (faction == null) return;

        TreasurySnapshot treasury = FindTreasury(faction);
        treasury.currency = Mathf.Min(treasury.currency + amount, currencyCap);
    }

    private TreasurySnapshot FindTreasury(Faction faction)
    {
        TreasurySnapshot treasury = treasuries.Find(t => t.faction == faction);
        if (treasury == null)
        {
            treasury = new TreasurySnapshot { faction = faction, currency = startingCurrency };
            treasuries.Add(treasury);
        }
        return treasury;
    }
}
