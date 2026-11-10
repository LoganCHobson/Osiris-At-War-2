using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Events;

// Dev check: what each kind of hardpoint does when it dies or takes damage (persistent UnityEvent calls).
public static class HardpointEventsReport
{
    public static void Run()
    {
        StringBuilder text = new StringBuilder();
        foreach (string name in new[] { "Human/Human_Destroyer", "Avanti/Avanti_Destroyer", "Human/Human_Carrier" })
        {
            GameObject ship = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Prefabs/Osiris/{name}.prefab"));
            text.AppendLine("=== " + name);
            foreach (var g in ship.GetComponentsInChildren<HardpointHealth>(true).GroupBy(h => h.name.Split('_')[0]))
            {
                HardpointHealth h = g.First();
                text.AppendLine($"  {h.name} ({g.Count()}x) maxHealth {h.maxHealth}");
                Dump("die", h.die, text);
                Dump("dealDamage", h.dealDamage, text);
                foreach (ParticleSystem ps in h.GetComponentsInChildren<ParticleSystem>(true))
                {
                    if (ps.transform.parent != h.transform) continue;
                    var main = ps.main;
                    text.AppendLine($"     fx {ps.name} playOnAwake {main.playOnAwake} loop {main.loop} space {main.simulationSpace} lpos {ps.transform.localPosition:F2} lrot {ps.transform.localEulerAngles:F0} scale {ps.transform.lossyScale.x:F2}");
                }
            }
            Object.DestroyImmediate(ship);
        }
        File.WriteAllText(System.IO.Path.Combine(Directory.GetCurrentDirectory(), "Logs", "hardpoint_events.txt"), text.ToString());
        if (Application.isBatchMode) EditorApplication.Exit(0);
    }

    private static void Dump(string label, UnityEvent e, StringBuilder text)
    {
        if (e == null) return;
        for (int i = 0; i < e.GetPersistentEventCount(); i++)
        {
            Object target = e.GetPersistentTarget(i);
            text.AppendLine($"     {label} -> {(target != null ? target.name + " (" + target.GetType().Name + ")" : "null")}.{e.GetPersistentMethodName(i)}");
        }
    }
}
