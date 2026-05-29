using Microsoft.Xna.Framework.Audio;

namespace RangedrifterClone.Core;

public static class MusicPlayer
{
    private static DynamicSoundEffectInstance? _inst;
    private static float _phase    = 0f;
    private static int   _noteIdx  = 0;
    private static float _noteTime = 0f;
    private const  int   SampleRate = 22050;
    private const  int   BufSamples = 4096;

    // A-minor pentatonic intro arpeggio: ascending then descending
    private static readonly (float Freq, float Dur)[] Notes =
    {
        (220.00f, 0.25f), // A3
        (261.63f, 0.20f), // C4
        (329.63f, 0.20f), // E4
        (440.00f, 0.30f), // A4
        (523.25f, 0.20f), // C5
        (440.00f, 0.20f), // A4
        (329.63f, 0.20f), // E4
        (261.63f, 0.20f), // C4
        (220.00f, 0.40f), // A3 sustain
        (000.00f, 0.20f), // rest
    };

    public static void StartIntro()
    {
        try
        {
            StopAll();
            _inst     = new DynamicSoundEffectInstance(SampleRate, AudioChannels.Mono);
            _phase    = 0f;
            _noteIdx  = 0;
            _noteTime = 0f;
            _inst.BufferNeeded += (_, _) => SubmitBuffer();
            for (int i = 0; i < 3; i++) SubmitBuffer();
            _inst.Volume = 0.35f;
            _inst.Play();
        }
        catch { _inst = null; }
    }

    public static void StopAll()
    {
        try { _inst?.Stop(true); } catch { }
        _inst = null;
    }

    private static void SubmitBuffer()
    {
        if (_inst == null) return;
        var buf = new byte[BufSamples * 2];
        for (int i = 0; i < BufSamples; i++)
        {
            var (freq, dur) = Notes[_noteIdx];
            float amp = 0f;
            if (freq > 0f)
            {
                float t  = _noteTime;
                float a  = 0.02f;
                float r2 = Math.Min(0.04f, dur * 0.3f);
                float env = t < a ? t / a : t > dur - r2 ? Math.Max(0f, (dur - t) / r2) : 1f;
                _phase += freq / SampleRate;
                if (_phase >= 1f) _phase -= 1f;
                amp = env * (_phase < 0.5f ? 1f : -1f) * 0.28f;
            }
            _noteTime += 1f / SampleRate;
            if (_noteTime >= dur)
            {
                _noteTime = 0f;
                _noteIdx  = (_noteIdx + 1) % Notes.Length;
            }
            short s = (short)(amp * 32767f);
            buf[i * 2]     = (byte)(s & 0xFF);
            buf[i * 2 + 1] = (byte)((s >> 8) & 0xFF);
        }
        try { _inst.SubmitBuffer(buf); } catch { }
    }
}
