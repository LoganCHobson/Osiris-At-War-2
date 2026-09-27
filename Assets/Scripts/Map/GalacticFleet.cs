using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class GalacticFleet : MonoBehaviour
{
    [Header("Roster")]
    public List<Ship> roster = new List<Ship>();

    [Header("Map state")]
    public bool isPlayerFleet = true;
    public Planet currentPlanet;
    public float travelSpeed = 5f;
    public float stopoverDuration = 0.75f;
    public bool preferFriendlyRoute = true;

    public bool IsTraveling { get; private set; }

    private List<Planet> route = new List<Planet>();
    private int routeIndex;
    private int finalSlot = -1;
    private bool isStoppedOver;
    private float stopoverTimer;

    private void Start()
    {
        if (currentPlanet != null)
        {
            SnapToPlanet(currentPlanet);
        }
    }

    private void Update()
    {
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
        if (IsTraveling || currentPlanet == null || planet == null)
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

        List<Planet> path = GalacticPathfinder.FindPath(currentPlanet, planet, preferFriendlyRoute);
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

    public bool TransferShipTo(Ship ship, GalacticFleet destination)
    {
        if (destination == this || IsTraveling || destination.IsTraveling) return false;
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

    private void ArriveAtWaypoint(Planet planet, bool isFinalHop)
    {
        currentPlanet = planet;

        if (!isFinalHop)
        {
            planet.ClaimSlot(this); // Brief stopover parking while passing through.
        }

        if (CheckForEngagement(planet))
        {
            return; // Battle scene is loading - stop right here, mid-route.
        }

        if (isFinalHop)
        {
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

    private void DepartCurrentWaypoint()
    {
        currentPlanet.ReleaseSlot(this);
        routeIndex++;
    }

    private bool CheckForEngagement(Planet planet)
    {
        Debug.Log($"[CheckForEngagement] planet={planet.planetName} isPlayerFleet={isPlayerFleet} contested={planet.contested} roster.Count={roster.Count}");

        if (!isPlayerFleet || !planet.contested)
        {
            return false;
        }

        if (BattleContext.Instance == null)
        {
            Debug.LogWarning("No BattleContext in scene - can't hand off fleet roster to BattleScene.");
            return false;
        }

        for (int i = 0; i < roster.Count; i++)
        {
            Debug.Log($"[CheckForEngagement] roster[{i}] = {(roster[i] == null ? "NULL" : roster[i].name)}");
        }

        BattleContext.Instance.SetIncomingRoster(roster);
        Debug.Log($"[CheckForEngagement] BattleContext.incomingRoster.Count after SetIncomingRoster = {BattleContext.Instance.incomingRoster.Count}");
        SceneManager.LoadScene("BattleScene");
        return true;
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
