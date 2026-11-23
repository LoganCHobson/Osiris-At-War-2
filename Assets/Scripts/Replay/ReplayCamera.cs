using System.Collections.Generic;
using UnityEngine;

// The replay's camera: free flight anywhere on the field, or a cinematic director cutting between shots of the
// ships and craft as they were at that moment of the battle.
//
// Free: WASD / arrows to fly, Q/E down and up, hold the right mouse button to look, Shift to go fast, the
// scroll wheel sets the flying speed. Cinematic: chase, broadside, bow, orbit, fly-by, craft and overview
// shots, cutting every few seconds (Tab: next shot).
public class ReplayCamera : MonoBehaviour
{
    public enum Mode { Cinematic, Free }

    public Mode CurrentMode { get; private set; } = Mode.Cinematic;

    private enum Shot { Chase, Broadside, LowBow, Orbit, Flyby, Craft, Overview }
    private static readonly Shot[] ShipShots = { Shot.Chase, Shot.Broadside, Shot.LowBow, Shot.Orbit, Shot.Flyby, Shot.Broadside, Shot.Orbit };

    private ReplayPlayer player;
    private Camera view;
    private Transform cam;
    private UnityEngine.Rendering.Volume look;
    private float lookWeight;

    private float yaw, pitch, flySpeed = 80f;

    private ReplayItem subject;
    private Renderer subjectRenderer;
    private Shot shot;
    private float shotEndsAt, lostAt = -1f, side = 1f, orbitAngle;
    private Vector3 fixedPoint, lastCentre, velocity;
    private float lastSize = 10f;
    private bool snap = true;
    private float lastNow;
    private readonly List<ReplayItem> candidates = new List<ReplayItem>();

    public Renderer SubjectRenderer => subjectRenderer;
    public string ShotName => CurrentMode == Mode.Free ? "Free camera" : subject != null && shot != Shot.Overview ? $"{shot} - {subject.label}" : shot.ToString();

    public void Begin(ReplayPlayer owner)
    {
        player = owner;
        view = Camera.main;
        cam = view != null ? view.transform : null;
        CinematicCamera cinematic = FindFirstObjectByType<CinematicCamera>(FindObjectsInactive.Include);
        look = cinematic != null ? cinematic.cinematicVolume : null;
        if (cam != null)
        {
            Vector3 e = cam.rotation.eulerAngles;
            yaw = e.y;
            pitch = e.x > 180f ? e.x - 360f : e.x;
        }
        lastNow = player.Now;
    }

    public void SetMode(Mode mode)
    {
        if (mode == CurrentMode) return;
        CurrentMode = mode;
        if (cam != null)
        {
            Vector3 e = cam.rotation.eulerAngles;
            yaw = e.y;
            pitch = e.x > 180f ? e.x - 360f : e.x;
        }
        if (mode == Mode.Cinematic) { subject = null; snap = true; }
    }

    public void Toggle() => SetMode(CurrentMode == Mode.Free ? Mode.Cinematic : Mode.Free);

    public void NextShot()
    {
        if (CurrentMode != Mode.Cinematic) SetMode(Mode.Cinematic);
        shotEndsAt = float.NegativeInfinity;
    }

    private void LateUpdate()
    {
        if (cam == null || player == null) return;
        float dt = Time.unscaledDeltaTime;
        // A jump in the timeline: a fresh shot, cut straight to.
        if (Mathf.Abs(player.Now - lastNow) > 1.5f || player.Now < lastNow - 0.01f) { subject = null; snap = true; }
        lastNow = player.Now;

        if (CurrentMode == Mode.Free) Fly(dt);
        else Direct(dt);

        float target = CurrentMode == Mode.Cinematic ? 1f : 0f;
        lookWeight = Mathf.MoveTowards(lookWeight, target, dt * 2.5f);
        if (look != null) { look.enabled = lookWeight > 0.001f; look.weight = lookWeight; }
    }

    // ---------------- Free flight ----------------

    private void Fly(float dt)
    {
        if (Input.GetMouseButton(1))
        {
            yaw += Input.GetAxisRaw("Mouse X") * 3f;
            pitch = Mathf.Clamp(pitch - Input.GetAxisRaw("Mouse Y") * 3f, -89f, 89f);
        }
        if (!UIKit.PointerOverScreenUI())
        {
            float scroll = Input.mouseScrollDelta.y;
            if (Mathf.Abs(scroll) > 0.01f) flySpeed = Mathf.Clamp(flySpeed * Mathf.Pow(1.15f, scroll), 5f, 2000f);
        }
        cam.rotation = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 move = Vector3.zero;
        if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) move += Vector3.forward;
        if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) move += Vector3.back;
        if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) move += Vector3.right;
        if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) move += Vector3.left;
        if (Input.GetKey(KeyCode.E)) move += Vector3.up;
        if (Input.GetKey(KeyCode.Q)) move += Vector3.down;
        float speed = flySpeed * (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) ? 4f : 1f);
        cam.position += cam.rotation * move.normalized * speed * dt;
    }

    // ---------------- Director ----------------

    private void Direct(float dt)
    {
        float now = player.Now;
        bool lost = subject == null || !subject.ghostOn;
        if (lost && subject != null && shot != Shot.Overview)
        {
            if (lostAt < 0f) lostAt = now;
            if (now - lostAt > 3f || now < lostAt) subject = null; // Linger on the wreck a moment.
        }
        else lostAt = -1f;

        if (subject == null || now >= shotEndsAt || now < shotEndsAt - 30f) Cut(now);

        Vector3 centre = lastCentre;
        float size = lastSize;
        Vector3 forward = Vector3.forward, up = Vector3.up, right = Vector3.right;
        if (subject != null && subject.ghostOn && subject.ghostT != null)
        {
            Transform frame = subject.ghostParent != null && subject.subject == ReplayKind.Ship ? subject.ghostParent.ghost : subject.ghostT;
            if (subjectRenderer != null && subjectRenderer.enabled) centre = subjectRenderer.bounds.center;
            else centre = subject.ghostT.position;
            size = SizeOf(subject);
            forward = frame.forward; up = frame.up; right = frame.right;
            lastCentre = centre; lastSize = size;
        }

        Vector3 position, target;
        float speed = player.Paused ? 0f : player.Speed;
        switch (shot)
        {
            case Shot.Chase:
                position = centre - forward * size * 2.3f + up * size * 0.65f + right * side * size * 0.3f;
                target = centre + forward * size * 1.4f;
                break;
            case Shot.Broadside:
                position = centre + right * side * size * 2.7f + up * size * 0.3f;
                target = centre;
                break;
            case Shot.LowBow:
                position = centre + forward * size * 1.7f - up * size * 0.5f + right * side * size * 0.6f;
                target = centre - forward * size * 0.2f;
                break;
            case Shot.Orbit:
                orbitAngle += dt * speed * 9f * side;
                position = centre + Quaternion.AngleAxis(orbitAngle, Vector3.up) * (Vector3.forward * size * 2.9f) + Vector3.up * size * 0.55f;
                target = centre;
                break;
            case Shot.Flyby:
                position = fixedPoint;
                target = centre;
                break;
            case Shot.Craft:
                position = centre - forward * size * 9f + up * size * 3f;
                target = centre + forward * size * 6f;
                break;
            default:
                Spread(out Vector3 middle, out float radius);
                position = middle + new Vector3(-0.55f, 0.75f, -0.55f).normalized * radius * 1.7f;
                target = middle;
                break;
        }

        Quaternion rotation = Quaternion.LookRotation((target - position).sqrMagnitude > 1e-4f ? target - position : cam.forward);
        if (snap)
        {
            cam.SetPositionAndRotation(position, rotation);
            velocity = Vector3.zero;
            snap = false;
        }
        else
        {
            cam.position = shot == Shot.Flyby ? position : Vector3.SmoothDamp(cam.position, position, ref velocity, 0.35f, Mathf.Infinity, dt);
            cam.rotation = Quaternion.Slerp(cam.rotation, rotation, 1f - Mathf.Exp(-6f * dt));
        }
    }

    private void Cut(float now)
    {
        side = Random.value < 0.5f ? -1f : 1f;
        orbitAngle = Random.Range(0f, 360f);
        bool keep = subject != null && subject.ghostOn && Random.value < 0.45f;
        if (!keep) subject = Pick();
        subjectRenderer = subject != null && subject.ghost != null ? subject.ghost.GetComponent<Renderer>() : null;
        if (subject == null) shot = Shot.Overview;
        else if (subject.subject == ReplayKind.Craft) shot = Random.value < 0.25f ? Shot.Overview : Shot.Craft;
        else shot = Random.value < 0.1f ? Shot.Overview : ShipShots[Random.Range(0, ShipShots.Length)];
        shotEndsAt = now + Random.Range(4.5f, 8f) * Mathf.Max(0.25f, Mathf.Min(1f, player.Speed));
        lostAt = -1f;
        snap = true;
        velocity = Vector3.zero;
        if (subject != null && subject.ghostT != null)
        {
            Transform frame = subject.ghostParent != null ? subject.ghostParent.ghost : subject.ghostT;
            float size = SizeOf(subject);
            Vector3 c = subjectRenderer != null ? subjectRenderer.bounds.center : subject.ghostT.position;
            fixedPoint = c + frame.forward * size * 4f + frame.right * side * size * 1.3f + frame.up * size * 0.35f;
        }
    }

    // How big the subject is, measured from its hull as drawn now (the size noted when it was recorded can be
    // taken before the ship was set to its final scale).
    private float SizeOf(ReplayItem item)
    {
        Renderer r = item == subject ? subjectRenderer : item.ghost != null ? item.ghost.GetComponent<Renderer>() : null;
        if (r != null && r.enabled && r.gameObject.activeInHierarchy) return Mathf.Max(0.5f, r.bounds.extents.magnitude);
        return Mathf.Max(1f, item.subjectSize);
    }

    // Something worth watching: ships mostly (bigger ones more often), sometimes a craft.
    private ReplayItem Pick()
    {
        candidates.Clear();
        float total = 0f;
        bool craft = Random.value < 0.3f;
        foreach (ReplayItem item in player.Data.items)
        {
            if (item.subject == ReplayKind.Other || !item.ghostOn) continue;
            if ((item.subject == ReplayKind.Craft) != craft) continue;
            candidates.Add(item);
            total += Mathf.Sqrt(Mathf.Max(1f, item.subjectSize));
        }
        if (candidates.Count == 0 && craft)
        {
            foreach (ReplayItem item in player.Data.items)
                if (item.subject == ReplayKind.Ship && item.ghostOn) { candidates.Add(item); total += Mathf.Sqrt(Mathf.Max(1f, item.subjectSize)); }
        }
        if (candidates.Count == 0) return null;
        float pick = Random.value * total;
        foreach (ReplayItem item in candidates)
        {
            pick -= Mathf.Sqrt(Mathf.Max(1f, item.subjectSize));
            if (pick <= 0f) return item;
        }
        return candidates[candidates.Count - 1];
    }

    private void Spread(out Vector3 centre, out float radius)
    {
        centre = Vector3.zero;
        int n = 0;
        foreach (ReplayAnchor a in player.Data.anchors)
        {
            if (a.kind != ReplayKind.Ship || a.shown == 0 || a.ghost == null) continue;
            centre += a.ghost.position;
            n++;
        }
        if (n == 0) { centre = lastCentre; radius = 200f; return; }
        centre /= n;
        radius = 60f;
        foreach (ReplayAnchor a in player.Data.anchors)
        {
            if (a.kind != ReplayKind.Ship || a.shown == 0 || a.ghost == null) continue;
            radius = Mathf.Max(radius, Vector3.Distance(centre, a.ghost.position) + a.size);
        }
    }
}
