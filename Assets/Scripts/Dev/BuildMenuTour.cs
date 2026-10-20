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

        // Drag one ship out of the fleet's stack into an empty lane, the way a player would.
        ShipIconDragHandler tile = FindObjectsByType<ShipIconDragHandler>(FindObjectsSortMode.None).FirstOrDefault(t => !t.IsLead);
        FleetRowUI emptyLane = FindObjectsByType<FleetRowUI>(FindObjectsSortMode.None).FirstOrDefault(r => r.Fleet == null);
        Debug.Log($"BUILDTOUR drag test: tile={(tile != null ? tile.Ship.name + " x" + tile.Count : "none")} lane={(emptyLane != null ? emptyLane.name : "none")} " +
                  (tile != null && tile.SourceRow.Fleet != null ? $"fleet traveling={tile.SourceRow.Fleet.IsTraveling} at={tile.SourceRow.Fleet.currentPlanet?.planetName} faction={tile.SourceRow.Fleet.faction?.name}" : ""));
        if (tile != null && emptyLane != null)
        {
            PointerEventData drag = new PointerEventData(EventSystem.current) { pointerDrag = tile.gameObject, position = tile.transform.position };
            ExecuteEvents.Execute(tile.gameObject, drag, ExecuteEvents.beginDragHandler);
            ExecuteEvents.Execute(emptyLane.gameObject, drag, ExecuteEvents.dropHandler);
            ExecuteEvents.Execute(tile.gameObject, drag, ExecuteEvents.endDragHandler);
        }
        yield return Wait(0.5f);
        int fleetsHere = GalacticFleet.All.Count(f => f.currentPlanet == planet && f.roster.Count > 0);
        int strayTiles = FindObjectsByType<ShipIconDragHandler>(FindObjectsSortMode.None).Count(t => t.GetComponentInParent<FleetRowUI>() == null);
        Debug.Log($"BUILDTOUR after drag: {fleetsHere} fleets at {planet.planetName}, {strayTiles} stray tiles");
        yield return Shot("5_fleet_split");
        PlanetBuildPanel.Instance.Show(Planet.All.First(p => p.owner != null && !p.owner.isPlayerFaction));
        yield return Wait(0.5f);
        yield return Shot("4_build_menu_enemy");

        Quit();
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
