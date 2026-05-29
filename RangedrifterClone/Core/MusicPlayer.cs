using Microsoft.Xna.Framework.Audio;

namespace RangedrifterClone.Core;

public static class MusicPlayer
{
    // ── Intro music ──────────────────────────────────────────────────────────
    private static DynamicSoundEffectInstance? _inst;
    private static float _phase    = 0f;
    private static int   _noteIdx  = 0;
    private static float _noteTime = 0f;
    private const  int   SR = 22050;
    private const  int   BufSamples = 4096;

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
            _inst     = new DynamicSoundEffectInstance(SR, AudioChannels.Mono);
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
        for (int i = 0; i < _sfxPool.Length; i++)
        {
            try { _sfxPool[i]?.Stop(true); } catch { }
            _sfxPool[i] = null;
        }
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
                _phase += freq / SR;
                if (_phase >= 1f) _phase -= 1f;
                amp = env * (_phase < 0.5f ? 1f : -1f) * 0.28f;
            }
            _noteTime += 1f / SR;
            if (_noteTime >= dur) { _noteTime = 0f; _noteIdx = (_noteIdx + 1) % Notes.Length; }
            short s = (short)(amp * 32767f);
            buf[i * 2]     = (byte)(s & 0xFF);
            buf[i * 2 + 1] = (byte)((s >> 8) & 0xFF);
        }
        try { _inst.SubmitBuffer(buf); } catch { }
    }

    // ── SFX pool ─────────────────────────────────────────────────────────────
    private static readonly DynamicSoundEffectInstance?[] _sfxPool =
        new DynamicSoundEffectInstance?[4];
    private static readonly Random _sfxRng = new();

    private static void PlaySfx(byte[] buf, float vol)
    {
        try
        {
            for (int i = 0; i < _sfxPool.Length; i++)
            {
                _sfxPool[i] ??= new DynamicSoundEffectInstance(SR, AudioChannels.Mono);
                if (_sfxPool[i]!.State == SoundState.Playing) continue;
                _sfxPool[i]!.Volume = vol;
                _sfxPool[i]!.SubmitBuffer(buf);
                _sfxPool[i]!.Play();
                return;
            }
        }
        catch { }
    }

    // ── Footstep (60 ms low thump with pitch fall) ────────────────────────
    public static void PlayFootstep()
    {
        int n = (int)(SR * 0.06f);
        var buf = new byte[n * 2];
        float ph = 0f;
        for (int i = 0; i < n; i++)
        {
            float t   = (float)i / n;
            float env = (1f - t) * (1f - t);
            float f   = 100f + 30f * (1f - t);
            float s   = (ph < 0.5f ? 1f : -1f) * env * 0.38f;
            ph += f / SR; if (ph >= 1f) ph -= 1f;
            short ss = (short)(s * 32767f);
            buf[i * 2] = (byte)(ss & 0xFF); buf[i * 2 + 1] = (byte)((ss >> 8) & 0xFF);
        }
        PlaySfx(buf, 0.22f);
    }

    // ── Enemy growl (150 ms low noise + tone mix) ─────────────────────────
    public static void PlayGrowl()
    {
        int n = (int)(SR * 0.15f);
        var buf = new byte[n * 2];
        float ph = 0f;
        for (int i = 0; i < n; i++)
        {
            float t   = (float)i / n;
            float env = t < 0.08f ? t / 0.08f : MathF.Exp(-(t - 0.08f) * 9f);
            float noise = (float)(_sfxRng.NextDouble() * 2.0 - 1.0) * 0.28f;
            float tone  = (ph < 0.5f ? 1f : -1f) * 0.45f;
            ph += 75f / SR; if (ph >= 1f) ph -= 1f;
            float s = (noise + tone) * env * 0.22f;
            short ss = (short)(Math.Clamp(s * 32767f, -32767f, 32767f));
            buf[i * 2] = (byte)(ss & 0xFF); buf[i * 2 + 1] = (byte)((ss >> 8) & 0xFF);
        }
        PlaySfx(buf, 0.28f);
    }

    // ── Secret reveal (ascending A3→E4→A4 arpeggio, 500 ms) ──────────────
    public static void PlayReveal()
    {
        float[] freqs = { 220f, 329.63f, 440f };
        int segN = (int)(SR * 0.16f);
        int n    = segN * freqs.Length;
        var buf  = new byte[n * 2];
        for (int seg = 0; seg < freqs.Length; seg++)
        {
            float ph2 = 0f;
            for (int i = 0; i < segN; i++)
            {
                float t   = (float)i / segN;
                float env = t < 0.05f ? t / 0.05f : MathF.Exp(-(t - 0.05f) * 6f);
                float s   = (ph2 < 0.5f ? 1f : -1f) * env * 0.3f;
                ph2 += freqs[seg] / SR; if (ph2 >= 1f) ph2 -= 1f;
                int idx = seg * segN + i;
                short ss = (short)(s * 32767f);
                buf[idx * 2] = (byte)(ss & 0xFF); buf[idx * 2 + 1] = (byte)((ss >> 8) & 0xFF);
            }
        }
        PlaySfx(buf, 0.42f);
    }

    // ── Ambient drip (60 ms soft sine drip, randomised pitch) ────────────
    public static void PlayAmbientDrip()
    {
        int n = (int)(SR * 0.06f);
        var buf = new byte[n * 2];
        float f   = 600f + _sfxRng.Next(400);
        float ph2 = 0f;
        for (int i = 0; i < n; i++)
        {
            float t   = (float)i / n;
            float env = t < 0.05f ? t / 0.05f : MathF.Exp(-(t - 0.05f) * 25f);
            // sine wave (softer than square for a drip)
            float s = MathF.Sin(ph2 * MathF.PI * 2f) * env * 0.30f;
            ph2 += f / SR; if (ph2 >= 1f) ph2 -= 1f;
            short ss = (short)(s * 32767f);
            buf[i * 2] = (byte)(ss & 0xFF); buf[i * 2 + 1] = (byte)((ss >> 8) & 0xFF);
        }
        PlaySfx(buf, 0.18f);
    }
}
