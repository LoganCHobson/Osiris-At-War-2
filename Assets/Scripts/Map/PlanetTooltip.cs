using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

public class PlanetTooltip : MonoBehaviour
{
    public static PlanetTooltip Instance { get; private set; }

    [Header("UI")]
    public RectTransform panel;
    public TMP_Text titleText;
    public TMP_Text bodyText;
    public TMP_Text routeText;

    [Header("Behaviour")]
    public Vector2 cursorOffset = new Vector2(24f, 24f);
    public float refreshInterval = 0.25f;

    private readonly StringBuilder builder = new StringBuilder();
    private readonly Dictionary<Faction, (int ships, float power)> fleetsByFaction = new Dictionary<Faction, (int, float)>();
    private readonly Dictionary<string, int> composition = new Dictionary<string, int>();

    private RectTransform canvasRect;
    private Camera cam;
    private Planet shownPlanet;
    private GalacticFleet shownFleet;
    private string routeInfo;
    private bool routeDirty;
    private float nextRefreshAt;

    private void Awake()
    {
        Instance = this;
        canvasRect = panel.parent as RectTransform;
        panel.gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public void SetRouteInfo(string info)
    {
        if (info == routeInfo) return;
        routeInfo = info;
        routeDirty = true;
    }

    private void Update()
    {
        if (!FindHoverTarget(out Planet planet, out GalacticFleet fleet))
        {
            Hide();
            return;
        }

        bool targetChanged = planet != shownPlanet || fleet != shownFleet;
        if (targetChanged || routeDirty || Time.unscaledTime >= nextRefreshAt)
        {
            shownPlanet = planet;
            shownFleet = fleet;
            routeDirty = false;
            nextRefreshAt = Time.unscaledTime + refreshInterval;

            if (fleet != null)
            {
                DescribeFleet(fleet);
            }
            else
            {
                DescribePlanet(planet);
            }

            bool showRoute = planet != null && !string.IsNullOrEmpty(routeInfo);
            routeText.gameObject.SetActive(showRoute);
            if (showRoute)
            {
                routeText.text = routeInfo;
            }
        }

        if (!panel.gameObject.activeSelf)
        {
            panel.gameObject.SetActive(true);
        }

        FollowCursor();
    }

    private bool FindHoverTarget(out Planet planet, out GalacticFleet fleet)
    {
        planet = null;
        fleet = null;

        GalacticMapManager map = GalacticMapManager.Instance;
        if (map == null || BattlePrompt.IsOpen) return false;
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return false;

        if (cam == null) cam = Camera.main;
        if (cam == null) return false;

        Ray ray = cam.ScreenPointToRay(Input.mousePosition);

        if (!map.IsDraggingFleet && Physics.Raycast(ray, out RaycastHit fleetHit, Mathf.Infinity, map.fleetLayer))
        {
            fleet = fleetHit.collider.GetComponentInParent<GalacticFleet>();
            if (fleet != null) return true;
        }

        if (Physics.Raycast(ray, out RaycastHit planetHit, Mathf.Infinity, map.planetLayer))
        {
            planet = planetHit.collider.GetComponentInParent<Planet>();
            if (planet != null) return true;
        }

        return false;
    }

    private void Hide()
    {
        shownPlanet = null;
        shownFleet = null;
        if (panel.gameObject.activeSelf)
        {
            panel.gameObject.SetActive(false);
        }
    }

    private void FollowCursor()
    {
        if (canvasRect == null) return;

        Vector2 mouse = Input.mousePosition;
        bool flipX = mouse.x > Screen.width * 0.7f;
        bool flipY = mouse.y < Screen.height * 0.35f;

        panel.pivot = new Vector2(flipX ? 1f : 0f, flipY ? 0f : 1f);

        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, mouse, null, out Vector2 local);
        Vector2 fromBottomLeft = local - canvasRect.rect.min;
        Vector2 offset = new Vector2(flipX ? -cursorOffset.x : cursorOffset.x, flipY ? cursorOffset.y : -cursorOffset.y);

        panel.anchoredPosition = fromBottomLeft + offset;
    }

    private void DescribePlanet(Planet planet)
    {
        titleText.text = planet.owner != null
            ? $"<color=#{ColorUtility.ToHtmlStringRGB(planet.owner.color)}>{planet.planetName}</color>"
            : planet.planetName;

        builder.Clear();
        builder.Append("Owner: ").Append(MapText.Colored(planet.owner)).Append('\n');

        if (planet.owner != null)
        {
            int income = Planet.BaseIncome + (planet.hasTaxOffice ? Planet.TaxOfficeIncome : 0);
            float interval = EconomyManager.Instance != null ? EconomyManager.Instance.taxTickInterval : 20f;
            builder.Append("Income: ").Append(MapText.Tint($"+{income}", MapText.Good)).Append($" every {MapText.RealTime(interval)}\n");
        }

        builder.Append("Buildings: ");
        int buildingCount = 0;
        AppendBuilding(planet.hasTaxOffice, "Tax Office", ref buildingCount);
        AppendBuilding(planet.hasCapitalShipyard, "Capital Shipyard", ref buildingCount);
        AppendBuilding(planet.hasBattleStation, "Battle Station", ref buildingCount);
        if (buildingCount == 0) builder.Append(MapText.Tint("None", MapText.Muted));
        builder.Append('\n');

        if (planet.canBuildCapitalShipyard && !planet.hasCapitalShipyard)
        {
            builder.Append(MapText.Tint("Shipyard site - can build a Capital Shipyard", MapText.Site)).Append('\n');
        }

        if (planet.shipBuildQueue.Count > 0 && planet.owner != null && planet.owner.isPlayerFaction)
        {
            ShipBuildOrder current = planet.shipBuildQueue[0];
            builder.Append("Building: ").Append(current.ship != null ? current.ship.name : "?")
                .Append($" ({MapText.RealTime(current.remainingTime)})");
            if (planet.shipBuildQueue.Count > 1) builder.Append($"  +{planet.shipBuildQueue.Count - 1} queued");
            builder.Append('\n');
        }

        AppendFleetsAt(planet);

        int lanes = 0;
        foreach (Planet connection in planet.connections)
        {
            if (connection != null) lanes++;
        }
        builder.Append(MapText.Tint($"{MapText.Plural(lanes, "hyperspace lane")}", MapText.Muted));

        bodyText.text = builder.ToString();
    }

    private void AppendBuilding(bool has, string label, ref int count)
    {
        if (!has) return;
        if (count > 0) builder.Append(", ");
        builder.Append(label);
        count++;
    }

    private void AppendFleetsAt(Planet planet)
    {
        fleetsByFaction.Clear();

        for (int slot = 0; slot < Planet.FleetSlotCount; slot++)
        {
            GalacticFleet fleet = planet.GetFleetInSlot(slot);
            if (fleet == null || !fleet.IsPresentAt(planet) || fleet.faction == null) continue;

            fleetsByFaction.TryGetValue(fleet.faction, out (int ships, float power) totals);
            fleetsByFaction[fleet.faction] = (totals.ships + fleet.roster.Count, totals.power + Strength.Of(fleet));
        }

        if (fleetsByFaction.Count == 0)
        {
            builder.Append("Fleets: ").Append(MapText.Tint("None", MapText.Muted)).Append('\n');
            return;
        }

        builder.Append("Fleets:\n");
        foreach (KeyValuePair<Faction, (int ships, float power)> entry in fleetsByFaction)
        {
            builder.Append("  ").Append(MapText.Colored(entry.Key))
                .Append($"  {MapText.Plural(entry.Value.ships, "ship")}  |  strength {entry.Value.power:0}\n");
        }
    }

    private void DescribeFleet(GalacticFleet fleet)
    {
        titleText.text = $"{MapText.Colored(fleet.faction, "Unaligned")} Fleet";

        builder.Clear();
        builder.Append($"{MapText.Plural(fleet.roster.Count, "ship")}  |  strength {Strength.Of(fleet):0}\n");

        composition.Clear();
        foreach (Ship ship in fleet.roster)
        {
            string shipName = ship != null ? ship.name : "Unknown";
            composition.TryGetValue(shipName, out int count);
            composition[shipName] = count + 1;
        }
        foreach (KeyValuePair<string, int> entry in composition)
        {
            builder.Append($"  {entry.Value}x {entry.Key}\n");
        }

        if (fleet.AwaitingBattle)
        {
            builder.Append(MapText.Tint("Engaged in battle", MapText.Bad));
        }
        else if (fleet.IsTraveling)
        {
            Planet destination = fleet.Destination;
            builder.Append($"In transit to {(destination != null ? destination.planetName : "?")} - {MapText.RealTime(fleet.RemainingTravelTime())}");
        }
        else if (fleet.IsHolding)
        {
            builder.Append(MapText.Tint($"Regrouping after battle ({MapText.RealTime(fleet.HoldTimeRemaining)})", MapText.Warning));
        }
        else if (fleet.currentPlanet != null)
        {
            builder.Append($"Stationed at {fleet.currentPlanet.planetName}");
        }

        bodyText.text = builder.ToString();
    }
}
