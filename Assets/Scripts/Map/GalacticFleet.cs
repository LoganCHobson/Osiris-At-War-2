using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class GalacticFleet : MonoBehaviour
{
    public static readonly List<GalacticFleet> All = new List<GalacticFleet>();

    [Header("Roster")]
    public List<Ship> roster = new List<Ship>();

    [Header("Map state")]
    public Faction faction;
    public Planet currentPlanet;
    public float travelSpeed = 5f;
    public float stopoverDuration = 0.75f;
    public bool preferFriendlyRoute = true;
    public float postBattleHoldDuration = 5f;

    public bool IsTraveling { get; private set; }
    public bool IsHolding => holdTimer > 0f;
    public bool AwaitingBattle { get; set; }
    public bool CanTakeOrders => !IsTraveling && !IsHolding && !AwaitingBattle && currentPlanet != null;
    public float HoldTimeRemaining => Mathf.Max(0f, holdTimer);
    public Planet Destination => IsTraveling && route.Count > 0 ? route[route.Count - 1] : null;
    public Planet NextWaypoint => IsTraveling && routeIndex < route.Count ? route[routeIndex] : null;
    public bool IsRevealed { get; private set; } = true;

    private Renderer[] revealRenderers;
    private Collider[] revealColliders;
    private Canvas[] revealCanvases;

    public bool IsPresentAt(Planet planet)
    {
        return planet != null && currentPlanet == planet && (!IsTraveling || isStoppedOver);
    }

    private float holdTimer;

    private List<Planet> route = new List<Planet>();
    private int routeIndex;
    private int finalSlot = -1;
    private bool isStoppedOver;
    private float stopoverTimer;

    private static readonly int BaseColorID = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorID = Shader.PropertyToID("_Color");
    private MaterialPropertyBlock factionProperties;

    private void OnEnable()
    {
        All.Add(this);
    }

    private void OnDisable()
    {
        All.Remove(this);
    }

    public void SetRevealed(bool revealed)
    {
        if (revealed == IsRevealed) return;

        IsRevealed = revealed;
        revealRenderers ??= GetComponentsInChildren<Renderer>(true);
        revealColliders ??= GetComponentsInChildren<Collider>(true);
        revealCanvases ??= GetComponentsInChildren<Canvas>(true);

        foreach (Renderer part in revealRenderers)
        {
            if (part != null) part.enabled = revealed;
        }
        foreach (Collider part in revealColliders)
        {
            if (part != null) part.enabled = revealed;
        }
        foreach (Canvas part in revealCanvases)
        {
            if (part != null) part.enabled = revealed;
        }
    }

    private void Start()
    {
        UpdateFactionVisual();

        if (currentPlanet != null && !IsTraveling)
        {
            SnapToPlanet(currentPlanet);
        }
    }

    private void OnValidate()
    {
        UpdateFactionVisual();
    }

    private void Update()
    {
        if (AwaitingBattle) return;

        if (holdTimer > 0f)
        {
            holdTimer -= Time.deltaTime;
        }

        if (!IsTraveling) return;

        if (isStoppedOver)
        {
            stopoverTimer -= Time.deltaTime;
            if (stopoverTimer <= 0f)
            {
                isStoppedOver = false;
                DepartCurrentWaypoint();
            }
            return;
        }

        Planet target = route[routeIndex];
        bool isFinalHop = routeIndex == route.Count - 1;
        Vector3 targetPosition = isFinalHop && finalSlot >= 0 ? target.GetSlotPosition(finalSlot) : target.transform.position;

        transform.position = Vector3.MoveTowards(transform.position, targetPosition, travelSpeed * Time.deltaTime);

        if (Vector3.Distance(transform.position, targetPosition) <= 0.05f)
        {
            ArriveAtWaypoint(target, isFinalHop);
        }
    }

    public bool TrySetDestination(Planet planet)
    {
        if (IsTraveling || IsHolding || currentPlanet == null || planet == null)
        {
            return false;
        }

        if (planet == currentPlanet)
        {
            // Re-parking at the same planet - just claim a fresh slot, no travel needed.
            currentPlanet.ReleaseSlot(this);
            int slot = currentPlanet.ClaimSlot(this);
            transform.position = slot >= 0 ? currentPlanet.GetSlotPosition(slot) : currentPlanet.transform.position;
            return true;
        }

        List<Planet> path = PlanRoute(planet);
        if (path.Count < 2)
        {
            return false; // No hyperspace route exists to that planet.
        }

        currentPlanet.ReleaseSlot(this);
        route = path;
        routeIndex = 1; // route[0] is the planet we're departing from.
        finalSlot = planet.ClaimSlot(this);
        IsTraveling = true;
        return true;
    }

    public List<Planet> PlanRoute(Planet planet)
    {
        if (currentPlanet == null || planet == null) return new List<Planet>();
        return GalacticPathfinder.FindPath(currentPlanet, planet, preferFriendlyRoute ? faction : null);
    }

    public float EstimateTravelTime(List<Planet> path)
    {
        if (path == null || path.Count < 2 || travelSpeed <= 0f) return 0f;

        float distance = Vector3.Distance(transform.position, path[1].transform.position);
        for (int i = 1; i < path.Count - 1; i++)
        {
            distance += Vector3.Distance(path[i].transform.position, path[i + 1].transform.position);
        }

        return distance / travelSpeed + Mathf.Max(0, path.Count - 2) * stopoverDuration;
    }

    public float RemainingTravelTime()
    {
        if (!IsTraveling || travelSpeed <= 0f || routeIndex >= route.Count) return 0f;

        float distance = Vector3.Distance(transform.position, route[routeIndex].transform.position);
        for (int i = routeIndex; i < route.Count - 1; i++)
        {
            distance += Vector3.Distance(route[i].transform.position, route[i + 1].transform.position);
        }

        int stopoversLeft = Mathf.Max(0, route.Count - 1 - routeIndex - (isStoppedOver ? 1 : 0));
        return distance / travelSpeed + stopoversLeft * stopoverDuration + (isStoppedOver ? stopoverTimer : 0f);
    }

    public bool TransferShipTo(Ship ship, GalacticFleet destination)
    {
        if (destination == this || IsTraveling || destination.IsTraveling) return false;
        if (destination.faction != faction) return false; // Can't reorganize ships across factions.
        if (currentPlanet == null || currentPlanet != destination.currentPlanet) return false;
        if (!roster.Remove(ship)) return false;

        destination.roster.Add(ship);

        if (roster.Count == 0)
        {
            currentPlanet.ReleaseSlot(this);
            Destroy(gameObject);
        }

        return true;
    }

    public void RemoveShip(Ship ship)
    {
        if (!roster.Remove(ship)) return;

        if (roster.Count == 0)
        {
            currentPlanet?.ReleaseSlot(this);
            Destroy(gameObject);
        }
    }

    public void PlaceAt(Planet planet)
    {
        if (planet == null) return;

        currentPlanet?.ReleaseSlot(this);
        currentPlanet = planet;
        SnapToPlanet(planet);
    }

    private void ArriveAtWaypoint(Planet planet, bool isFinalHop)
    {
        currentPlanet = planet;

        if (!isFinalHop)
        {
            planet.ClaimSlot(this); // Brief stopover parking while passing through.
        }

        if (TryStartBattle(planet))
        {
            return; // Battle scene is loading - stop right here, mid-route.
        }

        TryCapturePlanet(planet);

        if (isFinalHop)
        {
            if (finalSlot < 0)
            {
                finalSlot = planet.ClaimSlot(this); // A slot may have freed up during the trip - try again before giving up.
                if (finalSlot >= 0)
                {
                    transform.position = planet.GetSlotPosition(finalSlot);
                }
            }

            if (finalSlot < 0 && MergeIntoFirstSlot(planet))
            {
                return; // Absorbed into the fleet already parked there - this fleet object is gone.
            }

            IsTraveling = false;
            route.Clear();
            routeIndex = 0;
            finalSlot = -1;
        }
        else
        {
            isStoppedOver = true;
            stopoverTimer = stopoverDuration;
        }
    }

    private bool MergeIntoFirstSlot(Planet planet)
    {
        GalacticFleet targetFleet = planet.GetFleetInSlot(0);
        if (targetFleet == null || targetFleet == this || targetFleet.faction != faction || targetFleet.IsTraveling || targetFleet.currentPlanet != planet)
        {
            return false;
        }

        targetFleet.roster.AddRange(roster);
        roster.Clear();
        Destroy(gameObject);
        return true;
    }

    private void DepartCurrentWaypoint()
    {
        currentPlanet.ReleaseSlot(this);
        routeIndex++;
    }

    public bool HasBattleAt(Planet planet)
    {
        bool planetIsDefended = planet.owner != faction && (planet.hasCapitalShipyard || planet.hasBattleStation);
        return FindOpposingFleet(planet) != null || planetIsDefended;
    }

    private bool TryStartBattle(Planet planet)
    {
        if (!HasBattleAt(planet))
        {
            return false;
        }

        GalacticFleet defender = FindOpposingFleet(planet);
        Faction defendingFaction = defender != null ? defender.faction : planet.owner;
        if (!Strength.IsPlayer(faction) && !Strength.IsPlayer(defendingFaction))
        {
            ResolveAutomatically(planet);
            return true;
        }

        AwaitingBattle = true;
        BattlePrompt.Request(this, planet);
        return true;
    }

    public bool ResolveAutomatically(Planet planet)
    {
        AwaitingBattle = false;

        if (!AutoResolver.ResolveAll(this, planet)) return false;

        HaltAt(planet);
        HoldAfterBattle();
        return true;
    }

    public void LaunchBattle(Planet planet)
    {
        AwaitingBattle = false;

        if (BattleContext.Instance == null)
        {
            Debug.LogWarning("No BattleContext in scene - can't hand off fleets to BattleScene.");
            return;
        }

        GalacticState.Instance?.CaptureFromScene();
        BattleContext.Instance.BeginBattle(this, planet);
        SceneManager.LoadScene("BattleScene");
    }

    private void TryCapturePlanet(Planet planet)
    {
        if (planet.owner == faction) return;
        if (FindOpposingFleet(planet) != null) return;

        planet.Capture(faction);
    }

    public void HoldAfterBattle()
    {
        holdTimer = postBattleHoldDuration;
    }

    public Planet RetreatFrom(Planet from)
    {
        if (from == null) from = currentPlanet;

        List<Planet> path = GalacticPathfinder.FindNearest(from, planet => planet.owner == faction);
        if (path.Count < 2)
        {
            path = GalacticPathfinder.FindNearest(from, planet => planet.owner == null || planet.owner.isNeutral);
        }

        if (path.Count < 2)
        {
            roster.Clear();
            Destroy(gameObject);
            return null;
        }

        from.ReleaseSlot(this);
        currentPlanet = from;
        AwaitingBattle = false;
        isStoppedOver = false;
        route = path;
        routeIndex = 1;
        Planet haven = path[path.Count - 1];
        finalSlot = haven.ClaimSlot(this);
        IsTraveling = true;
        return haven;
    }

    private void HaltAt(Planet planet)
    {
        Planet destination = Destination;
        if (destination != null && destination != planet)
        {
            destination.ReleaseSlot(this);
        }

        IsTraveling = false;
        isStoppedOver = false;
        route.Clear();
        routeIndex = 0;
        finalSlot = -1;

        currentPlanet = planet;
        int slot = planet.ClaimSlot(this);
        transform.position = slot >= 0 ? planet.GetSlotPosition(slot) : planet.transform.position;
    }

    public GalacticFleet FindOpposingFleet(Planet planet)
    {
        for (int slot = 0; slot < Planet.FleetSlotCount; slot++)
        {
            GalacticFleet occupant = planet.GetFleetInSlot(slot);
            if (occupant != null && occupant.faction != faction && occupant.IsPresentAt(planet))
            {
                return occupant;
            }
        }
        return null;
    }

    private void UpdateFactionVisual()
    {
        factionProperties ??= new MaterialPropertyBlock();
        Color tint = faction != null ? faction.color : Color.gray;

        foreach (MeshRenderer meshRenderer in GetComponentsInChildren<MeshRenderer>())
        {
            meshRenderer.GetPropertyBlock(factionProperties);
            factionProperties.SetColor(BaseColorID, tint);
            factionProperties.SetColor(ColorID, tint);
            meshRenderer.SetPropertyBlock(factionProperties);
        }
    }

    private void SnapToPlanet(Planet planet)
    {
        int slot = planet.ClaimSlot(this);
        transform.position = slot >= 0 ? planet.GetSlotPosition(slot) : planet.transform.position;
    }

    private void OnDestroy()
    {
        currentPlanet?.ReleaseSlot(this);
    }
}
