using TMPro;
using UnityEngine;

public class BattleEndBanner : MonoBehaviour
{
    public static BattleEndBanner Instance { get; private set; }

    public GameObject banner;
    public TMP_Text titleText;
    public TMP_Text subtitleText;
    public Color victoryColor = new Color(0.49f, 0.99f, 0.49f);
    public Color defeatColor = new Color(1f, 0.38f, 0.38f);

    private void Awake()
    {
        Instance = this;
        banner.SetActive(false);
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    public void Show(bool playerWon)
    {
        titleText.text = playerWon ? "VICTORY" : "DEFEAT";
        titleText.color = playerWon ? victoryColor : defeatColor;
        subtitleText.text = "Returning to the galactic map...";
        banner.SetActive(true);
    }
}
