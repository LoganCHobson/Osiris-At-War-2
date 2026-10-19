using UnityEngine;

// Shows, while a reinforcement is being dragged, where it can't be dropped: red hatched areas over every
// asteroid field/cluster and around enemy shipyards and battle stations.
public class DropZoneOverlay : MonoBehaviour
{
    private const string MaterialPath = "Battle/DropZoneOverlay";
    private const int Resolution = 160;
    private const float Height = 0.6f;
    private const float FadeSpeed = 6f;

    private static DropZoneOverlay instance;

    private Material material;
    private Texture2D mask;
    private readonly float[] values = new float[Resolution * Resolution];
    private MeshRenderer quad;
    private bool shown;
    private float fade;
    private int builtForSide = -1;
    private int builtForDefenses = -1;

    private static readonly int FadeID = Shader.PropertyToID("_Fade");
    private static readonly int MaskID = Shader.PropertyToID("_MaskTex");

    public static void Show(bool attackerSide)
    {
        if (BattleTerrain.Instance == null) return;
        if (instance == null) instance = new GameObject("DropZoneOverlay").AddComponent<DropZoneOverlay>();
        instance.Open(attackerSide);
    }

    public static void Hide()
    {
        if (instance != null) instance.shown = false;
    }

    private void Open(bool attackerSide)
    {
        if (material == null && !Build()) return;

        int defenses = LiveDefenseCount();
        int side = attackerSide ? 1 : 0;
        if (side != builtForSide || defenses != builtForDefenses)
        {
            BattleTerrain.Instance.FillNoDropMask(values, Resolution, attackerSide);
            Color[] pixels = new Color[values.Length];
            for (int i = 0; i < values.Length; i++) pixels[i] = new Color(values[i], 0f, 0f, 1f);
            mask.SetPixels(pixels);
            mask.Apply();
            builtForSide = side;
            builtForDefenses = defenses;
        }

        shown = true;
        quad.enabled = true;
    }

    private bool Build()
    {
        Material source = Resources.Load<Material>(MaterialPath);
        if (source == null)
        {
            Debug.LogWarning($"DropZoneOverlay: missing Resources/{MaterialPath} material.");
            return false;
        }

        material = new Material(source);
        mask = new Texture2D(Resolution, Resolution, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            name = "NoDropMask"
        };
        material.SetTexture(MaskID, mask);

        Rect map = BattleTerrain.Instance.Map;
        Mesh mesh = new Mesh { name = "DropZoneOverlay" };
        mesh.vertices = new[]
        {
            new Vector3(map.xMin, Height, map.yMin), new Vector3(map.xMax, Height, map.yMin),
            new Vector3(map.xMin, Height, map.yMax), new Vector3(map.xMax, Height, map.yMax)
        };
        mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
        mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
        mesh.RecalculateBounds();

        gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
        quad = gameObject.AddComponent<MeshRenderer>();
        quad.sharedMaterial = material;
        quad.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        quad.receiveShadows = false;
        quad.enabled = false;
        return true;
    }

    private static int LiveDefenseCount()
    {
        int count = 0;
        foreach (UnitHealthManager unit in UnitHealthManager.Active)
        {
            if (unit != null && unit.IsDefense && !unit.IsDead) count++;
        }
        return count;
    }

    private void Update()
    {
        if (quad == null) return;

        fade = Mathf.MoveTowards(fade, shown ? 1f : 0f, Time.unscaledDeltaTime * FadeSpeed);
        material.SetFloat(FadeID, fade);
        if (!shown && fade <= 0f) quad.enabled = false;
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
        if (mask != null) Destroy(mask);
        if (material != null) Destroy(material);
    }
}
