using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;

public class PlacementManager : MonoBehaviour
{
    public static PlacementManager Instance;

    [Header("Placement Hologram")]
    public Material hologramMaterial;
    public Color validColor = new Color(0.3f, 0.85f, 1f, 0.35f);
    public Color invalidColor = new Color(1f, 0.3f, 0.3f, 0.35f);
    public float emissionStrength = 1.5f;

    private static readonly int BaseColorID = Shader.PropertyToID("_BaseColor");
    private static readonly int EmissionColorID = Shader.PropertyToID("_EmissionColor");

    private GameObject hologram;
    private Renderer[] hologramRenderers;
    private MaterialPropertyBlock hologramProperties;
    private int shownValidity = -1;

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
    }

    public bool TryPlace(Ship ship, Vector2 screenPosition, GameObject sourceIcon)
    {
        EndPreview();

        if (ship == null || ship.prefab == null) return false;
        if (!CanDeploy()) return false;
        if (!TryGetPlacementPoint(screenPosition, out Vector3 point)) return false;

        GameObject spawned = Instantiate(ship.prefab, point, Quaternion.identity);
        GameManager.TagShip(spawned, ship, GameManager.Instance == null || GameManager.Instance.PlayerIsAttacker);

        Destroy(sourceIcon);
        return true;
    }

    public void BeginPreview(Ship ship)
    {
        EndPreview();
        if (ship == null || ship.prefab == null || hologramMaterial == null) return;

        hologram = BuildHologram(ship.prefab);
        hologramRenderers = hologram.GetComponentsInChildren<Renderer>(true);
        shownValidity = -1;
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

        int validity = CanDeploy() ? 1 : 0;
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
            Destroy(hologram);
        }

        hologram = null;
        hologramRenderers = null;
    }

    private static bool CanDeploy()
    {
        if (PlayerSpaceManager.PlayerRetreating) return false;
        return GameManager.Instance == null || GameManager.Instance.CanDeployPlayerShip();
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

        foreach (Canvas canvas in model.GetComponentsInChildren<Canvas>(true))
        {
            if (canvas != null) DestroyImmediate(canvas.gameObject);
        }

        foreach (MonoBehaviour behaviour in model.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (behaviour != null) DestroyImmediate(behaviour);
        }

        StripAll<NavMeshAgent>(model);
        StripAll<NavMeshObstacle>(model);
        StripAll<Rigidbody>(model);
        StripAll<Collider>(model);
        StripAll<AudioSource>(model);
        StripAll<Light>(model);

        foreach (ParticleSystem particles in model.GetComponentsInChildren<ParticleSystem>(true))
        {
            ParticleSystem.MainModule main = particles.main;
            main.playOnAwake = false;
        }

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

    private static void StripAll<T>(GameObject model) where T : Component
    {
        foreach (T component in model.GetComponentsInChildren<T>(true))
        {
            if (component != null) DestroyImmediate(component);
        }
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
