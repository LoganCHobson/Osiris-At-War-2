using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Battle HUD: a card for every hero on the field along the top of the screen - portrait, name and their
// flagship's hull - framed blue for yours and red for the enemy's. Click one of yours to select the
// flagship; hover any for the info blade. Added automatically when the battle scene loads.
public class HeroBar : MonoBehaviour
{
    private const float CardWidth = 230f;
    private const float CardHeight = 74f;
    private const float RefreshInterval = 0.25f;
    private static readonly Color Friendly = new Color(0.35f, 0.8f, 1f);
    private static readonly Color Enemy = new Color(1f, 0.4f, 0.35f);

    private RectTransform row;
    private readonly List<Card> cards = new List<Card>();
    private float nextRefreshAt;

    private class Card
    {
        public RectTransform root;
        public Image portrait;
        public TMP_Text label;
        public RectTransform hull;
        public Image frame;
        public CardClick click;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Hook()
    {
        SceneManager.sceneLoaded += (scene, _) => AttachTo(scene);
        AttachTo(SceneManager.GetActiveScene());
    }

    private static void AttachTo(Scene scene)
    {
        if (scene.name != "BattleScene" || FindFirstObjectByType<HeroBar>() != null) return;
        Canvas canvas = UIKit.Canvas("HeroBarCanvas", 37);
        HeroBar bar = canvas.gameObject.AddComponent<HeroBar>();
        bar.row = UIKit.Rect("Heroes", canvas.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -12f), new Vector2(0f, CardHeight));
        HorizontalLayoutGroup layout = bar.row.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 8f;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = false;
        layout.childControlHeight = false;
        bar.row.gameObject.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
    }

    private void Update()
    {
        if (Time.unscaledTime < nextRefreshAt) return;
        nextRefreshAt = Time.unscaledTime + RefreshInterval;

        bool playerAttacker = GameManager.Instance == null || GameManager.Instance.PlayerIsAttacker;
        int used = 0;
        foreach ((HeroDefinition hero, UnitHealthManager unit, bool attackerSide) in HeroForces.OnField())
        {
            if (!BattleSensors.PlayerCanSee(unit)) continue;
            if (used == cards.Count) cards.Add(CreateCard());
            Card card = cards[used++];
            bool mine = attackerSide == playerAttacker;

            card.root.gameObject.SetActive(true);
            card.portrait.sprite = hero.Portrait;
            card.portrait.enabled = hero.Portrait != null;
            card.label.text = $"<b>{hero.name}</b>\n<size=12><color=#{ColorUtility.ToHtmlStringRGBA(UIKit.MutedColor)}>{hero.flagshipName}</color></size>";
            card.frame.color = mine ? Friendly : Enemy;
            float hull = unit.maxHealth > 0f ? Mathf.Clamp01(unit.currentHealth / unit.maxHealth) : 1f;
            card.hull.anchorMax = new Vector2(hull, 1f);
            card.click.unit = unit;
            card.click.mine = mine;
        }

        for (int i = used; i < cards.Count; i++) cards[i].root.gameObject.SetActive(false);
    }

    private Card CreateCard()
    {
        RectTransform root = UIKit.Rect("HeroCard", row, Vector2.zero, Vector2.zero, new Vector2(0.5f, 1f), Vector2.zero, new Vector2(CardWidth, CardHeight));
        UIKit.Image(root, UIKit.PanelColor, true);

        RectTransform edge = UIKit.Rect("Frame", root, Vector2.zero, new Vector2(0f, 1f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(4f, 0f));
        Image frame = UIKit.Image(edge, Friendly);

        RectTransform portraitRect = UIKit.Rect("Portrait", root, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(8f, 0f), new Vector2(64f, 64f));
        Image portrait = UIKit.Image(portraitRect, Color.white);
        portrait.preserveAspect = true;

        TMP_Text label = UIKit.Text(root, "", 15f, UIKit.TextColor, TextAlignmentOptions.TopLeft);
        label.rectTransform.offsetMin = new Vector2(80f, 14f);
        label.rectTransform.offsetMax = new Vector2(-6f, -6f);
        label.richText = true;
        label.overflowMode = TextOverflowModes.Ellipsis;

        RectTransform track = UIKit.Rect("Hull", root, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(36f, 6f), new Vector2(-88f, 5f));
        UIKit.Image(track, new Color(0f, 0f, 0f, 0.6f));
        RectTransform fill = UIKit.Rect("Fill", track, Vector2.zero, Vector2.one, new Vector2(0f, 0.5f), Vector2.zero, Vector2.zero);
        UIKit.Image(fill, new Color(0.4f, 0.95f, 0.5f));

        CardClick click = root.gameObject.AddComponent<CardClick>();
        return new Card { root = root, portrait = portrait, label = label, hull = fill, frame = frame, click = click };
    }

    private class CardClick : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
    {
        public UnitHealthManager unit;
        public bool mine;

        public void OnPointerClick(PointerEventData eventData)
        {
            if (!mine || unit == null || unit.IsDead || PlayerSpaceManager.Instance == null) return;
            SpaceUnit spaceUnit = unit.GetComponent<SpaceUnit>();
            if (spaceUnit != null) PlayerSpaceManager.Instance.SelectOnly(new[] { spaceUnit });
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (unit != null && unit.sourceShip != null) ShipBlade.Show(unit.sourceShip, unit);
        }

        public void OnPointerExit(PointerEventData eventData) => ShipBlade.Hide();
    }
}
