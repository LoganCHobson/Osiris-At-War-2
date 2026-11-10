using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Battle HUD: a side-on view of the fight in the bottom-left corner - left/right is across your screen,
// up/down is height above or below the battle plane - so you can see at a glance who is over and under
// whom without orbiting the camera. Your ships are blue, the enemy red, your selection bright and larger.
// Click a dot to select that ship. H hides/shows it. Added automatically when the battle scene loads.
public class SideViewStrip : MonoBehaviour
{
    private const float Width = 380f;
    private const float Height = 150f;
    private const float Padding = 10f;
    private const float RefreshInterval = 0.1f;
    private const string PrefKey = "osiris.sideView";

    private static readonly Color Friendly = new Color(0.35f, 0.8f, 1f, 0.85f);
    private static readonly Color Enemy = new Color(1f, 0.4f, 0.35f, 0.85f);
    private static readonly Color SelectedColor = new Color(0.75f, 1f, 0.8f, 1f);

    private RectTransform panel;
    private RectTransform plot;
    private readonly List<Dot> dots = new List<Dot>();
    private float nextRefreshAt;
    private bool visible = true;

    private class Dot
    {
        public RectTransform rect;
        public Image image;
        public DotClick click;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Hook()
    {
        SceneManager.sceneLoaded += (scene, _) => AttachTo(scene);
        AttachTo(SceneManager.GetActiveScene());
    }

    private static void AttachTo(Scene scene)
    {
        if (scene.name != "BattleScene" || FindFirstObjectByType<SideViewStrip>() != null) return;
        Canvas canvas = UIKit.Canvas("SideViewCanvas", 38);
        canvas.gameObject.AddComponent<SideViewStrip>().Build(canvas.transform);
    }

    private void Build(Transform canvas)
    {
        panel = UIKit.Rect("SideView", canvas, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(16f, 16f), new Vector2(Width, Height));
        UIKit.Image(panel, new Color(UIKit.PanelColor.r, UIKit.PanelColor.g, UIKit.PanelColor.b, 0.8f), true);

        TMP_Text title = UIKit.Text(panel, "SIDE VIEW  [H]", 11f, UIKit.MutedColor, TextAlignmentOptions.TopLeft);
        title.characterSpacing = 6f;
        title.rectTransform.offsetMin = new Vector2(8f, 0f);
        title.rectTransform.offsetMax = new Vector2(0f, -4f);

        plot = UIKit.Rect("Plot", panel, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(0f, -6f), new Vector2(-Padding * 2f, -Padding * 2f - 12f));

        // Plane line and height marks.
        RectTransform plane = UIKit.Rect("Plane", plot, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(0f, 1f));
        UIKit.Image(plane, new Color(1f, 1f, 1f, 0.25f));
        foreach (float mark in new[] { -0.5f, 0.5f })
        {
            RectTransform line = UIKit.Rect("Mark", plot, new Vector2(0f, 0.5f + mark * 0.5f), new Vector2(1f, 0.5f + mark * 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(0f, 1f));
            UIKit.Image(line, new Color(1f, 1f, 1f, 0.06f));
        }
        TMP_Text top = UIKit.Text(plot, $"+{ShipAltitude.MaxAltitude:0}", 10f, UIKit.MutedColor, TextAlignmentOptions.TopRight);
        TMP_Text bottom = UIKit.Text(plot, $"-{ShipAltitude.MaxAltitude:0}", 10f, UIKit.MutedColor, TextAlignmentOptions.BottomRight);
        top.raycastTarget = bottom.raycastTarget = false;

        try { visible = PlayerPrefs.GetInt(PrefKey, 1) == 1; } catch { visible = true; }
        panel.gameObject.SetActive(visible);
    }

    private void Update()
    {
        if (Keybinds.Down(KeyAction.SideView))
        {
            visible = !visible;
            panel.gameObject.SetActive(visible);
            PlayerPrefs.SetInt(PrefKey, visible ? 1 : 0);
        }

        if (!visible || Time.unscaledTime < nextRefreshAt) return;
        nextRefreshAt = Time.unscaledTime + RefreshInterval;
        Refresh();
    }

    private void Refresh()
    {
        Camera cam = Camera.main;
        if (cam == null) return;

        Vector3 across = Vector3.ProjectOnPlane(cam.transform.right, Vector3.up).normalized;
        Vector3 focus = cam.transform.position + cam.transform.forward * 100f;
        bool playerAttacker = GameManager.Instance == null || GameManager.Instance.PlayerIsAttacker;

        // Fit everyone across the strip.
        float min = float.MaxValue, max = float.MinValue;
        foreach (ShipAltitude ship in ShipAltitude.All)
        {
            if (!Alive(ship)) continue;
            float x = Vector3.Dot(ship.transform.position - focus, across);
            min = Mathf.Min(min, x);
            max = Mathf.Max(max, x);
        }
        if (min > max)
        {
            foreach (Dot dot in dots) dot.rect.gameObject.SetActive(false);
            return;
        }
        float span = Mathf.Max(200f, max - min) * 1.1f;
        float centre = (min + max) * 0.5f;
        Vector2 size = plot.rect.size;

        int used = 0;
        foreach (ShipAltitude ship in ShipAltitude.All)
        {
            if (!Alive(ship)) continue;
            if (used == dots.Count) dots.Add(CreateDot());
            Dot dot = dots[used++];

            UnitHealthManager health = ship.GetComponent<UnitHealthManager>();
            SpaceUnit unit = ship.GetComponent<SpaceUnit>();
            bool mine = health == null || health.isAttackerSide == playerAttacker;
            bool selected = mine && unit != null && unit.isSelected;

            float x = (Vector3.Dot(ship.transform.position - focus, across) - centre) / span;
            float y = ship.Altitude / ShipAltitude.MaxAltitude * 0.5f;
            float dotSize = (ship.Airborne ? 4f : Mathf.Clamp(ship.Radius * 0.12f, 5f, 11f)) * (selected ? 1.5f : 1f);

            dot.rect.gameObject.SetActive(true);
            dot.rect.anchoredPosition = new Vector2(x * size.x, y * size.y);
            dot.rect.sizeDelta = new Vector2(dotSize, dotSize);
            dot.image.color = selected ? SelectedColor : mine ? Friendly : Enemy;
            dot.click.unit = mine ? unit : null;
        }

        for (int i = used; i < dots.Count; i++) dots[i].rect.gameObject.SetActive(false);
    }

    private static bool Alive(ShipAltitude ship)
    {
        if (ship == null || !ship.isActiveAndEnabled) return false;
        UnitHealthManager health = ship.GetComponent<UnitHealthManager>();
        return health == null || (!health.IsDead && BattleSensors.PlayerCanSee(health));
    }

    private Dot CreateDot()
    {
        RectTransform rect = UIKit.Rect("Dot", plot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(6f, 6f));
        Image image = UIKit.Image(rect, Friendly, true);
        DotClick click = rect.gameObject.AddComponent<DotClick>();
        return new Dot { rect = rect, image = image, click = click };
    }

    private class DotClick : MonoBehaviour, IPointerClickHandler
    {
        public SpaceUnit unit;

        public void OnPointerClick(PointerEventData eventData)
        {
            PlayerSpaceManager manager = PlayerSpaceManager.Instance;
            if (unit == null || manager == null) return;

            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            if (shift) manager.SelectUnit(unit);
            else manager.SelectOnly(new[] { unit });
            manager.NotifySelection();
        }
    }
}
