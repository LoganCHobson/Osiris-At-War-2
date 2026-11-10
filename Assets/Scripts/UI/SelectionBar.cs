using System.Linq;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Battle HUD strip along the bottom showing every selected ship as a tile with its icon and live hull bar.
//   Left-click a tile          - select only that ship
//   Ctrl+click a tile          - select only the ships of that class that are already selected
//   Right-click / Shift+click  - drop that ship from the selection
// Hovering a tile slides in the ship's info blade. Added automatically when the battle scene loads.
public class SelectionBar : MonoBehaviour
{
    private const float TileWidth = 58f;
    private const float TileHeight = 66f;
    private const float Spacing = 4f;
    private const int MaxColumns = 16;
    private const int MaxRows = 3;
    private const float BottomOffset = 92f; // Sits just above the command bar.
    private const float RefreshInterval = 0.15f;

    private static readonly Color TileColor = new Color(1f, 1f, 1f, 0.06f);
    private static readonly Color HullHigh = new Color(0.35f, 0.95f, 0.45f);
    private static readonly Color HullLow = new Color(1f, 0.3f, 0.25f);

    private PlayerSpaceManager manager;
    private RectTransform panel;
    private RectTransform grid;
    private TMP_Text header;
    private readonly List<Tile> tiles = new List<Tile>();
    private readonly List<SpaceUnit> shown = new List<SpaceUnit>();
    private float nextRefreshAt;

    private class Tile
    {
        public RectTransform root;
        public Image icon;
        public RectTransform hullFill;
        public Image hullImage;
        public TMP_Text overflow;
        public TMP_Text height;
        public TileClick click;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Hook()
    {
        SceneManager.sceneLoaded += (scene, _) => AttachTo(scene);
        AttachTo(SceneManager.GetActiveScene());
    }

    private static void AttachTo(Scene scene)
    {
        if (scene.name != "BattleScene" || FindFirstObjectByType<SelectionBar>() != null) return;
        Canvas canvas = UIKit.Canvas("SelectionBarCanvas", 40);
        canvas.gameObject.AddComponent<SelectionBar>().Build(canvas.transform);
    }

    private void Build(Transform canvas)
    {
        panel = UIKit.Rect("SelectionBar", canvas, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, BottomOffset), new Vector2(200f, 100f));
        UIKit.Image(panel, UIKit.PanelColor, true);

        header = UIKit.Text(panel, "", 14f, UIKit.MutedColor, TextAlignmentOptions.TopLeft);
        RectTransform headerRect = header.rectTransform;
        headerRect.anchorMin = new Vector2(0f, 1f);
        headerRect.anchorMax = new Vector2(1f, 1f);
        headerRect.pivot = new Vector2(0.5f, 1f);
        headerRect.anchoredPosition = new Vector2(0f, -4f);
        headerRect.sizeDelta = new Vector2(-20f, 20f);

        grid = UIKit.Rect("Tiles", panel, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0f, -10f), new Vector2(-16f, -36f));
        GridLayoutGroup layout = grid.gameObject.AddComponent<GridLayoutGroup>();
        layout.cellSize = new Vector2(TileWidth, TileHeight);
        layout.spacing = new Vector2(Spacing, Spacing);
        layout.startCorner = GridLayoutGroup.Corner.UpperLeft;
        layout.childAlignment = TextAnchor.UpperLeft;
        layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        layout.constraintCount = MaxColumns;

        panel.gameObject.SetActive(false);
    }

    private void Update()
    {
        if (manager == null)
        {
            manager = PlayerSpaceManager.Instance != null ? PlayerSpaceManager.Instance : FindFirstObjectByType<PlayerSpaceManager>();
            if (manager == null) return;
        }

        if (Time.unscaledTime < nextRefreshAt) return;
        nextRefreshAt = Time.unscaledTime + RefreshInterval;
        Refresh();
    }

    private void Refresh()
    {
        shown.Clear();
        foreach (SpaceUnit unit in manager.selectedUnits)
        {
            UnitHealthManager health = unit != null ? unit.GetComponent<UnitHealthManager>() : null;
            if (health != null && !health.IsDead) shown.Add(unit);
        }

        bool visible = shown.Count > 0 && !PlayerSpaceManager.PlayerRetreating;
        if (panel.gameObject.activeSelf != visible) panel.gameObject.SetActive(visible);
        if (!visible) return;

        // Group tiles by class so like ships sit together.
        shown.Sort((a, b) => string.CompareOrdinal(ClassName(a), ClassName(b)));

        int capacity = MaxColumns * MaxRows;
        bool overflow = shown.Count > capacity;
        int tileCount = overflow ? capacity : shown.Count;
        while (tiles.Count < tileCount) tiles.Add(CreateTile(tiles.Count));

        for (int i = 0; i < tiles.Count; i++)
        {
            Tile tile = tiles[i];
            bool active = i < tileCount;
            if (tile.root.gameObject.activeSelf != active) tile.root.gameObject.SetActive(active);
            if (!active) continue;

            if (overflow && i == tileCount - 1)
            {
                ShowOverflow(tile, shown.Count - (capacity - 1));
                continue;
            }
            ShowUnit(tile, shown[i]);
        }

        int columns = Mathf.Min(MaxColumns, tileCount);
        int rows = Mathf.CeilToInt(tileCount / (float)MaxColumns);
        panel.sizeDelta = new Vector2(Mathf.Max(220f, columns * (TileWidth + Spacing) - Spacing + 16f), rows * (TileHeight + Spacing) - Spacing + 46f);
        int ready = shown.Count(u => u.TryGetComponent(out ShipAbility a) && a.Ready);
        string abilities = ready > 0 ? $"   <color=#8EE59A>B: {ready} abilit{(ready == 1 ? "y" : "ies")} ready</color>" : "";
        header.richText = true;
        header.text = (shown.Count == 1 ? DisplayName(shown[0]) : $"{shown.Count} SELECTED") + abilities;
    }

    private void ShowUnit(Tile tile, SpaceUnit unit)
    {
        UnitHealthManager health = unit.GetComponent<UnitHealthManager>();
        Ship ship = health != null ? health.sourceShip : null;

        tile.overflow.gameObject.SetActive(false);
        tile.icon.gameObject.SetActive(true);
        tile.hullFill.parent.gameObject.SetActive(true);
        tile.click.unit = unit;
        tile.click.bar = this;

        Sprite sprite = ship != null && unit.GetComponent<Squadron>() == null ? ship.IconSprite : null;
        if (sprite == null && unit.GetComponent<Squadron>() != null) sprite = SquadronGlyphs.For(ship != null ? ship.fleetRole : FleetRole.Fighter);
        tile.icon.sprite = sprite;
        tile.icon.color = sprite != null ? Color.white : new Color(1f, 1f, 1f, 0.2f);

        ShipAltitude altitude = unit.GetComponent<ShipAltitude>();
        tile.height.gameObject.SetActive(altitude != null && Mathf.Abs(altitude.Altitude) >= 1f);
        if (altitude != null) tile.height.text = altitude.Altitude.ToString("+0;-0");

        float hull = health != null && health.maxHealth > 0f ? Mathf.Clamp01(health.currentHealth / health.maxHealth) : 1f;
        tile.hullFill.anchorMax = new Vector2(hull, 1f);
        tile.hullImage.color = Color.Lerp(HullLow, HullHigh, hull);
    }

    private static void ShowOverflow(Tile tile, int hidden)
    {
        tile.icon.gameObject.SetActive(false);
        tile.hullFill.parent.gameObject.SetActive(false);
        tile.overflow.gameObject.SetActive(true);
        tile.height.gameObject.SetActive(false);
        tile.overflow.text = $"+{hidden}";
        tile.click.unit = null;
    }

    private Tile CreateTile(int index)
    {
        RectTransform root = UIKit.Rect($"Tile{index}", grid, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(TileWidth, TileHeight));
        UIKit.Image(root, TileColor, true);

        RectTransform iconRect = UIKit.Rect("Icon", root, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0f, 4f), new Vector2(-8f, -16f));
        Image icon = UIKit.Image(iconRect, Color.white);
        icon.preserveAspect = true;

        RectTransform track = UIKit.Rect("Hull", root, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 3f), new Vector2(-8f, 5f));
        UIKit.Image(track, new Color(0f, 0f, 0f, 0.6f));
        RectTransform fill = UIKit.Rect("Fill", track, Vector2.zero, Vector2.one, new Vector2(0f, 0.5f), Vector2.zero, Vector2.zero);
        Image fillImage = UIKit.Image(fill, HullHigh);

        TMP_Text overflow = UIKit.Text(root, "", 18f, UIKit.TextColor, TextAlignmentOptions.Center);
        overflow.rectTransform.anchorMin = Vector2.zero;
        overflow.rectTransform.anchorMax = Vector2.one;
        overflow.rectTransform.sizeDelta = Vector2.zero;
        overflow.gameObject.SetActive(false);

        TMP_Text height = UIKit.Text(root, "", 11f, new Color(0.45f, 0.9f, 1f), TextAlignmentOptions.TopRight);
        height.rectTransform.offsetMax = new Vector2(-3f, -2f);

        TileClick click = root.gameObject.AddComponent<TileClick>();
        return new Tile { root = root, icon = icon, hullFill = fill, hullImage = fillImage, overflow = overflow, height = height, click = click };
    }

    // A lone selected ship shows its own name.
    private static string DisplayName(SpaceUnit unit)
    {
        UnitHealthManager health = unit != null ? unit.GetComponent<UnitHealthManager>() : null;
        ShipEntry entry = health != null ? health.entry : null;
        return entry != null && !string.IsNullOrEmpty(entry.shipName) ? $"{entry.shipName}  ·  {ClassName(unit)}" : ClassName(unit);
    }

    private static string ClassName(SpaceUnit unit)
    {
        UnitHealthManager health = unit != null ? unit.GetComponent<UnitHealthManager>() : null;
        return health != null && health.sourceShip != null ? ShipInfo.Name(health.sourceShip) : unit != null ? unit.shipType.ToString() : "";
    }

    private void Clicked(SpaceUnit unit, PointerEventData.InputButton button)
    {
        if (unit == null || manager == null) return;

        bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);

        if (button == PointerEventData.InputButton.Right || shift)
        {
            manager.DeselectUnit(unit);
        }
        else if (ctrl)
        {
            string wanted = ClassName(unit);
            manager.SelectOnly(new List<SpaceUnit>(shown).FindAll(u => ClassName(u) == wanted));
        }
        else
        {
            manager.SelectOnly(new[] { unit });
        }
        nextRefreshAt = 0f;
    }

    private class TileClick : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
    {
        public SpaceUnit unit;
        public SelectionBar bar;
        private bool hovering;

        public void OnPointerClick(PointerEventData eventData)
        {
            bar?.Clicked(unit, eventData.button);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            UnitHealthManager health = unit != null ? unit.GetComponent<UnitHealthManager>() : null;
            if (health == null || health.sourceShip == null) return;
            hovering = true;
            ShipBlade.Show(health.sourceShip, health);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (!hovering) return;
            hovering = false;
            ShipBlade.Hide();
        }
    }
}
