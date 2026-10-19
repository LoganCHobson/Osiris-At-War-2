using System.Collections.Generic;
using UnityEngine;

// Lays out a battle's asteroids. Some maps are clear; others get belts that divide the map (always with
// gaps), chokes, partial lines, a sealed-off corner, or scattered clusters. Whatever is rolled, the
// attacker's start must be able to reach the defender's start - dense clusters that would cut the
// route are thinned into ordinary fields until a path exists.
public static class BattleTerrainLayout
{
    public class FieldSpec
    {
        public AsteroidDensity density;
        public readonly List<AsteroidField.Blob> blobs = new List<AsteroidField.Blob>();
    }

    [System.Serializable]
    public class Settings
    {
        [Range(0f, 1f)] public float clearMapChance = 0.3f;
        [Tooltip("Asteroid-free space around the attacker's arrival point.")]
        public float attackerClearRadius = 170f;
        [Tooltip("Asteroid-free space around the defender's start (covers its shipyard/station).")]
        public float defenderClearRadius = 240f;
        [Tooltip("Room a capital ship needs to squeeze between dense clusters.")]
        public float shipClearance = 45f;
        [Range(0.05f, 0.5f)] public float maxCoverage = 0.22f;
    }

    private enum Pattern { Divider, Choke, PartialLines, CornerPocket, Scatter }

    // Extra room kept between capital hulls and dense rock.
    public const float HullPadding = 25f;

    // Dense clusters are barred on the NavMesh as squares of this half-size around each blob - shared with
    // BattleTerrain so the route check sees exactly what ships will.
    public static float DenseHalfExtent(float blobRadius) => (blobRadius + HullPadding) * 0.875f;

    public static List<FieldSpec> Generate(int seed, Rect map, Vector2 attacker, Vector2 defender, Settings settings)
    {
        System.Random random = new System.Random(seed);
        List<FieldSpec> specs = new List<FieldSpec>();
        if (random.NextDouble() < settings.clearMapChance) return specs;

        int patternCount = random.NextDouble() < 0.35 ? 2 : 1;
        float coverage = 0f;
        float mapArea = map.width * map.height;

        for (int i = 0; i < patternCount; i++)
        {
            List<FieldSpec> added = new List<FieldSpec>();
            switch (PickPattern(random))
            {
                case Pattern.Divider: Divider(added, random, map, attacker, defender, 2, new Vector2(170f, 240f), 0.25f); break;
                case Pattern.Choke: Divider(added, random, map, attacker, defender, 1, new Vector2(130f, 180f), 0.55f); break;
                case Pattern.PartialLines: PartialLines(added, random, map); break;
                case Pattern.CornerPocket: CornerPocket(added, random, map, attacker, defender); break;
                case Pattern.Scatter: Scatter(added, random, map); break;
            }

            Clean(added, map, attacker, defender, settings);
            float addedArea = Area(added);
            if (i > 0 && coverage + addedArea > settings.maxCoverage * mapArea) continue; // Never smother the map.

            coverage += addedArea;
            specs.AddRange(added);
        }

        EnsureRoute(specs, map, attacker, defender, settings, random);
        return specs;
    }

    private static Pattern PickPattern(System.Random random)
    {
        double roll = random.NextDouble();
        if (roll < 0.28) return Pattern.Divider;
        if (roll < 0.48) return Pattern.Choke;
        if (roll < 0.72) return Pattern.PartialLines;
        if (roll < 0.84) return Pattern.CornerPocket;
        return Pattern.Scatter;
    }

    // A belt across the map between the two fleets, broken by gaps you can fly through at full speed.
    private static void Divider(List<FieldSpec> specs, System.Random random, Rect map, Vector2 attacker, Vector2 defender, int gaps, Vector2 gapWidth, float denseChance)
    {
        Vector2 axis = (defender - attacker).normalized;
        float tilt = Range(random, -25f, 25f) * Mathf.Deg2Rad;
        Vector2 across = Rotate(new Vector2(-axis.y, axis.x), tilt);
        Vector2 centre = Vector2.Lerp(attacker, defender, Range(random, 0.4f, 0.6f));
        float halfLength = new Vector2(map.width, map.height).magnitude * 0.5f;

        List<Vector2> path = new List<Vector2>();
        for (float s = -halfLength; s <= halfLength; s += 20f) path.Add(centre + across * s);

        // Gap positions along the belt, measured from the centre line.
        float[] gapCentres = new float[gaps];
        float[] gapHalfWidths = new float[gaps];
        for (int g = 0; g < gaps; g++)
        {
            gapCentres[g] = gaps == 1 ? Range(random, -150f, 150f) : (g == 0 ? Range(random, -320f, -80f) : Range(random, 80f, 320f));
            gapHalfWidths[g] = Range(random, gapWidth.x, gapWidth.y) * 0.5f;
        }

        float width = Range(random, 70f, 115f);
        List<Vector2> run = new List<Vector2>();
        for (int i = 0; i < path.Count; i++)
        {
            float along = -halfLength + i * 20f;
            bool inGap = false;
            for (int g = 0; g < gaps; g++)
            {
                if (Mathf.Abs(along - gapCentres[g]) < gapHalfWidths[g] + width * 0.5f) inGap = true;
            }

            if (inGap)
            {
                if (run.Count > 0) Belt(specs, random, new List<Vector2>(run), width, denseChance);
                run.Clear();
            }
            else
            {
                run.Add(path[i]);
            }
        }
        if (run.Count > 0) Belt(specs, random, run, width, denseChance);
    }

    private static void PartialLines(List<FieldSpec> specs, System.Random random, Rect map)
    {
        int lines = random.Next(1, 4);
        for (int i = 0; i < lines; i++)
        {
            Vector2 centre = new Vector2(Range(random, map.xMin + 150f, map.xMax - 150f), Range(random, map.yMin + 150f, map.yMax - 150f));
            Vector2 direction = Rotate(Vector2.right, Range(random, 0f, Mathf.PI * 2f));
            float halfLength = Range(random, 120f, 240f);

            List<Vector2> path = new List<Vector2>();
            for (float s = -halfLength; s <= halfLength; s += 20f) path.Add(centre + direction * s);
            Belt(specs, random, path, Range(random, 55f, 95f), 0.35f);
        }
    }

    // An arc that walls off the corner furthest from both fleets.
    private static void CornerPocket(List<FieldSpec> specs, System.Random random, Rect map, Vector2 attacker, Vector2 defender)
    {
        Vector2[] corners = { new Vector2(map.xMin, map.yMin), new Vector2(map.xMax, map.yMin), new Vector2(map.xMin, map.yMax), new Vector2(map.xMax, map.yMax) };
        Vector2 corner = corners[0];
        float best = float.MinValue;
        foreach (Vector2 candidate in corners)
        {
            float score = Mathf.Min(Vector2.Distance(candidate, attacker), Vector2.Distance(candidate, defender));
            if (score > best)
            {
                best = score;
                corner = candidate;
            }
        }

        Vector2 toCentre = (map.center - corner).normalized;
        float radius = Range(random, 280f, 400f);
        float baseAngle = Mathf.Atan2(toCentre.y, toCentre.x);

        List<Vector2> path = new List<Vector2>();
        for (float a = -55f; a <= 55f; a += 4f)
        {
            float angle = baseAngle + a * Mathf.Deg2Rad;
            path.Add(corner + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius);
        }
        Belt(specs, random, path, Range(random, 70f, 110f), 0.7f);
    }

    private static void Scatter(List<FieldSpec> specs, System.Random random, Rect map)
    {
        int fields = random.Next(3, 7);
        for (int i = 0; i < fields; i++)
        {
            FieldSpec field = new FieldSpec { density = AsteroidDensity.Field };
            Vector2 centre = new Vector2(Range(random, map.xMin + 100f, map.xMax - 100f), Range(random, map.yMin + 100f, map.yMax - 100f));
            int blobs = random.Next(2, 5);
            for (int b = 0; b < blobs; b++)
            {
                field.blobs.Add(new AsteroidField.Blob { centre = centre + RandomInDisc(random) * 70f, radius = Range(random, 45f, 95f) });
            }
            specs.Add(field);
        }

        int clumps = random.Next(1, 4);
        for (int i = 0; i < clumps; i++)
        {
            FieldSpec clump = new FieldSpec { density = AsteroidDensity.Dense };
            Vector2 centre = new Vector2(Range(random, map.xMin + 100f, map.xMax - 100f), Range(random, map.yMin + 100f, map.yMax - 100f));
            int blobs = random.Next(1, 4);
            for (int b = 0; b < blobs; b++)
            {
                clump.blobs.Add(new AsteroidField.Blob { centre = centre + RandomInDisc(random) * 45f, radius = Range(random, 35f, 70f) });
            }
            specs.Add(clump);
        }
    }

    // Turns a path into an ordinary field belt with wobble, and promotes some runs of it to dense cores.
    private static void Belt(List<FieldSpec> specs, System.Random random, List<Vector2> path, float width, float denseChance)
    {
        if (path.Count == 0) return;

        FieldSpec field = new FieldSpec { density = AsteroidDensity.Field };
        FieldSpec dense = null;
        float phase = Range(random, 0f, 100f);
        float step = width * 0.55f;
        float travelled = step;
        int denseRun = 0;

        for (int i = 0; i < path.Count; i++)
        {
            if (i > 0) travelled += Vector2.Distance(path[i], path[i - 1]);
            if (travelled < step) continue;
            travelled = 0f;

            Vector2 direction = i + 1 < path.Count ? (path[i + 1] - path[i]).normalized : (path[i] - path[Mathf.Max(0, i - 1)]).normalized;
            Vector2 normal = new Vector2(-direction.y, direction.x);
            Vector2 centre = path[i] + normal * Mathf.Sin(phase + i * 0.11f) * width * 0.45f;
            float radius = width * 0.5f * Range(random, 0.75f, 1.25f);
            field.blobs.Add(new AsteroidField.Blob { centre = centre, radius = radius });

            if (denseRun == 0 && random.NextDouble() < denseChance * 0.12) denseRun = random.Next(2, 5);
            if (denseRun > 0)
            {
                dense ??= new FieldSpec { density = AsteroidDensity.Dense };
                dense.blobs.Add(new AsteroidField.Blob { centre = centre, radius = radius * 0.65f });
                denseRun--;
                if (denseRun == 0)
                {
                    specs.Add(dense);
                    dense = null;
                }
            }
        }

        if (dense != null) specs.Add(dense);
        specs.Add(field);
    }

    // Keeps the start areas clear and the asteroids on the map.
    private static void Clean(List<FieldSpec> specs, Rect map, Vector2 attacker, Vector2 defender, Settings settings)
    {
        Rect inner = new Rect(map.xMin + 20f, map.yMin + 20f, map.width - 40f, map.height - 40f);
        foreach (FieldSpec spec in specs)
        {
            spec.blobs.RemoveAll(blob =>
                !inner.Contains(blob.centre)
                || Vector2.Distance(blob.centre, attacker) < settings.attackerClearRadius + blob.radius
                || Vector2.Distance(blob.centre, defender) < settings.defenderClearRadius + blob.radius);
        }
        specs.RemoveAll(spec => spec.blobs.Count == 0);
    }

    private static float Area(List<FieldSpec> specs)
    {
        float area = 0f;
        foreach (FieldSpec spec in specs)
        {
            if (spec.density != AsteroidDensity.Field) continue; // Dense cores sit inside fields.
            foreach (AsteroidField.Blob blob in spec.blobs) area += Mathf.PI * blob.radius * blob.radius * 0.6f; // Blobs overlap.
        }
        return area;
    }

    // Flood-fills a grid from the attacker's start; while the defender's start can't be reached, dense
    // clusters are thinned into ordinary fields (which ships can still cross).
    private static void EnsureRoute(List<FieldSpec> specs, Rect map, Vector2 attacker, Vector2 defender, Settings settings, System.Random random)
    {
        List<FieldSpec> denseSpecs = specs.FindAll(spec => spec.density == AsteroidDensity.Dense);
        while (denseSpecs.Count > 0 && !Reachable(denseSpecs, map, attacker, defender, settings.shipClearance))
        {
            FieldSpec thinned = denseSpecs[random.Next(denseSpecs.Count)];
            thinned.density = AsteroidDensity.Field;
            denseSpecs.Remove(thinned);
        }
    }

    public static bool Reachable(List<FieldSpec> denseSpecs, Rect map, Vector2 from, Vector2 to, float clearance)
    {
        const float cell = 20f;
        int width = Mathf.CeilToInt(map.width / cell);
        int height = Mathf.CeilToInt(map.height / cell);
        bool[,] blocked = new bool[width, height];

        foreach (FieldSpec spec in denseSpecs)
        {
            foreach (AsteroidField.Blob blob in spec.blobs)
            {
                float reach = DenseHalfExtent(blob.radius) + clearance; // Same square the NavMesh uses, plus a hull.
                int x0 = Mathf.Max(0, Mathf.FloorToInt((blob.centre.x - reach - map.xMin) / cell));
                int x1 = Mathf.Min(width - 1, Mathf.FloorToInt((blob.centre.x + reach - map.xMin) / cell));
                int y0 = Mathf.Max(0, Mathf.FloorToInt((blob.centre.y - reach - map.yMin) / cell));
                int y1 = Mathf.Min(height - 1, Mathf.FloorToInt((blob.centre.y + reach - map.yMin) / cell));
                for (int x = x0; x <= x1; x++)
                for (int y = y0; y <= y1; y++)
                {
                    blocked[x, y] = true;
                }
            }
        }

        Vector2Int start = Cell(from, map, cell, width, height);
        Vector2Int goal = Cell(to, map, cell, width, height);
        blocked[start.x, start.y] = false;
        blocked[goal.x, goal.y] = false;

        bool[,] seen = new bool[width, height];
        Queue<Vector2Int> open = new Queue<Vector2Int>();
        open.Enqueue(start);
        seen[start.x, start.y] = true;

        while (open.Count > 0)
        {
            Vector2Int current = open.Dequeue();
            if (current == goal) return true;

            for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            {
                int nx = current.x + dx, ny = current.y + dy;
                if (nx < 0 || ny < 0 || nx >= width || ny >= height || seen[nx, ny] || blocked[nx, ny]) continue;
                seen[nx, ny] = true;
                open.Enqueue(new Vector2Int(nx, ny));
            }
        }
        return false;
    }

    private static Vector2Int Cell(Vector2 point, Rect map, float cell, int width, int height)
    {
        return new Vector2Int(Mathf.Clamp((int)((point.x - map.xMin) / cell), 0, width - 1), Mathf.Clamp((int)((point.y - map.yMin) / cell), 0, height - 1));
    }

    private static float Range(System.Random random, float min, float max) => min + (float)random.NextDouble() * (max - min);

    private static Vector2 RandomInDisc(System.Random random)
    {
        float angle = Range(random, 0f, Mathf.PI * 2f);
        return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * Mathf.Sqrt((float)random.NextDouble());
    }

    private static Vector2 Rotate(Vector2 v, float radians)
    {
        float s = Mathf.Sin(radians), c = Mathf.Cos(radians);
        return new Vector2(c * v.x - s * v.y, s * v.x + c * v.y);
    }
}
