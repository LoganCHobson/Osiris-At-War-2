using UnityEngine;

public static class InteriorTexture
{
    private const int DeckPitch = 32;
    private const int Seed = 7341;

    public static Texture2D Generate(int size, bool emission)
    {
        System.Random random = new System.Random(Seed);
        float[] lattice = new float[64 * 64];
        for (int i = 0; i < lattice.Length; i++) lattice[i] = (float)random.NextDouble();

        int decks = size / DeckPitch;
        int[][] bulkheads = new int[decks][];
        float[][] rooms = new float[decks][];
        bool[][] burning = new bool[decks][];
        for (int d = 0; d < decks; d++)
        {
            int count = 4 + random.Next(6);
            bulkheads[d] = new int[count];
            rooms[d] = new float[count];
            burning[d] = new bool[count];
            int x = random.Next(size);
            for (int b = 0; b < count; b++)
            {
                bulkheads[d][b] = x % size;
                x += size / count + random.Next(-size / (count * 3), size / (count * 3) + 1);
                rooms[d][b] = (float)random.NextDouble();
                burning[d][b] = random.NextDouble() < 0.2;
            }
            System.Array.Sort(bulkheads[d]);
        }

        int[] spines = { random.Next(size), random.Next(size), random.Next(size) };

        Color[] pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            int deck = y / DeckPitch;
            int inDeck = y % DeckPitch;

            for (int x = 0; x < size; x++)
            {
                float u = (float)x / size;
                float v = (float)y / size;
                float grain = Fbm(lattice, u, v, 5);
                float scorch = Fbm(lattice, u + 0.37f, v + 0.61f, 3);

                int room = RoomIndex(bulkheads[deck], x, size);
                float roomShade = rooms[deck][room];
                int distanceToBulkhead = DistanceToBulkhead(bulkheads[deck], x, size);

                float value = 0.46f + (grain - 0.5f) * 0.14f;
                float glow = 0f;

                if (roomShade < 0.2f) value *= 0.45f;
                else if (roomShade < 0.6f) value *= 0.75f;

                if (burning[deck][room])
                {
                    float heat = Mathf.Pow(inDeck / (float)DeckPitch, 1.6f) * (0.6f + grain * 0.6f);
                    glow = Mathf.Max(glow, heat);
                    value *= 0.6f;
                }

                if (distanceToBulkhead <= 1) value = 0.2f;
                else if (distanceToBulkhead == 2) value *= 1.25f;

                if (inDeck < 5)
                {
                    value = 0.2f + grain * 0.06f;
                    if (inDeck == 4) value = 0.55f;
                }

                foreach (int spine in spines)
                {
                    int dx = Mathf.Abs(WrapDelta(x - spine, size));
                    if (dx < 5)
                    {
                        value = dx == 4 ? 0.22f : 0.48f + grain * 0.1f;
                        if (dx < 2 && (y % 12) < 2) value = 0.62f;
                    }
                }

                if (scorch < 0.42f) value *= Mathf.Lerp(0.35f, 1f, scorch / 0.42f);

                if (inDeck == 5 && grain > 0.64f) glow = Mathf.Max(glow, (grain - 0.64f) * 3.5f);
                if (Hash(x, y) > 0.998f) glow = Mathf.Max(glow, 0.9f);

                if (emission)
                {
                    float g = Mathf.Clamp01(glow);
                    pixels[y * size + x] = new Color(g, g * 0.55f, g * 0.2f, 1f);
                }
                else
                {
                    float tint = Mathf.Clamp01(value);
                    pixels[y * size + x] = new Color(tint, tint * 0.98f, tint * 0.95f, 1f);
                }
            }
        }

        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, true);
        texture.SetPixels(pixels);
        texture.Apply();
        return texture;
    }

    private static int RoomIndex(int[] bulkheads, int x, int size)
    {
        for (int i = bulkheads.Length - 1; i >= 0; i--)
        {
            if (x >= bulkheads[i]) return i;
        }
        return bulkheads.Length - 1;
    }

    private static int DistanceToBulkhead(int[] bulkheads, int x, int size)
    {
        int best = int.MaxValue;
        foreach (int b in bulkheads) best = Mathf.Min(best, Mathf.Abs(WrapDelta(x - b, size)));
        return best;
    }

    private static int WrapDelta(int delta, int size)
    {
        delta %= size;
        if (delta > size / 2) delta -= size;
        if (delta < -size / 2) delta += size;
        return delta;
    }

    private static float Fbm(float[] lattice, float u, float v, int octaves)
    {
        float total = 0f;
        float amplitude = 0.5f;
        int cells = 4;
        for (int o = 0; o < octaves; o++)
        {
            total += Noise(lattice, u, v, cells) * amplitude;
            amplitude *= 0.5f;
            cells *= 2;
        }
        return total / (1f - Mathf.Pow(0.5f, octaves));
    }

    private static float Noise(float[] lattice, float u, float v, int cells)
    {
        float x = Mathf.Repeat(u, 1f) * cells;
        float y = Mathf.Repeat(v, 1f) * cells;
        int x0 = Mathf.FloorToInt(x);
        int y0 = Mathf.FloorToInt(y);
        float fx = x - x0;
        float fy = y - y0;
        fx = fx * fx * (3f - 2f * fx);
        fy = fy * fy * (3f - 2f * fy);

        float a = Lattice(lattice, x0, y0, cells);
        float b = Lattice(lattice, x0 + 1, y0, cells);
        float c = Lattice(lattice, x0, y0 + 1, cells);
        float d = Lattice(lattice, x0 + 1, y0 + 1, cells);
        return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
    }

    private static float Lattice(float[] lattice, int x, int y, int cells)
    {
        x = ((x % cells) + cells) % cells;
        y = ((y % cells) + cells) % cells;
        return lattice[(y * 64 + x * 7 + cells * 13) % lattice.Length];
    }

    private static float Hash(int x, int y)
    {
        uint h = (uint)(x * 374761393 + y * 668265263 + Seed);
        h = (h ^ (h >> 13)) * 1274126177u;
        h ^= h >> 16;
        return (h & 0xFFFFFF) / (float)0x1000000;
    }
}
