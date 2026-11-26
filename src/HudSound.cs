using System;
using System.IO;
using UnityEngine;

namespace ValheimNewUI;

// Plays the mod's own interface sounds exactly as they were mastered: no mixer group, no
// effects, reverb or 3D placement. The game's master volume still applies through the listener.
internal sealed class HudSound : IDisposable
{
    private readonly GameObject host;
    private readonly AudioSource source;
    private readonly AudioClip clip;

    internal float Length => clip ? clip.length : 0;

    internal HudSound(string name)
    {
        using var stream = typeof(HudSound).Assembly.GetManifestResourceStream("ValheimNewUI.sounds." + name + ".wav");
        if (stream == null) throw new FileNotFoundException("Missing sound " + name);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        clip = Decode(name, buffer.ToArray());
        host = new GameObject("ValheimNewUI_Sound_" + name);
        UnityEngine.Object.DontDestroyOnLoad(host);
        source = host.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.clip = clip;
        source.outputAudioMixerGroup = null;
        source.spatialBlend = 0;
        source.spatialize = false;
        source.spatializePostEffects = false;
        source.panStereo = 0;
        source.spread = 0;
        source.dopplerLevel = 0;
        source.reverbZoneMix = 0;
        source.bypassEffects = true;
        source.bypassListenerEffects = true;
        source.bypassReverbZones = true;
        source.priority = 0;
        source.pitch = 1;
    }

    internal void Play(float volume)
    {
        if (!source || volume <= 0) return;
        source.volume = Mathf.Clamp01(volume);
        source.Play();
    }

    internal void Stop()
    {
        if (source && source.isPlaying) source.Stop();
    }

    // 16-bit PCM WAV only: the sounds ship with the mod, so nothing else needs to be read.
    private static AudioClip Decode(string name, byte[] wav)
    {
        if (wav.Length < 12 || wav[0] != 'R' || wav[8] != 'W') throw new InvalidDataException("Not a WAV file");
        int channels = 0, rate = 0, bits = 0, dataAt = -1, dataLength = 0;
        for (int at = 12; at + 8 <= wav.Length;)
        {
            string id = System.Text.Encoding.ASCII.GetString(wav, at, 4);
            int size = BitConverter.ToInt32(wav, at + 4);
            if (id == "fmt ")
            {
                channels = BitConverter.ToInt16(wav, at + 10);
                rate = BitConverter.ToInt32(wav, at + 12);
                bits = BitConverter.ToInt16(wav, at + 22);
            }
            else if (id == "data")
            {
                dataAt = at + 8;
                dataLength = Math.Min(size, wav.Length - dataAt);
                break;
            }
            at += 8 + size + (size & 1);
        }
        if (bits != 16 || channels < 1 || rate <= 0 || dataAt < 0) throw new InvalidDataException("Unsupported WAV format");
        int samples = dataLength / 2;
        var data = new float[samples];
        for (int i = 0; i < samples; i++) data[i] = BitConverter.ToInt16(wav, dataAt + i * 2) / 32768f;
        var clip = AudioClip.Create("ValheimNewUI/" + name, samples / channels, channels, rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    public void Dispose()
    {
        if (host) UnityEngine.Object.Destroy(host);
        if (clip) UnityEngine.Object.Destroy(clip);
    }
}
