using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

// Fills Resources/ReplayAssets with every model, material, texture, sound, destruction profile and effect prefab
// the battle can use (everything the prefabs, Resources and build scenes depend on), so saved replays can name
// them. Runs before every player build; also Osiris > Dev > Build Replay Asset List.
public class ReplayAssetsBuilder : IPreprocessBuildWithReport
{
    private const string AssetPath = "Assets/Resources/ReplayAssets.asset";

    public int callbackOrder => 0;
    public void OnPreprocessBuild(BuildReport report) => Build();

    [MenuItem("Osiris/Dev/Build Replay Asset List")]
    public static void Build()
    {
        List<string> roots = new List<string>();
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs", "Assets/Resources" })) roots.Add(AssetDatabase.GUIDToAssetPath(guid));
        foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets/Resources" })) roots.Add(AssetDatabase.GUIDToAssetPath(guid));
        foreach (string guid in AssetDatabase.FindAssets("t:AudioClip", new[] { "Assets/Resources" })) roots.Add(AssetDatabase.GUIDToAssetPath(guid));
        foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes) if (scene.enabled) roots.Add(scene.path);

        HashSet<string> paths = new HashSet<string>(AssetDatabase.GetDependencies(roots.ToArray(), true));
        List<string> keys = new List<string>();
        List<Object> objects = new List<Object>();
        HashSet<Object> seen = new HashSet<Object>();
        foreach (string path in paths)
        {
            if (path.EndsWith(".cs") || path.EndsWith(".unity") || path.EndsWith(".shader") || path == AssetPath) continue;
            Object[] all = path.EndsWith(".prefab") ? new[] { AssetDatabase.LoadMainAssetAtPath(path) } : AssetDatabase.LoadAllAssetsAtPath(path);
            foreach (Object o in all)
            {
                if (o == null || seen.Contains(o)) continue;
                if (!(o is Mesh || o is Material || o is Texture || o is AudioClip || o is DestructionProfile || (o is GameObject && path.EndsWith(".prefab")))) continue;
                if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(o, out string guid, out long local)) continue;
                seen.Add(o);
                keys.Add(guid + ":" + local);
                objects.Add(o);
            }
        }

        ReplayAssets list = AssetDatabase.LoadAssetAtPath<ReplayAssets>(AssetPath);
        if (list == null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(AssetPath));
            list = ScriptableObject.CreateInstance<ReplayAssets>();
            AssetDatabase.CreateAsset(list, AssetPath);
        }
        list.keys = keys.ToArray();
        list.objects = objects.ToArray();
        EditorUtility.SetDirty(list);
        AssetDatabase.SaveAssets();
        Debug.Log($"ReplayAssetsBuilder: {keys.Count} assets listed.");
    }
}
