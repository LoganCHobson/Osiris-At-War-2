using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

// Skirmish: a one-off battle from the main menu. Pick your navy, the enemy and its commander, and whether you
// attack or defend. Each side's fleet is either rolled to a budget (FleetBuilder, the same way the self-play
// farm builds them, with a reroll) or built by hand, ship by ship, from that navy's roster. The defender can
// have a battle station and a shipyard of any tier, and the map's asteroids, nebulae and ion storm can be set
// or left to chance. The battle runs in the normal battle scene through BattleContext.BeginSkirmish and comes
// back to the main menu.
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

    private static readonly string[] FleetModes = { "Rolled to the fleet size", "Built by hand" };
    private static readonly string[] Structures =
    {
        "None", "Battle station", "Light Frigate Yard", "Heavy Cruiser Yard", "Capital Shipyard",
        "Station + Light Frigate Yard", "Station + Heavy Cruiser Yard", "Station + Capital Shipyard",
    };
    private static readonly string[] AsteroidOptions = { "Random", "None", "A few", "Many" };
    private static readonly string[] NebulaOptions = { "Random", "None", "One", "Two" };
    private static readonly string[] StormOptions = { "Random", "None", "One" };
    private const int MaxOfOne = 20;

    private const string Prefix = "osiris.skirmish.";

    public static bool IsOpen => current != null;
    private static SkirmishSetup current;

    private List<Faction> factions;
    private Faction player, enemy;
    private int commander, size, role; // role 0 = attacking, 1 = defending
    private int playerMode, enemyMode;  // 0 rolled, 1 built by hand
    private int structures, asteroids, nebulae, storm;
    private List<Ship> playerFleet = new List<Ship>(), enemyFleet = new List<Ship>();
    private readonly Dictionary<Ship, int> playerCustom = new Dictionary<Ship, int>(), enemyCustom = new Dictionary<Ship, int>();

    private RectTransform enemyControl, playerColumn, enemyColumn;
    private TMP_Text commanderNote;
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
        commander = Remembered("commander", 2, Commanders.Length);
        size = Remembered("size", 1, Sizes.Length);
        role = Remembered("role", 0, 2);
        playerMode = Remembered("playerMode", 0, 2);
        enemyMode = Remembered("enemyMode", 0, 2);
        structures = Remembered("structures", 0, Structures.Length);
        asteroids = Remembered("asteroids", 0, AsteroidOptions.Length);
        nebulae = Remembered("nebulae", 0, NebulaOptions.Length);
        storm = Remembered("storm", 0, StormOptions.Length);

        UIKit.Image(root, UIKit.Dim, true);
        RectTransform window = UIKit.Rect("Window", root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1720f, 1040f));
        UIKit.Image(window, UIKit.PanelColor, true);

        RectTransform header = UIKit.Rect("Header", window, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 80f));
        TMP_Text heading = UIKit.Text(header, "SKIRMISH", 40f, UIKit.TextColor, TextAlignmentOptions.Center);
        heading.characterSpacing = 12f;
        heading.fontStyle = FontStyles.Bold;

        // Two columns of choices: the sides on the left, the battlefield on the right.
        RectTransform left = UIKit.Rect("Sides", window, new Vector2(0f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, 1f), new Vector2(50f, -80f), new Vector2(-70f, 300f));
        UIKit.List(left, 8f);
        RectTransform right = UIKit.Rect("Battlefield", window, new Vector2(0.5f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f), new Vector2(20f, -80f), new Vector2(-70f, 300f));
        UIKit.List(right, 8f);

        UIKit.Selector(UIKit.Row(left, "YOUR NAVY", 52f), factions.Select(f => f.factionName).ToList(), factions.IndexOf(player), i =>
        {
            player = factions[i];
            playerCustom.Clear();
            if (enemy == player) { enemy = EnemyOptions().FirstOrDefault(); enemyCustom.Clear(); }
            BuildEnemySelector();
            Changed();
        });
        enemyControl = UIKit.Row(left, "ENEMY", 52f);
        BuildEnemySelector();
        UIKit.Selector(UIKit.Row(left, "ENEMY COMMANDER", 52f), Commanders.Select(Difficulty.DisplayName).ToList(), commander, i =>
        {
            commander = i;
            Changed(rebuildFleets: false);
        });
        UIKit.Selector(UIKit.Row(left, "YOU ARE", 52f), new[] { "Attacking", "Defending" }, role, i =>
        {
            role = i;
            Changed(rebuildFleets: false);
        });
        UIKit.Selector(UIKit.Row(left, "ROLLED FLEET SIZE", 52f), Sizes.Select(s => s.name).ToList(), size, i =>
        {
            size = i;
            Changed();
        });

        UIKit.Selector(UIKit.Row(right, "DEFENDER HAS", 52f), Structures, structures, i => { structures = i; Changed(rebuildFleets: false); });
        UIKit.Selector(UIKit.Row(right, "ASTEROIDS", 52f), AsteroidOptions, asteroids, i => asteroids = i);
        UIKit.Selector(UIKit.Row(right, "NEBULAE", 52f), NebulaOptions, nebulae, i => nebulae = i);
        UIKit.Selector(UIKit.Row(right, "ION STORM", 52f), StormOptions, storm, i => storm = i);

        RectTransform noteArea = UIKit.Rect("CommanderNote", window, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -385f), new Vector2(-100f, 44f));
        commanderNote = UIKit.Text(noteArea, "", 20f, UIKit.MutedColor);
        commanderNote.textWrappingMode = TextWrappingModes.Normal;

        // The two fleets, side by side: each a rolled list, or a hand-built order sheet.
        RectTransform fleets = UIKit.Rect("Fleets", window, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -435f), new Vector2(-100f, 500f));
        playerColumn = UIKit.Rect("YourFleet", fleets, new Vector2(0f, 0f), new Vector2(0.49f, 1f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        enemyColumn = UIKit.Rect("EnemyFleet", fleets, new Vector2(0.51f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);

        RectTransform footer = UIKit.Rect("Footer", window, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 22f), new Vector2(-60f, 60f));
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
            enemyCustom.Clear();
            Changed();
        });
    }

    // Rolls the rolled fleets (rebuildFleets), refreshes the hand-built ones from their counts, redraws both.
    private void Changed(bool rebuildFleets = true)
    {
        // A hand-built fleet with nothing in it yet starts from a rolled one.
        if (playerMode == 1 && playerCustom.Count == 0) Seed(playerCustom, player);
        if (enemyMode == 1 && enemyCustom.Count == 0) Seed(enemyCustom, enemy);
        if (rebuildFleets || playerFleet.Count == 0) playerFleet = playerMode == 0 ? FleetBuilder.EqualCost(player, Sizes[size].budget) : FromCounts(playerCustom);
        if (rebuildFleets || enemyFleet.Count == 0) enemyFleet = enemyMode == 0 ? FleetBuilder.EqualCost(enemy, Sizes[size].budget) : FromCounts(enemyCustom);
        DifficultyLevel level = Commanders[commander];
        commanderNote.text = level == DifficultyLevel.GrandAdmiral && enemy != null && !enemy.CanBeGrandAdmiral
            ? $"The Grand Admiral commands Avanti ships only: the {enemy.factionName} will fight as Admiral."
            : Difficulty.Describe(level);
        DrawColumn(playerColumn, true);
        DrawColumn(enemyColumn, false);
    }

    private void Seed(Dictionary<Ship, int> counts, Faction faction)
    {
        if (faction == null) return;
        foreach (IGrouping<Ship, Ship> group in FleetBuilder.EqualCost(faction, Sizes[size].budget).GroupBy(s => s)) counts[group.Key] = group.Count();
    }

    private static List<Ship> FromCounts(Dictionary<Ship, int> counts)
    {
        List<Ship> fleet = new List<Ship>();
        foreach (KeyValuePair<Ship, int> pair in counts) for (int i = 0; i < pair.Value; i++) fleet.Add(pair.Key);
        return fleet;
    }

    private string Summary(bool mine)
    {
        Faction faction = mine ? player : enemy;
        List<Ship> fleet = mine ? playerFleet : enemyFleet;
        bool attacking = mine ? role == 0 : role == 1;
        string defends = !attacking && structures > 0 ? $", with {Structures[structures].ToLower()}" : "";
        string colour = faction != null ? ColorUtility.ToHtmlStringRGB(faction.color) : "FFFFFF";
        return $"<b>{(mine ? "YOUR FLEET" : "ENEMY FLEET")}</b>  <color=#{colour}>{faction?.factionName}</color>\n"
               + $"<size=85%><color=#FFFFFF8C>{(attacking ? "attacking" : "defending")}{defends}  -  {FleetBuilder.Cost(fleet):N0} cr, "
               + $"{fleet.Sum(s => s.populationCost)} pop, {fleet.Count} ships</color></size>";
    }

    private void DrawColumn(RectTransform column, bool mine)
    {
        foreach (Transform child in column) Destroy(child.gameObject);
        if (column.GetComponent<UnityEngine.UI.Image>() == null) UIKit.Image(column, UIKit.RowColor);
        Faction faction = mine ? player : enemy;
        List<Ship> fleet = mine ? playerFleet : enemyFleet;
        int mode = mine ? playerMode : enemyMode;

        RectTransform modeRow = UIKit.Rect("Mode", column, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -8f), new Vector2(-24f, 44f));
        UIKit.Selector(modeRow, FleetModes, mode, i =>
        {
            if (mine) playerMode = i; else enemyMode = i;
            Dictionary<Ship, int> counts = mine ? playerCustom : enemyCustom;
            if (i == 1 && counts.Count == 0)
                foreach (IGrouping<Ship, Ship> group in (mine ? playerFleet : enemyFleet).GroupBy(s => s)) counts[group.Key] = group.Count(); // start from the rolled fleet
            if (mine) playerFleet = i == 0 ? FleetBuilder.EqualCost(player, Sizes[size].budget) : FromCounts(playerCustom);
            else enemyFleet = i == 0 ? FleetBuilder.EqualCost(enemy, Sizes[size].budget) : FromCounts(enemyCustom);
            DrawColumn(column, mine);
        });

        RectTransform titleArea = UIKit.Rect("Title", column, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -56f), new Vector2(-40f, 56f));
        TMP_Text title = UIKit.Text(titleArea, Summary(mine), 22f, UIKit.TextColor, TextAlignmentOptions.TopLeft);
        title.textWrappingMode = TextWrappingModes.Normal;

        RectTransform body = UIKit.Rect("Ships", column, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        body.offsetMin = new Vector2(20f, 12f);
        body.offsetMax = new Vector2(-20f, -118f);

        if (mode == 0)
        {
            TMP_Text text = UIKit.Text(body, "", 22f, UIKit.TextColor, TextAlignmentOptions.TopLeft);
            text.textWrappingMode = TextWrappingModes.Normal;
            text.text = string.Join("\n", fleet.GroupBy(s => s).OrderByDescending(g => g.Key.cost)
                .Select(g => $"{g.Count()} x {ShipName(g.Key)}"));
            return;
        }

        // Built by hand: one line per ship the navy builds, with how many.
        UIKit.List(body, 4f);
        Dictionary<Ship, int> counts = mine ? playerCustom : enemyCustom;
        foreach (Ship ship in Roster(faction))
        {
            RectTransform row = UIKit.Rect(ShipName(ship), body, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 40f));
            row.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().preferredHeight = 40f;
            TMP_Text label = UIKit.Text(row, $"{ShipName(ship)}  <size=80%><color=#FFFFFF73>{ship.cost:N0} cr, {ship.populationCost} pop</color></size>", 20f, UIKit.TextColor, TextAlignmentOptions.Left);
            label.rectTransform.anchorMax = new Vector2(0.62f, 1f);
            RectTransform control = UIKit.Rect("Count", row, new Vector2(0.62f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            counts.TryGetValue(ship, out int have);
            UIKit.Selector(control, Enumerable.Range(0, MaxOfOne + 1).Select(n => n.ToString()).ToList(), have, n =>
            {
                counts[ship] = n;
                if (mine) playerFleet = FromCounts(counts); else enemyFleet = FromCounts(counts);
                title.text = Summary(mine);
            });
        }
    }

    // What a navy fields, biggest first, each kind once.
    private static IEnumerable<Ship> Roster(Faction faction) =>
        faction == null ? Enumerable.Empty<Ship>() : faction.roster.Where(s => s != null).Distinct().OrderByDescending(s => s.cost);

    private static string ShipName(Ship ship) => string.IsNullOrEmpty(ship.displayName) ? ship.name : ship.displayName;

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

    private static int Remembered(string key, int fallback, int count) =>
        Mathf.Clamp(PlayerPrefs.GetInt(Prefix + key, fallback), 0, count - 1);

    private void StartBattle()
    {
        if (player == null || enemy == null || playerFleet.Count == 0 || enemyFleet.Count == 0)
        {
            Toast.Show("Both sides need a fleet.");
            return;
        }
        PlayerPrefs.SetString(Prefix + "player", SaveRegistry.Id(player));
        PlayerPrefs.SetString(Prefix + "enemy", SaveRegistry.Id(enemy));
        foreach ((string key, int value) in new[] { ("commander", commander), ("size", size), ("role", role), ("playerMode", playerMode),
                     ("enemyMode", enemyMode), ("structures", structures), ("asteroids", asteroids), ("nebulae", nebulae), ("storm", storm) })
            PlayerPrefs.SetInt(Prefix + key, value);
        PlayerPrefs.Save();

        BattleContext context = BattleContext.Instance;
        if (context == null) context = new GameObject("BattleContext").AddComponent<BattleContext>();
        context.BeginSkirmish(player, enemy, role == 0, Commanders[commander], playerFleet, enemyFleet);

        // The defender's structures and the battlefield (after BeginSkirmish, which clears the context).
        bool station = structures == 1 || structures >= 5;
        int yard = structures >= 5 ? structures - 4 : structures >= 2 ? structures - 1 : 0;
        context.defenderHasBattleStation = station;
        context.defenderHasShipyard = yard > 0;
        context.defenderShipyardTier = yard;
        context.skirmishAsteroids = asteroids - 1;
        context.skirmishNebulae = nebulae - 1;
        context.skirmishIonStorm = storm - 1;

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
