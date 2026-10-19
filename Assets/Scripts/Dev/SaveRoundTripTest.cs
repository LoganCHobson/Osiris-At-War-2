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

        string before = Snapshot();
        Vector3 travellerPosition = traveller != null ? traveller.transform.position : Vector3.zero;
        float clock = AIDirector.Instance != null ? AIDirector.Instance.GalacticTime : 0f;
        int brains = AIDirector.Instance != null ? AIDirector.Instance.brains.Count : 0;

        Check(SaveSystem.Save(Slot, "Round trip test", out string error), "saved (" + error + ")");

        // Scramble things so a load that did nothing would be caught.
        foreach (Planet planet in Planet.All) planet.hasTaxOffice = !planet.hasTaxOffice;
        if (traveller != null) traveller.roster.Clear();

        Check(SaveSystem.Load(Slot, out error), "load started (" + error + ")");
        yield return Wait(3f);

        GalacticFleet restored = traveller != null ? GalacticFleet.All.FirstOrDefault(fleet => fleet.name == traveller.name) : null;
        string after = Snapshot();

        Check(restored != null, "travelling fleet exists after load");
        Check(restored != null && restored.IsTraveling, "travelling fleet is still mid-trip");
        Check(restored != null && restored.Destination == far, "travelling fleet still heading to " + (far != null ? far.planetName : "?"));
        Check(restored != null && Vector3.Distance(restored.transform.position, travellerPosition) < 15f, "travelling fleet picked up near where it was saved");
        Check(AIDirector.Instance != null && Mathf.Abs(AIDirector.Instance.GalacticTime - clock) < 10f, $"galactic clock restored ({clock:F0}s)");
        Check(AIDirector.Instance != null && AIDirector.Instance.brains.Count >= brains, $"AI empires restored ({brains})");
        Check(Strip(before) == Strip(after), "planets, fleets and money match the save");
        if (Strip(before) != Strip(after))
        {
            report.AppendLine("BEFORE:\n" + before);
            report.AppendLine("AFTER:\n" + after);
        }

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
            text.AppendLine($"{fleet.name}: {SaveRegistry.Id(fleet.faction)} at {(fleet.currentPlanet != null ? fleet.currentPlanet.planetName : "-")} ships={string.Join(",", fleet.roster.Select(SaveRegistry.Id))} travelling={fleet.IsTraveling}");
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

    private static string Strip(string text) => text.Replace("\r", "");

    private static IEnumerator Wait(float seconds)
    {
        for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime) yield return null;
    }
}
