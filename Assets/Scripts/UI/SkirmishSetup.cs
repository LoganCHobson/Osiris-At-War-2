using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

// Skirmish: a one-off battle from the main menu. Pick your navy, the enemy and its commander, the fleet
// size and whether you attack or defend; both fleets are built to the same budget (FleetBuilder, the
// same way the self-play farm builds them) and shown before you start, with a reroll. The battle runs
// in the normal battle scene through BattleContext.BeginSkirmish and comes back to the main menu.
public class SkirmishSetup : MonoBehaviour
{
    private static readonly (string name, int budget)[] Sizes =
    {
        ("Patrol  (1,500 cr)", 1500),
        ("Battle group  (2,500 cr)", 2500),
        ("Task force  (4,000 cr)", 4000),
        ("Armada  (6,000 cr)", 6000),
    };

    private static readonly DifficultyLevel[] Commanders =
        { DifficultyLevel.Cadet, DifficultyLevel.Captain, DifficultyLevel.Admiral, DifficultyLevel.GrandAdmiral };

    private const string Prefix = "osiris.skirmish.";

    public static bool IsOpen => current != null;
    private static SkirmishSetup current;

    private List<Faction> factions;
    private Faction player, enemy;
    private int commander, size, role; // role 0 = attacking, 1 = defending
    private List<Ship> playerFleet = new List<Ship>(), enemyFleet = new List<Ship>();

    private RectTransform enemyControl;
    private TMP_Text commanderNote, playerFleetText, enemyFleetText;
    private UnityAction onClose;
    private UnityAction<string> leaveTo;

    // leaveTo loads a scene with the menu's fade; onClose is called on BACK.
    public static void Open(Transform parent, UnityAction onClose, UnityAction<string> leaveTo)
    {
        if (current != null) return;
        RectTransform root = UIKit.Fill("Skirmish", parent);
        current = root.gameObject.AddComponent<SkirmishSetup>();
        current.onClose = onClose;
        current.leaveTo = leaveTo;
        current.Build(root);
    }

    private void OnDestroy()
    {
        if (current == this) current = null;
    }

    private void Build(RectTransform root)
    {
        // Every navy that fights: the neutral holdouts never field a fleet of their own.
        factions = SaveRegistry.Factions.Where(f => f != null && !f.isNeutral && f.roster != null && f.roster.Count > 0).ToList();
        player = Remembered("player", factions.FirstOrDefault(f => f.isPlayerFaction) ?? factions.FirstOrDefault());
        enemy = Remembered("enemy", null);
        if (enemy == null || enemy == player) enemy = EnemyOptions().FirstOrDefault();
        commander = Mathf.Clamp(PlayerPrefs.GetInt(Prefix + "commander", 2), 0, Commanders.Length - 1);
        size = Mathf.Clamp(PlayerPrefs.GetInt(Prefix + "size", 1), 0, Sizes.Length - 1);
        role = Mathf.Clamp(PlayerPrefs.GetInt(Prefix + "role", 0), 0, 1);

        UIKit.Image(root, UIKit.Dim, true);
        RectTransform window = UIKit.Rect("Window", root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1240f, 960f));
        UIKit.Image(window, UIKit.PanelColor, true);

        RectTransform header = UIKit.Rect("Header", window, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 100f));
        TMP_Text heading = UIKit.Text(header, "SKIRMISH", 42f, UIKit.TextColor, TextAlignmentOptions.Center);
        heading.characterSpacing = 12f;
        heading.fontStyle = FontStyles.Bold;

        RectTransform options = UIKit.Rect("Options", window, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -100f), new Vector2(-120f, 360f));
        UIKit.List(options, 10f);

        UIKit.Selector(UIKit.Row(options, "YOUR NAVY"), factions.Select(f => f.factionName).ToList(), factions.IndexOf(player), i =>
        {
            player = factions[i];
            if (enemy == player) enemy = EnemyOptions().FirstOrDefault();
            BuildEnemySelector();
            Changed();
        });
        enemyControl = UIKit.Row(options, "ENEMY");
        BuildEnemySelector();
        UIKit.Selector(UIKit.Row(options, "ENEMY COMMANDER"), Commanders.Select(Difficulty.DisplayName).ToList(), commander, i =>
        {
            commander = i;
            Changed(rebuildFleets: false);
        });
        UIKit.Selector(UIKit.Row(options, "FLEET SIZE"), Sizes.Select(s => s.name).ToList(), size, i =>
        {
            size = i;
            Changed();
        });
        UIKit.Selector(UIKit.Row(options, "YOU ARE"), new[] { "Attacking", "Defending" }, role, i =>
        {
            role = i;
            Changed(rebuildFleets: false);
        });

        RectTransform noteArea = UIKit.Rect("CommanderNote", window, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -470f), new Vector2(-120f, 60f));
        commanderNote = UIKit.Text(noteArea, "", 22f, UIKit.MutedColor);
        commanderNote.textWrappingMode = TextWrappingModes.Normal;

        // The two fleets, side by side.
        RectTransform fleets = UIKit.Rect("Fleets", window, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -540f), new Vector2(-120f, 300f));
        playerFleetText = FleetColumn(fleets, 0f, 0.49f);
        enemyFleetText = FleetColumn(fleets, 0.51f, 1f);

        RectTransform footer = UIKit.Rect("Footer", window, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 24f), new Vector2(-60f, 60f));
        Button(footer, "BACK", 0f, 240f, Close);
        Button(footer, "REROLL FLEETS", 0.5f, 360f, () => Changed());
        Button(footer, "START BATTLE", 1f, 360f, StartBattle);

        Changed();
    }

    private IEnumerable<Faction> EnemyOptions() => factions.Where(f => f != player);

    private void BuildEnemySelector()
    {
        foreach (Transform child in enemyControl) Destroy(child.gameObject);
        List<Faction> options = EnemyOptions().ToList();
        UIKit.Selector(enemyControl, options.Select(f => f.factionName).ToList(), Mathf.Max(0, options.IndexOf(enemy)), i =>
        {
            enemy = options[i];
            Changed();
        });
    }

    private void Changed(bool rebuildFleets = true)
    {
        if (rebuildFleets)
        {
            playerFleet = FleetBuilder.EqualCost(player, Sizes[size].budget);
            enemyFleet = FleetBuilder.EqualCost(enemy, Sizes[size].budget);
        }
        DifficultyLevel level = Commanders[commander];
        commanderNote.text = level == DifficultyLevel.GrandAdmiral && enemy != null && !enemy.CanBeGrandAdmiral
            ? $"The Grand Admiral commands Avanti ships only: the {enemy.factionName} will fight as Admiral."
            : Difficulty.Describe(level);
        playerFleetText.text = Describe("YOUR FLEET", player, playerFleet, role == 0 ? "attacking" : "defending");
        enemyFleetText.text = Describe("ENEMY FLEET", enemy, enemyFleet, role == 0 ? "defending" : "attacking");
    }

    private static string Describe(string title, Faction faction, List<Ship> fleet, string stance)
    {
        string colour = faction != null ? ColorUtility.ToHtmlStringRGB(faction.color) : "FFFFFF";
        string lines = string.Join("\n", fleet.GroupBy(s => s)
            .OrderByDescending(g => g.Key.cost)
            .Select(g => $"{g.Count()} x {(string.IsNullOrEmpty(g.Key.displayName) ? g.Key.name : g.Key.displayName)}"));
        return $"<b>{title}</b>  <color=#{colour}>{faction?.factionName}</color>  <size=80%><color=#FFFFFF73>{stance}, "
               + $"{FleetBuilder.Cost(fleet):N0} cr, {fleet.Count} ships</color></size>\n\n{lines}";
    }

    private static TMP_Text FleetColumn(RectTransform parent, float from, float to)
    {
        RectTransform column = UIKit.Rect("Fleet", parent, new Vector2(from, 0f), new Vector2(to, 1f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        UIKit.Image(column, UIKit.RowColor);
        TMP_Text text = UIKit.Text(column, "", 22f, UIKit.TextColor, TextAlignmentOptions.TopLeft);
        text.rectTransform.offsetMin = new Vector2(20f, 14f);
        text.rectTransform.offsetMax = new Vector2(-20f, -14f);
        text.textWrappingMode = TextWrappingModes.Normal;
        return text;
    }

    private static void Button(RectTransform footer, string label, float anchorX, float width, UnityAction onClick)
    {
        UnityEngine.UI.Button button = UIKit.MenuButton(footer, label, 32f, new Vector2(width, 60f), onClick);
        RectTransform rect = (RectTransform)button.transform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(anchorX, 0f);
        rect.anchoredPosition = Vector2.zero;
    }

    private Faction Remembered(string key, Faction fallback)
    {
        string id = PlayerPrefs.GetString(Prefix + key, "");
        return factions.FirstOrDefault(f => SaveRegistry.Id(f) == id) ?? fallback;
    }

    private void StartBattle()
    {
        if (player == null || enemy == null || playerFleet.Count == 0 || enemyFleet.Count == 0)
        {
            Toast.Show("Both sides need a fleet.");
            return;
        }
        PlayerPrefs.SetString(Prefix + "player", SaveRegistry.Id(player));
        PlayerPrefs.SetString(Prefix + "enemy", SaveRegistry.Id(enemy));
        PlayerPrefs.SetInt(Prefix + "commander", commander);
        PlayerPrefs.SetInt(Prefix + "size", size);
        PlayerPrefs.SetInt(Prefix + "role", role);
        PlayerPrefs.Save();

        BattleContext context = BattleContext.Instance;
        if (context == null) context = new GameObject("BattleContext").AddComponent<BattleContext>();
        context.BeginSkirmish(player, enemy, role == 0, Commanders[commander], playerFleet, enemyFleet);
        gameObject.SetActive(false);
        leaveTo?.Invoke("BattleScene");
    }

    private void Close()
    {
        UnityAction closed = onClose;
        Destroy(gameObject);
        current = null;
        closed?.Invoke();
    }
}
