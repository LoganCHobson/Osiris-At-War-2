using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

public static class DestructionFx
{
    private const int MaxVoices = 16;

    private static readonly List<AudioSource> voices = new List<AudioSource>();
    private static Transform voiceRoot;

    public static void Explode(ParticleSystem template, Vector3 position, float scale, float lifetime)
    {
        if (template == null) return;
        EffectPool.Play("Destruction/" + template.name, template, position, template.transform.rotation, lifetime, template.transform.localScale * scale);
    }

    public static GameObject Attach(ParticleSystem template, Transform parent, Vector3 position, float scale)
    {
        if (template == null) return null;

        ParticleSystem instance = Object.Instantiate(template, position, template.transform.rotation, parent);
        instance.transform.localScale = template.transform.localScale * scale;

        foreach (CFX_AutoDestructShuriken autoDestruct in instance.GetComponentsInChildren<CFX_AutoDestructShuriken>(true))
        {
            Object.Destroy(autoDestruct);
        }

        foreach (ParticleSystem system in instance.GetComponentsInChildren<ParticleSystem>(true))
        {
            ParticleSystem.MainModule main = system.main;
            main.loop = true;
            main.stopAction = ParticleSystemStopAction.None;
        }

        instance.Play(true);
        return instance.gameObject;
    }

    public static void StopEmitting(GameObject root)
    {
        if (root == null) return;
        foreach (ParticleSystem system in root.GetComponentsInChildren<ParticleSystem>())
        {
            system.Stop(false, ParticleSystemStopBehavior.StopEmitting);
        }
    }

    public static void Flash(Vector3 position, Color color, float intensity, float range, float duration)
    {
        GameObject flash = new GameObject("ExplosionFlash");
        flash.transform.position = position;
        Light light = flash.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = color;
        light.range = range;
        light.intensity = intensity;
        light.shadows = LightShadows.None;
        flash.AddComponent<ExplosionFlash>().Begin(light, duration);
    }

    public static void Blast(AudioClip[] clips, Vector3 position, DestructionProfile profile, float pitch)
    {
        if (clips == null || clips.Length == 0) return;

        AudioClip clip = clips[Random.Range(0, clips.Length)];
        if (clip == null) return;

        Camera cam = Camera.main;
        float distance = cam != null ? Vector3.Distance(cam.transform.position, position) : 0f;
        float falloff = 1f - Mathf.Clamp01(distance / Mathf.Max(1f, profile.audibleRange));
        if (falloff <= 0.01f) return;

        AudioSource source = FreeVoice(profile.mixer);
        if (source == null) return;

        float pan = 0f;
        if (cam != null)
        {
            Vector3 local = cam.transform.InverseTransformPoint(position);
            pan = Mathf.Clamp(local.x / Mathf.Max(1f, profile.audibleRange * 0.5f), -0.6f, 0.6f);
        }

        source.transform.position = position;
        source.clip = clip;
        source.volume = profile.volume * falloff * falloff;
        source.pitch = pitch * Random.Range(0.9f, 1.1f);
        source.panStereo = pan;
        source.Play();
    }

    private static AudioSource FreeVoice(AudioMixerGroup mixer)
    {
        if (voiceRoot == null)
        {
            voices.Clear();
            voiceRoot = new GameObject("DestructionAudio").transform;
        }

        foreach (AudioSource voice in voices)
        {
            if (voice != null && !voice.isPlaying)
            {
                voice.outputAudioMixerGroup = mixer;
                return voice;
            }
        }

        if (voices.Count >= MaxVoices) return null;

        GameObject child = new GameObject("Voice");
        child.transform.SetParent(voiceRoot, false);
        AudioSource created = child.AddComponent<AudioSource>();
        created.playOnAwake = false;
        created.spatialBlend = 0f;
        created.outputAudioMixerGroup = mixer;
        voices.Add(created);
        return created;
    }
}
