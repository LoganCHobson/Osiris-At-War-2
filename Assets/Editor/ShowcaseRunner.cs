using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Starts the dev recordings (run with -executeMethod): MissileShowcase and BattleCinematic. Each plays the
// battle scene, writes frames under Logs/, and quits the editor when done.
public static class ShowcaseRunner
{
    public static void RunMissiles() => Run<MissileShowcase>();

    public static void RunBattle() => Run<BattleCinematic>();

    private static void Run<T>() where T : MonoBehaviour
    {
        EditorSceneManager.OpenScene("Assets/Scenes/BattleScene.unity", OpenSceneMode.Single);
        new GameObject(typeof(T).Name).AddComponent<T>();
        Application.runInBackground = true;
        EditorApplication.EnterPlaymode();
    }
}
