using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class HardpointHealth : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public float maxHealth;
    public float currentHealth;
    public Image healthVisual;
    private Vector3 originalScale;
    public float scaleMultiplier = 1.5f;
    public float scaleDuration = 0.2f;

    private UnitHealthManager healthManager;
    public UnityEvent dealDamage; //May not actually need this.
    public UnityEvent die;

    private Color green = new Color(0f, 1f, 0f);  // Green
    private Color yellow = new Color(1f, 1f, 0f); // Yellow
    private Color orange = new Color(1f, 0.5f, 0f); // Orange
    private Color red = new Color(1f, 0f, 0f); // Red
    private void Start()
    {
        if (healthVisual != null) healthVisual.color = green;
        currentHealth = maxHealth;
        healthManager = GetComponentInParent<UnitHealthManager>();
    }

    // Hardpoints added mid-battle (HardpointFactory, shield generators) can be hit before their Start runs.
    private UnitHealthManager Manager => healthManager != null ? healthManager : healthManager = GetComponentInParent<UnitHealthManager>();

    private bool destroyed;
    public bool Destroyed => destroyed;
    private const float WreckHitShare = 0.35f; // Same as a laser on bare hull (Laser.hullDamageMultiplier).

    private ShipAbility evasion;
    private bool evasionLooked;

    public void DealDamage(float value)
    {
        if (GameManager.CombatOver) return;

        if (!evasionLooked) { evasion = ShipAbility.Of(this); evasionLooked = true; }
        if (evasion != null) value *= evasion.DamageTakenMultiplier;
        if (!destroyed && TryGetComponent(out CraftEvasion jink) && jink.Dodges()) return; // Human interceptors.
        if (!destroyed && TryGetComponent(out FighterShield shield)) { value = shield.Absorb(value); if (value <= 0f) return; }

        // Already destroyed (its wreck is still on the hull and still gets hit): the shot goes into the ship
        // like any other hull hit - reduced, onto a surviving hardpoint - instead of being lost.
        UnitHealthManager manager = Manager;
        if (destroyed)
        {
            // DealRandomDamage only picks hardpoints still standing, so this can't bounce back here.
            if (manager != null && !manager.IsDead) manager.DealRandomDamage(value * WreckHitShare);
            return;
        }
        if (manager == null) return; // Not on a ship (yet): nothing to damage.

        DamageLedger.Hull(manager, Mathf.Min(value, Mathf.Max(0f, currentHealth)), currentHealth - value <= 0);

        if(currentHealth - value > 0)
        {
            dealDamage.Invoke();
            manager.SubtractHealth(value, this); //Actual health subtraction in this script is done by the manager.

            float healthPercent = currentHealth / maxHealth;
            UpdateHealth(healthPercent);
        }
        else
        {
            destroyed = true;
            // The ship always learns of the loss, even if a death listener throws: a destroyed hardpoint left
            // in its list sent wreck hits round in circles until the stack overflowed.
            try { die.Invoke(); }
            finally { manager.HardpointDeath(this); }
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
        if (healthVisual == null) return;

        if(healthVisual.enabled != value)
        {
            healthVisual.enabled = value;
        }
        
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if(healthVisual != null && gameObject.layer == 8)
        {
            healthVisual.rectTransform.sizeDelta = new Vector2(15, 15);
        }
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (healthVisual != null && gameObject.layer == 8)
        {
            healthVisual.rectTransform.sizeDelta = new Vector2(10, 10);
        }
    }
}
