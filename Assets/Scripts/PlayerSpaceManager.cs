using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

public class PlayerSpaceManager : MonoBehaviour
{
    public enum CommandMode { None, Guard, PriorityMove }

    private Camera cam;



    public LayerMask groundLayer;
    public LayerMask friendlyUnitLayer;
    public LayerMask enemyUnitLayer;

    public LayerMask uiLayer = 5;

    public Animator selectionAnim;

    public List<SpaceUnit> selectedUnits = new List<SpaceUnit>();

    [Header("Command Hotkeys")]
    public KeyCode guardKey = KeyCode.G;
    public KeyCode priorityMoveKey = KeyCode.F;
    public KeyCode cancelCommandKey = KeyCode.Escape;

    public CommandMode Mode { get; private set; }
    public event System.Action<CommandMode> ModeChanged;

    public bool HasSelection
    {
        get
        {
            foreach (SpaceUnit unit in selectedUnits)
            {
                if (unit != null && !IsDead(unit)) return true;
            }
            return false;
        }
    }

    private HardpointManager lastHighlight;

    // Double-clicking a friendly ship selects every ship of that class.
    public const float DoubleClickTime = 0.35f;
    private SpaceUnit lastClickedUnit;
    private float lastClickTime = float.NegativeInfinity;

    // Ctrl+number saves the selection as a group, number recalls it (Shift+number adds it to the selection).
    private readonly Dictionary<int, List<SpaceUnit>> controlGroups = new Dictionary<int, List<SpaceUnit>>();

    public static PlayerSpaceManager Instance { get; private set; }
    public event System.Action SelectionChanged;

    private void Awake()
    {
        Instance = this;
    }

    // True once a control group lives on this number key (it then takes the key over from game speed).
    public bool HasGroup(int number)
    {
        return controlGroups.TryGetValue(number, out List<SpaceUnit> group) && group.Exists(u => u != null && !IsDead(u));
    }

    void Start()
    {
        cam = Camera.main;
    }


    public static bool PlayerRetreating => RetreatManager.Instance != null && GameManager.Instance != null
        && RetreatManager.Instance.IsRetreating(GameManager.Instance.PlayerIsAttacker);

    void Update()
    {
        ShipHeathHighlighter();
        CursorSelector();

        if (PlayerRetreating)
        {
            if (selectedUnits.Count > 0) DeselectAllUnits();
            SetMode(CommandMode.None);
            return;
        }

        HandleCommandKeys();

        if (Mode != CommandMode.None && !HasSelection)
        {
            SetMode(CommandMode.None);
        }

        bool pointerOverUI = UIKit.PointerOverScreenUI(); // Target icons and health bars on the ships don't block clicks.

        if (HandleHeightPick(pointerOverUI)) return;

        if (Input.GetMouseButtonDown(0) && !pointerOverUI && Mode != CommandMode.None)
        {
            HandleCommandClick();
        }
        else if (Input.GetMouseButtonDown(1) && Mode != CommandMode.None)
        {
            SetMode(CommandMode.None);
        }
        else if (Input.GetMouseButtonDown(0) && !pointerOverUI)
        {
            RaycastHit hit;
            Ray ray = cam.ScreenPointToRay(Input.mousePosition);

            if (TryHostileTargetIcon(ray, out hit))
            {
                if (selectedUnits.Count > 0)
                {
                    TargetSelection(hit);
                }
            }
            else if (TrySquadronIcon(ray, out hit, out bool hostileSquadron))
            {
                if (!hostileSquadron)
                {
                    PlayerSelection(hit);
                }
                else if (selectedUnits.Count > 0)
                {
                    TargetSelection(hit);
                }
            }
            else if (Physics.Raycast(ray, out hit, Mathf.Infinity, enemyUnitLayer))
            {
                if (selectedUnits.Count > 0)
                {
                    TargetSelection(hit);
                }
            }
            else if (Physics.Raycast(ray, out hit, Mathf.Infinity, friendlyUnitLayer))
            {
                PlayerSelection(hit);
            }
            else if (Physics.Raycast(ray, out hit, Mathf.Infinity, groundLayer) && selectedUnits.Count > 0) //Location selection
            {
                PlayerLocation(hit);
            }

        }
        else if (Input.GetMouseButtonDown(1) && !pointerOverUI) // Clear selection (right-clicks on the HUD are the HUD's)
        {
            DeselectAllUnits();
        }
    }

    private bool TryHostileTargetIcon(Ray ray, out RaycastHit best)
    {
        best = default;
        float bestDistance = float.MaxValue;

        foreach (RaycastHit candidate in Physics.RaycastAll(ray, Mathf.Infinity, uiLayer))
        {
            if (candidate.distance >= bestDistance || !candidate.transform.CompareTag("TargetUI")) continue;

            UnitHealthManager owner = candidate.collider.GetComponentInParent<UnitHealthManager>();
            if (owner == null || owner.IsDead || (enemyUnitLayer.value & (1 << owner.gameObject.layer)) == 0) continue;

            best = candidate;
            bestDistance = candidate.distance;
        }

        return bestDistance < float.MaxValue;
    }

    private bool TrySquadronIcon(Ray ray, out RaycastHit best, out bool hostile)
    {
        best = default;
        hostile = false;
        float bestDistance = float.MaxValue;

        foreach (RaycastHit candidate in Physics.RaycastAll(ray, Mathf.Infinity, uiLayer))
        {
            if (candidate.distance >= bestDistance) continue;

            Squadron squadron = candidate.collider.GetComponentInParent<Squadron>();
            if (squadron == null || squadron.Health == null || squadron.Health.IsDead) continue;

            best = candidate;
            bestDistance = candidate.distance;
            hostile = (enemyUnitLayer.value & (1 << squadron.gameObject.layer)) != 0;
        }

        return bestDistance < float.MaxValue;
    }

    public void BeginCommand(CommandMode mode)
    {
        if (PlayerRetreating) mode = CommandMode.None;
        PruneSelection();
        if (selectedUnits.Count == 0 || Mode == mode)
        {
            mode = CommandMode.None;
        }
        SetMode(mode);
    }

    public void CancelCommand()
    {
        SetMode(CommandMode.None);
    }

    private void SetMode(CommandMode mode)
    {
        if (Mode == mode) return;
        Mode = mode;
        ModeChanged?.Invoke(Mode);
    }

    private void HandleCommandKeys()
    {
        HandleControlGroups();
        HandleFlightKeys();

        if (Keybinds.Down(KeyAction.Guard)) BeginCommand(CommandMode.Guard);
        else if (Keybinds.Down(KeyAction.PriorityMove)) BeginCommand(CommandMode.PriorityMove);
        else if (Input.GetKeyDown(cancelCommandKey)) CancelCommand();
    }

    // ---------------- 3D flight ----------------
    //   PageUp / PageDown  climb / dive (Shift: three steps)      End   back down to the battle plane
    //   Z / X              roll left / right                       R / T pitch nose up / down
    //   Home               level out, back to automatic angling
    //   Alt + drag on open space: move there, dragging up/down to choose the height.

    private void HandleFlightKeys()
    {
        if (PlacementManager.Instance != null && PlacementManager.Instance.IsPreviewing) return; // Those keys set the drop height.
        bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        float step = ShipAltitude.Step * (shift ? 3f : 1f);

        if (Keybinds.Down(KeyAction.Climb)) ForSelectedAltitude(a => a.ChangeAltitude(step));
        else if (Keybinds.Down(KeyAction.Dive)) ForSelectedAltitude(a => a.ChangeAltitude(-step));
        else if (Keybinds.Down(KeyAction.ToPlane)) ForSelectedAltitude(a => a.SetTargetAltitude(0f));
        else if (Keybinds.Down(KeyAction.RollLeft)) ForSelectedAltitude(a => a.Roll(ShipAltitude.AttitudeStep));
        else if (Keybinds.Down(KeyAction.RollRight)) ForSelectedAltitude(a => a.Roll(-ShipAltitude.AttitudeStep));
        else if (Keybinds.Down(KeyAction.PitchUp)) ForSelectedAltitude(a => a.Pitch(-ShipAltitude.AttitudeStep));
        else if (Keybinds.Down(KeyAction.PitchDown)) ForSelectedAltitude(a => a.Pitch(ShipAltitude.AttitudeStep));
        else if (Keybinds.Down(KeyAction.Level)) ForSelectedAltitude(a => a.Level());
        else if (Keybinds.Down(KeyAction.VerticalFormation)) CycleVerticalFormation();
        else if (Keybinds.Down(KeyAction.Stance)) CycleStance();
        else if (Keybinds.Down(KeyAction.Ability)) UseAbilities();
    }

    // B: every selected ship whose ability is ready uses it.
    private void UseAbilities()
    {
        PruneSelection();
        int used = 0;
        float soonest = float.MaxValue;
        foreach (SpaceUnit unit in selectedUnits)
        {
            if (unit == null || !unit.TryGetComponent(out ShipAbility ability) || ability.kind == ShipAbility.Kind.None) continue;
            if (ability.TryActivate()) used++;
            else soonest = Mathf.Min(soonest, ability.CooldownLeft);
        }

        if (used > 0) Toast.Show($"{used} abilit{(used == 1 ? "y" : "ies")} activated");
        else if (soonest < float.MaxValue) Toast.Show($"No abilities ready - next in {Mathf.CeilToInt(soonest)}s");
    }

    // Y: everyone selected moves to the next stance after the first selected ship's.
    private void CycleStance()
    {
        PruneSelection();
        ShipStance first = null;
        foreach (SpaceUnit unit in selectedUnits)
        {
            if (unit != null && unit.TryGetComponent(out first)) break;
        }
        if (first == null) return;

        ShipStance.Kind next = (ShipStance.Kind)(((int)first.Current + 1) % 3);
        foreach (SpaceUnit unit in selectedUnits)
        {
            if (unit != null && unit.TryGetComponent(out ShipStance stance)) stance.Set(next);
        }
        Toast.Show($"Stance: {next}");
    }

    private float AverageTargetAltitude()
    {
        float sum = 0f;
        int count = 0;
        ForSelectedAltitude(a => { sum += a.TargetAltitude; count++; });
        return count > 0 ? sum / count : 0f;
    }

    // L: reshape the selection vertically around its average height.
    //   Flat   - everyone at the same height
    //   Wall   - staggered up and down across the line, so ships behind can fire over/under ships in front
    //   Layers - escorts on top, strike craft above the line, capitals in the middle, carriers underneath
    public enum VerticalFormation { Flat, Wall, Layers }
    private VerticalFormation verticalFormation = VerticalFormation.Layers;
    private const float WallStep = 30f;
    private const float LayerStep = 40f;

    private void CycleVerticalFormation()
    {
        PruneSelection();
        if (selectedUnits.Count == 0) return;

        verticalFormation = (VerticalFormation)(((int)verticalFormation + 1) % 3);
        float middle = AverageTargetAltitude();

        List<SpaceUnit> ordered = new List<SpaceUnit>(selectedUnits);
        Vector3 across = cam != null ? Vector3.ProjectOnPlane(cam.transform.right, Vector3.up).normalized : Vector3.right;
        ordered.Sort((a, b) => Vector3.Dot(a.transform.position, across).CompareTo(Vector3.Dot(b.transform.position, across)));

        for (int i = 0; i < ordered.Count; i++)
        {
            if (!ordered[i].TryGetComponent(out ShipAltitude altitude)) continue;

            float offset = 0f;
            switch (verticalFormation)
            {
                case VerticalFormation.Wall:
                    offset = (i % 3 - 1) * WallStep;
                    break;
                case VerticalFormation.Layers:
                    UnitHealthManager health = ordered[i].GetComponent<UnitHealthManager>();
                    Ship ship = health != null ? health.sourceShip : null;
                    ShipInfo.Category category = ship != null ? ShipInfo.CategoryOf(ship) : ShipInfo.Category.Capital;
                    offset = category == ShipInfo.Category.Escort ? LayerStep
                        : category == ShipInfo.Category.StrikeCraft ? LayerStep * 0.5f
                        : category == ShipInfo.Category.Carrier ? -LayerStep
                        : 0f;
                    break;
            }
            altitude.SetTargetAltitude(middle + offset);
        }

        Toast.Show($"Vertical formation: {verticalFormation}");
    }

    private void ForSelectedAltitude(System.Action<ShipAltitude> action)
    {
        PruneSelection();
        foreach (SpaceUnit unit in selectedUnits)
        {
            if (unit != null && unit.TryGetComponent(out ShipAltitude altitude)) action(altitude);
        }
    }

    private bool heightPicking;
    private RaycastHit heightPickHit;
    private float heightPickStartY;
    private float heightPickValue;
    private LineRenderer heightPreview;

    private static bool AltHeld => Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
    public bool IsPickingHeight => heightPicking;

    // Returns true while a height pick owns the mouse.
    private bool HandleHeightPick(bool pointerOverUI)
    {
        if (!heightPicking)
        {
            if (!Input.GetMouseButtonDown(0) || !AltHeld || pointerOverUI || Mode != CommandMode.None || !HasSelection) return false;

            Ray ray = cam.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, Mathf.Infinity, friendlyUnitLayer | enemyUnitLayer)) return false; // Clicking a ship, not open space.
            if (!Physics.Raycast(ray, out heightPickHit, Mathf.Infinity, groundLayer)) return false;

            heightPicking = true;
            heightPickStartY = Input.mousePosition.y;
            heightPickValue = 0f;
            ShowHeightPreview(true);
            return true;
        }

        if (Input.GetMouseButtonDown(1))
        {
            heightPicking = false;
            ShowHeightPreview(false);
            return true;
        }

        // Screen pixels to world units at the picked spot, so dragging feels the same at any zoom.
        float distance = Vector3.Distance(cam.transform.position, heightPickHit.point);
        float worldPerPixel = 2f * distance * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) / Screen.height;
        heightPickValue = Mathf.Clamp((Input.mousePosition.y - heightPickStartY) * worldPerPixel, -ShipAltitude.MaxAltitude, ShipAltitude.MaxAltitude);
        heightPickValue = Mathf.Round(heightPickValue / 5f) * 5f;

        heightPreview.SetPosition(0, heightPickHit.point);
        heightPreview.SetPosition(1, heightPickHit.point + Vector3.up * heightPickValue);

        if (Input.GetMouseButtonUp(0))
        {
            heightPicking = false;
            ShowHeightPreview(false);
            float chosen = heightPickValue;
            PlayerLocation(heightPickHit);

            // The picked height is the group's middle; each ship keeps its place above/below the others.
            float middle = AverageTargetAltitude();
            ForSelectedAltitude(a => a.SetTargetAltitude(chosen + (a.TargetAltitude - middle)));
        }
        return true;
    }

    private void ShowHeightPreview(bool visible)
    {
        if (heightPreview == null)
        {
            GameObject go = new GameObject("HeightPickPreview");
            heightPreview = go.AddComponent<LineRenderer>();
            heightPreview.positionCount = 2;
            heightPreview.widthMultiplier = 1.5f;
            Shader shader = Shader.Find("Sprites/Default");
            heightPreview.sharedMaterial = new Material(shader != null ? shader : Shader.Find("Universal Render Pipeline/Unlit"));
            heightPreview.startColor = new Color(0.4f, 1f, 0.5f, 0.9f);
            heightPreview.endColor = new Color(0.4f, 1f, 0.5f, 0.3f);
        }
        heightPreview.enabled = visible;
    }

    private void HandleControlGroups()
    {
        bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

        for (int number = 0; number <= 9; number++)
        {
            if (!Input.GetKeyDown(KeyCode.Alpha0 + number)) continue;

            if (ctrl)
            {
                PruneSelection();
                controlGroups[number] = new List<SpaceUnit>(selectedUnits);
                Toast.Show($"Group {number} set ({selectedUnits.Count} ships)");
            }
            else if (HasGroup(number))
            {
                if (!shift) DeselectAllUnits();
                foreach (SpaceUnit unit in controlGroups[number])
                {
                    if (unit != null && !IsDead(unit) && !selectedUnits.Contains(unit)) SelectUnit(unit);
                }
                NotifySelection();
            }
            return;
        }
    }

    // The class a unit belongs to for "select all of this kind": its ship design, else its hull type.
    private static object ClassOf(SpaceUnit unit)
    {
        UnitHealthManager health = unit.GetComponent<UnitHealthManager>();
        if (health != null && health.sourceShip != null) return health.sourceShip.prefab;
        return unit.shipType;
    }

    public void SelectAllOfClass(SpaceUnit sample, bool additive)
    {
        if (sample == null || PlayerRetreating) return;
        object wanted = ClassOf(sample);

        if (!additive) DeselectAllUnits();
        foreach (UnitHealthManager health in UnitHealthManager.Active)
        {
            if (health == null || health.IsDead || health.IsDefense) continue;
            if ((friendlyUnitLayer.value & (1 << health.gameObject.layer)) == 0) continue;

            SpaceUnit unit = health.GetComponent<SpaceUnit>();
            if (unit == null || selectedUnits.Contains(unit) || !Equals(ClassOf(unit), wanted)) continue;
            SelectUnit(unit);
        }
        NotifySelection();
    }

    // Selection bar: narrow the selection down to just these units.
    public void SelectOnly(IEnumerable<SpaceUnit> units)
    {
        DeselectAllUnits();
        foreach (SpaceUnit unit in units)
        {
            if (unit != null && !IsDead(unit) && !selectedUnits.Contains(unit)) SelectUnit(unit);
        }
        NotifySelection();
    }

    public void NotifySelection()
    {
        SelectionChanged?.Invoke();
    }

    private void HandleCommandClick()
    {
        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        RaycastHit hit;

        if (Mode == CommandMode.Guard)
        {
            bool squadronIcon = TrySquadronIcon(ray, out hit, out bool hostileIcon) && !hostileIcon;
            if (squadronIcon || Physics.Raycast(ray, out hit, Mathf.Infinity, friendlyUnitLayer))
            {
                UnitHealthManager ward = hit.collider.GetComponentInParent<UnitHealthManager>();
                if (ward != null && !ward.IsDead)
                {
                    PruneSelection();
                    ShipGuard.Assign(selectedUnits, ward);
                }
            }
            SetMode(CommandMode.None);
            return;
        }

        if (Physics.Raycast(ray, out hit, Mathf.Infinity, enemyUnitLayer))
        {
            TargetSelection(hit);
        }
        else if (!Physics.Raycast(ray, out hit, Mathf.Infinity, friendlyUnitLayer)
            && Physics.Raycast(ray, out hit, Mathf.Infinity, groundLayer))
        {
            PlayerLocation(hit, true);
        }

        if (!Input.GetKey(KeyCode.LeftShift))
        {
            SetMode(CommandMode.None);
        }
    }

    private void TargetSelection(RaycastHit hit)
    {
        PruneSelection();

        HardpointManager targetManager = hit.collider.GetComponentInParent<HardpointManager>();
        UnitHealthManager targetUnit = hit.collider.GetComponentInParent<UnitHealthManager>();
        if (targetUnit != null && !BattleSensors.PlayerCanSee(targetUnit)) return; // Not on our sensors.
        Vector3 targetCenter = targetManager != null ? targetManager.transform.position : hit.point;

        List<SpaceUnit> attackers = new List<SpaceUnit>();
        foreach (SpaceUnit unit in selectedUnits)
        {
            Transform aimPoint = ResolveAimPoint(hit, targetManager);
            if (aimPoint == null) continue;

            ShipGuard.Cancel(unit);
            bool wholeShip = !hit.transform.gameObject.CompareTag("TargetUI") && !hit.collider.gameObject.CompareTag("Hardpoint"); // Clicked the hull, not a hardpoint.
            unit.GetComponent<HardpointManager>().AssignTarget(aimPoint, wholeShip);
            if (unit.IsMobile) attackers.Add(unit);
        }

        if (attackers.Count == 0) return;

        List<Vector3> origins = new List<Vector3>();
        foreach (SpaceUnit unit in attackers)
        {
            origins.Add(unit.transform.position);
        }

        FleetFormation.Slot[] slots = FleetFormation.Attack(attackers, origins, targetCenter);
        int group = SolarStudios.PlayerUnitMoveState.NextOrderGroup();
        for (int i = 0; i < attackers.Count; i++)
        {
            attackers[i].moveState.ClearDestinations();
            attackers[i].moveState.SetOrderGroup(group);
            if (slots[i].move)
            {
                attackers[i].moveState.AddDestination(slots[i].position);
            }
            attackers[i].moveState.SetFacing(slots[i].facing);
            BeginMoving(attackers[i]);
        }
    }

    private static Transform ResolveAimPoint(RaycastHit hit, HardpointManager targetManager)
    {
        if (hit.transform.gameObject.CompareTag("TargetUI")) return hit.transform.parent.parent;
        if (hit.collider.gameObject.CompareTag("Hardpoint")) return hit.collider.transform;
        return targetManager != null ? targetManager.GetRandomHardpoint() : null;
    }

    private static void BeginMoving(SpaceUnit unit)
    {
        if (!unit.IsMobile) return;

        if ((Object)unit.stateMachine.currentState != unit.moveState)
        {
            unit.stateMachine.SetState(unit.moveState);
        }
    }

    private void PruneSelection()
    {
        selectedUnits.RemoveAll(unit => unit == null || IsDead(unit));
    }

    private static bool IsDead(SpaceUnit unit)
    {
        UnitHealthManager health = unit.GetComponent<UnitHealthManager>();
        return health != null && health.IsDead;
    }

    private void PlayerSelection(RaycastHit hit)
    {
        SpaceUnit unit = hit.collider.GetComponentInParent<SpaceUnit>();
        if (unit == null || IsDead(unit)) return;

        bool doubleClick = unit == lastClickedUnit && Time.unscaledTime - lastClickTime <= DoubleClickTime;
        lastClickedUnit = unit;
        lastClickTime = Time.unscaledTime;
        if (doubleClick)
        {
            lastClickedUnit = null;
            SelectAllOfClass(unit, Input.GetKey(KeyCode.LeftShift));
            return;
        }

        if (Input.GetKey(KeyCode.LeftShift)) //Multi selection
        {
            if (!selectedUnits.Contains(unit))
            {
                SelectUnit(unit);
            }
        }
        else //Single selection
        {
            DeselectAllUnits();
            SelectUnit(unit);
            unit.ToggleSelect(true);
        }
    }

    // A group order moves at the pace of its slowest ship; a single ship flies at its own speed.
    private static float SlowestCruiseSpeed(List<SpaceUnit> movers)
    {
        if (movers.Count < 2) return float.PositiveInfinity;

        float slowest = float.PositiveInfinity;
        foreach (SpaceUnit unit in movers)
        {
            UnityEngine.AI.NavMeshAgent agent = unit.agent != null ? unit.agent : unit.GetComponent<UnityEngine.AI.NavMeshAgent>();
            ShipSpeed speed = ShipSpeed.For(agent);
            if (speed != null) slowest = Mathf.Min(slowest, speed.CruiseSpeed);
        }
        return slowest;
    }

    private void PlayerLocation(RaycastHit hit, bool priority = false)
    {
        PruneSelection();
        List<SpaceUnit> movers = selectedUnits.FindAll(unit => unit.IsMobile);

        // Only fighters can be ordered into a dense asteroid cluster; for anything else the order does nothing.
        if (BattleTerrain.Instance != null && BattleTerrain.Instance.InDense(hit.point))
        {
            movers = movers.FindAll(unit => unit.GetComponent<Squadron>() != null);
        }
        if (movers.Count == 0) return;

        bool queue = Input.GetKey(KeyCode.LeftShift);

        List<Vector3> origins = new List<Vector3>();
        foreach (SpaceUnit unit in movers)
        {
            origins.Add(queue ? unit.moveState.LastQueuedPosition : unit.transform.position);
        }

        FleetFormation.Slot[] slots = FleetFormation.Move(movers, origins, hit.point);
        int group = SolarStudios.PlayerUnitMoveState.NextOrderGroup();
        float groupPace = SlowestCruiseSpeed(movers);
        for (int i = 0; i < movers.Count; i++)
        {
            SpaceUnit unit = movers[i];
            ShipGuard.Cancel(unit);
            if (unit.TryGetComponent(out Squadron squadron)) squadron.ClearTarget();
            if (!queue)
            {
                unit.moveState.ClearDestinations();
            }
            unit.moveState.AddDestination(slots[i].position);
            unit.moveState.SetFacing(slots[i].facing);
            unit.moveState.SetPriority(priority);
            unit.moveState.SetOrderGroup(group);
            BeginMoving(unit);
            ShipSpeed.For(unit.agent != null ? unit.agent : unit.GetComponent<UnityEngine.AI.NavMeshAgent>())?.SetGroupCap(groupPace);
        }

        selectionAnim.gameObject.transform.localPosition = hit.point;
        selectionAnim.Play("GroundMarker");
    }

    private void ShipHeathHighlighter()
    {
        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        RaycastHit hit;


        if (Physics.Raycast(ray, out hit, Mathf.Infinity, friendlyUnitLayer))
        {

            if (hit.collider.gameObject.transform.root.TryGetComponent(out HardpointManager manager))
            {

                lastHighlight = manager;
                manager.ToggleHighlight(true);
            }
        }
        else if (Physics.Raycast(ray, out hit, Mathf.Infinity, enemyUnitLayer))
        {

            if (hit.collider.gameObject.transform.root.TryGetComponent(out HardpointManager manager))
            {

                lastHighlight = manager;
                manager.ToggleHighlight(true);
            }

        }
        else
        {
            if (lastHighlight != null && !(lastHighlight.TryGetComponent(out SpaceUnit highlighted) && highlighted.isSelected))
            {
                lastHighlight.ToggleHighlight(false);

                lastHighlight = null;
            }

        }
    }

    private void CursorSelector()
    {
        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        RaycastHit hit;

        if (Mode == CommandMode.Guard)
        {
            bool overFriendly = Physics.Raycast(ray, out hit, Mathf.Infinity, friendlyUnitLayer);
            CursorManager.Instance.SetMarkerType(overFriendly ? CursorManager.CursorType.Selectable : CursorManager.CursorType.None);
            return;
        }

        if (Physics.Raycast(ray, out hit, Mathf.Infinity, friendlyUnitLayer))
        {
            CursorManager.Instance.SetMarkerType(CursorManager.CursorType.Selectable);
        }
        else if (Physics.Raycast(ray, out hit, Mathf.Infinity, enemyUnitLayer) && selectedUnits.Count > 0)
        {
            CursorManager.Instance.SetMarkerType(CursorManager.CursorType.Attackable);
        }
        else if (Physics.Raycast(ray, out hit, Mathf.Infinity, groundLayer) && selectedUnits.Count > 0)
        {
            CursorManager.Instance.SetMarkerType(CursorManager.CursorType.Walkable);
        }
        else
        {
            CursorManager.Instance.SetMarkerType(CursorManager.CursorType.None);
        }
    }

    public void DeselectAllUnits()
    {
        selectedUnits.RemoveAll(unit => unit == null);
        foreach (SpaceUnit unit in selectedUnits)
        {
            unit.gameObject.GetComponent<HardpointManager>().ToggleHighlight(false);
            unit.isSelected = false;
            unit.ToggleSelect(false);
        }
        selectedUnits.Clear();
    }
    public void DeselectUnit(SpaceUnit unit)
    {
        unit.gameObject.GetComponent<HardpointManager>().ToggleHighlight(false);
        unit.isSelected = false;
        unit.ToggleSelect(false);
        selectedUnits.Remove(unit);
        NotifySelection();
    }

    public void SelectUnit(SpaceUnit unit)
    {
        if (unit == null) return;
        unit.isSelected = true;
        if (!selectedUnits.Contains(unit)) selectedUnits.Add(unit);
        unit.ToggleSelect(true);
    }

    public void DragSelect(SpaceUnit unit)
    {
        if (IsDead(unit) || PlayerRetreating) return;

        if (!selectedUnits.Contains(unit))
        {
            unit.isSelected = true;
            unit.ToggleSelect(true);
            selectedUnits.Add(unit);
            //Debug.Log("Added Unit");
        }
    }


}
