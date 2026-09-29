using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class RetreatPanel : MonoBehaviour
{
    public GameObject panel;
    public Button retreatButton;
    public TMP_Text buttonLabel;
    public GameObject status;
    public TMP_Text statusText;

    public string retreatLabel = "Retreat";
    public string retreatingLabel = "Retreating";
    public Color playerRetreatColor = new Color(1f, 0.69f, 0.25f);
    public Color enemyRetreatColor = new Color(0.49f, 0.99f, 0.49f);

    private void LateUpdate()
    {
        RetreatManager retreat = RetreatManager.Instance;
        bool battle = retreat != null && GameManager.Instance != null && BattleContext.Instance != null && BattleContext.Instance.hasPendingBattle;

        if (panel.activeSelf != battle)
        {
            panel.SetActive(battle);
        }
        if (!battle) return;

        bool playerSide = GameManager.Instance.PlayerIsAttacker;
        bool playerRetreating = retreat.IsRetreating(playerSide);
        bool enemyRetreating = retreat.IsRetreating(!playerSide);

        if (playerRetreating)
        {
            buttonLabel.text = retreatingLabel;
        }
        else if (retreat.CooldownRemaining > 0f)
        {
            buttonLabel.text = $"{retreatLabel} ({Mathf.CeilToInt(retreat.CooldownRemaining)}s)";
        }
        else
        {
            buttonLabel.text = retreatLabel;
        }

        retreatButton.interactable = retreat.CanRetreat(playerSide);

        bool showStatus = playerRetreating || enemyRetreating;
        if (status.activeSelf != showStatus)
        {
            status.SetActive(showStatus);
        }

        if (playerRetreating)
        {
            statusText.color = playerRetreatColor;
            statusText.text = $"Retreating in {Mathf.CeilToInt(retreat.RetreatRemaining(playerSide))}s";
        }
        else if (enemyRetreating)
        {
            statusText.color = enemyRetreatColor;
            statusText.text = $"Enemy retreating in {Mathf.CeilToInt(retreat.RetreatRemaining(!playerSide))}s - hit their engines!";
        }
    }

    public void Retreat()
    {
        if (RetreatManager.Instance == null || GameManager.Instance == null) return;
        RetreatManager.Instance.BeginRetreat(GameManager.Instance.PlayerIsAttacker);
    }
}
