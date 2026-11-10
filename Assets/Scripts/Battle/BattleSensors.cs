using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

// Who can see whom in a battle.
//   - Sensor coverage: a side fielding only corvettes and strike craft sees just what's inside their sensor
//     range. The moment it fields a destroyer or anything larger (or has a station/shipyard), its sensors
//     cover the whole battlefield.
//   - Concealment: corvettes and strike craft inside an asteroid field are hidden until an enemy ship comes
//     within RevealDistance - sensors or not. Ambushes are built on this (the AI uses it too).
// Both sides play by the same rules: a hidden ship can't be targeted. Ships hidden from the player
// disappear from view (models, icons, health bars, height markers) until spotted.
public class BattleSensors : MonoBehaviour
{
    public const float RevealDistance = 110f;
    public const float FireRevealTime = 3f; // Opening fire gives a concealed ship away for this long.
    public const float CorvetteSensorRange = 260f;
    public const float CraftSensorRange = 220f;
    private const float UpdateInterval = 0.25f;

    private static BattleSensors instance;
    private static readonly HashSet<UnitHealthManager> hiddenFromAttacker = new HashSet<UnitHealthManager>();
    private static readonly HashSet<UnitHealthManager> hiddenFromDefender = new HashSet<UnitHealthManager>();

    public static bool AttackerFullSight { get; private set; } = true;
    public static bool DefenderFullSight { get; private set; } = true;

    private readonly Dictionary<UnitHealthManager, SensorVisibility> visuals = new Dictionary<UnitHealthManager, SensorVisibility>();
    private readonly List<UnitHealthManager> attackers = new List<UnitHealthManager>();
    private readonly List<UnitHealthManager> defenders = new List<UnitHealthManager>();
    private float nextUpdateAt;
    private TMP_Text notice;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Hook()
    {
        SceneManager.sceneLoaded += (scene, _) => AttachTo(scene);
        AttachTo(SceneManager.GetActiveScene());
    }

    private static void AttachTo(Scene scene)
    {
        if (scene.name != "BattleScene" || instance != null) return;
        instance = new GameObject("BattleSensors").AddComponent<BattleSensors>();
    }

    // Can a unit on `viewerAttackerSide` see (and so target) this unit?
    public static bool CanSee(bool viewerAttackerSide, UnitHealthManager unit)
    {
        if (unit == null) return false;
        return !(viewerAttackerSide ? hiddenFromAttacker : hiddenFromDefender).Contains(unit);
    }

    public static bool CanSee(Component viewer, UnitHealthManager unit)
    {
        UnitHealthManager self = viewer != null ? viewer.GetComponentInParent<UnitHealthManager>() : null;
        return self == null || CanSee(self.isAttackerSide, unit);
    }

    public static bool PlayerCanSee(UnitHealthManager unit)
    {
        bool playerAttacker = GameManager.Instance == null || GameManager.Instance.PlayerIsAttacker;
        return CanSee(playerAttacker, unit);
    }

    private void Awake()
    {
        hiddenFromAttacker.Clear();
        hiddenFromDefender.Clear();
        AttackerFullSight = DefenderFullSight = true;

        Canvas canvas = UIKit.Canvas("SensorCanvas", 36);
        notice = UIKit.Text(canvas.transform, "", 15f, new Color(1f, 0.75f, 0.35f), TextAlignmentOptions.Top);
        notice.rectTransform.offsetMax = new Vector2(0f, -96f);
        notice.characterSpacing = 4f;
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
        hiddenFromAttacker.Clear();
        hiddenFromDefender.Clear();
    }

    private void Update()
    {
        if (Time.time < nextUpdateAt) return;
        nextUpdateAt = Time.time + UpdateInterval;

        attackers.Clear();
        defenders.Clear();
        foreach (UnitHealthManager unit in UnitHealthManager.Active)
        {
            if (unit == null || unit.IsDead) continue;
            (unit.isAttackerSide ? attackers : defenders).Add(unit);
        }

        AttackerFullSight = HasFullSight(attackers);
        DefenderFullSight = HasFullSight(defenders);

        Recompute(defenders, attackers, AttackerFullSight, hiddenFromAttacker);
        Recompute(attackers, defenders, DefenderFullSight, hiddenFromDefender);

        ApplyPlayerView();
    }

    // Destroyers and up, and stations/shipyards, give full battlefield coverage.
    private static bool HasFullSight(List<UnitHealthManager> side)
    {
        foreach (UnitHealthManager unit in side)
        {
            if (unit.IsDefense) return true;
            if (unit.GetComponent<Squadron>() != null) continue;
            SpaceUnit spaceUnit = unit.GetComponent<SpaceUnit>();
            if (spaceUnit == null) continue;
            if (spaceUnit.shipType != ShipType.Corvette && spaceUnit.shipType != ShipType.Fighter) return true;
        }
        return false;
    }

    private static void Recompute(List<UnitHealthManager> targets, List<UnitHealthManager> viewers, bool fullSight, HashSet<UnitHealthManager> hidden)
    {
        hidden.Clear();
        foreach (UnitHealthManager target in targets)
        {
            if (target.IsDefense) continue; // Stations and shipyards are never hidden.
            Vector3 position = target.transform.position;
            float nearest = NearestViewer(viewers, position, out float margin);

            bool firedRecently = Time.time - target.LastFiredAt < FireRevealTime;
            bool concealed = Concealable(target) && !firedRecently && BattleTerrain.Instance != null && BattleTerrain.Instance.InAsteroids(position) && nearest > RevealDistance;
            bool outOfSensors = !fullSight && margin > 0f;
            if (concealed || outOfSensors) hidden.Add(target);
        }
    }

    private static bool Concealable(UnitHealthManager unit)
    {
        if (unit.GetComponent<Squadron>() != null) return true;
        SpaceUnit spaceUnit = unit.GetComponent<SpaceUnit>();
        return spaceUnit != null && (spaceUnit.shipType == ShipType.Corvette || spaceUnit.shipType == ShipType.Fighter);
    }

    // Distance to the nearest viewer; `margin` is how far outside the best-placed viewer's sensor range the
    // target is (negative = inside someone's range).
    private static float NearestViewer(List<UnitHealthManager> viewers, Vector3 position, out float margin)
    {
        float nearest = float.MaxValue;
        margin = float.MaxValue;
        foreach (UnitHealthManager viewer in viewers)
        {
            float distance = Vector3.Distance(viewer.transform.position, position);
            float reach = viewer.GetComponent<Squadron>() != null ? CraftSensorRange : CorvetteSensorRange;
            nearest = Mathf.Min(nearest, distance);
            margin = Mathf.Min(margin, distance - reach);
        }
        return nearest;
    }

    private void ApplyPlayerView()
    {
        bool playerAttacker = GameManager.Instance == null || GameManager.Instance.PlayerIsAttacker;
        HashSet<UnitHealthManager> hidden = playerAttacker ? hiddenFromAttacker : hiddenFromDefender;

        foreach (UnitHealthManager unit in UnitHealthManager.Active)
        {
            if (unit == null || unit.isAttackerSide == playerAttacker) continue; // Our own ships are always shown.
            bool show = !hidden.Contains(unit);
            if (!visuals.TryGetValue(unit, out SensorVisibility visibility))
            {
                if (show) continue; // Never hidden so far - nothing to do.
                visibility = unit.gameObject.AddComponent<SensorVisibility>();
                visuals[unit] = visibility;
            }
            visibility.SetVisible(show);
        }

        bool fullSight = playerAttacker ? AttackerFullSight : DefenderFullSight;
        notice.text = fullSight ? "" : "LIMITED SENSORS  -  deploy a destroyer or larger for full battlefield coverage";
    }
}

// Hides a ship from the player's view without disturbing anything else that toggles its parts: models via
// forceRenderingOff, world-space UI via a CanvasGroup, and the height marker via ShipAltitude.
public class SensorVisibility : MonoBehaviour
{
    public bool Visible { get; private set; } = true;

    public void SetVisible(bool visible)
    {
        if (visible == Visible) return;
        Visible = visible;

        foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
        {
            renderer.forceRenderingOff = !visible;
        }
        foreach (Canvas canvas in GetComponentsInChildren<Canvas>(true))
        {
            if (!canvas.TryGetComponent(out CanvasGroup group)) group = canvas.gameObject.AddComponent<CanvasGroup>();
            group.alpha = visible ? 1f : 0f;
            group.blocksRaycasts = visible;
        }
        if (TryGetComponent(out ShipAltitude altitude)) altitude.HiddenBySensors = !visible;
    }
}
