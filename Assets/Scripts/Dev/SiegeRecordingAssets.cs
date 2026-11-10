using System.Collections.Generic;
using UnityEngine;

// What the siege recording needs in a standalone build, where AssetDatabase doesn't exist: the two factions
// and the ship prefabs. Filled in by SiegeRecorderBuild before it builds (Resources/Dev/SiegeRecordingAssets).
public class SiegeRecordingAssets : ScriptableObject
{
    public Faction titans;
    public Faction avanti;
    public List<GameObject> ships = new List<GameObject>(); // Found by prefab name, e.g. "Human_Battleship".

    public Ship Find(string key)
    {
        string name = key.Substring(key.LastIndexOf('/') + 1);
        GameObject prefab = ships.Find(p => p != null && p.name == name);
        return prefab != null && prefab.TryGetComponent(out Ship ship) ? ship : null;
    }
}
