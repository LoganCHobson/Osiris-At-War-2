using System.Collections.Generic;
using UnityEngine;

// Battle sound effects, played in 3D where they happen: zoomed out you hear a distant rumble, zoomed into
// a fight you hear every gun. One pool of audio sources is shared by all sounds; each cue has its own mix
// volume, pitch spread and voice limit, sounds out of earshot are skipped, and when a cue is at its limit a
// nearer sound takes over its farthest voice, so a huge battle stays readable instead of turning to mush.
// Clips live in Resources/Audio/SFX as <cue prefix>_<n>.ogg: Kenney and Freesound CC0 sounds plus a
// synthesized CIWS. Fighters are deliberately silent; ship and hardpoint deaths are voiced by DestructionFx.
// Volume follows the Effects slider through the listener volume (see GameSettings).
public class Sfx : MonoBehaviour
{
    public enum Cue
    {
        LaserHeavy, LaserAvanti, Ciws, Beam, MissileLaunch, TorpedoLaunch,
        HitHull, HitShield, ExplosionSmall, FtlArrive, FtlDepart,
    }

    private class Setting
    {
        public string prefix;
        public float volume;
        public float pitchSpread;
        public int voices;
        public float range;
        public float minInterval; // Per cue, so a broadside doesn't fire forty identical sounds in one frame.
        public AudioClip[] clips;
        public float lastPlayed = float.NegativeInfinity;
    }

    private const int PoolSize = 48;
    private const float FullVolumeDistance = 22f;

    private static Sfx instance;
    private static readonly Dictionary<Cue, Setting> settings = new Dictionary<Cue, Setting>
    {
        [Cue.LaserHeavy] = new Setting { prefix = "laser_heavy", volume = 0.4f, pitchSpread = 0.1f, voices = 8, range = 240f, minInterval = 0.03f },
        [Cue.LaserAvanti] = new Setting { prefix = "laser_avanti", volume = 0.45f, pitchSpread = 0.1f, voices = 8, range = 240f, minInterval = 0.03f },
        [Cue.Ciws] = new Setting { prefix = "ciws_burst", volume = 0.25f, pitchSpread = 0.06f, voices = 4, range = 220f, minInterval = 0.1f },
        [Cue.Beam] = new Setting { prefix = "beam_fire", volume = 0.35f, pitchSpread = 0.1f, voices = 4, range = 200f, minInterval = 0.1f },
        [Cue.MissileLaunch] = new Setting { prefix = "missile_launch", volume = 0.45f, pitchSpread = 0.1f, voices = 4, range = 240f, minInterval = 0.06f },
        [Cue.TorpedoLaunch] = new Setting { prefix = "torpedo_launch", volume = 0.5f, pitchSpread = 0.08f, voices = 4, range = 240f, minInterval = 0.08f },
        [Cue.HitHull] = new Setting { prefix = "hit_hull", volume = 0.3f, pitchSpread = 0.12f, voices = 6, range = 180f, minInterval = 0.04f },
        [Cue.HitShield] = new Setting { prefix = "hit_shield", volume = 0.3f, pitchSpread = 0.12f, voices = 6, range = 180f, minInterval = 0.05f },
        [Cue.ExplosionSmall] = new Setting { prefix = "explosion_small", volume = 0.55f, pitchSpread = 0.1f, voices = 6, range = 280f, minInterval = 0.05f },
        [Cue.FtlArrive] = new Setting { prefix = "ftl_arrive", volume = 0.75f, pitchSpread = 0.05f, voices = 3, range = 420f, minInterval = 0.25f },
        [Cue.FtlDepart] = new Setting { prefix = "ftl_depart", volume = 0.75f, pitchSpread = 0.05f, voices = 3, range = 420f, minInterval = 0.25f },
    };

    private readonly List<AudioSource> sources = new List<AudioSource>();
    private readonly Dictionary<AudioSource, Cue> playing = new Dictionary<AudioSource, Cue>();
    private AudioListener listener;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Initialize()
    {
        instance = null;
        foreach (Setting setting in settings.Values)
        {
            setting.clips = null;
            setting.lastPlayed = float.NegativeInfinity;
        }
    }

    public static void Play(Cue cue, Vector3 position, float volume = 1f, float pitch = 1f)
    {
        if (instance == null)
        {
            instance = new GameObject("Sfx").AddComponent<Sfx>(); // Lives in the current scene.
            instance.Build();
        }
        instance.Emit(cue, position, volume, pitch);
    }

    private void Build()
    {
        // Full volume up close, fading smoothly to silence at each cue's range (set as maxDistance).
        AnimationCurve rolloff = new AnimationCurve(
            new Keyframe(0f, 1f), new Keyframe(0.08f, 1f), new Keyframe(0.25f, 0.5f), new Keyframe(0.5f, 0.18f), new Keyframe(1f, 0f));

        for (int i = 0; i < PoolSize; i++)
        {
            AudioSource source = new GameObject("Voice").AddComponent<AudioSource>();
            source.transform.SetParent(transform, false);
            source.playOnAwake = false;
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Custom;
            source.SetCustomCurve(AudioSourceCurveType.CustomRolloff, rolloff);
            source.minDistance = FullVolumeDistance;
            source.dopplerLevel = 0f;
            source.spread = 40f;
            sources.Add(source);
        }
    }

    private void Emit(Cue cue, Vector3 position, float volume, float pitch)
    {
        Setting setting = settings[cue];
        if (Time.unscaledTime - setting.lastPlayed < setting.minInterval) return;

        if (listener == null) listener = FindFirstObjectByType<AudioListener>();
        float distance = listener != null ? Vector3.Distance(listener.transform.position, position) : 0f;
        if (distance > setting.range) return; // Out of earshot.

        setting.clips ??= LoadClips(setting.prefix);
        if (setting.clips.Length == 0) return;

        AudioSource source = Voice(cue, setting, distance);
        if (source == null) return;

        setting.lastPlayed = Time.unscaledTime;
        source.transform.position = position;
        source.clip = setting.clips[Random.Range(0, setting.clips.Length)];
        source.volume = Mathf.Clamp01(setting.volume * volume);
        source.pitch = pitch * (1f + Random.Range(-setting.pitchSpread, setting.pitchSpread));
        source.maxDistance = setting.range;
        source.Play();
        playing[source] = cue;
    }

    // A free voice; or, if this cue is at its limit, its farthest voice when the new sound is nearer.
    private AudioSource Voice(Cue cue, Setting setting, float distance)
    {
        AudioSource free = null;
        AudioSource farthest = null;
        float farthestDistance = distance;
        int busy = 0;
        Vector3 ear = listener != null ? listener.transform.position : Vector3.zero;

        foreach (AudioSource source in sources)
        {
            if (!source.isPlaying)
            {
                if (free == null) free = source;
                continue;
            }
            if (!playing.TryGetValue(source, out Cue other) || other != cue) continue;

            busy++;
            float d = Vector3.Distance(ear, source.transform.position);
            if (d > farthestDistance)
            {
                farthestDistance = d;
                farthest = source;
            }
        }

        if (busy < setting.voices && free != null) return free;
        if (farthest != null)
        {
            farthest.Stop();
            return farthest;
        }
        return null;
    }

    private static AudioClip[] LoadClips(string prefix)
    {
        List<AudioClip> clips = new List<AudioClip>();
        for (int i = 0; ; i++)
        {
            AudioClip clip = Resources.Load<AudioClip>($"Audio/SFX/{prefix}_{i}");
            if (clip == null) break;
            clips.Add(clip);
        }
        return clips.ToArray();
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }
}
