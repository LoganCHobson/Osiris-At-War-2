using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

// "INCOMING TRANSMISSION": a warlord admiral speaking, at the lower left of the screen for a few seconds.
// Queues if several arrive at once; click to dismiss. Survives scene loads (a line queued just before
// the battle scene opens still plays), like Toast.
public class TransmissionPanel : MonoBehaviour, IPointerClickHandler
{
    private const float FadeTime = 0.35f;
    private const float Hold = 7f;

    private static TransmissionPanel instance;

    private struct Message { public RivalAdmiral rival; public string line; public float notBefore; }
    private readonly Queue<Message> queue = new Queue<Message>();

    private CanvasGroup group;
    private TMP_Text header, speaker, quote;
    private float shownAt = float.NegativeInfinity;
    private bool showing;

    // delay: seconds (real time) before it may appear, e.g. to let a scene settle first.
    public static void Show(RivalAdmiral rival, string line, float delay = 0f)
    {
        if (rival == null || string.IsNullOrEmpty(line)) return;
        if (instance == null)
        {
            instance = new GameObject("TransmissionPanel").AddComponent<TransmissionPanel>();
            DontDestroyOnLoad(instance.gameObject);
            instance.Build();
        }
        instance.queue.Enqueue(new Message { rival = rival, line = line, notBefore = Time.unscaledTime + delay });
    }

    private void Build()
    {
        UIKit.EnsureEventSystem();
        Canvas canvas = UIKit.Canvas("TransmissionCanvas", 985, transform);
        // Lower left: at the top it covered the battle prompt's Auto-resolve and Battle-now buttons.
        RectTransform box = UIKit.Rect("Box", canvas.transform, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(40f, 40f), new Vector2(760f, 150f));
        UIKit.Image(box, UIKit.PanelColor, true);
        box.gameObject.AddComponent<ClickRelay>().owner = this;
        RectTransform stripe = UIKit.Rect("Stripe", box, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(6f, 0f));
        UIKit.Image(stripe, UIKit.Accent);

        group = box.gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.blocksRaycasts = false;

        header = Line(box, 16f, UIKit.Accent, -14f);
        header.text = "INCOMING TRANSMISSION";
        header.characterSpacing = 10f;
        speaker = Line(box, 24f, UIKit.TextColor, -40f);
        speaker.fontStyle = FontStyles.Bold;
        quote = Line(box, 24f, UIKit.TextColor, -76f);
        quote.fontStyle = FontStyles.Italic;
        quote.textWrappingMode = TextWrappingModes.Normal;
        quote.rectTransform.sizeDelta = new Vector2(-60f, 64f);
    }

    private static TMP_Text Line(RectTransform box, float size, Color color, float y)
    {
        TMP_Text text = UIKit.Text(box, "", size, color, TextAlignmentOptions.TopLeft);
        RectTransform rect = text.rectTransform;
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(10f, y);
        rect.sizeDelta = new Vector2(-60f, size * 1.4f);
        return text;
    }

    private void Update()
    {
        if (!showing && queue.Count > 0 && Time.unscaledTime >= queue.Peek().notBefore) Present(queue.Dequeue());

        float age = Time.unscaledTime - shownAt;
        float alpha = !showing ? 0f : age < FadeTime ? age / FadeTime : age < Hold ? 1f : 1f - (age - Hold) / FadeTime;
        group.alpha = Mathf.Clamp01(alpha);
        group.blocksRaycasts = showing;
        if (showing && age > Hold + FadeTime) showing = false;
    }

    private void Present(Message message)
    {
        Faction faction = FindFaction(message.rival.faction);
        string colour = faction != null ? ColorUtility.ToHtmlStringRGB(Color.Lerp(faction.color, Color.white, 0.25f)) : "FFFFFF";
        speaker.text = $"{message.rival.FullName}  <size=75%><color=#{colour}>{message.rival.faction.ToUpperInvariant()}</color></size>";
        quote.text = $"\"{message.line}\"";
        shownAt = Time.unscaledTime;
        showing = true;
    }

    private static Faction FindFaction(string name)
    {
        foreach (Faction faction in SaveRegistry.Factions)
        {
            if (faction != null && faction.factionName == name) return faction;
        }
        return null;
    }

    public void OnPointerClick(PointerEventData eventData) => Dismiss();

    private void Dismiss()
    {
        if (!showing) return;
        shownAt = Mathf.Min(shownAt, Time.unscaledTime - Hold); // Start fading now.
    }

    // The click lands on the box, a child; pass it up.
    private class ClickRelay : MonoBehaviour, IPointerClickHandler
    {
        public TransmissionPanel owner;
        public void OnPointerClick(PointerEventData eventData) => owner?.Dismiss();
    }
}
