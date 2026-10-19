using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

// Hover feel for menu buttons: the label brightens and slides right, the accent bar grows in.
public class MenuButtonFeel : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    private TMP_Text label;
    private RectTransform bar;
    private bool active;
    private Color baseColor;
    private Color accent;
    private bool hovered;
    private float blend;

    public void Bind(TMP_Text text, RectTransform accentBar, bool enabled, Color textColor, Color accentColor)
    {
        label = text;
        bar = accentBar;
        active = enabled;
        baseColor = textColor;
        accent = accentColor;
        bar.localScale = new Vector3(1f, 0f, 1f);
    }

    public void OnPointerEnter(PointerEventData eventData) => hovered = active;
    public void OnPointerExit(PointerEventData eventData) => hovered = false;

    private void Update()
    {
        if (label == null) return;

        blend = Mathf.MoveTowards(blend, hovered ? 1f : 0f, Time.unscaledDeltaTime * 7f);
        float eased = blend * blend * (3f - 2f * blend);
        if (active) label.color = Color.Lerp(baseColor, Color.white, eased);
        label.rectTransform.offsetMin = new Vector2(24f + 14f * eased, 0f);
        bar.localScale = new Vector3(1f, eased, 1f);
    }
}
