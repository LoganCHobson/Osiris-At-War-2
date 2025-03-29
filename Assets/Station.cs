using SolarStudios;
using Unity.VisualScripting;
using UnityEngine;

public class StationController : MonoBehaviour
{

    private SpaceUnit unit;
    public GameObject prefab;
    public float cost = 100;

    public Transform spawnpoint;

    public float coolDownTimer = 0f;
    public float coolDownTime = 30f;
    [HideInInspector]
    public bool cooling = false;
    private void Start()
    {
        unit = GetComponentInParent<SpaceUnit>();
    }

    private void Update()
    {
        if(gameObject.layer == 7)
        {
            GameManager.Instance.playerCash += Time.deltaTime; //Stations should have passive income as well to prevent soft lock.
        }
        if(gameObject.layer == 8)
        {
            GameManager.Instance.enemyCash += Time.deltaTime;
        }

        //Also just for the AI:
        if (cooling)
        {
            if (coolDownTimer > 0)
            {

                coolDownTimer -= Time.deltaTime;

            }
            else
            {
                
                cooling = false;
                coolDownTimer = coolDownTime;

            }
        }

    }
    public void MakeShip()
    {
        
        if(gameObject.layer == 8) //Just for when the AI does it. FOr now.
        {
            if(!cooling)
            {
                GameManager.Instance.enemyCash -= cost;
                GameObject temp = Instantiate(prefab, spawnpoint);
                temp.transform.parent = null;
                StartCool();
            }
            
        }
        else
        {
            GameObject temp = Instantiate(prefab, spawnpoint);
        }
    }

    public void StartCool()
    {
        cooling = true;
        coolDownTimer = coolDownTime;
    }
}
