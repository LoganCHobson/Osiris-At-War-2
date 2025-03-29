using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
public class Purchaseable : MonoBehaviour
{
    public float cost = 100f;

    private Store store;
    private Button button;
    public float coolDownTimer = 0f;
    public float coolDownTime = 30f;
    private bool cooling = false;

    public TMP_Text cooldownText;
    void Start()
    {
        coolDownTimer = coolDownTime;
        store = GetComponentInParent<Store>();  
        button = GetComponentInChildren<Button>();
        
        //button.onClick.AddListener(() => store.Buy(cost));
    }

    // Update is called once per frame
    void Update()
    {
        if (cooling)
        {
            if (coolDownTimer > 0)
            {

                coolDownTimer -= Time.deltaTime;

                cooldownText.text = coolDownTimer.ToString("F0");
               
            }
            else
            {
                cooldownText.text = "";
                button.interactable = true;
                cooling = false;
                coolDownTimer = coolDownTime;
                
            }
        }
    }

    public void StartCool()
    {
        cooldownText.text = "";
        cooling = true;
        coolDownTimer = coolDownTime;
    }
}
