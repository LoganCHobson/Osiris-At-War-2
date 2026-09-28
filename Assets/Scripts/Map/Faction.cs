using UnityEngine;

[CreateAssetMenu(fileName = "Faction", menuName = "Osiris/Faction")]
public class Faction : ScriptableObject
{
    public string factionName;
    public Color color = Color.white;
    public bool isPlayerFaction;
    public bool isNeutral;
    public AIPersonality personality;

    [TextArea]
    public string description;
}
