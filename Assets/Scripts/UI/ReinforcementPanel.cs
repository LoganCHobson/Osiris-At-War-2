using TMPro;
using UnityEngine;

public class ReinforcementPanel : MonoBehaviour
{
    private const string WidthKey = "ReinforcementPanel.Width";
    private const string CollapsedKey = "ReinforcementPanel.Collapsed";

    public RectTransform window;
    public GameObject body;
    public Transform iconContainer;
    public TMP_Text titleText;
    public TMP_Text toggleLabel;

    public float minWidth = 120f;
    public float maxWidth = 640f;
    public string expandedGlyph = "-";
    public string collapsedGlyph = "+";

    private int shownCount = -1;
    private bool collapsed;

    private void Start()
    {
        collapsed = PlayerPrefs.GetInt(CollapsedKey, 0) == 1;
        SetWidth(PlayerPrefs.GetFloat(WidthKey, window.sizeDelta.x));
        ApplyCollapsed();
        Refresh();
    }

    private void LateUpdate()
    {
        Refresh();
    }

    private void Refresh()
    {
        int count = CountIcons();
        if (count == shownCount) return;

        shownCount = count;
        window.gameObject.SetActive(count > 0);
        titleText.text = $"Reinforcements ({count})";
    }

    private int CountIcons()
    {
        int count = 0;
        foreach (Transform child in iconContainer)
        {
            if (child.GetComponent<IconShipRef>() != null)
            {
                count++;
            }
        }
        return count;
    }

    public void ToggleCollapsed()
    {
        collapsed = !collapsed;
        PlayerPrefs.SetInt(CollapsedKey, collapsed ? 1 : 0);
        ApplyCollapsed();
    }

    private void ApplyCollapsed()
    {
        body.SetActive(!collapsed);
        toggleLabel.text = collapsed ? collapsedGlyph : expandedGlyph;
    }

    public void Resize(float deltaWidth)
    {
        SetWidth(window.sizeDelta.x + deltaWidth);
    }

    public void SaveSize()
    {
        PlayerPrefs.SetFloat(WidthKey, window.sizeDelta.x);
    }

    private void SetWidth(float width)
    {
        window.sizeDelta = new Vector2(Mathf.Clamp(width, minWidth, maxWidth), window.sizeDelta.y);
    }
}
