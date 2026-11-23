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
        // Every ship's recorded scale over the whole battle: where it changes, and to what.
        int scaleChanges = 0;
        foreach (ReplayAnchor a in player.Data.anchors)
        {
            if (a.kind != ReplayKind.Ship) continue;
            PoseTrack p = a.pose;
            float lastS = -1f;
            string history = "";
            for (int k = 0; k < p.Count; k++)
            {
                float s = p.Key(k).s.x;
                if (lastS < 0f || Mathf.Abs(s - lastS) > lastS * 0.02f) { history += $" t={p.times[k] - player.Start0:F2}:{s:F3}"; if (lastS >= 0f) scaleChanges++; lastS = s; }
            }
            if (history.Split(' ').Length > 2) report.AppendLine($"scale {a.name}:{history}");
        }
        foreach (ReplayItem item in player.Data.items)
        {
            if (item.subject != ReplayKind.Ship) continue;
            PoseTrack p = item.pose;
            float lastS = -1f;
            string history = "";
            for (int k = 0; k < p.Count; k++)
            {
                float s = p.Key(k).s.x;
                if (lastS < 0f || Mathf.Abs(s - lastS) > lastS * 0.02f) { history += $" t={p.times[k] - player.Start0:F2}:{s:F3}"; lastS = s; }
            }
            if (history.Split(' ').Length > 2) report.AppendLine($"hull scale {item.label}:{history}");
        }
        report.AppendLine($"ship scale changes: {scaleChanges}");

        // -moments <file>: the live battle's stills' moments and cameras (SiegeRecording), shown from this file.
        string momentsFile = Arg("-moments");
        if (momentsFile != null && File.Exists(momentsFile))
        {
            player.SetPaused(true);
            int i = 0;
            foreach (string line in File.ReadAllLines(momentsFile))
            {
                float[] v = Array.ConvertAll(line.Split(','), x => float.Parse(x, System.Globalization.CultureInfo.InvariantCulture));
                player.Seek(v[0]);
                player.Camera.SetMode(ReplayCamera.Mode.Cinematic);
                Camera.main.transform.SetPositionAndRotation(new Vector3(v[1], v[2], v[3]), new Quaternion(v[4], v[5], v[6], v[7]));
                player.Camera.SetMode(ReplayCamera.Mode.Free);
                Camera.main.fieldOfView = v[8];
                for (int f = 0; f < 6; f++) yield return null;
                ScreenCapture.CaptureScreenshot(Path.Combine(dir, $"pair{i}_file.png"));
                yield return null;
                i++;
            }
            Camera.main.fieldOfView = 60f;
        }

        // Every damaged hull: the model it started as, against what the replay draws for it before and after damage.
        int checkedHulls = 0;
        foreach (ReplayItem item in player.Data.items)
        {
            if (!(item is ReplayMeshItem m) || m.meshes.Count < 2 || checkedHulls >= 12) continue;
            Mesh original = m.meshes.values[0];
            Mesh copy = null;
            float swapAt = 0f;
            for (int k = 0; k < m.meshes.Count; k++) if (m.meshes.values[k] != null && player.Data.meshes.ContainsKey(m.meshes.values[k])) { copy = m.meshes.values[k]; swapAt = m.meshes.times[k]; break; }
            if (copy == null || original == null) continue;
            checkedHulls++;
            ReplayMeshHistory h = player.Data.meshes[copy];
            Mesh before = player.MeshAt(copy, swapAt + 0.001f, out int v0);
            Vector3 b0 = before.bounds.size;
            Mesh after = player.MeshAt(copy, player.End, out int v1);
            Vector3 b1 = after.bounds.size;
            Mesh source = h.Source;
            report.AppendLine($"hull {item.label}: original {original.name} {original.bounds.size} v{original.vertexCount}; source {(source != null ? source.name + " " + source.bounds.size + " v" + source.vertexCount : "none")}; replay first {b0} v{before.vertexCount}; replay last (version {v1}/{h.diffs.Count}) {b1} v{after.vertexCount}");
        }

        // A still camera on one ship around its first hit: stills before and after, with what's drawn of it.
        foreach (ReplayItem item in player.Data.items)
        {
            if (!(item is ReplayMeshItem m) || m.meshes.Count < 2 || item.subject != ReplayKind.Ship) continue;
            float hitAt = -1f;
            for (int k = 0; k < m.meshes.Count; k++) if (m.meshes.values[k] != null && player.Data.meshes.ContainsKey(m.meshes.values[k])) { hitAt = m.meshes.times[k]; break; }
            if (hitAt < 0f) continue;
            player.Camera.SetMode(ReplayCamera.Mode.Free);
            player.SetPaused(true);
            player.Seek(hitAt - 1.5f);
            yield return null;
            yield return null;
            Renderer body = item.ghost != null ? item.ghost.GetComponent<Renderer>() : null;
            if (body == null) continue;
            Vector3 centre = body.bounds.center;
            float size = body.bounds.extents.magnitude;
            Transform eye2 = Camera.main.transform;
            eye2.position = centre + new Vector3(1f, 0.6f, -1f).normalized * size * 3f;
            eye2.LookAt(centre);
            foreach (float dt in new[] { -1.5f, -0.5f, 0.1f, 0.5f, 1.5f, 4f })
            {
                player.Seek(hitAt + dt);
                yield return null;
                yield return null;
                int parts = 0;
                Bounds all = new Bounds(centre, Vector3.zero);
                foreach (ReplayItem p in player.Data.items)
                {
                    if (!p.ghostOn || p.ghostParent != item.ghostParent) continue;
                    Renderer r = p.ghost.GetComponent<Renderer>();
                    if (r is MeshRenderer) { parts++; all.Encapsulate(r.bounds); }
                }
                ScreenCapture.CaptureScreenshot(Path.Combine(dir, $"hit_{dt:+0.0;-0.0}.png"));
                yield return null;
                report.AppendLine($"hit {item.label} {dt:+0.0;-0.0}s: hull drawn {body.bounds.size} mesh {body.GetComponent<MeshFilter>().sharedMesh.name}; ship parts drawn {parts}, all parts span {all.size}; anchor scale {(item.ghostParent != null ? item.ghostParent.ghost.lossyScale.ToString() : "-")}");
            }
            break;
        }

        // Stills through a few cinematic cuts onto ships, every half second, with what each hull mesh is.
        player.Camera.SetMode(ReplayCamera.Mode.Cinematic);
        player.SetSpeed(1f);
        player.Seek(player.Start0 + span * 0.4f);
        player.SetPaused(false);
        for (int cut = 0; cut < 3; cut++)
        {
            player.Camera.NextShot();
            for (int k = 0; k < 16; k++)
            {
                yield return new WaitForSecondsRealtime(0.5f);
                ScreenCapture.CaptureScreenshot(Path.Combine(dir, $"cut{cut}_{k:00}.png"));
                Renderer hull = player.Camera.SubjectRenderer;
                MeshFilter mf = hull != null ? hull.GetComponent<MeshFilter>() : null;
                report.AppendLine($"cut{cut}_{k:00} t={player.Now - player.Start0:F1} shot '{player.Camera.ShotName}' cam {Camera.main.transform.position} hull {(mf != null && mf.sharedMesh != null ? $"{mf.sharedMesh.name} verts {mf.sharedMesh.vertexCount} bounds {mf.sharedMesh.bounds.size}" : "-")} renderer bounds {(hull != null ? hull.bounds.size.ToString() : "-")} lossy {(hull != null ? hull.transform.lossyScale.ToString() : "-")}");
            }
        }

        // Does anything change size while it plays? Every ship's ghost (its anchor and hull), the camera's view.
        player.Seek(player.Start0 + span * 0.5f);
        System.Collections.Generic.Dictionary<Transform, (float min, float max)> sizes = new System.Collections.Generic.Dictionary<Transform, (float, float)>();
        float fovMin = 999f, fovMax = 0f;
        System.Collections.Generic.HashSet<string> cams = new System.Collections.Generic.HashSet<string>();
        for (float until = Time.unscaledTime + 15f; Time.unscaledTime < until;)
        {
            yield return null;
            Camera main = Camera.main;
            if (main != null) { fovMin = Mathf.Min(fovMin, main.fieldOfView); fovMax = Mathf.Max(fovMax, main.fieldOfView); cams.Add(main.name + "#" + main.GetInstanceID()); }
            foreach (ReplayItem item in player.Data.items)
            {
                if (item.subject != ReplayKind.Ship || !item.ghostOn) continue;
                foreach (Transform t in new[] { item.ghostT, item.ghostParent != null ? item.ghostParent.ghost : null })
                {
                    if (t == null) continue;
                    float s = t.lossyScale.x;
                    sizes[t] = sizes.TryGetValue(t, out var mm) ? (Mathf.Min(mm.min, s), Mathf.Max(mm.max, s)) : (s, s);
                }
            }
        }
        int changed = 0;
        foreach (var pair in sizes)
        {
            if (pair.Value.max > pair.Value.min * 1.01f)
            {
                if (changed++ < 8) report.AppendLine($"  size changed: {pair.Key.name} {pair.Value.min:F3} -> {pair.Value.max:F3}");
            }
        }
        report.AppendLine($"15 s of play: {sizes.Count} ship ghosts watched, {changed} changed size; camera fov {fovMin:F1}-{fovMax:F1}; cameras drawing: {string.Join(", ", cams)}");
        int cameraCount = 0;
        foreach (Camera c in FindObjectsByType<Camera>(FindObjectsSortMode.None)) if (c.enabled && c.gameObject.activeInHierarchy) { cameraCount++; report.AppendLine($"  camera on: {c.name} depth {c.depth} fov {c.fieldOfView:F1} target {(c.targetTexture != null ? c.targetTexture.name : "screen")} pos {c.transform.position}"); }

        int rts = 0, cinematic = 0;
        foreach (RTSCameraController c in FindObjectsByType<RTSCameraController>(FindObjectsSortMode.None)) if (c.enabled) rts++;
        foreach (CinematicCamera c in FindObjectsByType<CinematicCamera>(FindObjectsSortMode.None)) if (c.enabled) cinematic++;
        report.AppendLine($"free camera idle 4 s: moved {drift:F3} units; battle camera controllers still on: RTS {rts}, cinematic {cinematic}");
        File.WriteAllText(Path.Combine(dir, "watch_report.txt"), report.ToString());
        Application.Quit();
    }
}
