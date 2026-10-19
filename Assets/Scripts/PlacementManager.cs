using System.Collections.Generic;
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
    }

    public bool TryPlace(Ship ship, Vector2 screenPosition, GameObject sourceIcon)
    {
        EndPreview();

        if (ship == null || ship.prefab == null) return false;
        if (!CanDeploy(ship)) return false;
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
        previewShip = ship;
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

        int validity = CanDeploy(previewShip) ? 1 : 0;
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

        foreach (Canvas canvas in model.GetComponentsInChildren<Canvas>(true))
        {
            if (canvas != null) DestroyImmediate(canvas.gameObject);
        }

        StripBehaviours(model);

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

    // Removes every script, dependents first - Unity refuses to remove a component another one
    // [RequireComponent]s (e.g. ShipDestruction -> HullDamage -> UnitHealthManager), which would leave
    // live gameplay scripts running on the hologram.
    private static void StripBehaviours(GameObject model)
    {
        List<MonoBehaviour> remaining = new List<MonoBehaviour>(model.GetComponentsInChildren<MonoBehaviour>(true));
        bool progress = true;

        while (remaining.Count > 0 && progress)
        {
            progress = false;
            for (int i = remaining.Count - 1; i >= 0; i--)
            {
                MonoBehaviour behaviour = remaining[i];
                if (behaviour != null && IsRequiredByAnother(behaviour, remaining)) continue;

                if (behaviour != null) DestroyImmediate(behaviour);
                remaining.RemoveAt(i);
                progress = true;
            }
        }
    }

    private static bool IsRequiredByAnother(MonoBehaviour target, List<MonoBehaviour> remaining)
    {
        System.Type targetType = target.GetType();

        foreach (MonoBehaviour other in remaining)
        {
            if (other == null || other == target || other.gameObject != target.gameObject) continue;

            foreach (RequireComponent requirement in other.GetType().GetCustomAttributes(typeof(RequireComponent), true))
            {
                if (Requires(requirement.m_Type0, targetType) || Requires(requirement.m_Type1, targetType) || Requires(requirement.m_Type2, targetType))
                {
                    return true;
                }
            }
        }
        return false;
    }

    private static bool Requires(System.Type required, System.Type candidate)
    {
        return required != null && required.IsAssignableFrom(candidate);
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
