using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// How a planet's defenders are laid out when the battle opens. A formation is a set of slots on the
// battlefield, in the defender's own frame: x across the battlefield, z towards the enemy, in world units
// from the defender's start point (0,0 is roughly where the shipyard/station sits just behind). Each slot
// asks for a ship category (Capital, Escort, Carrier, Strike craft) or one specific ship class. Ships are
// matched to slots when the battle starts; anything left over falls back to the standard block.
[System.Serializable]
public class DefenseFormation
{
    [System.Serializable]
    public class Slot
    {
        public string category; // ShipInfo.Category name, used when ship is empty.
        public string ship;     // Optional: a specific ship class (SaveRegistry id).
        public float x;
        public float z;
        public float y; // Height above (+) or below (-) the battle plane.
    }

    public const string StandardName = "Standard";

    public string name = "New Layout";
    public List<Slot> slots = new List<Slot>();

    // Planner area, in the same units as the slots.
    public static readonly Rect Area = new Rect(-450f, -250f, 900f, 600f);

    public bool Accepts(Slot slot, Ship candidate)
    {
        if (!string.IsNullOrEmpty(slot.ship)) return SaveRegistry.Id(candidate) == slot.ship;
        return ShipInfo.CategoryOf(candidate).ToString() == slot.category;
    }

    public DefenseFormation Clone()
    {
        return new DefenseFormation
        {
            name = name,
            slots = slots.Select(s => new Slot { category = s.category, ship = s.ship, x = s.x, z = s.z, y = s.y }).ToList()
        };
    }

    // ---------------- Built-in layouts (also what the AI picks from) ----------------

    public static List<DefenseFormation> Presets()
    {
        return new List<DefenseFormation> { ForwardScreen(), Turtle(), WideLine(), Hammer(), Layered() };
    }

    public static DefenseFormation Preset(string name)
    {
        return Presets().Find(f => f.name == name);
    }

    private static void Row(DefenseFormation f, ShipInfo.Category category, int count, float z, float width)
    {
        for (int i = 0; i < count; i++)
        {
            float t = count == 1 ? 0.5f : i / (float)(count - 1);
            f.slots.Add(new Slot { category = category.ToString(), x = Mathf.Lerp(-width * 0.5f, width * 0.5f, t), z = z });
        }
    }

    private static void Arc(DefenseFormation f, ShipInfo.Category category, int count, float radius, float spreadDegrees, float centreZ)
    {
        for (int i = 0; i < count; i++)
        {
            float t = count == 1 ? 0.5f : i / (float)(count - 1);
            float angle = Mathf.Lerp(-spreadDegrees * 0.5f, spreadDegrees * 0.5f, t) * Mathf.Deg2Rad;
            f.slots.Add(new Slot { category = category.ToString(), x = Mathf.Sin(angle) * radius, z = centreZ + Mathf.Cos(angle) * radius });
        }
    }

    // Escorts out front to soak and catch strike craft, the battle line behind them, carriers well back.
    public static DefenseFormation ForwardScreen()
    {
        DefenseFormation f = new DefenseFormation { name = "Forward Screen" };
        Row(f, ShipInfo.Category.Escort, 10, 170f, 640f);
        Row(f, ShipInfo.Category.Capital, 8, 60f, 520f);
        Row(f, ShipInfo.Category.StrikeCraft, 8, 110f, 760f);
        Row(f, ShipInfo.Category.Carrier, 4, -90f, 360f);
        return f;
    }

    // Everything pulled in tight around the station/shipyard so its guns join the fight.
    public static DefenseFormation Turtle()
    {
        DefenseFormation f = new DefenseFormation { name = "Turtle" };
        Arc(f, ShipInfo.Category.Capital, 8, 150f, 150f, -40f);
        Arc(f, ShipInfo.Category.Escort, 10, 230f, 170f, -40f);
        Arc(f, ShipInfo.Category.StrikeCraft, 6, 90f, 120f, -40f);
        Row(f, ShipInfo.Category.Carrier, 4, -150f, 260f);
        return f;
    }

    // One long line to bring every gun to bear at once.
    public static DefenseFormation WideLine()
    {
        DefenseFormation f = new DefenseFormation { name = "Wide Line" };
        Row(f, ShipInfo.Category.Capital, 10, 80f, 820f);
        Row(f, ShipInfo.Category.Escort, 10, 140f, 860f);
        Row(f, ShipInfo.Category.StrikeCraft, 8, 40f, 700f);
        Row(f, ShipInfo.Category.Carrier, 4, -60f, 500f);
        return f;
    }

    // The battle line massed on one flank, escorts holding the other - for refusing a flank.
    public static DefenseFormation Hammer()
    {
        DefenseFormation f = new DefenseFormation { name = "Hammer" };
        void Add(ShipInfo.Category category, float x, float z) => f.slots.Add(new Slot { category = category.ToString(), x = x, z = z });

        for (int i = 0; i < 8; i++) Add(ShipInfo.Category.Capital, -300f + (i % 4) * 75f, 100f - (i / 4) * 85f);   // Massed left.
        for (int i = 0; i < 8; i++) Add(ShipInfo.Category.Escort, 120f + (i % 4) * 70f, 120f - (i / 4) * 70f);     // Holding right.
        for (int i = 0; i < 6; i++) Add(ShipInfo.Category.StrikeCraft, -260f + i * 50f, 190f);                     // Ahead of the hammer.
        for (int i = 0; i < 4; i++) Add(ShipInfo.Category.Carrier, -240f + i * 90f, -120f);
        return f;
    }

    // Stacked in three dimensions: escorts high to catch strike craft, the line in the middle, a second
    // line underneath so attackers who match one height are fired on from another, carriers low and back.
    public static DefenseFormation Layered()
    {
        DefenseFormation f = new DefenseFormation { name = "Layered" };
        void Add(ShipInfo.Category category, float x, float z, float y) => f.slots.Add(new Slot { category = category.ToString(), x = x, z = z, y = y });

        for (int i = 0; i < 8; i++) Add(ShipInfo.Category.Escort, -280f + i * 80f, 150f, 60f);
        for (int i = 0; i < 5; i++) Add(ShipInfo.Category.Capital, -240f + i * 120f, 70f, 0f);
        for (int i = 0; i < 4; i++) Add(ShipInfo.Category.Capital, -180f + i * 120f, 40f, -55f);
        for (int i = 0; i < 6; i++) Add(ShipInfo.Category.StrikeCraft, -200f + i * 80f, 110f, 30f);
        for (int i = 0; i < 4; i++) Add(ShipInfo.Category.Carrier, -180f + i * 120f, -100f, -40f);
        return f;
    }

    // What an AI defender opens with, judged on what it's fielding and whether it has a station to hide by.
    public static DefenseFormation ChooseForAI(IList<ShipEntry> ships, bool hasStationOrYard)
    {
        int total = Mathf.Max(1, ships.Count(s => s != null));
        float escorts = ships.Count(s => s != null && ShipInfo.CategoryOf(s) == ShipInfo.Category.Escort) / (float)total;
        float capitals = ships.Count(s => s != null && ShipInfo.CategoryOf(s) == ShipInfo.Category.Capital) / (float)total;

        if (hasStationOrYard) return Turtle();
        if (total >= 12 && escorts >= 0.2f && capitals >= 0.3f) return Layered();
        if (escorts >= 0.35f) return ForwardScreen();
        if (capitals >= 0.5f) return WideLine();
        return null; // The standard block suits a mixed fleet fine.
    }
}

// The player's saved layouts (kept across campaigns, per faction) and which one each planet uses (campaign
// state: saved with the game and kept across the switch to the battle scene).
public static class DefensePlans
{
    [System.Serializable]
    private class Store
    {
        public List<DefenseFormation> formations = new List<DefenseFormation>();
    }

    [System.Serializable]
    public class Assignment
    {
        public string planet;
        public string formation;
    }

    // planet name -> formation name. "*" is the default for planets without their own.
    public static readonly Dictionary<string, string> assignments = new Dictionary<string, string>();
    public const string AllPlanets = "*";

    private static string Key(Faction faction) => $"osiris.defenseLayouts.{SaveRegistry.Id(faction)}";

    public static List<DefenseFormation> LoadCustom(Faction faction)
    {
        if (faction == null) return new List<DefenseFormation>();
        string json = PlayerPrefs.GetString(Key(faction), "");
        Store store = string.IsNullOrEmpty(json) ? null : JsonUtility.FromJson<Store>(json);
        return store != null ? store.formations : new List<DefenseFormation>();
    }

    public static void SaveCustom(Faction faction, List<DefenseFormation> formations)
    {
        if (faction == null) return;
        PlayerPrefs.SetString(Key(faction), JsonUtility.ToJson(new Store { formations = formations }));
        PlayerPrefs.Save();
    }

    public static string AssignedName(string planetName)
    {
        if (!string.IsNullOrEmpty(planetName) && assignments.TryGetValue(planetName, out string own)) return own;
        return assignments.TryGetValue(AllPlanets, out string fallback) ? fallback : DefenseFormation.StandardName;
    }

    // The formation a planet's defenders use, or null for the standard block.
    public static DefenseFormation For(Faction faction, string planetName)
    {
        string name = AssignedName(planetName);
        if (string.IsNullOrEmpty(name) || name == DefenseFormation.StandardName) return null;
        return LoadCustom(faction).Find(f => f.name == name) ?? DefenseFormation.Preset(name);
    }

    public static void Assign(string planetName, string formationName)
    {
        if (string.IsNullOrEmpty(planetName)) return;
        if (planetName == AllPlanets)
        {
            // "Use everywhere" also replaces any per-planet choices.
            assignments.Clear();
        }
        assignments[planetName] = formationName;
    }

    public static List<Assignment> Capture()
    {
        return assignments.Select(a => new Assignment { planet = a.Key, formation = a.Value }).ToList();
    }

    public static void Restore(List<Assignment> saved)
    {
        assignments.Clear();
        if (saved == null) return;
        foreach (Assignment a in saved)
        {
            if (!string.IsNullOrEmpty(a.planet)) assignments[a.planet] = a.formation;
        }
    }
}
