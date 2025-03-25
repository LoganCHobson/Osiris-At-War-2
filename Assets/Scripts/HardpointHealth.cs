using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class HardpointHealth : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public float maxHealth;
    public float currentHealth;
    public Image healthVisual;
    private UnitHealthManager healthManager;
    public UnityEvent dealDamage; //May not actually need this.
    public UnityEvent die;

    private Color green = new Color(0f, 1f, 0f);  // Green
    private Color yellow = new Color(1f, 1f, 0f); // Yellow
    private Color orange = new Color(1f, 0.5f, 0f); // Orange
    private Color red = new Color(1f, 0f, 0f); // Red
    private void Start()
    {
        currentHealth = maxHealth;
        healthManager = GetComponentInParent<UnitHealthManager>();
    }

    public void DealDamage(float value)
    {
        if(currentHealth - value > 0)
        {
            dealDamage.Invoke();
            healthManager.SubtractHealth(value, this); //Actual health subtraction in this script is done by the manager.

            float healthPercent = currentHealth / maxHealth;
            UpdateHealth(healthPercent);
        }
        else
        {
            
            die.Invoke();
            healthManager.HardpointDeath(this);
        }
        
    }

    public void UpdateHealth(float health)
    {
        Color newColor;

        if (health > 0.75f)
            newColor = Color.Lerp(green, yellow, (1f - health) * 4f); // Green to Yellow
        else if (health > 0.5f)
            newColor = Color.Lerp(yellow, orange, (0.75f - health) * 4f); // Yellow to Orange
        else
            newColor = Color.Lerp(orange, red, (0.5f - health) * 2f); // Orange to Red

        // Apply to UI Image
        if (healthVisual != null)
            healthVisual.color = newColor;

       
    }

    public void ToggleVisual(bool value)
    {
        healthVisual.enabled = value;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        ToggleVisual(true);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        ToggleVisual(false);
    }


}
