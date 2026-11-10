using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering.Universal;

// Dev recording: a large Human (Titans of Osiris) fleet assaults an Avanti Empire world defended by a capital
// shipyard AND a battle station, with asteroid fields and nebulae guaranteed on the map. Unlike the older
// showcases this is a real battle: it is set up through BattleContext exactly as the galactic map would,
// and BOTH sides are run by the game's own AI battle commander (the Titans get a recording-only, non-player
// copy of their faction). Heroes lead both fleets (Von Wolfgang on the Chimera, Kaal on the Throne of Ashes).
//
// Run with -executeMethod ShowcaseRunner.RunSiege (frames to Logs/SiegeRecording, then ffmpeg), or add
// -nebulaPreview to just take a few stills of a nebula and quit.
//
// Live mode (a standalone build from SiegeRecorderBuild, launched with -siege): the battle plays at normal
// speed on screen for a screen recorder and nothing is captured in-game. It writes the wall-clock start and
// end of the battle (live_times.txt, to trim the recording) and logs the frame rate through the fight.
[DefaultExecutionOrder(-1000)]
public class SiegeRecording : MonoBehaviour
{
    public int width = 1920;
    public int height = 1080;
    public int frameRate = 30;
    public float duration = 2400f; // Until the battle is decided, capped at 40 minutes.
    public int populationCap = 90;

    private static readonly (string key, int count)[] HumanFleet =
    {
        ("Human/Human_Battleship", 8), ("Human/Human_Carrier", 4), ("Human/Human_Cruiser", 10), ("Human/Human_Destroyer", 13),
        ("Human/Human_Corvette", 11), ("Human/Human_FighterSquadron", 6), ("Human/Human_InterceptorSquadron", 4), ("Human/Human_BomberSquadron", 5),
    };

    private static readonly (string key, int count)[] AvantiFleet =
    {
        ("Avanti/Avanti_Capital", 3), ("Avanti/Avanti_Carrier", 2), ("Avanti/Avanti_Cruiser", 5), ("Avanti/Avanti_Destroyer", 6),
        ("Avanti/Avanti_Corvette", 6), ("Avanti/Avanti_FighterSquadron", 3), ("Avanti/Avanti_InterceptorSquadron", 2), ("Avanti/Avanti_BomberSquadron", 3),
    };

    // Dev balance runs (-balance cost|pop|siege, -timeScale N): headless, sped up, no heroes. "cost" and "pop" are
    // open-field fights (no station or shipyard) between fleets of about equal cost or equal population; "siege"
    // is this recording's battle. The report gives losses per class and in credits.
    private static readonly (string key, int count)[] HumanCostFleet =
    {
        ("Human/Human_Battleship", 4), ("Human/Human_Cruiser", 5), ("Human/Human_Destroyer", 6), ("Human/Human_Corvette", 7), ("Human/Human_Carrier", 1),
    };
    private static readonly (string key, int count)[] AvantiCostFleet =
    {
        ("Avanti/Avanti_Capital", 3), ("Avanti/Avanti_Cruiser", 3), ("Avanti/Avanti_Destroyer", 3), ("Avanti/Avanti_Corvette", 3), ("Avanti/Avanti_Carrier", 1),
    };
    private static readonly (string key, int count)[] AvantiPopFleet =
    {
        ("Avanti/Avanti_Capital", 4), ("Avanti/Avanti_Cruiser", 4), ("Avanti/Avanti_Destroyer", 3), ("Avanti/Avanti_Corvette", 4), ("Avanti/Avanti_Carrier", 1),
    };
    private string balance;

    private readonly StringBuilder report = new StringBuilder();
    private bool live;

    // In a standalone build, -siege turns the player into the recording.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void BootFromCommandLine()
    {
        if (Application.isEditor || !HasFlag("-siege")) return;
        GameObject go = new GameObject("SiegeRecording");
        DontDestroyOnLoad(go);
        go.AddComponent<SiegeRecording>();
    }
    private string outputDir;
    private bool preview;
    private Camera mainCamera;
    private Camera captureCamera;
    private CinematicCamera cinematic;
    private RenderTexture target;
    private Texture2D readback;
    private int frame;
    private bool capturing;
    private float startedAt;

    private void Awake()
    {
        preview = HasFlag("-nebulaPreview");
        balance = Argument("-balance", "");
        if (balance != "") populationCap = balance == "siege" ? populationCap : 999;
        AIBattleCommander.ProbeEnabled = true; // This recording's Avanti send a probe out to meet the attack.
        live = HasFlag("-live") || (!Application.isEditor && !HasFlag("-frames"));
        pipeTo = Argument("-pipeTo", "");
        outputDir = Argument("-captureDir", Path.Combine(Directory.GetCurrentDirectory(), "Logs", preview ? "NebulaPreview" : "SiegeRecording"));
        if (float.TryParse(Argument("-duration", ""), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float d)) duration = d;
        if (int.TryParse(Argument("-fps", ""), out int fps)) frameRate = fps;
        Directory.CreateDirectory(outputDir);
        foreach (string old in Directory.GetFiles(outputDir, "*.jpg")) File.Delete(old);
        foreach (string old in Directory.GetFiles(outputDir, "*.png")) File.Delete(old);

        // The map must have asteroids and nebulae for this one.
        BattleTerrain.ForceAsteroids = true;
        BattleHazards.ForceNebulae = 2;
        PresentationClock.FollowGameTime = !live; // Frame-by-frame capture: camera moves and FTL jumps at film speed.
        if (live)
        {
            Cursor.visible = false;
            Application.runInBackground = true; // Keeps playing while other windows are on top (window capture).
            Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = 0;
        }

        ConfigureManager();
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded; // In a build the scene loads after us.

        SetUpBattle();
    }

    private void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode) => ConfigureManager();

    private void ConfigureManager()
    {
        GameManager manager = FindFirstObjectByType<GameManager>();
        if (manager == null) return;
        manager.populationCap = populationCap;
        manager.battleEndDelay = 100000f; // Don't leave for the galactic map mid-recording.
    }

    private void SetUpBattle()
    {
#if UNITY_EDITOR
        Faction titans = UnityEditor.AssetDatabase.LoadAssetAtPath<Faction>("Assets/Data/Factions/Faction_TitansOfOsiris.asset");
        Faction avanti = UnityEditor.AssetDatabase.LoadAssetAtPath<Faction>("Assets/Data/Factions/Faction_AvantiEmpire.asset");
#else
        SiegeRecordingAssets bundle = Resources.Load<SiegeRecordingAssets>("Dev/SiegeRecordingAssets");
        Faction titans = bundle != null ? bundle.titans : null, avanti = bundle != null ? bundle.avanti : null;
#endif
        if (titans == null || avanti == null)
        {
            report.AppendLine("missing faction assets");
            return;
        }

        // A recording-only copy of the Titans that the AI commands (the real asset is untouched).
        Faction attacker = Instantiate(titans);
        attacker.name = titans.name;
        attacker.isPlayerFaction = false;

        BattleContext context = BattleContext.Instance != null ? BattleContext.Instance : new GameObject("BattleContext").AddComponent<BattleContext>();
        context.Clear();
        context.hasPendingBattle = true;
        context.attackerFaction = attacker;
        context.defenderFaction = avanti;
        context.hasDefender = true;
        context.destinationPlanetName = "Kesharra Prime";
        context.defenderHasShipyard = true;
        context.defenderHasBattleStation = true;

        if (balance == "cost" || balance == "pop")
        {
            context.defenderHasShipyard = false;
            context.defenderHasBattleStation = false;
            Fill(context, HumanCostFleet, attacker, "", true);
            Fill(context, balance == "pop" ? AvantiPopFleet : AvantiCostFleet, avanti, "", false);
        }
        else
        {
            Fill(context, HumanFleet, attacker, balance != "" ? "" : "eric_von_wolfgang", true);
            Fill(context, AvantiFleet, avanti, balance != "" ? "" : "varethis_kaal", false);
        }
        context.attackerStartPower = Strength.Of(context.attackerRoster);
        context.defenderStartPower = Strength.Of(context.defenderRoster);
        context.attackerStartRoster = new List<ShipEntry>(context.attackerRoster);
        context.defenderStartRoster = new List<ShipEntry>(context.defenderRoster);

        report.AppendLine($"humans {context.attackerRoster.Count} ships power {context.attackerStartPower:0}  avanti {context.defenderRoster.Count} ships power {context.defenderStartPower:0} + shipyard + station  popCap {populationCap}  difficulty {Difficulty.Current}");
    }

    private void Fill(BattleContext context, (string key, int count)[] fleet, Faction faction, string heroId, bool attacker)
    {
        List<ShipEntry> roster = new List<ShipEntry>();
        HeroDefinition hero = HeroDefinition.Find(heroId);
        if (hero != null)
        {
            Ship flagship = LoadShip(hero.factionName.Contains("Titans") ? $"Human/{hero.shipClass}" : $"Avanti/{hero.shipClass}");
            if (flagship != null) roster.Add(new ShipEntry { type = flagship, shipName = hero.flagshipName, hero = hero.id });
        }
        foreach ((string key, int count) in fleet)
        {
            Ship ship = LoadShip(key);
            for (int i = 0; i < count && ship != null; i++) roster.Add(ShipEntry.New(ship, faction));
        }

        List<BattleContext.ShipToken> tokens = attacker ? context.attackerTokens : context.defenderTokens;
        (attacker ? context.attackerFleetNames : context.defenderFleetNames).Add(attacker ? "SiegeFleet" : "HomeGuard");
        foreach (ShipEntry ship in roster) tokens.Add(new BattleContext.ShipToken { ship = ship, fleet = 0 });
        if (attacker) context.attackerRoster = roster;
        else context.defenderRoster = roster;
    }

    private Ship LoadShip(string key)
    {
#if UNITY_EDITOR
        GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Prefabs/Osiris/{key}.prefab");
        if (prefab != null && prefab.TryGetComponent(out Ship ship)) return ship;
#else
        SiegeRecordingAssets bundle = Resources.Load<SiegeRecordingAssets>("Dev/SiegeRecordingAssets");
        Ship built = bundle != null ? bundle.Find(key) : null;
        if (built != null) return built;
#endif
        report.AppendLine($"missing prefab {key}");
        return null;
    }

    private IEnumerator Start()
    {
        if (!live) StartCoroutine(CaptureLoop());
        yield return null;
        if (!live) Time.captureFramerate = frameRate;
        yield return null;
        yield return null;

        cinematic = FindFirstObjectByType<CinematicCamera>();
        mainCamera = cinematic != null && cinematic.viewCamera != null ? cinematic.viewCamera : Camera.main;
        if (!live) SetupCapture();

        report.AppendLine($"nebulae {BattleHazards.Instance?.zones.Count(z => z.kind == BattleHazards.Kind.Nebula)} ionStorms {BattleHazards.Instance?.zones.Count(z => z.kind == BattleHazards.Kind.IonStorm)} asteroidFields {BattleTerrain.Instance?.Fields.Count}");

        if (preview)
        {
            yield return NebulaStills();
            Finish();
            yield break;
        }

        if (balance != "") { yield return BalanceRun(); Finish(); yield break; }
        if (HasFlag("-gridTest")) { yield return GridTest(); Finish(); yield break; }
        if (HasFlag("-hitTest")) { yield return HitTest(); Finish(); yield break; }
        if (HasFlag("-reel")) yield return DestructionReel();
        else yield return Battle();
        Finish();
    }

    // ---------------- Nebula preview ----------------

    private IEnumerator NebulaStills()
    {
        BattleHazards.Zone nebula = BattleHazards.Instance != null ? BattleHazards.Instance.zones.FirstOrDefault(z => z.kind == BattleHazards.Kind.Nebula) : null;
        if (nebula == null)
        {
            report.AppendLine("no nebula on the map");
            yield break;
        }

        Vector3 centre = new Vector3(nebula.centre.x, 0f, nebula.centre.y);
        float r = nebula.radius;
        (string name, Vector3 offset)[] shots =
        {
            ("1_gameplay_view", new Vector3(0f, r * 2.4f, -r * 1.9f)),          // Roughly the normal RTS camera angle.
            ("2_low_angle", new Vector3(r * 1.6f, r * 0.35f, -r * 1.4f)),        // Cinematic, near the plane.
            ("3_from_above", new Vector3(0.01f, r * 3.2f, 0f)),                  // Straight down.
            ("4_wide_battlefield", new Vector3(-r * 2.5f, r * 3.5f, -r * 4f)),   // Nebula in context.
        };

        foreach ((string name, Vector3 offset) in shots)
        {
            captureCamera.transform.position = centre + offset;
            captureCamera.transform.LookAt(centre);
            yield return new WaitForEndOfFrame();
            yield return new WaitForEndOfFrame();
            SaveStill(name);
        }
        report.AppendLine($"nebula at {centre} radius {r:0}");
    }

    private void SaveStill(string name)
    {
        captureCamera.Render();
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = target;
        readback.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        readback.Apply(false);
        RenderTexture.active = previous;
        File.WriteAllBytes(Path.Combine(outputDir, name + ".png"), readback.EncodeToPNG());
    }

    // ---------------- The battle ----------------

    // Direction: action first. Every 14-18s the camera cuts to a ship that is firing right now - capitals
    // trading broadsides, escorts launching missiles, squadrons on attack runs, heroes in the thick of it -
    // never the same ship twice in a row. A short wide establishing shot opens, then roughly one 10s wide
    // every four minutes; the station gets one brief look. Runs until the battle is decided.
    private const float WideEvery = 600f; // Only the odd establishing shot: the film is about the fighting.
    private const float WideLength = 7f;
    private const float StationAt = 75f;
    private const float StationLength = 5f;
    private const float FiringWindow = 2.5f;

    private bool onOverview;
    private readonly List<UnitHealthManager> recent = new List<UnitHealthManager>();

    private IEnumerator Battle()
    {
        cinematic.Enter();
        cinematic.shotDuration = new Vector2(100000f, 100000f); // Angles change only on our cuts.
        cinematic.orbitSpeed *= 0.65f; // A slower, steadier orbit for film.

        capturing = true;
        startedAt = Time.time;
        wallStart = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
        if (HasFlag("-profile")) StartCoroutine(ProfileFight());
        float overAt = -1f;
        float cutEndsAt = 0f;
        float nextWideAt = WideEvery;
        bool stationShown = false;
        bool first = true;
        bool filmingRetreat = false;
        float subjectDiedAt = -1f;

        float nextReportAt = 30f;
        while (Time.time - startedAt < duration)
        {
            float t = Time.time - startedAt;
            if (t >= nextReportAt) // Keep the report on disk as we go, so a stopped run still has one.
            {
                nextReportAt += 30f;
                File.WriteAllText(Path.Combine(outputDir, "report.txt"), report.ToString());
            }
            if (GameManager.CombatOver)
            {
                if (overAt < 0f)
                {
                    overAt = Time.time;
                    report.AppendLine($"t={t:F0}s battle over - humans in play {GameManager.CountInPlay(true)}, avanti {GameManager.CountInPlay(false)}");
                    onOverview = true;
                    cinematic.ShowOverview(); // Close on the aftermath.
                }
                if (Time.time - overAt > 8f) break;
                yield return null;
                continue;
            }

            // A fleet is pulling out: stay on its flagship from the order through the FTL jump - no more cuts.
            if (!filmingRetreat && RetreatManager.Instance != null)
            {
                bool? leaving = RetreatManager.Instance.IsRetreating(true) ? true : RetreatManager.Instance.IsRetreating(false) ? false : (bool?)null;
                if (leaving.HasValue)
                {
                    filmingRetreat = true;
                    UnitHealthManager flagship = Retreater(leaving.Value);
                    if (flagship != null)
                    {
                        onOverview = false;
                        cinematic.Spectate(flagship);
                        report.AppendLine($"t={t:F0}s retreat ({(leaving.Value ? "humans" : "avanti")}) - " + Describe(flagship));
                    }
                }
            }
            if (filmingRetreat)
            {
                yield return null;
                continue;
            }

            // The subject was destroyed: stay for the explosion and the wreck (the camera orbits it) before moving on.
            bool subjectDead = !onOverview && cinematic.Target != null && cinematic.Target.IsDead;
            if (subjectDead && subjectDiedAt < 0f)
            {
                subjectDiedAt = Time.time;
                report.AppendLine($"t={t:F0}s   destroyed: {Describe(cinematic.Target)}");
            }
            bool lingering = subjectDiedAt >= 0f && Time.time - subjectDiedAt < DeathLinger;

            // Someone big is about to go: cut to them so the kill is on film.
            UnitHealthManager dying = null;
            if (!lingering && !onOverview && Time.time - lastCutAt > 5f) dying = Dying();

            bool due = Time.time >= cutEndsAt;
            bool subjectLost = !onOverview && !lingering && (cinematic.Target == null || cinematic.Target.IsDead);
            if (lingering && !due) { yield return null; continue; }
            if (dying != null && !due && !subjectLost)
            {
                onOverview = false;
                cinematic.Spectate(dying);
                Remember(dying);
                subjectDiedAt = -1f;
                lastCutAt = Time.time;
                cutEndsAt = Time.time + 12f;
                report.AppendLine($"t={t:F0}s dying {Describe(dying)}");
                yield return null;
                continue;
            }
            if (due || subjectLost)
            {
                subjectDiedAt = -1f;
                lastCutAt = Time.time;
                string what;
                if (first)
                {
                    first = false;
                    onOverview = true;
                    cinematic.ShowOverview();
                    cutEndsAt = Time.time + WideLength;
                    what = "wide (opening)";
                }
                else if (due && t >= nextWideAt)
                {
                    nextWideAt += WideEvery;
                    onOverview = true;
                    cinematic.ShowOverview();
                    cutEndsAt = Time.time + WideLength;
                    what = "wide";
                }
                else if (due && !stationShown && t >= StationAt && ShowStation())
                {
                    stationShown = true;
                    cutEndsAt = Time.time + StationLength;
                    what = "station " + Describe(cinematic.Target);
                }
                else
                {
                    UnitHealthManager pick = PickAction();
                    if (pick == null)
                    {
                        onOverview = true;
                        cinematic.ShowOverview();
                        what = "wide (nothing firing)";
                    }
                    else
                    {
                        onOverview = false;
                        cinematic.Spectate(pick);
                        Remember(pick);
                        what = Describe(pick);
                    }
                    cutEndsAt = due ? Time.time + UnityEngine.Random.Range(14f, 18f) : Mathf.Max(cutEndsAt, Time.time + 8f);
                }
                report.AppendLine($"t={t:F0}s {what}");
            }
            yield return null;
        }

        report.AppendLine($"end t={Time.time - startedAt:F0}s humans in play={GameManager.CountInPlay(true)} avanti in play={GameManager.CountInPlay(false)} frames={frame}");
        double wallEnd = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
        File.WriteAllText(Path.Combine(outputDir, "live_times.txt"), string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:F3} {1:F3}", wallStart, wallEnd));
        cinematic.Exit();
        capturing = false;
    }

    private const float DeathLinger = 6f;
    private float lastCutAt;
    private readonly HashSet<UnitHealthManager> deathsFilmed = new HashSet<UnitHealthManager>();

    private static float Hull(UnitHealthManager u) => u.maxHealth > 0f ? u.currentHealth / u.maxHealth : 1f;

    // A warship nearly gone and still in the fight: worth cutting to for the kill.
    private UnitHealthManager Dying()
    {
        if (cinematic.Target != null && !cinematic.Target.IsDead && Hull(cinematic.Target) < 0.25f) return null; // Already on one.
        UnitHealthManager pick = UnitHealthManager.Active
            .Where(u => u != null && !u.IsDead && !u.IsFreeUnit && u.GetComponent<Squadron>() == null && !deathsFilmed.Contains(u)
                && Hull(u) < 0.18f && Time.time - u.LastFiredAt < 6f)
            .OrderByDescending(u => u.maxHealth).FirstOrDefault();
        if (pick != null) deathsFilmed.Add(pick);
        return pick;
    }

    // The biggest ship of the retreating side, preferably its hero.
    private static UnitHealthManager Retreater(bool attackerSide)
    {
        List<UnitHealthManager> side = UnitHealthManager.Active
            .Where(u => u != null && !u.IsDead && u.isAttackerSide == attackerSide && !u.IsDefense && u.GetComponent<Squadron>() == null).ToList();
        return side.FirstOrDefault(u => u.entry != null && u.entry.IsHero) ?? side.OrderByDescending(u => u.maxHealth).FirstOrDefault();
    }

    // ---------------- Destruction reel (-reel) ----------------
    // A test film of the damage and destruction effects, in a live battle: turrets wrecked one by one on a
    // warship, a hull hammered until it breaches, then a death of each class. About two and a half minutes.
    private IEnumerator DestructionReel()
    {
        cinematic.Enter();
        cinematic.shotDuration = new Vector2(100000f, 100000f);
        cinematic.orbitSpeed *= 0.65f;
        capturing = true;
        startedAt = Time.time;
        report.AppendLine("destruction reel");

        // Let the fleets close and start trading fire.
        cinematic.ShowOverview();
        yield return Wait(12f);

        // 1. Turret wrecks: a Human cruiser loses its guns one at a time.
        UnitHealthManager subject = Pick(u => u.isAttackerSide && Class(u) == ShipType.Cruiser) ?? Pick(u => Class(u) == ShipType.Cruiser);
        if (subject != null)
        {
            report.AppendLine($"t={Time.time - startedAt:F0}s turret wrecks on {Describe(subject)}");
            cinematic.SpectateOrbit(subject);
            yield return Wait(3f);
            TurretController[] guns = subject.GetComponentsInChildren<TurretController>();
            int wrecked = 0;
            foreach (TurretController gun in guns)
            {
                if (wrecked >= 6 || subject.IsDead) break;
                if (gun == null || !gun.TryGetComponent(out HardpointHealth hp) || hp.currentHealth <= 0f) continue;
                hp.DealDamage(hp.currentHealth + 1f);
                wrecked++;
                yield return Wait(2.2f);
            }
            yield return Wait(3f);
        }

        // 2. Hull scarring: sustained fire on one patch of an Avanti cruiser's hull until it breaches.
        UnitHealthManager anvil = Pick(u => !u.isAttackerSide && Class(u) == ShipType.Cruiser) ?? Pick(u => Class(u) == ShipType.Destroyer);
        if (anvil != null)
        {
            report.AppendLine($"t={Time.time - startedAt:F0}s hull scarring on {Describe(anvil)}");
            cinematic.SpectateOrbit(anvil);
            HullDamage hull = anvil.GetComponent<HullDamage>();
            for (int i = 0; i < 60 && anvil != null && !anvil.IsDead; i++)
            {
                float flank = i % 2 == 0 ? 1f : -1f; // Both sides, so the orbiting camera sees one of them.
                Vector3 aim = anvil.transform.TransformPoint(hull.LocalBounds.center + Vector3.forward * hull.LocalBounds.extents.z * 0.25f) + UnityEngine.Random.insideUnitSphere * hull.Size * 0.01f;
                Vector3 from = aim + anvil.transform.right * hull.Size * flank;
                Collider struck = null;
                foreach (Collider c in anvil.GetComponentsInChildren<Collider>()) if (c is MeshCollider && c.name.Contains("Hull")) { struck = c; break; }
                if (struck == null) struck = anvil.GetComponentInChildren<Collider>();
                HullScarring.Impact(struck, aim, (aim - from).normalized, anvil.maxHealth * 0.003f);
                yield return Wait(0.15f);
            }
            yield return Wait(4f);
            report.AppendLine($"t={Time.time - startedAt:F0}s   marks on {Describe(anvil)}: {anvil.GetComponent<HullScarring>()?.MarkCount}");
        }

        // 2b. A ship worn down by spread-out fire to 30%: its worst patches should scar and breach.
        UnitHealthManager worn = Pick(u => u.isAttackerSide && Class(u) == ShipType.Battleship) ?? Pick(u => Class(u) == ShipType.Cruiser && u != subject);
        if (worn != null)
        {
            report.AppendLine($"t={Time.time - startedAt:F0}s wearing down {Describe(worn)}");
            cinematic.SpectateOrbit(worn);
            HullDamage wornHull = worn.GetComponent<HullDamage>();
            Collider[] wornColliders = worn.GetComponentsInChildren<MeshCollider>();
            for (int i = 0; i < 80 && worn != null && !worn.IsDead && worn.currentHealth > worn.maxHealth * 0.3f; i++)
            {
                Vector3 local = wornHull.LocalBounds.center + Vector3.Scale(wornHull.LocalBounds.extents, new Vector3(UnityEngine.Random.Range(-1f, 1f), UnityEngine.Random.Range(-0.5f, 1f), UnityEngine.Random.Range(-0.8f, 0.8f)));
                Vector3 aim = worn.transform.TransformPoint(local);
                Vector3 dir = (aim - (aim + UnityEngine.Random.onUnitSphere * wornHull.Size)).normalized;
                float hit = worn.maxHealth * 0.009f;
                if (wornColliders.Length > 0) HullScarring.Impact(wornColliders[0], aim, dir, hit);
                worn.DealRandomDamage(hit);
                yield return Wait(0.12f);
            }
            yield return Wait(5f);
            report.AppendLine($"t={Time.time - startedAt:F0}s   marks on {Describe(worn)}: {worn.GetComponent<HullScarring>()?.MarkCount}");
        }

        // 3. Missiles: a Human destroyer's salvos leaving its launch cells; then its tubes are knocked out (cook-off).
        UnitHealthManager launcherShip = Pick(u => u.isAttackerSide && Class(u) == ShipType.Destroyer && u.GetComponent<MissileLauncher>() != null);
        if (launcherShip != null)
        {
            report.AppendLine($"t={Time.time - startedAt:F0}s missiles from {Describe(launcherShip)}");
            cinematic.SpectateOrbit(launcherShip);
            yield return Wait(12f);
            Transform tubes = launcherShip.transform.Find("MissileTubes");
            if (tubes != null && tubes.TryGetComponent(out HardpointHealth tubeHealth) && tubeHealth.currentHealth > 0f)
            {
                report.AppendLine($"t={Time.time - startedAt:F0}s   tubes destroyed");
                tubeHealth.DealDamage(tubeHealth.currentHealth + 1f);
            }
            yield return Wait(5f);
        }

        // 4. Strike craft: a bomber squadron's runs, then fighters of each side in a dogfight.
        foreach ((string label, System.Func<UnitHealthManager, bool> filter, float hold) in new (string, System.Func<UnitHealthManager, bool>, float)[]
        {
            ("bombers", u => u.TryGetComponent(out Squadron q) && q.CraftRole == Squadron.Role.Bomber && q.Engaging, 14f),
            ("human fighters", u => u.TryGetComponent(out Squadron q) && !q.Avanti && q.CraftRole != Squadron.Role.Bomber && q.Engaging, 11f),
            ("avanti fighters", u => u.TryGetComponent(out Squadron q) && q.Avanti && q.CraftRole != Squadron.Role.Bomber && q.Engaging, 11f),
        })
        {
            UnitHealthManager craft = UnitHealthManager.Active.FirstOrDefault(u => u != null && !u.IsDead && filter(u));
            if (craft == null) { report.AppendLine($"t={Time.time - startedAt:F0}s no {label} engaged"); continue; }
            report.AppendLine($"t={Time.time - startedAt:F0}s {label}: {Describe(craft)}");
            cinematic.Spectate(craft);
            yield return Wait(hold);
        }

        // 5. Deaths: one of each class, held long enough to see the whole thing.
        ShipType[] classes = { ShipType.Corvette, ShipType.Destroyer, ShipType.Cruiser, ShipType.Battleship, ShipType.Carrier };
        bool attackers = false;
        foreach (ShipType type in classes)
        {
            attackers = !attackers;
            bool side = attackers;
            UnitHealthManager victim = Pick(u => Class(u) == type && u.isAttackerSide == side && u != subject) ?? Pick(u => Class(u) == type && u != subject);
            if (victim == null) continue;
            report.AppendLine($"t={Time.time - startedAt:F0}s death {Describe(victim)}");
            cinematic.SpectateOrbit(victim);
            yield return Wait(3f);
            foreach (HardpointHealth hp in victim.GetComponentsInChildren<HardpointHealth>())
            {
                if (hp.currentHealth > 0f) hp.DealDamage(hp.currentHealth + 1f);
            }
            yield return Wait(type == ShipType.Corvette ? 9f : 13f);
        }

        report.AppendLine($"end t={Time.time - startedAt:F0}s frames={frame}");
        cinematic.Exit();
        capturing = false;
    }

    // Dev check (-gridTest): put an enemy ship below the battle plane and fire click rays at it from where the
    // player's camera would be, logging everything each ray passes through and what the click code's own
    // raycasts would pick.
    private IEnumerator GridTest()
    {
        yield return Wait(4f);
        PlayerSpaceManager psm = FindFirstObjectByType<PlayerSpaceManager>();
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
        LayerMask Mask(string field) => psm != null ? (LayerMask)typeof(PlayerSpaceManager).GetField(field, flags).GetValue(psm) : (LayerMask)0;
        LayerMask enemyMask = Mask("enemyUnitLayer"), friendlyMask = Mask("friendlyUnitLayer"), groundMask = Mask("groundLayer"), uiMask = Mask("uiLayer");
        report.AppendLine($"masks enemy {enemyMask.value} friendly {friendlyMask.value} ground {groundMask.value} ui {uiMask.value} playerIsAttacker {GameManager.Instance.PlayerIsAttacker}");

        foreach (bool enemySide in new[] { true, false })
        {
            UnitHealthManager ship = Pick(u => (u.isAttackerSide != GameManager.Instance.PlayerIsAttacker) == enemySide && Class(u) == ShipType.Cruiser) ?? Pick(u => (u.isAttackerSide != GameManager.Instance.PlayerIsAttacker) == enemySide);
            if (ship == null) continue;
            ShipAltitude altitude = ship.GetComponent<ShipAltitude>();
            altitude?.SnapAltitude(-45f);
            yield return Wait(2f);
            Physics.SyncTransforms();
            report.AppendLine($"{(enemySide ? "ENEMY" : "FRIENDLY")} {Describe(ship)} layer {LayerMask.LayerToName(ship.gameObject.layer)} pos {ship.transform.position:F1}");
            foreach (Collider c in ship.GetComponentsInChildren<Collider>())
                if (!c.enabled || c.isTrigger) continue; else { }
            int byLayer = 0;
            foreach (Collider c in ship.GetComponentsInChildren<Collider>())
            {
                if (!c.enabled) continue;
                byLayer++;
                if (byLayer <= 6) report.AppendLine($"   collider {c.name} {c.GetType().Name} layer {LayerMask.LayerToName(c.gameObject.layer)} trigger {c.isTrigger} centre y {c.bounds.center.y:F1}");
            }
            report.AppendLine($"   enabled colliders {byLayer}");
            foreach (Vector3 eye in new[] { new Vector3(0f, 70f, -40f), new Vector3(30f, 25f, -60f), new Vector3(0f, -70f, -40f) })
            {
                Vector3 from = ship.transform.position + eye - Vector3.up * ship.transform.position.y; // Eye positions relative to the plane.
                Ray ray = new Ray(from, (ship.transform.position - from).normalized);
                string all = string.Join(", ", Physics.RaycastAll(ray, 500f).OrderBy(h => h.distance).Take(6).Select(h => $"{h.collider.name}[{LayerMask.LayerToName(h.collider.gameObject.layer)}]@{h.distance:F0}"));
                bool enemyHit = Physics.Raycast(ray, out RaycastHit eh, Mathf.Infinity, enemyMask);
                bool friendlyHit = Physics.Raycast(ray, out RaycastHit fh, Mathf.Infinity, friendlyMask);
                report.AppendLine($"   eye {eye} -> all: {all}");
                report.AppendLine($"      enemyMask hit {(enemyHit ? eh.collider.name : "none")}  friendlyMask hit {(friendlyHit ? fh.collider.name : "none")}  sensorsVisible {BattleSensors.PlayerCanSee(ship)}");
            }
        }
    }

    // Dev check (-hitTest): four missiles into an Avanti corvette, with its health before and after; then the
    // real battle for a few minutes, logging where every laser actually lands.
    private IEnumerator HitTest()
    {
        cinematic.Enter();
        capturing = true;
        startedAt = Time.time;
        yield return Wait(15f);

        UnitHealthManager corvette = null;
        for (float waited = 0f; waited < 120f && corvette == null; waited += 1f)
        {
            corvette = Pick(u => !u.isAttackerSide && Class(u) == ShipType.Corvette && u.GetComponent<ShieldGenerator>() != null);
            if (corvette == null) yield return Wait(1f);
        }
        if (corvette == null) report.AppendLine("MISSILE TEST: no Avanti corvette appeared");
        if (corvette != null)
        {
            HardpointManager hpm = corvette.GetComponent<HardpointManager>();
            report.AppendLine($"MISSILE TEST on {Describe(corvette)} health {corvette.currentHealth:F0}/{corvette.maxHealth:F0} hardpoints {hpm.hardpoints.Count}: " +
                string.Join(", ", hpm.hardpoints.Select(h => $"{h.name}={h.currentHealth:F0}")));
            int struckBefore = Missile.Struck, launchedBefore = Missile.Launched;
            int marksBefore = corvette.GetComponent<HullScarring>()?.MarkCount ?? 0;
            MissileArsenal.Warhead warhead = MissileArsenal.Instance.conventional;
            report.AppendLine($"   warhead {warhead.name} damage {warhead.damage} hullMultiplier {warhead.hullMultiplier} passesShields {warhead.passesThroughShields}");
            for (int i = 0; i < 4; i++)
            {
                Vector3 from = corvette.transform.position + corvette.transform.right * 70f + Vector3.up * 10f + UnityEngine.Random.insideUnitSphere * 5f;
                Missile.Fire(warhead, from, (corvette.transform.position - from).normalized, corvette, LayerMask.NameToLayer(GameManager.Instance.PlayerIsAttacker ? "FriendlyUnit" : "EnemyUnit"), 1 << corvette.gameObject.layer, 12f);
                yield return Wait(0.3f);
            }
            yield return Wait(8f);
            report.AppendLine($"   after: dead {corvette.IsDead} health {corvette.currentHealth:F0}/{corvette.maxHealth:F0} hardpoints {(hpm != null ? hpm.hardpoints.Count : -1)} struck {Missile.Struck - struckBefore} launched {Missile.Launched - launchedBefore} shotDown {Missile.ShotDown} marks +{(corvette.GetComponent<HullScarring>()?.MarkCount ?? 0) - marksBefore}");
            if (hpm != null) report.AppendLine("   " + string.Join(", ", hpm.hardpoints.Select(h => $"{h.name}={h.currentHealth:F0}")));
        }

        Laser.ResetStats();
        for (int i = 0; i < 6; i++)
        {
            yield return Wait(30f);
            int total = Laser.StatHardpoint + Laser.StatHullFoundHardpointBelow + Laser.StatHull + Laser.StatShield + Laser.StatOther + Laser.StatExpired;
            int marks = UnitHealthManager.Active.Where(u => u != null).Sum(u => u.GetComponent<HullScarring>()?.MarkCount ?? 0);
            report.AppendLine($"LASERS t={Time.time - startedAt:F0}s total {total}: hardpoint {Laser.StatHardpoint}  hull(child hardpoint) {Laser.StatHullFoundHardpointBelow}  bare hull {Laser.StatHull}  shield {Laser.StatShield}  wreck/rock/fighter {Laser.StatOther}  missed {Laser.StatExpired}   marks on living ships {marks}");
            report.AppendLine($"   bolt speed avg {(Laser.SpeedCount > 0 ? Laser.SpeedSum / Laser.SpeedCount : 0):F0} max {Laser.SpeedMax:F0}");
            foreach ((string label, Laser.MissStats m) in new[] { ("at ships", Laser.MissShip), ("at fighters/missiles", Laser.MissCraft) })
            {
                int measured = Mathf.Max(1, m.count - m.noTarget);
                report.AppendLine($"   misses {label}: {m.count} (target gone {m.noTarget})  closest pass avg {m.closest / measured:F1}  near<3 {m.near} mid<10 {m.mid} wild {m.wild}  fired from {m.fireDistance / measured:F0}  bolt {m.boltSpeed / measured:F0}  target moving {m.targetSpeed / measured:F1}  shooter moving {m.shooterSpeed / measured:F1}");
            }
        }
        cinematic.Exit();
        capturing = false;
    }

    private IEnumerator Wait(float seconds)
    {
        float until = Time.time + seconds;
        while (Time.time < until) yield return null;
    }

    private static ShipType Class(UnitHealthManager unit) => unit.TryGetComponent(out SpaceUnit su) ? su.shipType : ShipType.Station;

    private static UnitHealthManager Pick(System.Func<UnitHealthManager, bool> filter)
    {
        return UnitHealthManager.Active.Where(u => u != null && !u.IsDead && !u.IsDefense && u.GetComponent<Squadron>() == null && filter(u))
            .OrderByDescending(u => u.currentHealth).FirstOrDefault();
    }

    private bool ShowStation()
    {
        UnitHealthManager station = UnitHealthManager.Active.FirstOrDefault(u => u != null && !u.IsDead && u.isBattleStation)
            ?? UnitHealthManager.Active.FirstOrDefault(u => u != null && !u.IsDead && u.IsDefense);
        if (station == null) return false;
        onOverview = false;
        cinematic.Spectate(station);
        return true;
    }

    private void Remember(UnitHealthManager unit)
    {
        recent.Add(unit);
        if (recent.Count > 3) recent.RemoveAt(0);
    }

    // A squadron worth filming: some of its craft are dogfighting, being chased or strafing a ship right now.
    private static bool InTheFight(Squadron squadron)
    {
        int busy = 0;
        foreach (Fighter fighter in squadron.fighters)
            if (fighter != null && fighter.Alive && (fighter.Prey != null || fighter.Pursuer != null || fighter.ShipAim != null || fighter.IsFiring)) busy++;
        return busy >= 2;
    }

    // Something firing right now, weighted towards the big guns and missile boats.
    private UnitHealthManager PickAction()
    {
        // Carriers hang back launching craft and barely fight: they're not worth the screen time.
        List<UnitHealthManager> firing = UnitHealthManager.Active
            .Where(u => u != null && !u.IsDead && !u.IsDefense && !recent.Contains(u) && Time.time - u.LastFiredAt < FiringWindow && Class(u) != ShipType.Carrier)
            .ToList();

        List<UnitHealthManager> heroes = firing.Where(u => u.entry != null && u.entry.IsHero).ToList();
        List<UnitHealthManager> squadrons = firing.Where(u => u.TryGetComponent(out Squadron s) && s.Engaging && InTheFight(s)).ToList();
        List<UnitHealthManager> missiles = firing.Where(u => u.GetComponentInChildren<MissileLauncher>() != null).ToList();
        List<UnitHealthManager> capitals = firing.Where(u => u.TryGetComponent(out SpaceUnit su)
            && (su.shipType == ShipType.Battleship || su.shipType == ShipType.Cruiser)).ToList();
        List<UnitHealthManager> escorts = firing.Where(u => u.GetComponent<Squadron>() == null).Except(capitals).ToList();

        float roll = UnityEngine.Random.value;
        List<UnitHealthManager> pool =
            roll < 0.1f && heroes.Count > 0 ? heroes :
            roll < 0.27f && missiles.Count > 0 ? missiles :
            roll < 0.57f && capitals.Count > 0 ? capitals :
            roll < 0.83f && squadrons.Count > 0 ? squadrons :
            escorts.Count > 0 ? escorts :
            capitals.Count > 0 ? capitals : firing;
        if (pool.Count == 0)
        {
            List<UnitHealthManager> all = UnitHealthManager.Active.Where(u => u != null && !u.IsDead).ToList();
            return MostEngaged(all);
        }
        return pool[UnityEngine.Random.Range(0, pool.Count)];
    }

    // A warship in the thick of it: the most enemy warships close by.
    private static UnitHealthManager MostEngaged(List<UnitHealthManager> all)
    {
        List<UnitHealthManager> ships = all.Where(u => !u.IsDefense && u.GetComponent<Squadron>() == null && !u.IsFreeUnit && Class(u) != ShipType.Carrier).ToList();
        return ships.OrderByDescending(u => ships.Count(o => o.isAttackerSide != u.isAttackerSide && Vector3.Distance(o.transform.position, u.transform.position) < 220f))
            .FirstOrDefault();
    }

    private static string Describe(UnitHealthManager unit)
    {
        string name = unit.entry != null && !string.IsNullOrEmpty(unit.entry.shipName) ? unit.entry.shipName : unit.name.Replace("(Clone)", "");
        return $"{name} ({(unit.isAttackerSide ? "human" : "avanti")}) hp={(unit.maxHealth > 0f ? unit.currentHealth / unit.maxHealth : 0f):P0}";
    }

    // ---------------- Capture ----------------

    private void SetupCapture()
    {
        GameObject go = new GameObject("CaptureCamera");
        captureCamera = go.AddComponent<Camera>();
        if (mainCamera != null) captureCamera.CopyFrom(mainCamera);
        UniversalAdditionalCameraData data = captureCamera.GetUniversalAdditionalCameraData();
        data.renderPostProcessing = true;
        data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        captureCamera.allowHDR = true;
        captureCamera.farClipPlane = Mathf.Max(captureCamera.farClipPlane, 6000f);
        target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        captureCamera.targetTexture = target;
        readback = new Texture2D(width, height, TextureFormat.RGB24, false);
    }

    private double wallStart;
    private float fpsWindowStart = -1f, fpsWorst = float.MaxValue;
    private int fpsFrames;

    // Live mode: the frame rate through the fight, every 10 seconds (average and worst frame).
    private void Update()
    {
        if (!live || !capturing) return;
        if (fpsWindowStart < 0f) fpsWindowStart = Time.unscaledTime;
        fpsFrames++;
        if (Time.unscaledDeltaTime > 0f) fpsWorst = Mathf.Min(fpsWorst, 1f / Time.unscaledDeltaTime);
        float span = Time.unscaledTime - fpsWindowStart;
        if (span < 10f) return;
        report.AppendLine($"  fps t={Time.time - startedAt:F0}s avg {fpsFrames / span:F0} worst {fpsWorst:F0}");
        fpsWindowStart = Time.unscaledTime;
        fpsFrames = 0;
        fpsWorst = float.MaxValue;
    }

    private void LateUpdate()
    {
        if (preview || captureCamera == null || mainCamera == null) return;
        captureCamera.transform.SetPositionAndRotation(mainCamera.transform.position, mainCamera.transform.rotation);
        captureCamera.fieldOfView = mainCamera.fieldOfView;
    }

    // ---------------- Piped capture ----------------
    // -frames -pipeTo <file.mkv>: every frame is copied off the GPU in the background (AsyncGPUReadback, a few
    // frames in flight) and streamed as raw pixels into ffmpeg, which encodes on its own threads. No image files,
    // and the game never stalls waiting for a readback. Frames stay in order: the oldest request is written first.
    private string pipeTo;
    private System.Diagnostics.Process encoder;
    private Stream encoderInput;
    private readonly Queue<UnityEngine.Rendering.AsyncGPUReadbackRequest> inFlight = new Queue<UnityEngine.Rendering.AsyncGPUReadbackRequest>();
    private byte[] frameBytes;
    private const int MaxInFlight = 4;

    private bool StartEncoder()
    {
        if (string.IsNullOrEmpty(pipeTo) || encoder != null) return encoder != null;
        string flip = HasFlag("-noFlip") ? "" : "-vf vflip ";
        var info = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "ffmpeg",
            Arguments = $"-hide_banner -loglevel error -y -f rawvideo -pix_fmt rgba -s {width}x{height} -r {frameRate} -i - {flip}-c:v h264_amf -quality quality -rc cqp -qp_i 16 -qp_p 18 \"{pipeTo}\"",
            UseShellExecute = false,
            RedirectStandardInput = true,
            CreateNoWindow = true,
        };
        encoder = System.Diagnostics.Process.Start(info);
        encoderInput = encoder.StandardInput.BaseStream;
        frameBytes = new byte[width * height * 4];
        report.AppendLine("encoding to " + pipeTo);
        return true;
    }

    private void WriteFrame(UnityEngine.Rendering.AsyncGPUReadbackRequest request)
    {
        if (request.hasError) { report.AppendLine($"readback error at frame {frame}"); return; }
        request.GetData<byte>().CopyTo(frameBytes);
        encoderInput.Write(frameBytes, 0, frameBytes.Length);
        frame++;
    }

    private void DrainReadbacks(bool all)
    {
        while (inFlight.Count > 0)
        {
            UnityEngine.Rendering.AsyncGPUReadbackRequest oldest = inFlight.Peek();
            if (!oldest.done)
            {
                if (!all && inFlight.Count < MaxInFlight) return;
                oldest.WaitForCompletion();
            }
            WriteFrame(inFlight.Dequeue());
        }
    }

    private void StopEncoder()
    {
        if (encoder == null) return;
        DrainReadbacks(true);
        encoderInput.Flush();
        encoderInput.Close();
        encoder.WaitForExit(120000);
        report.AppendLine($"encoder exit {(encoder.HasExited ? encoder.ExitCode.ToString() : "timeout")} frames={frame}");
        encoder = null;
    }

    private IEnumerator CaptureLoop()
    {
        while (true)
        {
            if (!string.IsNullOrEmpty(pipeTo))
            {
                yield return new WaitForEndOfFrame();
                if (!capturing || captureCamera == null) { if (encoder != null && !capturing) StopEncoder(); continue; }
                if (!StartEncoder()) continue;
                if (Application.isBatchMode) captureCamera.Render(); // Headless: nothing draws cameras on its own.
                inFlight.Enqueue(UnityEngine.Rendering.AsyncGPUReadback.Request(target, 0, TextureFormat.RGBA32));
                DrainReadbacks(false);
                continue;
            }

            yield return new WaitForEndOfFrame();
            if (!capturing || captureCamera == null) continue;

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            readback.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            RenderTexture.active = previous;
            readback.Apply(false);
            File.WriteAllBytes(Path.Combine(outputDir, $"f{frame:D5}.jpg"), readback.EncodeToJPG(90));
            frame++;
        }
    }

    private void Finish()
    {
        StopEncoder();
        File.WriteAllText(Path.Combine(outputDir, "report.txt"), report.ToString());
        Debug.Log("SIEGE REPORT\n" + report);
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }

    // Development builds: -profile records Unity's profiler through 60s of the main engagement (from 45s in)
    // to <captureDir>/siege_profile.raw, then quits. SiegeProfileReport (editor) turns it into a summary.
    private IEnumerator ProfileFight()
    {
        float from = startedAt + 45f;
        while (Time.time < from) yield return null;
        string path = Path.Combine(outputDir, "siege_profile.raw");
        UnityEngine.Profiling.Profiler.logFile = path;
        UnityEngine.Profiling.Profiler.enableBinaryLog = true;
        UnityEngine.Profiling.Profiler.maxUsedMemory = 256 * 1024 * 1024;
        UnityEngine.Profiling.Profiler.enabled = true;
        report.AppendLine($"profiling from t={Time.time - startedAt:F0}s units={UnitHealthManager.Active.Count}");
        float until = Time.time + 60f;
        while (Time.time < until) yield return null;
        UnityEngine.Profiling.Profiler.enabled = false;
        UnityEngine.Profiling.Profiler.logFile = "";
        report.AppendLine($"profiling done t={Time.time - startedAt:F0}s units={UnitHealthManager.Active.Count} particles={FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None).Length} lights={FindObjectsByType<Light>(FindObjectsSortMode.None).Length} lines={FindObjectsByType<LineRenderer>(FindObjectsSortMode.None).Length} rigidbodies={FindObjectsByType<Rigidbody>(FindObjectsSortMode.None).Length}");
        Finish();
    }

    private IEnumerator BalanceRun()
    {
        float scale = float.TryParse(Argument("-timeScale", "3"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float ts) ? ts : 3f;
        Application.targetFrameRate = -1;
        Time.timeScale = scale;
        startedAt = Time.time;
        DamageLedger.Enabled = true;
        DamageLedger.StartedAt = Time.time;
        BattleContext context = BattleContext.Instance;
        int CostOf(IEnumerable<ShipEntry> roster) => roster.Sum(e => e != null && e.type != null ? e.type.cost : 0);
        int popH = context.attackerRoster.Sum(e => e.type != null ? e.type.populationCost : 0), popA = context.defenderRoster.Sum(e => e.type != null ? e.type.populationCost : 0);
        report.AppendLine($"BALANCE {balance}  timeScale {scale}  humans cost {CostOf(context.attackerRoster)} pop {popH}  avanti cost {CostOf(context.defenderRoster)} pop {popA}");

        // Every warship and station that takes part (strike craft aside), and when it died.
        Dictionary<UnitHealthManager, float> seen = new Dictionary<UnitHealthManager, float>();
        Dictionary<UnitHealthManager, (string name, ShipType type, int cost, bool attacker, bool free)> info = new Dictionary<UnitHealthManager, (string, ShipType, int, bool, bool)>();
        float limit = 1500f;
        while (Time.time - startedAt < limit)
        {
            foreach (UnitHealthManager u in UnitHealthManager.Active)
            {
                if (u == null || u.GetComponent<Squadron>() != null || info.ContainsKey(u)) continue;
                SpaceUnit su = u.GetComponent<SpaceUnit>();
                info[u] = (u.IsDefense ? u.name.Replace("(Clone)", "") : (u.sourceShip != null ? u.sourceShip.name : u.name), su != null ? su.shipType : ShipType.Station,
                    u.sourceShip != null ? u.sourceShip.cost : 0, u.isAttackerSide, u.IsFreeUnit && !u.IsDefense);
            }
            foreach (var pair in info) if (!seen.ContainsKey(pair.Key) && (pair.Key == null || pair.Key.IsDead)) seen[pair.Key] = Time.time - startedAt;
            if (GameManager.CombatOver) break;
            yield return new WaitForSeconds(1f);
        }
        float end = Time.time - startedAt;
        bool retreatH = RetreatManager.Instance != null && RetreatManager.Instance.IsRetreating(true);
        bool retreatA = RetreatManager.Instance != null && RetreatManager.Instance.IsRetreating(false);
        report.AppendLine($"ended t={end:F0}s (game)  combatOver {GameManager.CombatOver}  humans retreating {retreatH}  avanti retreating {retreatA}");
        foreach (bool attacker in new[] { true, false })
        {
            var mine = info.Where(p => p.Value.attacker == attacker).ToList();
            int deployedCost = mine.Sum(p => p.Value.cost), lostCost = mine.Where(p => seen.ContainsKey(p.Key)).Sum(p => p.Value.cost);
            float hpLeft = 0f, hpMax = 0f;
            foreach (var p in mine) if (p.Key != null) { hpMax += p.Key.maxHealth; if (!p.Key.IsDead) hpLeft += p.Key.currentHealth; }
            report.AppendLine($"  {(attacker ? "HUMANS" : "AVANTI")}: lost {mine.Count(p => seen.ContainsKey(p.Key))}/{mine.Count} units, cost lost {lostCost}/{deployedCost} ({(deployedCost > 0 ? 100f * lostCost / deployedCost : 0f):F0}%), hull left {(hpMax > 0 ? 100f * hpLeft / hpMax : 0f):F0}%");
            foreach (var g in mine.GroupBy(p => (p.Value.name, p.Value.free)).OrderBy(g => g.Key.name))
            {
                var deaths = g.Where(p => seen.ContainsKey(p.Key)).Select(p => seen[p.Key]).OrderBy(t => t).ToList();
                report.AppendLine($"     {g.Key.name}{(g.Key.free ? " (shipyard guard)" : "")}: {deaths.Count}/{g.Count()} lost{(deaths.Count > 0 ? " at " + string.Join(", ", deaths.Select(t => $"{t:F0}s")) : "")}");
            }
        }
        report.AppendLine(DamageLedger.Report());
        Time.timeScale = 1f;
    }

    private static bool HasFlag(string name) => Array.IndexOf(Environment.GetCommandLineArgs(), name) >= 0;

    private static string Argument(string name, string fallback)
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++) if (args[i] == name) return args[i + 1];
        return fallback;
    }
}
