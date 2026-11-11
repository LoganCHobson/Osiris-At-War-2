using System.Collections.Generic;
using UnityEngine;

// A soft dark disc on the battle plane under (or over) every warship that isn't on it, sized to the hull,
// with a faint line up to the ship: an at-a-glance read of who is high, who is low, and where they really
// are over the plane. Strike craft are left out (too many, too small). Created by PlayerSpaceManager.
public class PlaneShadows : MonoBehaviour
{
    public float planeHeight = 0f;
    public float minAltitude = 2f;      // closer to the plane than this, no shadow
    public Color shadowColor = new Color(0f, 0f, 0f, 0.35f);
    public Color stalkColor = new Color(1f, 1f, 1f, 0.12f);

    private readonly List<(Transform disc, LineRenderer stalk)> pool = new List<(Transform, LineRenderer)>();
    private readonly Dictionary<UnitHealthManager, float> radius = new Dictionary<UnitHealthManager, float>();
    private Material discMaterial, lineMaterial;
    private Mesh quad;

    private void LateUpdate()
    {
        int used = 0;
        foreach (UnitHealthManager unit in UnitHealthManager.Active)
        {
            if (unit == null || unit.IsDead || unit.IsDefense || unit.GetComponent<Squadron>() != null) continue;
            if (!BattleSensors.PlayerCanSee(unit)) continue;
            Vector3 position = unit.transform.position;
            if (Mathf.Abs(position.y - planeHeight) < minAltitude) continue;

            (Transform disc, LineRenderer stalk) marker = Marker(used++);
            float r = Radius(unit);
            marker.disc.position = new Vector3(position.x, planeHeight + 0.05f, position.z);
            marker.disc.localScale = new Vector3(r * 2f, r * 2f, 1f);
            marker.disc.gameObject.SetActive(true);
            marker.stalk.SetPosition(0, marker.disc.position);
            marker.stalk.SetPosition(1, position);
            marker.stalk.enabled = true;
        }
        for (int i = used; i < pool.Count; i++)
        {
            pool[i].disc.gameObject.SetActive(false);
            pool[i].stalk.enabled = false;
        }
    }

    private float Radius(UnitHealthManager unit)
    {
        if (radius.TryGetValue(unit, out float r)) return r;
        Bounds bounds = new Bounds(unit.transform.position, Vector3.zero);
        foreach (Renderer renderer in unit.GetComponentsInChildren<Renderer>())
        {
            if (renderer is ParticleSystemRenderer || renderer is LineRenderer || renderer is TrailRenderer) continue;
            bounds.Encapsulate(renderer.bounds);
        }
        r = Mathf.Clamp(Mathf.Max(bounds.extents.x, bounds.extents.z), 2f, 60f);
        radius[unit] = r;
        return r;
    }

    private (Transform, LineRenderer) Marker(int index)
    {
        while (pool.Count <= index)
        {
            if (discMaterial == null)
            {
                discMaterial = new Material(Shader.Find("Sprites/Default")) { mainTexture = SoftDisc(), color = shadowColor };
                lineMaterial = new Material(Shader.Find("Sprites/Default"));
                quad = BuildQuad();
            }
            GameObject disc = new GameObject("PlaneShadow");
            disc.transform.SetParent(transform, false);
            disc.transform.rotation = Quaternion.Euler(90f, 0f, 0f); // Flat on the plane.
            disc.AddComponent<MeshFilter>().sharedMesh = quad;
            MeshRenderer renderer = disc.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = discMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            GameObject stalkObject = new GameObject("ShadowStalk");
            stalkObject.transform.SetParent(transform, false);
            LineRenderer stalk = stalkObject.AddComponent<LineRenderer>();
            stalk.sharedMaterial = lineMaterial;
            stalk.positionCount = 2;
            stalk.widthMultiplier = 0.25f;
            stalk.startColor = stalk.endColor = stalkColor;
            stalk.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            stalk.receiveShadows = false;

            disc.SetActive(false);
            stalk.enabled = false;
            pool.Add((disc.transform, stalk));
        }
        return pool[index];
    }

    private static Mesh BuildQuad()
    {
        Mesh mesh = new Mesh();
        mesh.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f) };
        mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
        mesh.triangles = new[] { 0, 2, 1, 2, 3, 1, 0, 1, 2, 2, 1, 3 }; // Both faces: visible from above and below.
        mesh.RecalculateBounds();
        return mesh;
    }

    private static Texture2D SoftDisc()
    {
        const int size = 64;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        Color[] pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(size / 2f, size / 2f)) / (size / 2f);
                pixels[y * size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(1f - d) * Mathf.Clamp01(1f - d));
            }
        texture.SetPixels(pixels);
        texture.Apply();
        return texture;
    }
}
