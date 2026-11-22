using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

// Dev check for battle damage on the hull (HullScarring's marks and HullDeformation's dents, holes and
// peeled plates). Each ship in turn is shot up the way gunfire lands in battle - streams of hits on a few
// patches of hull, from a few directions - then the camera circles it close, then it's killed so its
// breakup and flying plating show too. Frames go to -captureDir; -noDeform turns the mesh damage off for a
// before/after. Started by DestructionShowcaseRunner.RunHullDamage.
public class HullDamageShowcase : MonoBehaviour
{
    public int width = 1280, height = 720, frameRate = 30;

    static readonly string[] Ships = { "Human/Human_Battleship", "Human/Human_Cruiser", "Avanti/Avanti_Capital", "Avanti/Avanti_Destroyer" };

    private string outputDir;
    private Camera cam;
    private RenderTexture target;
    private Texture2D readback;
    private int frame;
    private bool capturing;
    private int landed;
    private readonly List<(Vector3 point, Vector3 normal)> lines = new List<(Vector3, Vector3)>();
    private Vector3 focus;
    private float distance, elevation = 22f, orbit, orbitSpeed = 14f;

    IEnumerator Start()
    {
        outputDir = Argument("-captureDir", Path.Combine(Application.temporaryCachePath, "HullDamage"));
        Directory.CreateDirectory(outputDir);
        foreach (string old in Directory.GetFiles(outputDir, "*.jpg")) File.Delete(old);
        HullDeformation.Enabled = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-noDeform") < 0;
        yield return null;
        Time.captureFramerate = frameRate;
        SetupCamera();

        Vector3 a = GameManager.Instance.attackerShipSpawnPoint.position, d = GameManager.Instance.defenderShipSpawnPoint.position;
        Vector3 centre = (a + d) / 2f, axis = (d - a).normalized;
        foreach (string key in Ships)
        {
            Random.InitState(key.GetHashCode()); // the same patches with and without -noDeform
            GameObject ship = Spawn(key, centre, Quaternion.LookRotation(axis));
            yield return null;
            yield return null;
            yield return ShootUp(ship);
            yield return Circle(ship);
            yield return Kill(ship);
        }
        capturing = false;
        File.WriteAllText(Path.Combine(outputDir, "done.txt"), frame.ToString());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#endif
    }

    IEnumerator ShootUp(GameObject ship)
    {
        HullDamage hull = ship.GetComponent<HullDamage>();
        UnitHealthManager health = ship.GetComponent<UnitHealthManager>();
        NavMeshStop(ship);
        focus = ship.transform.position;
        distance = Mathf.Max(25f, hull.Size * 1.15f);
        capturing = true;

        // A few patches, each under a stream of fire from one bearing, enough to carry most to breached and
        // some past it (HullScarring's stages are shares of the ship's health taken by one patch).
        int patches = 6;
        lines.Clear(); // ship-local, so they follow it
        for (int tries = 0; lines.Count < patches && tries < 200; tries++)
        {
            Vector3 p = hull.RandomSurfacePoint(out Vector3 n);
            if (n.y < -0.3f && tries < 150) continue; // keep the hits where the camera sees them
            lines.Add((ship.transform.InverseTransformPoint(p), ship.transform.InverseTransformDirection(n)));
        }
        float[] dose = { 0.12f, 0.1f, 0.095f, 0.07f, 0.06f, 0.035f };
        float until = Time.time + 9f;
        int shot = 0;
        landed = 0;
        while (Time.time < until)
        {
            for (int burst = 0; burst < 4; burst++, shot++)
            {
                int i = shot % lines.Count;
                if (dose[i] <= 0f) continue;
                Vector3 point = ship.transform.TransformPoint(lines[i].point);
                Vector3 normal = ship.transform.TransformDirection(lines[i].normal);
                Vector3 dir = (-normal + Random.insideUnitSphere * 0.25f).normalized;
                Vector3 from = point + Random.insideUnitSphere * hull.Size * 0.012f - dir * hull.Size * 0.6f;
                foreach (RaycastHit hit in Physics.RaycastAll(from, dir, hull.Size * 1.2f))
                {
                    if (hit.collider.GetComponentInParent<HullScarring>() == null || hit.collider.transform.root != ship.transform) continue;
                    dose[i] -= 0.004f;
                    landed++;
                    HullScarring.Impact(hit.collider, hit.point, dir * 40f, health.maxHealth * 0.004f);
                    break;
                }
            }
            focus = Vector3.Lerp(focus, ship.transform.position, 0.1f);
            yield return null;
        }
    }

    IEnumerator Circle(GameObject ship)
    {
        HullScarring scarring = ship.GetComponent<HullScarring>();
        HullDeformation deformation = ship.GetComponent<HullDeformation>();
        Closeups(ship);
        Debug.Log($"[HullDamage] {ship.name}: marks {(scarring != null ? scarring.MarkCount : -1)}, holes {(deformation != null ? deformation.Holes : 0)}, hits {landed}");
        float until = Time.time + 8f;
        orbitSpeed = 30f;
        while (Time.time < until && ship != null)
        {
            focus = Vector3.Lerp(focus, ship.transform.position, 0.1f);
            yield return null;
        }
        orbitSpeed = 14f;
    }

    IEnumerator Kill(GameObject ship)
    {
        UnitHealthManager health = ship.GetComponent<UnitHealthManager>();
        float next = 0f, giveUp = Time.time + 10f;
        while (ship != null && !health.IsDead)
        {
            if (Time.time > giveUp) { ship.SendMessage("DestroyNow"); break; }
            if (Time.time >= next) { next = Time.time + 0.2f; health.DealRandomDamage(health.maxHealth * 0.06f); }
            focus = Vector3.Lerp(focus, ship.transform.position, 0.1f);
            yield return null;
        }
        float until = Time.time + 9f;
        while (Time.time < until) yield return null;
        foreach (WreckChunk chunk in FindObjectsByType<WreckChunk>(FindObjectsSortMode.None)) Destroy(chunk.gameObject);
        yield return null;
    }

    // A still of each patch that was shot up, from close in and off to one side so dents and petals show.
    void Closeups(GameObject ship)
    {
        HullDamage hull = ship.GetComponent<HullDamage>();
        string dir = Path.Combine(outputDir, "close");
        Directory.CreateDirectory(dir);
        Camera still = new GameObject("Still").AddComponent<Camera>();
        still.CopyFrom(cam);
        still.fieldOfView = 40f;
        List<(Vector3 point, Vector3 normal)> spots = new List<(Vector3, Vector3)>(lines);
        HullDeformation deformation = ship.GetComponent<HullDeformation>();
        if (deformation != null) spots.InsertRange(0, deformation.HoleSpots);
        for (int i = 0; i < spots.Count; i++)
        {
            Vector3 point = ship.transform.TransformPoint(spots[i].point);
            Vector3 normal = ship.transform.TransformDirection(spots[i].normal);
            Vector3 side = Vector3.Cross(normal, Vector3.up);
            if (side.sqrMagnitude < 0.01f) side = Vector3.right;
            Vector3 from = point + (normal + side.normalized * 0.7f + Vector3.up * 0.25f).normalized * hull.Size * 0.32f;
            still.transform.SetPositionAndRotation(from, Quaternion.LookRotation(point - from));
            still.Render();
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            readback.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            readback.Apply(false);
            RenderTexture.active = previous;
            File.WriteAllBytes(Path.Combine(dir, $"{ship.name.Replace("(Clone)", "")}_{(deformation != null && i < deformation.HoleSpots.Count ? "hole" : "patch")}{i}.jpg"), readback.EncodeToJPG(92));
        }
        Destroy(still.gameObject);
    }

    void NavMeshStop(GameObject ship)
    {
        UnityEngine.AI.NavMeshAgent agent = ship.GetComponent<UnityEngine.AI.NavMeshAgent>();
        if (agent != null && agent.isOnNavMesh) { agent.isStopped = true; agent.ResetPath(); agent.velocity = Vector3.zero; }
        foreach (SolarStudios.PlayerUnitStateMachine machine in ship.GetComponentsInChildren<SolarStudios.PlayerUnitStateMachine>()) machine.enabled = false;
    }

    void SetupCamera()
    {
        Camera main = Camera.main;
        cam = new GameObject("DamageCamera").AddComponent<Camera>();
        if (main != null) cam.CopyFrom(main);
        cam.farClipPlane = 5000f;
        cam.fieldOfView = 45f;
        target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        cam.targetTexture = target;
        readback = new Texture2D(width, height, TextureFormat.RGB24, false);
    }

    void LateUpdate()
    {
        if (cam == null || !capturing) return;
        orbit += Time.deltaTime * orbitSpeed;
        Quaternion around = Quaternion.Euler(elevation + Mathf.Sin(orbit * 0.02f) * 12f, 120f + orbit, 0f);
        cam.transform.position = focus - around * Vector3.forward * distance;
        cam.transform.rotation = around;
        cam.Render();
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = target;
        readback.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        readback.Apply(false);
        RenderTexture.active = previous;
        File.WriteAllBytes(Path.Combine(outputDir, $"f{frame:D5}.jpg"), readback.EncodeToJPG(92));
        frame++;
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
        string[] args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++) if (args[i] == name) return args[i + 1];
        return fallback;
    }
}
