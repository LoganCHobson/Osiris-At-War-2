using UnityEngine;

// Puts the planet the battle is being fought over into the sky, below the fleets, using the same look the
// planet has on the galactic map. Replaces the old fixed planet picture on the skybox's bottom face.
public class SkyPlanet : MonoBehaviour
{
    private const string MaterialPath = "Planets/SkyPlanet";

    [Header("Placement")]
    [Tooltip("Direction from the battle to the planet's centre.")]
    public Vector3 planetDirection = new Vector3(0.12f, -1f, 0.18f);
    [Tooltip("Planet's north pole - tilts the world so it isn't seen dead-on from a pole.")]
    public Vector3 northPole = new Vector3(0.3f, 0.25f, 1f);
    [Tooltip("Half the planet's size in the sky, in degrees (before the planet's own size is applied).")]
    [Range(10f, 80f)] public float angularRadius = 38f;

    [Header("Lighting")]
    [Tooltip("Where sunlight comes from. Mostly sideways so the night side and its city lights show.")]
    public Vector3 sunDirection = new Vector3(-0.75f, 0.35f, 0.3f);

    [Header("Motion")]
    public float spin = 0.004f;
    public float cloudDrift = 0.002f;

    [Header("Fallback (no battle context, e.g. playing BattleScene directly)")]
    public PlanetArchetype fallbackArchetype = PlanetArchetype.Tundra;
    public int fallbackSeed = 1;

    private Material material;
    private Mesh mesh;

    public PlanetAppearance Appearance { get; private set; }

    public static SkyPlanet Create()
    {
        SkyPlanet existing = FindFirstObjectByType<SkyPlanet>();
        if (existing != null) return existing;
        return new GameObject("SkyPlanet").AddComponent<SkyPlanet>();
    }

    private void Start()
    {
        Material source = Resources.Load<Material>(MaterialPath);
        if (source == null)
        {
            Debug.LogWarning($"SkyPlanet: missing Resources/{MaterialPath} material - no planet in the sky.");
            return;
        }

        material = new Material(source);
        BuildFullScreenTriangle();
        ClearSkyboxPlanet();
        Show(PickAppearance());
    }

    public void Show(PlanetAppearance appearance)
    {
        Appearance = appearance;
        if (material == null || appearance == null) return;

        appearance.ApplyTo(material);
        material.SetFloat("_Spin", spin);
        material.SetFloat("_CloudSpin", cloudDrift);
        material.SetVector("_SkyCenterDir", planetDirection.normalized);
        material.SetVector("_SkyPoleDir", northPole.normalized);
        material.SetVector("_SkySunDir", sunDirection.normalized);

        float radiusDegrees = Mathf.Clamp(angularRadius * appearance.size, 8f, 85f);
        material.SetFloat("_SkyAngularRadius", Mathf.Sin(radiusDegrees * Mathf.Deg2Rad));
    }

    private PlanetAppearance PickAppearance()
    {
        BattleContext context = BattleContext.Instance;
        if (context != null && context.destinationAppearance != null && context.destinationAppearance.generated)
        {
            return context.destinationAppearance;
        }
        return PlanetArchetypes.Create(fallbackArchetype, fallbackSeed);
    }

    // One triangle that covers the whole screen; the shader places it at the far plane.
    private void BuildFullScreenTriangle()
    {
        mesh = new Mesh { name = "SkyPlanetTriangle" };
        mesh.vertices = new[] { new Vector3(-1f, -1f, 0f), new Vector3(3f, -1f, 0f), new Vector3(-1f, 3f, 0f) };
        mesh.triangles = new[] { 0, 1, 2 };
        mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e7f); // Never frustum-culled.

        gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer meshRenderer = gameObject.AddComponent<MeshRenderer>();
        meshRenderer.sharedMaterial = material;
        meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        meshRenderer.receiveShadows = false;
    }

    // The old skybox had a planet photo on its bottom face - swap in a starfield face so there aren't two planets.
    private static void ClearSkyboxPlanet()
    {
        Material skybox = RenderSettings.skybox;
        if (skybox == null || !skybox.HasProperty("_DownTex") || !skybox.HasProperty("_BackTex")) return;

        Material copy = new Material(skybox);
        copy.SetTexture("_DownTex", skybox.GetTexture("_BackTex"));
        RenderSettings.skybox = copy;
    }

    private void OnDestroy()
    {
        if (material != null) Destroy(material);
        if (mesh != null) Destroy(mesh);
    }
}
