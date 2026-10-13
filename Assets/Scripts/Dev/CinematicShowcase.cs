using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.AI;

[DefaultExecutionOrder(200)]
public class CinematicShowcase : MonoBehaviour
{
    public int width = 960;
    public int height = 540;
    public int frameRate = 20;

    static readonly string[] Humans = { "Human/Human_Cruiser", "Human/Human_Battleship", "Human/Human_Destroyer" };
    static readonly string[] Avanti = { "Avanti/Avanti_Destroyer", "Avanti/Avanti_Capital", "Avanti/Avanti_Cruiser" };

    private readonly StringBuilder report = new StringBuilder();
    private string outputDir;
    private Camera mainCamera;
    private Camera captureCamera;
    private RenderTexture target;
    private Texture2D readback;
    private int frame;
    private bool capturing;

    IEnumerator Start()
    {
        outputDir = Argument("-captureDir", Path.Combine(Application.temporaryCachePath, "CinematicShowcase"));
        Directory.CreateDirectory(outputDir);
        foreach (string old in Directory.GetFiles(outputDir, "*.png")) File.Delete(old);

        yield return null;
        Time.captureFramerate = frameRate;

        Vector3 a = GameManager.Instance.attackerShipSpawnPoint.position;
        Vector3 d = GameManager.Instance.defenderShipSpawnPoint.position;
        Vector3 centre = (a + d) / 2f;
        Vector3 axis = (d - a).normalized;
        Vector3 across = Vector3.Cross(Vector3.up, axis);

        for (int i = 0; i < 3; i++)
        {
            Vector3 offset = across * (i - 1) * 55f;
            Order(Spawn(Humans[i], centre - axis * 110f + offset, Quaternion.LookRotation(axis), true), centre - axis * 30f + offset);
            Order(Spawn(Avanti[i], centre + axis * 110f + offset, Quaternion.LookRotation(-axis), false), centre + axis * 30f + offset);
        }

        yield return null;
        yield return null;

        CinematicCamera cinematic = FindFirstObjectByType<CinematicCamera>();
        report.AppendLine($"cinematic present={cinematic != null} wired camera={(cinematic != null && cinematic.viewCamera != null)} controls={(cinematic != null ? cinematic.disableWhileActive.Length : 0)}");
        if (cinematic == null) yield break;

        mainCamera = cinematic.viewCamera != null ? cinematic.viewCamera : Camera.main;
        SetupCapture();

        cinematic.Enter();
        for (int i = 0; i < 8 && cinematic.Target != null && !cinematic.Target.name.Contains("Battleship"); i++) cinematic.Cycle(1);
        report.AppendLine($"entered active={CinematicCamera.Active} target={Name(cinematic)} canvases hidden={CountHiddenCanvases()}");
        capturing = true;

        yield return Wait(16f);
        cinematic.Cycle(1);
        report.AppendLine($"right click -> {Name(cinematic)}");

        yield return Wait(12f);
        cinematic.Cycle(1);
        UnitHealthManager victim = cinematic.Target;
        report.AppendLine($"right click -> {Name(cinematic)} (will be destroyed)");

        yield return Wait(3f);
        UnitHealthManager health = victim;
        float nextHit = 0f;
        while (health != null && !health.IsDead)
        {
            if (Time.time >= nextHit)
            {
                nextHit = Time.time + 0.25f;
                health.DealRandomDamage(health.maxHealth * 0.07f);
            }
            yield return null;
        }

        float until = Time.time + 20f;
        while (Time.time < until && (cinematic.Target == null || cinematic.Target == victim)) yield return null;
        report.AppendLine($"after death auto-switched to {Name(cinematic)}");

        yield return Wait(6f);
        cinematic.Cycle(-1);
        report.AppendLine($"left click -> {Name(cinematic)}");
        yield return Wait(5f);

        cinematic.Exit();
        report.AppendLine($"exited active={CinematicCamera.Active} canvases restored={CountHiddenCanvases() == 0}");

        capturing = false;
        File.WriteAllText(Path.Combine(outputDir, "report.txt"), report.ToString());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#endif
    }

    static string Name(CinematicCamera cinematic)
    {
        return cinematic.Target != null ? cinematic.Target.name.Replace("(Clone)", "") : "none";
    }

    static int CountHiddenCanvases()
    {
        int count = 0;
        foreach (Canvas canvas in FindObjectsByType<Canvas>(FindObjectsSortMode.None)) if (!canvas.enabled) count++;
        return count;
    }

    IEnumerator Wait(float seconds)
    {
        float until = Time.time + seconds;
        while (Time.time < until) yield return null;
    }

    void Order(GameObject ship, Vector3 destination)
    {
        if (ship != null && ship.TryGetComponent(out NavMeshAgent agent) && agent.isOnNavMesh) agent.SetDestination(destination);
    }

    void SetupCapture()
    {
        GameObject go = new GameObject("CaptureCamera");
        captureCamera = go.AddComponent<Camera>();
        captureCamera.CopyFrom(mainCamera);
        target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
        target.antiAliasing = 4;
        captureCamera.targetTexture = target;
        readback = new Texture2D(width, height, TextureFormat.RGB24, false);
    }

    void LateUpdate()
    {
        if (captureCamera == null || mainCamera == null) return;
        captureCamera.transform.SetPositionAndRotation(mainCamera.transform.position, mainCamera.transform.rotation);
        captureCamera.fieldOfView = mainCamera.fieldOfView;
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
            File.WriteAllBytes(Path.Combine(outputDir, $"f{frame:D5}.png"), readback.EncodeToPNG());
            frame++;
        }
    }

    void OnEnable()
    {
        StartCoroutine(CaptureLoop());
    }

    GameObject Spawn(string key, Vector3 pos, Quaternion rot, bool attacker)
    {
        GameObject prefab = null;
#if UNITY_EDITOR
        prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Prefabs/Osiris/{key}.prefab");
#endif
        Ship ship = prefab.GetComponent<Ship>();
        if (ship != null) return GameManager.Instance.SpawnShip(ship, pos, rot, attacker, false);
        GameObject go = Instantiate(prefab, pos, rot);
        GameManager.Instance.ApplyAllegiance(go, attacker);
        return go;
    }

    static string Argument(string name, string fallback)
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++) if (args[i] == name) return args[i + 1];
        return fallback;
    }
}
