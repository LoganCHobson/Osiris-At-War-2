using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

// Dev: what instantiating each warship prefab costs, and how much of it each kind of component accounts for
// (batchmode: -executeMethod SpawnCostReport.Run). Mid-battle reinforcements were 400+ ms freezes.
public static class SpawnCostReport
{
    public static void Run()
    {
        StringBuilder sb = new StringBuilder();
        string[] names = { "Human/Human_Battleship", "Human/Human_Cruiser", "Human/Human_Destroyer", "Human/Human_Carrier", "Avanti/Avanti_Capital", "Avanti/Avanti_Cruiser", "Avanti/Avanti_Destroyer" };
        foreach (string name in Application.isBatchMode && System.Environment.GetCommandLineArgs().Contains("-namesOnly") ? new string[0] : names)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Prefabs/Osiris/{name}.prefab");
            if (prefab == null) continue;
            Object.DestroyImmediate(Object.Instantiate(prefab)); // warm up
            float full = Time(prefab);
            sb.AppendLine($"{name}: {full:F0} ms   renderers {prefab.GetComponentsInChildren<Renderer>(true).Length}  meshColliders {prefab.GetComponentsInChildren<MeshCollider>(true).Length} (convex {prefab.GetComponentsInChildren<MeshCollider>(true).Count(c => c.convex)})  particleSystems {prefab.GetComponentsInChildren<ParticleSystem>(true).Length}  lights {prefab.GetComponentsInChildren<Light>(true).Length}  animators {prefab.GetComponentsInChildren<Animator>(true).Length}  audio {prefab.GetComponentsInChildren<AudioSource>(true).Length}  transforms {prefab.GetComponentsInChildren<Transform>(true).Length}");
            foreach (System.Type strip in new[] { typeof(MeshCollider), typeof(ParticleSystem), typeof(Light), typeof(Animator), typeof(AudioSource), typeof(LODGroup), typeof(Canvas) })
            {
                GameObject copy = Object.Instantiate(prefab);
                copy.SetActive(false);
                if (strip == typeof(ParticleSystem))
                {
                    foreach (ParticleSystemRenderer r in copy.GetComponentsInChildren<ParticleSystemRenderer>(true)) Object.DestroyImmediate(r);
                }
                foreach (Component c in copy.GetComponentsInChildren(strip, true)) Object.DestroyImmediate(c);
                float without = Time(copy, true);
                Object.DestroyImmediate(copy);
                sb.AppendLine($"     without {strip.Name}: {without:F0} ms  (saves {full - without:F0})");
            }
        }
        GameObject capital = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Osiris/Avanti/Avanti_Capital.prefab");
        sb.AppendLine("Avanti_Capital particle systems by name (parent/name):");
        foreach (var g in capital.GetComponentsInChildren<ParticleSystem>(true).GroupBy(p => (p.transform.parent != null ? p.transform.parent.name : "-") + "/" + p.name).OrderByDescending(g => g.Count()).Take(25))
            sb.AppendLine($"   {g.Count(),5}  {g.Key}");
        File.WriteAllText(Path.Combine("Logs", "spawn_cost.txt"), sb.ToString());
        if (Application.isBatchMode) EditorApplication.Exit(0);
    }

    private static float Time(GameObject source, bool activate = false)
    {
        Stopwatch clock = Stopwatch.StartNew();
        const int runs = 3;
        for (int i = 0; i < runs; i++)
        {
            GameObject go = Object.Instantiate(source);
            if (activate) go.SetActive(true);
            Object.DestroyImmediate(go);
        }
        return (float)clock.Elapsed.TotalMilliseconds / runs;
    }
}
