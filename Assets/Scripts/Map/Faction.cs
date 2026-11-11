using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "Faction", menuName = "Osiris/Faction")]
public class Faction : ScriptableObject
{
    public string factionName;
    public Color color = Color.white;
    public bool isPlayerFaction;
    public bool isNeutral;
    public AIPersonality personality;

    [Header("Ships")]
    public List<Ship> roster = new List<Ship>();
    [Min(0.01f)] public float buildTimeMultiplier = 1f;
    public GameObject projectilePrefab;
    public Material shipMaterial;

    [Header("Structures")]
    [Min(0)] public int maxBattleStations = 50;
    public GameObject capitalShipyardPrefab;
    public GameObject battleStationPrefab;

    [TextArea]
    public string description;

    // Fields Avanti ships: every ship in its roster is an Avanti hull (shielded, as the battle doctrine tells
    // them apart in GameManager.IsAvanti). That's the Avanti Empire and every other AI faction that flies
    // Avanti ships; the Titans of Osiris fly Human ships.
    public bool IsAvanti
    {
        get
        {
            bool any = false;
            foreach (Ship ship in roster)
            {
                if (ship == null) continue;
                bool avanti = ship.GetComponent<ShieldGenerator>() != null
                    || ship.name.IndexOf("Avanti", System.StringComparison.OrdinalIgnoreCase) >= 0;
                if (!avanti) return false;
                any = true;
            }
            return any;
        }
    }

    // Grand Admiral commands Avanti ships, never Human ones: its battle records don't say which navy it had,
    // so a single battle fought with Human ships would poison its training. Any AI faction flying Avanti
    // ships qualifies, neutral fleets included (they just never retreat); the player's faction never does.
    public bool CanBeGrandAdmiral => IsAvanti && !isPlayerFaction;

    public float BuildTime(Ship ship)
    {
        return ship != null ? ship.buildTime * buildTimeMultiplier : 0f;
    }
}
