using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering.Universal;

[DefaultExecutionOrder(200)]
public class ShieldDemo : MonoBehaviour
{
    public int width = 1280;
    public int height = 720;
    public int frameRate = 20;
    public float maxDuration = 48f;

    private readonly StringBuilder report = new StringBuilder();
    private string outputDir;
    private Camera mainCamera;
    private Camera captureCamera;
    private RenderTexture target;
    private Texture2D readback;
    private int frame;
    private bool capturing;
    private Transform focus;
    private float distance;
    private float orbit;
    private UnitHealthManager subject;

    IEnumerator Start()
    {
        outputDir = Argument("-captureDir", Path.Combine(Application.temporaryCachePath, "ShieldDemo"));
        Directory.CreateDirectory(outputDir);
        foreach (string old in Directory.GetFiles(outputDir, "*.jpg")) File.Delete(old);

        yield return null;
        Time.captureFramerate = frameRate;

        foreach (RTSCameraController controller in FindObjectsByType<RTSCameraController>(FindObjectsSortMode.None)) controller.enabled = false;
        foreach (PlayerSpaceManager manager in FindObjectsByType<PlayerSpaceManager>(FindObjectsSortMode.None)) manager.enabled = false;

        Vector3 a = GameManager.Instance.attackerShipSpawnPoint.position;
        Vector3 d = GameManager.Instance.defenderShipSpawnPoint.position;
        Vector3 centre = (a + d) / 2f;
        Vector3 axis = (d - a).normalized;
        Vector3 across = Vector3.Cross(Vector3.up, axis);

        GameObject avanti = Spawn("Avanti/Avanti_Cruiser", centre, Quaternion.LookRotation(-axis), false);
        subject = avanti.GetComponent<UnitHealthManager>();
        List<GameObject> attackers = new List<GameObject>
        {
            Spawn("Human/Human_Cruiser", centre - axis * 80f + across * 30f, Quaternion.LookRotation(axis), true),
            Spawn("Human/Human_Destroyer", centre - axis * 75f - across * 35f, Quaternion.LookRotation(axis), true),
        };

        GameObject ground = GameObject.Find("Ground");
        if (ground != null)
        {
            foreach (Renderer renderer in ground.GetComponentsInChildren<Renderer>()) renderer.enabled = false;
        }

        yield return null;
        yield return null;

        HardpointManager hardpoints = avanti.GetComponent<HardpointManager>();
        foreach (GameObject attacker in attackers)
        {
            if (attacker.TryGetComponent(out HardpointManager own)) own.AssignTarget(hardpoints.GetRandomHardpoint());
            if (attacker.TryGetComponent(out NavMeshAgent agent) && agent.isOnNavMesh) agent.ResetPath();
        }

        ShieldGenerator shield = avanti.GetComponent<ShieldGenerator>();
        HullDamage hull = avanti.GetComponent<HullDamage>();
        focus = avanti.transform;
        distance = Mathf.Max(30f, hull != null ? hull.Size * 1.35f : 40f);
        mainCamera = Camera.main;
        SetupCapture();

        yield return null;
        report.AppendLine($"shield present={shield != null} capacity={shield?.Capacity:F0} hull max={subject.maxHealth:F0} slider wired={shield != null && shield.shieldSlider != null} layer={ShieldGenerator.Layer}");

        capturing = true;
        float started = Time.time;
        float collapsedAt = -1f;
        float lastReport = 0f;

        while (Time.time - started < maxDuration)
        {
            ShowBars();
            float elapsed = Time.time - started;

            if (collapsedAt < 0f && shield != null && !shield.IsUp && shield.Capacity > 0f)
            {
                collapsedAt = elapsed;
                report.AppendLine($"t={elapsed:F1}s shield collapsed, hull={subject.currentHealth / subject.maxHealth:P0}");
            }

            if (elapsed - lastReport >= 4f)
            {
                lastReport = elapsed;
                report.AppendLine($"t={elapsed:F0}s shield={(shield != null ? shield.Fraction : 0f):P0} hull={subject.currentHealth / subject.maxHealth:P0}");
            }

            if (collapsedAt >= 0f && elapsed - collapsedAt > 12f) break;
            if (subject.IsDead) break;
            yield return null;
        }

        capturing = false;
        report.AppendLine($"frames={frame}");
        File.WriteAllText(Path.Combine(outputDir, "report.txt"), report.ToString());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#endif
    }

    void ShowBars()
    {
        if (subject == null || subject.healthSlider == null) return;
        Transform canvas = subject.healthSlider.transform.parent;
        if (canvas != null && !canvas.gameObject.activeSelf) canvas.gameObject.SetActive(true);
        if (!subject.healthSlider.gameObject.activeSelf) subject.healthSlider.gameObject.SetActive(true);
    }

    void LateUpdate()
    {
        if (!capturing || focus == null || mainCamera == null) return;

        orbit += Time.deltaTime * 6f;
        Quaternion around = Quaternion.Euler(24f, 200f + orbit, 0f);
        Vector3 aim = focus.position + Vector3.up * distance * 0.12f;
        mainCamera.transform.SetPositionAndRotation(aim - around * Vector3.forward * distance * 1.25f, around);
        captureCamera.transform.SetPositionAndRotation(mainCamera.transform.position, mainCamera.transform.rotation);
        captureCamera.fieldOfView = 50f;
    }

    void SetupCapture()
    {
        GameObject go = new GameObject("CaptureCamera");
        captureCamera = go.AddComponent<Camera>();
        captureCamera.CopyFrom(mainCamera);
        UniversalAdditionalCameraData data = captureCamera.GetUniversalAdditionalCameraData();
        data.renderPostProcessing = true;
        data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        captureCamera.farClipPlane = 5000f;
        target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
        target.antiAliasing = 4;
        captureCamera.targetTexture = target;
        readback = new Texture2D(width, height, TextureFormat.RGB24, false);
    }

    IEnumerator CaptureLoop()
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

    void OnEnable()
    {
        StartCoroutine(CaptureLoop());
    }

    GameObject Spawn(string key, Vector3 position, Quaternion rotation, bool attacker)
    {
        GameObject prefab = null;
#if UNITY_EDITOR
        prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Prefabs/Osiris/{key}.prefab");
#endif
        return GameManager.Instance.SpawnShip(prefab.GetComponent<Ship>(), position, rotation, attacker, false);
    }

    static string Argument(string name, string fallback)
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++) if (args[i] == name) return args[i + 1];
        return fallback;
    }
}
