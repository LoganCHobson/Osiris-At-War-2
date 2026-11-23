using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;

// Dev check: -watchReplay <file> [-captureDir <dir>] opens a saved replay as the main menu's gallery does, plays
// a few moments of it with each camera, takes stills, writes watch_report.txt and quits.
public class ReplayWatchTest : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        string path = Arg("-watchReplay");
        if (path == null || FindFirstObjectByType<ReplayWatchTest>() != null) return;
        GameObject go = new GameObject("ReplayWatchTest");
        DontDestroyOnLoad(go);
        go.AddComponent<ReplayWatchTest>().StartCoroutine(go.GetComponent<ReplayWatchTest>().Run(path));
    }

    private static string Arg(string name)
    {
        string[] args = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    private IEnumerator Run(string path)
    {
        string dir = Arg("-captureDir") ?? Path.GetDirectoryName(path);
        StringBuilder report = new StringBuilder();
        ReplayHeader header = ReplayFile.ReadHeader(path);
        if (header == null) { File.WriteAllText(Path.Combine(dir, "watch_report.txt"), "unreadable header"); Application.Quit(); yield break; }
        report.AppendLine($"header: {header.system} {header.attacker} vs {header.defender} {header.duration:F0}s v{header.gameVersion} {header.bytes / 1048576f:F1} MB");
        float started = Time.realtimeSinceStartup;
        ReplayLaunch.Watch(header);
        while (ReplayPlayer.Instance == null && Time.realtimeSinceStartup - started < 300f) yield return null;
        ReplayPlayer player = ReplayPlayer.Instance;
        if (player == null) { report.AppendLine("no player after 300 s"); File.WriteAllText(Path.Combine(dir, "watch_report.txt"), report.ToString()); Application.Quit(); yield break; }
        report.AppendLine($"loaded in {Time.realtimeSinceStartup - started:F1} s: {player.Data.anchors.Count} anchors, {player.Data.items.Count} items, {player.Data.meshes.Count} hulls ({ReplayFile.LastLoadTimings})");

        float span = player.End - player.Start0;
        foreach ((string label, float at, float speed, ReplayCamera.Mode mode) in new[]
        {
            ("load_early", Mathf.Min(20f, span * 0.2f), 1f, ReplayCamera.Mode.Cinematic),
            ("load_mid", span * 0.6f, 1f, ReplayCamera.Mode.Cinematic),
            ("load_late", span * 0.9f, 2f, ReplayCamera.Mode.Cinematic),
        })
        {
            player.Camera.SetMode(mode);
            player.SetSpeed(speed);
            player.SetPaused(false);
            player.Seek(player.Start0 + at);
            float until = Time.unscaledTime + 5f, worst = 0f;
            int frames = 0;
            while (Time.unscaledTime < until) { yield return null; if (frames++ > 3) worst = Mathf.Max(worst, Time.unscaledDeltaTime * 1000f); }
            int shown = 0;
            foreach (ReplayItem item in player.Data.items) if (item.ghostOn) shown++;
            ScreenCapture.CaptureScreenshot(Path.Combine(dir, $"replay_{label}.png"));
            yield return null;
            report.AppendLine($"{label}: t={player.Now - player.Start0:F1}s worst frame {worst:F0} ms ghosts {shown} shot '{player.Camera.ShotName}'");
        }
        // Free camera with nothing pressed: it must not move, and the battle's own camera must stay off.
        player.Camera.SetMode(ReplayCamera.Mode.Free);
        player.SetPaused(false);
        Transform eye = Camera.main.transform;
        Vector3 from = eye.position;
        float drift = 0f;
        for (float until = Time.unscaledTime + 4f; Time.unscaledTime < until;)
        {
            yield return null;
            drift = Mathf.Max(drift, Vector3.Distance(from, eye.position));
        }
        int rts = 0, cinematic = 0;
        foreach (RTSCameraController c in FindObjectsByType<RTSCameraController>(FindObjectsSortMode.None)) if (c.enabled) rts++;
        foreach (CinematicCamera c in FindObjectsByType<CinematicCamera>(FindObjectsSortMode.None)) if (c.enabled) cinematic++;
        report.AppendLine($"free camera idle 4 s: moved {drift:F3} units; battle camera controllers still on: RTS {rts}, cinematic {cinematic}");
        File.WriteAllText(Path.Combine(dir, "watch_report.txt"), report.ToString());
        Application.Quit();
    }
}
