using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class BattleReportPanel : MonoBehaviour
{
    public static BattleReportPanel Instance { get; private set; }
    public static bool IsOpen => Instance != null && Instance.panel.activeSelf;

    private static BattleResult pendingResult;

    public GameObject panel;
    public TMP_Text titleText;
    public TMP_Text subtitleText;
    public TMP_Text playerHeaderText;
    public TMP_Text enemyHeaderText;
    public Transform playerLossesContainer;
    public Transform enemyLossesContainer;
    public GameObject playerNoLossesText;
    public GameObject enemyNoLossesText;
    public BattleReportRow rowPrefab;

    private readonly List<GameObject> spawnedRows = new List<GameObject>();
    private Action onClosed;

    public static void Show(BattleResult result, Action closed = null)
    {
        if (result == null)
        {
            closed?.Invoke();
            return;
        }

        if (Instance == null)
        {
            pendingResult = result;
            return;
        }

        Instance.Display(result, closed);
    }

    private void Awake()
    {
        Instance = this;
        panel.SetActive(false);
    }

    private void Start()
    {
        if (pendingResult == null) return;

        BattleResult result = pendingResult;
        pendingResult = null;
        Display(result, null);
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void Display(BattleResult result, Action closed)
    {
        onClosed = closed;

        FleetPanel.Instance?.Hide();
        PlanetBuildPanel.Instance?.Hide();

        string outcome = result.playerWon ? MapText.Tint("VICTORY", MapText.Good) : MapText.Tint("DEFEAT", MapText.Bad);
        titleText.text = $"{outcome} at {result.planetName}";
        subtitleText.text = Subtitle(result);

        playerHeaderText.text = "Your Losses";
        enemyHeaderText.text = $"{MapText.Colored(result.enemy, "Enemy")} Losses";

        ClearRows();
        bool playerLostAny = FillColumn(playerLossesContainer, result.playerLosses, result.playerStructuresLost);
        bool enemyLostAny = FillColumn(enemyLossesContainer, result.enemyLosses, result.enemyStructuresLost);
        playerNoLossesText.SetActive(!playerLostAny);
        enemyNoLossesText.SetActive(!enemyLostAny);

        panel.SetActive(true);
        GameSpeed.SetSuspended(true);
    }

    public void Continue()
    {
        panel.SetActive(false);
        ClearRows();
        GameSpeed.SetSuspended(false);

        Action closed = onClosed;
        onClosed = null;
        closed?.Invoke();
    }

    private static string Subtitle(BattleResult result)
    {
        string resolution = result.autoResolved ? "Auto-resolved" : "Tactical engagement";
        if (result.planetCaptured) return $"{result.planetName} captured  -  {resolution}";
        if (result.planetLost) return MapText.Tint($"{result.planetName} has fallen", MapText.Bad) + $"  -  {resolution}";
        return MapText.Tint(resolution, MapText.Muted);
    }

    private bool FillColumn(Transform container, List<Ship> ships, List<string> structures)
    {
        List<Ship> order = new List<Ship>();
        Dictionary<Ship, int> counts = new Dictionary<Ship, int>();
        foreach (Ship ship in ships)
        {
            if (ship == null) continue;
            if (!counts.ContainsKey(ship))
            {
                counts[ship] = 0;
                order.Add(ship);
            }
            counts[ship]++;
        }

        foreach (Ship ship in order)
        {
            AddRow(container, ship.IconSprite, ship.name, counts[ship]);
        }

        foreach (string structure in structures)
        {
            AddRow(container, null, structure, 1);
        }

        return order.Count > 0 || structures.Count > 0;
    }

    private void AddRow(Transform container, Sprite icon, string label, int count)
    {
        BattleReportRow row = Instantiate(rowPrefab, container);
        row.gameObject.SetActive(true);
        row.Bind(icon, label, count);
        spawnedRows.Add(row.gameObject);
    }

    private void ClearRows()
    {
        foreach (GameObject row in spawnedRows)
        {
            if (row != null)
            {
                Destroy(row);
            }
        }
        spawnedRows.Clear();
    }
}
