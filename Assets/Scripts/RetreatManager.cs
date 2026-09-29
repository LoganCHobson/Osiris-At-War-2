using System.Collections.Generic;
using UnityEngine;

public class RetreatManager : MonoBehaviour
{
    public static RetreatManager Instance { get; private set; }

    public float openingCooldown = 10f;
    public float retreatDuration = 10f;

    public bool attackerRetreatBlocked;
    public bool defenderRetreatBlocked;

    public float BattleTime { get; private set; }
    public float CooldownRemaining => Mathf.Max(0f, openingCooldown - BattleTime);

    private bool attackerRetreating;
    private bool defenderRetreating;
    private float attackerRemaining;
    private float defenderRemaining;
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
        unit.gameObject.SetActive(false);
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
            if (attackerRemaining <= 0f)
            {
                Complete(true);
                return;
            }
        }

        if (defenderRetreating)
        {
            defenderRemaining -= Time.deltaTime;
            if (defenderRemaining <= 0f)
            {
                Complete(false);
            }
        }
    }

    private void Complete(bool attackerSide)
    {
        finished = true;
        GameManager.Instance?.ConcludeBattle(attackerSide, true);
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
