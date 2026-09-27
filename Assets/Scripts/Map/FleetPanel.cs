using System.Collections.Generic;
using UnityEngine;

public class FleetPanel : MonoBehaviour
{
    public static FleetPanel Instance;

    public GameObject panelRoot;
    public Transform rowContainer;
    public FleetRowUI rowPrefab;
    public GameObject newFleetPrefab;

    private readonly List<FleetRowUI> spawnedRows = new List<FleetRowUI>();
    private Planet currentPlanet;

    private void Awake()
    {
        Instance = this;
        panelRoot.SetActive(false);
    }

    public void Show(Planet planet)
    {
        currentPlanet = planet;
        Refresh();
        panelRoot.SetActive(true);
    }

    public void Hide()
    {
        currentPlanet = null;
        panelRoot.SetActive(false);
    }

    public void Refresh()
    {
        foreach (FleetRowUI row in spawnedRows)
        {
            Destroy(row.gameObject);
        }
        spawnedRows.Clear();

        if (currentPlanet == null) return;

        for (int slot = 0; slot < Planet.FleetSlotCount; slot++)
        {
            GalacticFleet fleet = currentPlanet.GetFleetInSlot(slot);

            FleetRowUI row = Instantiate(rowPrefab, rowContainer);
            row.Bind(fleet, currentPlanet, slot, this);
            spawnedRows.Add(row);
        }
    }

    public GalacticFleet CreateFleetInSlot(Planet planet, int slot)
    {
        if (newFleetPrefab == null)
        {
            Debug.LogWarning("FleetPanel.newFleetPrefab is not assigned - can't create a new fleet from an empty lane.");
            return null;
        }

        GameObject obj = Instantiate(newFleetPrefab, planet.GetSlotPosition(slot), Quaternion.identity);
        GalacticFleet fleet = obj.GetComponent<GalacticFleet>();
        if (fleet == null)
        {
            Destroy(obj);
            return null;
        }

        fleet.roster.Clear(); // The template prefab may carry stale/unresolved roster entries - a freshly split fleet always starts empty.
        fleet.currentPlanet = planet;
        planet.ForceClaimSlot(fleet, slot);
        return fleet;
    }
}
