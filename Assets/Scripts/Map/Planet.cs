using System.Collections.Generic;
using UnityEngine;

public class Planet : MonoBehaviour
{
    public const int FleetSlotCount = 3;

    public string planetName;
    public bool ownedByPlayer = true;
    public bool contested;
    public float fleetSlotRadius = 3f;

    public List<Planet> connections = new List<Planet>();

    private readonly GalacticFleet[] fleetSlots = new GalacticFleet[FleetSlotCount];

    [Header("Ownership Visual")]
    public MeshRenderer ownershipRing;
    public Color playerColor = new Color(0.25f, 0.55f, 1f, 0.85f);
    public Color enemyColor = new Color(1f, 0.2f, 0.2f, 0.85f);

    private static readonly int ColorID = Shader.PropertyToID("_Color");
    private MaterialPropertyBlock ringProperties;

    private void Start()
    {
        UpdateOwnershipVisual();
    }

    private void OnValidate()
    {
        UpdateOwnershipVisual();
    }

    public void SetOwnership(bool isOwnedByPlayer)
    {
        ownedByPlayer = isOwnedByPlayer;
        UpdateOwnershipVisual();
    }

    public void UpdateOwnershipVisual()
    {
        if (ownershipRing == null) return;

        ringProperties ??= new MaterialPropertyBlock();
        ownershipRing.GetPropertyBlock(ringProperties);
        ringProperties.SetColor(ColorID, ownedByPlayer ? playerColor : enemyColor);
        ownershipRing.SetPropertyBlock(ringProperties);
    }

    public bool IsConnectedTo(Planet other)
    {
        return other != null && connections.Contains(other);
    }

    public Vector3 GetSlotPosition(int slot)
    {
        float angle = (360f / FleetSlotCount) * slot;
        Vector3 offset = Quaternion.Euler(0f, angle, 0f) * Vector3.forward * fleetSlotRadius;
        return transform.position + offset;
    }

    public int ClaimSlot(GalacticFleet fleet)
    {
        for (int i = 0; i < fleetSlots.Length; i++)
        {
            if (fleetSlots[i] == fleet) return i;
        }

        for (int i = 0; i < fleetSlots.Length; i++)
        {
            if (fleetSlots[i] == null)
            {
                fleetSlots[i] = fleet;
                return i;
            }
        }

        return -1;
    }

    public void ReleaseSlot(GalacticFleet fleet)
    {
        for (int i = 0; i < fleetSlots.Length; i++)
        {
            if (fleetSlots[i] == fleet)
            {
                fleetSlots[i] = null;
            }
        }
    }

    public GalacticFleet GetFleetInSlot(int slot)
    {
        return fleetSlots[slot];
    }

    public void ForceClaimSlot(GalacticFleet fleet, int slot)
    {
        fleetSlots[slot] = fleet;
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = contested ? Color.red : (ownedByPlayer ? Color.cyan : Color.gray);
        Gizmos.DrawWireSphere(transform.position, 1f);

        Gizmos.color = new Color(0f, 1f, 1f, 0.4f);
        foreach (Planet connection in connections)
        {
            if (connection != null)
            {
                Gizmos.DrawLine(transform.position, connection.transform.position);
            }
        }

        Gizmos.color = new Color(1f, 1f, 0f, 0.6f);
        for (int i = 0; i < FleetSlotCount; i++)
        {
            Gizmos.DrawWireSphere(GetSlotPosition(i), 0.5f);
        }
    }
}
