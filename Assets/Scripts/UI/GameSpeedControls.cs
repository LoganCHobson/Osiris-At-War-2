using UnityEngine;
using UnityEngine.UI;

public class GameSpeedControls : MonoBehaviour
{
    public float fastSpeed = 2f;

    [Header("Buttons")]
    public Button pauseButton;
    public Button playButton;
    public Button fastButton;

    [Header("Highlight")]
    public Color normalColor = new Color(0.16f, 0.22f, 0.3f, 1f);
    public Color activeColor = new Color(0.25f, 0.55f, 0.85f, 1f);

    private float shownSpeed = -1f;

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
        if (!Mathf.Approximately(shownSpeed, GameSpeed.Speed))
        {
            RefreshHighlight();
        }
    }

    public void Pause()
    {
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
