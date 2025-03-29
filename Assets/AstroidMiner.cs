using UnityEngine;
using UnityEngine.UI;

public class AstroidMiner : MonoBehaviour
{
    public int team = -1;
    private GameManager gameManager;

    public Slider progressSlider;
    public Image image;

    public Color friendlyColor = new Color(0f, 0f, 1f);
    public Color enemyColor = new Color(1f, 0f, 0f);

    public float capturePercentage = 0f;

    void Start()
    {
        gameManager = GameManager.Instance;
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
            gameManager.playerCash += Time.deltaTime;
            
        }
        else if (team == LayerMask.NameToLayer("EnemyUnit"))
        {
            gameManager.enemyCash += Time.deltaTime;
        }

    }

    private void OnTriggerStay(Collider other)
    {
       
        int otherTeam = other.gameObject.layer;

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
                    image.color = enemyColor;
                }
                capturePercentage = 0f;
                progressSlider.value = progressSlider.maxValue;
            }
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
