using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// Plays the soundtrack across the whole game: picks the playlist for the current scene (main menu,
// galactic map, battle), shuffles through it without immediate repeats, crossfades between tracks and
// when the scene changes, and follows the Music volume setting. Created automatically on startup.
public class MusicManager : MonoBehaviour
{
    private const string LibraryPath = "Music/MusicLibrary";

    public float crossfadeTime = 3f;
    public float sceneFadeTime = 1.5f;

    private static MusicManager instance;

    private MusicLibrary library;
    private readonly AudioSource[] sources = new AudioSource[2];
    private readonly MusicLibrary.Track[] playing = new MusicLibrary.Track[2];
    private readonly float[] fades = new float[2];
    private int active;
    private List<MusicLibrary.Track> playlist;
    private MusicLibrary.Track lastTrack;
    private float fadeTime;

    public static MusicLibrary.Track Current => instance != null ? instance.playing[instance.active] : null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Create()
    {
        if (instance != null) return;

        GameObject go = new GameObject("MusicManager");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<MusicManager>();
    }

    private void Awake()
    {
        library = Resources.Load<MusicLibrary>(LibraryPath);

        for (int i = 0; i < sources.Length; i++)
        {
            AudioSource source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 0f;
            source.ignoreListenerVolume = true; // Music follows the Music slider, not the Effects one.
            source.priority = 0;
            sources[i] = source;
        }

        SceneManager.sceneLoaded += OnSceneLoaded;
        SwitchTo(SceneManager.GetActiveScene().name);
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (instance == this) instance = null;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (mode == LoadSceneMode.Single) SwitchTo(scene.name);
    }

    private void SwitchTo(string sceneName)
    {
        List<MusicLibrary.Track> next = library != null ? library.ForScene(sceneName) : null;
        if (next == playlist) return; // Same playlist (e.g. reloading the scene) - keep the current track going.

        playlist = next;
        if (HasTracks(playlist))
        {
            // Keep playing if the track that's on also belongs to the new playlist (e.g. Eclipse Legion
            // on both the menu and the galactic map); otherwise crossfade to a new one.
            MusicLibrary.Track current = playing[active];
            if (current == null || !ContainsClip(playlist, current.clip)) PlayNext(sceneFadeTime);
        }
        else
        {
            FadeOutAll(sceneFadeTime);
        }
    }

    private void Update()
    {
        float step = fadeTime > 0f ? Time.unscaledDeltaTime / fadeTime : 1f;
        for (int i = 0; i < sources.Length; i++)
        {
            AudioSource source = sources[i];
            if (!source.isPlaying) continue;

            float target = i == active && playing[i] != null ? 1f : 0f;
            fades[i] = Mathf.MoveTowards(fades[i], target, step);
            source.volume = fades[i] * GameSettings.MusicOutput * (playing[i] != null ? playing[i].volume : 1f);

            if ((i != active || playing[i] == null) && fades[i] <= 0f)
            {
                source.Stop();
                playing[i] = null;
            }
        }

        // Start the next track a little before this one ends so they overlap.
        AudioSource current = sources[active];
        if (HasTracks(playlist) && playing[active] != null && current.clip != null
            && (!current.isPlaying || current.clip.length - current.time <= crossfadeTime))
        {
            PlayNext(crossfadeTime);
        }
    }

    private void PlayNext(float fade)
    {
        MusicLibrary.Track track = PickTrack();
        if (track == null) return;

        active = 1 - active;
        AudioSource source = sources[active];
        source.Stop();
        source.clip = track.clip;
        source.time = 0f;
        source.volume = 0f;
        fades[active] = 0f;
        playing[active] = track;
        source.Play();

        lastTrack = track;
        fadeTime = fade;
    }

    private void FadeOutAll(float fade)
    {
        playing[active] = null;
        fadeTime = fade;
    }

    // Random track from the playlist, never the one that just played (unless it's the only one).
    private MusicLibrary.Track PickTrack()
    {
        List<MusicLibrary.Track> options = new List<MusicLibrary.Track>();
        foreach (MusicLibrary.Track track in playlist)
        {
            if (track != null && track.clip != null && (lastTrack == null || track.clip != lastTrack.clip)) options.Add(track);
        }
        if (options.Count == 0)
        {
            foreach (MusicLibrary.Track track in playlist)
            {
                if (track != null && track.clip != null) options.Add(track);
            }
        }
        return options.Count > 0 ? options[Random.Range(0, options.Count)] : null;
    }

    private static bool HasTracks(List<MusicLibrary.Track> list)
    {
        if (list == null) return false;
        foreach (MusicLibrary.Track track in list)
        {
            if (track != null && track.clip != null) return true;
        }
        return false;
    }

    private static bool ContainsClip(List<MusicLibrary.Track> list, AudioClip clip)
    {
        foreach (MusicLibrary.Track track in list)
        {
            if (track != null && track.clip == clip) return true;
        }
        return false;
    }
}
