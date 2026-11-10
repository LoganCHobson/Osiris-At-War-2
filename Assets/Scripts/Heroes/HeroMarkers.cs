using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Battle HUD: each hero's hologram portrait floats above their flagship so you can find them at a glance -
// framed blue for yours, red for the enemy's, with their name beneath. Click one of yours to select the
// flagship. Hidden when the ship is off-screen, hidden by sensors, or while the cinematic camera is on.
public class HeroMarkers : MonoBehaviour
{
    private const float Size = 54f;
    private const float MinScale = 0.65f;
    private static readonly Color Friendly = new Color(0.35f, 0.8f, 1f);
    private static readonly Color Enemy = new Color(1f, 0.4f, 0.35f);

    private RectTransform root;
    private readonly Dictionary<UnitHealthManager, Marker> markers = new Dictionary<UnitHealthManager, Marker>();
    private readonly List<UnitHealthManager> stale = new List<UnitHealthManager>();

    private class Marker
    {
        public RectTransform rect;
        public Image frame;
        public Image portrait;
        public TMP_Text label;
        public MarkerClick click;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Hook()
    {
        SceneManager.sceneLoaded += (scene, _) => AttachTo(scene);
        AttachTo(SceneManager.GetActiveScene());
    }

    private static void AttachTo(Scene scene)
    {
        if (scene.name != "BattleScene" || FindFirstObjectByType<HeroMarkers>() != null) return;
        Canvas canvas = UIKit.Canvas("HeroMarkerCanvas", 30);
        HeroMarkers markers = canvas.gameObject.AddComponent<HeroMarkers>();
        markers.root = UIKit.Fill("Markers", canvas.transform);
    }

    private void LateUpdate()
    {
        Camera cam = Camera.main;
        bool show = cam != null && !CinematicCamera.Active;
        bool playerAttacker = GameManager.Instance == null || GameManager.Instance.PlayerIsAttacker;
        RectTransform canvasRect = (RectTransform)root.parent;

        stale.Clear();
        stale.AddRange(markers.Keys);

        foreach ((HeroDefinition hero, UnitHealthManager unit, bool attackerSide) in HeroForces.OnField())
        {
            stale.Remove(unit);
            if (!markers.TryGetValue(unit, out Marker marker))
            {
                marker = Create(hero);
                markers[unit] = marker;
            }

            bool mine = attackerSide == playerAttacker;
            Vector3 anchor = unit.transform.position + Vector3.up * Lift(unit);
            Vector3 screen = show ? cam.WorldToScreenPoint(anchor) : Vector3.zero;
            bool visible = show && screen.z > 0f && BattleSensors.PlayerCanSee(unit)
                && screen.x > -Size && screen.x < Screen.width + Size && screen.y > -Size && screen.y < Screen.height + Size;
            marker.rect.gameObject.SetActive(visible);
            if (!visible) continue;

            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen, null, out Vector2 local);
            marker.rect.anchoredPosition = local;
            float scale = Mathf.Clamp(600f / Mathf.Max(1f, screen.z), MinScale, 1f);
            marker.rect.localScale = Vector3.one * scale;
            marker.frame.color = mine ? Friendly : Enemy;
            marker.label.color = mine ? Friendly : Enemy;
            marker.click.unit = unit;
            marker.click.mine = mine;
        }

        foreach (UnitHealthManager gone in stale)
        {
            if (markers.TryGetValue(gone, out Marker marker) && marker.rect != null) Destroy(marker.rect.gameObject);
            markers.Remove(gone);
        }
    }

    // How far above the hull the marker floats.
    private static float Lift(UnitHealthManager unit)
    {
        ShipAltitude altitude = unit.GetComponent<ShipAltitude>();
        return altitude != null ? altitude.HalfHeight + altitude.Radius * 0.35f + 12f : 30f;
    }

    private Marker Create(HeroDefinition hero)
    {
        RectTransform rect = UIKit.Rect(hero.id, root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(Size, Size));
        Image frame = UIKit.Image(rect, Friendly, true);

        RectTransform portraitRect = UIKit.Fill("Portrait", rect);
        portraitRect.offsetMin = new Vector2(3f, 3f);
        portraitRect.offsetMax = new Vector2(-3f, -3f);
        Image portrait = UIKit.Image(portraitRect, Color.white);
        portrait.sprite = hero.Portrait;
        portrait.preserveAspect = true;
        portrait.enabled = hero.Portrait != null;

        // A small pointer under the frame towards the ship.
        RectTransform pointer = UIKit.Rect("Pointer", rect, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 1f), new Vector2(0f, 0f), new Vector2(10f, 10f));
        pointer.localRotation = Quaternion.Euler(0f, 0f, 45f);
        UIKit.Image(pointer, new Color(1f, 1f, 1f, 0.6f));

        TMP_Text label = UIKit.Text(rect, hero.name, 13f, Friendly, TextAlignmentOptions.Top);
        label.rectTransform.anchorMin = new Vector2(0.5f, 1f);
        label.rectTransform.anchorMax = new Vector2(0.5f, 1f);
        label.rectTransform.pivot = new Vector2(0.5f, 0f);
        label.rectTransform.anchoredPosition = new Vector2(0f, 2f);
        label.rectTransform.sizeDelta = new Vector2(180f, 18f);
        label.fontStyle = FontStyles.Bold;
        label.outlineWidth = 0.2f;
        label.outlineColor = new Color32(0, 0, 0, 200);

        MarkerClick click = rect.gameObject.AddComponent<MarkerClick>();
        return new Marker { rect = rect, frame = frame, portrait = portrait, label = label, click = click };
    }

    private class MarkerClick : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
    {
        public UnitHealthManager unit;
        public bool mine;

        public void OnPointerClick(PointerEventData eventData)
        {
            if (!mine || unit == null || unit.IsDead || PlayerSpaceManager.Instance == null) return;
            SpaceUnit spaceUnit = unit.GetComponent<SpaceUnit>();
            if (spaceUnit != null) PlayerSpaceManager.Instance.SelectOnly(new[] { spaceUnit });
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (unit != null && unit.sourceShip != null) ShipBlade.Show(unit.sourceShip, unit);
        }

        public void OnPointerExit(PointerEventData eventData) => ShipBlade.Hide();
    }
}
