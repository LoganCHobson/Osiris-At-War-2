using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;

// Where a group of ships appears when it enters a battle: an EAW-style block with the screen (corvettes,
// destroyers) in front, the battle line behind it with the lead ship at its centre, and carriers and strike
// craft at the back. Ranks are spaced by the ships' own sizes and wrap into more ranks rather than running
// off the battlefield; the block is pulled inside the map, and every ship is snapped onto the navmesh at a
// spot no other ship is already using.
public static class BattleDeployment
{
    public struct Placement
    {
        public ShipEntry ship;
        public Vector3 position;
        public float altitude; // Height to start at (defense layouts can stack ships).
    }

    private const float Gap = 10f;
    private const float MapMargin = 30f;
    private const float FallbackWidth = 500f;

    private enum Rank { Screen, Line, Rear }

    // anchor: where the front-centre of the formation should be; forward: the way the ships face.
    public static List<Placement> Arrange(IList<ShipEntry> ships, ShipEntry lead, Vector3 anchor, Vector3 forward)
    {
        return Arrange(ships, lead, anchor, forward, new List<(Vector3, float)>());
    }

    // Lays ships out on a defense formation's slots (see DefenseFormation): specific-class slots are filled
    // first, then category slots, strongest ships first; whatever doesn't get a slot falls back to the
    // standard block. No formation = the standard block.
    public static List<Placement> ArrangeFormation(IList<ShipEntry> ships, DefenseFormation formation, ShipEntry lead, Vector3 anchor, Vector3 forward)
    {
        if (formation == null || formation.slots.Count == 0) return Arrange(ships, lead, anchor, forward);

        forward.y = 0f;
        forward = forward.sqrMagnitude > 0.01f ? forward.normalized : Vector3.forward;
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        Rect? map = BattleTerrain.Instance != null ? BattleTerrain.Instance.Map : (Rect?)null;

        List<ShipEntry> remaining = ships.Where(s => s != null).OrderByDescending(s => s == lead).ThenByDescending(s => s.combatPower).ToList();
        List<Placement> placements = new List<Placement>();
        List<(Vector3 position, float radius)> taken = new List<(Vector3, float)>();

        for (int pass = 0; pass < 2; pass++)
        {
            foreach (DefenseFormation.Slot slot in formation.slots)
            {
                bool specific = !string.IsNullOrEmpty(slot.ship);
                if (specific != (pass == 0)) continue;

                ShipEntry ship = remaining.FirstOrDefault(s => formation.Accepts(slot, s));
                if (ship == null) continue;
                remaining.Remove(ship);

                float radius = Radius(ship);
                Vector3 wanted = anchor + right * slot.x + forward * slot.z;
                if (map.HasValue)
                {
                    float margin = radius + MapMargin;
                    wanted.x = Mathf.Clamp(wanted.x, map.Value.xMin + margin, map.Value.xMax - margin);
                    wanted.z = Mathf.Clamp(wanted.z, map.Value.yMin + margin, map.Value.yMax - margin);
                }

                Vector3 position = FindClearSpot(wanted, radius, taken);
                taken.Add((position, radius));
                placements.Add(new Placement { ship = ship, position = position, altitude = slot.y });
            }
        }

        if (remaining.Count > 0)
        {
            placements.AddRange(Arrange(remaining, remaining.Contains(lead) ? lead : null, anchor, forward, taken));
        }
        return placements;
    }

    private static List<Placement> Arrange(IList<ShipEntry> ships, ShipEntry lead, Vector3 anchor, Vector3 forward, List<(Vector3 position, float radius)> taken)
    {
        List<Placement> placements = new List<Placement>();
        List<ShipEntry> fleet = ships.Where(s => s != null).ToList();
        if (fleet.Count == 0) return placements;

        forward.y = 0f;
        forward = forward.sqrMagnitude > 0.01f ? forward.normalized : Vector3.forward;
        Vector3 right = Vector3.Cross(Vector3.up, forward);

        Rect? map = BattleTerrain.Instance != null ? BattleTerrain.Instance.Map : (Rect?)null;
        // How wide the battlefield is across the formation's front.
        float mapWidth = map.HasValue ? Mathf.Abs(right.x) * map.Value.width + Mathf.Abs(right.z) * map.Value.height - MapMargin * 2f : FallbackWidth;
        float maxWidth = Mathf.Clamp(mapWidth, 120f, FallbackWidth);

        // Lay the ranks out in formation space: x across, z forward (negative = further back).
        List<(ShipEntry ship, Vector2 local)> layout = new List<(ShipEntry, Vector2)>();
        float depth = 0f;
        foreach (Rank rank in new[] { Rank.Screen, Rank.Line, Rank.Rear })
        {
            List<ShipEntry> members = fleet.Where(s => RankOf(s) == rank).OrderByDescending(s => s == lead).ThenByDescending(Radius).ToList();
            while (members.Count > 0)
            {
                List<ShipEntry> row = TakeRow(members, maxWidth);
                float rowRadius = row.Max(Radius);
                depth -= rowRadius;
                foreach ((ShipEntry ship, float x) in PlaceRow(row)) layout.Add((ship, new Vector2(x, depth)));
                depth -= rowRadius + Gap;
            }
        }

        // Keep the whole block inside the battlefield.
        Vector3 Point(Vector2 local) => anchor + right * local.x + forward * local.y;
        if (map.HasValue)
        {
            Vector3 shift = Vector3.zero;
            for (int pass = 0; pass < 2; pass++)
            {
                foreach ((ShipEntry ship, Vector2 local) in layout)
                {
                    Vector3 p = Point(local) + shift;
                    float r = Radius(ship) + MapMargin;
                    if (p.x - r < map.Value.xMin) shift.x += map.Value.xMin - (p.x - r);
                    if (p.x + r > map.Value.xMax) shift.x -= p.x + r - map.Value.xMax;
                    if (p.z - r < map.Value.yMin) shift.z += map.Value.yMin - (p.z - r);
                    if (p.z + r > map.Value.yMax) shift.z -= p.z + r - map.Value.yMax;
                }
            }
            anchor += shift;
        }

        foreach ((ShipEntry ship, Vector2 local) in layout)
        {
            float radius = Radius(ship);
            Vector3 position = FindClearSpot(Point(local), radius, taken);
            taken.Add((position, radius));
            placements.Add(new Placement { ship = ship, position = position });
        }
        return placements;
    }

    // The lead ship to give a fleet: the strongest ship that is allowed to lead.
    public static ShipEntry ChooseLead(IEnumerable<ShipEntry> ships)
    {
        return ships.Where(s => s != null && s.CanLead && s.fleetRole != FleetRole.Carrier).OrderByDescending(s => s.combatPower).FirstOrDefault()
            ?? ships.Where(s => s != null && s.CanLead).OrderByDescending(s => s.combatPower).FirstOrDefault()
            ?? ships.FirstOrDefault(s => s != null);
    }

    private static Rank RankOf(ShipEntry ship)
    {
        switch (ship.fleetRole)
        {
            case FleetRole.Screen: return Rank.Screen;
            case FleetRole.Line: return Rank.Line;
            default: return Rank.Rear; // Carriers and strike craft.
        }
    }

    // As many ships as fit across the battlefield, biggest first.
    private static List<ShipEntry> TakeRow(List<ShipEntry> members, float maxWidth)
    {
        List<ShipEntry> row = new List<ShipEntry>();
        float width = 0f;
        for (int i = 0; i < members.Count; i++)
        {
            float span = Radius(members[i]) * 2f + (row.Count > 0 ? Gap : 0f);
            if (row.Count > 0 && width + span > maxWidth) break;
            row.Add(members[i]);
            width += span;
        }
        members.RemoveRange(0, row.Count);
        return row;
    }

    // First ship in the centre, the rest alternating out to the sides, each by its own width.
    private static IEnumerable<(ShipEntry, float)> PlaceRow(List<ShipEntry> row)
    {
        float leftEdge = 0f, rightEdge = 0f;
        for (int i = 0; i < row.Count; i++)
        {
            float r = Radius(row[i]);
            if (i == 0)
            {
                leftEdge = -r;
                rightEdge = r;
                yield return (row[i], 0f);
            }
            else if (i % 2 == 1)
            {
                float x = rightEdge + Gap + r;
                rightEdge = x + r;
                yield return (row[i], x);
            }
            else
            {
                float x = leftEdge - Gap - r;
                leftEdge = x - r;
                yield return (row[i], x);
            }
        }
    }

    // The nearest point on the navmesh to the wanted spot that doesn't overlap a ship already placed,
    // searching outward in rings when the spot itself is taken or off the mesh.
    private static Vector3 FindClearSpot(Vector3 wanted, float radius, List<(Vector3 position, float radius)> taken)
    {
        for (int ring = 0; ring <= 8; ring++)
        {
            int steps = ring == 0 ? 1 : 8 + ring * 4;
            for (int k = 0; k < steps; k++)
            {
                float angle = k * Mathf.PI * 2f / steps;
                Vector3 candidate = wanted + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * ring * radius;
                if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, radius, NavMesh.AllAreas)) continue;
                if (Clear(hit.position, radius, taken)) return hit.position;
            }
        }
        return NavMesh.SamplePosition(wanted, out NavMeshHit last, 200f, NavMesh.AllAreas) ? last.position : wanted;
    }

    private static bool Clear(Vector3 point, float radius, List<(Vector3 position, float radius)> taken)
    {
        foreach ((Vector3 position, float r) in taken)
        {
            if ((point - position).sqrMagnitude < (radius + r + Gap * 0.5f) * (radius + r + Gap * 0.5f)) return false;
        }
        return true;
    }

    private static float Radius(ShipEntry ship)
    {
        NavMeshAgent agent = ship != null && ship.type != null ? ship.type.GetComponent<NavMeshAgent>() : null;
        float scale = ship != null && ship.type != null ? ship.type.transform.localScale.x : 1f;
        return agent != null ? agent.radius * scale : FleetFormation.DefaultRadius;
    }
}
