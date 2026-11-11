using System.Collections.Generic;
using UnityEngine;

// While the battle is paused, shows where the selected ships are headed and at what height: a ring at each
// ordered spot, lifted to the ship's target elevation, with a stalk down to the battle plane. Driven each
// frame by PlayerSpaceManager (hover preview of the next order, plus orders already given).
public class OrderPreview : MonoBehaviour
{
    public float ringRadius = 6f;
    public float lineWidth = 0.8f;
    public int ringSegments = 24;

    private readonly List<(LineRenderer ring, LineRenderer stalk)> pool = new List<(LineRenderer, LineRenderer)>();
    private int used;
    private Material material;

    public void BeginFrame() => used = 0;

    public void Add(Vector3 ground, float altitude, Color color)
    {
        (LineRenderer ring, LineRenderer stalk) marker = Marker(used++);
        Vector3 lifted = ground + Vector3.up * altitude;

        for (int i = 0; i < ringSegments; i++)
        {
            float angle = i * Mathf.PI * 2f / ringSegments;
            marker.ring.SetPosition(i, lifted + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * ringRadius);
        }
        marker.stalk.SetPosition(0, ground);
        marker.stalk.SetPosition(1, lifted);

        Color faint = new Color(color.r, color.g, color.b, color.a * 0.35f);
        marker.ring.startColor = marker.ring.endColor = color;
        marker.stalk.startColor = faint;
        marker.stalk.endColor = color;
        marker.ring.enabled = true;
        marker.stalk.enabled = Mathf.Abs(altitude) > 0.5f; // Nothing to show at plane level.
    }

    public void EndFrame()
    {
        for (int i = used; i < pool.Count; i++)
        {
            pool[i].ring.enabled = false;
            pool[i].stalk.enabled = false;
        }
    }

    private (LineRenderer, LineRenderer) Marker(int index)
    {
        while (pool.Count <= index)
        {
            if (material == null) material = new Material(Shader.Find("Sprites/Default"));
            LineRenderer ring = Line("OrderRing", ringSegments, true);
            LineRenderer stalk = Line("OrderStalk", 2, false);
            pool.Add((ring, stalk));
        }
        return pool[index];
    }

    private LineRenderer Line(string name, int points, bool loop)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(transform, false);
        LineRenderer line = go.AddComponent<LineRenderer>();
        line.material = material;
        line.positionCount = points;
        line.loop = loop;
        line.widthMultiplier = lineWidth;
        line.useWorldSpace = true;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.enabled = false;
        return line;
    }
}
