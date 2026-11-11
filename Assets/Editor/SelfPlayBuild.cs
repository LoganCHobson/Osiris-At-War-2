using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

// Builds the Grand Admiral self-play farm's player: a headless Linux dedicated server that boots straight
// into the battle scene and runs DifficultySim (Grand Admiral, always Avanti, vs Admiral). A built player
// needs no Unity license to run, so the cluster can run as many copies as it has room for.
//   Unity -batchmode -quit -projectPath <clone> -executeMethod SelfPlayBuild.Build [-buildPath Builds/SelfPlay/osiris-selfplay]
// Then run: osiris-selfplay -batchmode -nographics -selfPlay -selfPlayRounds 10 -gaServer <url> -gaCredentials <file>
// GrandAdmiral/tools/build-selfplay.ps1 does the build in its own clone and ships it to the cluster.
public static class SelfPlayBuild
{
    [MenuItem("Osiris/Build Self-Play Server (Linux)")]
    public static void Build()
    {
        SiegeRecorderBuild.PrepareAssets(); // The factions, from Resources, since a player has no AssetDatabase.

        BuildPlayerOptions options = new BuildPlayerOptions
        {
            scenes = new[] { "Assets/Scenes/BattleScene.unity" },
            locationPathName = Argument("-buildPath", "Builds/SelfPlay/osiris-selfplay"),
            target = BuildTarget.StandaloneLinux64,
            subtarget = (int)StandaloneBuildSubtarget.Server,
            options = BuildOptions.None,
        };
        BuildReport report = BuildPipeline.BuildPlayer(options);
        Debug.Log($"SelfPlayBuild: {report.summary.result} ({report.summary.totalErrors} errors) -> {options.locationPathName}");
        if (Application.isBatchMode) EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
    }

    private static string Argument(string name, string fallback)
    {
        string[] args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++) if (args[i] == name) return args[i + 1];
        return fallback;
    }
}
