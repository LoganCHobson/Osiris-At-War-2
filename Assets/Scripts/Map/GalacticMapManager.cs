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

    [Header("Hyperspace lanes")]
    public Color laneColor = Color.cyan;
    public float laneWidth = 0.15f;

    [Header("Debug")]
    public bool debugControlAnyFleet = false;

    private Camera cam;
    private readonly List<GalacticFleet> selectedFleets = new List<GalacticFleet>();
    private bool isDragging;
    private Vector3 dragOrigin;

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        cam = Camera.main;
        Cursor.visible = true;

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

        bool valid = hoveredPlanet != null && CanAnySelectedFleetReach(hoveredPlanet);

        orderLinePreview.positionCount = 2;
        orderLinePreview.SetPosition(0, dragOrigin);
        orderLinePreview.SetPosition(1, dragPoint);

        Color lineColor = valid ? validOrderColor : invalidOrderColor;
        orderLinePreview.startColor = lineColor;
        orderLinePreview.endColor = lineColor;
    }

    private bool CanAnySelectedFleetReach(Planet planet)
    {
        foreach (GalacticFleet fleet in selectedFleets)
        {
            if (fleet.currentPlanet == null) continue;
            if (fleet.currentPlanet == planet) return true;
            if (GalacticPathfinder.FindPath(fleet.currentPlanet, planet, null).Count >= 2) return true;
        }
        return false;
    }

    private void HandleRelease()
    {
        isDragging = false;

        if (orderLinePreview != null)
        {
            orderLinePreview.positionCount = 0;
        }

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
