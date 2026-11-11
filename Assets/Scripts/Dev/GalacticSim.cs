using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

// Plays the galactic map with nobody at the controls, to measure how the AI factions wage war: battles and how
// they end (destroyed or retreated), ships lost, planets changing hands, and the standoff problem - how much
// of a navy sits pinned at one world, and for how long. Every battle the player would be asked about is
// auto-resolved. -legacy switches back to the old rules (fight-to-the-death auto-resolve, match-the-enemy
// defence) so the two can be compared on the same build. Driven by GalacticSimRunner; writes a report, quits.
public class GalacticSim : MonoBehaviour
{
    public float galacticSeconds = 1200f;
    public float speed = 10f;
    public bool legacy;
    public int seed = 12345;
    public string reportPath;

    private int battles, retreats, destroyedFleets, captures;
    private float powerLost;
    private readonly List<float> pinnedShares = new List<float>();
    private float worstPinnedShare;
    private int standoffSamples, samples;
    private readonly Dictionary<string, float> longestGuard = new Dictionary<string, float>();

    private IEnumerator Start()
    {
        Random.InitState(seed);
        AutoResolver.RetreatsEnabled = !legacy;
        FactionBrain.LegacyDefense = legacy;
        GalacticEvents.BattleResolved += OnBattle;
        GalacticEvents.PlanetCaptured += OnCapture;

        yield return null;
        GameSpeed.SetSpeed(speed);
        float started = AIDirector.Instance != null ? AIDirector.Instance.GalacticTime : 0f;
        float nextSample = 0f;

        while (AIDirector.Instance == null || AIDirector.Instance.GalacticTime - started < galacticSeconds)
        {
            // Nobody's at the controls: settle every battle the player would be asked about, close every report.
            if (BattlePrompt.IsOpen && BattlePrompt.Instance != null) BattlePrompt.Instance.AutoResolve();
            if (BattleReportPanel.IsOpen && BattleReportPanel.Instance != null) BattleReportPanel.Instance.Continue();
            if (GameSpeed.Suspended) GameSpeed.SetSuspended(false);

            if (AIDirector.Instance != null && AIDirector.Instance.GalacticTime >= nextSample)
            {
                nextSample = AIDirector.Instance.GalacticTime + 10f;
                Sample();
            }
            yield return null;
        }

        GameSpeed.Reset();
        string report = Report();
        Debug.Log("GALSIM REPORT\n" + report);
        if (!string.IsNullOrEmpty(reportPath)) File.WriteAllText(reportPath, report);
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit(0);
#endif
    }

    private void OnBattle(BattleReport report)
    {
        battles++;
        powerLost += report.attackerLosses + report.defenderLosses;
        if (report.loserRetreated) retreats++;
        else destroyedFleets++;
    }

    private void OnCapture(Planet planet, Faction from, Faction to) => captures++;

    // For each faction: the share of its navy assigned to its single biggest guard post, and which guards are
    // in a standoff. Also how long each guard post has held its current threat.
    private void Sample()
    {
        foreach (FactionBrain brain in AIDirector.Instance.brains)
        {
            if (brain.TotalPower <= 0f) continue;
            float biggest = 0f;
            foreach (FactionBrain.MilitaryTask task in brain.taskBook.Values)
            {
                if (task.kind != FactionBrain.OrderKind.Defend && task.kind != FactionBrain.OrderKind.Stage) continue;
                biggest = Mathf.Max(biggest, task.assigned / brain.TotalPower);
                if (task.kind != FactionBrain.OrderKind.Defend || task.threatSince < 0f) continue;
                if (task.standoff) standoffSamples++;
                float held = AIDirector.Instance.GalacticTime - task.threatSince;
                string key = brain.faction.name + "/" + task.planetName;
                longestGuard[key] = Mathf.Max(longestGuard.TryGetValue(key, out float h) ? h : 0f, held);
            }
            pinnedShares.Add(biggest);
            worstPinnedShare = Mathf.Max(worstPinnedShare, biggest);
            samples++;
        }
    }

    private string Report()
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine($"GALACTIC SIM  rules={(legacy ? "legacy" : "new")}  seed {seed}  {galacticSeconds:F0} galactic s at x{speed}");
        sb.AppendLine($"battles {battles}  retreated {retreats}  destroyed {destroyedFleets}  power lost {powerLost:F0}  planets captured {captures}");
        float meanPinned = pinnedShares.Count > 0 ? pinnedShares.Average() : 0f;
        sb.AppendLine($"navy at the single biggest guard post: mean {meanPinned:P0}  worst {worstPinnedShare:P0}");
        sb.AppendLine($"guard samples in a standoff: {standoffSamples} of {samples}");
        foreach (KeyValuePair<string, float> guard in longestGuard.OrderByDescending(g => g.Value).Take(5))
        {
            sb.AppendLine($"  longest-held guard: {guard.Key}  {guard.Value:F0}s");
        }
        return sb.ToString();
    }

    private void OnDestroy()
    {
        GalacticEvents.BattleResolved -= OnBattle;
        GalacticEvents.PlanetCaptured -= OnCapture;
    }
}
