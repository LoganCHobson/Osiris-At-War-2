using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

// Dev tool: opens the build menu on a player planet with a shipyard, queues a few ships, hovers a card and
// looks at an enemy planet, screenshotting each,
// so the build menu's layout can be checked without clicking through by hand. Writes to Logs/BuildMenuTour.
// Run with -executeMethod ShowcaseRunner.RunBuildMenuTour; quits when done.
public class BuildMenuTour : MonoBehaviour
{
    public string outputDir;

    private IEnumerator Start()
    {
        DontDestroyOnLoad(gameObject);
        if (string.IsNullOrEmpty(outputDir)) outputDir = Path.Combine(Application.dataPath, "..", "Logs", "BuildMenuTour");
        Directory.CreateDirectory(outputDir);

        yield return Wait(3f);
        SceneManager.LoadScene("GalacticMap");
        yield return Wait(4f);

        Planet planet = Planet.All.Where(p => p.owner != null && p.owner.isPlayerFaction)
                                  .OrderByDescending(p => GalacticFleet.All.Any(f => f.currentPlanet == p && f.faction == p.owner))
                                  .ThenByDescending(p => p.hasCapitalShipyard).FirstOrDefault();
        if (planet == null)
        {
            Debug.LogError("BUILDTOUR no player planet");
            Quit();
            yield break;
        }
        planet.hasCapitalShipyard = true;
        if (GalacticState.Instance != null) GalacticState.Instance.AddCurrency(planet.owner, 2000);

        FleetPanel.Instance?.Show(planet); // Opened together, as clicking a planet does.
        PlanetBuildPanel.Instance.Show(planet);
        yield return Wait(1f);
        LogRect("fleet panel", FleetPanel.Instance != null ? FleetPanel.Instance.panelRoot : null);
        yield return Shot("1_build_menu");

        foreach (Ship ship in planet.owner.roster.Where(s => s != null).Take(4))
        {
            planet.shipBuildQueue.Add(new ShipBuildOrder { ship = ship, remainingTime = planet.owner.BuildTime(ship) });
        }
        yield return Wait(2f);
        yield return Shot("2_build_menu_queue");

        BuildCard card = FindObjectsByType<BuildCard>(FindObjectsSortMode.None).FirstOrDefault(c => c.Ship != null && c.Ship.name.Contains("Destroyer"));
        if (card != null) ExecuteEvents.Execute(card.gameObject, new PointerEventData(EventSystem.current), ExecuteEvents.pointerEnterHandler);
        yield return Wait(0.5f);
        yield return Shot("3_build_menu_tooltip");

        if (card != null) ExecuteEvents.Execute(card.gameObject, new PointerEventData(EventSystem.current), ExecuteEvents.pointerExitHandler);
        PlanetBuildPanel.Instance.Show(Planet.All.First(p => p.owner != null && !p.owner.isPlayerFaction));
        yield return Wait(0.5f);
        yield return Shot("4_build_menu_enemy");

        Quit();
    }

    private static void LogRect(string label, GameObject panel)
    {
        if (panel == null || !(panel.transform is RectTransform rect)) return;
        Vector3[] corners = new Vector3[4];
        rect.GetWorldCorners(corners);
        Debug.Log($"BUILDTOUR {label} screen rect {corners[0]} to {corners[2]}");
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
        Debug.Log($"BUILDTOUR {name} {image.width}x{image.height}");
    }
}
