using UnityEngine;
using TMPro;
public class Store : MonoBehaviour
{
    public GameObject store;
    public TMP_Text moneyText;

    public StationController station;

    public float coolDownTimer = 0f;
    public float coolDownTime = 30f;
    [HideInInspector]
    public bool cooling = false;

    private void Start()
    {
        coolDownTimer = coolDownTime;
    }

    private void Update()
    {
       

        moneyText.text = "$" + GameManager.Instance.playerCash.ToString("F2");

        if(Input.GetKeyDown(KeyCode.Tab))
        {
            store.SetActive(!store.activeInHierarchy);
        }

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


    public void Buy(float cost)
    {
        if(GameManager.Instance.playerCash >= cost)
        {
            GameManager.Instance.playerCash -= cost;
            station.MakeShip();
        }
        else
        {
            Debug.Log("Not enough money bruh");
        }
    }
    public void StartCool()
    {
        if (GameManager.Instance.playerCash >= 100f)//Hard coded because im running out of time, fix later.
        {
            cooling = true;
            coolDownTimer = coolDownTime;
        }
    }
}
