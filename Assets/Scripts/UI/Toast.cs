using TMPro;
using UnityEngine;

// A short message at the bottom of the screen ("Game saved") that fades away. Survives scene loads so a
// message shown just before loading still appears.
public class Toast : MonoBehaviour
{
    private const float Duration = 2.4f;
    private const float FadeTime = 0.4f;

    private static Toast instance;

    private CanvasGroup group;
    private TMP_Text label;
    private float shownAt = float.NegativeInfinity;

    public static void Show(string message)
    {
        if (string.IsNullOrEmpty(message)) return;
        if (instance == null)
        {
            instance = new GameObject("Toast").AddComponent<Toast>();
            DontDestroyOnLoad(instance.gameObject);
            instance.Build();
        }
        instance.label.text = message;
        instance.shownAt = Time.unscaledTime;
    }

    private void Build()
    {
        Canvas canvas = UIKit.Canvas("ToastCanvas", 990, transform);
        RectTransform box = UIKit.Rect("Box", canvas.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 120f), new Vector2(620f, 64f));
        UIKit.Image(box, UIKit.PanelColor);
        group = box.gameObject.AddComponent<CanvasGroup>();
        group.blocksRaycasts = false;
        group.alpha = 0f;
        label = UIKit.Text(box, "", 28f, UIKit.TextColor, TextAlignmentOptions.Center);
    }

    private void Update()
    {
        float age = Time.unscaledTime - shownAt;
        float alpha = age < FadeTime ? age / FadeTime : age < Duration ? 1f : 1f - (age - Duration) / FadeTime;
        group.alpha = Mathf.Clamp01(alpha);
    }
}
