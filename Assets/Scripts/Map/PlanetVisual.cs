using UnityEngine;

// Paints a galactic map planet with its PlanetAppearance: feeds the body's Osiris/Planet material through a
// property block and adds an atmosphere shell around it. Runs in the editor too, so generated maps show
// their worlds without entering play mode.
[ExecuteAlways]
[RequireComponent(typeof(Planet))]
public class PlanetVisual : MonoBehaviour
{
    // Where the galaxy's light comes from on the map - a little off to one side so worlds show a night edge.
    public static Vector3 MapSunDirection = new Vector3(-0.6f, 0.6f, 0.35f);

    private const string PlanetMaterialPath = "Planets/Planet";
    private const string AtmosphereMaterialPath = "Planets/PlanetAtmosphere";

    private static readonly int SunDirectionID = Shader.PropertyToID("_PlanetSunDirection");
    private static Material atmosphereMaterial;
    private static Material planetMaterial;

    private Planet planet;
    private MeshRenderer body;
    private MeshRenderer atmosphere;
    private MaterialPropertyBlock block;

    private void OnEnable()
    {
        Shader.SetGlobalVector(SunDirectionID, MapSunDirection.normalized);
        Refresh();
    }

    private void OnDisable()
    {
        if (atmosphere != null)
        {
            DestroySafely(atmosphere.gameObject);
            atmosphere = null;
        }
    }

    private void OnValidate()
    {
        if (isActiveAndEnabled && body != null)
        {
            ApplyProperties(); // Inspector tweaks on the planet's appearance show up immediately.
        }
    }

    public void Refresh()
    {
        planet = GetComponent<Planet>();
        body = GetComponent<MeshRenderer>();
        if (planet == null || body == null) return;

        if (Application.isPlaying && (body.sharedMaterial == null || body.sharedMaterial.shader.name != "Osiris/Planet"))
        {
            planetMaterial ??= Resources.Load<Material>(PlanetMaterialPath);
            if (planetMaterial != null) body.sharedMaterial = planetMaterial;
        }

        EnsureAtmosphere();
        ApplyProperties();
    }

    private void ApplyProperties()
    {
        PlanetAppearance appearance = planet.Appearance;
        block ??= new MaterialPropertyBlock();

        body.GetPropertyBlock(block);
        appearance.ApplyTo(block);
        body.SetPropertyBlock(block);

        if (atmosphere != null)
        {
            atmosphere.SetPropertyBlock(block);
            atmosphere.transform.localScale = Vector3.one * (1f + appearance.atmosphereThickness);
            atmosphere.enabled = body.enabled && appearance.atmosphereIntensity > 0.01f;
        }
    }

    private void EnsureAtmosphere()
    {
        if (atmosphere != null) return;

        atmosphereMaterial ??= Resources.Load<Material>(AtmosphereMaterialPath);
        MeshFilter bodyMesh = GetComponent<MeshFilter>();
        if (atmosphereMaterial == null || bodyMesh == null) return;

        GameObject shell = new GameObject("Atmosphere");
        shell.hideFlags = HideFlags.HideAndDontSave;
        shell.layer = gameObject.layer;
        shell.transform.SetParent(transform, false);
        shell.AddComponent<MeshFilter>().sharedMesh = bodyMesh.sharedMesh;

        atmosphere = shell.AddComponent<MeshRenderer>();
        atmosphere.sharedMaterial = atmosphereMaterial;
        atmosphere.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        atmosphere.receiveShadows = false;
    }

    private static void DestroySafely(Object target)
    {
        if (Application.isPlaying) Destroy(target);
        else DestroyImmediate(target);
    }
}
