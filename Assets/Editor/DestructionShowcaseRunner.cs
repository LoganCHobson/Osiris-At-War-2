using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class DestructionShowcaseRunner
{
    public static void Run()
    {
        DestructionSetup.SetUpShips();
        EditorSceneManager.OpenScene("Assets/Scenes/BattleScene.unity", OpenSceneMode.Single);
        new GameObject("DestructionShowcase").AddComponent<DestructionShowcase>();
        Application.runInBackground = true;
        EditorApplication.EnterPlaymode();
    }

    public static void RunBattle()
    {
        DestructionSetup.SetUpShips();
        CinematicCameraSetup.AddToBattleScene();
        PostProcessingSetup.Apply();
        new GameObject("BattleShowcase").AddComponent<BattleShowcase>();
        Application.runInBackground = true;
        EditorApplication.EnterPlaymode();
    }

    public static void RunShieldBalance()
    {
        DestructionSetup.SetUpShips();
        ShieldSetup.SetUp();
        EditorSceneManager.OpenScene("Assets/Scenes/BattleScene.unity", OpenSceneMode.Single);
        new GameObject("ShieldBalanceSim").AddComponent<ShieldBalanceSim>();
        Application.runInBackground = true;
        EditorApplication.EnterPlaymode();
    }

    public static void RunShieldDemo()
    {
        DestructionSetup.SetUpShips();
        ShieldSetup.SetUp();
        CinematicCameraSetup.AddToBattleScene();
        PostProcessingSetup.Apply();
        new GameObject("ShieldDemo").AddComponent<ShieldDemo>();
        Application.runInBackground = true;
        EditorApplication.EnterPlaymode();
    }

    public static void RunCinematic()
    {
        DestructionSetup.SetUpShips();
        CinematicCameraSetup.AddToBattleScene();
        new GameObject("CinematicShowcase").AddComponent<CinematicShowcase>();
        Application.runInBackground = true;
        EditorApplication.EnterPlaymode();
    }
}
