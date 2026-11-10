using System.Collections.Generic;
using UnityEngine;

// Battle damage that builds up where the shots land. Every hit on a ship's hull adds its damage to the patch of
// hull it struck (the hull is divided into cells). As a patch takes sustained fire it goes through stages:
//   carbon scoring  - a few dark streaks along the line of fire (a single shot never does more than this)
//   blackened       - a wide charred scorch with embers glowing in it for a while
//   scarred         - a deep burn with the plating cracked across it, smouldering
//   breached        - the hull split open: a black wound, torn plates peeled back, fire and sparks pouring out
// The marks are cut from the hull mesh itself (so they wrap the hull's real shape), drawn slightly proud of it.
// Fitted to every ship by GameManager.ApplyAllegiance.
[DisallowMultipleComponent]
public class HullScarring : MonoBehaviour
{
    // Stage thresholds: damage taken by one patch, as a share of the ship's total health.
    private const float ScoreEvery = 0.004f;
    private const float BlackenedAt = 0.012f;
    private const float ScarredAt = 0.028f;
    private const float BreachedAt = 0.05f;
    private const int MaxScoresPerCell = 4;
    private const int MaxMarksPerShip = 110;

    private class Cell
    {
        public float damage;
        public int scores;
        public int stage;
        public Vector3 lastLocalPoint;
        public Vector3 lastLocalDirection;
        public Ember glow; // The patch's heat: kept hot while it's under fire, cools once the fire stops.
    }

    private HullDamage hull;
    private UnitHealthManager health;
    private readonly Dictionary<Vector3Int, Cell> cells = new Dictionary<Vector3Int, Cell>();
    private readonly List<Collider> hullColliders = new List<Collider>();
    private readonly List<Ember> embers = new List<Ember>();
    private Transform marksRoot;
    private Ember lastBreachGlow;
    private int marks;
    public int MarkCount => marks;
    private float cellSize;

    private class Ember
    {
        public MeshRenderer renderer;
        public float bornAt, life;
        public Color color;
    }

    private static Material scoreMaterial, scorchMaterial, scarMaterial, emberMaterial, crackGlowMaterial, woundMaterial;
    private static readonly Color EmberColor = new Color(1f, 0.38f, 0.08f, 1f);
    private static readonly Dictionary<Mesh, MeshSlicer.MeshData> meshData = new Dictionary<Mesh, MeshSlicer.MeshData>();
    private static MaterialPropertyBlock block;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => meshData.Clear();

    public static void Fit(GameObject ship)
    {
        if (ship.GetComponent<HullDamage>() != null && ship.GetComponent<HullScarring>() == null) ship.AddComponent<HullScarring>();
    }

    // A shot or warhead landed at this point. Anything that isn't part of a scarrable ship is ignored.
    public static void Impact(Collider struck, Vector3 point, Vector3 direction, float damage)
    {
        if (struck == null || damage <= 0f) return;
        HullScarring scarring = struck.GetComponentInParent<HullScarring>();
        if (scarring != null) scarring.Register(point, direction, damage, struck);
    }

    private void Start()
    {
        hull = GetComponent<HullDamage>();
        health = GetComponent<UnitHealthManager>();
        marksRoot = new GameObject("HullMarks").transform;
        marksRoot.SetParent(hull.gfxRoot != null ? hull.gfxRoot : transform, false);
    }

    // HullDamage measures the hull in its own Start, which may run after ours: gather what we need on first use.
    private bool Ready()
    {
        if (hullColliders.Count > 0) return true;
        if (hull == null || hull.Surfaces.Count == 0) return false;
        foreach (MeshFilter surface in hull.Surfaces)
        {
            if (surface.TryGetComponent(out Collider collider)) hullColliders.Add(collider);
        }
        cellSize = Mathf.Max(1f, hull.Size * 0.1f);
        return hullColliders.Count > 0;
    }

    private void Register(Vector3 point, Vector3 direction, float damage, Collider struck)
    {
        if (hull == null || health == null || health.IsDead || health.maxHealth <= 0f || !Ready()) return;

        // Where the shot met the hull plating (a hit on a turret or other hardpoint marks the deck under it).
        Vector3 dirN = direction.sqrMagnitude > 0.0001f ? direction.normalized : (transform.position - point).normalized;
        if (!HullPoint(point, dirN, out Vector3 surfacePoint, out Vector3 normal, out MeshFilter surface)) return;

        Vector3 local = transform.InverseTransformPoint(surfacePoint);
        Vector3Int key = new Vector3Int(Mathf.FloorToInt(local.x / cellSize), Mathf.FloorToInt(local.y / cellSize), Mathf.FloorToInt(local.z / cellSize));
        if (!cells.TryGetValue(key, out Cell cell)) cells[key] = cell = new Cell();

        float before = cell.damage;
        cell.damage += damage / health.maxHealth;
        cell.lastLocalPoint = local;
        cell.lastLocalDirection = transform.InverseTransformDirection(direction);
        if (marks >= MaxMarksPerShip) return;

        // Carbon scoring: one streak each time the patch soaks up another slice of damage.
        if (Mathf.FloorToInt(cell.damage / ScoreEvery) > Mathf.FloorToInt(before / ScoreEvery) && cell.scores < MaxScoresPerCell && cell.stage < 2)
        {
            cell.scores++;
            Mark(scoreMaterial ??= MakeMaterial(ScoreTexture(), new Color(0.02f, 0.017f, 0.014f, 1f)), surface, surfacePoint, normal, direction, hull.Size * Random.Range(0.028f, 0.04f), 2.4f, 0f);
        }
        int stage = cell.damage >= BreachedAt ? 3 : cell.damage >= ScarredAt ? 2 : cell.damage >= BlackenedAt ? 1 : 0;
        int stageBefore = cell.stage;
        Advance(cell, stage, surface, surfacePoint, normal, direction);
        if (cell.stage >= 1 && cell.stage == stageBefore) Reheat(cell, surface, surfacePoint, normal, direction);
    }

    // Bring a patch up to `stage`, showing each stage it passes through.
    private void Advance(Cell cell, int stage, MeshFilter surface, Vector3 surfacePoint, Vector3 normal, Vector3 direction)
    {
        if (cell.stage < 1 && stage >= 1)
        {
            cell.stage = 1;
            Mark(scorchMaterial ??= MakeMaterial(ScorchTexture(), new Color(0.02f, 0.018f, 0.015f, 1f)), surface, surfacePoint, normal, direction, hull.Size * 0.07f, 1.5f, 0.001f);
            cell.glow = Glow(emberMaterial ??= GlowMaterial(EmberTexture()), surface, surfacePoint, normal, direction, hull.Size * 0.05f, 12f, 3f);
        }
        if (cell.stage < 2 && stage >= 2)
        {
            cell.stage = 2;
            Mark(scarMaterial ??= MakeMaterial(ScarTexture(), new Color(0.015f, 0.012f, 0.01f, 1f)), surface, surfacePoint, normal, direction, hull.Size * 0.09f, 1.2f, 0.002f);
            cell.glow = Glow(crackGlowMaterial ??= GlowMaterial(CrackGlowTexture()), surface, surfacePoint, normal, direction, hull.Size * 0.09f, 30f, 3.5f);
            if (hull.profile != null && hull.profile.breachVent != null)
                DestructionFx.Attach(hull.profile.breachVent, marksRoot, surfacePoint + normal * hull.Size * 0.003f, hull.SizeScale * 0.35f, normal);
        }
        if (cell.stage < 3 && stage >= 3)
        {
            cell.stage = 3;
            Breach(surface, surfacePoint, normal, direction);
            if (lastBreachGlow != null) cell.glow = lastBreachGlow;
        }
    }

    // Where a hit at `point` travelling along `direction` lands on the hull plating.
    private bool HullPoint(Vector3 point, Vector3 direction, out Vector3 surfacePoint, out Vector3 normal, out MeshFilter surface, float back = -1f)
    {
        surfacePoint = point;
        normal = Vector3.up;
        surface = null;
        Vector3 dir = direction.sqrMagnitude > 0.0001f ? direction.normalized : (transform.position - point).normalized;
        float reach = back > 0f ? back * 2f : hull.Size * 0.25f;
        Ray ray = new Ray(point - dir * (back > 0f ? back : reach * 0.5f), dir);
        float best = float.MaxValue;
        foreach (Collider collider in hullColliders)
        {
            if (collider == null || !collider.enabled) continue;
            if (collider.Raycast(ray, out RaycastHit hit, reach) && hit.distance < best)
            {
                best = hit.distance;
                surfacePoint = hit.point;
                normal = hit.normal;
                surface = collider.GetComponent<MeshFilter>();
            }
        }
        if (surface != null) return true;

        // No collider in the way (e.g. a glancing hit): fall back to the nearest point on the hull's colliders.
        foreach (Collider collider in hullColliders)
        {
            if (collider == null || !collider.enabled || !(collider is MeshCollider mc) || !mc.convex) continue;
            Vector3 closest = collider.ClosestPoint(point);
            float distance = (closest - point).sqrMagnitude;
            if (distance < best && distance < reach * reach)
            {
                best = distance;
                surfacePoint = closest;
                normal = (point - closest).normalized;
                surface = collider.GetComponent<MeshFilter>();
            }
        }
        return surface != null;
    }

    // ---------------- Marks ----------------

    // A patch cut from the hull mesh around the point, lifted a hair off the surface, with the mark texture
    // projected onto it and stretched along the line of fire (`stretch`).
    private MeshRenderer Mark(Material material, MeshFilter surface, Vector3 point, Vector3 normal, Vector3 direction, float radius, float stretch, float lift)
    {
        Mesh mesh = Patch(surface, point, normal, direction, radius, stretch, lift + 0.0006f);
        if (mesh == null) return null;
        marks++;
        GameObject go = new GameObject("Mark");
        go.layer = surface.gameObject.layer;
        go.transform.SetParent(marksRoot, false);
        go.transform.SetPositionAndRotation(surface.transform.position, surface.transform.rotation);
        go.transform.localScale = Divide(surface.transform.lossyScale, marksRoot.lossyScale);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return renderer;
    }

    // Hot metal glowing through the damage (additive HDR, so bloom picks it up), cooling over `life` seconds.
    private Ember Glow(Material material, MeshFilter surface, Vector3 point, Vector3 normal, Vector3 direction, float radius, float life, float heat)
    {
        MeshRenderer glow = Mark(material, surface, point, normal, direction, radius, 1.3f, 0.003f);
        if (glow == null) return null;
        Ember ember = new Ember { renderer = glow, bornAt = Time.time, life = life, color = EmberColor * heat };
        embers.Add(ember);
        return ember;
    }

    // Another hit on a damaged patch: its glow flares back up (or re-lights if it had cooled right off).
    private void Reheat(Cell cell, MeshFilter surface, Vector3 point, Vector3 normal, Vector3 direction)
    {
        if (cell.glow != null && cell.glow.renderer != null)
        {
            cell.glow.bornAt = Mathf.Max(cell.glow.bornAt, Time.time - cell.glow.life * 0.1f);
            return;
        }
        if (marks >= MaxMarksPerShip) return;
        cell.glow = cell.stage >= 2
            ? Glow(crackGlowMaterial ??= GlowMaterial(CrackGlowTexture()), surface, point, normal, direction, hull.Size * 0.09f, 30f, 3.5f)
            : Glow(emberMaterial ??= GlowMaterial(EmberTexture()), surface, point, normal, direction, hull.Size * 0.05f, 12f, 3f);
    }

    // The hull split open: a black wound, jagged plates torn up and peeled back around it, burning.
    private void Breach(MeshFilter surface, Vector3 point, Vector3 normal, Vector3 direction)
    {
        float radius = hull.Size * 0.055f;
        Mark(woundMaterial ??= MakeMaterial(WoundTexture(), new Color(0f, 0f, 0f, 1f)), surface, point, normal, direction, radius, 1.4f, 0.004f);
        Ember breachGlow = Glow(emberMaterial ??= GlowMaterial(EmberTexture()), surface, point, normal, direction, radius * 1.3f, 30f, 3f);
        lastBreachGlow = breachGlow;

        MeshSlicer.MeshData data = Data(surface.sharedMesh);
        if (data != null)
        {
            Vector3 tangent = Vector3.Cross(normal, Mathf.Abs(Vector3.Dot(normal, Vector3.up)) > 0.9f ? Vector3.right : Vector3.up).normalized;
            int petals = Random.Range(3, 6);
            for (int i = 0; i < petals; i++)
            {
                // A plate from the rim of the wound, hinged on its outer edge and bent up out of the hull.
                float angle = (i + Random.Range(-0.3f, 0.3f)) * 360f / petals;
                Vector3 outwardDir = Quaternion.AngleAxis(angle, normal) * tangent;
                Vector3 plateCentre = point + outwardDir * radius * 0.75f;
                Vector3 localCentre = surface.transform.InverseTransformPoint(plateCentre);
                float localRadius = radius * 0.45f / Mathf.Max(0.0001f, surface.transform.lossyScale.x);
                Mesh plate = MeshSlicer.ExtractPatch(data, localCentre, localRadius, "TornPlate", out _);
                if (plate == null) continue;

                GameObject go = new GameObject("TornPlate");
                go.layer = surface.gameObject.layer;
                go.transform.SetParent(marksRoot, false);
                go.transform.SetPositionAndRotation(surface.transform.position, surface.transform.rotation);
                go.transform.localScale = Divide(surface.transform.lossyScale, marksRoot.lossyScale);
                go.AddComponent<MeshFilter>().sharedMesh = plate;
                MeshRenderer renderer = go.AddComponent<MeshRenderer>();
                MeshRenderer source = surface.GetComponent<MeshRenderer>();
                Material[] materials = source != null ? source.sharedMaterials : new Material[0];
                if (hull.profile != null && hull.profile.interiorMaterial != null && plate.subMeshCount > materials.Length)
                {
                    System.Array.Resize(ref materials, plate.subMeshCount);
                    materials[materials.Length - 1] = hull.profile.interiorMaterial;
                }
                renderer.sharedMaterials = materials;
                Tint(renderer, 0.3f);

                Vector3 hinge = point + outwardDir * radius * 1.15f;
                Vector3 axis = Vector3.Cross(normal, outwardDir).normalized;
                go.transform.RotateAround(hinge, axis, -Random.Range(25f, 65f));
                go.transform.position += normal * hull.Size * 0.004f;
                marks++;
            }
        }

        hull.SpawnDamageEffect(point, normal);
        if (hull.profile != null)
        {
            hull.Explode(hull.profile.smallExplosion, point, hull.profile.smallExplosionScale * 0.6f);
            if (hull.profile.breachVent != null) DestructionFx.Attach(hull.profile.breachVent, marksRoot, point + normal * hull.Size * 0.004f, hull.SizeScale * 0.5f, normal);
        }
    }

    // Where a turret was blown off its mount: a black pit with a charred ring, following the hull's surface.
    public void Crater(Vector3 point, Vector3 outward, float radius)
    {
        if (hull == null || !Ready()) return;
        if (!HullPoint(point + outward * hull.Size * 0.05f, -outward, out Vector3 surfacePoint, out Vector3 normal, out MeshFilter surface)) return;
        Mark(scorchMaterial ??= MakeMaterial(ScorchTexture(), new Color(0.02f, 0.018f, 0.015f, 1f)), surface, surfacePoint, normal, Vector3.Cross(normal, Vector3.up), radius * 1.5f, 1f, 0.001f);
        Mark(woundMaterial ??= MakeMaterial(WoundTexture(), new Color(0f, 0f, 0f, 1f)), surface, surfacePoint, normal, Vector3.Cross(normal, Vector3.up), radius, 1f, 0.003f);
    }

    // A molten crack along a seam that is about to give way (ShipDestruction's planned break lines).
    public void SeamGlow(Vector3 point, Vector3 normal, Vector3 along, float radius, float life)
    {
        if (hull == null || !Ready()) return;
        if (!HullPoint(point + normal * hull.Size * 0.02f, -normal, out Vector3 surfacePoint, out Vector3 surfaceNormal, out MeshFilter surface)) return;
        Mark(scarMaterial ??= MakeMaterial(ScarTexture(), new Color(0.015f, 0.012f, 0.01f, 1f)), surface, surfacePoint, surfaceNormal, along, radius * 0.8f, 3f, 0.002f);
        MeshRenderer glow = Mark(crackGlowMaterial ??= GlowMaterial(CrackGlowTexture()), surface, surfacePoint, surfaceNormal, along, radius, 3.2f, 0.003f);
        if (glow != null) embers.Add(new Ember { renderer = glow, bornAt = Time.time, life = life, color = EmberColor * 3f });
    }

    private void Update()
    {
        if (embers.Count == 0) return;
        block ??= new MaterialPropertyBlock();
        for (int i = embers.Count - 1; i >= 0; i--)
        {
            Ember ember = embers[i];
            if (ember.renderer == null) { embers.RemoveAt(i); continue; }
            float t = (Time.time - ember.bornAt) / ember.life;
            if (t >= 1f)
            {
                Destroy(ember.renderer.gameObject);
                embers.RemoveAt(i);
                continue;
            }
            // Cools from white-hot orange to a dull red as it fades.
            float flicker = 0.8f + 0.2f * Mathf.PerlinNoise(Time.time * 3f, i * 1.7f);
            float cool = (1f - t) * (1f - t) * flicker;
            Color c = Color.Lerp(new Color(0.6f, 0.08f, 0.02f), ember.color, 1f - t) * cool;
            c.a = 1f;
            ember.renderer.GetPropertyBlock(block);
            block.SetColor("_Color", c);
            ember.renderer.SetPropertyBlock(block);
        }
    }

    // ---------------- Geometry ----------------

    // The hull's triangles within `radius` of the point that face the same way as the struck surface,
    // projected-UV'd around the point (stretched along the shot) and lifted along their normals.
    private Mesh Patch(MeshFilter surface, Vector3 point, Vector3 normal, Vector3 direction, float radius, float stretch, float lift)
    {
        MeshSlicer.MeshData data = Data(surface.sharedMesh);
        if (data == null) return null;

        Transform t = surface.transform;
        Vector3 c = t.InverseTransformPoint(point);
        Vector3 n = t.InverseTransformDirection(normal).normalized;
        float scale = Mathf.Max(0.0001f, t.lossyScale.x);
        float r = radius / scale;
        float liftLocal = hull.Size * lift / scale;

        // Projection axes: u along the shot as seen on the surface, v across it.
        Vector3 along = Vector3.ProjectOnPlane(t.InverseTransformDirection(direction), n);
        if (along.sqrMagnitude < 0.0001f) along = Vector3.Cross(n, Vector3.up);
        if (along.sqrMagnitude < 0.0001f) along = Vector3.Cross(n, Vector3.right);
        along.Normalize();
        float spin = Random.Range(-20f, 20f);
        along = Quaternion.AngleAxis(spin, n) * along;
        Vector3 across = Vector3.Cross(n, along).normalized;
        float ru = r * stretch, rv = r;

        List<Vector3> positions = new List<Vector3>();
        List<Vector3> normals = new List<Vector3>();
        List<Vector2> uvs = new List<Vector2>();
        List<int> triangles = new List<int>();
        Dictionary<int, int> remap = new Dictionary<int, int>();
        float reachSqr = Mathf.Max(ru, rv) * Mathf.Max(ru, rv) * 1.2f;

        foreach (int[] tris in data.submeshes)
        {
            for (int k = 0; k < tris.Length; k += 3)
            {
                Vector3 a = data.positions[tris[k]], b = data.positions[tris[k + 1]], d = data.positions[tris[k + 2]];
                Vector3 centroid = (a + b + d) / 3f;
                if ((centroid - c).sqrMagnitude > reachSqr) continue;
                Vector3 faceNormal = Vector3.Cross(b - a, d - a);
                if (faceNormal.sqrMagnitude < 1e-12f || Vector3.Dot(faceNormal.normalized, n) < 0.25f) continue;
                // Skip triangles wholly outside the mark's ellipse.
                if (!Overlaps(a, b, d, c, along, across, ru, rv)) continue;

                for (int j = 0; j < 3; j++)
                {
                    int index = tris[k + j];
                    if (!remap.TryGetValue(index, out int mapped))
                    {
                        Vector3 p = data.positions[index];
                        Vector3 vn = data.hasNormals ? data.normals[index] : faceNormal.normalized;
                        mapped = positions.Count;
                        remap[index] = mapped;
                        positions.Add(p + vn * liftLocal);
                        normals.Add(vn);
                        Vector3 offset = p - c;
                        uvs.Add(new Vector2(Vector3.Dot(offset, along) / (2f * ru) + 0.5f, Vector3.Dot(offset, across) / (2f * rv) + 0.5f));
                    }
                    triangles.Add(mapped);
                }
            }
        }
        if (triangles.Count < 3) return null;

        Mesh mesh = new Mesh { name = "HullMark" };
        if (positions.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.SetVertices(positions);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        Color[] white = new Color[positions.Count];
        for (int i = 0; i < white.Length; i++) white[i] = Color.white;
        mesh.colors = white;
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    private static bool Overlaps(Vector3 a, Vector3 b, Vector3 d, Vector3 c, Vector3 along, Vector3 across, float ru, float rv)
    {
        float minU = float.MaxValue, maxU = float.MinValue, minV = float.MaxValue, maxV = float.MinValue;
        foreach (Vector3 p in new[] { a, b, d })
        {
            float u = Vector3.Dot(p - c, along), v = Vector3.Dot(p - c, across);
            minU = Mathf.Min(minU, u); maxU = Mathf.Max(maxU, u);
            minV = Mathf.Min(minV, v); maxV = Mathf.Max(maxV, v);
        }
        return maxU >= -ru && minU <= ru && maxV >= -rv && minV <= rv;
    }

    private static MeshSlicer.MeshData Data(Mesh mesh)
    {
        if (mesh == null) return null;
        if (!meshData.TryGetValue(mesh, out MeshSlicer.MeshData data)) meshData[mesh] = data = MeshSlicer.MeshData.Read(mesh);
        return data;
    }

    private void Tint(Renderer renderer, float brightness)
    {
        block ??= new MaterialPropertyBlock();
        Material[] materials = renderer.sharedMaterials;
        for (int i = 0; i < materials.Length; i++)
        {
            if (materials[i] == null || !materials[i].HasProperty("_BaseColor")) continue;
            renderer.GetPropertyBlock(block, i);
            Color original = materials[i].GetColor("_BaseColor");
            block.SetColor("_BaseColor", new Color(original.r * brightness, original.g * brightness, original.b * brightness, original.a));
            renderer.SetPropertyBlock(block, i);
        }
    }

    // ---------------- Materials and textures (generated once) ----------------

    private static Material MakeMaterial(Texture2D texture, Color color)
    {
        Material material = new Material(Shader.Find("Sprites/Default"));
        material.mainTexture = texture;
        material.color = color;
        material.renderQueue = 3001;
        return material;
    }

    private static Material GlowMaterial(Texture2D texture)
    {
        Material source = Resources.Load<Material>("FTL/FTLGlow");
        Material material = source != null ? new Material(source) : MakeMaterial(texture, EmberColor);
        material.SetTexture("_MainTex", texture);
        material.SetColor("_Color", EmberColor);
        material.SetFloat("_Intensity", 1f);
        material.renderQueue = 3002;
        return material;
    }

    // Glowing cracks: the scar's crack lines, hot at the heart.
    private static Texture2D CrackGlowTexture()
    {
        float[] cracks = new float[6];
        for (int i = 0; i < cracks.Length; i++) cracks[i] = Random.Range(0f, Mathf.PI * 2f);
        return Paint((u, v, seed) =>
        {
            Vector2 p = new Vector2(u - 0.5f, v - 0.5f) * 2f;
            float r = p.magnitude;
            float angle = Mathf.Atan2(p.y, p.x);
            float crack = 0f;
            foreach (float c in cracks)
            {
                float wobble = (Mathf.PerlinNoise(Mathf.Floor(r * 9f) * 0.7f, c * 3f) - 0.5f) * 0.18f; // Jagged, not wavy.
                float d = Mathf.Abs(Mathf.DeltaAngle(angle * Mathf.Rad2Deg, (c + wobble) * Mathf.Rad2Deg)) * Mathf.Deg2Rad * r;
                if (r < 0.8f) crack = Mathf.Max(crack, Mathf.Clamp01(1f - d / 0.018f) * (1f - r / 0.8f));
            }
            float heart = Mathf.Clamp01(1f - r / 0.25f) * Mathf.PerlinNoise(u * 20f + seed, v * 20f);
            return Mathf.Clamp01(crack + heart);
        });
    }

    private const int Res = 128;

    // Dark streaks along the line of fire, ragged at the ends.
    private static Texture2D ScoreTexture()
    {
        return Paint((u, v, seed) =>
        {
            float across = Mathf.Abs(v - 0.5f) * 2f;
            float along = Mathf.Abs(u - 0.5f) * 2f;
            float streaks = Mathf.PerlinNoise(seed, v * 22f) * 0.7f + Mathf.PerlinNoise(u * 6f, v * 40f + seed) * 0.5f;
            float body = Mathf.Clamp01(1f - along * along) * Mathf.Clamp01(1f - across * 1.15f);
            return Mathf.Clamp01(body * (0.6f + streaks) - 0.05f);
        });
    }

    // A wide charred blotch with a blackened core and a ragged edge.
    private static Texture2D ScorchTexture()
    {
        return Paint((u, v, seed) =>
        {
            float r = Vector2.Distance(new Vector2(u, v), new Vector2(0.5f, 0.5f)) * 2f;
            float edge = 0.75f + 0.35f * (Mathf.PerlinNoise(u * 7f + seed, v * 7f) - 0.5f) * 2f;
            float core = Mathf.Clamp01(1f - r / Mathf.Max(0.2f, edge));
            float mottle = 0.75f + 0.25f * Mathf.PerlinNoise(u * 18f, v * 18f + seed);
            return Mathf.Clamp01(Mathf.Pow(core, 0.6f) * mottle * 1.2f);
        });
    }

    // A deep burn with cracks running out across the plating.
    private static Texture2D ScarTexture()
    {
        float[] cracks = new float[7];
        for (int i = 0; i < cracks.Length; i++) cracks[i] = Random.Range(0f, Mathf.PI * 2f);
        return Paint((u, v, seed) =>
        {
            Vector2 p = new Vector2(u - 0.5f, v - 0.5f) * 2f;
            float r = p.magnitude;
            float burn = Mathf.Clamp01(1f - r / (0.55f + 0.2f * Mathf.PerlinNoise(u * 6f + seed, v * 6f)));
            float crack = 0f;
            float angle = Mathf.Atan2(p.y, p.x);
            foreach (float c in cracks)
            {
                float wobble = (Mathf.PerlinNoise(r * 5f, c * 3f) - 0.5f) * 0.5f;
                float d = Mathf.Abs(Mathf.DeltaAngle(angle * Mathf.Rad2Deg, (c + wobble) * Mathf.Rad2Deg)) * Mathf.Deg2Rad * r;
                if (r < 0.95f) crack = Mathf.Max(crack, Mathf.Clamp01(1f - d / 0.025f) * (1f - r));
            }
            return Mathf.Clamp01(Mathf.Max(Mathf.Pow(burn, 0.5f), crack * 1.5f));
        });
    }

    // The hole: solid black, ragged-edged.
    private static Texture2D WoundTexture()
    {
        return Paint((u, v, seed) =>
        {
            float r = Vector2.Distance(new Vector2(u, v), new Vector2(0.5f, 0.5f)) * 2f;
            float edge = 0.6f + 0.3f * Mathf.PerlinNoise(u * 9f + seed, v * 9f);
            return r < edge ? 1f : Mathf.Clamp01(1f - (r - edge) / 0.08f);
        });
    }

    // Hot embers: patchy glowing speckle, brighter at the heart.
    private static Texture2D EmberTexture()
    {
        return Paint((u, v, seed) =>
        {
            float r = Vector2.Distance(new Vector2(u, v), new Vector2(0.5f, 0.5f)) * 2f;
            float speckle = Mathf.Clamp01((Mathf.PerlinNoise(u * 22f + seed, v * 22f) - 0.5f) * 4f);
            return Mathf.Clamp01((1f - r * 1.3f) * 1.6f) * speckle;
        });
    }

    private static Texture2D Paint(System.Func<float, float, float, float> alpha)
    {
        Texture2D texture = new Texture2D(Res, Res, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        Color32[] pixels = new Color32[Res * Res];
        float seed = Random.Range(0f, 100f);
        for (int y = 0; y < Res; y++)
        {
            for (int x = 0; x < Res; x++)
            {
                float u = (x + 0.5f) / Res, v = (y + 0.5f) / Res;
                float a = alpha(u, v, seed);
                // Fade to nothing at the patch boundary so marks never show a hard edge.
                float border = Mathf.Clamp01(Mathf.Min(Mathf.Min(u, 1f - u), Mathf.Min(v, 1f - v)) * 12f);
                pixels[y * Res + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a * border) * 255f));
            }
        }
        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        return texture;
    }

    private static Vector3 Divide(Vector3 a, Vector3 b) => new Vector3(a.x / b.x, a.y / b.y, a.z / b.z);
}
