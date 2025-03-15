using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

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
    public LayerMask targetLayer;
    public Transform target;
    public float fireRate = 1f;
    private float lastFiredTime;
    public GameObject projectilePrefab;
    public float damage;
    private float timeOutOfLOS = 0f; 
    public float maxTimeWithoutLOS = 3f;

    void Update()
    {

        if(target == null)
        {
            AcquireTarget();
        }
        

        
        if (target != null)
        {
            Traverse();

            //Reminder to add a timer here for if a target is out of sight for too long to pick another.
            if (HasLineOfSight(firingPoints[0]))  //I would use one of the middle guns but I don't want to adjust this for single use guns. Wont matter much anyway.
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


    void AcquireTarget() //Auto Target aquisition
    {
        Collider[] targetsInRange = Physics.OverlapSphere(transform.position, range, targetLayer);

        if (targetsInRange.Length > 0)
        {

            float closestDistanceSqr = Mathf.Infinity;
            Transform closestTarget = null;

            foreach (Collider col in targetsInRange)
            {
                Transform potentialTarget = col.transform;
                float distanceSqr = (potentialTarget.position - transform.position).sqrMagnitude;

                if (distanceSqr < closestDistanceSqr)
                {
                    closestDistanceSqr = distanceSqr;
                    closestTarget = potentialTarget;
                }
            }

            HardpointManager hardpointManager = closestTarget.root.GetComponent<HardpointManager>();
            target = hardpointManager.GetRandomHardpoint();
        }
        else
        {
            target = null;
        }
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
        //Debug.DrawRay(firingPoint.position, direction, Color.red);
        if (Physics.Raycast(firingPoint.position, direction, out hit, range))
        {
            Debug.Log("LOS Made it.");
            return hit.transform == target || ((1 << hit.transform.gameObject.layer) & targetLayer) != 0; //IF we atleast hit something of the correct layer, fire anyway.
        }
        return false;
    }
    void FireProjectile(Transform firingPoint)
    {
        if (target == null) return;

        Vector3 direction = (target.position - firingPoint.position).normalized;
        GameObject temp = Instantiate(projectilePrefab, firingPoint.position, firingPoint.parent.parent.rotation);
        temp.GetComponent<Laser>().damage = damage;

        Rigidbody rb = temp.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.linearVelocity = direction * 20f;
        }
    }

    public void AssignTurretTarget(Transform _target)
    {
        target = _target;
    }
}
