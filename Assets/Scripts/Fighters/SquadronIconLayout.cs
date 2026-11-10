using System.Collections.Generic;
using UnityEngine;

// EAW-style clumping for strike-craft icons: when several squadrons on the same side are bunched up on
// screen, their icons snap into a tidy grid around the middle of the bunch instead of piling on top of one
// another - you can see at a glance that a dozen squadrons are fighting there, and click any one of them.
// Runs after every SquadronIcon has placed itself (they set their home position in LateUpdate).
[DefaultExecutionOrder(1000)]
public class SquadronIconLayout : MonoBehaviour
{
    // Icons closer than this share of their size (on screen) join the same clump.
    private const float JoinDistance = 0.95f;
    // Grid cell size as a share of the icon size.
    private const float CellSize = 1.08f;

    private static readonly List<SquadronIcon> icons = new List<SquadronIcon>();
    private static SquadronIconLayout instance;

    private readonly List<Vector3> screen = new List<Vector3>();
    private readonly List<int> cluster = new List<int>();
    private readonly List<int> members = new List<int>();
    private readonly Queue<int> frontier = new Queue<int>();

    public static void Register(SquadronIcon icon)
    {
        if (icon == null || icons.Contains(icon)) return;
        icons.Add(icon);

        if (instance == null)
        {
            instance = new GameObject("SquadronIconLayout").AddComponent<SquadronIconLayout>();
        }
    }

    public static void Unregister(SquadronIcon icon)
    {
        icons.Remove(icon);
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    private void LateUpdate()
    {
        icons.RemoveAll(i => i == null);
        if (icons.Count < 2) return;

        Camera cam = Camera.main;
        if (cam == null) return;

        // Stable order so icons don't swap grid cells from frame to frame.
        icons.Sort((a, b) => a.GetInstanceID().CompareTo(b.GetInstanceID()));

        screen.Clear();
        cluster.Clear();
        foreach (SquadronIcon icon in icons)
        {
            screen.Add(cam.WorldToScreenPoint(icon.HomePosition));
            cluster.Add(-1);
        }

        for (int seed = 0; seed < icons.Count; seed++)
        {
            if (cluster[seed] >= 0 || screen[seed].z <= 0f) continue;

            // Gather everything chained to this icon (single linkage), same side only.
            members.Clear();
            frontier.Clear();
            cluster[seed] = seed;
            frontier.Enqueue(seed);
            while (frontier.Count > 0)
            {
                int current = frontier.Dequeue();
                members.Add(current);
                float join = icons[current].PixelSize * JoinDistance;

                for (int other = 0; other < icons.Count; other++)
                {
                    if (cluster[other] >= 0 || screen[other].z <= 0f) continue;
                    if (icons[other].Friendly != icons[seed].Friendly) continue;
                    if (((Vector2)screen[other] - (Vector2)screen[current]).sqrMagnitude > join * join) continue;

                    cluster[other] = seed;
                    frontier.Enqueue(other);
                }
            }

            if (members.Count > 1) Arrange(cam);
        }
    }

    // Lays the current clump out as a grid centred on its average screen position, grouped by craft type.
    private void Arrange(Camera cam)
    {
        Vector3 centre = Vector3.zero;
        float cell = 0f;
        foreach (int i in members)
        {
            centre += screen[i];
            cell = Mathf.Max(cell, icons[i].PixelSize * CellSize);
        }
        centre /= members.Count;

        members.Sort((a, b) =>
        {
            int byRole = icons[a].Role.CompareTo(icons[b].Role);
            return byRole != 0 ? byRole : a.CompareTo(b);
        });

        int columns = Mathf.CeilToInt(Mathf.Sqrt(members.Count));
        int rows = Mathf.CeilToInt(members.Count / (float)columns);

        for (int n = 0; n < members.Count; n++)
        {
            int row = n / columns;
            int column = n % columns;
            int inRow = Mathf.Min(columns, members.Count - row * columns);

            float x = centre.x + (column - (inRow - 1) * 0.5f) * cell;
            float y = centre.y + ((rows - 1) * 0.5f - row) * cell;
            int i = members[n];
            icons[i].PlaceAt(cam.ScreenToWorldPoint(new Vector3(x, y, screen[i].z)));
        }
    }
}
