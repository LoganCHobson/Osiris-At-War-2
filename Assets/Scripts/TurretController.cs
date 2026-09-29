using SolarStudios;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Events;
using static SpaceUnit;

public class TurretController : MonoBehaviour
{
    public UnityEvent onFire;

    [Header("Gun parts")]
    public Transform turretBase;  // The base of the turret (azimuth rotation)
    public Transform turretBarrel;  // The barrel of the turret (elevation rotation)
    public List<Transform> firingPoints = new List<Transform>();

    [Header("Traverse settings")]
    public float rotationSpeed = 10f;  // Speed for azimuth rotation
    public float elevationSpeed = 10f;  // Speed for elevation rotation
    public float maxElevationAngle = 30f;  // Max elevation angle
    public float minElevationAngle = -5f;  // Min elevation angle

    [Header("Firing settings")]
    public float range = 50;
    public float baseAccuracy = 0.02f;
    public float rangeFallOffValue = 0.1f;
    public LayerMask targetLayer;
    public Transform target;
    public float fireRate = 1f;
    private float lastFiredTime;
    public GameObject projectilePrefab;
    public float projectileSpeed = 20f;
    public float damage;
    private float timeOutOfLOS = 0f;
    public float maxTimeWithoutLOS = 3f;

    [Header("Performance")]
    public float targetSearchInterval = 0.25f;
    public float lineOfSightInterval = 0.2f;

    private static readonly Collider[] overlapBuffer = new Collider[128];

    private float nextSearchAt;
    private float nextLineOfSightAt;
    private bool hasLineOfSight;
    private Transform lineOfSightTarget;
    private Transform typedTarget;
    private ShipType targetType;

    void Update()
    {
        if (target == null && Time.time >= nextSearchAt)
        {
            nextSearchAt = Time.time + targetSearchInterval * Random.Range(0.8f, 1.2f);
            AcquireTarget();
        }

        if (target != null)
        {


            Traverse();

            if (target != lineOfSightTarget || Time.time >= nextLineOfSightAt)
            {
                lineOfSightTarget = target;
                nextLineOfSightAt = Time.time + lineOfSightInterval * Random.Range(0.8f, 1.2f);
                hasLineOfSight = HasLineOfSight(firingPoints[0]);  //I would use one of the middle guns but I don't want to adjust this for single use guns. Wont matter much anyway.
            }

            if (hasLineOfSight)
            {
                timeOutOfLOS = 0f;
                Shoot();

            }
            else
            {
                timeOutOfLOS += Time.deltaTime;

                if (timeOutOfLOS >= maxTimeWithoutLOS)
                {
                    target = null;
                    timeOutOfLOS = 0f;
                }
            }
        }
    }


    void AcquireTarget()
    {
        int count = Physics.OverlapSphereNonAlloc(transform.position, range, overlapBuffer, targetLayer);

        float closestDistanceSqr = Mathf.Infinity;
        HardpointManager closestManager = null;

        for (int i = 0; i < count; i++)
        {
            Transform potentialTarget = overlapBuffer[i].transform;
            float distanceSqr = (potentialTarget.position - transform.position).sqrMagnitude;
            if (distanceSqr >= closestDistanceSqr) continue;

            if (potentialTarget.root.TryGetComponent(out HardpointManager manager))
            {
                closestDistanceSqr = distanceSqr;
                closestManager = manager;
            }
        }

        target = closestManager != null ? closestManager.GetRandomHardpoint() : null;
    }
    void Traverse()
    {
        Vector3 targetDirection = target.position - turretBase.position;


        Vector3 targetDirectionFlat = new Vector3(targetDirection.x, 0, targetDirection.z);
        if (targetDirectionFlat.sqrMagnitude > 0.01f)
        {
            Quaternion targetAzimuthRotation = Quaternion.LookRotation(targetDirectionFlat);
            turretBase.rotation = Quaternion.Slerp(turretBase.rotation, targetAzimuthRotation, rotationSpeed * Time.deltaTime);
        }


        float targetElevationAngle = Mathf.Atan2(targetDirection.y, targetDirectionFlat.magnitude) * Mathf.Rad2Deg;
        targetElevationAngle = -targetElevationAngle;
        targetElevationAngle = Mathf.Clamp(targetElevationAngle, minElevationAngle, maxElevationAngle);


        Quaternion targetElevationRotation = Quaternion.Euler(targetElevationAngle, turretBase.eulerAngles.y, 0);
        turretBarrel.rotation = Quaternion.Slerp(turretBarrel.rotation, targetElevationRotation, elevationSpeed * Time.deltaTime);
    }

    void Shoot()
    {
        if (Time.time - lastFiredTime >= fireRate)
        {
            onFire.Invoke();
            lastFiredTime = Time.time;

            foreach (Transform firingPoint in firingPoints)
            {
                FireProjectile(firingPoint);
            }
        }
    }

    bool HasLineOfSight(Transform firingPoint)
    {
        Vector3 direction = (target.position - firingPoint.position).normalized;
        RaycastHit hit;
        if (Physics.Raycast(firingPoint.position, direction, out hit, range))
        {
            return hit.transform == target || ((1 << hit.transform.gameObject.layer) & targetLayer) != 0;
        }
        return false;
    }
    void FireProjectile(Transform firingPoint)
    {
        if (target == null) return;

        Vector3 direction = (target.position - firingPoint.position).normalized;
        float accuracyMultiplier = GetAccuracyMultiplier(target);
        Vector3 inaccuracyOffset = Random.insideUnitSphere * accuracyMultiplier;
        Vector3 finalDirection = (direction + inaccuracyOffset).normalized;

        ObjectPool pool = ObjectPool.GetPoolFor(projectilePrefab);
        GameObject temp = pool != null
            ? pool.Spawn(firingPoint.position, firingPoint.parent.parent.rotation)
            : Instantiate(projectilePrefab, firingPoint.position, firingPoint.parent.parent.rotation);

        if (temp == null) return;

        Laser laser = temp.GetComponent<Laser>();
        laser.damage = damage;
        laser.SetSourcePool(pool);
        laser.Launch(temp.transform.forward * laser.speed + finalDirection * projectileSpeed);
    }

    float GetAccuracyMultiplier(Transform target)
    {

        float distance = Vector3.Distance(transform.position, target.position);

        float rangeFalloff = distance / range * rangeFallOffValue;

        if (target != typedTarget)
        {
            typedTarget = target;
            SpaceUnit unit = target.GetComponentInParent<SpaceUnit>();
            targetType = unit != null ? unit.shipType : ShipType.Cruiser;
        }

        switch (targetType)
        {
            case ShipType.Station:
            case ShipType.Battleship:
                return baseAccuracy * 0.5f; 
            case ShipType.Carrier:
            case ShipType.Cruiser:
                return baseAccuracy;
            case ShipType.Destroyer:
            case ShipType.Corvette:
                return baseAccuracy * 1.5f; 
            case ShipType.Fighter:
                return baseAccuracy * 3f + rangeFalloff;
            default:
                return baseAccuracy;
        }
    }

    public void AssignTurretTarget(Transform _target)
    {
        target = _target;
    }
}
