using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

// One shared queue for the expensive visual side of battle damage (hull dents and holes, scorch-mark decals,
// reshaping decals over bent plating). In a big fight dozens of hits land in the same frame, and doing each
// one's mesh work on the spot stacked up into frames of 200-750 ms. Work goes in here instead and runs in
// arrival order within a fixed time budget each frame (always at least one job), so a burst of hits spreads
// over a few frames - a dent may appear a frame or two after its hit - and the frame rate holds.
public static class DeferredWork
{
    // Milliseconds of queued work per frame, across all ships.
    public const float BudgetMs = 3f;

    private static readonly Queue<(UnityEngine.Object owner, Action job)> queue = new Queue<(UnityEngine.Object, Action)>();
    private static Runner runner;

    public static int Pending => queue.Count;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset()
    {
        queue.Clear();
        runner = null;
    }

    // `owner` gone (its ship destroyed) by the time the job's turn comes: the job is dropped.
    public static void Enqueue(UnityEngine.Object owner, Action job)
    {
        if (job == null) return;
        if (runner == null)
        {
            GameObject host = new GameObject("DeferredWork");
            runner = host.AddComponent<Runner>();
        }
        queue.Enqueue((owner, job));
    }

    private class Runner : MonoBehaviour
    {
        private readonly Stopwatch clock = new Stopwatch();

        private void Update()
        {
            if (queue.Count == 0) return;
            clock.Restart();
            int waiting = queue.Count; // Jobs queued during this frame (a job putting itself back) wait for the next.
            do
            {
                waiting--;
                (UnityEngine.Object owner, Action job) = queue.Dequeue();
                if (owner == null) continue;
                try { job(); }
                catch (Exception e) { UnityEngine.Debug.LogException(e); }
            }
            while (waiting > 0 && queue.Count > 0 && clock.Elapsed.TotalMilliseconds < BudgetMs);
        }

        private void OnDestroy()
        {
            if (runner == this) { runner = null; queue.Clear(); }
        }
    }
}
