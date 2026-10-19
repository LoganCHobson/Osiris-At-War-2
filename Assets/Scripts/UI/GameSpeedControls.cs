using UnityEngine;
using UnityEngine.UI;

public class GameSpeedControls : MonoBehaviour
{
    public float fastSpeed = 2f;
    public float fastestSpeed = 4f;

    [Header("Hotkeys")]
    public KeyCode pauseToggleKey = KeyCode.Space;
    public KeyCode normalSpeedKey = KeyCode.Alpha1;
    public KeyCode fastSpeedKey = KeyCode.Alpha2;
    public KeyCode fastestSpeedKey = KeyCode.Alpha3;

    [Header("Buttons")]
    public Button pauseButton;
    public Button playButton;
    public Button fastButton;

    [Header("Highlight")]
    public Color normalColor = new Color(0.16f, 0.22f, 0.3f, 1f);
    public Color activeColor = new Color(0.25f, 0.55f, 0.85f, 1f);

    private float shownSpeed = -1f;
    private float resumeSpeed = 1f;

    private void Awake()
    {
        GameSpeed.Reset();
    }

    private void OnDestroy()
    {
        GameSpeed.Reset();
    }

    private void Update()
    {
        HandleHotkeys();

        if (!Mathf.Approximately(shownSpeed, GameSpeed.Speed))
        {
            RefreshHighlight();
        }
    }

    private void HandleHotkeys()
    {
        if (GameSpeed.MenuPaused || IsTyping()) return;

        // A clicked button stays selected, and Unity UI presses the selected button on Space -
        // drop that selection so Space only ever means pause/play.
        UnityEngine.EventSystems.EventSystem events = UnityEngine.EventSystems.EventSystem.current;
        if (events != null && events.currentSelectedGameObject != null && events.currentSelectedGameObject.GetComponent<Button>() != null)
        {
            events.SetSelectedGameObject(null);
        }

        if (Input.GetKeyDown(pauseToggleKey)) TogglePause();
        else if (Input.GetKeyDown(normalSpeedKey) || Input.GetKeyDown(KeyCode.Keypad1)) GameSpeed.SetSpeed(1f);
        else if (Input.GetKeyDown(fastSpeedKey) || Input.GetKeyDown(KeyCode.Keypad2)) GameSpeed.SetSpeed(fastSpeed);
        else if (Input.GetKeyDown(fastestSpeedKey) || Input.GetKeyDown(KeyCode.Keypad3)) GameSpeed.SetSpeed(fastestSpeed);
    }

    // Space flips between paused and whatever speed you were running at.
    public void TogglePause()
    {
        if (GameSpeed.Speed <= 0f)
        {
            GameSpeed.SetSpeed(resumeSpeed);
        }
        else
        {
            resumeSpeed = GameSpeed.Speed;
            GameSpeed.SetSpeed(0f);
        }
    }

    private static bool IsTyping()
    {
        GameObject selected = UnityEngine.EventSystems.EventSystem.current != null ? UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject : null;
        return selected != null && (selected.GetComponent<TMPro.TMP_InputField>() != null || selected.GetComponent<InputField>() != null);
    }

    public void Pause()
    {
        if (GameSpeed.Speed > 0f) resumeSpeed = GameSpeed.Speed;
        GameSpeed.SetSpeed(0f);
    }

    public void Play()
    {
        GameSpeed.SetSpeed(1f);
    }

    public void FastForward()
    {
        GameSpeed.SetSpeed(fastSpeed);
    }

    private void RefreshHighlight()
    {
        shownSpeed = GameSpeed.Speed;
        Highlight(pauseButton, shownSpeed <= 0f);
        Highlight(playButton, Mathf.Approximately(shownSpeed, 1f));
        Highlight(fastButton, shownSpeed > 1f);
    }

    private void Highlight(Button button, bool active)
    {
        if (button == null || button.targetGraphic == null) return;
        button.targetGraphic.color = active ? activeColor : normalColor;
    }
}
