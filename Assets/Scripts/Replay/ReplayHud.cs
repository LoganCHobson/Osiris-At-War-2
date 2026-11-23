using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// The replay's controls: a timeline to drag through the battle, play/pause, jumps back and forward, speed, and the
// camera. Keys: Space play/pause, Left/Right 5 s back/forward (Shift: 30 s), [ and ] slower/faster, C cinematic
// or free camera, Tab next shot, H hide the controls, Esc leave the replay.
public class ReplayHud : MonoBehaviour
{
    private ReplayPlayer player;
    private Canvas canvas;
    private RectTransform panel, topBar, bottomBar;
    private Slider timeline;
    private TMP_Text clock, playLabel, speedLabel, cameraLabel, shotLabel;
    private bool hidden, dragging;
    private float letterbox;

    public void Begin(ReplayPlayer owner)
    {
        player = owner;
        canvas = UIKit.Canvas("ReplayHud", 900);
        canvas.transform.SetParent(transform, false);

        // Cinematic letterbox, under the controls.
        topBar = UIKit.Rect("Letterbox Top", canvas.transform, new Vector2(0f, 1f), Vector2.one, new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 0f));
        UIKit.Image(topBar, Color.black);
        bottomBar = UIKit.Rect("Letterbox Bottom", canvas.transform, Vector2.zero, new Vector2(1f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(0f, 0f));
        UIKit.Image(bottomBar, Color.black);

        panel = UIKit.Rect("Controls", canvas.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 24f), new Vector2(1500f, 118f));
        UIKit.Image(panel, UIKit.PanelColor, true);

        TMP_Text title = UIKit.Text(panel, "<b>BATTLE REPLAY</b>", 20f, UIKit.Accent);
        Place(title.rectTransform, 24f, -10f, 260f, 28f);
        shotLabel = UIKit.Text(panel, "", 20f, UIKit.MutedColor);
        Place(shotLabel.rectTransform, 300f, -10f, 900f, 28f);
        TMP_Text keys = UIKit.Text(panel, "Space play  ←/→ skip  [ ] speed  C camera  Tab next shot  H hide  Esc leave", 16f, UIKit.MutedColor, TextAlignmentOptions.MidlineRight);
        Place(keys.rectTransform, 760f, -10f, 716f, 28f);

        RectTransform area = UIKit.Rect("Timeline", panel, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, -44f), new Vector2(1452f, 26f));
        timeline = UIKit.Slider(area, player.Start0, Mathf.Max(player.Start0 + 0.01f, player.End), player.Now, v => { if (!syncing) player.Seek(v); }, v => "");
        timeline.GetComponent<RectTransform>().anchorMax = new Vector2(1f, 0.5f);
        EventTrigger drag = timeline.gameObject.AddComponent<EventTrigger>();
        Add(drag, EventTriggerType.PointerDown, () => dragging = true);
        Add(drag, EventTriggerType.PointerUp, () => dragging = false);

        float x = 24f, y = -78f;
        Button(ref x, y, 70f, "-30s", () => player.Skip(-30f));
        Button(ref x, y, 70f, "-5s", () => player.Skip(-5f));
        playLabel = Button(ref x, y, 110f, "Pause", () => player.SetPaused(!player.Paused));
        Button(ref x, y, 70f, "+5s", () => player.Skip(5f));
        Button(ref x, y, 70f, "+30s", () => player.Skip(30f));
        x += 20f;
        Button(ref x, y, 44f, "-", () => Step(-1));
        speedLabel = Label(ref x, y, 80f, "1x");
        Button(ref x, y, 44f, "+", () => Step(1));
        x += 20f;
        cameraLabel = Button(ref x, y, 190f, "Cinematic", () => player.Camera.Toggle());
        Button(ref x, y, 130f, "Next shot", () => player.Camera.NextShot());
        clock = UIKit.Text(panel, "", 22f, UIKit.TextColor, TextAlignmentOptions.MidlineRight);
        Place(clock.rectTransform, 1100f, y, 220f, 30f);
        float exitX = 1476f - 130f;
        Button(ref exitX, y, 130f, "Leave", () => player.Exit());
    }

    private bool syncing;

    private void Step(int direction)
    {
        float[] speeds = ReplayPlayer.Speeds;
        int index = 0;
        for (int i = 0; i < speeds.Length; i++) if (Mathf.Abs(speeds[i] - player.Speed) < Mathf.Abs(speeds[index] - player.Speed)) index = i;
        player.SetSpeed(speeds[Mathf.Clamp(index + direction, 0, speeds.Length - 1)]);
    }

    private void Update()
    {
        if (player == null) return;
        bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        if (Input.GetKeyDown(KeyCode.Space)) player.SetPaused(!player.Paused);
        if (Input.GetKeyDown(KeyCode.LeftArrow) && player.Camera.CurrentMode == ReplayCamera.Mode.Cinematic) player.Skip(shift ? -30f : -5f);
        if (Input.GetKeyDown(KeyCode.RightArrow) && player.Camera.CurrentMode == ReplayCamera.Mode.Cinematic) player.Skip(shift ? 30f : 5f);
        if (Input.GetKeyDown(KeyCode.LeftBracket)) Step(-1);
        if (Input.GetKeyDown(KeyCode.RightBracket)) Step(1);
        if (Input.GetKeyDown(KeyCode.C)) player.Camera.Toggle();
        if (Input.GetKeyDown(KeyCode.Tab)) player.Camera.NextShot();
        if (Input.GetKeyDown(KeyCode.H)) hidden = !hidden;
        if (Input.GetKeyDown(KeyCode.Escape)) { player.Exit(); return; }

        panel.gameObject.SetActive(!hidden);
        if (!dragging)
        {
            syncing = true;
            timeline.SetValueWithoutNotify(player.Now);
            syncing = false;
        }
        clock.text = $"{Format(player.Now - player.Start0)} / {Format(player.End - player.Start0)}";
        playLabel.text = player.Paused ? "Play" : "Pause";
        speedLabel.text = player.Speed < 1f ? $"{player.Speed:0.##}x" : $"{player.Speed:0}x";
        bool cinematic = player.Camera.CurrentMode == ReplayCamera.Mode.Cinematic;
        cameraLabel.text = cinematic ? "Camera: Cinematic" : "Camera: Free";
        shotLabel.text = player.Camera.ShotName;

        // Letterbox for the cinematic camera, as the battle's own cinematic mode frames it.
        float target = cinematic ? 1f : 0f;
        letterbox = Mathf.MoveTowards(letterbox, target, Time.unscaledDeltaTime * 2.5f);
        float aspect = (float)Screen.width / Mathf.Max(1, Screen.height);
        float bar = Mathf.Max(0f, 1080f * 0.5f * (1f - aspect / 2.35f)) * letterbox;
        topBar.sizeDelta = new Vector2(0f, bar);
        bottomBar.sizeDelta = new Vector2(0f, bar);
    }

    private static string Format(float seconds)
    {
        int s = Mathf.Max(0, Mathf.FloorToInt(seconds));
        return $"{s / 60}:{s % 60:00}";
    }

    private static void Place(RectTransform rect, float x, float y, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(x, y);
        rect.sizeDelta = new Vector2(width, height);
    }

    private TMP_Text Button(ref float x, float y, float width, string text, UnityEngine.Events.UnityAction onClick)
    {
        RectTransform rect = UIKit.Rect(text, panel, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(x, y), new Vector2(width, 32f));
        Image image = UIKit.Image(rect, new Color(1f, 1f, 1f, 0.08f), true);
        Button button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        ColorBlock colors = button.colors;
        colors.highlightedColor = new Color(1f, 0.74f, 0.32f, 1f);
        colors.pressedColor = new Color(1f, 0.6f, 0.2f, 1f);
        button.colors = colors;
        button.onClick.AddListener(onClick);
        TMP_Text label = UIKit.Text(rect, text, 18f, UIKit.TextColor, TextAlignmentOptions.Center);
        x += width + 8f;
        return label;
    }

    private TMP_Text Label(ref float x, float y, float width, string text)
    {
        RectTransform rect = UIKit.Rect(text, panel, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(x, y), new Vector2(width, 32f));
        TMP_Text label = UIKit.Text(rect, text, 20f, UIKit.TextColor, TextAlignmentOptions.Center);
        x += width + 8f;
        return label;
    }

    private static void Add(EventTrigger trigger, EventTriggerType type, System.Action action)
    {
        EventTrigger.Entry entry = new EventTrigger.Entry { eventID = type };
        entry.callback.AddListener(_ => action());
        trigger.triggers.Add(entry);
    }
}
