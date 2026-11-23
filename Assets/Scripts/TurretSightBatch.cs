using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

// Every gun's line-of-sight ray for the frame, cast in one batch of RaycastCommands across the worker threads.
// Guns queue a ray during Update; the batch is scheduled at the end of the frame (it runs while the frame renders)
// and its hits handed back at the start of the next, before the guns update. Each answer is checked against the
// gun's current target, so one about a target it has since dropped is ignored.
[DefaultExecutionOrder(-200)]
public class TurretSightBatch : MonoBehaviour
{
    private const int MaxHits = 8; // Enough to see past the gun's own ship's hardpoints and a shield or two.

    private struct Ask { public TurretController gun; public Transform target; }

    private static TurretSightBatch instance;
    private readonly List<Ask> asking = new List<Ask>();
    private readonly List<RaycastCommand> rays = new List<RaycastCommand>();
    private readonly List<Ask> inFlight = new List<Ask>();
    private NativeArray<RaycastCommand> commands;
    private NativeArray<RaycastHit> results;
    private JobHandle handle;
    private bool scheduled;
    private readonly RaycastHit[] scratch = new RaycastHit[MaxHits];
    private static readonly IComparer<RaycastHit> nearestFirst = Comparer<RaycastHit>.Create((a, b) => a.distance.CompareTo(b.distance));

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset() => instance = null;

    public static void Request(TurretController gun, Vector3 from, Vector3 direction, float range, Transform target)
    {
        if (instance == null) instance = new GameObject("TurretSightBatch").AddComponent<TurretSightBatch>();
        instance.asking.Add(new Ask { gun = gun, target = target });
        instance.rays.Add(new RaycastCommand(from, direction, new QueryParameters(TurretController.SightMask, false, QueryTriggerInteraction.Ignore, false), range));
    }

    // Physics steps before the frame's Update: the casts must be finished before the physics scene changes.
    private void FixedUpdate()
    {
        if (scheduled) handle.Complete();
    }

    // Start of the frame: hand out last frame's answers.
    private void Update()
    {
        if (!scheduled) return;
        handle.Complete();
        scheduled = false;
        for (int i = 0; i < inFlight.Count; i++)
        {
            int count = 0;
            for (int h = 0; h < MaxHits; h++)
            {
                RaycastHit hit = results[i * MaxHits + h];
                if (hit.colliderInstanceID == 0) break;
                scratch[count++] = hit;
            }
            System.Array.Sort(scratch, 0, count, nearestFirst);
            Ask ask = inFlight[i];
            if (ask.gun != null) ask.gun.ApplySight(ask.target, scratch, count);
        }
        inFlight.Clear();
    }

    // End of the frame: cast this frame's rays.
    private void LateUpdate()
    {
        if (scheduled) { handle.Complete(); scheduled = false; inFlight.Clear(); }
        if (rays.Count == 0) return;
        if (!commands.IsCreated || commands.Length < rays.Count)
        {
            if (commands.IsCreated) commands.Dispose();
            if (results.IsCreated) results.Dispose();
            int size = Mathf.NextPowerOfTwo(rays.Count);
            commands = new NativeArray<RaycastCommand>(size, Allocator.Persistent);
            results = new NativeArray<RaycastHit>(size * MaxHits, Allocator.Persistent);
        }
        for (int i = 0; i < rays.Count; i++) commands[i] = rays[i];
        handle = RaycastCommand.ScheduleBatch(commands.GetSubArray(0, rays.Count), results.GetSubArray(0, rays.Count * MaxHits), 16, MaxHits);
        JobHandle.ScheduleBatchedJobs();
        scheduled = true;
        inFlight.AddRange(asking);
        asking.Clear();
        rays.Clear();
    }

    private void OnDestroy()
    {
        if (scheduled) handle.Complete();
        if (commands.IsCreated) commands.Dispose();
        if (results.IsCreated) results.Dispose();
        if (instance == this) instance = null;
    }
}
