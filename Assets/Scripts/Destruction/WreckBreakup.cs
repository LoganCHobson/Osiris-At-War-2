using System.Collections.Generic;
using UnityEngine;

// A dying ship coming apart in stages rather than all at once. ShipDestruction slices the hull into pieces
// along its planned seams; this then lets the seams go one by one: the ship breaks its back first (the
// seam nearest the middle), and the rest fail a moment apart. Pieces still joined fly on together as one
// rigid section (the hulk keeps its drift and tumble), and each failing seam blows: a blast at the break,
// plating torn from around it, burning vents on both ends, and the two halves kicked apart and hinged away
// from each other like a snapped keel. A single piece is handed to WreckChunk to drift and burn as before.
public class WreckBreakup : MonoBehaviour
{
    private class Section
    {
        public int first, last;   // pieces, in order along the hull
        public Transform root;
        public Vector3 velocity;
        public Vector3 spin;      // degrees per second, world axis (as WreckChunk)
    }

    private struct Seam
    {
        public Vector3 pointLocal;   // in the section that holds it, set as it's created
        public Vector3 normalLocal;
        public Transform holder;
        public float releaseAt;
        public bool released;
        public List<WreckChunk> plating;
    }

    private DestructionProfile profile;
    private WreckChunk[] pieces;
    private Seam[] seams;
    private readonly List<Section> sections = new List<Section>();
    private float scale, sizeT, size;

    public static WreckBreakup Begin(DestructionProfile profile, WreckChunk[] pieces, Plane[] planes, Vector3[] cutPoints,
        List<WreckChunk>[] plating, Vector3 velocity, Vector3 spin, float scale, float sizeT, float size)
    {
        WreckBreakup breakup = new GameObject("Breakup").AddComponent<WreckBreakup>();
        breakup.Setup(profile, pieces, planes, cutPoints, plating, velocity, spin, scale, sizeT, size);
        return breakup;
    }

    private void Setup(DestructionProfile source, WreckChunk[] chunks, Plane[] planes, Vector3[] cutPoints,
        List<WreckChunk>[] plating, Vector3 velocity, Vector3 spin, float effectScale, float shipSizeT, float shipSize)
    {
        profile = source;
        pieces = chunks;
        scale = effectScale;
        sizeT = shipSizeT;
        size = shipSize;

        Section hulk = NewSection(0, pieces.Length - 1, velocity, spin);
        sections.Add(hulk);

        // The middle seam goes first (the keel breaks), the others after it, a beat apart.
        seams = new Seam[planes.Length];
        int first = 0;
        float middle = (planes.Length - 1) * 0.5f;
        for (int i = 1; i < planes.Length; i++) if (Mathf.Abs(i - middle) < Mathf.Abs(first - middle)) first = i;
        List<int> order = new List<int>();
        for (int i = 0; i < planes.Length; i++) if (i != first) order.Add(i);
        for (int i = order.Count - 1; i > 0; i--) { int j = Random.Range(0, i + 1); (order[i], order[j]) = (order[j], order[i]); }
        order.Insert(0, first);

        float at = Time.time;
        for (int n = 0; n < order.Count; n++)
        {
            int i = order[n];
            if (n > 0) at += Random.Range(profile.seamStagger.x, profile.seamStagger.y) * Mathf.Lerp(0.8f, 1.3f, sizeT);
            seams[i] = new Seam
            {
                holder = hulk.root,
                pointLocal = hulk.root.InverseTransformPoint(cutPoints[i]),
                normalLocal = hulk.root.InverseTransformDirection(planes[i].normal),
                releaseAt = at,
                plating = plating != null && i < plating.Length ? plating[i] : null,
            };
            if (seams[i].plating != null)
            {
                foreach (WreckChunk shard in seams[i].plating) if (shard != null) shard.transform.SetParent(hulk.root, true);
            }
        }
    }

    private Section NewSection(int first, int last, Vector3 velocity, Vector3 spin)
    {
        Bounds bounds = PieceBounds(first, last);
        Transform root = new GameObject("Hulk " + first + "-" + last).transform;
        root.SetParent(transform, false);
        root.SetPositionAndRotation(bounds.center, pieces[first] != null ? pieces[first].transform.rotation : Quaternion.identity);
        for (int k = first; k <= last; k++) if (pieces[k] != null) pieces[k].transform.SetParent(root, true);
        return new Section { first = first, last = last, root = root, velocity = velocity, spin = spin };
    }

    private Bounds PieceBounds(int first, int last)
    {
        bool any = false;
        Bounds bounds = default;
        for (int k = first; k <= last; k++)
        {
            if (pieces[k] == null) continue;
            if (!any) { bounds = new Bounds(pieces[k].transform.position, Vector3.zero); any = true; }
            else bounds.Encapsulate(pieces[k].transform.position);
        }
        return bounds;
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        foreach (Section section in sections)
        {
            if (section.root == null) continue;
            section.root.position += section.velocity * dt;
            float angle = section.spin.magnitude * dt;
            if (angle > 0f) section.root.rotation = Quaternion.AngleAxis(angle, section.spin.normalized) * section.root.rotation;
            section.velocity *= Mathf.Max(0f, 1f - profile.wreckDrag * dt);
        }

        bool pending = false;
        for (int i = 0; i < seams.Length; i++)
        {
            if (seams[i].released) continue;
            if (Time.time >= seams[i].releaseAt) Release(i);
            else pending = true;
        }
        if (!pending && sections.Count == 0) Destroy(gameObject);
    }

    // Velocity of a point riding on a section (its drift plus its tumble).
    private static Vector3 VelocityAt(Section section, Vector3 point) =>
        section.velocity + Vector3.Cross(section.spin * Mathf.Deg2Rad, point - section.root.position);

    private void Release(int seam)
    {
        seams[seam].released = true;
        Section whole = sections.Find(s => s.first <= seam && seam < s.last);
        if (whole == null) return;

        Transform holder = seams[seam].holder != null ? seams[seam].holder : whole.root;
        Vector3 point = holder.TransformPoint(seams[seam].pointLocal);
        Vector3 normal = holder.TransformDirection(seams[seam].normalLocal).normalized;
        bool first = seams.Length == 0 || AllOthersHeld(seam);

        // The break itself: a blast at the seam (the first one, the keel, the biggest), lit, felt and heard.
        float blast = first ? 1f : 0.65f;
        if (first)
        {
            foreach (ParticleSystem template in profile.breakupExplosions)
                DestructionFx.Explode(template, point, profile.breakupExplosionScale * profile.breakupBlastScale * scale * Random.Range(0.85f, 1.15f), profile.explosionLifetime);
        }
        DestructionFx.Explode(profile.mediumExplosion, point, profile.mediumExplosionScale * 1.6f * scale * blast, profile.explosionLifetime);
        DestructionFx.Flash(point, profile.flashColor, profile.flashIntensity * 0.45f * blast * Mathf.Lerp(0.6f, 1.4f, sizeT), size * profile.flashRange * 0.8f, profile.flashDuration * Mathf.Lerp(0.8f, 1.4f, sizeT));
        DestructionFx.Blast(profile.largeBlastClips, point, profile, Mathf.Lerp(1.1f, 0.7f, sizeT) * Random.Range(0.92f, 1.08f));
        CameraShake.Shake(point, profile.breakupShake * blast * Mathf.Lerp(0.4f, 1.1f, sizeT));
        ImpactFlash.Pulse(point, Mathf.Lerp(0.12f, 0.3f, sizeT) * blast);
        if (first) Shockwave.Maybe(point, whole.root.up, size * Mathf.Lerp(0.8f, 1.3f, sizeT), Mathf.Lerp(1.1f, 1.8f, sizeT), sizeT);

        // Plating torn off around the break, flung outward from it.
        if (seams[seam].plating != null)
        {
            foreach (WreckChunk shard in seams[seam].plating)
            {
                if (shard == null) continue;
                Vector3 at = shard.transform.position;
                Vector3 carried = VelocityAt(whole, at);
                shard.transform.SetParent(null, true);
                shard.gameObject.SetActive(true);
                if (Random.value < 0.35f && profile.hullFires != null && profile.hullFires.Length > 0)
                    DestructionFx.Attach(profile.hullFires[Random.Range(0, profile.hullFires.Length)], shard.transform, at, profile.fireScale * scale * 0.4f);
                Vector3 outward = at - point;
                outward -= normal * Vector3.Dot(outward, normal) * 0.6f; // mostly out from the hull, not along it
                Vector3 direction = (outward.normalized + Random.insideUnitSphere * 0.45f).normalized;
                shard.Launch(profile, carried + direction * profile.debrisSpeed * Random.Range(0.9f, 1.8f),
                    Random.onUnitSphere * Random.Range(profile.debrisSpin.x, profile.debrisSpin.y),
                    scale * 0.4f, false, Random.Range(profile.debrisLifetime.x, profile.debrisLifetime.y), false);
            }
        }

        // Split the section at the seam; the two halves part and hinge away from each other.
        sections.Remove(whole);
        int[][] halves = { new[] { whole.first, seam }, new[] { seam + 1, whole.last } };
        float separation = profile.separationSpeed * Random.Range(0.8f, 1.2f) * Mathf.Lerp(1.3f, 0.75f, sizeT);
        float hingeRate = profile.chunkSpin * Mathf.Lerp(1.4f, 0.6f, sizeT) * Random.Range(0.7f, 1.2f);
        Vector3 twist = Random.insideUnitSphere * hingeRate * 0.25f;
        foreach (int[] half in halves)
        {
            Bounds bounds = PieceBounds(half[0], half[1]);
            Vector3 away = bounds.center - point;
            away = Vector3.Dot(away, normal) >= 0f ? normal : -normal; // straight off the break face
            Vector3 linear = VelocityAt(whole, bounds.center) + away * separation + Random.insideUnitSphere * 0.3f;
            // Hinged at the break: each half's far end swings away, as a snapped keel folds.
            Vector3 hinge = Vector3.Cross(away, whole.root.up);
            if (hinge.sqrMagnitude < 0.01f) hinge = Vector3.Cross(away, whole.root.right);
            Vector3 angular = whole.spin + hinge.normalized * hingeRate + twist;

            if (half[0] == half[1])
            {
                WreckChunk piece = pieces[half[0]];
                if (piece == null) continue;
                piece.transform.SetParent(null, true);
                Vent(piece.transform, point, away);
                piece.Launch(profile, linear, angular, scale, profile.persistentWrecks, profile.wreckLifetime * Random.Range(0.8f, 1.2f), true);
            }
            else
            {
                Section part = NewSection(half[0], half[1], linear, angular);
                sections.Add(part);
                WreckChunk end = pieces[half[0] == seam + 1 ? half[0] : half[1]]; // the piece at the break
                if (end != null) Vent(end.transform, point, away);
                // Seams in this half are now held by it.
                for (int i = half[0]; i < half[1]; i++)
                {
                    if (seams[i].released) continue;
                    Vector3 p = seams[i].holder.TransformPoint(seams[i].pointLocal);
                    Vector3 n = seams[i].holder.TransformDirection(seams[i].normalLocal);
                    seams[i].holder = part.root;
                    seams[i].pointLocal = part.root.InverseTransformPoint(p);
                    seams[i].normalLocal = part.root.InverseTransformDirection(n);
                    if (seams[i].plating == null) continue;
                    foreach (WreckChunk shard in seams[i].plating) if (shard != null) shard.transform.SetParent(part.root, true);
                }
            }
        }
        if (whole.root != null) Destroy(whole.root.gameObject);
    }

    private bool AllOthersHeld(int seam)
    {
        for (int i = 0; i < seams.Length; i++) if (i != seam && seams[i].released) return false;
        return true;
    }

    // A burning, venting broken end on a piece that just came off.
    private void Vent(Transform piece, Vector3 point, Vector3 away)
    {
        Vector3 end = point + away * size * 0.02f;
        DestructionFx.Attach(profile.breachVent, piece, end, profile.breachVentScale * scale, -away);
        if (profile.hullFires != null && profile.hullFires.Length > 0)
            DestructionFx.Attach(profile.hullFires[Random.Range(0, profile.hullFires.Length)], piece, end, profile.fireScale * scale * 1.2f);
    }
}
