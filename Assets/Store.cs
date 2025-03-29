using UnityEngine;
using TMPro;
public class Store : MonoBehaviour
{
    public GameObject store;
    public TMP_Text moneyText;

    public StationController station;

    
    private void Update()
    {
        
        moneyText.text = "$" + GameManager.Instance.playerCash.ToString("F2");

        if(Input.GetKeyDown(KeyCode.Tab))
        {
            store.SetActive(!store.activeInHierarchy);
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
}
