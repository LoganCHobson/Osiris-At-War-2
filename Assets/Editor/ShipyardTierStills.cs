using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Dev check: renders every shipyard tier prefab (as built by OsirisPrefabBuilder.BuildShipyardTiers) with its real
// materials, guns and LODs, from a 3/4 view and from above, to Logs/ShipyardTiers. Also logs each prefab's guns,
// hardpoints, hull and triangle counts. -executeMethod ShipyardTierStills.Run (not -nographics: it renders).
public static class ShipyardTierStills
{
    static readonly string[] Prefabs =
    {
        "Assets/Prefabs/Osiris/Human/Human_Shipyard_T1.prefab",
        "Assets/Prefabs/Osiris/Human/Human_Shipyard.prefab",
        "Assets/Prefabs/Osiris/Human/Human_Shipyard_T3.prefab",
        "Assets/Prefabs/Osiris/Avanti/Avanti_Shipyard_T1.prefab",
        "Assets/Prefabs/Osiris/Avanti/Avanti_Shipyard_T2.prefab",
        "Assets/Prefabs/Osiris/Avanti/Avanti_Shipyard_T3.prefab",
    };

    [MenuItem("Osiris/Render Shipyard Tiers")]
    public static void Run()
    {
        string outDir = Path.Combine(Application.dataPath, "..", "Logs", "ShipyardTiers");
        Directory.CreateDirectory(outDir);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.32f, 0.34f, 0.4f);
        var sun = new GameObject("Sun").AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.intensity = 1.6f;
        sun.transform.rotation = Quaternion.Euler(40f, -35f, 0f);
        var fill = new GameObject("Fill").AddComponent<Light>();
        fill.type = LightType.Directional;
        fill.intensity = 0.5f;
        fill.transform.rotation = Quaternion.Euler(20f, 150f, 0f);

        var cam = new GameObject("Camera").AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.06f, 0.07f, 0.1f);
        cam.fieldOfView = 30f;
        cam.farClipPlane = 5000f;
        var rt = new RenderTexture(1600, 1000, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        cam.targetTexture = rt;

        foreach (string path in Prefabs)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) { Debug.LogError($"[YardStills] missing {path}"); continue; }
            GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.transform.position = Vector3.zero;
            foreach (LODGroup lod in go.GetComponentsInChildren<LODGroup>(true)) lod.ForceLOD(0);

            Renderer[] renderers = go.GetComponentsInChildren<Renderer>(true).Where(r => r.enabled && !(r is ParticleSystemRenderer) && !(r is LineRenderer)).ToArray();
            Bounds b = renderers[0].bounds;
            foreach (Renderer r in renderers) b.Encapsulate(r.bounds);
            int tris = go.GetComponentsInChildren<MeshFilter>(true).Where(f => f.sharedMesh != null && f.name.Contains("LOD0")).Sum(f => f.sharedMesh.triangles.Length / 3);
            int guns = go.GetComponentsInChildren<TurretController>(true).Length;
            int hardpoints = go.GetComponentsInChildren<HardpointHealth>(true).Length;
            float hull = go.GetComponentsInChildren<HardpointHealth>(true).Sum(h => h.maxHealth);
            int hangars = go.GetComponentsInChildren<HangarHardpoint>(true).Length;
            Debug.Log($"[YardStills] {prefab.name}: {guns} turrets, {hardpoints} hardpoints, hull {hull:0}, {hangars} hangars, LOD0 tris {tris}, size {b.size.x:0}x{b.size.z:0}x{b.size.y:0}");

            float radius = b.extents.magnitude;
            float distance = radius / Mathf.Sin(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 0.95f;
            Shot(cam, rt, b.center + Quaternion.Euler(32f, 140f, 0f) * Vector3.back * distance, b.center, Path.Combine(outDir, prefab.name + "_34.png"));
            Shot(cam, rt, b.center + Vector3.up * distance, b.center, Path.Combine(outDir, prefab.name + "_top.png"), Vector3.forward);
            Object.DestroyImmediate(go);
        }
        Debug.Log("[YardStills] done");
    }

    static void Shot(Camera cam, RenderTexture rt, Vector3 from, Vector3 at, string file, Vector3? up = null)
    {
        cam.transform.position = from;
        cam.transform.rotation = Quaternion.LookRotation(at - from, up ?? Vector3.up);
        cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        tex.Apply();
        File.WriteAllBytes(file, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        RenderTexture.active = null;
    }
}
