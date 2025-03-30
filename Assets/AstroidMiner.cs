using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class AstroidMiner : MonoBehaviour
{
    public int team = -1;
    public Slider progressSlider;
    public Image image;

    public Color friendlyColor = new Color(0f, 0f, 1f);
    public Color enemyColor = new Color(1f, 0f, 0f);

    private float capturePercentage = 0f;
    private HashSet<int> teamsInside = new HashSet<int>(); 

    private void Start()
    {
        GameManager.Instance.allAstroidMiners.Add(this);
    }

    private void Update()
    {
        AddCash();

        if (progressSlider.gameObject.activeInHierarchy)
        {
            progressSlider.gameObject.transform.parent.LookAt(Camera.main.transform);
        }

        if (teamsInside.Count == 1)
        {
            int occupyingTeam = -1;
            foreach (int t in teamsInside) { occupyingTeam = t; } 

            if (occupyingTeam != team)
            {
                capturePercentage = Mathf.Clamp(capturePercentage + Time.deltaTime, 0f, 100f);
                progressSlider.value = capturePercentage;

                if (capturePercentage >= 100f)
                {
                    team = occupyingTeam;
                    image.color = (team == LayerMask.NameToLayer("FriendlyUnit")) ? friendlyColor : enemyColor;
                    capturePercentage = 0f;
                    progressSlider.value = progressSlider.maxValue;
                }
            }
        }
    }

    private void AddCash()
    {
        if (team == LayerMask.NameToLayer("FriendlyUnit"))
        {
            GameManager.Instance.playerCash += Time.deltaTime;
        }
        else if (team == LayerMask.NameToLayer("EnemyUnit"))
        {
            GameManager.Instance.enemyCash += Time.deltaTime;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        int otherTeam = other.gameObject.layer;
        teamsInside.Add(otherTeam);
    }

    private void OnTriggerExit(Collider other)
    {
        int otherTeam = other.gameObject.layer;
        teamsInside.Remove(otherTeam);
        capturePercentage = Mathf.Clamp(capturePercentage - Time.deltaTime, 0f, 100f);
    }

    private void OnTriggerStay(Collider other)
    {
        int otherTeam = other.gameObject.layer;

        if (teamsInside.Count > 1) return; 

        if (otherTeam != team)
        {
            capturePercentage = Mathf.Clamp(capturePercentage + Time.deltaTime, 0f, 100f);
            progressSlider.value = capturePercentage;

            if (capturePercentage >= 100f)
            {
                team = otherTeam;
                image.color = (team == LayerMask.NameToLayer("FriendlyUnit")) ? friendlyColor : enemyColor;
                capturePercentage = 0f;
                progressSlider.value = progressSlider.maxValue;
            }
        }

        if (otherTeam == team && other.gameObject.layer == 8)
        {
            if (other.gameObject.transform.root.GetComponent<AIUnitBrain>().HasTask())
            {
                MarkTaskComplete(other.gameObject.transform.root.GetComponent<AIUnitBrain>());
            }
        }
    }

    private void MarkTaskComplete(AIUnitBrain aIUnitBrain)
    {
        if (aIUnitBrain.currentTask is CaptureAsteroidTask captureTask)
        {
            aIUnitBrain.CompleteTask();
        }
    }
}
