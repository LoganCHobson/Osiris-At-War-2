using System.Collections.Generic;
using UnityEngine;

public enum AsteroidDensity
{
    // Ships can fly through: slower on a normal move, full speed but damaged on a priority move.
    Field,
    // Nothing but fighters gets in, and fighters take damage while they're moving inside.
    Dense
}

// One asteroid area in a battle. Its shape is a chain of overlapping circles (so belts can curve and
// branch), filled with rocks. Big rocks get colliders so they stop lasers. Rocks are drawn instanced.
public class AsteroidField : MonoBehaviour
{
    [System.Serializable]
    public struct Blob
    {
        public Vector2 centre; // x/z on the battle plane.
        public float radius;
    }

    public AsteroidDensity density;
    public readonly List<Blob> blobs = new List<Blob>();
    public Bounds Bounds { get; private set; }

    private const int RockVariants = 5;
    private static Mesh[] rockMeshes;

    private class Rock
    {
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 spinAxis;
        public float spinSpeed;
        public Vector3 scale;
        public int variant;
    }

    private readonly List<Rock> rocks = new List<Rock>();
    private readonly List<Matrix4x4>[] batches = new List<Matrix4x4>[RockVariants];
    private Material material;

    public bool Contains(Vector3 point, float padding = 0f)
    {
        Vector2 flat = new Vector2(point.x, point.z);
        foreach (Blob blob in blobs)
        {
            float reach = blob.radius + padding;
            if ((flat - blob.centre).sqrMagnitude <= reach * reach) return true;
        }
        return false;
    }

    // Does the straight line from a to b pass through this field?
    public bool Crosses(Vector3 a, Vector3 b)
    {
        Vector2 from = new Vector2(a.x, a.z);
        Vector2 to = new Vector2(b.x, b.z);
        Vector2 segment = to - from;
        float lengthSq = Mathf.Max(segment.sqrMagnitude, 0.0001f);

        foreach (Blob blob in blobs)
        {
            float t = Mathf.Clamp01(Vector2.Dot(blob.centre - from, segment) / lengthSq);
            if ((from + segment * t - blob.centre).sqrMagnitude <= blob.radius * blob.radius) return true;
        }
        return false;
    }

    public void Build(int seed, Material rockMaterial, int rockLayer)
    {
        material = rockMaterial;
        BuildRockMeshes();
        System.Random random = new System.Random(seed);

        Bounds bounds = new Bounds(new Vector3(blobs[0].centre.x, 0f, blobs[0].centre.y), Vector3.zero);
        foreach (Blob blob in blobs)
        {
            bounds.Encapsulate(new Bounds(new Vector3(blob.centre.x, 0f, blob.centre.y), new Vector3(blob.radius * 2f, 80f, blob.radius * 2f)));
        }
        Bounds = bounds;

        bool dense = density == AsteroidDensity.Dense;
        float spacing = dense ? 13f : 22f;   // Rough distance between rocks.
        Vector2 sizeRange = dense ? new Vector2(5f, 22f) : new Vector2(2.5f, 13f);
        float colliderSize = dense ? 7f : 6f; // Rocks this big or bigger stop lasers.

        foreach (Blob blob in blobs)
        {
            int count = Mathf.CeilToInt(Mathf.PI * blob.radius * blob.radius / (spacing * spacing));
            for (int i = 0; i < count; i++)
            {
                // Uniform in the disc, but skip spots another blob already filled heavily.
                float angle = (float)random.NextDouble() * Mathf.PI * 2f;
                float distance = Mathf.Sqrt((float)random.NextDouble()) * blob.radius;
                Vector2 flat = blob.centre + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * distance;
                if (OverlapsEarlierBlob(blob, flat) && random.NextDouble() < 0.5) continue;

                float size = Mathf.Lerp(sizeRange.x, sizeRange.y, Mathf.Pow((float)random.NextDouble(), 2.2f));
                float height = Mathf.Lerp(-12f, 28f, (float)random.NextDouble());
                Vector3 stretch = new Vector3(Mathf.Lerp(0.75f, 1.3f, (float)random.NextDouble()), Mathf.Lerp(0.6f, 1f, (float)random.NextDouble()), Mathf.Lerp(0.8f, 1.4f, (float)random.NextDouble()));

                Rock rock = new Rock
                {
                    position = new Vector3(flat.x, height, flat.y),
                    rotation = Quaternion.Euler((float)random.NextDouble() * 360f, (float)random.NextDouble() * 360f, (float)random.NextDouble() * 360f),
                    spinAxis = new Vector3((float)random.NextDouble() - 0.5f, (float)random.NextDouble() - 0.5f, (float)random.NextDouble() - 0.5f).normalized,
                    spinSpeed = Mathf.Lerp(2f, 12f, (float)random.NextDouble()) / Mathf.Sqrt(size),
                    scale = stretch * size,
                    variant = random.Next(RockVariants)
                };
                rocks.Add(rock);

                if (size >= colliderSize)
                {
                    GameObject blocker = new GameObject("Rock");
                    blocker.layer = rockLayer;
                    blocker.transform.SetParent(transform, false);
                    blocker.transform.position = rock.position;
                    SphereCollider sphere = blocker.AddComponent<SphereCollider>();
                    sphere.radius = size * 0.42f;
                }
            }
        }

        for (int i = 0; i < RockVariants; i++) batches[i] = new List<Matrix4x4>();
    }

    private bool OverlapsEarlierBlob(Blob current, Vector2 point)
    {
        foreach (Blob blob in blobs)
        {
            if (blob.centre == current.centre) return false; // Only blobs before this one.
            if ((point - blob.centre).sqrMagnitude < blob.radius * blob.radius) return true;
        }
        return false;
    }

    private void Update()
    {
        if (material == null || rocks.Count == 0) return;

        foreach (List<Matrix4x4> batch in batches) batch.Clear();

        float time = Time.time;
        foreach (Rock rock in rocks)
        {
            Quaternion spin = Quaternion.AngleAxis(rock.spinSpeed * time, rock.spinAxis);
            batches[rock.variant].Add(Matrix4x4.TRS(rock.position, spin * rock.rotation, rock.scale));
        }

        RenderParams renderParams = new RenderParams(material)
        {
            worldBounds = Bounds,
            shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On,
            receiveShadows = true,
            layer = gameObject.layer
        };

        for (int variant = 0; variant < RockVariants; variant++)
        {
            List<Matrix4x4> batch = batches[variant];
            for (int start = 0; start < batch.Count; start += 1023)
            {
                Graphics.RenderMeshInstanced(renderParams, rockMeshes[variant], 0, batch, Mathf.Min(1023, batch.Count - start), start);
            }
        }
    }

    // ---- Procedural rock meshes: lumpy, squashed icospheres ----

    private static void BuildRockMeshes()
    {
        if (rockMeshes != null) return;

        rockMeshes = new Mesh[RockVariants];
        for (int i = 0; i < RockVariants; i++)
        {
            rockMeshes[i] = BuildRock(1000 + i * 77);
        }
    }

    private static Mesh BuildRock(int seed)
    {
        List<Vector3> vertices = new List<Vector3>();
        List<int> triangles = new List<int>();
        Icosphere(vertices, triangles, 2);

        System.Random random = new System.Random(seed);
        Vector3 offset = new Vector3((float)random.NextDouble() * 100f, (float)random.NextDouble() * 100f, (float)random.NextDouble() * 100f);
        for (int i = 0; i < vertices.Count; i++)
        {
            Vector3 v = vertices[i];
            float lumps = Noise(v * 1.6f + offset) * 0.35f + Noise(v * 4f + offset * 2f) * 0.12f;
            vertices[i] = v * (0.5f + lumps * 0.5f);
        }

        // Flat shading reads better as rock than smooth spheres - split every triangle's vertices.
        Vector3[] flatVertices = new Vector3[triangles.Count];
        int[] flatTriangles = new int[triangles.Count];
        for (int i = 0; i < triangles.Count; i++)
        {
            flatVertices[i] = vertices[triangles[i]];
            flatTriangles[i] = i;
        }

        Mesh mesh = new Mesh { name = "Asteroid" };
        mesh.vertices = flatVertices;
        mesh.triangles = flatTriangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static float Noise(Vector3 p)
    {
        float xy = Mathf.PerlinNoise(p.x, p.y);
        float yz = Mathf.PerlinNoise(p.y + 31.7f, p.z);
        float zx = Mathf.PerlinNoise(p.z + 71.3f, p.x);
        return (xy + yz + zx) / 3f * 2f - 1f;
    }

    private static void Icosphere(List<Vector3> vertices, List<int> triangles, int subdivisions)
    {
        float t = (1f + Mathf.Sqrt(5f)) / 2f;
        Vector3[] seed =
        {
            new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
            new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
            new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1)
        };
        foreach (Vector3 v in seed) vertices.Add(v.normalized);

        int[] faces =
        {
            0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
            3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1
        };
        triangles.AddRange(faces);

        Dictionary<long, int> midpoints = new Dictionary<long, int>();
        for (int level = 0; level < subdivisions; level++)
        {
            List<int> next = new List<int>();
            for (int i = 0; i < triangles.Count; i += 3)
            {
                int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
                int ab = Midpoint(a, b, vertices, midpoints);
                int bc = Midpoint(b, c, vertices, midpoints);
                int ca = Midpoint(c, a, vertices, midpoints);
                next.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
            }
            triangles.Clear();
            triangles.AddRange(next);
        }
    }

    private static int Midpoint(int a, int b, List<Vector3> vertices, Dictionary<long, int> cache)
    {
        long key = a < b ? ((long)a << 32) + b : ((long)b << 32) + a;
        if (cache.TryGetValue(key, out int index)) return index;

        vertices.Add(((vertices[a] + vertices[b]) * 0.5f).normalized);
        index = vertices.Count - 1;
        cache[key] = index;
        return index;
    }
}
