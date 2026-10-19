using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering.Universal;

// Dev showcase: Human and Avanti destroyers and corvettes trade missile salvos while a scripted camera
// follows them in - a wide shot, Avanti torpedoes running into Human CIWS fire (slow motion), Human
// missiles punching through an Avanti shield, then a chase cam. Frames go to Logs/MissileShowcase for
// ffmpeg. Run with -executeMethod ShowcaseRunner.RunMissiles.
[DefaultExecutionOrder(200)]
public class MissileShowcase : MonoBehaviour
{
    public int width = 1280;
    public int height = 720;
    public int frameRate = 30;
    public string outputDir;

    private readonly StringBuilder report = new StringBuilder();
    private Camera mainCamera;
    private Camera captureCamera;
    private RenderTexture target;
    private Texture2D readback;
    private int frame;
    private bool capturing;

    private Vector3 centre, axis, across;
    private Vector3 camPosition, camLook;
    private Vector3 wantPosition, wantLook;
    private float follow = 4f;

    private IEnumerator Start()
    {
        if (string.IsNullOrEmpty(outputDir)) outputDir = Path.Combine(Application.dataPath, "..", "Logs", "MissileShowcase");
        Directory.CreateDirectory(outputDir);
        foreach (string old in Directory.GetFiles(outputDir, "*.jpg")) File.Delete(old);

        yield return null;
        Time.captureFramerate = frameRate;

        foreach (RTSCameraController controller in FindObjectsByType<RTSCameraController>(FindObjectsSortMode.None)) controller.enabled = false;
        foreach (PlayerSpaceManager manager in FindObjectsByType<PlayerSpaceManager>(FindObjectsSortMode.None)) manager.enabled = false;

        Vector3 a = GameManager.Instance.attackerShipSpawnPoint.position;
        Vector3 d = GameManager.Instance.defenderShipSpawnPoint.position;
        centre = (a + d) / 2f;
        axis = (d - a).normalized;
        across = Vector3.Cross(Vector3.up, axis);

        GameObject humanDestroyer = Spawn("Human/Human_Destroyer", centre - axis * 55f, axis, true);
        GameObject humanCorvetteA = Spawn("Human/Human_Corvette", centre - axis * 50f + across * 40f, axis, true);
        GameObject humanCorvetteB = Spawn("Human/Human_Corvette", centre - axis * 50f - across * 40f, axis, true);
        GameObject avantiDestroyer = Spawn("Avanti/Avanti_Destroyer", centre + axis * 55f, -axis, false);
        Spawn("Avanti/Avanti_Corvette", centre + axis * 50f + across * 40f, -axis, false);
        Spawn("Avanti/Avanti_Corvette", centre + axis * 50f - across * 40f, -axis, false);

        GameObject ground = GameObject.Find("Ground"); // The placement grid isn't drawn in a real battle.
        if (ground != null)
        {
            foreach (Renderer renderer in ground.GetComponentsInChildren<Renderer>()) renderer.enabled = false;
        }

        yield return null;
        yield return null;

        foreach (Canvas canvas in FindObjectsByType<Canvas>(FindObjectsSortMode.None)) // Health bars would block the close-ups.
        {
            if (canvas.renderMode == RenderMode.WorldSpace) canvas.enabled = false;
        }

        mainCamera = Camera.main;
        SetupCapture();

        // 1. Wide: both lines open fire.
        Cut(centre - across * 150f + Vector3.up * 55f - axis * 10f, centre);
        capturing = true;
        float until = Time.time + 10f;
        while (Time.time < until)
        {
            wantPosition = centre - across * 130f + Vector3.up * 45f + axis * Mathf.Lerp(-10f, 10f, 1f - (until - Time.time) / 10f);
            wantLook = centre;
            yield return null;
        }
        report.AppendLine($"wide: launched {Missile.Launched}, struck {Missile.Struck}, shot down {Missile.ShotDown}, splashed on shields {Missile.Splashed}");

        // 2. Plasma torpedoes run into Human CIWS (slow motion).
        yield return FrameSalvo(plasma: true, fallback: humanCorvetteA != null ? humanCorvetteA : humanDestroyer, seconds: 7f);
        report.AppendLine($"torpedoes vs CIWS: launched {Missile.Launched}, struck {Missile.Struck}, shot down {Missile.ShotDown}, splashed {Missile.Splashed}");

        // 3. Human missiles straight through an Avanti shield (slow motion).
        yield return FrameSalvo(plasma: false, fallback: avantiDestroyer, seconds: 7f);
        report.AppendLine($"missiles vs shield: launched {Missile.Launched}, struck {Missile.Struck}, shot down {Missile.ShotDown}, splashed {Missile.Splashed}");

        // 4. Chase cam: ride a fresh missile all the way in, then hold on the impact.
        GameSpeed.SetSpeed(0.6f);
        Missile chased = null;
        float wait = Time.time + 16f;
        while (chased == null && Time.time < wait)
        {
            wantPosition = centre - across * 130f + Vector3.up * 45f;
            wantLook = centre;
            follow = 2f;
            chased = Newest(null);
            if (chased != null && chased.TargetShip != null && Vector3.Distance(chased.transform.position, chased.TargetShip != null ? chased.TargetShip.transform.position : centre) < 60f) chased = null; // Want one with a long flight ahead.
            yield return null;
        }
        Vector3 lastPosition = chased != null ? chased.transform.position : centre;
        Vector3 lastHeading = axis;
        until = Time.time + 12f;
        while (chased != null && chased.InFlight && Time.time < until)
        {
            lastHeading = chased.Velocity.sqrMagnitude > 0.01f ? chased.Velocity.normalized : lastHeading;
            lastPosition = chased.transform.position;
            wantPosition = lastPosition - lastHeading * 14f + Vector3.up * 4f;
            wantLook = lastPosition + lastHeading * 20f;
            follow = 8f;
            yield return null;
        }
        report.AppendLine($"chase cam rode a missile for {12f - (until - Time.time):F1}s");

        until = Time.time + 2.5f; // Hold on the hit.
        while (Time.time < until)
        {
            wantPosition = lastPosition - lastHeading * 18f + Vector3.up * 5f;
            wantLook = lastPosition;
            follow = 3f;
            yield return null;
        }
        GameSpeed.SetSpeed(1f);

        report.Insert(0, $"MISSILE SHOWCASE: {frame} frames\n");
        report.AppendLine($"final: launched {Missile.Launched}, struck {Missile.Struck}, shot down {Missile.ShotDown} ({Missile.ShotDownByCIWS} by CIWS), splashed {Missile.Splashed}, out of fuel {Missile.Fizzled}");
        capturing = false;
        File.WriteAllText(Path.Combine(outputDir, "report.txt"), report.ToString());
        Debug.Log(report.ToString());

#if UNITY_EDITOR
        if (Application.isBatchMode || System.Environment.CommandLine.Contains("-executeMethod")) UnityEditor.EditorApplication.Exit(0);
        else UnityEditor.EditorApplication.isPlaying = false;
#endif
    }

    // Waits (up to a few seconds) for a salvo of the given kind, then frames its target from the side so
    // the incoming missiles and the defending fire are both in shot.
    private IEnumerator FrameSalvo(bool plasma, GameObject fallback, float seconds)
    {
        float wait = Time.time + 6f;
        Missile salvo = Newest(plasma);
        while (salvo == null && Time.time < wait)
        {
            wantLook = centre;
            yield return null;
            salvo = Newest(plasma);
        }

        Transform subject = salvo != null && salvo.TargetShip != null ? salvo.TargetShip.transform : fallback != null ? fallback.transform : null;
        if (subject == null) yield break;
        report.AppendLine($"{(plasma ? "torpedo" : "missile")} shot on {subject.name} (salvo found={salvo != null})");

        Vector3 incoming = salvo != null ? Flat(subject.position - salvo.transform.position) : (plasma ? -axis : axis);
        Vector3 side = Vector3.Cross(Vector3.up, incoming);
        float size = subject.TryGetComponent(out HullDamage hull) ? hull.Size : 25f;
        float away = Mathf.Max(45f, size * 2f);
        Vector3 Eye() => subject.position + side * away + Vector3.up * away * 0.3f + incoming * away * 0.35f;
        Vector3 Look() => subject.position - incoming * away * 0.45f;
        Cut(Eye(), Look());
        GameSpeed.SetSpeed(0.4f);

        float until = Time.time + seconds;
        while (Time.time < until && subject != null)
        {
            wantPosition = Eye();
            wantLook = Look();
            follow = 4f;
            yield return null;
        }
        GameSpeed.SetSpeed(1f);
    }

    private static Missile Newest(bool? plasma)
    {
        for (int i = Missile.Active.Count - 1; i >= 0; i--)
        {
            Missile missile = Missile.Active[i];
            if (plasma == null || missile.Warhead.passesThroughShields != plasma.Value) return missile;
        }
        return null;
    }

    private static Vector3 Flat(Vector3 v)
    {
        v.y = 0f;
        return v.sqrMagnitude > 0.001f ? v.normalized : Vector3.forward;
    }

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
        captureCamera.transform.SetPositionAndRotation(camPosition, Quaternion.LookRotation(camLook - camPosition));
        if (mainCamera != null) mainCamera.transform.SetPositionAndRotation(camPosition, captureCamera.transform.rotation); // Billboards face this view.
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
            File.WriteAllBytes(Path.Combine(outputDir, $"f{frame:D5}.jpg"), readback.EncodeToJPG(92));
            frame++;
        }
    }

    private GameObject Spawn(string key, Vector3 position, Vector3 facing, bool attacker)
    {
        GameObject prefab = null;
#if UNITY_EDITOR
        prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Prefabs/Osiris/{key}.prefab");
#endif
        if (prefab == null)
        {
            report.AppendLine("missing prefab " + key);
            return null;
        }

        Quaternion rotation = Quaternion.LookRotation(facing);
        GameObject go = prefab.TryGetComponent(out Ship ship)
            ? GameManager.Instance.SpawnShip(ship, position, rotation, attacker, false)
            : Instantiate(prefab, position, rotation);
        if (ship == null) GameManager.Instance.ApplyAllegiance(go, attacker);
        if (go != null && go.TryGetComponent(out NavMeshAgent agent) && agent.isOnNavMesh) agent.ResetPath(); // Hold the line.
        return go;
    }
}
