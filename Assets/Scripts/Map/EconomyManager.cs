using UnityEngine;

public class EconomyManager : MonoBehaviour
{
    public static EconomyManager Instance;

    public float taxTickInterval = 20f;
    private float taxTickTimer;

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
    }

    private void CollectTaxes()
    {
        if (GalacticState.Instance == null) return;

        foreach (Planet planet in FindObjectsByType<Planet>(FindObjectsSortMode.None))
        {
            if (planet.hasTaxOffice && planet.owner != null)
            {
                GalacticState.Instance.AddCurrency(planet.owner, Planet.TaxOfficeIncome);
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
                CompleteBuild(planet, order.ship);
            }
        }
    }

    private void CompleteBuild(Planet planet, Ship ship)
    {
        if (ship == null) return;

        for (int slot = 0; slot < Planet.FleetSlotCount; slot++)
        {
            GalacticFleet occupant = planet.GetFleetInSlot(slot);
            if (occupant != null && occupant.faction == planet.owner)
            {
                occupant.roster.Add(ship);
                return;
            }
        }

        int openSlot = FindOpenSlot(planet);
        if (openSlot < 0)
        {
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
        newFleet.roster.Add(ship);
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
