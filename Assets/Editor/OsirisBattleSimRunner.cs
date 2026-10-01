using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class OsirisBattleSimRunner
{
    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/BattleScene.unity", OpenSceneMode.Single);
        new GameObject("OsirisBattleSim").AddComponent<OsirisBattleSim>();
        Application.runInBackground = true;
        EditorApplication.EnterPlaymode();
    }

    public static void BuildAndRun()
    {
        OsirisPrefabBuilder.BuildAll();
        Run();
    }
}
