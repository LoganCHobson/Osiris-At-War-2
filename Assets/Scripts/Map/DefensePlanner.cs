using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Lay out how a planet's defenders stand when it's attacked (DEFENSE LAYOUT on the planet's build menu).
// The middle is the battlefield seen from above - your shipyard/station at the bottom, the enemy arriving
// from the top. Pick a ship category (or one of the ship classes stationed here) on the right, click the
// field to place it, drag markers to move them, right-click a marker to remove it. Save it as a named
// layout, then use it here or at every planet. Ships are matched to markers when the battle opens; any
// that don't get a marker form up in the standard block.
public class DefensePlanner : MonoBehaviour
{
    private const float Width = 1260f;
    private const float Height = 720f;
    private const float FieldWidth = 720f;
    private static float FieldHeight => FieldWidth * DefenseFormation.Area.height / DefenseFormation.Area.width;
    private const float MarkerSize = 28f;

    private static DefensePlanner instance;
    public static bool IsOpen => instance != null && instance.shade != null && instance.shade.gameObject.activeSelf;

    private Planet planet;
    private Faction faction;
    private List<DefenseFormation> custom = new List<DefenseFormation>();
    private DefenseFormation draft = new DefenseFormation();
    private string selectedName = DefenseFormation.StandardName;
    private bool draftIsCustom;

    private ShipInfo.Category? toolCategory = ShipInfo.Category.Capital;
    private Ship toolShip;

    private RectTransform shade;
    private TMP_Text title;
    private RectTransform layoutList;
    private RectTransform field;
    private RectTransform markers;
    private RectTransform palette;
    private TMP_InputField nameField;
    private TMP_Text status;
    private readonly List<(UIBits.PillButton button, ShipInfo.Category? category, Ship ship)> tools = new List<(UIBits.PillButton, ShipInfo.Category?, Ship)>();

    public static void Open(Planet planet)
    {
        if (planet == null || planet.owner == null) return;
        if (instance == null) instance = new GameObject("DefensePlanner").AddComponent<DefensePlanner>();
        instance.Show(planet);
    }

    public static void Close()
    {
        if (instance == null || instance.shade == null) return;
        instance.shade.gameObject.SetActive(false);
        UIBits.ModalOpen = ProductionWindow.IsOpen;
    }

    private void Awake()
    {
        Canvas canvas = UIKit.Canvas("DefensePlannerCanvas", 81, transform);
        Build(canvas.transform);
        shade.gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
        UIBits.ModalOpen = false;
    }

    private void Update()
    {
        if (IsOpen && Input.GetKeyDown(KeyCode.Escape) && !TypingInField())
        {
            UIBits.EscapeConsumedFrame = Time.frameCount;
            Close();
        }
    }

    private static bool TypingInField()
    {
        GameObject focused = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        return focused != null && focused.GetComponent<TMP_InputField>() != null && focused.GetComponent<TMP_InputField>().isFocused;
    }

    private void Show(Planet target)
    {
        planet = target;
        faction = target.owner;
        custom = DefensePlans.LoadCustom(faction);

        shade.gameObject.SetActive(true);
        UIBits.ModalOpen = true;
        title.text = $"DEFENSE LAYOUT  ·  {planet.planetName.ToUpperInvariant()}";

        Select(DefensePlans.AssignedName(planet.planetName));
        RebuildPalette();
    }

    // ---------------- Layouts ----------------

    private IEnumerable<string> AllNames()
    {
        yield return DefenseFormation.StandardName;
        foreach (DefenseFormation preset in DefenseFormation.Presets()) yield return preset.name;
        foreach (DefenseFormation mine in custom) yield return mine.name;
    }

    private void Select(string name)
    {
        selectedName = name;
        DefenseFormation mine = custom.Find(f => f.name == name);
        draftIsCustom = mine != null;
        draft = mine != null ? mine.Clone()
            : name == DefenseFormation.StandardName ? new DefenseFormation { name = DefenseFormation.StandardName }
            : (DefenseFormation.Preset(name) ?? new DefenseFormation { name = DefenseFormation.StandardName });

        nameField.SetTextWithoutNotify(draft.name);
        RebuildLayoutList();
        RebuildMarkers();
        status.text = draftIsCustom || name == DefenseFormation.StandardName ? "" : "Built-in layout - edits are saved as your own copy.";
    }

    private void RebuildLayoutList()
    {
        UIBits.Clear(layoutList);
        string assignedHere = DefensePlans.AssignedName(planet.planetName);
        foreach (string name in AllNames())
        {
            string captured = name;
            string tag = name == assignedHere ? "   <color=#8EE59A>IN USE</color>" : "";
            UIBits.PillButton button = UIBits.Button(layoutList, name + tag, 16f, new Vector2(0f, 38f), () => Select(captured));
            button.label.alignment = TextAlignmentOptions.MidlineLeft;
            button.label.rectTransform.offsetMin = new Vector2(12f, 0f);
            button.label.color = UIKit.TextColor;
            button.label.richText = true;
            button.background.color = name == selectedName ? UIBits.Selected : UIKit.RowColor;
        }
    }

    // Any edit to a built-in layout turns the draft into the player's own copy.
    private void Touch()
    {
        if (draftIsCustom) return;
        if (selectedName != DefenseFormation.StandardName || draft.slots.Count > 0)
        {
            string baseName = selectedName == DefenseFormation.StandardName ? "My Layout" : selectedName + " (mine)";
            string name = baseName;
            for (int n = 2; AllNames().Contains(name); n++) name = $"{baseName} {n}";
            draft.name = name;
            nameField.SetTextWithoutNotify(name);
            draftIsCustom = true;
            selectedName = name;
            status.text = "Unsaved - press SAVE LAYOUT.";
        }
    }

    private bool SaveDraft()
    {
        if (!draftIsCustom) return true; // Built-ins need no saving.
        if (draft.slots.Count == 0)
        {
            status.text = "Place some ships first.";
            return false;
        }

        string wanted = string.IsNullOrWhiteSpace(nameField.text) ? draft.name : nameField.text.Trim();
        if (wanted == DefenseFormation.StandardName || DefenseFormation.Preset(wanted) != null) wanted += " (mine)";
        int existing = custom.FindIndex(f => f.name == selectedName);
        draft.name = wanted;
        if (existing >= 0) custom[existing] = draft.Clone();
        else custom.Add(draft.Clone());

        DefensePlans.SaveCustom(faction, custom);
        selectedName = wanted;
        nameField.SetTextWithoutNotify(wanted);
        status.text = $"Saved \"{wanted}\".";
        RebuildLayoutList();
        return true;
    }

    private void DeleteLayout()
    {
        int index = custom.FindIndex(f => f.name == selectedName);
        if (index < 0)
        {
            status.text = "Built-in layouts can't be deleted.";
            return;
        }
        custom.RemoveAt(index);
        DefensePlans.SaveCustom(faction, custom);
        Select(DefenseFormation.StandardName);
    }

    private void UseHere(bool everywhere)
    {
        if (!SaveDraft()) return;
        DefensePlans.Assign(everywhere ? DefensePlans.AllPlanets : planet.planetName, selectedName);
        status.text = everywhere ? $"\"{selectedName}\" now defends every planet." : $"\"{selectedName}\" now defends {planet.planetName}.";
        RebuildLayoutList();
    }

    // ---------------- Palette ----------------

    private void RebuildPalette()
    {
        UIBits.Clear(palette);
        tools.Clear();

        foreach (ShipInfo.Category category in ShipInfo.Categories)
        {
            ShipInfo.Category captured = category;
            UIBits.PillButton button = UIBits.Button(palette, ShipInfo.CategoryName(category).ToUpperInvariant(), 15f, new Vector2(0f, 36f), () => SetTool(captured, null));
            tools.Add((button, category, null));
        }

        // The classes actually stationed here, for placing a particular ship.
        List<Ship> stationed = new List<Ship>();
        for (int slot = 0; slot < Planet.FleetSlotCount; slot++)
        {
            GalacticFleet fleet = planet.GetFleetInSlot(slot);
            if (fleet == null || fleet.faction != faction) continue;
            foreach (Ship ship in fleet.ships)
            {
                if (ship != null && !stationed.Exists(s => s.prefab == ship.prefab)) stationed.Add(ship);
            }
        }

        if (stationed.Count > 0)
        {
            UIBits.Heading(palette, "STATIONED HERE");
            foreach (Ship ship in stationed)
            {
                Ship captured = ship;
                UIBits.PillButton button = UIBits.Button(palette, ShipInfo.Name(ship), 15f, new Vector2(0f, 34f), () => SetTool(null, captured));
                tools.Add((button, null, ship));
            }
        }
        SetTool(toolCategory ?? ShipInfo.Category.Capital, toolShip != null && stationed.Contains(toolShip) ? toolShip : null);
    }

    private void SetTool(ShipInfo.Category? category, Ship ship)
    {
        toolShip = ship;
        toolCategory = ship != null ? (ShipInfo.Category?)null : category;
        foreach ((UIBits.PillButton button, ShipInfo.Category? c, Ship s) in tools)
        {
            bool on = ship != null ? s == ship : s == null && c == category;
            button.background.color = on ? UIBits.Selected : UIBits.ButtonIdle;
        }
    }

    // ---------------- Field ----------------

    private Vector2 ToField(float x, float z)
    {
        Rect area = DefenseFormation.Area;
        return new Vector2((x - area.center.x) / area.width * FieldWidth, (z - area.center.y) / area.height * FieldHeight);
    }

    private Vector2 FromField(Vector2 local)
    {
        Rect area = DefenseFormation.Area;
        float x = Mathf.Clamp(local.x / FieldWidth * area.width + area.center.x, area.xMin, area.xMax);
        float z = Mathf.Clamp(local.y / FieldHeight * area.height + area.center.y, area.yMin, area.yMax);
        return new Vector2(x, z);
    }

    private void PlaceAt(Vector2 screen, Camera eventCamera)
    {
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(field, screen, eventCamera, out Vector2 local)) return;
        Touch();

        Vector2 world = FromField(local);
        DefenseFormation.Slot slot = new DefenseFormation.Slot
        {
            ship = toolShip != null ? SaveRegistry.Id(toolShip) : null,
            category = toolShip != null ? ShipInfo.CategoryOf(toolShip).ToString() : (toolCategory ?? ShipInfo.Category.Capital).ToString(),
            x = world.x,
            z = world.y
        };
        draft.slots.Add(slot);
        RebuildMarkers();
    }

    private void RebuildMarkers()
    {
        UIBits.Clear(markers);
        foreach (DefenseFormation.Slot slot in draft.slots)
        {
            CreateMarker(slot);
        }
        if (draft.slots.Count == 0 && selectedName == DefenseFormation.StandardName)
        {
            status.text = "Standard: the usual block - escorts in front, the line behind, carriers at the back.";
        }
    }

    private void CreateMarker(DefenseFormation.Slot slot)
    {
        RectTransform marker = UIKit.Rect("Marker", markers, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), ToField(slot.x, slot.z), new Vector2(MarkerSize, MarkerSize));
        Ship ship = string.IsNullOrEmpty(slot.ship) ? null : SaveRegistry.FindShip(slot.ship);
        ShipInfo.Category category = System.Enum.TryParse(slot.category, out ShipInfo.Category parsed) ? parsed : ShipInfo.Category.Capital;

        Image background = UIKit.Image(marker, CategoryColor(category), true);
        if (ship != null && ship.IconSprite != null)
        {
            RectTransform iconRect = UIKit.Fill("Icon", marker);
            iconRect.offsetMin = new Vector2(2f, 2f);
            iconRect.offsetMax = new Vector2(-2f, -2f);
            Image icon = UIKit.Image(iconRect, Color.white);
            icon.sprite = ship.IconSprite;
            icon.preserveAspect = true;
        }
        else
        {
            TMP_Text label = UIKit.Text(marker, CategoryLetter(category), 15f, new Color(0.05f, 0.07f, 0.1f), TextAlignmentOptions.Center);
            label.fontStyle = FontStyles.Bold;
        }

        MarkerHandle handle = marker.gameObject.AddComponent<MarkerHandle>();
        handle.planner = this;
        handle.slot = slot;
        handle.background = background;

        RectTransform tagRect = UIKit.Rect("Height", marker, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 1f), new Vector2(0f, -1f), new Vector2(44f, 16f));
        handle.heightTag = UIKit.Text(tagRect, "", 11f, new Color(0.45f, 0.9f, 1f), TextAlignmentOptions.Center);
        handle.RefreshHeight();
    }

    private static Color CategoryColor(ShipInfo.Category category)
    {
        switch (category)
        {
            case ShipInfo.Category.Escort: return new Color(0.45f, 0.8f, 1f);
            case ShipInfo.Category.Carrier: return new Color(0.75f, 0.55f, 1f);
            case ShipInfo.Category.StrikeCraft: return new Color(0.55f, 1f, 0.65f);
            default: return UIKit.Accent;
        }
    }

    private static string CategoryLetter(ShipInfo.Category category)
    {
        switch (category)
        {
            case ShipInfo.Category.Escort: return "E";
            case ShipInfo.Category.Carrier: return "V";
            case ShipInfo.Category.StrikeCraft: return "S";
            default: return "C";
        }
    }

    private void MoveSlot(DefenseFormation.Slot slot, RectTransform marker, Vector2 screen, Camera eventCamera)
    {
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(field, screen, eventCamera, out Vector2 local)) return;
        if (!draftIsCustom)
        {
            // Editing a built-in: copy it, then keep editing the copy's matching slot.
            int index = draft.slots.IndexOf(slot);
            Touch();
            if (index >= 0 && index < draft.slots.Count) slot = draft.slots[index];
        }

        Vector2 world = FromField(local);
        slot.x = world.x;
        slot.z = world.y;
        marker.anchoredPosition = ToField(slot.x, slot.z);
    }

    private void RaiseSlot(DefenseFormation.Slot slot, MarkerHandle handle, float delta)
    {
        if (!draftIsCustom)
        {
            int index = draft.slots.IndexOf(slot);
            Touch();
            if (index >= 0 && index < draft.slots.Count) slot = draft.slots[index];
            handle.slot = slot;
        }
        slot.y = Mathf.Clamp(Mathf.Round((slot.y + delta) / 5f) * 5f, -ShipAltitude.MaxAltitude, ShipAltitude.MaxAltitude);
        handle.RefreshHeight();
    }

    private void RemoveSlot(DefenseFormation.Slot slot)
    {
        int index = draft.slots.IndexOf(slot);
        Touch();
        if (index >= 0 && index < draft.slots.Count) draft.slots.RemoveAt(index);
        RebuildMarkers();
    }

    private void ClearSlots()
    {
        Touch();
        draft.slots.Clear();
        RebuildMarkers();
    }

    private class FieldClick : MonoBehaviour, IPointerClickHandler
    {
        public DefensePlanner planner;

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left && !eventData.dragging)
            {
                planner.PlaceAt(eventData.position, eventData.pressEventCamera);
            }
        }
    }

    private class MarkerHandle : MonoBehaviour, IPointerClickHandler, IDragHandler, IBeginDragHandler, IScrollHandler
    {
        public DefensePlanner planner;
        public DefenseFormation.Slot slot;
        public Image background;
        public TMP_Text heightTag;

        public void OnScroll(PointerEventData eventData)
        {
            planner.RaiseSlot(slot, this, Mathf.Sign(eventData.scrollDelta.y) * 10f);
        }

        public void RefreshHeight()
        {
            if (heightTag == null) return;
            heightTag.text = Mathf.Abs(slot.y) < 1f ? "" : slot.y.ToString("+0;-0");
        }

        public void OnBeginDrag(PointerEventData eventData) { }

        public void OnDrag(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left) return;
            planner.MoveSlot(slot, (RectTransform)transform, eventData.position, eventData.pressEventCamera);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Right) planner.RemoveSlot(slot);
        }
    }

    // ---------------- Layout ----------------

    private void Build(Transform canvas)
    {
        shade = UIKit.Fill("Shade", canvas);
        UIKit.Image(shade, new Color(0f, 0f, 0f, 0.5f), true);

        RectTransform panel = UIKit.Rect("DefensePlanner", shade, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(Width, Height));
        UIKit.Image(panel, UIKit.PanelColor, true);
        RectTransform rule = UIKit.Rect("Rule", panel, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 2f));
        UIKit.Image(rule, UIKit.Accent);

        title = UIKit.Text(panel, "DEFENSE LAYOUT", 26f, UIKit.TextColor, TextAlignmentOptions.TopLeft);
        title.characterSpacing = 8f;
        title.rectTransform.offsetMin = new Vector2(28f, 0f);
        title.rectTransform.offsetMax = new Vector2(-200f, -18f);

        UIBits.PillButton close = UIBits.Button(panel, "CLOSE  [ESC]", 15f, new Vector2(150f, 38f), Close);
        RectTransform closeRect = (RectTransform)close.button.transform;
        closeRect.anchorMin = closeRect.anchorMax = closeRect.pivot = new Vector2(1f, 1f);
        closeRect.anchoredPosition = new Vector2(-20f, -16f);

        // Left: layouts.
        RectTransform left = UIBits.Column(panel, 28f, 220f, 76f, 24f, 8f);
        UIBits.Heading(left, "LAYOUTS");
        layoutList = UIBits.ScrollList(UIBits.Flexible(left, 200f), 5f);
        nameField = UIBits.InputField(left, "Layout name", 40f, value =>
        {
            if (!draftIsCustom) Touch();
            if (!string.IsNullOrWhiteSpace(value)) draft.name = value.Trim();
        });
        UIBits.Button(left, "SAVE LAYOUT", 15f, new Vector2(0f, 38f), () => SaveDraft());
        UIBits.Button(left, "DELETE LAYOUT", 15f, new Vector2(0f, 38f), DeleteLayout);

        // Centre: the battlefield.
        RectTransform centre = UIKit.Rect("Field", panel, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(272f, -96f), new Vector2(FieldWidth, FieldHeight));
        field = centre;
        UIKit.Image(field, new Color(0.02f, 0.03f, 0.06f, 1f), true);
        field.gameObject.AddComponent<FieldClick>().planner = this;
        DrawGrid();

        markers = UIKit.Fill("Markers", field);

        TMP_Text enemy = UIKit.Text(field, "ENEMY ARRIVES", 13f, new Color(1f, 0.45f, 0.4f, 0.8f), TextAlignmentOptions.Top);
        enemy.characterSpacing = 8f;
        enemy.rectTransform.offsetMax = new Vector2(0f, -6f);

        RectTransform home = UIKit.Rect("Home", field, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), ToField(0f, -60f), new Vector2(120f, 30f));
        UIKit.Image(home, new Color(1f, 1f, 1f, 0.08f));
        UIKit.Text(home, "STATION / YARD", 11f, UIKit.MutedColor, TextAlignmentOptions.Center).characterSpacing = 4f;

        TMP_Text help = UIKit.Text(panel, "Click to place  ·  Drag to move  ·  Mouse wheel over a marker sets its height  ·  Right-click to remove", 14f, UIKit.MutedColor, TextAlignmentOptions.TopLeft);
        help.rectTransform.anchorMin = help.rectTransform.anchorMax = help.rectTransform.pivot = new Vector2(0f, 1f);
        help.rectTransform.anchoredPosition = new Vector2(272f, -96f - FieldHeight - 10f);
        help.rectTransform.sizeDelta = new Vector2(FieldWidth, 22f);

        status = UIKit.Text(panel, "", 15f, UIKit.TextColor, TextAlignmentOptions.TopLeft);
        status.rectTransform.anchorMin = status.rectTransform.anchorMax = status.rectTransform.pivot = new Vector2(0f, 1f);
        status.rectTransform.anchoredPosition = new Vector2(272f, -96f - FieldHeight - 36f);
        status.rectTransform.sizeDelta = new Vector2(FieldWidth, 24f);

        // Right: palette + assignment.
        RectTransform right = UIBits.Column(panel, 1012f, 220f, 76f, 24f, 6f);
        UIBits.Heading(right, "PLACE");
        palette = UIBits.ScrollList(UIBits.Flexible(right, 200f), 5f);
        UIBits.Button(right, "CLEAR FIELD", 15f, new Vector2(0f, 36f), ClearSlots);
        UIBits.Heading(right, "USE THIS LAYOUT");
        UIBits.Button(right, "AT THIS PLANET", 15f, new Vector2(0f, 40f), () => UseHere(false));
        UIBits.Button(right, "AT ALL PLANETS", 15f, new Vector2(0f, 40f), () => UseHere(true));
    }

    private void DrawGrid()
    {
        Rect area = DefenseFormation.Area;
        Color line = new Color(1f, 1f, 1f, 0.05f);
        for (float x = Mathf.Ceil(area.xMin / 100f) * 100f; x <= area.xMax; x += 100f)
        {
            RectTransform v = UIKit.Rect("V", field, new Vector2(0.5f, 0f), new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(ToField(x, 0f).x, 0f), new Vector2(x == 0f ? 2f : 1f, 0f));
            UIKit.Image(v, x == 0f ? new Color(1f, 1f, 1f, 0.1f) : line);
        }
        for (float z = Mathf.Ceil(area.yMin / 100f) * 100f; z <= area.yMax; z += 100f)
        {
            RectTransform h = UIKit.Rect("H", field, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, ToField(0f, z).y), new Vector2(0f, z == 0f ? 2f : 1f));
            UIKit.Image(h, z == 0f ? new Color(1f, 1f, 1f, 0.1f) : line);
        }
    }
}
