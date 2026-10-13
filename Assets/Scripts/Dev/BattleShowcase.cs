using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

[DefaultExecutionOrder(200)]
public class BattleShowcase : MonoBehaviour
{
    public int width = 1280;
    public int height = 720;
    public int frameRate = 20;
    public float duration = 232f;
    public float attritionPerSecond = 0.004f;
    public int maxReinforcementWaves = 6;

    static readonly (string key, int count)[] HumanFleet =
    {
        ("Human/Human_Battleship", 2), ("Human/Human_Carrier", 2), ("Human/Human_Cruiser", 3), ("Human/Human_Destroyer", 4),
        ("Human/Human_Corvette", 4), ("Human/Human_FighterSquadron", 2), ("Human/Human_InterceptorSquadron", 2), ("Human/Human_BomberSquadron", 2),
    };

    static readonly (string key, int count)[] AvantiFleet =
    {
        ("Avanti/Avanti_Capital", 2), ("Avanti/Avanti_Carrier", 1), ("Avanti/Avanti_Cruiser", 3), ("Avanti/Avanti_Destroyer", 3),
        ("Avanti/Avanti_Corvette", 4), ("Avanti/Avanti_FighterSquadron", 1), ("Avanti/Avanti_InterceptorSquadron", 1), ("Avanti/Avanti_BomberSquadron", 2),
    };

    static readonly (string key, int count)[] HumanWave =
    {
        ("Human/Human_Cruiser", 1), ("Human/Human_Destroyer", 2), ("Human/Human_Corvette", 2), ("Human/Human_FighterSquadron", 1), ("Human/Human_BomberSquadron", 1),
    };

    static readonly (string key, int count)[] AvantiWave =
    {
        ("Avanti/Avanti_Cruiser", 1), ("Avanti/Avanti_Destroyer", 2), ("Avanti/Avanti_Corvette", 2), ("Avanti/Avanti_FighterSquadron", 1), ("Avanti/Avanti_BomberSquadron", 1),
    };

    private readonly StringBuilder report = new StringBuilder();
    private readonly List<UnitHealthManager> humans = new List<UnitHealthManager>();
    private readonly List<UnitHealthManager> avanti = new List<UnitHealthManager>();
    private string outputDir;
    private Camera mainCamera;
    private Camera captureCamera;
    private RenderTexture target;
    private Texture2D readback;
    private int frame;
    private bool capturing;
    private float startedAt;
    private Vector3 centre;
    private Vector3 axis;
    private Vector3 across;
    private int humanWaves;
    private int avantiWaves;

    private float Elapsed => Time.time - startedAt;

    IEnumerator Start()
    {
        outputDir = Argument("-captureDir", Path.Combine(Application.temporaryCachePath, "BattleShowcase"));
        if (float.TryParse(Argument("-duration", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out float requested)) duration = requested;
        Directory.CreateDirectory(outputDir);
        foreach (string old in Directory.GetFiles(outputDir, "*.jpg")) File.Delete(old);

        yield return null;
        Time.captureFramerate = frameRate;

        Vector3 a = GameManager.Instance.attackerShipSpawnPoint.position;
        Vector3 d = GameManager.Instance.defenderShipSpawnPoint.position;
        centre = (a + d) / 2f;
        axis = (d - a).normalized;
        across = Vector3.Cross(Vector3.up, axis);

        Deploy(HumanFleet, centre - axis * 150f, axis, true, humans);
        Deploy(AvantiFleet, centre + axis * 150f, -axis, false, avanti);
        GameObject station = Spawn("Avanti/Avanti_Station", centre + axis * 260f, Quaternion.LookRotation(-axis), false);
        if (station != null)
        {
            UnitHealthManager stationHealth = station.GetComponent<UnitHealthManager>();
            stationHealth.ConfigureSpecial(false, true);
            avanti.Add(stationHealth);
        }

        report.AppendLine($"duration={duration:F0}s human units={humans.Count} (squadrons={humans.Count(IsSquadron)}) pop={Population(HumanFleet)}  avanti units={avanti.Count} (squadrons={avanti.Count(IsSquadron)}) pop={Population(AvantiFleet)} +station");

        yield return null;
        yield return null;

        CinematicCamera cinematic = FindFirstObjectByType<CinematicCamera>();
        mainCamera = cinematic.viewCamera != null ? cinematic.viewCamera : Camera.main;
        SetupCapture();

        cinematic.Enter();
        UnitHealthManager opener = avanti.Where(u => IsAlive(u) && u.GetComponent<HullDamage>() != null).OrderByDescending(u => u.GetComponent<HullDamage>().Size).FirstOrDefault();
        cinematic.Spectate(opener);
        report.AppendLine($"grid hidden={GroundHidden()}");

        capturing = true;
        startedAt = Time.time;

        StartCoroutine(Command(humans, avanti));
        StartCoroutine(Command(avanti, humans));
        StartCoroutine(Attrition());
        StartCoroutine(Telemetry());

        float nextCut = Time.time + 14f;
        int cut = 0;
        while (Elapsed < duration)
        {
            Reinforce();

            bool watchingDeath = cinematic.Target == null || cinematic.Target.IsDead;
            if (!watchingDeath && Time.time >= nextCut)
            {
                cut++;
                List<UnitHealthManager> side = cut % 2 == 0 ? avanti : humans;
                UnitHealthManager pick = cut % 3 == 1 ? PickSquadron(side, cinematic.Target) : PickShip(side, cinematic.Target);
                if (pick == null) pick = PickShip(side, cinematic.Target) ?? PickSquadron(side, cinematic.Target);
                if (pick != null)
                {
                    cinematic.Spectate(pick);
                    report.AppendLine($"t={Elapsed:F0}s spectate {Label(pick)}");
                }
                nextCut = Time.time + UnityEngine.Random.Range(12f, 17f);
            }
            yield return null;
        }

        report.AppendLine($"end t={Elapsed:F0}s humans alive={Combatants(humans)} avanti alive={Combatants(avanti)} waves human={humanWaves} avanti={avantiWaves}");
        cinematic.Exit();
        capturing = false;
        report.AppendLine($"frames={frame} grid restored={!GroundHidden()}");
        File.WriteAllText(Path.Combine(outputDir, "report.txt"), report.ToString());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#endif
    }

    void Deploy((string key, int count)[] fleet, Vector3 front, Vector3 facing, bool attacker, List<UnitHealthManager> roster)
    {
        Quaternion rotation = Quaternion.LookRotation(facing);
        int row = 0;
        foreach ((string key, int count) in fleet)
        {
            bool small = key.Contains("Squadron") || key.Contains("Corvette");
            float spacing = small ? 32f : key.Contains("Destroyer") ? 42f : 60f;
            float depth = key.Contains("Squadron") ? 55f : key.Contains("Corvette") ? 0f : key.Contains("Destroyer") ? 35f : key.Contains("Cruiser") ? 75f : 120f;
            float stagger = (row % 2) * spacing * 0.5f;
            for (int i = 0; i < count; i++)
            {
                Vector3 position = front - facing * depth + across * ((i - (count - 1) * 0.5f) * spacing + stagger);
                GameObject spawned = Spawn(key, position, rotation, attacker);
                if (spawned != null) roster.Add(spawned.GetComponent<UnitHealthManager>());
            }
            row++;
        }
    }

    void Reinforce()
    {
        if (duration - Elapsed < 45f) return;

        if (Combatants(humans) < 5 && humanWaves < maxReinforcementWaves)
        {
            humanWaves++;
            Deploy(HumanWave, centre - axis * 170f, axis, true, humans);
            report.AppendLine($"t={Elapsed:F0}s human reinforcements #{humanWaves}");
        }

        if (Combatants(avanti) < 5 && avantiWaves < maxReinforcementWaves)
        {
            avantiWaves++;
            Deploy(AvantiWave, centre + axis * 170f, -axis, false, avanti);
            report.AppendLine($"t={Elapsed:F0}s avanti reinforcements #{avantiWaves}");
        }
    }

    IEnumerator Command(List<UnitHealthManager> side, List<UnitHealthManager> enemies)
    {
        float wait = UnityEngine.Random.Range(0.5f, 2f);
        while (true)
        {
            yield return new WaitForSeconds(wait);
            wait = UnityEngine.Random.Range(8f, 12f);

            List<UnitHealthManager> foes = enemies.Where(IsAlive).ToList();
            if (foes.Count == 0) continue;

            List<UnitHealthManager> ships = side.Where(u => IsAlive(u) && !IsSquadron(u) && !u.isBattleStation).ToList();
            for (int group = 0; group < 3; group++)
            {
                List<SpaceUnit> members = new List<SpaceUnit>();
                for (int i = group; i < ships.Count; i += 3)
                {
                    if (ships[i].TryGetComponent(out SpaceUnit unit)) members.Add(unit);
                }
                if (members.Count == 0) continue;

                Vector3 centroid = Vector3.zero;
                foreach (SpaceUnit unit in members) centroid += unit.transform.position;
                centroid /= members.Count;

                List<UnitHealthManager> shipFoes = foes.Where(f => !IsSquadron(f)).ToList();
                if (shipFoes.Count == 0) shipFoes = foes;
                UnitHealthManager focus = shipFoes.OrderBy(f => (f.transform.position - centroid).sqrMagnitude + group * 2000f * UnityEngine.Random.value).First();
                AttackWithShips(members, focus);
            }

            foreach (UnitHealthManager unit in side.Where(u => IsAlive(u) && IsSquadron(u)))
            {
                OrderSquadron(unit.GetComponent<Squadron>(), foes);
            }
        }
    }

    void AttackWithShips(List<SpaceUnit> members, UnitHealthManager focus)
    {
        HardpointManager targetManager = focus.GetComponent<HardpointManager>();
        List<SpaceUnit> movers = new List<SpaceUnit>();
        foreach (SpaceUnit unit in members)
        {
            Transform aim = targetManager != null ? targetManager.GetRandomHardpoint() : focus.transform;
            if (aim == null) continue;
            ShipGuard.Cancel(unit);
            if (unit.TryGetComponent(out HardpointManager own)) own.AssignTarget(aim);
            if (unit.IsMobile) movers.Add(unit);
        }
        if (movers.Count == 0) return;

        List<Vector3> origins = movers.Select(u => u.transform.position).ToList();
        FleetFormation.Slot[] slots = FleetFormation.Attack(movers, origins, focus.transform.position);
        int order = SolarStudios.PlayerUnitMoveState.NextOrderGroup();
        for (int i = 0; i < movers.Count; i++)
        {
            SpaceUnit unit = movers[i];
            unit.moveState.ClearDestinations();
            unit.moveState.SetOrderGroup(order);
            if (slots[i].move) unit.moveState.AddDestination(slots[i].position);
            unit.moveState.SetFacing(slots[i].facing);
            BeginMoving(unit);
        }
    }

    void OrderSquadron(Squadron squadron, List<UnitHealthManager> foes)
    {
        if (squadron == null || squadron.IsMoving) return;
        if (squadron.CurrentTarget != null && !squadron.CurrentTarget.IsDead && squadron.Engaging) return;

        Vector3 from = squadron.transform.position;
        float fighterBias = squadron.fighterPriority > squadron.shipPriority ? 0.5f : 1.5f;
        UnitHealthManager focus = foes.OrderBy(f => (f.transform.position - from).sqrMagnitude * (IsSquadron(f) ? fighterBias : 1f)).First();

        HardpointManager targetManager = focus.GetComponent<HardpointManager>();
        Transform aim = targetManager != null && targetManager.hardpoints.Count > 0 ? targetManager.GetRandomHardpoint() : focus.transform;
        squadron.AssignTarget(aim);

        float distance = Vector3.Distance(from, focus.transform.position);
        if (distance <= squadron.engageRadius * 0.6f || squadron.Unit == null || !squadron.Unit.IsMobile) return;

        Vector3 approach = focus.transform.position + (from - focus.transform.position).normalized * squadron.engageRadius * 0.35f;
        squadron.Unit.moveState.ClearDestinations();
        squadron.Unit.moveState.AddDestination(approach);
        BeginMoving(squadron.Unit);
    }

    static void BeginMoving(SpaceUnit unit)
    {
        if (!unit.IsMobile) return;
        if ((UnityEngine.Object)unit.stateMachine.currentState != unit.moveState) unit.stateMachine.SetState(unit.moveState);
    }

    IEnumerator Attrition()
    {
        while (true)
        {
            yield return new WaitForSeconds(1f);
            float schedule = 1f - 0.8f * Mathf.Clamp01(Elapsed / (duration - 12f));
            Bleed(humans, schedule);
            Bleed(avanti, schedule);
        }
    }

    void Bleed(List<UnitHealthManager> side, float schedule)
    {
        List<UnitHealthManager> live = side.Where(u => IsAlive(u) && !u.isBattleStation && !IsSquadron(u)).ToList();
        float max = live.Sum(u => u.maxHealth);
        if (max <= 0f) return;

        float current = live.Sum(u => u.currentHealth);
        float roster = side.Where(u => u != null && !u.isBattleStation && !IsSquadron(u)).Sum(u => u.maxHealth);
        float fraction = roster > 0f ? current / roster : 0f;
        if (fraction <= schedule) return;

        float pressure = Mathf.Clamp01((fraction - schedule) * 6f);
        foreach (UnitHealthManager unit in live)
        {
            unit.DealRandomDamage(unit.maxHealth * attritionPerSecond * pressure * UnityEngine.Random.Range(0.3f, 1.7f));
        }
    }

    IEnumerator Telemetry()
    {
        while (true)
        {
            yield return new WaitForSeconds(30f);
            report.AppendLine($"t={Elapsed:F0}s humans={Combatants(humans)} avanti={Combatants(avanti)} | {SquadronStatus(humans, "human")} | {SquadronStatus(avanti, "avanti")}");
        }
    }

    string SquadronStatus(List<UnitHealthManager> side, string label)
    {
        List<Squadron> squadrons = side.Where(u => IsAlive(u) && IsSquadron(u)).Select(u => u.GetComponent<Squadron>()).ToList();
        int engaging = squadrons.Count(s => s.Engaging);
        int fighters = squadrons.Sum(s => s.fighters.Count(f => f != null && f.Alive));
        int holding = squadrons.Sum(s => s.fighters.Count(f => f != null && f.Alive && f.IsHolding));
        return $"{label} squadrons={squadrons.Count} engaging={engaging} fighters={fighters} holding={holding}";
    }

    UnitHealthManager PickShip(List<UnitHealthManager> side, UnitHealthManager current)
    {
        List<HullDamage> ships = side.Where(u => IsAlive(u) && u != current && u.GetComponent<HullDamage>() != null).Select(u => u.GetComponent<HullDamage>()).ToList();
        if (ships.Count == 0) return null;
        List<HullDamage> wounded = ships.Where(s => s.HealthFraction < 0.5f).ToList();
        List<HullDamage> pool = wounded.Count > 0 && UnityEngine.Random.value < 0.6f ? wounded : ships;
        return pool.OrderByDescending(s => s.Size * UnityEngine.Random.Range(0.6f, 1.4f)).First().GetComponent<UnitHealthManager>();
    }

    UnitHealthManager PickSquadron(List<UnitHealthManager> side, UnitHealthManager current)
    {
        List<Squadron> squadrons = side.Where(u => IsAlive(u) && u != current && IsSquadron(u)).Select(u => u.GetComponent<Squadron>())
            .Where(s => s.fighters.Any(f => f != null && f.Alive)).ToList();
        if (squadrons.Count == 0) return null;
        List<Squadron> fighting = squadrons.Where(s => s.Engaging).ToList();
        List<Squadron> pool = fighting.Count > 0 ? fighting : squadrons;
        return pool[UnityEngine.Random.Range(0, pool.Count)].Health;
    }

    static string Label(UnitHealthManager unit)
    {
        string name = unit.name.Replace("(Clone)", "");
        if (unit.TryGetComponent(out HullDamage hull)) return $"{name} hp={hull.HealthFraction:P0}";
        if (unit.TryGetComponent(out Squadron squadron)) return $"{name} fighters={squadron.fighters.Count(f => f != null && f.Alive)} engaging={squadron.Engaging}";
        return name;
    }

    static bool IsAlive(UnitHealthManager unit) => unit != null && !unit.IsDead;
    static bool IsSquadron(UnitHealthManager unit) => unit != null && unit.GetComponent<Squadron>() != null;
    static int Combatants(List<UnitHealthManager> side) => side.Count(u => IsAlive(u) && !u.isBattleStation && !IsSquadron(u));
    static int Population((string key, int count)[] fleet) => fleet.Sum(f => f.count * Cost(f.key));

    static int Cost(string key)
    {
#if UNITY_EDITOR
        GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Prefabs/Osiris/{key}.prefab");
        if (prefab != null && prefab.TryGetComponent(out Ship ship)) return ship.populationCost;
#endif
        return 0;
    }

    static bool GroundHidden()
    {
        GameObject ground = GameObject.Find("Ground");
        Renderer renderer = ground != null ? ground.GetComponentInChildren<Renderer>() : null;
        return renderer != null && !renderer.enabled;
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
            File.WriteAllBytes(Path.Combine(outputDir, $"f{frame:D5}.jpg"), readback.EncodeToJPG(92));
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
        if (prefab == null)
        {
            report.AppendLine($"missing prefab {key}");
            return null;
        }
        Ship ship = prefab.GetComponent<Ship>();
        if (ship != null) return GameManager.Instance.SpawnShip(ship, pos, rot, attacker, false);
        GameObject go = Instantiate(prefab, pos, rot);
        go.GetComponent<UnitHealthManager>().battleToken = UnitHealthManager.FreeUnitToken;
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
