using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public class PlacementManager : MonoBehaviour
{
    public static PlacementManager Instance;

    [Header("Placement Hologram")]
    public Material hologramMaterial;
    public Color validColor = new Color(0.3f, 0.85f, 1f, 0.35f);
    public Color invalidColor = new Color(1f, 0.3f, 0.3f, 0.35f);
    public float emissionStrength = 1.5f;
    [Tooltip("Seconds into the battle before holograms for the reinforcement list start being pre-built.")]
    public float prewarmDelay = 1f;

    private static readonly int BaseColorID = Shader.PropertyToID("_BaseColor");
    private static readonly int EmissionColorID = Shader.PropertyToID("_EmissionColor");

    private GameObject hologram;
    private Renderer[] hologramRenderers;

    // Building a hologram instantiates the whole ship prefab, which hitches - so each ship type's hologram is
    // built once (ahead of time where possible) and reused for every drag.
    private class CachedHologram
    {
        public GameObject root;
        public Renderer[] renderers;
    }

    private readonly Dictionary<GameObject, CachedHologram> holograms = new Dictionary<GameObject, CachedHologram>();
    private readonly Queue<GameObject> prewarmQueue = new Queue<GameObject>();
    private MaterialPropertyBlock hologramProperties;
    private int shownValidity = -1;
    private Ship previewShip;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void OnDestroy()
    {
        EndPreview();
        foreach (CachedHologram cached in holograms.Values)
        {
            if (cached.root != null) Destroy(cached.root);
        }
        holograms.Clear();
    }

    // Queue a ship type's hologram to be built in the background so its first drag is smooth.
    public void Prewarm(Ship ship)
    {
        if (ship == null || ship.prefab == null || hologramMaterial == null) return;
        if (holograms.ContainsKey(ship.prefab) || prewarmQueue.Contains(ship.prefab)) return;
        prewarmQueue.Enqueue(ship.prefab);
    }

    private void Update()
    {
        // One per few frames, never mid-drag, so the cost is spread out where nobody notices.
        if (prewarmQueue.Count == 0 || hologram != null || Time.timeSinceLevelLoad < prewarmDelay || Time.frameCount % 4 != 0) return;
        GetHologram(prewarmQueue.Dequeue());
    }

    private CachedHologram GetHologram(GameObject prefab)
    {
        if (holograms.TryGetValue(prefab, out CachedHologram cached) && cached.root != null) return cached;

        GameObject root = BuildHologram(prefab);
        cached = new CachedHologram { root = root, renderers = root.GetComponentsInChildren<Renderer>(true) };
        holograms[prefab] = cached;
        return cached;
    }

    public bool TryPlace(Ship ship, Vector2 screenPosition, GameObject sourceIcon)
    {
        EndPreview();

        if (ship == null || ship.prefab == null) return false;
        if (!CanDeploy(ship)) return false;
        if (!TryGetPlacementPoint(screenPosition, out Vector3 point)) return false;
        if (!CanDropAt(point)) return false; // Asteroids or an enemy shipyard/station in the way.

        GameObject spawned = Instantiate(ship.prefab, point, Quaternion.identity);
        GameManager.TagShip(spawned, ship, PlayerSide);
        FTLJump.Arrive(spawned);

        Destroy(sourceIcon);
        return true;
    }

    public void BeginPreview(Ship ship)
    {
        EndPreview();
        if (ship == null || ship.prefab == null || hologramMaterial == null) return;

        CachedHologram cached = GetHologram(ship.prefab);
        hologram = cached.root;
        hologramRenderers = cached.renderers;
        shownValidity = -1;
        previewShip = ship;
        DropZoneOverlay.Show(PlayerSide);
    }

    public void UpdatePreview(Vector2 screenPosition)
    {
        if (hologram == null) return;

        bool hasPoint = TryGetPlacementPoint(screenPosition, out Vector3 point);
        if (hologram.activeSelf != hasPoint)
        {
            hologram.SetActive(hasPoint);
        }
        if (!hasPoint) return;

        hologram.transform.SetPositionAndRotation(point, Quaternion.identity);

        int validity = CanDeploy(previewShip) && CanDropAt(point) ? 1 : 0;
        if (validity != shownValidity)
        {
            shownValidity = validity;
            ApplyHologramColor(validity == 1 ? validColor : invalidColor);
        }
    }

    public void EndPreview()
    {
        if (hologram != null)
        {
            hologram.SetActive(false); // Kept for the next drag.
        }
        DropZoneOverlay.Hide();

        hologram = null;
        hologramRenderers = null;
    }

    private static bool PlayerSide => GameManager.Instance == null || GameManager.Instance.PlayerIsAttacker;

    private static bool CanDropAt(Vector3 point)
    {
        return BattleTerrain.Instance == null || BattleTerrain.Instance.CanDrop(point, PlayerSide);
    }

    private static bool CanDeploy(Ship ship)
    {
        if (PlayerSpaceManager.PlayerRetreating) return false;
        return GameManager.Instance == null || GameManager.Instance.CanDeployPlayerShip(ship);
    }

    private static bool TryGetPlacementPoint(Vector2 screenPosition, out Vector3 point)
    {
        point = default;

        Camera cam = Camera.main;
        if (cam == null) return false;

        if (!Physics.Raycast(cam.ScreenPointToRay(screenPosition), out RaycastHit hit)) return false;

        point = hit.point;
        return true;
    }

    private GameObject BuildHologram(GameObject prefab)
    {
        GameObject root = new GameObject("PlacementHologram");
        root.SetActive(false);

        GameObject model = Instantiate(prefab, root.transform);
        model.transform.localPosition = Vector3.zero;
        model.transform.localRotation = Quaternion.identity;

        ShipVisualProxy.Strip(model);

        foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer is MeshRenderer || renderer is SkinnedMeshRenderer)
            {
                Material[] materials = new Material[renderer.sharedMaterials.Length];
                for (int i = 0; i < materials.Length; i++)
                {
                    materials[i] = hologramMaterial;
                }

                renderer.sharedMaterials = materials;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
            else
            {
                renderer.enabled = false;
            }
        }

        return root;
    }

    private void ApplyHologramColor(Color color)
    {
        hologramProperties ??= new MaterialPropertyBlock();
        Color emission = new Color(color.r, color.g, color.b) * emissionStrength;

        foreach (Renderer renderer in hologramRenderers)
        {
            if (renderer == null || !renderer.enabled) continue;

            renderer.GetPropertyBlock(hologramProperties);
            hologramProperties.SetColor(BaseColorID, color);
            hologramProperties.SetColor(EmissionColorID, emission);
            renderer.SetPropertyBlock(hologramProperties);
        }
    }
}
