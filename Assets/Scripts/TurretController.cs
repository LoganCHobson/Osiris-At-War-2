using UnityEngine;

public class TurretController : MonoBehaviour
{
    public Transform turretBase;  // The base of the turret (azimuth rotation)
    public Transform turretBarrel;  // The barrel of the turret (elevation rotation)
    public Transform target;  // The target the turret aims at

    public float rotationSpeed = 10f;  // Speed for azimuth rotation
    public float elevationSpeed = 10f;  // Speed for elevation rotation
    public float maxElevationAngle = 30f;  // Max elevation angle
    public float minElevationAngle = -5f;  // Min elevation angle

    void Update()
    {
        if (target != null)
        {
            Traverse();


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
}
