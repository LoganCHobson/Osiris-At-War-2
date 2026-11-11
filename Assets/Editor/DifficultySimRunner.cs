using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Osiris > Run Difficulty Sim: opens the battle scene and plays AI-vs-AI battles between difficulty
// levels (see DifficultySim). Also runnable headless with -executeMethod DifficultySimRunner.Run.
public static class DifficultySimRunner
{
    [MenuItem("Osiris/Run Difficulty Sim")]
    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/BattleScene.unity", OpenSceneMode.Single);

        DifficultySim sim = new GameObject("DifficultySim").AddComponent<DifficultySim>();
        sim.factionA = AssetDatabase.LoadAssetAtPath<Faction>(Argument("-factionA", "Assets/Data/Factions/Faction_TitansOfOsiris.asset"));
        sim.factionB = AssetDatabase.LoadAssetAtPath<Faction>(Argument("-factionB", "Assets/Data/Factions/Faction_AvantiEmpire.asset"));
        sim.reportPath = Path.Combine(Application.dataPath, "..", "Logs", "DifficultySim.txt");
        sim.Configure(); // -simSpeed, -simLimit, -selfPlay, -grandAdmiralOnly, -selfPlayRounds, -simReport

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
