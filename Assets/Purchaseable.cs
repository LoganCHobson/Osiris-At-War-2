using TMPro;
using UnityEngine;
using UnityEngine.UI;
public class Purchaseable : MonoBehaviour
{
    public float cost = 100f;

    private Store store;
    private Button button;
    

    public TMP_Text cooldownText;
    void Start()
    {
       
        store = GetComponentInParent<Store>();
        button = GetComponentInChildren<Button>();

        //button.onClick.AddListener(() => store.Buy(cost));
    }

    // Update is called once per frame
    void Update()
    {
        if (store.cooling)
        {
            cooldownText.text = store.coolDownTimer.ToString("F0");
            button.interactable = false;
        }
        else
        {
            cooldownText.text = "";
            button.interactable = true;
        }
    }

    
}
