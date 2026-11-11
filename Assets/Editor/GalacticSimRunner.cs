using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Osiris > Run Galactic Sim: opens the galactic map and lets the AI factions wage war unattended (see
// GalacticSim). Headless: -executeMethod GalacticSimRunner.Run [-galSeconds 1200] [-galSpeed 10] [-galSeed N]
// [-legacy] [-galReport path].
public static class GalacticSimRunner
{
    [MenuItem("Osiris/Run Galactic Sim")]
    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/GalacticMap.unity", OpenSceneMode.Single);

        GalacticSim sim = new GameObject("GalacticSim").AddComponent<GalacticSim>();
        if (float.TryParse(Argument("-galSeconds", ""), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float seconds)) sim.galacticSeconds = seconds;
        if (float.TryParse(Argument("-galSpeed", ""), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float speed)) sim.speed = speed;
        if (int.TryParse(Argument("-galSeed", ""), out int seed)) sim.seed = seed;
        sim.legacy = Array.IndexOf(Environment.GetCommandLineArgs(), "-legacy") >= 0;
        sim.reportPath = Argument("-galReport", Path.Combine(Application.dataPath, "..", "Logs", "GalacticSim.txt"));

        Application.runInBackground = true;
        EditorApplication.EnterPlaymode();
    }

    private static string Argument(string name, string fallback)
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == name) return args[i + 1];
        }
        return fallback;
    }
}
