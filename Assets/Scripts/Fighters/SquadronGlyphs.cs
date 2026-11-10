using System.Collections.Generic;
using UnityEngine;

// Icon shapes for strike craft so you can tell them apart at a glance (colour still says whose they are):
//   fighter     - triangle (an arrowhead)
//   interceptor - tall, narrow diamond (a dart)
//   bomber      - square (a heavy block)
// Drawn once at runtime as anti-aliased white shapes with a dark rim; the icon's colour tints them.
public static class SquadronGlyphs
{
    private const int Size = 64;
    private const float Rim = 3.5f;

    private static readonly Dictionary<FleetRole, Sprite> cache = new Dictionary<FleetRole, Sprite>();

    public static Sprite For(FleetRole role)
    {
        if (role != FleetRole.Fighter && role != FleetRole.Interceptor && role != FleetRole.Bomber) role = FleetRole.Fighter;
        if (cache.TryGetValue(role, out Sprite sprite) && sprite != null) return sprite;

        Texture2D texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
        {
            name = $"SquadronGlyph_{role}",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };

        Color32[] pixels = new Color32[Size * Size];
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                Vector2 p = new Vector2(x + 0.5f - Size * 0.5f, y + 0.5f - Size * 0.5f);
                float d = Distance(role, p);                          // < 0 inside.
                float alpha = Mathf.Clamp01(0.5f - d);
                float body = Mathf.Clamp01(0.5f - (d + Rim));         // Inside the rim.
                byte shade = (byte)Mathf.RoundToInt(Mathf.Lerp(40f, 255f, body));
                pixels[y * Size + x] = new Color32(shade, shade, shade, (byte)Mathf.RoundToInt(alpha * 255f));
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        sprite = Sprite.Create(texture, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f), 100f);
        sprite.name = texture.name;
        cache[role] = sprite;
        return sprite;
    }

    public static string Label(FleetRole role)
    {
        switch (role)
        {
            case FleetRole.Interceptor: return "Interceptors";
            case FleetRole.Bomber: return "Bombers";
            default: return "Fighters";
        }
    }

    private static float Distance(FleetRole role, Vector2 p)
    {
        float r = Size * 0.5f - 2f;
        switch (role)
        {
            case FleetRole.Bomber:
                return Box(p, new Vector2(r * 0.78f, r * 0.78f), r * 0.12f);
            case FleetRole.Interceptor:
                return Diamond(p, r * 0.55f, r);
            default:
                return Triangle(p, r);
        }
    }

    private static float Box(Vector2 p, Vector2 half, float round)
    {
        Vector2 q = new Vector2(Mathf.Abs(p.x), Mathf.Abs(p.y)) - half + Vector2.one * round;
        return new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f)).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - round;
    }

    private static float Diamond(Vector2 p, float halfWidth, float halfHeight)
    {
        // Distance to a rhombus, approximated well enough at icon size.
        float k = Mathf.Abs(p.x) / halfWidth + Mathf.Abs(p.y) / halfHeight - 1f;
        return k * halfWidth * halfHeight / Mathf.Sqrt(halfWidth * halfWidth + halfHeight * halfHeight);
    }

    private static float Triangle(Vector2 p, float r)
    {
        // Upward-pointing triangle: apex at the top, flat base at the bottom.
        Vector2 a = new Vector2(0f, r);
        Vector2 b = new Vector2(-r * 0.9f, -r * 0.75f);
        Vector2 c = new Vector2(r * 0.9f, -r * 0.75f);
        float d = Mathf.Max(Edge(p, a, b), Mathf.Max(Edge(p, b, c), Edge(p, c, a)));
        return d;
    }

    // Signed distance to the line through a->b; positive on the outside (counter-clockwise winding).
    private static float Edge(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 edge = b - a;
        Vector2 normal = new Vector2(edge.y, -edge.x).normalized;
        return Vector2.Dot(p - a, normal);
    }
}
