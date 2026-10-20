using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.AI;

// What the interface says about a ship class: its display name, category, a one-line role and a stat sheet
// read straight off the prefab (cached), so new ships show up properly without any UI work.
public static class ShipInfo
{
    public enum Category { Capital, Escort, Carrier, StrikeCraft }

    public static readonly Category[] Categories = { Category.Capital, Category.Escort, Category.Carrier, Category.StrikeCraft };

    public class Sheet
    {
        public float hull;
        public float shields;
        public float firepower;
        public float range;
        public float speed;
        public int pointDefence;
        public string missiles;
        public int squadrons;
        public int craft;
    }

    private static readonly Dictionary<Ship, Sheet> sheets = new Dictionary<Ship, Sheet>();

    public static string Name(Ship ship)
    {
        if (ship == null) return "";
        if (!string.IsNullOrEmpty(ship.displayName)) return ship.displayName;

        string name = ship.name;
        int underscore = name.IndexOf('_');
        if (underscore >= 0) name = name.Substring(underscore + 1); // Drop the faction prefix.
        name = Regex.Replace(name, "(?<=[a-z])(?=[A-Z])", " ");
        return name == "Capital" ? "Capital Ship" : name;
    }

    public static Category CategoryOf(Ship ship)
    {
        switch (ship.fleetRole)
        {
            case FleetRole.Screen: return Category.Escort;
            case FleetRole.Carrier: return Category.Carrier;
            case FleetRole.Fighter:
            case FleetRole.Interceptor:
            case FleetRole.Bomber: return Category.StrikeCraft;
            default: return Category.Capital;
        }
    }

    public static string CategoryName(Category category)
    {
        switch (category)
        {
            case Category.Escort: return "Escorts";
            case Category.Carrier: return "Carriers";
            case Category.StrikeCraft: return "Strike Craft";
            default: return "Capital";
        }
    }

    public static string Role(Ship ship)
    {
        switch (ship.fleetRole)
        {
            case FleetRole.Screen:
                SpaceUnit unit = ship.GetComponent<SpaceUnit>();
                return unit != null && unit.shipType == ShipType.Corvette ? "Picket and anti-fighter screen" : "Escort with missiles";
            case FleetRole.Carrier: return "Launches fighter squadrons";
            case FleetRole.Fighter: return "Dogfighter";
            case FleetRole.Interceptor: return "Hunts enemy fighters";
            case FleetRole.Bomber: return "Strikes capital ships";
            default: return "Heavy warship of the battle line";
        }
    }

    public static Sheet Stats(Ship ship)
    {
        if (ship == null) return new Sheet();
        if (sheets.TryGetValue(ship, out Sheet cached)) return cached;

        GameObject prefab = ship.prefab;
        Sheet sheet = new Sheet();
        Fighter[] fighters = prefab.GetComponentsInChildren<Fighter>(true);

        if (fighters.Length > 0)
        {
            sheet.craft = fighters.Length;
            sheet.hull = fighters.Sum(f => f.GetComponent<HardpointHealth>() is HardpointHealth h ? h.maxHealth : 0f);
            sheet.firepower = fighters.Sum(f => f.shipDamage / Mathf.Max(0.05f, f.fireRate));
            sheet.range = fighters.Max(f => f.fireRange);
            sheet.speed = fighters.Max(f => f.speed);
        }
        else
        {
            sheet.hull = prefab.GetComponentsInChildren<HardpointHealth>(true).Sum(h => h.maxHealth);
            ShieldGenerator shield = prefab.GetComponent<ShieldGenerator>();
            if (shield != null)
            {
                sheet.hull *= shield.hullHealthMultiplier;
                sheet.shields = sheet.hull / Mathf.Max(0.01f, shield.hullHealthMultiplier) * shield.capacityFraction;
            }

            foreach (TurretController gun in prefab.GetComponentsInChildren<TurretController>(true))
            {
                if (gun.targetFilter == TurretController.TargetFilter.FightersOnly)
                {
                    sheet.pointDefence++;
                    continue;
                }
                sheet.firepower += gun.damage * gun.firingPoints.Count / Mathf.Max(0.05f, gun.fireRate);
                sheet.range = Mathf.Max(sheet.range, gun.range);
            }

            NavMeshAgent agent = prefab.GetComponent<NavMeshAgent>();
            if (agent != null) sheet.speed = agent.speed;

            SpaceUnit unit = prefab.GetComponent<SpaceUnit>();
            MissileArsenal arsenal = MissileArsenal.Instance;
            MissileArsenal.Warhead warhead = shield != null ? arsenal.plasma : arsenal.conventional;
            int salvo = unit != null ? warhead.SalvoFor(unit.shipType) : 0;
            if (salvo > 0) sheet.missiles = $"{salvo} {(shield != null ? "torpedoes" : "missiles")} / {warhead.reload:0}s";

            sheet.squadrons = prefab.GetComponentsInChildren<HangarHardpoint>(true).Sum(h => h.complement.Sum(bay => bay.count));
        }

        sheets[ship] = sheet;
        return sheet;
    }

    public static string Time(float seconds)
    {
        int s = Mathf.CeilToInt(seconds);
        return s >= 60 ? $"{s / 60}:{s % 60:00}" : $"{s}s";
    }
}
