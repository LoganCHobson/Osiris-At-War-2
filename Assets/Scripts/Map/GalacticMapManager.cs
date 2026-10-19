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

    [Header("Fleet drag and drop")]
    [Tooltip("How close (in map units) the cursor must be to a parking spot to drop a fleet onto it.")]
    public float slotSnapRadius = 1.4f;
    public Color mergeOrderColor = new Color(1f, 0.78f, 0.25f);
    public float slotMarkerRadius = 0.7f;

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

    private enum DropKind { None, Route, Merge, Park }
    private DropKind dropKind;
    private GalacticFleet dropFleet;
    private Planet dropPlanet;
    private int dropSlot = -1;
    private LineRenderer slotMarker;

    public bool IsDraggingFleet => isDragging;
    public GalacticFleet DropTargetFleet => isDragging && dropKind == DropKind.Merge ? dropFleet : null;
    public Planet DragHoverPlanet => isDragging ? dropPlanet : null;

    public bool IsSelected(GalacticFleet fleet)
    {
        return fleet != null && selectedFleets.Contains(fleet);
    }

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
        CreateSlotMarker();

        if (orderLinePreview != null)
        {
            orderLinePreview.positionCount = 0;
        }

        if (AIDirector.Instance == null)
        {
            new GameObject("AIDirector").AddComponent<AIDirector>();
        }

        if (FindFirstObjectByType<ResourceBar>() == null)
        {
            new GameObject("ResourceBar").AddComponent<ResourceBar>();
        }

        GalacticState.Instance?.ApplyToScene();
        ApplyPendingBattleResult();
    }

    private void ApplyPendingBattleResult()
    {
        if (BattleContext.Instance == null || !BattleContext.Instance.hasPendingBattle) return;

        BattleContext context = BattleContext.Instance;
        Planet destinationPlanet = FindPlanetByName(context.destinationPlanetName);

        List<GalacticFleet> attackerFleets = ApplySideResult(context, true, destinationPlanet);
        List<GalacticFleet> defenderFleets = ApplySideResult(context, false, null);

        bool attackerWon = context.defenderLost && !context.attackerLost;

        float destroyedDefenses = (context.defenderHasShipyard && !context.defenderShipyardSurvived ? Strength.ShipyardPower : 0f)
            + (context.defenderHasBattleStation && !context.defenderBattleStationSurvived ? Strength.BattleStationPower : 0f);

        GalacticEvents.RaiseBattleResolved(new BattleReport
        {
            planet = destinationPlanet,
            attacker = context.attackerFaction,
            defender = context.defenderFaction,
            attackerLosses = context.attackerStartPower - Strength.Of(context.attackerRoster),
            defenderLosses = context.defenderStartPower - Strength.Of(context.defenderRoster) + destroyedDefenses,
            attackerWon = attackerWon
        });

        Faction ownerBefore = destinationPlanet != null ? destinationPlanet.owner : null;
        if (attackerWon && destinationPlanet != null)
        {
            destinationPlanet.Capture(context.attackerFaction);
        }

        List<GalacticFleet> losingFleets = attackerWon ? defenderFleets : attackerFleets;
        Planet retreatedTo = null;
        foreach (GalacticFleet fleet in losingFleets)
        {
            Planet haven = fleet.RetreatFrom(destinationPlanet);
            if (retreatedTo == null) retreatedTo = haven;
        }

        BattleResult result = BuildBattleResult(context, destinationPlanet, attackerWon, ownerBefore);
        if (result != null && losingFleets.Count > 0)
        {
            bool playerLost = Strength.IsPlayer(attackerWon ? context.defenderFaction : context.attackerFaction);
            result.playerRetreated = playerLost;
            result.enemyRetreated = !playerLost;
            result.retreatedTo = retreatedTo != null ? retreatedTo.planetName : null;
        }
        BattleReportPanel.Show(result);

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

    private static BattleResult BuildBattleResult(BattleContext context, Planet planet, bool attackerWon, Faction ownerBefore)
    {
        bool playerAttacking = Strength.IsPlayer(context.attackerFaction);
        bool playerDefending = !playerAttacking && Strength.IsPlayer(context.defenderFaction);
        if (!playerAttacking && !playerDefending) return null;

        List<Ship> attackerLosses = BattleResult.Missing(context.attackerStartRoster, context.attackerRoster);
        List<Ship> defenderLosses = BattleResult.Missing(context.defenderStartRoster, context.defenderRoster);
        bool captured = attackerWon && planet != null && ownerBefore != context.attackerFaction;

        BattleResult result = new BattleResult
        {
            planetName = planet != null ? planet.planetName : context.destinationPlanetName,
            enemy = playerAttacking ? context.defenderFaction : context.attackerFaction,
            playerWon = playerAttacking == attackerWon,
            planetCaptured = playerAttacking && captured,
            planetLost = playerDefending && captured,
            playerLosses = playerAttacking ? attackerLosses : defenderLosses,
            enemyLosses = playerAttacking ? defenderLosses : attackerLosses
        };

        if (context.defenderHasShipyard && !context.defenderShipyardSurvived)
        {
            result.AddStructureLoss(playerDefending, BattleResult.ShipyardName);
        }

        if (context.defenderHasBattleStation && !context.defenderBattleStationSurvived)
        {
            result.AddStructureLoss(playerDefending, BattleResult.BattleStationName);
        }

        return result;
    }

    private List<GalacticFleet> ApplySideResult(BattleContext context, bool attackerSide, Planet moveToPlanet)
    {
        List<GalacticFleet> fleets = new List<GalacticFleet>();
        List<string> names = attackerSide ? context.attackerFleetNames : context.defenderFleetNames;

        for (int i = 0; i < names.Count; i++)
        {
            GalacticFleet fleet = ApplyFleetResult(names[i], context.SurvivorsOf(attackerSide, i), moveToPlanet);
            if (fleet == null) continue;

            fleet.HoldAfterBattle();
            fleets.Add(fleet);
        }

        return fleets;
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
            ResetDropTarget();
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
        Plane mapPlane = new Plane(Vector3.up, dragOrigin);
        Vector3 dragPoint = mapPlane.Raycast(ray, out float enter) ? ray.GetPoint(enter) : dragOrigin;

        DropKind previousKind = dropKind;
        GalacticFleet previousFleet = dropFleet;
        Planet previousPlanet = dropPlanet;
        int previousSlot = dropSlot;

        ResolveDropTarget(ray, dragPoint);

        bool targetChanged = dropKind != previousKind || dropFleet != previousFleet || dropPlanet != previousPlanet || dropSlot != previousSlot;
        if (targetChanged)
        {
            previewPlanet = dropKind == DropKind.Route ? dropPlanet : null;
            RebuildRoutePreview();
        }

        Vector3 lift = Vector3.up * routeLineHeight;
        Vector3 from = dragFleet != null ? dragFleet.transform.position : dragOrigin;

        if (dropKind == DropKind.Merge || dropKind == DropKind.Park)
        {
            Vector3 to = dropKind == DropKind.Merge ? dropFleet.transform.position : dropPlanet.GetSlotPosition(dropSlot);
            orderLinePreview.positionCount = 2;
            orderLinePreview.SetPosition(0, from + lift);
            orderLinePreview.SetPosition(1, to + lift);
        }
        else if (previewRoute.Count >= 2 && dragFleet != null)
        {
            orderLinePreview.positionCount = previewRoute.Count;
            orderLinePreview.SetPosition(0, from + lift);
            for (int i = 1; i < previewRoute.Count; i++)
            {
                orderLinePreview.SetPosition(i, previewRoute[i].transform.position + lift);
            }
        }
        else
        {
            Vector3 to = dropPlanet != null ? dropPlanet.transform.position : dragPoint;
            orderLinePreview.positionCount = 2;
            orderLinePreview.SetPosition(0, dragOrigin + lift);
            orderLinePreview.SetPosition(1, to + lift);
        }

        Color lineColor = !previewValid ? invalidOrderColor : dropKind == DropKind.Merge ? mergeOrderColor : validOrderColor;
        orderLinePreview.startColor = lineColor;
        orderLinePreview.endColor = lineColor;

        UpdateSlotMarker(lineColor);
        PlanetTooltip.Instance?.SetRouteInfo(previewInfo);
    }

    // Works out what releasing the mouse right now would do: merge into a friendly fleet, park in an
    // empty spot around the current planet, or travel to another planet (optionally into a chosen spot).
    private void ResolveDropTarget(Ray ray, Vector3 dragPoint)
    {
        ResetDropTarget();

        GalacticFleet hoveredFleet = FindHoveredFleet(ray);
        if (hoveredFleet != null)
        {
            SetFleetTarget(hoveredFleet);
            return;
        }

        if (Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity, planetLayer))
        {
            Planet planet = hit.collider.GetComponentInParent<Planet>();
            if (planet != null)
            {
                dropKind = DropKind.Route;
                dropPlanet = planet;
                return;
            }
        }

        Planet slotPlanet = Planet.FindNearestSlot(dragPoint, slotSnapRadius, out int slot);
        if (slotPlanet == null) return;

        GalacticFleet occupant = slotPlanet.GetFleetInSlot(slot);
        if (occupant != null)
        {
            if (selectedFleets.Contains(occupant)) return; // Dropped back on itself - nothing to do.
            if (occupant.IsRevealed)
            {
                SetFleetTarget(occupant);
                return;
            }
        }

        dropPlanet = slotPlanet;
        dropSlot = slot;
        bool parkingHere = dragFleet != null && dragFleet.currentPlanet == slotPlanet && !dragFleet.IsTraveling;
        dropKind = parkingHere && occupant == null ? DropKind.Park : DropKind.Route;
    }

    private void SetFleetTarget(GalacticFleet target)
    {
        dropPlanet = target.currentPlanet;
        dropFleet = target;

        foreach (GalacticFleet fleet in selectedFleets)
        {
            if (fleet != null && fleet.CanReorganizeWith(target))
            {
                dropKind = DropKind.Merge;
                return;
            }
        }

        // Can't merge right now (other planet, enemy, or busy) - treat it as an order to fly to its planet.
        dropFleet = null;
        dropKind = target.currentPlanet != null && !target.IsTraveling ? DropKind.Route : DropKind.None;
        if (dropKind == DropKind.None) dropPlanet = null;
    }

    private GalacticFleet FindHoveredFleet(Ray ray)
    {
        RaycastHit[] hits = Physics.RaycastAll(ray, Mathf.Infinity, fleetLayer);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        foreach (RaycastHit hit in hits)
        {
            GalacticFleet fleet = hit.collider.GetComponentInParent<GalacticFleet>();
            if (fleet != null && fleet.IsRevealed && !selectedFleets.Contains(fleet))
            {
                return fleet;
            }
        }
        return null;
    }

    private void ResetDropTarget()
    {
        dropKind = DropKind.None;
        dropFleet = null;
        dropPlanet = null;
        dropSlot = -1;
    }

    private void RebuildRoutePreview()
    {
        previewRoute.Clear();
        previewValid = false;
        previewInfo = null;

        if (dropKind == DropKind.Merge)
        {
            BuildMergePreview();
            return;
        }

        if (dropKind == DropKind.Park)
        {
            previewValid = true;
            previewInfo = "<b>Park</b>  Move to this spot";
            return;
        }

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

    private void BuildMergePreview()
    {
        int merging = 0;
        int ships = dropFleet.roster.Count;
        float power = Strength.Of(dropFleet);

        foreach (GalacticFleet fleet in selectedFleets)
        {
            if (fleet == null || !fleet.CanReorganizeWith(dropFleet)) continue;
            merging++;
            ships += fleet.roster.Count;
            power += Strength.Of(fleet);
        }

        previewValid = merging > 0;

        List<string> lines = new List<string>
        {
            $"<b>Merge</b>  {MapText.Plural(merging, "fleet")} into this one",
            MapText.Tint($"Result: {MapText.Plural(ships, "ship")}, power {power:0}", MapText.Good)
        };

        int left = selectedFleets.Count - merging;
        if (left > 0) lines.Add(MapText.Tint($"{MapText.Plural(left, "fleet")} not here and won't merge", MapText.Muted));

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

        if (slotMarker != null)
        {
            slotMarker.enabled = false;
        }

        PlanetTooltip.Instance?.SetRouteInfo(null);
    }

    private void HandleRelease()
    {
        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        Plane mapPlane = new Plane(Vector3.up, dragOrigin);
        Vector3 dropPoint = mapPlane.Raycast(ray, out float enter) ? ray.GetPoint(enter) : dragOrigin;
        ResolveDropTarget(ray, dropPoint);

        DropKind kind = dropKind;
        GalacticFleet targetFleet = dropFleet;
        Planet targetPlanet = dropPlanet;
        int targetSlot = dropSlot;

        isDragging = false;
        ClearRoutePreview();
        ResetDropTarget();

        switch (kind)
        {
            case DropKind.Merge:
                MergeSelectedInto(targetFleet);
                break;

            case DropKind.Park:
                dragFleet?.MoveToSlot(targetSlot);
                break;

            case DropKind.Route:
                foreach (GalacticFleet fleet in selectedFleets)
                {
                    if (fleet == null) continue;
                    fleet.TrySetDestination(targetPlanet, fleet == dragFleet ? targetSlot : -1);
                }
                break;
        }
    }

    private void MergeSelectedInto(GalacticFleet target)
    {
        if (target == null) return;

        foreach (GalacticFleet fleet in new List<GalacticFleet>(selectedFleets))
        {
            if (fleet != null && fleet.MergeInto(target))
            {
                selectedFleets.Remove(fleet);
            }
        }

        if (!selectedFleets.Contains(target))
        {
            selectedFleets.Add(target);
        }
        dragFleet = target;
    }

    private void CreateSlotMarker()
    {
        GameObject markerObj = new GameObject("SlotDropMarker");
        markerObj.transform.SetParent(transform);

        slotMarker = markerObj.AddComponent<LineRenderer>();
        slotMarker.loop = true;
        slotMarker.useWorldSpace = true;
        slotMarker.positionCount = 32;
        slotMarker.startWidth = 0.08f;
        slotMarker.endWidth = 0.08f;
        slotMarker.material = new Material(Shader.Find("Sprites/Default"));
        slotMarker.enabled = false;
    }

    private void UpdateSlotMarker(Color color)
    {
        if (slotMarker == null) return;

        bool show = dropPlanet != null && dropSlot >= 0 && (dropKind == DropKind.Park || dropKind == DropKind.Route);
        slotMarker.enabled = show;
        if (!show) return;

        Vector3 centre = dropPlanet.GetSlotPosition(dropSlot) + Vector3.up * routeLineHeight;
        for (int i = 0; i < slotMarker.positionCount; i++)
        {
            float angle = i * Mathf.PI * 2f / slotMarker.positionCount;
            slotMarker.SetPosition(i, centre + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * slotMarkerRadius);
        }
        slotMarker.startColor = color;
        slotMarker.endColor = color;
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
