using System.Collections.Generic;
using UnityEngine;

// Animated chevrons marching along a route on the galactic map (the fleet move preview). Green for a clear
// route, red when a battle is expected on the way or at the destination; GalacticMapManager picks the colour.
public class RouteArrows : MonoBehaviour
{
    public float spacing = 2.4f;   // map units between chevrons
    public float size = 0.7f;      // chevron arm length
    public float width = 0.18f;
    public float speed = 2.5f;     // map units per second along the route
    public int maxArrows = 120;

    private readonly List<LineRenderer> pool = new List<LineRenderer>();
    private readonly List<Vector3> points = new List<Vector3>();
    private readonly List<float> cumulative = new List<float>();
    private Material material;
    private Color color = Color.green;
    private bool showing;

    public void Show(IList<Vector3> path, Color tint)
    {
        points.Clear();
        points.AddRange(path);
        color = tint;
        showing = points.Count >= 2;

        cumulative.Clear();
        float total = 0f;
        cumulative.Add(0f);
        for (int i = 1; i < points.Count; i++)
        {
            total += Vector3.Distance(points[i - 1], points[i]);
            cumulative.Add(total);
        }
        if (total <= 0.01f) showing = false;
        if (!showing) Hide();
    }

    public void Hide()
    {
        showing = false;
        foreach (LineRenderer arrow in pool) arrow.enabled = false;
    }

    private void Update()
    {
        if (!showing) return;

        float total = cumulative[cumulative.Count - 1];
        // Unscaled time, so the arrows keep moving while the map is paused.
        float offset = Mathf.Repeat(Time.unscaledTime * speed, spacing);
        int used = 0;
        for (float d = offset; d < total && used < maxArrows; d += spacing)
        {
            Locate(d, out Vector3 position, out Vector3 direction);
            LineRenderer arrow = Arrow(used++);
            Vector3 side = Vector3.Cross(Vector3.up, direction).normalized;
            arrow.SetPosition(0, position - direction * size + side * size * 0.75f);
            arrow.SetPosition(1, position);
            arrow.SetPosition(2, position - direction * size - side * size * 0.75f);
            // Fade in at the start of the route and out at the end, so arrows don't pop.
            float edge = Mathf.Clamp01(Mathf.Min(d, total - d) / spacing);
            Color tint = new Color(color.r, color.g, color.b, color.a * edge);
            arrow.startColor = tint;
            arrow.endColor = tint;
            arrow.enabled = true;
        }
        for (int i = used; i < pool.Count; i++) pool[i].enabled = false;
    }

    private void Locate(float distance, out Vector3 position, out Vector3 direction)
    {
        for (int i = 1; i < points.Count; i++)
        {
            if (distance > cumulative[i] && i < points.Count - 1) continue;
            float length = Mathf.Max(0.0001f, cumulative[i] - cumulative[i - 1]);
            float t = Mathf.Clamp01((distance - cumulative[i - 1]) / length);
            direction = (points[i] - points[i - 1]).normalized;
            position = Vector3.Lerp(points[i - 1], points[i], t);
            return;
        }
        direction = Vector3.forward;
        position = points[points.Count - 1];
    }

    private LineRenderer Arrow(int index)
    {
        while (pool.Count <= index)
        {
            if (material == null) material = new Material(Shader.Find("Sprites/Default"));
            GameObject go = new GameObject("RouteArrow");
            go.transform.SetParent(transform, false);
            LineRenderer line = go.AddComponent<LineRenderer>();
            line.material = material;
            line.positionCount = 3;
            line.widthMultiplier = width;
            line.numCapVertices = 2;
            line.numCornerVertices = 2;
            line.useWorldSpace = true;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.enabled = false;
            pool.Add(line);
        }
        return pool[index];
    }
}
