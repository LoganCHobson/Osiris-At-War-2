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

        nextLaunchAt = Time.time + launchInterval;

        while (launched.Count < complement.Count)
        {
            launched.Add(new List<UnitHealthManager>());
        }

        int bay = NeediestBay();
        if (bay < 0) return;

        Launch(bay);
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
        onLaunch.Invoke();
    }

    private static Vector3 Flat(Vector3 vector)
    {
        vector.y = 0f;
        return vector.sqrMagnitude > 0.001f ? vector : Vector3.forward;
    }
}
