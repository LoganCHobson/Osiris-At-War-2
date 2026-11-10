using UnityEngine;

public class EconomyManager : MonoBehaviour
{
    public static EconomyManager Instance;

    public float taxTickInterval = 20f;
    private float taxTickTimer;

    // Time since the last tax payout (saved so a loaded game pays out on schedule).
    public float TaxTimer
    {
        get => taxTickTimer;
        set => taxTickTimer = Mathf.Max(0f, value);
    }

    private void Awake()
    {
        Instance = this;
    }

    private void Update()
    {
        taxTickTimer += Time.deltaTime;
        if (taxTickTimer >= taxTickInterval)
        {
            taxTickTimer -= taxTickInterval;
            CollectTaxes();
        }

        AdvanceBuildQueues();
        StandingOrders.Tick(Time.time);
    }

    private void CollectTaxes()
    {
        if (GalacticState.Instance == null) return;

        foreach (Planet planet in FindObjectsByType<Planet>(FindObjectsSortMode.None))
        {
            if (planet.owner != null)
            {
                GalacticState.Instance.AddCurrency(planet.owner, Planet.BaseIncome + (planet.hasTaxOffice ? Planet.TaxOfficeIncome : 0));
            }
        }
    }

    private void AdvanceBuildQueues()
    {
        foreach (Planet planet in FindObjectsByType<Planet>(FindObjectsSortMode.None))
        {
            if (planet.shipBuildQueue.Count == 0) continue;

            ShipBuildOrder order = planet.shipBuildQueue[0];
            order.remainingTime -= Time.deltaTime;

            if (order.remainingTime <= 0f)
            {
                planet.shipBuildQueue.RemoveAt(0);
                if (!ProductionOrders.TryDeliver(planet, order)) CompleteBuild(planet, order.ship);
            }
        }
    }

    private void CompleteBuild(Planet planet, Ship ship)
    {
        if (ship == null) return;

        for (int slot = 0; slot < Planet.FleetSlotCount; slot++)
        {
            GalacticFleet occupant = planet.GetFleetInSlot(slot);
            if (occupant != null && occupant.faction == planet.owner && !occupant.IsTraveling && occupant.currentPlanet == planet
                && string.IsNullOrEmpty(occupant.pendingDelivery)) // Don't mix local builds into a delivery bound elsewhere.
            {
                occupant.ships.Add(ShipEntry.New(ship, planet.owner));
                return;
            }
        }

        int openSlot = FindOpenSlot(planet);
        if (openSlot < 0)
        {
            // Every slot taken by deliveries: ride along with one rather than lose the ship.
            for (int slot = 0; slot < Planet.FleetSlotCount; slot++)
            {
                GalacticFleet occupant = planet.GetFleetInSlot(slot);
                if (occupant != null && occupant.faction == planet.owner && !occupant.IsTraveling && occupant.currentPlanet == planet)
                {
                    occupant.ships.Add(ShipEntry.New(ship, planet.owner));
                    return;
                }
            }

            Debug.LogWarning($"No open fleet slot at {planet.planetName} to deliver the newly built ship - it was lost.");
            return;
        }

        if (FleetPanel.Instance == null)
        {
            Debug.LogWarning("No FleetPanel in scene - can't create a new fleet for a completed ship build.");
            return;
        }

        GalacticFleet newFleet = FleetPanel.Instance.CreateFleetInSlot(planet, openSlot);
        if (newFleet == null) return;

        newFleet.faction = planet.owner;
        newFleet.ships.Add(ShipEntry.New(ship, planet.owner));
    }

    private int FindOpenSlot(Planet planet)
    {
        for (int i = 0; i < Planet.FleetSlotCount; i++)
        {
            if (planet.GetFleetInSlot(i) == null) return i;
        }
        return -1;
    }
}
