using UnityEngine;
using UnityEngine.UI;

public class AstroidMiner : MonoBehaviour
{
    public int team = -1;


    public Slider progressSlider;
    public Image image;

    public Color friendlyColor = new Color(0f, 0f, 1f);
    public Color enemyColor = new Color(1f, 0f, 0f);

    public float capturePercentage = 0f;

    public bool captureable;
    void Start()
    {
        GameManager.Instance.allAstroidMiners.Add(this);
    }

    void Update()
    {

        AddCash();


        if (progressSlider.gameObject.activeInHierarchy)
        {
            progressSlider.gameObject.transform.parent.LookAt(Camera.main.transform);
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


    public void OnTriggerEnter(Collider other)
    {
        
    }


    private void OnTriggerStay(Collider other)
    {
        int otherTeam = other.gameObject.layer;

        if (otherTeam == other.gameObject.layer)
        {
            captureable = false;
        }
        else
        {
            captureable = true;
        }

        if (captureable)
        {
            

            if (otherTeam != team)
            {

                capturePercentage = Mathf.Clamp(capturePercentage + Time.deltaTime, 0f, 100f);
                progressSlider.value = capturePercentage;

                if (capturePercentage >= 100f)
                {
                    team = otherTeam;
                    if (team == LayerMask.NameToLayer("FriendlyUnit"))
                    {

                        image.color = friendlyColor;
                    }
                    else if (team == LayerMask.NameToLayer("EnemyUnit"))
                    {
                        if (other.gameObject.transform.root.GetComponent<AIUnitBrain>().HasTask())
                        {
                            MarkTaskComplete(other.gameObject.transform.root.GetComponent<AIUnitBrain>());
                        }
                        image.color = enemyColor;
                    }
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

    }

    private void MarkTaskComplete(AIUnitBrain aIUnitBrain)
    {
        if (aIUnitBrain.currentTask is CaptureAsteroidTask captureTask)
        {
            aIUnitBrain.CompleteTask();
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (team == -1 || other.gameObject.layer != team)
        {
            capturePercentage = Mathf.Clamp(capturePercentage - Time.deltaTime, 0f, 100f);
        }
    }
}
