using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

// Builds the Grand Admiral self-play farm's player: a headless Linux server build (shaders kept), that boots straight
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

        // A dedicated-server build normally strips every shader ("Dedicated Server Optimizations"). The game
        // creates materials in Start() (path lines, weapon effects); without shaders that throws, leaves those
        // components half set up, and battles barely deal damage. So the shaders stay in for self-play.
        // Set by reflection so this still compiles on editors without the setting; restored afterwards.
        System.Reflection.PropertyInfo optimizations = typeof(PlayerSettings).GetProperty("dedicatedServerOptimizations",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        object before = optimizations?.GetValue(null);
        optimizations?.SetValue(null, false);
        // The farm talks to the API inside the cluster over plain HTTP (http://...svc.cluster.local; the
        // client refuses http anywhere else). Unity players block http by default, so allow it in this build
        // only; the game itself keeps the default and uses https.
        InsecureHttpOption http = PlayerSettings.insecureHttpOption;
        PlayerSettings.insecureHttpOption = InsecureHttpOption.AlwaysAllowed;
        BuildReport report;
        try
        {
            report = BuildPipeline.BuildPlayer(options);
        }
        finally
        {
            if (optimizations != null) optimizations.SetValue(null, before);
            PlayerSettings.insecureHttpOption = http;
        }
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
