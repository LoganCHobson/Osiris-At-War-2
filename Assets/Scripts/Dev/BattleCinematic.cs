using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering.Universal;

// Dev recording: a full Human vs Avanti fleet battle filmed by a scripted camera director, for a trailer cut
// to music. Each ship holds its own lane so nothing collides; destroyed ships are replaced by FTL
// reinforcements; one carrier per side sits at the back and its squadrons are sent at the enemy line.
// Frames go to Logs/BattleCinematic for ffmpeg. Run with -executeMethod ShowcaseRunner.RunBattle.
[DefaultExecutionOrder(200)]
public class BattleCinematic : MonoBehaviour
{
    public int width = 1920;
    public int height = 1080;
    public int frameRate = 30;
    public float duration = 232f; // Eclipse Legion.
    public string outputDir;

    private const float LaneWidth = 38f;
    private const float FrontGap = 110f;
    private const float RankDepth = 45f;
    private const float CarrierDepth = 115f;
    private const float ReinforceDelay = 7f;

    private class Slot
    {
        public string prefab;
        public bool human;
        public Vector3 position;
        public Quaternion facing;
        public UnitHealthManager ship;
        public float respawnAt = -1f;
        public int generation;
        public bool line => !prefab.Contains("Carrier");
    }

    private readonly List<Slot> slots = new List<Slot>();
    private readonly StringBuilder report = new StringBuilder();
    private Camera mainCamera;
    private Camera captureCamera;
    private RenderTexture target;
    private Texture2D readback;
    private int frame;
    private bool capturing;
    private Vector3 centre, axis, across;

    private Vector3 camPosition, camLook, wantPosition, wantLook;
    private float follow = 3f;
    private Slot arrival; // A reinforcement that just jumped in, for the director to cut to.
    private Vector3 deathAt;
    private float deathTime = -1f;
    private int kills, reinforcements, missileShots, fighterShots;

    private IEnumerator Start()
    {
        string[] args = System.Environment.GetCommandLineArgs();
        int seconds = System.Array.IndexOf(args, "-cinematicSeconds");
        if (seconds >= 0 && seconds < args.Length - 1) duration = float.Parse(args[seconds + 1]); // Short test takes.
        if (string.IsNullOrEmpty(outputDir)) outputDir = Path.Combine(Application.dataPath, "..", "Logs", "BattleCinematic");
        Directory.CreateDirectory(outputDir);
        foreach (string old in Directory.GetFiles(outputDir, "*.jpg")) File.Delete(old);

        yield return null;
        Time.captureFramerate = frameRate;

        foreach (RTSCameraController controller in FindObjectsByType<RTSCameraController>(FindObjectsSortMode.None)) controller.enabled = false;
        foreach (PlayerSpaceManager manager in FindObjectsByType<PlayerSpaceManager>(FindObjectsSortMode.None)) manager.enabled = false;
        GameObject ground = GameObject.Find("Ground");
        if (ground != null) foreach (Renderer r in ground.GetComponentsInChildren<Renderer>()) r.enabled = false;

        Vector3 a = GameManager.Instance.attackerShipSpawnPoint.position;
        Vector3 d = GameManager.Instance.defenderShipSpawnPoint.position;
        centre = (a + d) / 2f;
        axis = (d - a).normalized;
        across = Vector3.Cross(Vector3.up, axis);

        HideOverlays();
        Lay(true);
        Lay(false);
        foreach (Slot slot in slots) Spawn(slot, jumpIn: false);

        yield return null;
        yield return null;
        HideOverlays();

        mainCamera = Camera.main;
        SetupCapture();
        StartCoroutine(Upkeep());

        capturing = true;
        int totalFrames = Mathf.CeilToInt(duration * frameRate); // Counted in frames: slow-motion shots stretch game time.
        yield return Establishing(7f);
        int shot = 0;
        while (frame < totalFrames)
        {
            float length = Random.Range(6f, 8.5f);
            if (arrival != null && arrival.ship != null) { Slot s = arrival; arrival = null; yield return Arrival(s, length); }
            else if (deathTime > 0f && Time.time - deathTime < 1.5f) { deathTime = -1f; yield return Aftermath(deathAt, Mathf.Min(length, 5f)); }
            else
            {
                switch (shot++ % 9)
                {
                    case 0: yield return Flyby(true, length); break;
                    case 1: yield return Hero(length); break;
                    case 2: yield return Shoulder(false, length); break;
                    case 3: yield return MissileChase(length); break;
                    case 4: yield return Flyby(false, length); break;
                    case 5: yield return Hero(length); break;
                    case 6: yield return FighterRun(length); break;
                    case 7: yield return Shoulder(true, length); break;
                    default: yield return Establishing(length); break;
                }
            }
        }

        capturing = false;
        report.Insert(0, $"BATTLE CINEMATIC: {frame} frames, {kills} ships destroyed, {reinforcements} reinforcements, {missileShots} missile shots, {fighterShots} fighter shots\n");
        File.WriteAllText(Path.Combine(outputDir, "report.txt"), report.ToString());
        Debug.Log(report.ToString());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#endif
    }

    // ---------------- Fleets ----------------

    // Every ship gets its own lane across the battle line, so reinforcements jumping in and ships holding
    // position never cross paths.
    private void Lay(bool human)
    {
        string f = human ? "Human/Human_" : "Avanti/Avanti_";
        string big = human ? "Battleship" : "Capital";
        Vector3 forward = human ? axis : -axis;
        Vector3 front = centre - forward * FrontGap / 2f;
        Vector3 side = Vector3.Cross(Vector3.up, forward);

        void Add(string type, int lane, float depth) => slots.Add(new Slot
        {
            prefab = f + type,
            human = human,
            position = front + side * lane * LaneWidth - forward * depth,
            facing = Quaternion.LookRotation(forward),
        });

        Add("Corvette", -4, 0f);
        Add("Destroyer", -2, 0f);
        Add("Corvette", 0, 0f);
        Add("Destroyer", 2, 0f);
        Add("Corvette", 4, 0f);
        Add("Cruiser", -3, RankDepth);
        Add(big, -1, RankDepth + 8f);
        Add("Cruiser", 1, RankDepth);
        Add("Destroyer", 3, RankDepth);
        Add("Carrier", 0, CarrierDepth);
    }

    private void Spawn(Slot slot, bool jumpIn)
    {
        GameObject prefab = null;
#if UNITY_EDITOR
        prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Prefabs/Osiris/{slot.prefab}.prefab");
#endif
        if (prefab == null || !prefab.TryGetComponent(out Ship ship)) { report.AppendLine("missing " + slot.prefab); return; }

        // Replacements hold a little behind the wreck of the ship they replace.
        Vector3 forward = slot.facing * Vector3.forward;
        Vector3 position = slot.position - forward * (slot.generation % 2) * 22f;
        GameObject go = GameManager.Instance.SpawnShip(ship, position, slot.facing, !slot.human, false);
        if (go == null) return;
        if (go.TryGetComponent(out NavMeshAgent agent) && agent.isOnNavMesh) agent.ResetPath();
        if (jumpIn) FTLJump.Arrive(go);
        slot.ship = go.GetComponent<UnitHealthManager>();
        slot.respawnAt = -1f;
    }

    private IEnumerator Upkeep()
    {
        while (true)
        {
            HideOverlays();

            foreach (Slot slot in slots)
            {
                if (slot.respawnAt < 0f && (slot.ship == null || slot.ship.IsDead))
                {
                    kills++;
                    if (slot.ship != null) { deathAt = slot.ship.transform.position; deathTime = Time.time; }
                    slot.respawnAt = Time.time + ReinforceDelay;
                }
                else if (slot.respawnAt > 0f && Time.time >= slot.respawnAt)
                {
                    slot.generation++;
                    Spawn(slot, jumpIn: true);
                    reinforcements++;
                    if (slot.line) arrival = slot;
                }
            }

            SendSquadrons();
            yield return new WaitForSeconds(0.5f);
        }
    }

    // Squadrons left to themselves hover by their carrier; send them at the enemy line.
    private void SendSquadrons()
    {
        foreach (UnitHealthManager unit in UnitHealthManager.Active)
        {
            if (unit == null || unit.IsDead || !unit.TryGetComponent(out Squadron squadron)) continue;
            if (squadron.CurrentTarget != null && !squadron.CurrentTarget.IsDead && squadron.CurrentTarget.GetComponent<Squadron>() == null) continue;

            bool human = !unit.isAttackerSide;
            Slot prey = slots.Where(s => s.human != human && s.line && s.ship != null && !s.ship.IsDead)
                             .OrderBy(_ => Random.value).FirstOrDefault();
            if (prey == null) continue;

            if (prey.ship.TryGetComponent(out HardpointManager hardpoints)) squadron.AssignTarget(hardpoints.GetRandomHardpoint());
            if (unit.TryGetComponent(out NavMeshAgent agent) && agent.isOnNavMesh)
            {
                Vector3 toward = (prey.ship.transform.position - unit.transform.position).normalized;
                agent.SetDestination(prey.ship.transform.position - toward * 35f);
            }
        }
    }

    private void HideOverlays()
    {
        // Open space for the recording: generated asteroid fields can land right on the battle line.
        foreach (AsteroidField field in FindObjectsByType<AsteroidField>(FindObjectsSortMode.None)) Destroy(field.gameObject);
        DropZoneOverlay.Hide();
        foreach (DropZoneOverlay overlay in FindObjectsByType<DropZoneOverlay>(FindObjectsSortMode.None))
        {
            foreach (Renderer r in overlay.GetComponentsInChildren<Renderer>()) r.enabled = false;
        }

        foreach (Canvas canvas in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
        {
            if (canvas.renderMode == RenderMode.WorldSpace && canvas.enabled) canvas.enabled = false;
        }
        foreach (PathVisualization path in FindObjectsByType<PathVisualization>(FindObjectsSortMode.None)) // Move-order lines.
        {
            path.enabled = false;
            if (path.TryGetComponent(out LineRenderer line)) line.enabled = false;
        }
    }

    // ---------------- Shots ----------------

    private IEnumerable<UnitHealthManager> Line(bool? human) =>
        slots.Where(s => s.line && (human == null || s.human == human.Value) && s.ship != null && !s.ship.IsDead).Select(s => s.ship);

    private static float Size(Component ship) => ship != null && ship.TryGetComponent(out HullDamage hull) ? Mathf.Max(hull.Size, 12f) : 25f;

    private IEnumerator Establishing(float seconds)
    {
        float side = Random.value < 0.5f ? -1f : 1f;
        float start = Time.time;
        Cut(centre + across * side * 260f + Vector3.up * 110f - axis * 40f, centre);
        while (Time.time - start < seconds)
        {
            float u = (Time.time - start) / seconds;
            wantPosition = centre + across * side * Mathf.Lerp(260f, 220f, u) + Vector3.up * Mathf.Lerp(110f, 80f, u) + axis * Mathf.Lerp(-40f, 40f, u);
            wantLook = centre;
            follow = 2f;
            yield return null;
        }
    }

    // Low pass along one fleet's line, between the two fleets, looking across at the ships.
    private IEnumerator Flyby(bool human, float seconds)
    {
        Vector3 forward = human ? axis : -axis;
        Vector3 lineFront = centre - forward * FrontGap / 2f;
        float dir = Random.value < 0.5f ? -1f : 1f;
        float start = Time.time;
        Vector3 Eye(float u) => lineFront + forward * 38f + across * dir * Mathf.Lerp(-170f, 170f, u) + Vector3.up * 14f;
        Cut(Eye(0f), Eye(0f) - forward * 60f + across * dir * 30f);
        while (Time.time - start < seconds)
        {
            float u = (Time.time - start) / seconds;
            wantPosition = Eye(u);
            wantLook = Eye(u) - forward * 70f + across * dir * 45f - Vector3.up * 6f;
            follow = 3f;
            yield return null;
        }
    }

    // Slow orbit around a line ship, the enemy line beyond it.
    private IEnumerator Hero(float seconds)
    {
        UnitHealthManager subject = Line(null).OrderByDescending(s => Size(s) * Random.Range(0.6f, 1.4f)).FirstOrDefault();
        if (subject == null) { yield return Establishing(seconds); yield break; }

        float size = Size(subject);
        float distance = size * 1.9f + 14f;
        float startAngle = Random.Range(0f, 360f);
        float sweep = Random.Range(25f, 45f) * (Random.value < 0.5f ? -1f : 1f);
        float start = Time.time;
        Vector3 Eye(float u)
        {
            Quaternion around = Quaternion.Euler(0f, startAngle + sweep * u, 0f);
            return subject.transform.position + around * Vector3.forward * distance + Vector3.up * size * 0.45f;
        }
        Cut(Eye(0f), subject.transform.position);
        while (Time.time - start < seconds && subject != null && !subject.IsDead)
        {
            float u = (Time.time - start) / seconds;
            wantPosition = Eye(u);
            wantLook = subject.transform.position;
            follow = 3f;
            yield return null;
        }
    }

    // Over the shoulder of a ship, looking at the enemy line.
    private IEnumerator Shoulder(bool human, float seconds)
    {
        UnitHealthManager subject = Line(human).OrderBy(_ => Random.value).FirstOrDefault();
        if (subject == null) { yield return Establishing(seconds); yield break; }

        float size = Size(subject);
        float side = Random.value < 0.5f ? -1f : 1f;
        float start = Time.time;
        Vector3 Eye()
        {
            Transform t = subject.transform;
            return t.position - t.forward * size * 1.3f + t.right * side * size * 0.7f + Vector3.up * size * 0.45f;
        }
        Cut(Eye(), centre);
        while (Time.time - start < seconds && subject != null && !subject.IsDead)
        {
            wantPosition = Eye();
            wantLook = subject.transform.position + subject.transform.forward * 120f;
            follow = 3f;
            yield return null;
        }
    }

    private IEnumerator MissileChase(float seconds)
    {
        Missile chased = null;
        float wait = Time.time + 6f;
        while (chased == null && Time.time < wait)
        {
            chased = Missile.Active.LastOrDefault(m => m.TargetShip != null && Vector3.Distance(m.transform.position, m.TargetShip.transform.position) > 70f);
            if (chased == null) yield return null;
        }
        if (chased == null) { yield return Hero(seconds); yield break; }

        missileShots++;
        GameSpeed.SetSpeed(0.5f);
        Vector3 last = chased.transform.position, heading = chased.Velocity.normalized;
        Cut(last - heading * 14f + Vector3.up * 4f, last + heading * 20f);
        float until = Time.time + seconds * 0.5f;
        while (chased.InFlight && Time.time < until)
        {
            if (chased.Velocity.sqrMagnitude > 0.01f) heading = chased.Velocity.normalized;
            last = chased.transform.position;
            wantPosition = last - heading * 14f + Vector3.up * 4f;
            wantLook = last + heading * 20f;
            follow = 8f;
            yield return null;
        }
        float hold = Time.time + 1.2f;
        while (Time.time < hold)
        {
            wantLook = last;
            follow = 2f;
            yield return null;
        }
        GameSpeed.SetSpeed(1f);
    }

    // Chase a fighter in toward the enemy line.
    private IEnumerator FighterRun(float seconds)
    {
        Fighter chased = FindObjectsByType<Fighter>(FindObjectsSortMode.None)
            .Where(f => f.Alive && f.GetComponentInParent<Squadron>() is Squadron s && s.CurrentTarget != null && s.CurrentTarget.GetComponent<Squadron>() == null)
            .OrderBy(_ => Random.value).FirstOrDefault();
        if (chased == null) { yield return Shoulder(Random.value < 0.5f, seconds); yield break; }

        fighterShots++;
        float start = Time.time;
        Vector3 heading = chased.transform.forward;
        Cut(chased.transform.position - heading * 10f + Vector3.up * 3f, chased.transform.position + heading * 30f);
        while (Time.time - start < seconds * 0.8f && chased != null && chased.Alive)
        {
            heading = Vector3.Slerp(heading, chased.transform.forward, Time.unscaledDeltaTime * 3f);
            wantPosition = chased.transform.position - heading * 10f + Vector3.up * 3f;
            wantLook = chased.transform.position + heading * 30f;
            follow = 6f;
            yield return null;
        }
    }

    private IEnumerator Arrival(Slot slot, float seconds)
    {
        UnitHealthManager subject = slot.ship;
        float size = Size(subject);
        Vector3 forward = slot.facing * Vector3.forward;
        Vector3 side = Vector3.Cross(Vector3.up, forward);
        Vector3 at = subject.transform.position;
        Cut(at + side * size * 2.2f + forward * size * 1.2f + Vector3.up * size * 0.6f, at);
        float start = Time.time;
        while (Time.time - start < seconds && subject != null)
        {
            wantLook = subject.transform.position;
            follow = 2f;
            yield return null;
        }
    }

    private IEnumerator Aftermath(Vector3 at, float seconds)
    {
        Vector3 eye = at + across * 70f * (Random.value < 0.5f ? -1f : 1f) + Vector3.up * 25f + axis * Random.Range(-30f, 30f);
        Cut(eye, at);
        float start = Time.time;
        while (Time.time - start < seconds)
        {
            wantPosition = eye + (at - eye).normalized * (Time.time - start) * 3f;
            wantLook = at;
            follow = 2f;
            yield return null;
        }
    }

    // ---------------- Camera ----------------

    private void Cut(Vector3 position, Vector3 look)
    {
        camPosition = wantPosition = position;
        camLook = wantLook = look;
    }

    private void LateUpdate()
    {
        if (captureCamera == null) return;
        float k = 1f - Mathf.Exp(-follow * Time.unscaledDeltaTime);
        camPosition = Vector3.Lerp(camPosition, wantPosition, k);
        camLook = Vector3.Lerp(camLook, wantLook, k);
        camPosition = KeepOutOfHulls(camPosition);
        if ((camLook - camPosition).sqrMagnitude < 0.01f) camLook = camPosition + axis;
        Quaternion rotation = Quaternion.LookRotation(camLook - camPosition);
        captureCamera.transform.SetPositionAndRotation(camPosition, rotation);
        if (mainCamera != null) mainCamera.transform.SetPositionAndRotation(camPosition, rotation); // Billboards face this view.
    }

    // Never let the camera sit inside a ship.
    private static Vector3 KeepOutOfHulls(Vector3 position)
    {
        foreach (UnitHealthManager ship in UnitHealthManager.Active)
        {
            if (ship == null || ship.IsDead || ship.TryGetComponent(out Squadron _)) continue;
            float radius = Size(ship) * 0.6f + 4f;
            Vector3 offset = position - ship.transform.position;
            if (offset.sqrMagnitude < radius * radius)
            {
                position = ship.transform.position + (offset.sqrMagnitude > 0.01f ? offset.normalized : Vector3.up) * radius;
            }
        }
        return position;
    }

    private void SetupCapture()
    {
        GameObject go = new GameObject("CaptureCamera");
        captureCamera = go.AddComponent<Camera>();
        captureCamera.CopyFrom(mainCamera);
        UniversalAdditionalCameraData data = captureCamera.GetUniversalAdditionalCameraData();
        data.renderPostProcessing = true;
        data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        captureCamera.fieldOfView = 50f;
        captureCamera.nearClipPlane = 0.5f;
        captureCamera.farClipPlane = 5000f;
        target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        captureCamera.targetTexture = target;
        readback = new Texture2D(width, height, TextureFormat.RGB24, false);
    }

    private void OnEnable()
    {
        StartCoroutine(CaptureLoop());
    }

    private IEnumerator CaptureLoop()
    {
        while (true)
        {
            yield return new WaitForEndOfFrame();
            if (!capturing || captureCamera == null) continue;

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            readback.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            readback.Apply(false);
            RenderTexture.active = previous;
            File.WriteAllBytes(Path.Combine(outputDir, $"f{frame:D5}.jpg"), readback.EncodeToJPG(90));
            frame++;
        }
    }
}
