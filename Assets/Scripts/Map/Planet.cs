using System.Collections.Generic;
using UnityEngine;

public class Planet : MonoBehaviour
{
    public const int FleetSlotCount = 3;

    public static readonly List<Planet> All = new List<Planet>();

    public string planetName;
    public Faction owner;
    public float fleetSlotRadius = 3f;

    public List<Planet> connections = new List<Planet>();

    [Header("Buildings")]
    public const int BaseIncome = 10;
    public const int TaxOfficeCost = 200;
    public const int TaxOfficeIncome = 100;
    public const int CapitalShipyardCost = 1000;
    public const int BattleStationCost = 2000;
    public const int BattleStationRefund = BattleStationCost / 10;

    public bool canBuildCapitalShipyard = true;
    public bool hasTaxOffice;
    public bool hasCapitalShipyard;
    public bool hasBattleStation;
    public List<ShipBuildOrder> shipBuildQueue = new List<ShipBuildOrder>();

    public bool IsShipyardSite => canBuildCapitalShipyard || hasCapitalShipyard;

    private readonly GalacticFleet[] fleetSlots = new GalacticFleet[FleetSlotCount];

    [Header("Ownership Visual")]
    public MeshRenderer ownershipRing;
    public Color unclaimedColor = new Color(0.5f, 0.5f, 0.5f, 0.6f);

    private static readonly int ColorID = Shader.PropertyToID("_Color");
    private MaterialPropertyBlock ringProperties;

    private void OnEnable()
    {
        All.Add(this);
    }

    private void OnDisable()
    {
        All.Remove(this);
    }

    private void Start()
    {
        UpdateOwnershipVisual();
    }

    public static int CountBattleStations(Faction faction)
    {
        int count = 0;
        foreach (Planet planet in All)
        {
            if (planet.hasBattleStation && planet.owner == faction) count++;
        }
        return count;
    }

    public static bool BattleStationLimitReached(Faction faction)
    {
        return faction == null || CountBattleStations(faction) >= faction.maxBattleStations;
    }

    public bool DemolishBattleStation()
    {
        if (!hasBattleStation) return false;

        hasBattleStation = false;
        GalacticState.Instance?.AddCurrency(owner, BattleStationRefund);
        return true;
    }

    private void OnValidate()
    {
        UpdateOwnershipVisual();
    }

    public void SetOwnership(Faction newOwner)
    {
        owner = newOwner;
        UpdateOwnershipVisual();
    }

    public void Capture(Faction newOwner)
    {
        Faction previousOwner = owner;
        if (previousOwner == newOwner) return;

        shipBuildQueue.Clear();
        SetOwnership(newOwner);
        GalacticEvents.RaisePlanetCaptured(this, previousOwner, newOwner);
    }

    public void UpdateOwnershipVisual()
    {
        if (ownershipRing == null) return;

        ringProperties ??= new MaterialPropertyBlock();
        ownershipRing.GetPropertyBlock(ringProperties);
        ringProperties.SetColor(ColorID, owner != null ? owner.color : unclaimedColor);
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

    public int FindFreeSlot()
    {
        for (int i = 0; i < fleetSlots.Length; i++)
        {
            if (fleetSlots[i] == null) return i;
        }
        return -1;
    }

    public void ForceClaimSlot(GalacticFleet fleet, int slot)
    {
        fleetSlots[slot] = fleet;
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = owner != null ? owner.color : Color.gray;
        Gizmos.DrawWireSphere(transform.position, 1f);

        if (IsShipyardSite)
        {
            Gizmos.color = new Color(1f, 0.6f, 0f, 0.9f);
            Gizmos.DrawWireCube(transform.position, Vector3.one * 2.4f);
        }

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
