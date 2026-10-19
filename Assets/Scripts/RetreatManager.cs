using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class RetreatManager : MonoBehaviour
{
    public static RetreatManager Instance { get; private set; }

    public float openingCooldown = 10f;
    public float retreatDuration = 10f;
    [Tooltip("Seconds before the jump that retreating ships start gathering FTL energy.")]
    public float spoolLead = 2.5f;
    [Tooltip("Retreating ships jump out one after another across this many seconds.")]
    public float jumpOutWindow = 1.2f;
    [Tooltip("Pause after the last ship jumps before the battle ends.")]
    public float holdAfterJump = 1f;

    public bool attackerRetreatBlocked;
    public bool defenderRetreatBlocked;

    public float BattleTime { get; private set; }
    public bool JumpingOut { get; private set; }
    public float CooldownRemaining => Mathf.Max(0f, openingCooldown - BattleTime);

    private bool attackerRetreating;
    private bool defenderRetreating;
    private float attackerRemaining;
    private float defenderRemaining;
    private bool attackerSpooled;
    private bool defenderSpooled;
    private bool finished;

    private static bool BattleActive => BattleContext.Instance != null && BattleContext.Instance.hasPendingBattle;

    private void Awake()
    {
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    public bool IsRetreating(bool attackerSide)
    {
        return attackerSide ? attackerRetreating : defenderRetreating;
    }

    public float RetreatRemaining(bool attackerSide)
    {
        return attackerSide ? attackerRemaining : defenderRemaining;
    }

    public bool IsBlocked(bool attackerSide)
    {
        return attackerSide ? attackerRetreatBlocked : defenderRetreatBlocked;
    }

    public bool CanRetreat(bool attackerSide)
    {
        return BattleActive && !finished && CooldownRemaining <= 0f && !IsRetreating(attackerSide) && !IsBlocked(attackerSide);
    }

    public static event System.Action<UnitHealthManager> ShipDeparted;

    public static bool ShipCanRetreat(UnitHealthManager unit)
    {
        return unit != null && !unit.IsDead && !unit.IsDefense && !EngineHardpoint.EnginesDisabled(unit.gameObject);
    }

    public static void Depart(UnitHealthManager unit)
    {
        if (unit == null) return;

        ShipDeparted?.Invoke(unit);
        FTLJump.Depart(unit.gameObject); // Jumps out, then deactivates the ship.
    }

    public bool BeginRetreat(bool attackerSide)
    {
        if (!CanRetreat(attackerSide)) return false;

        if (attackerSide)
        {
            attackerRetreating = true;
            attackerRemaining = retreatDuration;
        }
        else
        {
            defenderRetreating = true;
            defenderRemaining = retreatDuration;
        }

        FaceStartPoint(attackerSide);
        return true;
    }

    public void Finish()
    {
        finished = true;
    }

    private void Update()
    {
        if (finished || !BattleActive) return;

        BattleTime += Time.deltaTime;

        if (attackerRetreating)
        {
            attackerRemaining -= Time.deltaTime;
            if (!attackerSpooled && attackerRemaining <= spoolLead)
            {
                attackerSpooled = true;
                SpoolSide(true, attackerRemaining);
            }
            if (attackerRemaining <= 0f)
            {
                Complete(true);
                return;
            }
        }

        if (defenderRetreating)
        {
            defenderRemaining -= Time.deltaTime;
            if (!defenderSpooled && defenderRemaining <= spoolLead)
            {
                defenderSpooled = true;
                SpoolSide(false, defenderRemaining);
            }
            if (defenderRemaining <= 0f)
            {
                Complete(false);
            }
        }
    }

    private void Complete(bool attackerSide)
    {
        finished = true;
        StartCoroutine(JumpOut(attackerSide));
    }

    // The retreating fleet jumps out one ship after another, and only once they're all gone does the
    // battle end - so the jumps play out on screen before the victory/defeat banner.
    private IEnumerator JumpOut(bool attackerSide)
    {
        JumpingOut = true;

        List<UnitHealthManager> leaving = new List<UnitHealthManager>();
        foreach (UnitHealthManager unit in UnitHealthManager.Active)
        {
            if (unit != null && unit.isAttackerSide == attackerSide && ShipCanRetreat(unit))
            {
                leaving.Add(unit);
            }
        }

        // Random order, spread across the window so it reads as a ripple.
        for (int i = leaving.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (leaving[i], leaving[j]) = (leaving[j], leaving[i]);
        }

        float window = Mathf.Min(jumpOutWindow, 0.2f * leaving.Count);
        for (int i = 0; i < leaving.Count; i++)
        {
            float delay = leaving.Count > 1 ? window * i / (leaving.Count - 1) : 0f;
            ShipDeparted?.Invoke(leaving[i]);
            FTLJump.Depart(leaving[i].gameObject, delay);
        }

        float wait = window + FTLJump.JumpOutDuration + holdAfterJump;
        for (float t = 0f; t < wait; t += Time.unscaledDeltaTime) yield return null;

        JumpingOut = false;
        GameManager.Instance?.ConcludeBattle(attackerSide, true);
    }

    // Ships that can still jump start gathering energy for the departure.
    private static void SpoolSide(bool attackerSide, float duration)
    {
        foreach (UnitHealthManager unit in UnitHealthManager.Active)
        {
            if (unit != null && unit.isAttackerSide == attackerSide && ShipCanRetreat(unit))
            {
                FTLJump.BeginSpool(unit.gameObject, duration);
            }
        }
    }

    private void FaceStartPoint(bool attackerSide)
    {
        Transform start = GameManager.Instance != null ? GameManager.Instance.StartPointFor(attackerSide) : null;

        foreach (UnitHealthManager unit in new List<UnitHealthManager>(UnitHealthManager.Active))
        {
            if (unit == null || unit.IsDead || unit.IsDefense || unit.isAttackerSide != attackerSide) continue;

            SpaceUnit spaceUnit = unit.GetComponent<SpaceUnit>();
            if (spaceUnit == null || spaceUnit.moveState == null || spaceUnit.stateMachine == null) continue;

            ShipGuard.Cancel(spaceUnit);

            Vector3 facing = start != null ? start.position - unit.transform.position : Vector3.zero;
            facing.y = 0f;
            if (facing.sqrMagnitude < 1f && start != null)
            {
                facing = -start.forward;
            }

            spaceUnit.moveState.ClearDestinations();
            spaceUnit.moveState.SetFacing(facing);
            if ((Object)spaceUnit.stateMachine.currentState != spaceUnit.moveState)
            {
                spaceUnit.stateMachine.SetState(spaceUnit.moveState);
            }
        }
    }
}
