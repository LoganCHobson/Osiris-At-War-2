using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Runs the balance suite (see BalanceSim) with -executeMethod. BuildAndRun rebuilds the ship prefabs from
// OsirisPrefabBuilder's numbers first (plus the destruction and shield setup), so tuning changes are tested.
public static class BalanceSimRunner
{
    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/BattleScene.unity", OpenSceneMode.Single);
        new GameObject("BalanceSim").AddComponent<BalanceSim>();
        Application.runInBackground = true;
        EditorApplication.EnterPlaymode();
    }

    public static void BuildAndRun()
    {
        Rebuild();
        Run();
    }

    // Just the rebuild, to compare the regenerated prefabs against the current ones.
    public static void Rebuild()
    {
        OsirisPrefabBuilder.BuildAll();
        DestructionSetup.SetUpShips();
        ShieldSetup.SetUp();
        AssetDatabase.SaveAssets();
    }

    public static void RebuildAndQuit()
    {
        Rebuild();
        EditorApplication.Exit(0);
    }
}
