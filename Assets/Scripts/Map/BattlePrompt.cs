using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class BattlePrompt : MonoBehaviour
{
    private class PendingBattle
    {
        public GalacticFleet attacker;
        public Planet planet;
    }

    public static BattlePrompt Instance { get; private set; }
    public static bool IsOpen => Instance != null && Instance.current != null;

    [Header("Prompt")]
    public GameObject promptPanel;
    public TMP_Text titleText;
    public TMP_Text detailText;
    public Button battleNowButton;

    [Header("Result")]
    public GameObject resultPanel;
    public TMP_Text resultText;
    public float resultDisplayTime = 5f;

    private readonly Queue<PendingBattle> queue = new Queue<PendingBattle>();
    private PendingBattle current;
    private float hideResultAt;

    public static void Request(GalacticFleet attacker, Planet planet)
    {
        if (Instance == null)
        {
            Debug.LogWarning("No BattlePrompt in scene - launching the battle directly.");
            attacker.LaunchBattle(planet);
            return;
        }

        Instance.Enqueue(attacker, planet);
    }

    private void Awake()
    {
        Instance = this;
        promptPanel.SetActive(false);
        resultPanel.SetActive(false);
    }

    private void OnDestroy()
    {
        if (Instance != this) return;

        Instance = null;
        GameSpeed.SetSuspended(false);
    }

    private void Update()
    {
        if (resultPanel.activeSelf && Time.unscaledTime >= hideResultAt)
        {
            resultPanel.SetActive(false);
        }
    }

    private void Enqueue(GalacticFleet attacker, Planet planet)
    {
        if (current != null && current.attacker == attacker) return;
        foreach (PendingBattle pending in queue)
        {
            if (pending.attacker == attacker) return;
        }

        queue.Enqueue(new PendingBattle { attacker = attacker, planet = planet });

        if (current == null)
        {
            ShowNext();
        }
    }

    private void ShowNext()
    {
        while (queue.Count > 0)
        {
            PendingBattle next = queue.Dequeue();
            if (next.attacker == null || next.planet == null || next.attacker.roster.Count == 0) continue;

            if (!next.attacker.HasBattleAt(next.planet))
            {
                next.attacker.AwaitingBattle = false;
                continue;
            }

            current = next;
            GameSpeed.SetSuspended(true);

            FleetPanel.Instance?.Hide();
            PlanetBuildPanel.Instance?.Hide();

            Describe(next);
            if (battleNowButton != null)
            {
                battleNowButton.interactable = BattleContext.Instance != null;
            }
            resultPanel.SetActive(false);
            promptPanel.SetActive(true);
            return;
        }

        current = null;
        promptPanel.SetActive(false);
        GameSpeed.SetSuspended(false);
    }

    private void Describe(PendingBattle battle)
    {
        GalacticFleet attacker = battle.attacker;
        Planet planet = battle.planet;
        bool playerAttacking = Strength.IsPlayer(attacker.faction);

        float attackerPower = Strength.Of(attacker);
        float defenderPower = OpposingPower(attacker, planet);

        if (playerAttacking)
        {
            GalacticFleet defender = attacker.FindOpposingFleet(planet);
            Faction enemy = defender != null ? defender.faction : planet.owner;
            titleText.text = $"Your fleet engages {Colored(enemy)} at {planet.planetName}";
            detailText.text = $"Your strength: {attackerPower:0}     Enemy strength: {defenderPower:0}";
        }
        else
        {
            titleText.text = $"{Colored(attacker.faction)} is attacking {planet.planetName}!";
            detailText.text = $"Your strength: {defenderPower:0}     Enemy strength: {attackerPower:0}";
        }
    }

    public void BattleNow()
    {
        if (current == null) return;

        PendingBattle battle = current;
        current = null;
        queue.Clear();
        promptPanel.SetActive(false);
        GameSpeed.SetSuspended(false);

        battle.attacker.LaunchBattle(battle.planet);
    }

    public void AutoResolve()
    {
        if (current == null) return;

        PendingBattle battle = current;
        current = null;
        promptPanel.SetActive(false);

        List<GalacticFleet> involved = FleetsAt(battle.planet);
        if (!involved.Contains(battle.attacker))
        {
            involved.Add(battle.attacker);
        }

        int playerBefore = CountShips(involved, true);
        int enemyBefore = CountShips(involved, false);
        bool playerAttacking = Strength.IsPlayer(battle.attacker.faction);

        bool attackerWon = battle.attacker.ResolveAutomatically(battle.planet);

        int playerLost = playerBefore - CountShips(involved, true);
        int enemyLost = enemyBefore - CountShips(involved, false);
        bool playerWon = playerAttacking == attackerWon;

        string outcome = playerWon ? "<color=#7CFC7C>VICTORY</color>" : "<color=#FF6060>DEFEAT</color>";
        string captured = attackerWon && playerAttacking ? $" - {battle.planet.planetName} captured" : "";
        resultText.text = $"{outcome} at {battle.planet.planetName}{captured}\n<size=80%>You lost {playerLost} ship{(playerLost == 1 ? "" : "s")}   |   Enemy lost {enemyLost} ship{(enemyLost == 1 ? "" : "s")}</size>";
        resultPanel.SetActive(true);
        hideResultAt = Time.unscaledTime + resultDisplayTime;

        ShowNext();
    }

    private static float OpposingPower(GalacticFleet attacker, Planet planet)
    {
        float power = planet.owner != attacker.faction ? Strength.Defenses(planet) : 0f;
        foreach (GalacticFleet fleet in FleetsAt(planet))
        {
            if (fleet.faction != attacker.faction)
            {
                power += Strength.Of(fleet);
            }
        }
        return power;
    }

    private static List<GalacticFleet> FleetsAt(Planet planet)
    {
        List<GalacticFleet> fleets = new List<GalacticFleet>();
        for (int slot = 0; slot < Planet.FleetSlotCount; slot++)
        {
            GalacticFleet fleet = planet.GetFleetInSlot(slot);
            if (fleet != null && fleet.IsPresentAt(planet) && !fleets.Contains(fleet))
            {
                fleets.Add(fleet);
            }
        }
        return fleets;
    }

    private static int CountShips(List<GalacticFleet> fleets, bool player)
    {
        int count = 0;
        foreach (GalacticFleet fleet in fleets)
        {
            if (fleet != null && Strength.IsPlayer(fleet.faction) == player)
            {
                count += fleet.roster.Count;
            }
        }
        return count;
    }

    private static string Colored(Faction faction)
    {
        if (faction == null) return "Unclaimed forces";
        return $"<color=#{ColorUtility.ToHtmlStringRGB(faction.color)}>{faction.factionName}</color>";
    }
}
