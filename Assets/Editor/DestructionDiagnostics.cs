using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class DestructionDiagnostics
{
    static readonly string[] Ships = { "Human/Human_Cruiser", "Avanti/Avanti_Capital", "Human/Human_Battleship", "Avanti/Avanti_Destroyer" };

    public static void RenderCuts()
    {
        string dir = Argument("-captureDir", Path.Combine(Application.temporaryCachePath, "DestructionDiagnostics"));
        Directory.CreateDirectory(dir);
        StringBuilder report = new StringBuilder();

        try
        {
            DestructionSetup.SetUpShips();
            EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            DestructionProfile profile = AssetDatabase.LoadAssetAtPath<DestructionProfile>("Assets/Data/Destruction/DestructionProfile.asset");
            Camera cam = Camera.main;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.12f, 0.13f, 0.16f);
            cam.farClipPlane = 5000f;
            RenderTexture rt = new RenderTexture(800, 500, 24);
            cam.targetTexture = rt;

            foreach (string key in Ships)
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Prefabs/Osiris/{key}.prefab");
                GameObject ship = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                ship.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                Transform gfx = ship.transform.Find("GFX");
                LODGroup group = gfx.GetComponentInChildren<LODGroup>(true);
                List<MeshRenderer> renderers = new List<MeshRenderer>();
                foreach (Renderer r in group.GetLODs()[0].renderers) if (r is MeshRenderer mr) renderers.Add(mr);

                Bounds bounds = renderers[0].bounds;
                float largest = 0f;
                foreach (MeshRenderer r in renderers)
                {
                    bounds.Encapsulate(r.bounds);
                    largest = Mathf.Max(largest, r.bounds.size.magnitude);
                }

                Vector3 axis = bounds.size.z >= bounds.size.x ? Vector3.forward : Vector3.right;
                float length = Vector3.Scale(bounds.size, axis).magnitude;
                Vector3 tiltAxis = (Vector3.up + Vector3.Cross(Vector3.up, axis)).normalized;
                Plane plane = new Plane(Quaternion.AngleAxis(22f, tiltAxis) * axis, bounds.center);
                report.AppendLine($"== {key} size={bounds.size} axis={axis}");

                GameObject backRoot = new GameObject("Back");
                GameObject frontRoot = new GameObject("Front");

                foreach (MeshRenderer renderer in renderers)
                {
                    MeshFilter filter = renderer.GetComponent<MeshFilter>();
                    Mesh mesh = filter.sharedMesh;
                    bool light = HullDamage.IsLight(renderer);
                    bool large = renderer.bounds.size.magnitude >= largest * profile.largePartFraction;
                    report.AppendLine($"  {renderer.name}: readable={mesh.isReadable} verts={mesh.vertexCount} sub={mesh.subMeshCount} mats={renderer.sharedMaterials.Length} extent={renderer.bounds.size.magnitude:F1} large={large} light={light} scale={renderer.transform.lossyScale}");
                    if (light) continue;

                    MeshSlicer.MeshData data = MeshSlicer.MeshData.Read(mesh);
                    if (data == null) continue;

                    Matrix4x4 toWorld = renderer.transform.localToWorldMatrix;
                    Matrix4x4 toLocal = renderer.transform.worldToLocalMatrix;
                    Vector3 n = toWorld.transpose.MultiplyVector(plane.normal).normalized;
                    Vector3 p = toLocal.MultiplyPoint3x4(plane.normal * -plane.distance);
                    MeshSlicer.Result[] results = MeshSlicer.Slice(data, new[] { new Plane(n, p) }, true, mesh.name, toLocal.MultiplyVector(Vector3.up));

                    for (int k = 0; k < 2; k++)
                    {
                        if (results[k].mesh == null) continue;
                        report.AppendLine($"     piece{k}: capped={results[k].capped} sub={results[k].mesh.subMeshCount} capTris={(results[k].capped ? results[k].mesh.GetTriangles(results[k].mesh.subMeshCount - 1).Length / 3 : 0)}");
                        GameObject piece = new GameObject(renderer.name + k);
                        piece.transform.SetPositionAndRotation(renderer.transform.position, renderer.transform.rotation);
                        piece.transform.localScale = renderer.transform.lossyScale;
                        piece.transform.SetParent(k == 0 ? backRoot.transform : frontRoot.transform, true);
                        piece.AddComponent<MeshFilter>().sharedMesh = results[k].mesh;
                        MeshRenderer pr = piece.AddComponent<MeshRenderer>();
                        Material[] mats = new Material[results[k].mesh.subMeshCount];
                        for (int m = 0; m < mats.Length; m++)
                        {
                            mats[m] = results[k].capped && m == mats.Length - 1 ? profile.interiorMaterial : renderer.sharedMaterials[Mathf.Min(m, renderer.sharedMaterials.Length - 1)];
                        }
                        pr.sharedMaterials = mats;
                    }
                }

                ship.SetActive(false);
                frontRoot.transform.position += axis * length * 0.35f;
                Vector3 cut = bounds.center;
                Vector3 side = Vector3.Cross(Vector3.up, axis);
                string name = Path.GetFileName(key);

                Shoot(cam, rt, cut + axis * length * 0.3f + side * length * 0.12f + Vector3.up * length * 0.12f, cut, Path.Combine(dir, $"{name}_backcap.png"));
                Shoot(cam, rt, cut + axis * length * 0.05f - side * length * 0.12f + Vector3.up * length * 0.12f, cut + axis * length * 0.35f, Path.Combine(dir, $"{name}_frontcap.png"));
                Shoot(cam, rt, cut + side * length * 0.9f + Vector3.up * length * 0.5f + axis * length * 0.2f, cut + axis * length * 0.17f, Path.Combine(dir, $"{name}_overview.png"));

                UnityEngine.Object.DestroyImmediate(backRoot);
                UnityEngine.Object.DestroyImmediate(frontRoot);
                UnityEngine.Object.DestroyImmediate(ship);
            }
        }
        catch (Exception e)
        {
            report.AppendLine("EXCEPTION " + e);
        }

        File.WriteAllText(Path.Combine(dir, "report.txt"), report.ToString());
        EditorApplication.Exit(0);
    }

    static void Shoot(Camera cam, RenderTexture rt, Vector3 from, Vector3 at, string path)
    {
        cam.transform.position = from;
        cam.transform.LookAt(at);
        cam.Render();
        RenderTexture.active = rt;
        Texture2D tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        tex.Apply();
        RenderTexture.active = null;
        File.WriteAllBytes(path, tex.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(tex);
    }

    static string Argument(string name, string fallback)
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++) if (args[i] == name) return args[i + 1];
        return fallback;
    }
}
