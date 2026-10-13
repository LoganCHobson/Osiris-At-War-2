using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class CinematicCameraSetup
{
    private const string ScenePath = "Assets/Scenes/BattleScene.unity";

    [MenuItem("Osiris/Add Cinematic Camera To Battle Scene")]
    public static void AddToBattleScene()
    {
        UnityEngine.SceneManagement.Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        CinematicCamera cinematic = Object.FindFirstObjectByType<CinematicCamera>(FindObjectsInactive.Include);
        if (cinematic == null) cinematic = new GameObject("CinematicCamera").AddComponent<CinematicCamera>();

        PlayerSpaceManager spaceManager = Object.FindFirstObjectByType<PlayerSpaceManager>(FindObjectsInactive.Include);
        List<Behaviour> controls = new List<Behaviour>();
        controls.AddRange(Object.FindObjectsByType<RTSCameraController>(FindObjectsInactive.Include, FindObjectsSortMode.None));
        controls.AddRange(Object.FindObjectsByType<PlayerSpaceManager>(FindObjectsInactive.Include, FindObjectsSortMode.None));
        controls.AddRange(Object.FindObjectsByType<UnitSelectionBox>(FindObjectsInactive.Include, FindObjectsSortMode.None));

        cinematic.viewCamera = Camera.main;
        cinematic.spaceManager = spaceManager;
        cinematic.disableWhileActive = controls.ToArray();

        List<Renderer> scenery = new List<Renderer>();
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == "Ground") scenery.AddRange(child.GetComponentsInChildren<Renderer>(true));
            }
        }
        cinematic.hideWhileActive = scenery.ToArray();
        EditorUtility.SetDirty(cinematic);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"Cinematic camera wired in {ScenePath}: camera={cinematic.viewCamera?.name}, controls={controls.Count}, hidden scenery={scenery.Count}");
    }

    public static void AddToBattleSceneAndQuit()
    {
        AddToBattleScene();
        EditorApplication.Exit(0);
    }
}
