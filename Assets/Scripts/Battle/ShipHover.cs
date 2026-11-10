using UnityEngine;
using UnityEngine.SceneManagement;

// In battle: keeping the mouse over (or right next to) a ship or squadron for ShipBlade.HoverDelay seconds
// slides in its info blade; moving off it slides the blade away. The pick is forgiving: a direct hit on the
// hull wins, otherwise the unit whose centre is nearest the cursor on screen within PickRadius pixels -
// so small ships and squadrons don't need pixel-perfect aim. Added automatically when the battle scene loads.
public class ShipHover : MonoBehaviour
{
    private const float CheckInterval = 0.1f;
    private const float LeaveGrace = 0.3f; // Brief slips off the hull don't restart the timer.
    private const float PickRadius = 36f;

    private UnitHealthManager hovered;
    private float hoverStart;
    private float lastSeen;
    private float nextCheckAt;
    private bool showing;
    private int unitMask;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Hook()
    {
        SceneManager.sceneLoaded += (scene, _) => AttachTo(scene);
        AttachTo(SceneManager.GetActiveScene());
    }

    private static void AttachTo(Scene scene)
    {
        if (scene.name != "BattleScene" || FindFirstObjectByType<ShipHover>() != null) return;
        new GameObject("ShipHover").AddComponent<ShipHover>();
    }

    private void Awake()
    {
        unitMask = LayerMask.GetMask("FriendlyUnit", "EnemyUnit");
    }

    private void Update()
    {
        if (Time.unscaledTime < nextCheckAt) return;
        nextCheckAt = Time.unscaledTime + CheckInterval;

        UnitHealthManager under = UnitUnderMouse();
        if (under != null)
        {
            if (under != hovered)
            {
                hovered = under;
                hoverStart = Time.unscaledTime;
                if (showing) ShowBlade(); // Already open: switch straight to the new ship.
            }
            lastSeen = Time.unscaledTime;
        }
        else if (hovered != null && Time.unscaledTime - lastSeen > LeaveGrace)
        {
            hovered = null;
            showing = false;
            ShipBlade.Hide();
        }

        if (hovered != null && !showing && Time.unscaledTime - hoverStart >= ShipBlade.HoverDelay)
        {
            ShowBlade();
        }
    }

    private void ShowBlade()
    {
        Ship ship = hovered.sourceShip != null ? hovered.sourceShip : hovered.GetComponent<Ship>();
        if (ship == null) return;
        ShipBlade.Show(ship, hovered);
        showing = true;
    }

    private UnitHealthManager UnitUnderMouse()
    {
        Camera cam = Camera.main;
        if (cam == null || GameSpeed.MenuPaused) return null;
        if (UIKit.PointerOverScreenUI()) return null;

        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity, unitMask))
        {
            UnitHealthManager unit = hit.collider.GetComponentInParent<UnitHealthManager>();
            if (unit != null && !unit.IsDead && BattleSensors.PlayerCanSee(unit)) return unit;
        }

        return NearestOnScreen(cam, Input.mousePosition);
    }

    public static UnitHealthManager NearestOnScreen(Camera cam, Vector2 mouse, float radius = PickRadius)
    {
        UnitHealthManager best = null;
        float bestDistance = radius * radius;
        foreach (UnitHealthManager unit in UnitHealthManager.Active)
        {
            if (unit == null || unit.IsDead || !BattleSensors.PlayerCanSee(unit)) continue;

            Vector3 screen = cam.WorldToScreenPoint(unit.transform.position);
            if (screen.z <= 0f) continue;

            float distance = ((Vector2)screen - mouse).sqrMagnitude;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = unit;
            }
        }
        return best;
    }
}
