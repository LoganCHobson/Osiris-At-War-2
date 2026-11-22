using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

// Dev test: starts a campaign, sends a fleet on a multi-jump trip, saves mid-jump, scrambles the galaxy,
// loads, and checks planets, fleets (including the one in flight), money, clock and AI state all came
// back. Started by SaveRoundTripTestRunner (Osiris > Test Save Round Trip); writes a report and quits.
public class SaveRoundTripTest : MonoBehaviour
{
    public string reportPath;

    private const string Slot = "roundtrip_test";
    private readonly StringBuilder report = new StringBuilder();
    private int failures;

    private IEnumerator Start()
    {
        DontDestroyOnLoad(gameObject);
        yield return Wait(3f);

        GalacticFleet traveller = GalacticFleet.All.FirstOrDefault(fleet => fleet.faction != null && fleet.faction.isPlayerFaction && fleet.CanTakeOrders);
        Planet far = null;
        if (traveller != null)
        {
            far = Planet.All.Where(planet => planet != traveller.currentPlanet)
                .Select(planet => (planet, route: traveller.PlanRoute(planet)))
                .Where(pair => pair.route.Count >= 4)
                .Select(pair => pair.planet)
                .FirstOrDefault();
            if (far != null) traveller.TrySetDestination(far);
        }
        Check(traveller != null && far != null && traveller.IsTraveling, "a player fleet set off on a multi-jump trip");
        yield return Wait(2.5f);

        // Spy probes: one parked over an enemy world with its clock running, one in flight, one being built.
        Planet home = Planet.All.FirstOrDefault(p => p.owner != null && p.owner.isPlayerFaction);
        Planet enemy = Planet.All.FirstOrDefault(p => p.owner != null && !p.owner.isPlayerFaction && !p.owner.isNeutral);
        string playerId = SaveRegistry.Id(home != null ? home.owner : null);
        SpyProbes.Probe parked = new SpyProbes.Probe { faction = playerId, planet = enemy?.planetName, lifeLeft = 150f, Position = enemy != null ? enemy.PlanetSpotPosition : Vector3.zero };
        SpyProbes.Probe flying = new SpyProbes.Probe { faction = playerId, planet = home?.planetName, Position = home != null ? home.PlanetSpotPosition : Vector3.zero };
        SpyProbes.All.Add(parked);
        SpyProbes.All.Add(flying);
        bool sent = SpyProbes.Send(flying, far ?? enemy);
        if (home != null) SpyProbes.Enqueue(home);
        yield return Wait(0.5f);
        Check(sent && !flying.Parked, "a spy probe set off");
        Vector3 flyingAt = flying.Position;
        List<string> flyingRoute = new List<string>(flying.route);
        float parkedLife = parked.lifeLeft;
        float orderLeft = SpyProbes.Orders.Where(o => o.faction == playerId).Select(o => o.remaining).DefaultIfEmpty(-1f).Max();

        GameSpeed.SetSuspended(true); // Freeze the galaxy across save and load, so the two snapshots compare exactly.
        string before = Snapshot();
        Vector3 travellerPosition = traveller != null ? traveller.transform.position : Vector3.zero;
        float clock = AIDirector.Instance != null ? AIDirector.Instance.GalacticTime : 0f;
        int brains = AIDirector.Instance != null ? AIDirector.Instance.brains.Count : 0;

        float savedAt = Time.realtimeSinceStartup;
        string travellerName = traveller != null ? traveller.name : null; // The objects themselves are gone after the load.
        string farName = far != null ? far.planetName : null;
        string enemyName = enemy != null ? enemy.planetName : null;
        string homeName = home != null ? home.planetName : null;
        Check(SaveSystem.Save(Slot, "Round trip test", out string error), "saved (" + error + ")");

        // Scramble things so a load that did nothing would be caught.
        foreach (Planet planet in Planet.All) planet.hasTaxOffice = !planet.hasTaxOffice;
        if (traveller != null) traveller.ships.Clear();
        SpyProbes.All.Clear();
        SpyProbes.Orders.Clear();

        // The new map's first frames run before anything can pause them: a fleet a hair from a planet can
        // arrive and take it. That's play, not the save, so planets taken then are left out of the comparison.
        List<string> capturedAfterLoad = new List<string>();
        System.Action<Planet, Faction, Faction> onCaptured = (planet, from, to) => capturedAfterLoad.Add(planet.planetName);
        GalacticEvents.PlanetCaptured += onCaptured;
        Check(SaveSystem.Load(Slot, out error), "load started (" + error + ")");
        // Loading (and the map starting) resets the game speed: freeze again the moment the new map is up.
        GalacticMapManager oldMap = GalacticMapManager.Instance;
        for (float waited = 0f; waited < 15f && (GalacticMapManager.Instance == null || GalacticMapManager.Instance == oldMap); waited += Time.unscaledDeltaTime) yield return null;
        yield return null;
        GameSpeed.SetSuspended(true);
        yield return Wait(3f);

        GalacticFleet restored = travellerName != null ? GalacticFleet.All.FirstOrDefault(fleet => fleet.name == travellerName) : null;
        GalacticEvents.PlanetCaptured -= onCaptured;
        string after = Snapshot();
        foreach (string taken in capturedAfterLoad)
        {
            report.AppendLine($"note {taken} was captured in the first frames after the load (left out of the comparison)");
            before = WithoutPlanet(before, taken);
            after = WithoutPlanet(after, taken);
        }

        Check(restored != null, "travelling fleet exists after load");
        Check(restored != null && restored.IsTraveling, "travelling fleet is still mid-trip");
        Check(restored != null && restored.Destination != null && restored.Destination.planetName == farName, "travelling fleet still heading to " + (farName ?? "?"));
        float fleetFlown = 5f * (Time.realtimeSinceStartup - savedAt) + 5f; // It keeps flying after the load.
        Check(restored != null && Vector3.Distance(restored.transform.position, travellerPosition) < fleetFlown, "travelling fleet picked up near where it was saved");
        Check(AIDirector.Instance != null && Mathf.Abs(AIDirector.Instance.GalacticTime - clock) < 10f, $"galactic clock restored ({clock:F0}s)");
        Check(AIDirector.Instance != null && AIDirector.Instance.brains.Count >= brains, $"AI empires restored ({brains})");
        Check(Strip(before) == Strip(after), "planets, fleets and money match the save");

        SpyProbes.Probe parkedAfter = SpyProbes.All.FirstOrDefault(p => p.Parked && p.planet == enemyName);
        enemy = SpyProbes.PlanetNamed(enemyName); // The reloaded scene's planets.
        Faction player = SpyProbes.Player;
        SpyProbes.Probe flyingAfter = SpyProbes.All.FirstOrDefault(p => !p.Parked && p.faction == playerId);
        int playerProbes = SpyProbes.All.Count(p => p.faction == playerId);
        Check(playerProbes == 2, $"both spy probes restored ({playerProbes})");
        Check(parkedAfter != null && parkedAfter.faction == playerId, "parked probe back over " + enemyName);
        Check(parkedAfter != null && parkedAfter.lifeLeft <= parkedLife && parkedAfter.lifeLeft > parkedLife - 15f, $"parked probe's clock carried over ({parkedLife:0}s -> {parkedAfter?.lifeLeft:0}s)");
        Check(flyingAfter != null && flyingRoute.Count > 0 && flyingAfter.route.Last() == flyingRoute.Last(), "flying probe still heading to " + (flyingRoute.Count > 0 ? flyingRoute.Last() : "?"));
        // It keeps flying after the load (2.5x fleet speed for the seconds since the save), so allow for that.
        float flown = 5f * SpyProbes.SpeedMultiplier * (Time.realtimeSinceStartup - savedAt) + 5f;
        Check(flyingAfter != null && Vector3.Distance(flyingAfter.Position, flyingAt) < flown, $"flying probe picked up near where it was saved ({(flyingAfter != null ? Vector3.Distance(flyingAfter.Position, flyingAt) : -1f):0} of {flown:0} allowed)");
        Check(SpyProbes.Orders.Exists(o => o.faction == playerId && o.planet == homeName && o.remaining <= orderLeft), "probe in production restored");
        Check(enemy != null && SpyProbes.Watching(player, enemy), "restored probe is watching " + enemy?.planetName);
        yield return Wait(1.5f); // The fog of war refreshes on its own clock.
        Check(enemy != null && FogOfWar.CanSee(enemy), "restored probe gives vision of " + enemy?.planetName);
        if (Strip(before) != Strip(after))
        {
            report.AppendLine("BEFORE:\n" + before);
            report.AppendLine("AFTER:\n" + after);
        }

        GameSpeed.SetSuspended(false);
        SaveSystem.Delete(Slot);
        report.Insert(0, failures == 0 ? "SAVE ROUND TRIP: PASS\n" : $"SAVE ROUND TRIP: {failures} FAILURE(S)\n");
        Debug.Log(report.ToString());
        if (!string.IsNullOrEmpty(reportPath)) File.WriteAllText(reportPath, report.ToString());

#if UNITY_EDITOR
        if (Application.isBatchMode) UnityEditor.EditorApplication.Exit(0);
        else UnityEditor.EditorApplication.isPlaying = false; // Run from the menu: just stop playing.
#endif
    }

    private void Check(bool ok, string what)
    {
        report.AppendLine($"{(ok ? "ok  " : "FAIL")} {what}");
        if (!ok) failures++;
    }

    // Planets, parked fleets and treasuries as text (travellers' positions are checked separately).
    private static string Snapshot()
    {
        StringBuilder text = new StringBuilder();
        foreach (Planet planet in Planet.All.OrderBy(p => p.planetName))
        {
            text.AppendLine($"{planet.planetName}: {SaveRegistry.Id(planet.owner)} tax={planet.hasTaxOffice} yard={planet.hasCapitalShipyard} station={planet.hasBattleStation} queue={planet.shipBuildQueue.Count}");
        }
        foreach (GalacticFleet fleet in GalacticFleet.All.OrderBy(f => f.name))
        {
            // A travelling fleet keeps moving after the load, so for it the destination is what must match.
            string where = fleet.IsTraveling ? "to " + (fleet.Destination != null ? fleet.Destination.planetName : "-") : "at " + (fleet.currentPlanet != null ? fleet.currentPlanet.planetName : "-");
            text.AppendLine($"{fleet.name}: {SaveRegistry.Id(fleet.faction)} {where} ships={string.Join(",", fleet.ships.Select(ship => SaveRegistry.Id(ship.type)))} travelling={fleet.IsTraveling}");
        }
        if (GalacticState.Instance != null)
        {
            foreach (GalacticState.TreasurySnapshot treasury in GalacticState.Instance.treasuries.OrderBy(t => SaveRegistry.Id(t.faction)))
            {
                text.AppendLine($"credits {SaveRegistry.Id(treasury.faction)} = {treasury.currency}");
            }
        }
        return text.ToString();
    }

    private static string WithoutPlanet(string snapshot, string planet) =>
        string.Join(System.Environment.NewLine, Strip(snapshot).Split('\n').Where(line => !line.StartsWith(planet + ":")));

    private static string Strip(string text) => text.Replace("\r", "");

    private static IEnumerator Wait(float seconds)
    {
        for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime) yield return null;
    }
}
