using UnityEngine;

// One individual ship in a fleet: its class (the Ship prefab), its own name, and - for heroes - which
// hero commands it. Fleets, battles and saves carry these, so a ship keeps its name for its whole life
// and a hero's flagship is always the same ship.
//
// It converts implicitly to its class (Ship), so code that only cares what kind of ship it is - power,
// cost, role, the prefab to spawn - reads it as before. Creating one always goes through New(), so a new
// ship gets a fresh name and never silently takes another ship's identity.
[System.Serializable]
public class ShipEntry
{
    public Ship type;
    public string shipName;
    public string hero; // HeroDefinition id, or empty for an ordinary ship.

    public static ShipEntry New(Ship type, Faction faction = null)
    {
        return type == null ? null : new ShipEntry { type = type, shipName = ShipNames.Generate(type, faction) };
    }

    public static implicit operator Ship(ShipEntry entry) => entry != null ? entry.type : null;

    public bool IsHero => !string.IsNullOrEmpty(hero);
    public HeroDefinition Hero => IsHero ? HeroDefinition.Find(hero) : null;

    // The class's numbers (a hero's flagship counts for a little more).
    public float combatPower => type == null ? 0f : type.combatPower * (IsHero && Hero != null ? Hero.powerMultiplier : 1f);
    public int populationCost => type != null ? type.populationCost : 0;
    public int cost => type != null ? type.cost : 0;
    public float buildTime => type != null ? type.buildTime : 0f;
    public FleetRole fleetRole => type != null ? type.fleetRole : FleetRole.Line;
    public bool CanLead => type != null && type.CanLead;
    public GameObject prefab => type != null ? type.prefab : null;
    public Sprite IconSprite => type != null ? type.IconSprite : null;
    public GameObject icon => type != null ? type.icon : null;

    // "UNS Resolute (Battleship)" style label for menus.
    public string Label => string.IsNullOrEmpty(shipName) ? ShipInfo.Name(type) : $"{shipName}  ({ShipInfo.Name(type)})";

    public override string ToString() => Label;
}

// Ship names. Humans use naval-style names with the UNS prefix; the Avanti warlords' ships carry the
// harsh, oath-laden names of a martial empire, with a prefix per warlord. Names aren't reused while the
// game runs.
public static class ShipNames
{
    private static readonly string[] HumanNames =
    {
        "Resolute", "Valiant", "Defiant", "Dauntless", "Vigilant", "Indomitable", "Intrepid", "Relentless", "Steadfast",
        "Audacious", "Tenacious", "Formidable", "Invincible", "Implacable", "Undaunted", "Gallant", "Fearless", "Unyielding",
        "Thunderchild", "Warspite", "Lionheart", "Ironside", "Stormcrow", "Nightingale", "Kestrel", "Harrier", "Peregrine",
        "Sabre", "Rapier", "Claymore", "Halberd", "Bastion", "Rampart", "Bulwark", "Citadel", "Aegis", "Sentinel",
        "Vanguard", "Paladin", "Crusader", "Centurion", "Templar", "Ranger", "Pathfinder", "Wayfarer", "Endeavour",
        "Horizon", "Meridian", "Zenith", "Equinox", "Solstice", "Aurora", "Polaris", "Sirius", "Vega", "Rigel", "Altair",
        "Antares", "Orion", "Perseus", "Hercules", "Achilles", "Ajax", "Hector", "Leonidas", "Spartan", "Trojan",
        "Liberty", "Freedom", "Independence", "Concord", "Providence", "Hope", "Mercy", "Justice", "Retribution",
        "Reckoning", "Vengeance", "Fury", "Wrath", "Tempest", "Hurricane", "Typhoon", "Monsoon", "Avalanche", "Glacier",
        "Ardent", "Eager", "Swift", "Keen", "Bold", "Brave", "Stalwart", "Sturdy", "Constant", "Faithful", "Loyal",
        "New Dawn", "Far Shore", "Long Watch", "Earthlight", "Homecoming", "Second Sun", "Last Light", "Open Road"
    };

    private static readonly string[] AvantiFirst =
    {
        "Ash", "Iron", "Black", "Crimson", "Silent", "Burning", "Hollow", "Grave", "Storm", "Blood", "Bone", "Ember",
        "Shadow", "Thorn", "Cinder", "Night", "Red", "Pale", "Broken", "Endless", "Sacred", "Last", "Falling", "Rising"
    };

    private static readonly string[] AvantiSecond =
    {
        "Oath", "Blade", "Crown", "Spear", "Tide", "Wind", "Fang", "Vow", "Talon", "Banner", "Hymn", "Pyre", "Dirge",
        "Moon", "Sun", "Star", "Throne", "Shield", "Wake", "Storm", "Lance", "Chain", "Gate", "Mountain", "River"
    };

    private static readonly System.Collections.Generic.HashSet<string> used = new System.Collections.Generic.HashSet<string>();

    public static string Generate(Ship type, Faction faction)
    {
        // The ship's own build decides its naming, not who controls it (an AI-run human fleet is still human).
        bool human = type != null ? type.name.StartsWith("Human") : faction != null && faction.isPlayerFaction;
        for (int attempt = 0; attempt < 40; attempt++)
        {
            string name = human ? HumanName(attempt) : AvantiName(faction, attempt);
            if (used.Add(name)) return name;
        }
        string fallback = (human ? "UNS " : "AWS ") + Random.Range(100, 9999);
        used.Add(fallback);
        return fallback;
    }

    // Names loaded from a save are taken, so new ships don't repeat them.
    public static void Reserve(string name)
    {
        if (!string.IsNullOrEmpty(name)) used.Add(name);
    }

    public static void ResetForNewCampaign() => used.Clear();

    private static string HumanName(int attempt)
    {
        string name = HumanNames[Random.Range(0, HumanNames.Length)];
        return attempt < 20 ? $"UNS {name}" : $"UNS {name} {Roman(attempt / 10 + 1)}";
    }

    private static string AvantiName(Faction faction, int attempt)
    {
        string name = $"{AvantiFirst[Random.Range(0, AvantiFirst.Length)]} {AvantiSecond[Random.Range(0, AvantiSecond.Length)]}";
        return $"{AvantiPrefix(faction)} {name}" + (attempt >= 30 ? $" {Roman(attempt / 10)}" : "");
    }

    // Each warlord marks its ships differently.
    private static string AvantiPrefix(Faction faction)
    {
        string id = faction != null ? faction.factionName : "";
        if (id.Contains("Kesh")) return "KDS";      // Kesh Dominion Shrine-ship
        if (id.Contains("Severed")) return "SHV";   // Severed Hand Vessel
        if (id.Contains("Thessary")) return "TCM";  // Thessary Concord Merchantman
        if (id.Contains("Vorrak")) return "VCR";    // Vorrak Clan Raider
        if (id.Contains("Ashcrown")) return "ALP";  // Ashcrown Legion Pyre-ship
        if (id.Contains("Neutral")) return "AHV";   // Avanti Holdout Vessel
        return "AIS";                                // Avanti Imperial Ship
    }

    private static string Roman(int n)
    {
        string[] numerals = { "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X" };
        return numerals[Mathf.Clamp(n - 1, 0, numerals.Length - 1)];
    }
}
