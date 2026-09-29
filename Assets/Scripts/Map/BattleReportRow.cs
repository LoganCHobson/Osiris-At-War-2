using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class BattleReportRow : MonoBehaviour
{
    public Image iconImage;
    public TMP_Text nameText;
    public TMP_Text countText;

    public void Bind(Sprite icon, string label, int count)
    {
        if (iconImage != null)
        {
            iconImage.sprite = icon;
            iconImage.enabled = icon != null;
        }

        nameText.text = label;
        countText.text = $"x{count}";
    }
}
