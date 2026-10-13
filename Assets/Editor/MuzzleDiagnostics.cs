using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class MuzzleDiagnostics
{
    public static void Report()
    {
        string dir = Argument("-captureDir", Application.temporaryCachePath);
        StringBuilder report = new StringBuilder();

        try
        {
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs/Osiris" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                TurretController[] turrets = prefab.GetComponentsInChildren<TurretController>(true);
                if (turrets.Length == 0) continue;

                report.AppendLine($"== {prefab.name} turrets={turrets.Length}");
                foreach (TurretController turret in turrets.Take(3))
                {
                    report.AppendLine($"  turret '{turret.name}' firingPoints={turret.firingPoints.Count} lossyScale={turret.transform.lossyScale.x:F2}");
                    foreach (Transform point in turret.firingPoints)
                    {
                        if (point != null) report.AppendLine($"    firingPoint '{point.name}' parent='{point.parent?.name}' local={point.localPosition}");
                    }

                    for (int i = 0; i < turret.onFire.GetPersistentEventCount(); i++)
                    {
                        UnityEngine.Object target = turret.onFire.GetPersistentTarget(i);
                        string method = turret.onFire.GetPersistentMethodName(i);
                        Component component = target as Component;
                        if (component == null)
                        {
                            report.AppendLine($"    onFire -> {target?.name}.{method}");
                            continue;
                        }

                        float nearest = turret.firingPoints.Where(p => p != null).Select(p => Vector3.Distance(p.position, component.transform.position)).DefaultIfEmpty(-1f).Min();
                        string parent = component.transform.parent != null ? component.transform.parent.name : "-";
                        string space = component is ParticleSystem ps ? ps.main.simulationSpace.ToString() : "";
                        report.AppendLine($"    onFire -> {component.GetType().Name} '{component.name}' parent='{parent}' {method} distToNearestFiringPoint={nearest:F2} {space}");
                    }

                    foreach (Renderer renderer in turret.GetComponentsInChildren<Renderer>(true).Where(r => !(r is ParticleSystemRenderer)).Take(6))
                    {
                        report.AppendLine($"    renderer '{renderer.name}' active={renderer.gameObject.activeInHierarchy} enabled={renderer.enabled}");
                    }
                }
            }
        }
        catch (Exception e)
        {
            report.AppendLine("EXCEPTION " + e);
        }

        File.WriteAllText(Path.Combine(dir, "muzzle_report.txt"), report.ToString());
        EditorApplication.Exit(0);
    }

    static string Argument(string name, string fallback)
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++) if (args[i] == name) return args[i + 1];
        return fallback;
    }
}
