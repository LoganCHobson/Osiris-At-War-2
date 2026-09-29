using System.Collections.Generic;
using UnityEngine;

public class FogOfWar : MonoBehaviour
{
    public static FogOfWar Instance { get; private set; }

    [Header("Fog Plane")]
    public MeshRenderer fogPlane;
    public float planeHeightOffset = -2f;
    public float mapPadding = 12f;
    public int maskResolution = 128;

    [Header("Reveal Shape")]
    public float planetRevealRadius = 7f;
    public float laneRevealRadius = 3f;
    [Range(0.05f, 1f)] public float edgeSoftness = 0.45f;
    public float fadeSpeed = 2.5f;

    [Header("Fogged Planets")]
    [Range(0f, 1f)] public float foggedPlanetBrightness = 0.35f;

    [Header("Refresh")]
    public float refreshInterval = 0.2f;

    public Faction Viewer { get; private set; }
    public bool RevealAll { get; private set; }
    public bool ViewingAsPlayer => !RevealAll && Viewer != null && Viewer == playerFaction;

    private class PlanetShade
    {
        public Renderer[] renderers;
        public int[] colorIds;
        public Color[] baseColors;
        public float brightness = 1f;
        public float target = 1f;
    }

    private static readonly int MaskTexID = Shader.PropertyToID("_MaskTex");
    private static readonly int FogTimeID = Shader.PropertyToID("_FogTime");
    private static readonly int BaseColorID = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorID = Shader.PropertyToID("_Color");

    private readonly List<Planet> planets = new List<Planet>();
    private readonly HashSet<Planet> visible = new HashSet<Planet>();
    private readonly HashSet<Planet> playerVisible = new HashSet<Planet>();
    private readonly List<PlanetShade> shades = new List<PlanetShade>();
    private readonly Dictionary<Planet, PlanetShade> shadeByPlanet = new Dictionary<Planet, PlanetShade>();

    private Faction playerFaction;
    private Texture2D maskTexture;
    private float[] maskTarget;
    private float[] maskCurrent;
    private byte[] maskBytes;
    private int maskWidth;
    private int maskHeight;
    private Vector2 mapMin;
    private Vector2 mapSize;
    private bool maskAnimating;
    private MaterialPropertyBlock shadeBlock;
    private MaterialPropertyBlock planeBlock;
    private float nextRefreshAt;
    private bool dirty = true;
    private bool firstRefresh = true;

    public static bool CanSee(Planet planet)
    {
        if (Instance == null || planet == null) return true;
        return Instance.RevealAll || Instance.visible.Contains(planet);
    }

    public static PlanetIntel LastSeen(Planet planet)
    {
        if (Instance == null || !Instance.ViewingAsPlayer || planet == null || GalacticState.Instance == null) return null;
        return GalacticState.Instance.playerIntel.TryGetValue(planet.planetName, out PlanetIntel intel) ? intel : null;
    }

    public static float Now => AIDirector.Instance != null ? AIDirector.Instance.GalacticTime : Time.time;

    private void Awake()
    {
        Instance = this;
        shadeBlock = new MaterialPropertyBlock();
        planeBlock = new MaterialPropertyBlock();
        GalacticEvents.PlanetCaptured += OnPlanetCaptured;
    }

    private void OnDestroy()
    {
        GalacticEvents.PlanetCaptured -= OnPlanetCaptured;
        if (Instance == this) Instance = null;
        if (maskTexture != null) Destroy(maskTexture);
    }

    private void Start()
    {
        planets.AddRange(FindObjectsByType<Planet>(FindObjectsSortMode.None));
        foreach (Planet planet in planets)
        {
            BuildShade(planet);
        }

        SetupMask();
    }

    private void OnPlanetCaptured(Planet planet, Faction previousOwner, Faction newOwner)
    {
        dirty = true;
    }

    private void Update()
    {
        if (dirty || Time.unscaledTime >= nextRefreshAt)
        {
            Refresh();
        }

        AnimateMask();
        AnimatePlanets();

        if (fogPlane != null)
        {
            fogPlane.GetPropertyBlock(planeBlock);
            planeBlock.SetFloat(FogTimeID, Time.unscaledTime);
            fogPlane.SetPropertyBlock(planeBlock);
        }
    }

    private void Refresh()
    {
        dirty = false;
        nextRefreshAt = Time.unscaledTime + refreshInterval;

        ResolveViewer();

        GalacticVisibility.Compute(playerFaction, planets, GalacticFleet.All, playerVisible);
        RecordIntel();

        visible.Clear();
        if (!RevealAll)
        {
            if (Viewer == playerFaction)
            {
                visible.UnionWith(playerVisible);
            }
            else
            {
                GalacticVisibility.Compute(Viewer, planets, GalacticFleet.All, visible);
            }
        }

        foreach (GalacticFleet fleet in GalacticFleet.All)
        {
            fleet.SetRevealed(RevealAll || GalacticVisibility.CanSeeFleet(fleet, Viewer, visible));
        }

        foreach (Planet planet in planets)
        {
            if (planet != null && shadeByPlanet.TryGetValue(planet, out PlanetShade shade))
            {
                shade.target = RevealAll || visible.Contains(planet) ? 1f : foggedPlanetBrightness;
            }
        }

        BuildMaskTarget();

        if (firstRefresh)
        {
            firstRefresh = false;
            SnapVisuals();
        }
    }

    private void ResolveViewer()
    {
        if (playerFaction == null)
        {
            playerFaction = FindPlayerFaction();
        }

        AIDebugOverlay overlay = AIDebugOverlay.Instance;
        if (overlay != null && overlay.visible)
        {
            Viewer = overlay.FocusedFaction;
            RevealAll = Viewer == null;
        }
        else
        {
            Viewer = playerFaction;
            RevealAll = playerFaction == null;
        }
    }

    private Faction FindPlayerFaction()
    {
        foreach (Planet planet in planets)
        {
            if (planet != null && planet.owner != null && planet.owner.isPlayerFaction) return planet.owner;
        }

        foreach (GalacticFleet fleet in GalacticFleet.All)
        {
            if (fleet.faction != null && fleet.faction.isPlayerFaction) return fleet.faction;
        }

        return null;
    }

    private void RecordIntel()
    {
        GalacticState state = GalacticState.Instance;
        if (state == null || playerFaction == null) return;

        float now = Now;
        foreach (Planet planet in playerVisible)
        {
            if (!state.playerIntel.TryGetValue(planet.planetName, out PlanetIntel intel))
            {
                intel = new PlanetIntel();
                state.playerIntel[planet.planetName] = intel;
            }

            intel.Record(planet, now);
        }
    }

    private void BuildShade(Planet planet)
    {
        List<Renderer> renderers = new List<Renderer>();
        List<int> ids = new List<int>();
        List<Color> colors = new List<Color>();

        foreach (MeshRenderer meshRenderer in planet.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (meshRenderer == planet.ownershipRing) continue;

            Material material = meshRenderer.sharedMaterial;
            if (material == null) continue;

            int id = material.HasProperty(BaseColorID) ? BaseColorID : material.HasProperty(ColorID) ? ColorID : -1;
            if (id < 0) continue;

            renderers.Add(meshRenderer);
            ids.Add(id);
            colors.Add(material.GetColor(id));
        }

        if (renderers.Count == 0) return;

        PlanetShade shade = new PlanetShade
        {
            renderers = renderers.ToArray(),
            colorIds = ids.ToArray(),
            baseColors = colors.ToArray()
        };
        shades.Add(shade);
        shadeByPlanet[planet] = shade;
    }

    private void AnimatePlanets()
    {
        float step = fadeSpeed * Time.unscaledDeltaTime;
        foreach (PlanetShade shade in shades)
        {
            if (Mathf.Approximately(shade.brightness, shade.target)) continue;

            shade.brightness = Mathf.MoveTowards(shade.brightness, shade.target, step);
            ApplyShade(shade);
        }
    }

    private void ApplyShade(PlanetShade shade)
    {
        for (int i = 0; i < shade.renderers.Length; i++)
        {
            Renderer target = shade.renderers[i];
            if (target == null) continue;

            Color baseColor = shade.baseColors[i];
            Color dimmed = new Color(baseColor.r * shade.brightness, baseColor.g * shade.brightness, baseColor.b * shade.brightness, baseColor.a);

            target.GetPropertyBlock(shadeBlock);
            shadeBlock.SetColor(shade.colorIds[i], dimmed);
            target.SetPropertyBlock(shadeBlock);
        }
    }

    private void SnapVisuals()
    {
        foreach (PlanetShade shade in shades)
        {
            shade.brightness = shade.target;
            ApplyShade(shade);
        }

        if (maskCurrent == null) return;

        System.Array.Copy(maskTarget, maskCurrent, maskTarget.Length);
        UploadMask();
        maskAnimating = false;
    }

    private void SetupMask()
    {
        if (fogPlane == null || planets.Count == 0) return;

        Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
        Vector2 max = new Vector2(float.MinValue, float.MinValue);
        float height = 0f;

        foreach (Planet planet in planets)
        {
            Vector3 position = planet.transform.position;
            min = Vector2.Min(min, new Vector2(position.x, position.z));
            max = Vector2.Max(max, new Vector2(position.x, position.z));
            height += position.y;
        }
        height /= planets.Count;

        float padding = mapPadding + planetRevealRadius;
        mapMin = min - Vector2.one * padding;
        mapSize = (max - min) + Vector2.one * padding * 2f;

        maskWidth = Mathf.Max(8, maskResolution);
        maskHeight = Mathf.Max(8, Mathf.RoundToInt(maskResolution * mapSize.y / Mathf.Max(0.01f, mapSize.x)));

        maskTexture = new Texture2D(maskWidth, maskHeight, TextureFormat.R8, false)
        {
            name = "FogOfWarMask",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };

        int count = maskWidth * maskHeight;
        maskTarget = new float[count];
        maskCurrent = new float[count];
        maskBytes = new byte[count];

        Transform plane = fogPlane.transform;
        plane.position = new Vector3(mapMin.x + mapSize.x * 0.5f, height + planeHeightOffset, mapMin.y + mapSize.y * 0.5f);
        plane.rotation = Quaternion.Euler(90f, 0f, 0f);
        plane.localScale = new Vector3(mapSize.x, mapSize.y, 1f);

        fogPlane.GetPropertyBlock(planeBlock);
        planeBlock.SetTexture(MaskTexID, maskTexture);
        fogPlane.SetPropertyBlock(planeBlock);
    }

    private void BuildMaskTarget()
    {
        if (maskTarget == null) return;

        if (RevealAll)
        {
            for (int i = 0; i < maskTarget.Length; i++) maskTarget[i] = 1f;
        }
        else
        {
            System.Array.Clear(maskTarget, 0, maskTarget.Length);

            foreach (Planet planet in visible)
            {
                Vector3 position = planet.transform.position;
                StampSegment(new Vector2(position.x, position.z), new Vector2(position.x, position.z), planetRevealRadius);
            }

            foreach (Planet planet in visible)
            {
                foreach (Planet connection in planet.connections)
                {
                    if (connection == null || !visible.Contains(connection)) continue;
                    if (planet.GetInstanceID() > connection.GetInstanceID()) continue;

                    Vector3 a = planet.transform.position;
                    Vector3 b = connection.transform.position;
                    StampSegment(new Vector2(a.x, a.z), new Vector2(b.x, b.z), laneRevealRadius);
                }
            }
        }

        maskAnimating = true;
    }

    private void StampSegment(Vector2 a, Vector2 b, float radius)
    {
        float cellX = mapSize.x / maskWidth;
        float cellY = mapSize.y / maskHeight;
        float soft = Mathf.Max(0.001f, radius * edgeSoftness);

        int x0 = Mathf.Clamp(Mathf.FloorToInt((Mathf.Min(a.x, b.x) - radius - mapMin.x) / cellX), 0, maskWidth - 1);
        int x1 = Mathf.Clamp(Mathf.CeilToInt((Mathf.Max(a.x, b.x) + radius - mapMin.x) / cellX), 0, maskWidth - 1);
        int y0 = Mathf.Clamp(Mathf.FloorToInt((Mathf.Min(a.y, b.y) - radius - mapMin.y) / cellY), 0, maskHeight - 1);
        int y1 = Mathf.Clamp(Mathf.CeilToInt((Mathf.Max(a.y, b.y) + radius - mapMin.y) / cellY), 0, maskHeight - 1);

        Vector2 ab = b - a;
        float abLengthSqr = ab.sqrMagnitude;

        for (int y = y0; y <= y1; y++)
        {
            float worldY = mapMin.y + (y + 0.5f) * cellY;
            for (int x = x0; x <= x1; x++)
            {
                Vector2 point = new Vector2(mapMin.x + (x + 0.5f) * cellX, worldY);
                float t = abLengthSqr > 0f ? Mathf.Clamp01(Vector2.Dot(point - a, ab) / abLengthSqr) : 0f;
                float distance = Vector2.Distance(point, a + ab * t);

                float value = Mathf.Clamp01((radius - distance) / soft);
                int index = y * maskWidth + x;
                if (value > maskTarget[index]) maskTarget[index] = value;
            }
        }
    }

    private void AnimateMask()
    {
        if (!maskAnimating || maskCurrent == null) return;

        float step = fadeSpeed * Time.unscaledDeltaTime;
        bool changed = false;
        bool settled = true;

        for (int i = 0; i < maskCurrent.Length; i++)
        {
            float current = maskCurrent[i];
            float target = maskTarget[i];
            if (current == target) continue;

            current = Mathf.MoveTowards(current, target, step);
            maskCurrent[i] = current;
            changed = true;
            if (current != target) settled = false;
        }

        if (changed) UploadMask();
        maskAnimating = !settled;
    }

    private void UploadMask()
    {
        for (int i = 0; i < maskCurrent.Length; i++)
        {
            maskBytes[i] = (byte)(maskCurrent[i] * 255f);
        }

        maskTexture.SetPixelData(maskBytes, 0);
        maskTexture.Apply(false);
    }
}
