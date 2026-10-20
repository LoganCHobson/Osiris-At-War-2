using System.Collections.Generic;
using System.IO;
using Unity.Collections;
using UnityEngine;

// Dev recording helper: captures the game's mixed audio in step with Time.captureFramerate (call
// CaptureFrame once per recorded frame) and writes it out as a 16-bit WAV for muxing with the frames.
public class AudioCapture
{
    private readonly List<short> samples = new List<short>();
    private readonly int channels;

    public AudioCapture()
    {
        channels = AudioSettings.speakerMode == AudioSpeakerMode.Mono ? 1 : 2;
        AudioRenderer.Start();
    }

    public void CaptureFrame()
    {
        int count = AudioRenderer.GetSampleCountForCaptureFrame() * channels;
        if (count <= 0) return;
        using (NativeArray<float> buffer = new NativeArray<float>(count, Allocator.Temp))
        {
            AudioRenderer.Render(buffer);
            for (int i = 0; i < count; i++) samples.Add((short)(Mathf.Clamp(buffer[i], -1f, 1f) * short.MaxValue));
        }
    }

    public void End(string path)
    {
        AudioRenderer.Stop();
        int rate = AudioSettings.outputSampleRate;
        using (BinaryWriter wav = new BinaryWriter(File.Create(path)))
        {
            int bytes = samples.Count * 2;
            wav.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
            wav.Write(36 + bytes);
            wav.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
            wav.Write(16);
            wav.Write((short)1);
            wav.Write((short)channels);
            wav.Write(rate);
            wav.Write(rate * channels * 2);
            wav.Write((short)(channels * 2));
            wav.Write((short)16);
            wav.Write(System.Text.Encoding.ASCII.GetBytes("data"));
            wav.Write(bytes);
            foreach (short s in samples) wav.Write(s);
        }
    }
}
