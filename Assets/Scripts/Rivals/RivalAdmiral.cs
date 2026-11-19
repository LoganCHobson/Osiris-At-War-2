using System.Collections.Generic;
using UnityEngine;

// The six warlord admirals the player faces: one per Avanti faction (the neutral holdouts have none).
// Who they are and everything they say lives in Resources/Rivals/RivalAdmirals.json, so the writing can
// change without touching code. Every line list may hold any number of lines; one is picked at random.
// Rivals.cs decides which list to draw from, from what each admiral remembers about the player.
[System.Serializable]
public class RivalAdmiral
{
    public string faction;   // must match the faction asset's Faction Name exactly
    public string title;
    public string name;
    public string epithet;   // one line under the name in the rivals dossier

    public string[] firstMeeting;   // a battle opens, and they have never fought the player
    public string[] leading;        // a battle opens; they have beaten the player more often
    public string[] trailing;       // a battle opens; the player has beaten them more often
    public string[] even;           // a battle opens; the record is level
    public string[] wonBattle;      // after beating the player
    public string[] lostBattle;     // after losing to the player
    public string[] playerFled;     // after the player retreated from them
    public string[] theyFled;       // after they retreated from the player

    // A battle opens and they have noticed one of the player's habits (Rivals.Tendency).
    public string[] onCarriers, onHeavyLine, onScreens, onRetreats, onAggressive, onOutnumbers;

    public string FullName => $"{title} {name}";

    public string[] OnTendency(Rivals.Tendency tendency) => tendency switch
    {
        Rivals.Tendency.Carriers => onCarriers,
        Rivals.Tendency.HeavyLine => onHeavyLine,
        Rivals.Tendency.Screens => onScreens,
        Rivals.Tendency.Retreats => onRetreats,
        Rivals.Tendency.Aggressive => onAggressive,
        Rivals.Tendency.Outnumbers => onOutnumbers,
        _ => null
    };

    [System.Serializable]
    private class File
    {
        public List<RivalAdmiral> admirals = new List<RivalAdmiral>();
    }

    private static List<RivalAdmiral> all;

    public static List<RivalAdmiral> All
    {
        get
        {
            if (all != null) return all;
            TextAsset json = Resources.Load<TextAsset>("Rivals/RivalAdmirals");
            if (json == null)
            {
                Debug.LogWarning("Rivals: Resources/Rivals/RivalAdmirals.json is missing - no warlord admirals this game.");
                return all = new List<RivalAdmiral>();
            }
            File file = JsonUtility.FromJson<File>(json.text);
            all = file != null && file.admirals != null ? file.admirals : new List<RivalAdmiral>();
            return all;
        }
    }

    public static RivalAdmiral For(Faction faction)
    {
        if (faction == null || faction.isNeutral || string.IsNullOrEmpty(faction.factionName)) return null;
        return All.Find(r => r.faction == faction.factionName);
    }
}
