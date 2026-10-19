using System.Collections.Generic;

// A saved campaign, as plain data for JSON. Factions and ships are stored by asset name (Unity object
// references don't survive between sessions); planets and fleets by their names in the galactic map.
[System.Serializable]
public class SaveData
{
    public const int CurrentVersion = 1;

    public int version = CurrentVersion;
    public string displayName;
    public string savedAt;        // ISO 8601, local time.
    public string mapSignature;   // Detects saves from a different galaxy layout.
    public string playerFaction;
    public int difficulty;
    public float galacticTime;
    public float taxTimer;

    // Shown in the save list.
    public int playerPlanets;
    public int playerShips;
    public int playerCredits;

    public List<PlanetSave> planets = new List<PlanetSave>();
    public List<FleetSave> fleets = new List<FleetSave>();
    public List<TreasurySave> treasuries = new List<TreasurySave>();
    public List<IntelSave> playerIntel = new List<IntelSave>();
    public List<BrainSave> brains = new List<BrainSave>();
}

[System.Serializable]
public class PlanetSave
{
    public string name;
    public string owner;
    public bool hasTaxOffice;
    public bool hasCapitalShipyard;
    public bool hasBattleStation;
    public List<BuildSave> buildQueue = new List<BuildSave>();
}

[System.Serializable]
public class BuildSave
{
    public string ship;
    public float remainingTime;
}

[System.Serializable]
public class FleetSave
{
    public string name;
    public string faction;
    public string planet;
    public List<string> roster = new List<string>();
    public GalacticFleet.TravelState travel;
}

[System.Serializable]
public class TreasurySave
{
    public string faction;
    public int currency;
}

[System.Serializable]
public class IntelSave
{
    public string planet;
    public float seenAt;
    public string owner;
    public bool hasTaxOffice;
    public bool hasCapitalShipyard;
    public bool hasBattleStation;
    public List<SightingSave> fleets = new List<SightingSave>();
}

[System.Serializable]
public class SightingSave
{
    public string faction;
    public int ships;
    public float power;
}

// An AI empire's memory: who it hates, what it last saw, what it's attacking and what each fleet is doing.
[System.Serializable]
public class BrainSave
{
    public string faction;
    public float nextThinkAt;
    public List<GrievanceSave> grievances = new List<GrievanceSave>();
    public List<BrainIntelSave> intel = new List<BrainIntelSave>();
    public List<CampaignSave> campaigns = new List<CampaignSave>();
    public List<OrderSave> orders = new List<OrderSave>();
    public List<string> log = new List<string>();
}

[System.Serializable]
public class GrievanceSave
{
    public string faction;
    public float amount;
}

[System.Serializable]
public class BrainIntelSave
{
    public string planet;
    public float power;
    public string faction;
    public float seenAt;
}

[System.Serializable]
public class CampaignSave
{
    public string target;
    public string objectivePlanet;
    public string stagingPlanet;
    public float startedAt;
    public float powerLost;
    public float enemyPowerDestroyed;
    public int planetsTaken;
    public float score;
    public float requiredPower;
    public float stagedPower;
    public bool suspended;
}

[System.Serializable]
public class OrderSave
{
    public string fleet;
    public int kind;
    public string planet;
}

// What the save list shows for each file.
public class SaveSummary
{
    public string slot;
    public string path;
    public SaveData data;
    public System.DateTime savedAt;
    public bool IsAutosave => slot.StartsWith(SaveSystem.AutosavePrefix);
    public bool IsQuicksave => slot == SaveSystem.QuicksaveSlot;
}
