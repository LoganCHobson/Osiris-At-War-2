using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

// Dev tool: spy probes on the galactic map. Builds a probe at a player shipyard, drags it (scripted) to an
// enemy planet's spot, lets it fly there and park, then opens that planet's panel to show the enemy build
// queue the probe reveals. Screenshots each step to Logs/ProbeTour and logs what it checked.
// Run with -executeMethod ShowcaseRunner.RunProbeTour; quits when done.
public class ProbeTour : MonoBehaviour
{
    private string outputDir;
    private Transform follow;

    private IEnumerator Start()
    {
        DontDestroyOnLoad(gameObject);
        outputDir = Path.Combine(Application.dataPath, "..", "Logs", "ProbeTour");
        Directory.CreateDirectory(outputDir);

        yield return Wait(3f);
        SceneManager.LoadScene("GalacticMap");
        yield return Wait(4f);

        Planet home = Planet.All.Where(p => p.owner != null && p.owner.isPlayerFaction).OrderByDescending(p => p.hasCapitalShipyard).FirstOrDefault();
        if (home == null) { Debug.LogError("PROBETOUR no player planet"); Quit(); yield break; }
        home.hasCapitalShipyard = true;
        GalacticState.Instance.AddCurrency(home.owner, 2000);

        // An enemy planet a few jumps away.
        Planet target = Planet.All.Where(p => p.owner != null && !p.owner.isPlayerFaction && !p.owner.isNeutral)
            .Select(p => (p, GalacticPathfinder.FindPath(home, p, home.owner)))
            .Where(t => t.Item2.Count >= 3).OrderBy(t => t.Item2.Count).Select(t => t.p).FirstOrDefault()
            ?? Planet.All.First(p => p.owner != null && !p.owner.isPlayerFaction);
        Debug.Log($"PROBETOUR home {home.planetName} ({home.owner.factionName}), target {target.planetName} ({target.owner?.factionName}), can see target before: {FogOfWar.CanSee(target)}");

        // Something on the enemy's build queue for the probe to find.
        foreach (Ship ship in target.owner.roster.Where(s => s != null).Take(3))
            target.shipBuildQueue.Add(new ShipBuildOrder { ship = ship, remainingTime = target.owner.BuildTime(ship) });

        Focus(home.transform.position);
        FleetPanel.Instance?.Show(home);
        PlanetBuildPanel.Instance.Show(home);
        bool queued = SpyProbes.Queue(home);
        Debug.Log($"PROBETOUR queued: {queued}, building {SpyProbes.BuildingAt(home):0}s");
        yield return Wait(1f);
        yield return Shot("1_build_probe_button");

        SpyProbes.Orders.ForEach(o => o.remaining = 0.5f); // Skip the build time.
        yield return Wait(1.5f);
        PlanetBuildPanel.Instance.Hide();
        FleetPanel.Instance?.Hide();
        SpyProbes.Probe probe = SpyProbes.All.FirstOrDefault();
        Debug.Log($"PROBETOUR probe built: {probe != null} at {probe?.planet}");
        if (probe == null) { Quit(); yield break; }
        yield return Wait(0.5f);
        yield return Shot("2_probe_parked_home");

        // Mid-drag: the route, every planet's spot, the target's lit.
        Vector3 between = Vector3.Lerp(home.transform.position, target.transform.position, 0.5f);
        Focus(between);
        SpyProbeLayer.Instance.DebugHoldDrag(probe, target.PlanetSpotPosition);
        yield return Wait(1f);
        yield return Shot("3_dragging_to_enemy_spot");
        SpyProbeLayer.Instance.DebugRelease();
        yield return Wait(0.2f);
        Debug.Log($"PROBETOUR route: {string.Join(" > ", probe.route)}");

        GameSpeed.SetSpeed(4f);
        float started = Time.realtimeSinceStartup;
        bool shotInFlight = false;
        while (!probe.Parked && Time.realtimeSinceStartup - started < 60f)
        {
            if (!shotInFlight && probe.route.Count > 0 && Time.realtimeSinceStartup - started > 1.5f)
            {
                Focus(probe.Position);
                yield return Shot("4_in_flight");
                shotInFlight = true;
            }
            yield return null;
        }
        GameSpeed.SetSpeed(1f);
        Debug.Log($"PROBETOUR parked at {probe.planet}: {probe.Parked}, life {probe.lifeLeft:0}s, battles awaiting: {GalacticFleet.All.Count(f => f.AwaitingBattle)}");

        Focus(target.transform.position);
        yield return Wait(1.5f); // Fog of war catches up.
        Debug.Log($"PROBETOUR can see target now: {FogOfWar.CanSee(target)}, neighbours seen: {target.connections.Count(FogOfWar.CanSee)}/{target.connections.Count}, watching: {SpyProbes.Watching(home.owner, target)}");
        yield return Shot("5_parked_over_enemy");

        FleetPanel.Instance?.Show(target);
        PlanetBuildPanel.Instance.Show(target);
        yield return Wait(1f);
        yield return Shot("6_enemy_queue_spied");

        // Burn out.
        probe.lifeLeft = 1f;
        yield return Wait(2f);
        Debug.Log($"PROBETOUR after lifetime: probes left {SpyProbes.All.Count}");
        Quit();
    }

    private void Focus(Vector3 point)
    {
        Camera cam = Camera.main;
        RTSCameraController rig = RTSCameraController.instance != null ? RTSCameraController.instance : FindFirstObjectByType<RTSCameraController>();
        if (cam == null || rig == null) return;
        Plane map = new Plane(Vector3.up, Vector3.zero);
        Ray ray = new Ray(cam.transform.position, cam.transform.forward);
        Vector3 offset = map.Raycast(ray, out float d) ? ray.GetPoint(d) - rig.transform.position : Vector3.zero;
        offset.y = 0f;
        if (follow == null) follow = new GameObject("ProbeTourFocus").transform;
        follow.position = new Vector3(point.x, 0f, point.z) - offset + Vector3.up * rig.transform.position.y;
        rig.followTransform = follow;
    }

    private static void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#endif
    }

    private static IEnumerator Wait(float seconds)
    {
        for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime) yield return null;
    }

    private IEnumerator Shot(string name)
    {
        yield return new WaitForEndOfFrame();
        Texture2D image = ScreenCapture.CaptureScreenshotAsTexture();
        File.WriteAllBytes(Path.Combine(outputDir, name + ".png"), image.EncodeToPNG());
        Destroy(image);
        Debug.Log($"PROBETOUR shot {name}");
    }
}
