using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Tab button look: lit when it's the open tab.
public class MenuTabFeel : MonoBehaviour
{
    private Image background;
    private TMP_Text label;

    public void Bind(Image image, TMP_Text text)
    {
        background = image;
        label = text;
    }

    public void SetActive(bool active)
    {
        if (background != null) background.color = active ? new Color(UIKit.Accent.r, UIKit.Accent.g, UIKit.Accent.b, 0.18f) : new Color(1f, 1f, 1f, 0.05f);
        if (label != null) label.color = active ? UIKit.Accent : UIKit.MutedColor;
    }
}
