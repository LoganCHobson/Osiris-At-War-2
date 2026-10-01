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

    public float BuildTime(Ship ship)
    {
        return ship != null ? ship.buildTime * buildTimeMultiplier : 0f;
    }
}
