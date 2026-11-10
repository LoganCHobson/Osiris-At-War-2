using UnityEngine;

// Human interceptors: small, quick and flown hard - they jink through fire, and a share of the hits that
// should have landed (warship guns, point defence, other craft) go wide. FleetDoctrine's air-superiority answer
// to Avanti technology.
[DisallowMultipleComponent]
public class CraftEvasion : MonoBehaviour
{
    public const float DodgeChance = 0.35f;

    public static void Fit(Fighter fighter)
    {
        if (fighter.GetComponent<CraftEvasion>() == null) fighter.gameObject.AddComponent<CraftEvasion>();
    }

    public bool Dodges() => Random.value < DodgeChance;
}
