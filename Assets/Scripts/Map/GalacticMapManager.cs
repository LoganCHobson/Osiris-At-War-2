using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

public class GalacticMapManager : MonoBehaviour
{
    public static GalacticMapManager Instance;

    [Header("Raycasting")]
    public LayerMask fleetLayer;
    public LayerMask planetLayer;

    [Header("Drag order preview")]
    public LineRenderer orderLinePreview;
    public Color validOrderColor = Color.cyan;
    public Color invalidOrderColor = Color.red;
    public float routeLineHeight = 0.1f;

    [Header("Hyperspace lanes")]
    public Color laneColor = Color.cyan;
    public float laneWidth = 0.15f;

    [Header("Debug")]
    public bool debugControlAnyFleet = false;

    private Camera cam;
    private readonly List<GalacticFleet> selectedFleets = new List<GalacticFleet>();
    private bool isDragging;
    private Vector3 dragOrigin;
    private GalacticFleet dragFleet;
    private Planet previewPlanet;
    private List<Planet> previewRoute = new List<Planet>();
    private bool previewValid;
    private string previewInfo;

    public bool IsDraggingFleet => isDragging;

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        cam = Camera.main;
        Cursor.visible = true;
        GameSpeed.Reset();

        DrawHyperspaceLanes();

        if (orderLinePreview != null)
        {
            orderLinePreview.positionCount = 0;
        }

        if (AIDirector.Instance == null)
        {
            new GameObject("AIDirector").AddComponent<AIDirector>();
        }

        GalacticState.Instance?.ApplyToScene();
        ApplyPendingBattleResult();
    }

    private void ApplyPendingBattleResult()
    {
        if (BattleContext.Instance == null || !BattleContext.Instance.hasPendingBattle) return;

        BattleContext context = BattleContext.Instance;
        Planet destinationPlanet = FindPlanetByName(context.destinationPlanetName);

        GalacticFleet attackerFleet = ApplyFleetResult(context.attackerFleetName, context.attackerRoster, destinationPlanet);
        GalacticFleet defenderFleet = null;

        if (context.hasDefender)
        {
            defenderFleet = ApplyFleetResult(context.defenderFleetName, context.defenderRoster, null);
        }

        attackerFleet?.HoldAfterBattle();
        defenderFleet?.HoldAfterBattle();

        bool attackerSurvived = context.attackerRoster.Count > 0;
        bool defenderRosterCleared = !context.hasDefender || context.defenderRoster.Count == 0;
        bool shipyardCleared = !context.defenderHasShipyard || !context.defenderShipyardSurvived;
        bool battleStationCleared = !context.defenderHasBattleStation || !context.defenderBattleStationSurvived;
        bool defenderDefeated = defenderRosterCleared && shipyardCleared && battleStationCleared;

        float destroyedDefenses = (context.defenderHasShipyard && !context.defenderShipyardSurvived ? Strength.ShipyardPower : 0f)
            + (context.defenderHasBattleStation && !context.defenderBattleStationSurvived ? Strength.BattleStationPower : 0f);

        GalacticEvents.RaiseBattleResolved(new BattleReport
        {
            planet = destinationPlanet,
            attacker = context.attackerFaction,
            defender = context.defenderFaction,
            attackerLosses = context.attackerStartPower - Strength.Of(context.attackerRoster),
            defenderLosses = context.defenderStartPower - Strength.Of(context.defenderRoster) + destroyedDefenses,
            attackerWon = attackerSurvived && defenderDefeated
        });

        if (attackerSurvived && defenderDefeated && destinationPlanet != null)
        {
            destinationPlanet.Capture(context.attackerFaction);
        }

        if (destinationPlanet != null)
        {
            if (context.defenderHasShipyard && !context.defenderShipyardSurvived)
            {
                destinationPlanet.hasCapitalShipyard = false;
            }

            if (context.defenderHasBattleStation && !context.defenderBattleStationSurvived)
            {
                destinationPlanet.hasBattleStation = false;
            }
        }

        context.Clear();
    }

    private GalacticFleet ApplyFleetResult(string fleetName, List<Ship> survivingRoster, Planet moveToPlanet)
    {
        if (string.IsNullOrEmpty(fleetName)) return null;

        GameObject fleetObject = GameObject.Find(fleetName);
        if (fleetObject == null) return null;

        GalacticFleet fleet = fleetObject.GetComponent<GalacticFleet>();
        if (fleet == null) return null;

        if (survivingRoster.Count == 0)
        {
            Destroy(fleetObject);
            return null;
        }

        fleet.roster = new List<Ship>(survivingRoster);

        if (moveToPlanet != null)
        {
            fleet.PlaceAt(moveToPlanet);
        }

        return fleet;
    }

    private Planet FindPlanetByName(string planetName)
    {
        if (string.IsNullOrEmpty(planetName)) return null;

        foreach (Planet planet in FindObjectsByType<Planet>(FindObjectsSortMode.None))
        {
            if (planet.planetName == planetName) return planet;
        }

        return null;
    }

    private void Update()
    {
        if (BattlePrompt.IsOpen)
        {
            isDragging = false;
            ClearRoutePreview();
            return;
        }

        if (Input.GetMouseButtonDown(0))
        {
            HandlePress();
        }
        else if (isDragging && Input.GetMouseButton(0))
        {
            HandleDrag();
        }
        else if (isDragging && Input.GetMouseButtonUp(0))
        {
            HandleRelease();
        }
        else if (Input.GetMouseButtonDown(1))
        {
            DeselectAll();
        }

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            FleetPanel.Instance?.Hide();
            PlanetBuildPanel.Instance?.Hide();
        }
    }

    private void HandlePress()
    {
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
        {
            return;
        }

        Ray ray = cam.ScreenPointToRay(Input.mousePosition);

        if (Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity, fleetLayer))
        {
            GalacticFleet fleet = hit.collider.GetComponentInParent<GalacticFleet>();
            if (fleet == null) return;
            if (!debugControlAnyFleet && (fleet.faction == null || !fleet.faction.isPlayerFaction)) return; // Can only command your own fleets.

            if (Input.GetKey(KeyCode.LeftShift))
            {
                if (!selectedFleets.Contains(fleet))
                {
                    selectedFleets.Add(fleet);
                }
            }
            else if (!selectedFleets.Contains(fleet))
            {
                DeselectAll();
                selectedFleets.Add(fleet);
            }

            isDragging = true;
            dragOrigin = fleet.transform.position;
            dragFleet = fleet;
            previewPlanet = null;
            previewRoute.Clear();
            FleetPanel.Instance?.Hide();
            PlanetBuildPanel.Instance?.Hide();
            return;
        }

        if (Physics.Raycast(ray, out RaycastHit planetHit, Mathf.Infinity, planetLayer))
        {
            Planet planet = planetHit.collider.GetComponentInParent<Planet>();
            if (planet != null)
            {
                FleetPanel.Instance?.Show(planet);
                PlanetBuildPanel.Instance?.Show(planet);
            }
            return;
        }

        FleetPanel.Instance?.Hide();
        PlanetBuildPanel.Instance?.Hide();
    }

    private void HandleDrag()
    {
        if (orderLinePreview == null) return;

        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        Vector3 dragPoint;
        Planet hoveredPlanet = null;

        if (Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity, planetLayer))
        {
            hoveredPlanet = hit.collider.GetComponentInParent<Planet>();
            dragPoint = hit.collider.transform.position;
        }
        else
        {
            Plane mapPlane = new Plane(Vector3.up, dragOrigin);
            dragPoint = mapPlane.Raycast(ray, out float enter) ? ray.GetPoint(enter) : dragOrigin;
        }

        if (hoveredPlanet != previewPlanet)
        {
            previewPlanet = hoveredPlanet;
            RebuildRoutePreview();
        }

        Vector3 lift = Vector3.up * routeLineHeight;
        if (previewRoute.Count >= 2 && dragFleet != null)
        {
            orderLinePreview.positionCount = previewRoute.Count;
            orderLinePreview.SetPosition(0, dragFleet.transform.position + lift);
            for (int i = 1; i < previewRoute.Count; i++)
            {
                orderLinePreview.SetPosition(i, previewRoute[i].transform.position + lift);
            }
        }
        else
        {
            orderLinePreview.positionCount = 2;
            orderLinePreview.SetPosition(0, dragOrigin + lift);
            orderLinePreview.SetPosition(1, dragPoint + lift);
        }

        Color lineColor = previewValid ? validOrderColor : invalidOrderColor;
        orderLinePreview.startColor = lineColor;
        orderLinePreview.endColor = lineColor;

        PlanetTooltip.Instance?.SetRouteInfo(previewInfo);
    }

    private void RebuildRoutePreview()
    {
        previewRoute.Clear();
        previewValid = false;
        previewInfo = null;

        if (previewPlanet == null) return;

        int ready = 0;
        int busy = 0;
        float slowest = 0f;
        GalacticFleet routeOwner = null;

        foreach (GalacticFleet fleet in selectedFleets)
        {
            if (fleet == null) continue;

            if (!fleet.CanTakeOrders)
            {
                busy++;
                continue;
            }

            if (fleet.currentPlanet == previewPlanet)
            {
                ready++;
                continue;
            }

            List<Planet> route = fleet.PlanRoute(previewPlanet);
            if (route.Count < 2) continue;

            ready++;
            slowest = Mathf.Max(slowest, fleet.EstimateTravelTime(route));

            if (routeOwner == null || fleet == dragFleet)
            {
                routeOwner = fleet;
                previewRoute = route;
            }
        }

        previewValid = ready > 0;
        if (!previewValid)
        {
            previewInfo = MapText.Tint(busy > 0 ? "Selected fleets can't take orders right now" : "No hyperspace route", MapText.Bad);
            return;
        }

        List<string> lines = new List<string>();

        if (routeOwner == null)
        {
            lines.Add("<b>Route</b>  Already here");
        }
        else
        {
            lines.Add($"<b>Route</b>  {MapText.Plural(previewRoute.Count - 1, "jump")}  |  ETA {MapText.RealTime(slowest)}");
            if (ready > 1) lines.Add(MapText.Tint($"{ready} fleets - ETA is for the slowest", MapText.Muted));

            int hostileStops = 0;
            for (int i = 1; i < previewRoute.Count - 1; i++)
            {
                Planet stop = previewRoute[i];
                bool enemySeen = FogOfWar.CanSee(stop) && routeOwner.FindOpposingFleet(stop) != null;
                if ((stop.owner != null && stop.owner != routeOwner.faction) || enemySeen) hostileStops++;
            }
            if (hostileStops > 0) lines.Add(MapText.Tint($"Passes {MapText.Plural(hostileStops, "hostile system")} - may be intercepted", MapText.Warning));

            if (!FogOfWar.CanSee(previewPlanet))
            {
                lines.Add(MapText.Tint(previewPlanet.owner != routeOwner.faction ? "No vision of destination - defenses unknown" : "No vision of destination", MapText.Warning));
            }
            else if (routeOwner.HasBattleAt(previewPlanet))
            {
                lines.Add(MapText.Tint("Battle expected at destination", MapText.Bad));
            }
            else if (previewPlanet.owner != routeOwner.faction)
            {
                lines.Add(MapText.Tint("Will claim this system", MapText.Good));
            }
        }

        if (busy > 0) lines.Add(MapText.Tint($"{MapText.Plural(busy, "fleet")} busy and won't move", MapText.Muted));

        previewInfo = string.Join("\n", lines);
    }

    private void ClearRoutePreview()
    {
        previewPlanet = null;
        previewRoute.Clear();
        previewValid = false;
        previewInfo = null;

        if (orderLinePreview != null)
        {
            orderLinePreview.positionCount = 0;
        }

        PlanetTooltip.Instance?.SetRouteInfo(null);
    }

    private void HandleRelease()
    {
        isDragging = false;
        ClearRoutePreview();

        Ray ray = cam.ScreenPointToRay(Input.mousePosition);

        if (!Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity, planetLayer))
        {
            return;
        }

        Planet targetPlanet = hit.collider.GetComponentInParent<Planet>();
        if (targetPlanet == null) return;

        foreach (GalacticFleet fleet in selectedFleets)
        {
            fleet.TrySetDestination(targetPlanet);
        }
    }

    private void DeselectAll()
    {
        selectedFleets.Clear();
        dragFleet = null;
        FleetPanel.Instance?.Hide();
        PlanetBuildPanel.Instance?.Hide();
    }

    private void DrawHyperspaceLanes()
    {
        Planet[] planets = FindObjectsByType<Planet>(FindObjectsSortMode.None);
        HashSet<(Planet, Planet)> drawn = new HashSet<(Planet, Planet)>();

        foreach (Planet planet in planets)
        {
            foreach (Planet connection in planet.connections)
            {
                if (drawn.Contains((connection, planet))) continue;
                drawn.Add((planet, connection));

                GameObject laneObj = new GameObject($"Lane_{planet.planetName}-{connection.planetName}");
                laneObj.transform.SetParent(transform);

                LineRenderer line = laneObj.AddComponent<LineRenderer>();
                line.positionCount = 2;
                line.SetPosition(0, planet.transform.position);
                line.SetPosition(1, connection.transform.position);
                line.startWidth = laneWidth;
                line.endWidth = laneWidth;
                line.material = new Material(Shader.Find("Sprites/Default"));
                line.startColor = laneColor;
                line.endColor = laneColor;
            }
        }
    }
}
