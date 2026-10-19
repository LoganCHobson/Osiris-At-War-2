using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

// Pits AI difficulty levels against each other in real battles to check that harder really is harder.
// Every matchup is played four times - each level gets each faction once, on each side once - with the
// same asteroid layout for both halves of a side-swap, so faction and map differences cancel out.
// Driven by DifficultySimRunner (Osiris > Run Difficulty Sim); writes a report and quits.
public class DifficultySim : MonoBehaviour
{
    public Faction factionA;
    public Faction factionB;
    public float simSpeed = 6f;
    public float battleTimeLimit = 420f;
    public string reportPath;

    private static readonly (DifficultyLevel stronger, DifficultyLevel weaker)[] Matchups =
    {
        (DifficultyLevel.Admiral, DifficultyLevel.Cadet),
        (DifficultyLevel.Admiral, DifficultyLevel.Captain),
        (DifficultyLevel.Captain, DifficultyLevel.Cadet)
    };

    private readonly StringBuilder report = new StringBuilder();
    private readonly List<AIBattleCommander> commanders = new List<AIBattleCommander>();

    private IEnumerator Start()
    {
        yield return null;
        GameSpeed.SetSpeed(simSpeed);
        report.AppendLine($"DIFFICULTY SIM  {factionA?.name} vs {factionB?.name}  speed x{simSpeed}  limit {battleTimeLimit}s");

        Vector3 attackerStart = GameManager.Instance.attackerShipSpawnPoint.position;
        Vector3 defenderStart = GameManager.Instance.defenderShipSpawnPoint.position;
        int battle = 0;

        foreach ((DifficultyLevel stronger, DifficultyLevel weaker) in Matchups)
        {
            int strongerWins = 0, weakerWins = 0, draws = 0;
            float marginTotal = 0f;

            for (int variant = 0; variant < 4; variant++)
            {
                bool strongerAttacks = variant % 2 == 0;
                bool strongerGetsA = variant < 2;
                int terrainSeed = 1000 + battle / 2 * 7;
                battle++;

                Clear();
                if (BattleTerrain.Instance != null) BattleTerrain.Instance.Regenerate(attackerStart, defenderStart, terrainSeed, true);
                yield return null;

                Faction strongFaction = strongerGetsA ? factionA : factionB;
                Faction weakFaction = strongerGetsA ? factionB : factionA;
                AIBattleCommander strong = Deploy(stronger, strongFaction, strongerAttacks);
                AIBattleCommander weak = Deploy(weaker, weakFaction, !strongerAttacks);

                yield return new WaitForSeconds(3f); // Let ships finish setting up their health first.
                float started = Time.time;
                float strongStart = Power(strongerAttacks, strong);
                float weakStart = Power(!strongerAttacks, weak);
                while (Time.time - started < battleTimeLimit)
                {
                    if (!SideAlive(strongerAttacks, strong) || !SideAlive(!strongerAttacks, weak)) break;
                    yield return new WaitForSeconds(1f);
                }

                float strongLeft = Power(strongerAttacks, strong) / Mathf.Max(1f, strongStart);
                float weakLeft = Power(!strongerAttacks, weak) / Mathf.Max(1f, weakStart);
                string outcome;
                if (strongLeft > weakLeft + 0.05f) { strongerWins++; outcome = stronger.ToString(); }
                else if (weakLeft > strongLeft + 0.05f) { weakerWins++; outcome = weaker.ToString(); }
                else { draws++; outcome = "draw"; }
                marginTotal += strongLeft - weakLeft;

                report.AppendLine($"  {stronger,-8}({strongFaction?.name}, {(strongerAttacks ? "attacker" : "defender")}) vs {weaker,-8}({weakFaction?.name})  "
                    + $"left {strongLeft:P0} vs {weakLeft:P0}  {Time.time - started:F0}s  terrain {terrainSeed}  winner={outcome}");
                Debug.Log($"DIFFSIM {report.ToString().Split('\n')[^2]}");
            }

            report.AppendLine($"== {stronger} vs {weaker}: {strongerWins} - {weakerWins} ({draws} draws), average margin {marginTotal / 4f:+0%;-0%}");
            report.AppendLine();
        }

        Clear();
        GameSpeed.Reset();
        string text = report.ToString();
        Debug.Log("DIFFSIM REPORT\n" + text);
        if (!string.IsNullOrEmpty(reportPath)) File.WriteAllText(reportPath, text);

#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#endif
    }

    private AIBattleCommander Deploy(DifficultyLevel level, Faction faction, bool attackerSide)
    {
        GameObject go = new GameObject($"Sim_{level}_{(attackerSide ? "Attacker" : "Defender")}");
        AIBattleCommander commander = go.AddComponent<AIBattleCommander>();
        commander.useDifficultyOverride = true;
        commander.difficultyOverride = DifficultyProfile.For(level);

        Transform spawn = attackerSide ? GameManager.Instance.attackerShipSpawnPoint : GameManager.Instance.defenderShipSpawnPoint;
        commander.Initialize(faction, attackerSide, Fleet(faction), spawn, GameManager.Instance.populationCap);
        commanders.Add(commander);
        return commander;
    }

    // The same recipe for every faction: a balanced battle group plus a reserve for reinforcements.
    private static List<Ship> Fleet(Faction faction)
    {
        List<Ship> fleet = new List<Ship>();
        if (faction == null) return fleet;

        foreach (Ship ship in faction.roster)
        {
            if (ship == null) continue;
            int count = ship.fleetRole switch
            {
                FleetRole.Line => 3,
                FleetRole.Screen => 4,
                FleetRole.Carrier => 1,
                _ => 2
            };
            for (int i = 0; i < count; i++) fleet.Add(ship);
        }
        return fleet;
    }

    private static bool SideAlive(bool attackerSide, AIBattleCommander commander)
    {
        if (commander != null && commander.ReserveCount > 0) return true;
        foreach (UnitHealthManager unit in UnitHealthManager.Active)
        {
            if (unit != null && !unit.IsDead && !unit.IsDefense && unit.isAttackerSide == attackerSide) return true;
        }
        return false;
    }

    // Combat power still standing on a side: live ships weighted by health, plus the unspent reserve.
    private static float Power(bool attackerSide, AIBattleCommander commander)
    {
        float power = 0f;
        foreach (UnitHealthManager unit in UnitHealthManager.Active)
        {
            if (unit == null || unit.IsDead || unit.IsDefense || unit.isAttackerSide != attackerSide || unit.sourceShip == null) continue;
            float health = unit.maxHealth > 0f ? Mathf.Clamp01(unit.currentHealth / unit.maxHealth) : 1f;
            power += unit.sourceShip.combatPower * health;
        }
        if (commander != null) power += commander.ReservePower;
        return power;
    }

    private void Clear()
    {
        foreach (AIBattleCommander commander in commanders)
        {
            if (commander != null) Destroy(commander.gameObject);
        }
        commanders.Clear();

        foreach (UnitHealthManager unit in new List<UnitHealthManager>(UnitHealthManager.Active))
        {
            if (unit != null && !unit.IsDefense) Destroy(unit.gameObject);
        }
        foreach (WreckChunk wreck in FindObjectsByType<WreckChunk>(FindObjectsSortMode.None))
        {
            if (wreck != null) Destroy(wreck.gameObject);
        }
    }
}
