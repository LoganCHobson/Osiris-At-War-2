using UnityEngine;
using UnityEngine.SceneManagement;

// In battle: keeping the mouse over a ship (or squadron) for ShipBlade.HoverDelay seconds slides in its
// info blade; moving off it slides the blade away. Added automatically when the battle scene loads.
public class ShipHover : MonoBehaviour
{
    private const float CheckInterval = 0.1f;
    private const float LeaveGrace = 0.3f; // Brief slips off the hull don't restart the timer.

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
        if (!Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity, unitMask)) return null;

        UnitHealthManager unit = hit.collider.GetComponentInParent<UnitHealthManager>();
        return unit != null && !unit.IsDead ? unit : null;
    }
}
