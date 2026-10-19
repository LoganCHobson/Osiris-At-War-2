using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Osiris > Run UI Tour: screenshots the menus in play mode (see UITour).
public static class UITourRunner
{
    [MenuItem("Osiris/Run UI Tour")]
    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/MainMenu.unity", OpenSceneMode.Single);
        UITour tour = new GameObject("UITour").AddComponent<UITour>();
        tour.outputDir = Argument("-tourDir", Path.Combine(Application.dataPath, "..", "Logs", "UITour"));
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
