using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// Campaign-side heroes. When a campaign starts, each hero joins their faction aboard their flagship (in that
// faction's strongest fleet). While on the galactic map it keeps watch: a hero whose flagship is no longer
// in any fleet has fallen, and is gone for the rest of the campaign. Saved with the game.
public class HeroRoster : MonoBehaviour
{
    private const float CheckInterval = 2f;

    // Heroes who have joined the campaign, and those who have fallen (ids).
    public static readonly HashSet<string> joined = new HashSet<string>();
    public static readonly HashSet<string> fallen = new HashSet<string>();

    private float nextCheckAt;
    private bool placed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Hook()
    {
        SceneManager.sceneLoaded += (scene, _) => AttachTo(scene);
        AttachTo(SceneManager.GetActiveScene());
    }

    private static void AttachTo(Scene scene)
    {
        if (scene.name != "GalacticMap" || FindFirstObjectByType<HeroRoster>() != null) return;
        new GameObject("HeroRoster").AddComponent<HeroRoster>();
    }

    public static bool IsAlive(HeroDefinition hero) => hero != null && joined.Contains(hero.id) && !fallen.Contains(hero.id);

    public static void Clear()
    {
        joined.Clear();
        fallen.Clear();
    }

    public static (List<string> joined, List<string> fallen) Capture() => (new List<string>(joined), new List<string>(fallen));

    public static void Restore(List<string> joinedIds, List<string> fallenIds)
    {
        Clear();
        if (joinedIds != null) joined.UnionWith(joinedIds);
        if (fallenIds != null) fallen.UnionWith(fallenIds);
    }

    private void Update()
    {
        if (Time.time < nextCheckAt) return;
        nextCheckAt = Time.time + CheckInterval;

        // Give the map a moment to restore its fleets after a scene change before judging anyone.
        if (Time.timeSinceLevelLoad < 1f) return;

        if (!placed)
        {
            placed = true;
            PlaceNewHeroes();
        }
        CheckForFallen();
    }

    private static void PlaceNewHeroes()
    {
        foreach (HeroDefinition hero in HeroDefinition.All)
        {
            if (joined.Contains(hero.id) || fallen.Contains(hero.id)) continue;

            Ship type = SaveRegistry.FindShip(hero.shipClass);
            GalacticFleet fleet = StrongestFleetOf(hero.factionName);
            if (type == null || fleet == null) continue; // Faction not on this map (or already wiped out).

            ShipEntry flagship = new ShipEntry { type = type, shipName = hero.flagshipName, hero = hero.id };
            ShipNames.Reserve(flagship.shipName);
            fleet.ships.Insert(0, flagship); // Heroes lead their fleet.
            joined.Add(hero.id);

            if (fleet.faction != null && fleet.faction.isPlayerFaction)
            {
                Toast.Show($"{hero.title} {hero.name} has joined your fleet aboard the {hero.flagshipName}");
            }
        }
    }

    private static GalacticFleet StrongestFleetOf(string factionName)
    {
        GalacticFleet best = null;
        float bestPower = -1f;
        foreach (GalacticFleet fleet in GalacticFleet.All)
        {
            if (fleet == null || fleet.faction == null || fleet.faction.factionName != factionName || fleet.ships.Count == 0) continue;
            float power = Strength.Of(fleet);
            if (power > bestPower)
            {
                bestPower = power;
                best = fleet;
            }
        }
        return best;
    }

    private static void CheckForFallen()
    {
        if (BattleContext.Instance != null && BattleContext.Instance.hasPendingBattle) return; // Mid-battle hand-off.

        HashSet<string> present = new HashSet<string>();
        foreach (GalacticFleet fleet in GalacticFleet.All)
        {
            if (fleet == null) continue;
            foreach (ShipEntry ship in fleet.ships)
            {
                if (ship != null && ship.IsHero) present.Add(ship.hero);
            }
        }

        foreach (string id in joined)
        {
            if (fallen.Contains(id) || present.Contains(id)) continue;

            fallen.Add(id);
            HeroDefinition hero = HeroDefinition.Find(id);
            if (hero == null) continue;

            bool ours = hero.factionName == PlayerFactionName();
            Toast.Show(ours
                ? $"{hero.title} {hero.name} has fallen with the {hero.flagshipName}"
                : $"Enemy {hero.title.ToLowerInvariant()} {hero.name} has been killed - the {hero.flagshipName} is destroyed");
        }
    }

    private static string PlayerFactionName()
    {
        foreach (GalacticFleet fleet in GalacticFleet.All)
        {
            if (fleet != null && fleet.faction != null && fleet.faction.isPlayerFaction) return fleet.faction.factionName;
        }
        foreach (Planet planet in Planet.All)
        {
            if (planet != null && planet.owner != null && planet.owner.isPlayerFaction) return planet.owner.factionName;
        }
        return null;
    }
}
