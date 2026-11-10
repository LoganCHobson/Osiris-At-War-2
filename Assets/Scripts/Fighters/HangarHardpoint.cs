using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Events;

public class HangarHardpoint : MonoBehaviour
{
    [System.Serializable]
    public class Bay
    {
        public Ship squadron;
        [Min(0)] public int count = 1;
    }

    public List<Bay> complement = new List<Bay>();
    public Transform launchPoint;
    public float firstLaunchDelay = 4f;
    public float launchInterval = 30f;

    public UnityEvent onLaunch;
    public UnityEvent onHangarDestroyed;

    public bool IsDestroyed { get; private set; }

    private readonly List<List<UnitHealthManager>> launched = new List<List<UnitHealthManager>>();
    private UnitHealthManager owner;
    private float nextLaunchAt;

    private void Awake()
    {
        owner = GetComponentInParent<UnitHealthManager>();
        HardpointHealth health = GetComponent<HardpointHealth>();
        if (health != null) health.die.AddListener(OnHangarDestroyed);
    }

    private void Start()
    {
        nextLaunchAt = Time.time + firstLaunchDelay;
    }

    private void OnHangarDestroyed()
    {
        if (IsDestroyed) return;
        IsDestroyed = true;
        onHangarDestroyed.Invoke();
    }

    public int ActiveCount(int bay)
    {
        if (bay < 0 || bay >= launched.Count) return 0;
        launched[bay].RemoveAll(unit => unit == null || unit.IsDead || !unit.gameObject.activeInHierarchy);
        return launched[bay].Count;
    }

    private void Update()
    {
        if (IsDestroyed || complement.Count == 0 || GameManager.Instance == null || GameManager.CombatOver) return;
        if (owner != null && owner.IsDead) return;
        if (Time.time < nextLaunchAt) return;

        nextLaunchAt = Time.time + launchInterval / (1f + HeroForces.BuffFor(this, HeroBuff.FighterLaunch));

        while (launched.Count < complement.Count)
        {
            launched.Add(new List<UnitHealthManager>());
        }

        int bay = NeediestBay();
        if (bay < 0) return;

        Launch(bay);
    }

    // Scramble Squadrons: launch every missing squadron right now.
    public void ScrambleAll()
    {
        if (IsDestroyed || complement.Count == 0 || GameManager.Instance == null || GameManager.CombatOver) return;
        while (launched.Count < complement.Count) launched.Add(new List<UnitHealthManager>());

        for (int bay = 0; bay < complement.Count; bay++)
        {
            if (complement[bay] == null || complement[bay].squadron == null) continue;
            for (int missing = complement[bay].count - ActiveCount(bay); missing > 0; missing--) Launch(bay);
        }
        nextLaunchAt = Time.time + launchInterval;
    }

    private int NeediestBay()
    {
        int best = -1;
        int bestShortfall = 0;

        for (int i = 0; i < complement.Count; i++)
        {
            if (complement[i] == null || complement[i].squadron == null) continue;

            int shortfall = complement[i].count - ActiveCount(i);
            if (shortfall > bestShortfall)
            {
                bestShortfall = shortfall;
                best = i;
            }
        }

        return best;
    }

    private void Launch(int bay)
    {
        Transform from = launchPoint != null ? launchPoint : transform;
        Vector3 position = NavMesh.SamplePosition(from.position, out NavMeshHit hit, 200f, NavMesh.AllAreas) ? hit.position : from.position;
        bool attackerSide = owner != null && owner.isAttackerSide;

        GameObject spawned = GameManager.Instance.SpawnShip(complement[bay].squadron, position, Quaternion.LookRotation(Flat(from.forward)), attackerSide, false);
        if (spawned == null) return;

        UnitHealthManager unit = spawned.GetComponent<UnitHealthManager>();
        if (unit != null) launched[bay].Add(unit);
        SendAfterCarrierTarget(spawned);
        onLaunch.Invoke();
    }

    // A fresh squadron goes after whatever its carrier has been ordered to attack, instead of idling beside
    // the carrier at the back of the fleet until something wanders into range.
    private void SendAfterCarrierTarget(GameObject spawned)
    {
        HardpointManager carrier = owner != null ? owner.GetComponent<HardpointManager>() : null;
        Transform target = carrier != null ? carrier.OrderedTarget : null;
        if (target == null || !target.gameObject.activeInHierarchy) return;

        UnitHealthManager targetHealth = target.GetComponentInParent<UnitHealthManager>();
        if (targetHealth == null || targetHealth.IsDead) return;

        if (spawned.TryGetComponent(out HardpointManager hardpoints)) hardpoints.AssignTarget(target);

        SpaceUnit unit = spawned.GetComponent<SpaceUnit>();
        if (unit == null || unit.moveState == null || unit.stateMachine == null) return;

        Vector3 toCarrier = spawned.transform.position - targetHealth.transform.position;
        toCarrier.y = 0f;
        Vector3 destination = targetHealth.transform.position + (toCarrier.sqrMagnitude > 0.01f ? toCarrier.normalized : Vector3.forward) * 40f;
        if (NavMesh.SamplePosition(destination, out NavMeshHit hit, 100f, NavMesh.AllAreas)) destination = hit.position;

        unit.moveState.ClearDestinations();
        unit.moveState.AddDestination(destination);
        unit.stateMachine.SetState(unit.moveState);
    }

    private static Vector3 Flat(Vector3 vector)
    {
        vector.y = 0f;
        return vector.sqrMagnitude > 0.001f ? vector : Vector3.forward;
    }
}
