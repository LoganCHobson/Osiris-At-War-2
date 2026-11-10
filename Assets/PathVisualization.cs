using SolarStudios;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// The line showing where one of your ships is going. In a 3D battle it flies with the ship: it starts at
// the hull, follows the route around asteroids and parked ships, climbs or dives along the way exactly as
// the ship will (see ShipAltitude.AltitudeAfter), carries on through queued waypoints, and finishes with a
// drop-pin straight down to the battle plane so you can read how high the ship will end up.
// Only drawn for your own ships, and brighter for the ones you have selected.
public class PathVisualization : MonoBehaviour
{
    private const int StepsPerSegment = 8;     // Curve points per route leg, so climbs bend smoothly.
    private const float Width = 0.6f;
    private const float SelectedWidth = 1.1f;
    private static readonly Color Idle = new Color(0.35f, 1f, 0.45f, 0.45f);
    private static readonly Color Selected = new Color(0.45f, 1f, 0.55f, 0.9f);

    private PlayerUnitStateMachine stateMachine;
    private PlayerUnitMoveState moveState;
    private NavMeshAgent agent;
    private LineRenderer lineRenderer;
    private ShipAltitude altitude;
    private SpaceUnit unit;
    private UnitHealthManager health;

    private readonly List<Vector3> points = new List<Vector3>();
    private Vector3[] buffer = new Vector3[0];

    void Start()
    {
        stateMachine = GetComponentInChildren<PlayerUnitStateMachine>();
        moveState = GetComponentInChildren<PlayerUnitMoveState>();
        agent = GetComponent<NavMeshAgent>();
        lineRenderer = GetComponent<LineRenderer>();
        altitude = GetComponent<ShipAltitude>();
        unit = GetComponent<SpaceUnit>();
        health = GetComponent<UnitHealthManager>();

        lineRenderer.positionCount = 0;
        lineRenderer.useWorldSpace = true;
        lineRenderer.material = new Material(Shader.Find("Sprites/Default"));
        lineRenderer.numCapVertices = 2;
        lineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    void Update()
    {
        if (altitude == null) altitude = GetComponent<ShipAltitude>(); // Fitted when the ship joins the battle.

        if (!ShouldShow())
        {
            if (lineRenderer.positionCount != 0) lineRenderer.positionCount = 0;
            return;
        }

        BuildPoints();
        if (points.Count < 2)
        {
            lineRenderer.positionCount = 0;
            return;
        }

        bool selected = unit != null && unit.isSelected;
        lineRenderer.startWidth = lineRenderer.endWidth = selected ? SelectedWidth : Width;
        lineRenderer.startColor = selected ? Selected : Idle;
        lineRenderer.endColor = (selected ? Selected : Idle) * new Color(1f, 1f, 1f, 0.6f);

        if (buffer.Length < points.Count) buffer = new Vector3[points.Count * 2];
        points.CopyTo(buffer);
        lineRenderer.positionCount = points.Count;
        lineRenderer.SetPositions(buffer);
    }

    private bool ShouldShow()
    {
        if (agent == null || !agent.enabled || stateMachine == null || moveState == null) return false;
        if (health != null && health.IsDead) return false;
        if (GameManager.Instance != null && health != null && health.isAttackerSide != GameManager.Instance.PlayerIsAttacker) return false;
        return (Object)stateMachine.currentState == moveState && (agent.hasPath || agent.pathPending) && moveState.destinations.Count > 0;
    }

    private void BuildPoints()
    {
        points.Clear();
        float lift = altitude != null ? altitude.Lift : 0f;

        points.Add(transform.position);
        float travelled = 0f;
        Vector3 previousFlat = Flat(transform.position);

        // The leg being flown now, along the agent's route.
        Vector3[] corners = agent.hasPath ? agent.path.corners : new Vector3[0];
        for (int i = 1; i < corners.Length; i++)
        {
            AddLeg(corners[i - 1], corners[i], lift, ref travelled, ref previousFlat, i == 1);
        }

        // Queued waypoints after it, straight legs on the plane.
        Vector3 last = corners.Length > 0 ? corners[corners.Length - 1] : transform.position;
        for (int i = 1; i < moveState.destinations.Count; i++)
        {
            Vector3 next = moveState.destinations[i];
            AddLeg(last, next, lift, ref travelled, ref previousFlat, false);
            last = next;
        }

        // Drop-pin: straight down from the arrival point to the plane.
        Vector3 end = points[points.Count - 1];
        points.Add(new Vector3(end.x, last.y, end.z));
    }

    // Adds a route leg from `from` to `to` (both on the plane), lifted to the height the ship will be at.
    private void AddLeg(Vector3 from, Vector3 to, float lift, ref float travelled, ref Vector3 previousFlat, bool firstLeg)
    {
        // The first leg starts from where the ship actually is, not the corner it's leaving.
        Vector3 start = firstLeg ? new Vector3(transform.position.x, from.y, transform.position.z) : from;

        for (int step = 1; step <= StepsPerSegment; step++)
        {
            Vector3 ground = Vector3.Lerp(start, to, step / (float)StepsPerSegment);
            Vector3 flat = Flat(ground);
            travelled += Vector3.Distance(flat, previousFlat);
            previousFlat = flat;

            float height = altitude != null ? altitude.AltitudeAfter(travelled) : 0f;
            points.Add(ground + Vector3.up * (lift + height));
        }
    }

    private static Vector3 Flat(Vector3 v)
    {
        v.y = 0f;
        return v;
    }
}
