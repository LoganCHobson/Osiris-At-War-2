using UnityEngine;

// Now and then a dying ship's reactor goes up with a shockwave: a translucent white disc in the ship's plane
// that spreads out fast and fades as it goes, its edge a little brighter than its middle.
public class Shockwave : MonoBehaviour
{
    private static Material material;
    private static Mesh disc;

    private MeshRenderer discRenderer;
    private float bornAt, life, radius;
    private MaterialPropertyBlock block;

    // Only some deaths get one; bigger ships more often (sizeT 0 = smallest, 1 = largest).
    public static void Maybe(Vector3 centre, Vector3 up, float radius, float life, float sizeT)
    {
        if (Random.value > Mathf.Lerp(0.2f, 0.55f, sizeT)) return;
        Spawn(centre, up, radius, life);
    }

    public static void Spawn(Vector3 centre, Vector3 up, float radius, float life)
    {
        if (material == null)
        {
            material = new Material(Shader.Find("Sprites/Default")) { mainTexture = DiscTexture(), renderQueue = 3100 };
            disc = BuildDisc();
        }

        GameObject go = new GameObject("Shockwave");
        go.transform.SetPositionAndRotation(centre, Quaternion.FromToRotation(Vector3.up, up) * Quaternion.Euler(Random.Range(-10f, 10f), Random.Range(0f, 360f), Random.Range(-10f, 10f)));
        Shockwave wave = go.AddComponent<Shockwave>();
        wave.radius = radius;
        wave.life = life;
        wave.bornAt = Time.time;
        go.AddComponent<MeshFilter>().sharedMesh = disc;
        wave.discRenderer = go.AddComponent<MeshRenderer>();
        wave.discRenderer.sharedMaterial = material;
        wave.discRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        wave.discRenderer.receiveShadows = false;
        wave.block = new MaterialPropertyBlock();
        wave.Draw(0f);
    }

    private void Update()
    {
        float t = (Time.time - bornAt) / life;
        if (t >= 1f)
        {
            Destroy(gameObject);
            return;
        }
        Draw(t);
    }

    private void Draw(float t)
    {
        float eased = 1f - (1f - t) * (1f - t) * (1f - t); // Fast out, slowing.
        float r = Mathf.Max(0.1f, radius * eased);
        transform.localScale = new Vector3(r, 1f, r);
        discRenderer.GetPropertyBlock(block);
        block.SetColor("_Color", new Color(1f, 1f, 1f, 0.45f * (1f - t) * (1f - t)));
        discRenderer.SetPropertyBlock(block);
    }

    // A flat, double-sided unit disc (radius 1) in the XZ plane, UVs mapped across it.
    private static Mesh BuildDisc()
    {
        const int segments = 64;
        Vector3[] vertices = new Vector3[segments + 1];
        Vector2[] uvs = new Vector2[segments + 1];
        vertices[0] = Vector3.zero;
        uvs[0] = new Vector2(0.5f, 0.5f);
        for (int i = 0; i < segments; i++)
        {
            float a = i * Mathf.PI * 2f / segments;
            vertices[i + 1] = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            uvs[i + 1] = new Vector2(0.5f + Mathf.Cos(a) * 0.5f, 0.5f + Mathf.Sin(a) * 0.5f);
        }
        int[] triangles = new int[segments * 6];
        for (int i = 0; i < segments; i++)
        {
            int a = i + 1, b = (i + 1) % segments + 1;
            triangles[i * 6] = 0; triangles[i * 6 + 1] = b; triangles[i * 6 + 2] = a;   // Top.
            triangles[i * 6 + 3] = 0; triangles[i * 6 + 4] = a; triangles[i * 6 + 5] = b; // Underside.
        }
        Mesh mesh = new Mesh { name = "ShockwaveDisc", vertices = vertices, uv = uvs, triangles = triangles };
        Color[] white = new Color[vertices.Length];
        for (int i = 0; i < white.Length; i++) white[i] = Color.white;
        mesh.colors = white;
        mesh.RecalculateBounds();
        return mesh;
    }

    // Translucent across the middle, brighter toward the rim, soft at the very edge.
    private static Texture2D DiscTexture()
    {
        const int res = 128;
        Texture2D texture = new Texture2D(res, res, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        Color32[] pixels = new Color32[res * res];
        for (int y = 0; y < res; y++)
        {
            for (int x = 0; x < res; x++)
            {
                float r = Vector2.Distance(new Vector2((x + 0.5f) / res, (y + 0.5f) / res), new Vector2(0.5f, 0.5f)) * 2f;
                float body = Mathf.Lerp(0.35f, 1f, Mathf.Pow(Mathf.Clamp01(r), 3f));
                float edge = Mathf.Clamp01((1f - r) / 0.08f);
                pixels[y * res + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(body * edge) * 255f));
            }
        }
        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        return texture;
    }
}
