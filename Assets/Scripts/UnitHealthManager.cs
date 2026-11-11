using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

public class UnitHealthManager : MonoBehaviour
{
    public static readonly List<UnitHealthManager> Active = new List<UnitHealthManager>();

    public float maxHealth;
    public float currentHealth;
    public Slider healthSlider;
    //private List<HardpointHealth> hardpoints = new List<HardpointHealth>();
    private HardpointManager hardpointManager;
    public UnityEvent onDie;
    public float destroyDelay = 5f;

    private bool dead;
    private Camera cam;

    public Ship sourceShip;
    public ShipEntry entry; // The individual ship (name, hero) - null for launched strike craft and defenses.
    // Firing gives a concealed ship away (BattleSensors); ambushers hold fire until they spring the trap.
    public float LastFiredAt
    {
        get => lastFiredAt;
        set { lastFiredAt = value; LastShotAnywhere = value; }
    }
    private float lastFiredAt = float.NegativeInfinity;
    // When anyone last fired - the music uses it to tell a fight from a lull.
    public static float LastShotAnywhere { get; private set; } = float.NegativeInfinity;
    public bool HoldFire { get; set; }
    public bool isAttackerSide;
    public bool isShipyardBonus;
    public bool isBattleStation;
    public int battleToken = -1;

    public const int FreeUnitToken = -2;
    public bool IsFreeUnit => battleToken == FreeUnitToken;

    public bool IsDead => dead;
    public bool IsDefense => isShipyardBonus || isBattleStation;

    public void Configure(Ship ship, bool attackerSide)
    {
        sourceShip = ship;
        isAttackerSide = attackerSide;
    }

    public void ConfigureSpecial(bool shipyardBonus, bool battleStation)
    {
        isShipyardBonus = shipyardBonus;
        isBattleStation = battleStation;
    }

    private void OnEnable()
    {
        Active.Add(this);
    }

    private void OnDisable()
    {
        Active.Remove(this);
    }

    private void Start()
    {
        cam = Camera.main;
        hardpointManager = GetComponent<HardpointManager>();

        if (hardpointManager.hardpoints.Count > 0)
        {
            foreach (HardpointHealth health in hardpointManager.hardpoints)
            {
                maxHealth += health.maxHealth; //Our maximum health is represented by all of the hardpoints.
            }
            if (healthSlider != null)
            {
                healthSlider.maxValue = maxHealth;
                healthSlider.value = maxHealth;
            }
        }
        else
        {
            Debug.LogError("Could not find hardpoints");
        }


        currentHealth = maxHealth;
    }
    public void SubtractHealth(float value, HardpointHealth hardpoint)
    {
        hardpoint.currentHealth -= value;
        currentHealth -= value;
        if (healthSlider != null) healthSlider.value = currentHealth;
    }

    // Patches a living hardpoint back up (hero repair crews). Never above its maximum.
    public void Repair(HardpointHealth hardpoint, float amount)
    {
        if (dead || hardpoint == null || hardpoint.currentHealth <= 0f || amount <= 0f) return;
        float healed = Mathf.Min(amount, hardpoint.maxHealth - hardpoint.currentHealth);
        if (healed <= 0f) return;

        hardpoint.currentHealth += healed;
        currentHealth = Mathf.Min(maxHealth, currentHealth + healed);
        if (healthSlider != null) healthSlider.value = currentHealth;
        hardpoint.UpdateHealth(hardpoint.currentHealth / Mathf.Max(0.01f, hardpoint.maxHealth));
    }

    public void HardpointDeath(HardpointHealth hardpoint)
    {
        SubtractHealth(hardpoint.currentHealth, hardpoint);
        if (hardpointManager == null) hardpointManager = GetComponent<HardpointManager>(); // Hit before Start ran.
        if (hardpointManager != null) hardpointManager.hardpoints.Remove(hardpoint);
        //Destroy(hardpoint.gameObject);
    }

    public void DealRandomDamage(float damage) //Just to ensure the game doesn't go on FOREVER.
    {
        if (GameManager.CombatOver) return;
        if (hardpointManager == null) hardpointManager = GetComponent<HardpointManager>();
        if (hardpointManager == null) return;

        // Only hardpoints still standing: a destroyed one would pass the hit straight back here.
        List<HardpointHealth> hardpoints = hardpointManager.hardpoints;
        hardpoints.RemoveAll(h => h == null || h.Destroyed);
        if (hardpoints.Count > 0)
        {
            int rand = Random.Range(0, hardpoints.Count);

            hardpoints[rand].DealDamage(damage);
        }
    }

    private void Update()
    {
        if (hardpointManager.hardpoints.Count <= 0 && !dead)
        {
            dead = true;
            DamageLedger.Died(this);
            onDie.Invoke();

            if (isShipyardBonus)
            {
                BattleContext.Instance?.ReportShipyardDestroyed();
            }
            else if (isBattleStation)
            {
                BattleContext.Instance?.ReportBattleStationDestroyed();
            }
            else if (!IsFreeUnit)
            {
                BattleContext.Instance?.ReportLoss(isAttackerSide, entry, battleToken);
            }

            if (destroyDelay >= 0f) Destroy(gameObject, destroyDelay);
        }

        if(healthSlider != null && healthSlider.gameObject.activeInHierarchy)
        {
            healthSlider.gameObject.transform.parent.LookAt(cam.transform);
        }
    }

   

}
