using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public static class FleetFormation
{
    public const int MaxPerRow = 5;
    public const float Gap = 8f;
    public const float DefaultRadius = 10f;
    public const float DefaultRange = 50f;
    public const float AttackRangeFraction = 0.85f;
    public const float InRangeFraction = 0.95f;
    public const float MaxAttackArc = 150f;

    public struct Slot
    {
        public bool move;
        public Vector3 position;
        public Vector3 facing;
    }

    public static float Spacing(IReadOnlyList<SpaceUnit> units)
    {
        float radius = 0f;
        foreach (SpaceUnit unit in units)
        {
            radius = Mathf.Max(radius, Radius(unit != null ? unit.GetComponent<NavMeshAgent>() : null));
        }
        return radius * 2f + Gap;
    }

    public static float Spacing(GameObject prefab)
    {
        return Radius(prefab != null ? prefab.GetComponent<NavMeshAgent>() : null) * 2f + Gap;
    }

    public static Slot[] Move(IReadOnlyList<SpaceUnit> units, IReadOnlyList<Vector3> origins, Vector3 destination)
    {
        int count = units.Count;
        Slot[] slots = new Slot[count];
        if (count == 0) return slots;

        Vector3 centroid = Average(origins);
        Vector3 forward = Flat(destination - centroid);
        if (forward.sqrMagnitude < 0.01f) forward = Flat(units[0].transform.forward);
        if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
        forward.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, forward);

        float spacing = Spacing(units);
        int perRow = Mathf.Min(MaxPerRow, count);

        List<int> order = Indices(count);
        order.Sort((a, b) => Vector3.Dot(origins[b] - centroid, forward).CompareTo(Vector3.Dot(origins[a] - centroid, forward)));

        for (int start = 0, row = 0; start < count; start += perRow, row++)
        {
            List<int> members = order.GetRange(start, Mathf.Min(perRow, count - start));
            members.Sort((a, b) => Vector3.Dot(origins[a] - centroid, right).CompareTo(Vector3.Dot(origins[b] - centroid, right)));

            for (int k = 0; k < members.Count; k++)
            {
                float lateral = (k - (members.Count - 1) * 0.5f) * spacing;
                Vector3 point = destination + right * lateral - forward * (row * spacing);
                slots[members[k]] = new Slot { move = true, position = Snap(point, spacing), facing = forward };
            }
        }

        return slots;
    }

    public static Slot[] Attack(IReadOnlyList<SpaceUnit> units, IReadOnlyList<Vector3> origins, Vector3 target)
    {
        int count = units.Count;
        Slot[] slots = new Slot[count];
        if (count == 0) return slots;

        Vector3 approach = Flat(Average(origins) - target);
        if (approach.sqrMagnitude < 0.01f) approach = -Flat(units[0].transform.forward);
        if (approach.sqrMagnitude < 0.01f) approach = Vector3.back;
        approach.Normalize();

        float spacing = Spacing(units);
        float averageRadius = 0f;
        foreach (SpaceUnit unit in units)
        {
            averageRadius += StandoffDistance(unit);
        }
        averageRadius /= count;

        float step = Mathf.Rad2Deg * spacing / Mathf.Max(1f, averageRadius);
        step = Mathf.Min(step, count > 1 ? MaxAttackArc / (count - 1) : step);

        List<int> order = Indices(count);
        order.Sort((a, b) => Vector3.SignedAngle(approach, Flat(origins[a] - target), Vector3.up)
            .CompareTo(Vector3.SignedAngle(approach, Flat(origins[b] - target), Vector3.up)));

        for (int k = 0; k < count; k++)
        {
            int index = order[k];
            SpaceUnit unit = units[index];
            float range = Range(unit);
            Vector3 toTarget = Flat(target - origins[index]);

            if (toTarget.magnitude <= range * InRangeFraction)
            {
                slots[index] = new Slot { move = false, position = origins[index], facing = toTarget.normalized };
                continue;
            }

            float angle = (k - (count - 1) * 0.5f) * step;
            Vector3 point = target + Quaternion.Euler(0f, angle, 0f) * approach * StandoffDistance(unit);
            point = Snap(point, spacing);
            slots[index] = new Slot { move = true, position = point, facing = Flat(target - point).normalized };
        }

        return slots;
    }

    private static float StandoffDistance(SpaceUnit unit)
    {
        return Range(unit) * AttackRangeFraction;
    }

    private static float Range(SpaceUnit unit)
    {
        return unit != null && unit.maxRange > 0f ? unit.maxRange : DefaultRange;
    }

    private static float Radius(NavMeshAgent agent)
    {
        return agent != null ? agent.radius : DefaultRadius;
    }

    private static Vector3 Snap(Vector3 point, float searchRadius)
    {
        return NavMesh.SamplePosition(point, out NavMeshHit hit, searchRadius * 2f, NavMesh.AllAreas) ? hit.position : point;
    }

    private static Vector3 Average(IReadOnlyList<Vector3> points)
    {
        Vector3 sum = Vector3.zero;
        foreach (Vector3 point in points)
        {
            sum += point;
        }
        return points.Count > 0 ? sum / points.Count : Vector3.zero;
    }

    private static Vector3 Flat(Vector3 vector)
    {
        vector.y = 0f;
        return vector;
    }

    private static List<int> Indices(int count)
    {
        List<int> indices = new List<int>(count);
        for (int i = 0; i < count; i++)
        {
            indices.Add(i);
        }
        return indices;
    }
}
