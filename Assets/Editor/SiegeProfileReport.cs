using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Profiling;
using UnityEditorInternal;
using UnityEngine;

// Summarises a profiler capture from the siege recording (SiegeRecording -profile): average milliseconds per
// frame for every marker on the main and render threads, by self time and by total time.
// Run: Unity -batchmode -projectPath ... -executeMethod SiegeProfileReport.Analyze -profileFile <raw> -reportFile <txt>
public static class SiegeProfileReport
{
    private class Stat
    {
        public double self, total;
        public int calls;
    }

    public static void Analyze()
    {
        string raw = Argument("-profileFile");
        string output = Argument("-reportFile");
        StringBuilder text = new StringBuilder();

        if (!ProfilerDriver.LoadProfile(raw, false))
        {
            text.AppendLine("could not load " + raw);
        }
        else
        {
            int first = ProfilerDriver.firstFrameIndex, last = ProfilerDriver.lastFrameIndex;
            int frames = Mathf.Max(1, last - first + 1);
            text.AppendLine($"frames {first}..{last} ({frames})");

            Dictionary<string, Dictionary<string, Stat>> byThread = new Dictionary<string, Dictionary<string, Stat>>();
            List<double> frameTimes = new List<double>();

            for (int frame = first; frame <= last; frame++)
            {
                for (int thread = 0; thread < 64; thread++)
                {
                    using (HierarchyFrameDataView view = ProfilerDriver.GetHierarchyFrameDataView(frame, thread, HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName, HierarchyFrameDataView.columnTotalTime, false))
                    {
                        if (view == null || !view.valid) break;
                        string name = view.threadName;
                        if (name != "Main Thread" && name != "Render Thread") continue;
                        if (name == "Main Thread") frameTimes.Add(view.frameTimeMs);
                        if (!byThread.TryGetValue(name, out Dictionary<string, Stat> stats)) byThread[name] = stats = new Dictionary<string, Stat>();
                        Walk(view, view.GetRootItemID(), stats, 0);
                    }
                }
            }

            frameTimes.Sort();
            if (frameTimes.Count > 0)
                text.AppendLine($"main thread frame ms: avg {frameTimes.Average():F1}  median {frameTimes[frameTimes.Count / 2]:F1}  p95 {frameTimes[(int)(frameTimes.Count * 0.95f)]:F1}");

            foreach (KeyValuePair<string, Dictionary<string, Stat>> thread in byThread)
            {
                text.AppendLine();
                text.AppendLine($"== {thread.Key}: top by SELF ms/frame ==");
                foreach (KeyValuePair<string, Stat> entry in thread.Value.OrderByDescending(e => e.Value.self).Take(45))
                    text.AppendLine($"{entry.Value.self / frames,8:F2} self {entry.Value.total / frames,8:F2} total {entry.Value.calls / (double)frames,8:F1} calls  {entry.Key}");
                text.AppendLine($"== {thread.Key}: top by TOTAL ms/frame ==");
                foreach (KeyValuePair<string, Stat> entry in thread.Value.OrderByDescending(e => e.Value.total).Take(45))
                    text.AppendLine($"{entry.Value.total / frames,8:F2} total {entry.Value.self / frames,8:F2} self {entry.Value.calls / (double)frames,8:F1} calls  {entry.Key}");
            }
        }

        File.WriteAllText(output, text.ToString());
        Debug.Log("SIEGE PROFILE\n" + text);
        if (Application.isBatchMode) EditorApplication.Exit(0);
    }

    private static readonly List<int> children = new List<int>();

    private static void Walk(HierarchyFrameDataView view, int item, Dictionary<string, Stat> stats, int depth)
    {
        List<int> kids = new List<int>();
        view.GetItemChildren(item, kids);
        foreach (int child in kids)
        {
            string name = view.GetItemName(child);
            if (!stats.TryGetValue(name, out Stat stat)) stats[name] = stat = new Stat();
            stat.self += view.GetItemColumnDataAsDouble(child, HierarchyFrameDataView.columnSelfTime);
            stat.total += view.GetItemColumnDataAsDouble(child, HierarchyFrameDataView.columnTotalTime);
            stat.calls += (int)view.GetItemColumnDataAsDouble(child, HierarchyFrameDataView.columnCalls);
            if (depth < 40) Walk(view, child, stats, depth + 1);
        }
    }

    private static string Argument(string name)
    {
        string[] args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++) if (args[i] == name) return args[i + 1];
        return "";
    }
}
