using System.Collections.Generic;
using UnityEngine;

// The game's soundtrack: one playlist per place, each track with the credit details its licence needs.
// Lives at Resources/Music/MusicLibrary - drag clips into the lists in the Inspector.
[CreateAssetMenu(fileName = "MusicLibrary", menuName = "Osiris/Music Library")]
public class MusicLibrary : ScriptableObject
{
    [System.Serializable]
    public class Track
    {
        public AudioClip clip;
        public string title;
        public string artist;
        [Tooltip("e.g. CC0, CC BY 4.0 - CC BY tracks must be credited in-game.")]
        public string license;
        public string source;
        [Range(0f, 1f)] public float volume = 1f;
    }

    public List<Track> menu = new List<Track>();
    public List<Track> galacticMap = new List<Track>();
    public List<Track> battle = new List<Track>();

    public List<Track> ForScene(string sceneName)
    {
        switch (sceneName)
        {
            case "MainMenu": return menu;
            case "GalacticMap": return galacticMap;
            case "BattleScene": return battle;
            default: return null;
        }
    }

    // Every distinct track, for a credits screen.
    public IEnumerable<Track> AllTracks()
    {
        HashSet<AudioClip> seen = new HashSet<AudioClip>();
        foreach (List<Track> list in new[] { menu, galacticMap, battle })
        {
            foreach (Track track in list)
            {
                if (track != null && track.clip != null && seen.Add(track.clip)) yield return track;
            }
        }
    }
}
