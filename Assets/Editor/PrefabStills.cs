using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

// Dev check: renders ship prefabs from several angles to PNGs (Logs/PrefabStills) so their models can be
// inspected without opening the editor. -prefabs "Human/Human_Destroyer,Human/Human_Corvette" picks which.
// Run: Unity -batchmode -projectPath ... -executeMethod PrefabStills.Run -prefabs ...
public static class PrefabStills
{
    public static void Run()
    {
        string list = Argument("-prefabs", "Human/Human_Destroyer");
        string outDir = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "PrefabStills");
        Directory.CreateDirectory(outDir);

        GameObject lightGo = new GameObject("Key");
        Light key = lightGo.AddComponent<Light>();
        key.type = LightType.Directional;
        key.intensity = 1.6f;
        lightGo.transform.rotation = Quaternion.Euler(45f, -30f, 0f);
        GameObject fillGo = new GameObject("Fill");
        Light fill = fillGo.AddComponent<Light>();
        fill.type = LightType.Directional;
        fill.intensity = 0.6f;
        fillGo.transform.rotation = Quaternion.Euler(-30f, 150f, 0f);
        RenderSettings.ambientLight = new Color(0.35f, 0.37f, 0.42f);

        GameObject camGo = new GameObject("StillCam");
        Camera cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.12f, 0.13f, 0.16f);
        cam.fieldOfView = 30f;
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 2000f;
        var data = cam.GetUniversalAdditionalCameraData();
        data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        RenderTexture rt = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        cam.targetTexture = rt;
        Texture2D read = new Texture2D(1600, 900, TextureFormat.RGB24, false);

        foreach (string key2 in list.Split(','))
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Prefabs/Osiris/{key2.Trim()}.prefab");
            if (prefab == null) { Debug.LogWarning("missing " + key2); continue; }
            GameObject ship = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            ship.transform.position = Vector3.zero;
            foreach (ParticleSystem ps in ship.GetComponentsInChildren<ParticleSystem>(true)) ps.gameObject.SetActive(false);
            foreach (Canvas c in ship.GetComponentsInChildren<Canvas>(true)) c.gameObject.SetActive(false);

            Bounds b = new Bounds(ship.transform.position, Vector3.zero);
            foreach (Renderer r in ship.GetComponentsInChildren<Renderer>())
            {
                if (r is ParticleSystemRenderer || !r.enabled) continue;
                b.Encapsulate(r.bounds);
            }
            float size = b.extents.magnitude;
            (string name, Vector3 dir)[] views =
            {
                ("top", new Vector3(0.001f, 1f, 0f)), ("side", new Vector3(1f, 0.05f, 0f)), ("front34", new Vector3(0.7f, 0.45f, 0.8f)),
                ("rear34", new Vector3(-0.7f, 0.5f, -0.8f)), ("below", new Vector3(0.3f, -1f, 0.2f)),
            };
            foreach ((string name, Vector3 dir) in views)
            {
                cam.transform.position = b.center + dir.normalized * size * 2.2f;
                cam.transform.LookAt(b.center);
                Shoot(cam, rt, read, Path.Combine(outDir, $"{prefab.name}_{name}.png"));
            }
            // -scarTest: wake the damage components and pour fire into four patches of the hull, each to a
            // different stage (score, blackened, scarred, breached), then photograph them.
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-scarTest") >= 0 && ship.GetComponent<HullDamage>() != null)
            {
                var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
                typeof(HardpointManager).GetMethod("Awake", flags)?.Invoke(ship.GetComponent<HardpointManager>(), null);
                UnitHealthManager uhm = ship.GetComponent<UnitHealthManager>();
                typeof(UnitHealthManager).GetMethod("Start", flags)?.Invoke(uhm, null);
                HullDamage hd = ship.GetComponent<HullDamage>();
                typeof(HullDamage).GetMethod("Awake", flags)?.Invoke(hd, null);
                typeof(HullDamage).GetMethod("Start", flags)?.Invoke(hd, null);
                Physics.SyncTransforms(); // The editor doesn't sync colliders to moved transforms on its own.
                HullScarring sc = ship.AddComponent<HullScarring>();
                typeof(HullScarring).GetMethod("Start", flags)?.Invoke(sc, null);
                int colliders = 0;
                foreach (MeshFilter f in hd.Surfaces) if (f.GetComponent<Collider>() != null) colliders++;
                Debug.Log($"SCARTEST {prefab.name} maxHealth {uhm.maxHealth} surfaces {hd.Surfaces.Count} withColliders {colliders} size {hd.Size}");
                float[] shares = { 0.02f, 0.05f, 0.09f, 0.16f };
                float[] alongShip = { -0.3f, -0.1f, 0.1f, 0.3f };
                var register = typeof(HullScarring).GetMethod("Register", flags);
                Vector3 side = ship.transform.right;
                for (int k = 0; k < 4; k++)
                {
                    Vector3 aim = ship.transform.TransformPoint(hd.LocalBounds.center + Vector3.forward * hd.LocalBounds.size.z * alongShip[k]);
                    int shots = 12;
                    for (int n = 0; n < shots; n++)
                    {
                        Vector3 jitter = Random.insideUnitSphere * hd.Size * 0.01f;
                        Vector3 from = aim + side * hd.Size + jitter + Vector3.up * hd.Size * 0.05f;
                        register.Invoke(sc, new object[] { aim + jitter, (aim - from).normalized, uhm.maxHealth * shares[k] / shots, null });
                    }
                }
                var cellsField = (System.Collections.IDictionary)typeof(HullScarring).GetField("cells", flags).GetValue(sc);
                int markCount = (int)typeof(HullScarring).GetField("marks", flags).GetValue(sc);
                var cols = (System.Collections.IList)typeof(HullScarring).GetField("hullColliders", flags).GetValue(sc);
                Debug.Log($"SCARTEST {prefab.name} cells {cellsField.Count} marks {markCount} hullColliders {cols.Count} children {sc.transform.GetComponentsInChildren<MeshRenderer>().Length}");
                foreach (Collider c in cols) Debug.Log($"SCARTEST   collider {c.name} {c.GetType().Name} enabled {c.enabled} bounds {c.bounds}");
                cam.transform.position = b.center + side * size * 1.6f + Vector3.up * size * 0.25f;
                cam.transform.LookAt(b.center);
                Shoot(cam, rt, read, Path.Combine(outDir, $"{prefab.name}_scars.png"));
                for (int k = 0; k < 4; k++)
                {
                    Vector3 aim = ship.transform.TransformPoint(hd.LocalBounds.center + Vector3.forward * hd.LocalBounds.size.z * alongShip[k]);
                    cam.transform.position = aim + (side * 1f + Vector3.up * 0.35f).normalized * hd.Size * 0.22f;
                    cam.transform.LookAt(aim);
                    Shoot(cam, rt, read, Path.Combine(outDir, $"{prefab.name}_scar{k}.png"));
                }
            }
            // -ortho: straight-down orthographic view of the top deck; 1 pixel = a known distance, logged, so
            // positions can be measured off the picture. Local x runs left-right, local z bottom-to-top (bow up).
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-ortho") >= 0)
            {
                Vector3 lmin = ship.transform.InverseTransformPoint(b.min), lmax = ship.transform.InverseTransformPoint(b.max);
                float halfLength = Mathf.Abs(lmax.z - lmin.z) * 0.5f + 1f;
                Vector3 mid = ship.transform.TransformPoint(new Vector3(0f, Mathf.Max(lmin.y, lmax.y) + 5f, (lmin.z + lmax.z) * 0.5f));
                cam.orthographic = true;
                cam.orthographicSize = halfLength; // Image height (900 px) covers the ship's length.
                cam.transform.position = mid;
                cam.transform.rotation = Quaternion.LookRotation(-ship.transform.up, ship.transform.forward);
                Shoot(cam, rt, read, Path.Combine(outDir, $"{prefab.name}_ortho.png"));
                Debug.Log($"ORTHO {prefab.name} centreZ {(lmin.z + lmax.z) * 0.5f:F2} unitsPerPixel {halfLength * 2f / 900f:F4} topY {Mathf.Max(lmin.y, lmax.y):F2} (pixel x 800 = local x 0, pixel y 450 = centreZ, up = +z)");
                cam.orthographic = false;
            }
            // -spots "0.24,0.6": close top-down looks at these fractions of the ship's length (stern = 0).
            foreach (string spot in Argument("-spots", "").Split(','))
            {
                if (!float.TryParse(spot, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float f)) continue;
                Vector3 local = ship.transform.InverseTransformPoint(b.center);
                Vector3 lmin = ship.transform.InverseTransformPoint(b.min), lmax = ship.transform.InverseTransformPoint(b.max);
                Vector3 at = ship.transform.TransformPoint(new Vector3(local.x, lmax.y, Mathf.Lerp(Mathf.Min(lmin.z, lmax.z), Mathf.Max(lmin.z, lmax.z), f)));
                cam.transform.position = at + new Vector3(0.6f, 1f, -0.5f).normalized * size * 0.35f;
                cam.transform.LookAt(at);
                Shoot(cam, rt, read, Path.Combine(outDir, $"{prefab.name}_spot{f:0.00}.png"));
            }
            // Close-ups of the first few turrets.
            TurretController[] turrets = ship.GetComponentsInChildren<TurretController>(true);
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-turretTest") >= 0)
            {
                // Rig like the game does, then yaw 40 and pitch the barrels 25 degrees up about the trunnion,
                // and mark the firing points in red: they should sit on the visible barrel tips.
                Material red = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                red.color = Color.red;
                foreach (TurretController t in turrets)
                {
                    if (t.turretBase == null || t.turretBarrel == null) continue;
                    TurretVisualRig.Result rig = TurretVisualRig.Rig(t);
                    t.turretBase.localRotation = t.turretBase.localRotation * Quaternion.Euler(0f, 40f, 0f);
                    Quaternion rest = t.turretBarrel.localRotation;
                    Vector3 restPos = t.turretBarrel.localPosition;
                    Quaternion pitch = Quaternion.Euler(-25f, 0f, 0f);
                    t.turretBarrel.localRotation = rest * pitch;
                    if (rig.rigged)
                    {
                        Vector3 arm = rig.trunnion - restPos;
                        t.turretBarrel.localPosition = restPos + arm - pitch * arm;
                    }
                    foreach (Transform fp in t.firingPoints)
                    {
                        if (fp == null) continue;
                        GameObject dot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                        dot.GetComponent<Renderer>().sharedMaterial = red;
                        dot.transform.position = fp.position;
                        dot.transform.localScale = Vector3.one * 0.08f;
                        dot.transform.SetParent(ship.transform, true);
                    }
                }
            }
            for (int i = 0; i < Mathf.Min(3, turrets.Length); i++)
            {
                Transform t = turrets[i].turretBase != null ? turrets[i].turretBase : turrets[i].transform;
                Bounds tb = new Bounds(t.position, Vector3.zero);
                foreach (Renderer r in t.GetComponentsInChildren<Renderer>())
                {
                    if (r is ParticleSystemRenderer || !r.enabled) continue;
                    tb.Encapsulate(r.bounds);
                }
                float ts = Mathf.Max(0.5f, tb.extents.magnitude);
                cam.transform.position = tb.center + (t.right * 0.9f + t.up * 0.7f + t.forward * 0.6f).normalized * ts * 3.2f;
                cam.transform.LookAt(tb.center);
                Shoot(cam, rt, read, Path.Combine(outDir, $"{prefab.name}_turret{i}.png"));
            }
            Object.DestroyImmediate(ship);
        }
        if (Application.isBatchMode) EditorApplication.Exit(0);
    }

    private static void Shoot(Camera cam, RenderTexture rt, Texture2D read, string path)
    {
        cam.Render();
        RenderTexture.active = rt;
        read.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        read.Apply();
        RenderTexture.active = null;
        File.WriteAllBytes(path, read.EncodeToPNG());
    }

    private static string Argument(string name, string fallback)
    {
        string[] args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++) if (args[i] == name) return args[i + 1];
        return fallback;
    }
}
