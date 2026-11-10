using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

// F1: a one-screen cheat sheet of the controls for wherever you are (battle or galactic map).
// F1 or Escape closes it. Added automatically to both scenes.
public class HelpOverlay : MonoBehaviour
{
    private const KeyCode ToggleKey = KeyCode.F1;

    private static HelpOverlay current;
    public static bool IsOpen => current != null && current.shade != null && current.shade.gameObject.activeSelf;

    private void Awake() { current = this; }
    private void OnDestroy() { if (current == this) current = null; }

    private RectTransform shade;
    private TMP_Text left;
    private TMP_Text right;
    private bool battle;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Hook()
    {
        SceneManager.sceneLoaded += (scene, _) => AttachTo(scene);
        AttachTo(SceneManager.GetActiveScene());
    }

    private static void AttachTo(Scene scene)
    {
        bool isBattle = scene.name == "BattleScene";
        if ((!isBattle && scene.name != "GalacticMap") || FindFirstObjectByType<HelpOverlay>() != null) return;

        Canvas canvas = UIKit.Canvas("HelpCanvas", 960);
        HelpOverlay overlay = canvas.gameObject.AddComponent<HelpOverlay>();
        overlay.battle = isBattle;
        overlay.Build(canvas.transform);
    }

    private void Build(Transform canvas)
    {
        // A small permanent hint so the overlay can be found.
        TMP_Text hint = UIKit.Text(canvas, "F1  CONTROLS", 12f, UIKit.MutedColor, battle ? TextAlignmentOptions.TopRight : TextAlignmentOptions.BottomRight);
        hint.characterSpacing = 6f;
        hint.rectTransform.offsetMin = new Vector2(0f, 8f);
        hint.rectTransform.offsetMax = new Vector2(battle ? -16f : -20f, battle ? -8f : 0f);

        shade = UIKit.Fill("Shade", canvas);
        UIKit.Image(shade, new Color(0f, 0f, 0f, 0.7f), true);

        RectTransform panel = UIKit.Rect("Help", shade, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1180f, 760f));
        UIKit.Image(panel, UIKit.PanelColor, true);
        RectTransform rule = UIKit.Rect("Rule", panel, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 2f));
        UIKit.Image(rule, UIKit.Accent);

        TMP_Text title = UIKit.Text(panel, battle ? "BATTLE CONTROLS" : "GALACTIC MAP CONTROLS", 26f, UIKit.TextColor, TextAlignmentOptions.TopLeft);
        title.characterSpacing = 10f;
        title.rectTransform.offsetMin = new Vector2(32f, 0f);
        title.rectTransform.offsetMax = new Vector2(0f, -20f);

        TMP_Text close = UIKit.Text(panel, "F1 / ESC TO CLOSE", 13f, UIKit.MutedColor, TextAlignmentOptions.TopRight);
        close.characterSpacing = 6f;
        close.rectTransform.offsetMax = new Vector2(-28f, -26f);

        left = Column(panel, 32f);
        right = Column(panel, 610f);
        Fill();

        shade.gameObject.SetActive(false);
    }

    private static TMP_Text Column(RectTransform panel, float x)
    {
        TMP_Text text = UIKit.Text(panel, "", 16f, UIKit.TextColor, TextAlignmentOptions.TopLeft);
        RectTransform rect = text.rectTransform;
        rect.anchorMin = new Vector2(0f, 0f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(x, -76f);
        rect.sizeDelta = new Vector2(540f, -100f);
        text.textWrappingMode = TextWrappingModes.Normal;
        text.richText = true;
        text.lineSpacing = 4f;
        return text;
    }

    private void Fill()
    {
        StringBuilder a = new StringBuilder();
        StringBuilder b = new StringBuilder();

        if (battle)
        {
            Section(a, "SELECTING");
            Row(a, "Click / drag", "Select a ship / box-select");
            Row(a, "Shift + click", "Add to the selection");
            Row(a, "Double-click", "Select every ship of that class");
            Row(a, "Ctrl + 0-9", "Save the selection as a group");
            Row(a, "0-9", "Recall a group (Shift: add it)");
            Row(a, "Right-click", "Clear the selection");
            Row(a, "Selection bar", "Click: only that ship  ·  Ctrl: that class  ·  Right/Shift: remove");

            Section(a, "ORDERS");
            Row(a, "Click open space", "Move (Shift: queue a waypoint)");
            Row(a, "Click an enemy", "Attack it  ·  click a hardpoint to target it");
            Row(a, "G", "Guard a friendly ship");
            Row(a, "F", "Priority move (others make way)");
            Row(a, "Esc", "Cancel the current command");

            Section(a, "REINFORCEMENTS");
            Row(a, "Drag an icon", "Drop a ship onto the battlefield");
            Row(a, "Mouse wheel (dragging)", "Set the drop height  ·  End: back to the plane");
            Row(a, "Shift + drop", "Drop as many of that class as fit");

            Section(b, "3D FLIGHT");
            Row(b, "PageUp / PageDown", "Climb / dive (Shift: bigger steps)");
            Row(b, "End", "Back down to the battle plane");
            Row(b, "Alt + drag open space", "Move, dragging up/down to set the height");
            Row(b, "Z / X", "Roll");
            Row(b, "R / T", "Pitch nose up / down");
            Row(b, "Home", "Level out - back to automatic angling");
            Row(b, "L", "Vertical formation: flat / wall / layers");
            Row(b, "H", "Show / hide the side view");

            Section(b, "BATTLEFIELD");
            Row(b, "Shield generator", "Avanti shields come from a hardpoint on the spine: destroy it and they never return");
            Row(b, "Nebulae / ion storms", "Shields fail inside / strike craft are grounded");
            Row(b, "Sensors", "Need a destroyer+ to see the whole map; small craft hide in asteroids");

            Section(b, "ABILITIES");
            Row(b, "B", "Selected ships use their class ability");

            Section(b, "STANCES");
            Row(b, "Y", "Cycle the selection's stance: aggressive / hold / skirmish");

            Section(b, "CAMERA");
            Row(b, "WASD / arrows / edges", "Pan (Shift: faster)");
            Row(b, "Mouse wheel", "Zoom");
            Row(b, "Q / E", "Rotate");
            Row(b, "Alt + middle-drag", "Orbit and tilt");
            Row(b, "C / V", "Cinematic camera / overview");

            Section(b, "GAME");
            Row(b, "Space", "Pause / resume");
            Row(b, "1 / 2 / 3, keypad", "Game speed (number keys yield to groups)");
            Row(b, "F5 / F9", "Quicksave / quickload");
        }
        else
        {
            Section(a, "FLEETS");
            Row(a, "Click a fleet", "Select it");
            Row(a, "Drag a fleet to a planet", "Send it there");
            Row(a, "Click a planet", "Open its build menu");
            Row(a, "Esc", "Close panels");

            Section(a, "PRODUCTION");
            Row(a, "P", "Production: design fleets, order them from every shipyard");
            Row(a, "BUILD & SEND HERE", "Order a fleet delivered to this planet");
            Row(a, "BUILD FLEET AT THIS YARD", "Order a fleet from this shipyard only");
            Row(a, "KEEP TOPPED UP", "Standing order: keep the destination topped up to the design");

            Section(b, "DEFENSE");
            Row(b, "DEFENSE LAYOUT", "Lay out how this planet's defenders stand");
            Row(b, "Mouse wheel on a marker", "Set its height");

            Section(b, "BATTLES");
            Row(b, "Battle prompt", "Shows the expected autoresolve losses before you choose");

            Section(b, "GAME");
            Row(b, "Space", "Pause / resume");
            Row(b, "1 / 2 / 3, keypad", "Game speed");
            Row(b, "F5 / F9", "Quicksave / quickload");
            Row(b, "F3 / F4", "AI debug overlay / cycle faction");
        }

        left.text = a.ToString();
        right.text = b.ToString();
    }

    private static void Section(StringBuilder text, string title)
    {
        if (text.Length > 0) text.AppendLine("<size=8> </size>");
        text.AppendLine($"<color=#{ColorUtility.ToHtmlStringRGB(UIKit.Accent)}><size=14><cspace=6>{title}</cspace></size></color>");
    }

    private static void Row(StringBuilder text, string keys, string action)
    {
        text.AppendLine($"<b>{keys}</b><pos=42%><color=#{ColorUtility.ToHtmlStringRGBA(UIKit.MutedColor)}>{action}</color>");
    }

    private void Update()
    {
        bool open = shade.gameObject.activeSelf;
        if (Keybinds.Down(KeyAction.Help) || (open && Input.GetKeyDown(KeyCode.Escape)))
        {
            if (open && Input.GetKeyDown(KeyCode.Escape)) UIBits.EscapeConsumedFrame = Time.frameCount;
            shade.gameObject.SetActive(!open);
        }
    }
}
