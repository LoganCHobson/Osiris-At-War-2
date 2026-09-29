using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class PlanetBuildPanel : MonoBehaviour
{
    public static PlanetBuildPanel Instance;

    [Header("Panel")]
    public GameObject panelRoot;
    public TMP_Text planetNameText;
    public TMP_Text currencyText;

    [Header("Buildings")]
    public Button taxOfficeButton;
    public Button capitalShipyardButton;
    public Button battleStationButton;

    [Header("Ship Construction")]
    public Transform shipButtonContainer;
    public Button shipButtonPrefab;
    public Transform queueListContainer;
    public TMP_Text queueEntryPrefab;

    [Header("Debug")]
    public bool debugBuildOnAnyPlanet = false;

    private TMP_Text taxOfficeLabel;
    private TMP_Text capitalShipyardLabel;
    private TMP_Text battleStationLabel;

    private Planet currentPlanet;
    private int lastQueueCount = -1;
    private readonly List<GameObject> spawnedShipButtons = new List<GameObject>();
    private readonly List<Ship> shownShips = new List<Ship>();
    private readonly List<TMP_Text> spawnedQueueEntries = new List<TMP_Text>();

    private Faction Builder => currentPlanet != null ? currentPlanet.owner : null;

    private void Awake()
    {
        Instance = this;
        panelRoot.SetActive(false);

        taxOfficeLabel = taxOfficeButton != null ? taxOfficeButton.GetComponentInChildren<TMP_Text>() : null;
        capitalShipyardLabel = capitalShipyardButton != null ? capitalShipyardButton.GetComponentInChildren<TMP_Text>() : null;
        battleStationLabel = battleStationButton != null ? battleStationButton.GetComponentInChildren<TMP_Text>() : null;
    }

    private void Update()
    {
        if (currentPlanet == null || panelRoot == null || !panelRoot.activeSelf) return;

        RefreshHeader();
        RefreshButtonStates();

        if (currentPlanet.shipBuildQueue.Count != lastQueueCount)
        {
            RebuildQueueList();
        }
        else
        {
            UpdateQueueText();
        }
    }

    public void Show(Planet planet)
    {
        currentPlanet = planet;
        panelRoot.SetActive(true);
        lastQueueCount = -1; // Force a queue rebuild on open.

        RebuildShipButtons();
        RefreshHeader();
        RefreshButtonStates();
        RebuildQueueList();
    }

    public void Hide()
    {
        currentPlanet = null;
        panelRoot.SetActive(false);
    }

    private bool CanBuildHere()
    {
        if (currentPlanet == null) return false;
        if (debugBuildOnAnyPlanet) return true;

        return currentPlanet.owner != null && currentPlanet.owner.isPlayerFaction;
    }

    private void RefreshHeader()
    {
        if (planetNameText != null)
        {
            planetNameText.text = currentPlanet.planetName;
        }

        if (currencyText != null)
        {
            int currency = currentPlanet.owner != null && GalacticState.Instance != null
                ? GalacticState.Instance.GetCurrency(currentPlanet.owner)
                : 0;
            currencyText.text = $"${currency}";
        }
    }

    private void RefreshButtonStates()
    {
        bool canBuild = CanBuildHere();
        bool known = canBuild || FogOfWar.CanSee(currentPlanet);
        int currency = !canBuild ? 0
            : debugBuildOnAnyPlanet ? int.MaxValue
            : GalacticState.Instance != null ? GalacticState.Instance.GetCurrency(currentPlanet.owner)
            : 0;

        if (taxOfficeButton != null)
        {
            taxOfficeButton.interactable = canBuild && !currentPlanet.hasTaxOffice && currency >= Planet.TaxOfficeCost;
        }
        if (taxOfficeLabel != null)
        {
            taxOfficeLabel.text = known && currentPlanet.hasTaxOffice ? "Tax Office (built)" : $"Build Tax Office (${Planet.TaxOfficeCost})";
        }

        if (capitalShipyardButton != null)
        {
            capitalShipyardButton.interactable = canBuild && currentPlanet.canBuildCapitalShipyard && !currentPlanet.hasCapitalShipyard && currency >= Planet.CapitalShipyardCost;
        }
        if (capitalShipyardLabel != null)
        {
            capitalShipyardLabel.text = known && currentPlanet.hasCapitalShipyard ? "Capital Shipyard (built)"
                : !currentPlanet.canBuildCapitalShipyard ? "No Shipyard Site"
                : $"Build Capital Shipyard (${Planet.CapitalShipyardCost})";
        }

        if (battleStationButton != null)
        {
            battleStationButton.interactable = canBuild && !currentPlanet.hasBattleStation && currency >= Planet.BattleStationCost;
        }
        if (battleStationLabel != null)
        {
            battleStationLabel.text = known && currentPlanet.hasBattleStation ? "Battle Station (built)" : $"Build Battle Station (${Planet.BattleStationCost})";
        }

        bool shipyardReady = canBuild && currentPlanet.hasCapitalShipyard;
        for (int i = 0; i < spawnedShipButtons.Count && i < shownShips.Count; i++)
        {
            Ship ship = shownShips[i];
            Button button = spawnedShipButtons[i].GetComponent<Button>();
            if (button == null || ship == null) continue;

            button.interactable = shipyardReady && currency >= ship.cost;
        }
    }

    public void BuildTaxOffice()
    {
        if (!TrySpend(Planet.TaxOfficeCost)) return;
        currentPlanet.hasTaxOffice = true;
    }

    public void BuildCapitalShipyard()
    {
        if (currentPlanet == null || !currentPlanet.canBuildCapitalShipyard || currentPlanet.hasCapitalShipyard) return;
        if (!TrySpend(Planet.CapitalShipyardCost)) return;
        currentPlanet.hasCapitalShipyard = true;
    }

    public void BuildBattleStation()
    {
        if (!TrySpend(Planet.BattleStationCost)) return;
        currentPlanet.hasBattleStation = true;
    }

    private bool TrySpend(int amount)
    {
        if (!CanBuildHere()) return false;
        if (debugBuildOnAnyPlanet) return true;
        if (GalacticState.Instance == null) return false;

        return GalacticState.Instance.TrySpend(currentPlanet.owner, amount);
    }

    private void QueueShip(Ship ship)
    {
        if (ship == null || !CanBuildHere() || !currentPlanet.hasCapitalShipyard) return;
        if (!TrySpend(ship.cost)) return;

        currentPlanet.shipBuildQueue.Add(new ShipBuildOrder { ship = ship, remainingTime = BuildTime(ship) });
    }

    private float BuildTime(Ship ship)
    {
        return Builder != null ? Builder.BuildTime(ship) : ship.buildTime;
    }

    private void RebuildShipButtons()
    {
        foreach (GameObject obj in spawnedShipButtons)
        {
            Destroy(obj);
        }
        spawnedShipButtons.Clear();
        shownShips.Clear();

        if (shipButtonContainer == null || shipButtonPrefab == null || Builder == null) return;

        foreach (Ship ship in Builder.roster)
        {
            if (ship == null) continue;

            Button button = Instantiate(shipButtonPrefab, shipButtonContainer);
            Ship capturedShip = ship;
            button.onClick.AddListener(() => QueueShip(capturedShip));

            TMP_Text label = button.GetComponentInChildren<TMP_Text>();
            if (label != null)
            {
                label.text = $"{ship.name} (${ship.cost}, {BuildTime(ship):0}s)";
            }

            spawnedShipButtons.Add(button.gameObject);
            shownShips.Add(ship);
        }
    }

    private void RebuildQueueList()
    {
        foreach (TMP_Text entry in spawnedQueueEntries)
        {
            Destroy(entry.gameObject);
        }
        spawnedQueueEntries.Clear();

        lastQueueCount = currentPlanet.shipBuildQueue.Count;

        if (queueListContainer == null || queueEntryPrefab == null) return;

        foreach (ShipBuildOrder order in currentPlanet.shipBuildQueue)
        {
            if (order.ship == null) continue;

            TMP_Text entry = Instantiate(queueEntryPrefab, queueListContainer);
            spawnedQueueEntries.Add(entry);
        }

        UpdateQueueText();
    }

    private void UpdateQueueText()
    {
        for (int i = 0; i < spawnedQueueEntries.Count && i < currentPlanet.shipBuildQueue.Count; i++)
        {
            ShipBuildOrder order = currentPlanet.shipBuildQueue[i];
            if (order.ship == null) continue;

            spawnedQueueEntries[i].text = $"{order.ship.name} - {Mathf.CeilToInt(order.remainingTime)}s";
        }
    }
}
