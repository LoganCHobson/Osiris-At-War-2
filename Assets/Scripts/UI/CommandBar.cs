using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class CommandBar : MonoBehaviour
{
    public PlayerSpaceManager manager;
    public GameObject bar;
    public Image guardButtonImage;
    public Image priorityMoveButtonImage;
    public GameObject hint;
    public TMP_Text hintText;

    public Color idleColor = new Color(0.16f, 0.22f, 0.3f, 1f);
    public Color activeColor = new Color(0.2f, 0.55f, 0.85f, 1f);

    [TextArea] public string guardHint = "GUARD  -  Click a friendly ship to escort it.   Right-click / Esc to cancel";
    [TextArea] public string priorityMoveHint = "PRIORITY MOVE  -  Click a destination. Ships in the way will make room.   Right-click / Esc to cancel";

    private void OnEnable()
    {
        if (manager != null) manager.ModeChanged += Show;
        Show(manager != null ? manager.Mode : PlayerSpaceManager.CommandMode.None);
    }

    private void OnDisable()
    {
        if (manager != null) manager.ModeChanged -= Show;
    }

    private void LateUpdate()
    {
        bool visible = manager != null && manager.HasSelection;
        if (bar.activeSelf != visible)
        {
            bar.SetActive(visible);
        }
    }

    public void Guard()
    {
        manager.BeginCommand(PlayerSpaceManager.CommandMode.Guard);
    }

    public void PriorityMove()
    {
        manager.BeginCommand(PlayerSpaceManager.CommandMode.PriorityMove);
    }

    private void Show(PlayerSpaceManager.CommandMode mode)
    {
        guardButtonImage.color = mode == PlayerSpaceManager.CommandMode.Guard ? activeColor : idleColor;
        priorityMoveButtonImage.color = mode == PlayerSpaceManager.CommandMode.PriorityMove ? activeColor : idleColor;

        hint.SetActive(mode != PlayerSpaceManager.CommandMode.None);
        hintText.text = mode == PlayerSpaceManager.CommandMode.Guard ? guardHint : priorityMoveHint;
    }
}
