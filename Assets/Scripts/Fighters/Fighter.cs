using UnityEngine;

public class Fighter : MonoBehaviour
{
    [Header("Flight")]
    public float speed = 30f;
    public float formationSpeedBoost = 1.4f;
    public float turnRate = 150f;
    public float bankAmount = 0.7f;
    public float maxBank = 65f;
    public float altitudeBand = 22f;

    [Header("Guns")]
    public float shipDamage = 0.5f;
    public float fighterDamage = 0.8f;
    public float fireRate = 0.45f;
    public float fireRange = 55f;
    public float fireCone = 10f;
    public float projectileSpeed = 110f;
    public float spread = 0.015f;

    [Header("Attack Runs")]
    public float breakDistance = 16f;
    public float breakTime = 1.8f;
    public float breakRadius = 55f;
    public float breakClimb = 18f;
    public float dogfightOvershoot = 8f;

    [Header("Death")]
    public ParticleSystem deathEffect;
    public float deathEffectLifetime = 3f;

    public bool Alive => gameObject.activeInHierarchy && health != null && health.currentHealth > 0f;
    public Vector3 Velocity => transform.forward * currentSpeed;

    private enum Phase { Approach, Break }

    private Squadron squadron;
    private HardpointHealth health;
    private Vector3 formationOffset;
    private Phase phase;
    private Vector3 breakPoint;
    private float phaseEndsAt;
    private float nextShotAt;
    private Transform aim;
    private Fighter prey;
    private float bank;
    private float currentSpeed;
    private float wobbleSeed;
    private bool hasPose;
    private Vector3 worldPosition;
    private Quaternion worldRotation;

    private void Awake()
    {
        health = GetComponent<HardpointHealth>();
        if (health != null) health.die.AddListener(Die);
        wobbleSeed = Random.value * 100f;
    }

    public void Bind(Squadron owner, Vector3 offset)
    {
        squadron = owner;
        formationOffset = offset;
        currentSpeed = speed;
    }

    private void LateUpdate()
    {
        if (squadron == null) return;

        if (!hasPose)
        {
            worldPosition = transform.position;
            worldRotation = transform.rotation;
            hasPose = true;
        }
        transform.SetPositionAndRotation(worldPosition, worldRotation);

        float dt = Time.deltaTime;
        if (dt > 0f)
        {
            Fly(dt);
        }

        worldPosition = transform.position;
        worldRotation = transform.rotation;
    }

    private void Fly(float dt)
    {
        Vector3 target;
        float throttle = 1f;

        if (squadron.Engaging && WithinLeash())
        {
            target = FightPoint();
        }
        else
        {
            aim = null;
            prey = null;
            phase = Phase.Approach;
            target = FormationPoint(out throttle);
        }

        Steer(target, throttle, dt);
    }

    private bool WithinLeash()
    {
        return (transform.position - squadron.transform.position).sqrMagnitude < squadron.leashRadius * squadron.leashRadius;
    }

    private Vector3 FormationPoint(out float throttle)
    {
        Vector3 slot = squadron.transform.TransformPoint(formationOffset);
        slot += Vector3.up * Mathf.Sin(Time.time * 1.3f + wobbleSeed) * 0.6f;

        float distance = Vector3.Distance(transform.position, slot);
        throttle = Mathf.Clamp(distance / 12f, 0.35f, formationSpeedBoost);

        if (distance < 1.5f)
        {
            transform.position = Vector3.Lerp(transform.position, slot, 0.2f);
            return transform.position + squadron.transform.forward * 10f;
        }

        return slot;
    }

    private Vector3 FightPoint()
    {
        if (squadron.EnemySquadron != null)
        {
            return DogfightPoint();
        }

        if (aim == null || !aim.gameObject.activeInHierarchy)
        {
            aim = squadron.PickAimPoint();
            phase = Phase.Approach;
        }

        if (aim == null) return squadron.transform.position;

        Vector3 aimPosition = aim.position;
        float distance = Vector3.Distance(transform.position, aimPosition);

        if (phase == Phase.Approach)
        {
            TryFire(aimPosition, distance, shipDamage);

            if (distance < breakDistance)
            {
                phase = Phase.Break;
                phaseEndsAt = Time.time + breakTime * Random.Range(0.8f, 1.25f);
                Vector3 away = Flat(transform.forward);
                if (away.sqrMagnitude < 0.01f) away = Random.insideUnitSphere;
                away = Quaternion.Euler(0f, Random.Range(-70f, 70f), 0f) * away.normalized;
                breakPoint = aimPosition + away * breakRadius + Vector3.up * Random.Range(-0.3f, 1f) * breakClimb;
            }
            return aimPosition;
        }

        if (Time.time >= phaseEndsAt || Vector3.Distance(transform.position, breakPoint) < 6f)
        {
            phase = Phase.Approach;
            if (Random.value < 0.35f) aim = squadron.PickAimPoint();
        }
        return breakPoint;
    }

    private Vector3 DogfightPoint()
    {
        if (prey == null || !prey.Alive)
        {
            prey = squadron.PickEnemyFighter(transform.position);
        }
        if (prey == null) return squadron.transform.position;

        Vector3 preyPosition = prey.transform.position;
        float distance = Vector3.Distance(transform.position, preyPosition);
        float leadTime = distance / Mathf.Max(1f, projectileSpeed);
        Vector3 lead = preyPosition + prey.Velocity * leadTime;

        if (phase == Phase.Break)
        {
            if (Time.time >= phaseEndsAt) phase = Phase.Approach;
            return breakPoint;
        }

        TryFire(lead, distance, fighterDamage);

        if (distance < dogfightOvershoot)
        {
            phase = Phase.Break;
            phaseEndsAt = Time.time + breakTime * 0.5f;
            breakPoint = transform.position + Quaternion.Euler(0f, Random.Range(-120f, 120f), 0f) * transform.forward * 30f + Vector3.up * Random.Range(-8f, 8f);
        }

        return lead;
    }

    private void TryFire(Vector3 point, float distance, float damage)
    {
        if (Time.time < nextShotAt || distance > fireRange) return;

        Vector3 toPoint = point - transform.position;
        if (Vector3.Angle(transform.forward, toPoint) > fireCone) return;

        nextShotAt = Time.time + fireRate * Random.Range(0.85f, 1.15f);
        Vector3 direction = (toPoint.normalized + Random.insideUnitSphere * spread).normalized;
        squadron.FireLaser(transform.position + transform.forward * 1.5f, direction, damage, projectileSpeed);
    }

    private void Steer(Vector3 target, float throttle, float dt)
    {
        Vector3 toTarget = target - transform.position;

        float anchorHeight = squadron.transform.position.y;
        float offBand = transform.position.y - anchorHeight;
        if (Mathf.Abs(offBand) > altitudeBand)
        {
            toTarget.y -= offBand;
        }

        if (toTarget.sqrMagnitude < 0.0001f) toTarget = transform.forward;

        Vector3 forward = transform.forward;
        Vector3 desired = toTarget.normalized;
        Vector3 newForward = Vector3.RotateTowards(forward, desired, turnRate * Mathf.Deg2Rad * dt, 0f);

        float turn = Vector3.SignedAngle(Flat(forward), Flat(newForward), Vector3.up) / dt;
        bank = Mathf.Lerp(bank, Mathf.Clamp(-turn * bankAmount * 0.1f, -maxBank, maxBank), dt * 4f);

        currentSpeed = Mathf.Lerp(currentSpeed, speed * throttle, dt * 2f);
        transform.position += newForward * currentSpeed * dt;
        transform.rotation = Quaternion.LookRotation(newForward) * Quaternion.Euler(0f, 0f, bank);
    }

    private void Die()
    {
        if (deathEffect != null)
        {
            EffectPool.Play("FighterDeath", deathEffect, transform.position, Quaternion.identity, deathEffectLifetime);
        }
        gameObject.SetActive(false);
    }

    private static Vector3 Flat(Vector3 vector)
    {
        vector.y = 0f;
        return vector;
    }
}
