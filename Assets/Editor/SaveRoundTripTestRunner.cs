using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Osiris > Test Save Round Trip: plays the galactic map and checks that saving and loading restores it
// (see SaveRoundTripTest).
public static class SaveRoundTripTestRunner
{
    [MenuItem("Osiris/Test Save Round Trip")]
    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/GalacticMap.unity", OpenSceneMode.Single);
        SaveRoundTripTest test = new GameObject("SaveRoundTripTest").AddComponent<SaveRoundTripTest>();
        test.reportPath = Argument("-testReport", Path.Combine(Application.dataPath, "..", "Logs", "SaveRoundTrip.txt"));
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
