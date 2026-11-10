using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

// Builds a standalone player for recording the siege battle with a screen recorder at full speed.
// Run: Unity -batchmode -projectPath ... -executeMethod SiegeRecorderBuild.Build
// Then launch Builds/SiegeRecorder/OsirisSiege.exe -siege (see SiegeRecording).
public static class SiegeRecorderBuild
{
    private const string AssetPath = "Assets/Resources/Dev/SiegeRecordingAssets.asset";

    [MenuItem("Osiris/Build Siege Recorder")]
    public static void Build() => Build(Argument("-buildPath", "Builds/SiegeRecorder/OsirisSiege.exe"), BuildOptions.None);

    private static string Argument(string name, string fallback)
    {
        string[] args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++) if (args[i] == name) return args[i + 1];
        return fallback;
    }

    // A development build for profiling the battle (launch with -siege -profile).
    public static void BuildProfiling() => Build("Builds/SiegeProfile/OsirisSiege.exe", BuildOptions.Development);

    private static void Build(string location, BuildOptions buildOptions)
    {
        SiegeRecordingAssets assets = AssetDatabase.LoadAssetAtPath<SiegeRecordingAssets>(AssetPath);
        if (assets == null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(AssetPath));
            assets = ScriptableObject.CreateInstance<SiegeRecordingAssets>();
            AssetDatabase.CreateAsset(assets, AssetPath);
        }
        assets.titans = AssetDatabase.LoadAssetAtPath<Faction>("Assets/Data/Factions/Faction_TitansOfOsiris.asset");
        assets.avanti = AssetDatabase.LoadAssetAtPath<Faction>("Assets/Data/Factions/Faction_AvantiEmpire.asset");
        assets.ships = new List<GameObject>();
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs/Osiris/Human", "Assets/Prefabs/Osiris/Avanti" }))
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
            if (prefab != null && prefab.GetComponent<Ship>() != null) assets.ships.Add(prefab);
        }
        EditorUtility.SetDirty(assets);
        AssetDatabase.SaveAssets();
        Debug.Log($"SiegeRecorderBuild: {assets.ships.Count} ship prefabs, titans={assets.titans != null}, avanti={assets.avanti != null}");

        BuildPlayerOptions options = new BuildPlayerOptions
        {
            scenes = new[] { "Assets/Scenes/BattleScene.unity", "Assets/Scenes/MainMenu.unity", "Assets/Scenes/GalacticMap.unity" },
            locationPathName = location,
            target = BuildTarget.StandaloneWindows64,
            options = buildOptions,
        };
        BuildReport report = BuildPipeline.BuildPlayer(options);
        Debug.Log($"SiegeRecorderBuild: {report.summary.result} ({report.summary.totalErrors} errors) -> {options.locationPathName}");
        if (Application.isBatchMode) EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
    }
}
