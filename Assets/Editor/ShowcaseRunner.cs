using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Starts the dev recordings (run with -executeMethod): MissileShowcase, BattleCinematic and BuildMenuTour.
// Each writes its frames or screenshots under Logs/ and quits the editor when done.
public static class ShowcaseRunner
{
    public static void RunMissiles() => Run<MissileShowcase>();

    public static void RunBattle() => Run<BattleCinematic>();

    // Starts from the main menu so the galactic map loads the way it does in a real game.
    public static void RunBuildMenuTour()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/MainMenu.unity", OpenSceneMode.Single);
        new GameObject("BuildMenuTour").AddComponent<BuildMenuTour>();
        Application.runInBackground = true;
        EditorApplication.EnterPlaymode();
    }

    private static void Run<T>() where T : MonoBehaviour
    {
        EditorSceneManager.OpenScene("Assets/Scenes/BattleScene.unity", OpenSceneMode.Single);
        new GameObject(typeof(T).Name).AddComponent<T>();
        Application.runInBackground = true;
        EditorApplication.EnterPlaymode();
    }
}
