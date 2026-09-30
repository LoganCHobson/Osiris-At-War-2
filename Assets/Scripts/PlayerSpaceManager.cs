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

        bool pointerOverUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

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
        else if (Input.GetMouseButtonDown(1)) // Clear selection
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
        if (Input.GetKeyDown(guardKey)) BeginCommand(CommandMode.Guard);
        else if (Input.GetKeyDown(priorityMoveKey)) BeginCommand(CommandMode.PriorityMove);
        else if (Input.GetKeyDown(cancelCommandKey)) CancelCommand();
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
        Vector3 targetCenter = targetManager != null ? targetManager.transform.position : hit.point;

        List<SpaceUnit> attackers = new List<SpaceUnit>();
        foreach (SpaceUnit unit in selectedUnits)
        {
            Transform aimPoint = ResolveAimPoint(hit, targetManager);
            if (aimPoint == null) continue;

            ShipGuard.Cancel(unit);
            unit.GetComponent<HardpointManager>().AssignTarget(aimPoint);
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

    private void PlayerLocation(RaycastHit hit, bool priority = false)
    {
        PruneSelection();
        List<SpaceUnit> movers = selectedUnits.FindAll(unit => unit.IsMobile);
        if (movers.Count == 0) return;

        bool queue = Input.GetKey(KeyCode.LeftShift);

        List<Vector3> origins = new List<Vector3>();
        foreach (SpaceUnit unit in movers)
        {
            origins.Add(queue ? unit.moveState.LastQueuedPosition : unit.transform.position);
        }

        FleetFormation.Slot[] slots = FleetFormation.Move(movers, origins, hit.point);
        int group = SolarStudios.PlayerUnitMoveState.NextOrderGroup();
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
    }

    public void SelectUnit(SpaceUnit unit)
    {
        unit.isSelected = true;
        selectedUnits.Add(unit);
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
