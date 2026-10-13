using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.AI;

public class DestructionShowcase : MonoBehaviour
{
    public int width = 960;
    public int height = 540;
    public int frameRate = 20;

    static readonly string[] Ships = { "Human/Human_Cruiser", "Avanti/Avanti_Destroyer", "Human/Human_Battleship", "Avanti/Avanti_Capital" };

    private readonly StringBuilder report = new StringBuilder();
    private string outputDir;
    private Camera captureCamera;
    private RenderTexture target;
    private Texture2D readback;
    private int frame;
    private Vector3 focus;
    private float viewDistance;
    private float elevation = 32f;
    private float orbit;
    private float orbitSpeed = 6f;
    private bool capturing;

    IEnumerator Start()
    {
        outputDir = Argument("-captureDir", Path.Combine(Application.temporaryCachePath, "DestructionShowcase"));
        Directory.CreateDirectory(outputDir);
        foreach (string old in Directory.GetFiles(outputDir, "*.png")) File.Delete(old);

        yield return null;
        Time.captureFramerate = frameRate;
        SetupCamera();

        Vector3 a = GameManager.Instance.attackerShipSpawnPoint.position;
        Vector3 d = GameManager.Instance.defenderShipSpawnPoint.position;
        Vector3 centre = (a + d) / 2f;
        Vector3 axis = (d - a).normalized;
        Vector3 across = Vector3.Cross(Vector3.up, axis);

        List<GameObject> fleet = new List<GameObject>();
        for (int i = 0; i < Ships.Length; i++)
        {
            Vector3 position = centre + across * (i - (Ships.Length - 1) * 0.5f) * 75f;
            fleet.Add(Spawn(Ships[i], position, Quaternion.LookRotation(axis)));
        }

        yield return null;
        yield return null;

        for (int i = 0; i < fleet.Count; i++)
        {
            yield return Showcase(fleet[i], Ships[i], axis);
        }

        yield return WreckField(centre);
        TestCover();

        capturing = false;
        File.WriteAllText(Path.Combine(outputDir, "report.txt"), report.ToString());
        File.WriteAllText(Path.Combine(outputDir, "done.txt"), frame.ToString());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#endif
    }

    IEnumerator Showcase(GameObject ship, string key, Vector3 axis)
    {
        UnitHealthManager health = ship.GetComponent<UnitHealthManager>();
        HullDamage hull = ship.GetComponent<HullDamage>();
        ShipDestruction destruction = ship.GetComponent<ShipDestruction>();

        viewDistance = Mathf.Max(30f, hull.Size * 1.6f);
        elevation = 30f;
        orbitSpeed = 6f;
        focus = ship.transform.position;

        NavMeshAgent agent = ship.GetComponent<NavMeshAgent>();
        if (agent != null && agent.isOnNavMesh) agent.SetDestination(ship.transform.position + axis * 300f);

        capturing = true;

        float until = Time.time + 1f;
        while (Time.time < until) { Track(ship); yield return null; }

        float nextHit = 0f;
        until = Time.time + 5f;
        while (Time.time < until && hull.HealthFraction > 0.12f && !health.IsDead)
        {
            if (Time.time >= nextHit)
            {
                nextHit = Time.time + 0.25f;
                health.DealRandomDamage(health.maxHealth * 0.05f);
            }
            Track(ship);
            yield return null;
        }

        until = Time.time + 1.5f;
        while (Time.time < until && ship != null) { Track(ship); yield return null; }

        destruction.SendMessage("DestroyNow");

        while (ship != null)
        {
            Track(ship);
            yield return null;
        }

        report.AppendLine($"{Path.GetFileName(key)}: style={destruction.Style} pieces={destruction.PieceCount}");

        until = Time.time + 7f;
        while (Time.time < until) yield return null;
    }

    IEnumerator WreckField(Vector3 centre)
    {
        viewDistance = 260f;
        elevation = 55f;
        orbitSpeed = 10f;

        float until = Time.time + 9f;
        while (Time.time < until)
        {
            Vector3 sum = Vector3.zero;
            int count = 0;
            foreach (WreckChunk chunk in FindObjectsByType<WreckChunk>(FindObjectsSortMode.None))
            {
                if (!chunk.IsPersistent) continue;
                sum += chunk.transform.position;
                count++;
            }
            Vector3 aim = count > 0 ? sum / count : centre;
            focus = Vector3.Lerp(focus, aim, 0.05f);
            yield return null;
        }
    }

    void TestCover()
    {
        int persistent = 0;
        int blocked = 0;
        foreach (WreckChunk chunk in FindObjectsByType<WreckChunk>(FindObjectsSortMode.None))
        {
            if (!chunk.IsPersistent) continue;
            persistent++;
            Collider body = chunk.GetComponentInChildren<Collider>();
            Vector3 aim = body != null ? body.bounds.center : chunk.transform.position;
            Vector3 from = aim + Vector3.right * 80f;
            Vector3 to = aim - Vector3.right * 80f;
            if (Physics.Linecast(from, to, out RaycastHit hit, WreckChunk.BlockingMask) && hit.collider.GetComponentInParent<WreckChunk>() != null) blocked++;
        }
        report.AppendLine($"wreck layer={WreckChunk.WreckLayer} persistent chunks={persistent} laser traces blocked={blocked}/{persistent}");
    }

    void Track(GameObject ship)
    {
        focus = Vector3.Lerp(focus, ship.transform.position, 0.1f);
    }

    void SetupCamera()
    {
        Camera main = Camera.main;
        GameObject go = new GameObject("CaptureCamera");
        captureCamera = go.AddComponent<Camera>();
        if (main != null) captureCamera.CopyFrom(main);
        captureCamera.farClipPlane = 5000f;
        captureCamera.fieldOfView = 50f;
        target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
        target.antiAliasing = 4;
        captureCamera.targetTexture = target;
        readback = new Texture2D(width, height, TextureFormat.RGB24, false);
    }

    void LateUpdate()
    {
        if (captureCamera == null || !capturing) return;

        orbit += Time.deltaTime * orbitSpeed;
        Quaternion around = Quaternion.Euler(elevation, 120f + orbit, 0f);
        captureCamera.transform.position = focus - around * Vector3.forward * viewDistance;
        captureCamera.transform.rotation = around;
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

    GameObject Spawn(string key, Vector3 pos, Quaternion rot)
    {
        GameObject prefab = null;
#if UNITY_EDITOR
        prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Prefabs/Osiris/{key}.prefab");
#endif
        Ship ship = prefab.GetComponent<Ship>();
        if (ship != null) return GameManager.Instance.SpawnShip(ship, pos, rot, true, false);
        GameObject go = Instantiate(prefab, pos, rot);
        GameManager.Instance.ApplyAllegiance(go, true);
        return go;
    }

    static string Argument(string name, string fallback)
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == name) return args[i + 1];
        }
        return fallback;
    }
}
