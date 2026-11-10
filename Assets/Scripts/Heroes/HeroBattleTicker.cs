using UnityEngine;
using UnityEngine.SceneManagement;

// Battle-time upkeep for hero buffs that aren't read on demand: keeps every ship's speed in line with its
// side's Speed buff, and runs hero repair crews (HullRepair) once a second. Added to the battle scene.
public class HeroBattleTicker : MonoBehaviour
{
    private const float Interval = 1f;
    private float nextTickAt;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Hook()
    {
        SceneManager.sceneLoaded += (scene, _) => AttachTo(scene);
        AttachTo(SceneManager.GetActiveScene());
    }

    private static void AttachTo(Scene scene)
    {
        if (scene.name != "BattleScene" || FindFirstObjectByType<HeroBattleTicker>() != null) return;
        new GameObject("HeroBattleTicker").AddComponent<HeroBattleTicker>();
    }

    private void Update()
    {
        if (Time.time < nextTickAt) return;
        nextTickAt = Time.time + Interval;

        float speedAttack = 1f + HeroForces.Buff(true, HeroBuff.Speed);
        float speedDefend = 1f + HeroForces.Buff(false, HeroBuff.Speed);
        float repairAttack = HeroForces.Buff(true, HeroBuff.HullRepair);
        float repairDefend = HeroForces.Buff(false, HeroBuff.HullRepair);

        foreach (UnitHealthManager unit in UnitHealthManager.Active)
        {
            if (unit == null || unit.IsDead || unit.IsDefense) continue;

            if (unit.TryGetComponent(out UnityEngine.AI.NavMeshAgent agent))
            {
                ShipSpeed.For(agent)?.SetHeroFactor(unit.isAttackerSide ? speedAttack : speedDefend);
            }

            float repair = unit.isAttackerSide ? repairAttack : repairDefend;
            if (repair <= 0f || !unit.TryGetComponent(out HardpointManager hardpoints)) continue;

            // `repair` is a share of max health per minute; this tick is a second of it.
            foreach (HardpointHealth hardpoint in hardpoints.hardpoints)
            {
                if (hardpoint != null) unit.Repair(hardpoint, hardpoint.maxHealth * repair * Interval / 60f);
            }
        }
    }
}
