using SadConsole;
using SadConsole.Input;
using SadRogue.Primitives;
using RangedrifterClone.Core;

namespace RangedrifterClone.UISystem;

public sealed class StudioIntroScreen : ScreenSurface
{
    // ── Timing ───────────────────────────────────────────────────────────────
    private double _t    = 0;
    private bool   _done = false;
    private const double SkipAfter = 2.0;
    private const double FadeStart = 5.8;
    private const double TotalTime = 7.0;

    // ── Matrix rain state ────────────────────────────────────────────────────
    private readonly float[] _rainY;
    private readonly float[] _rainSpeed;
    private readonly char[]  _rainHead;
    private readonly int[]   _rainLen;
    private double _lastRainTick = 0;
    private readonly Random _rng = new();

    // ── Block letter pixel data (5 cols × 5 rows each) ───────────────────────
    // Indexed: H=0, E=1, L=2, A=3, O=4, 2=5, S=6, T=7, U=8, D=9, I=10
    private static readonly string[][] Letters =
    {
        /* H */ new[]{"█   █","█   █","█████","█   █","█   █"},
        /* E */ new[]{"█████","█    ","████ ","█    ","█████"},
        /* L */ new[]{"█    ","█    ","█    ","█    ","█████"},
        /* A */ new[]{" ███ ","█   █","█████","█   █","█   █"},
        /* O */ new[]{" ███ ","█   █","█   █","█   █"," ███ "},
        /* 2 */ new[]{" ███ ","    █"," ██  ","█    ","█████"},
        /* S */ new[]{" ████","█    "," ███ ","    █","████ "},
        /* T */ new[]{"█████","  █  ","  █  ","  █  ","  █  "},
        /* U */ new[]{"█   █","█   █","█   █","█   █"," ███ "},
        /* D */ new[]{"████ ","█   █","█   █","█   █","████ "},
        /* I */ new[]{" ███ ","  █  ","  █  ","  █  "," ███ "},
    };

    // Word index sets
    private static readonly int[] Helao2Idx = { 0, 1, 2, 3, 4, 5 };
    private static readonly int[] StudioIdx = { 6, 7, 8, 9, 10, 4 };

    // Layout constants for 80×50
    private const int W = 80, H = 50;
    private const int PresentedRow = 17;
    private const int Helao2Row    = 21;
    private const int StudioRow    = 28;
    private const int CopyRow      = 40;
    private const int SkipRow      = 42;
    private const int WordStartX   = 20; // (80-40)/2 — each 6-letter word = 40 cols

    public StudioIntroScreen() : base(W, H)
    {
        _rainY     = new float[W];
        _rainSpeed = new float[W];
        _rainHead  = new char[W];
        _rainLen   = new int[W];

        for (int i = 0; i < W; i++)
        {
            _rainY[i]     = _rng.Next(-H, 0);
            _rainSpeed[i] = 6f + (float)_rng.NextDouble() * 10f;
            _rainHead[i]  = RainChar();
            _rainLen[i]   = 4 + _rng.Next(12);
        }

        MusicPlayer.StartIntro();
    }

    public override void Update(TimeSpan delta)
    {
        base.Update(delta);
        if (_done) return;

        _t += delta.TotalSeconds;
        if (_t >= TotalTime)
        {
            _done = true;
            MusicPlayer.StopAll();
            GameEngine.Instance.State = GameState.Loading;
            return;
        }

        TickRain(delta.TotalSeconds);
        Render();
    }

    public override bool ProcessKeyboard(Keyboard info)
    {
        if (_t >= SkipAfter && info.KeysPressed.Count > 0)
        {
            _done = true;
            MusicPlayer.StopAll();
            GameEngine.Instance.State = GameState.Loading;
            return true;
        }
        return base.ProcessKeyboard(info);
    }

    // ── Rain update ───────────────────────────────────────────────────────────
    private void TickRain(double dt)
    {
        const double Interval = 0.07;
        _lastRainTick += dt;
        if (_lastRainTick < Interval) return;
        _lastRainTick = 0;

        for (int c = 0; c < W; c++)
        {
            _rainY[c] += _rainSpeed[c] * (float)Interval;
            if (_rainY[c] > H + _rainLen[c])
                _rainY[c] = -_rainLen[c];
            _rainHead[c] = RainChar();
        }
    }

    // ── Frame render ──────────────────────────────────────────────────────────
    private void Render()
    {
        Surface.Clear();

        float ga = _t >= FadeStart
            ? (float)Math.Clamp(1.0 - (_t - FadeStart) / (TotalTime - FadeStart), 0.0, 1.0)
            : 1f;

        DrawRain(ga);
        DrawPresentedBy(ga);
        DrawHelao2(ga);
        DrawStudio(ga);
        DrawFooter(ga);
    }

    private void DrawRain(float alpha)
    {
        float density = (float)Math.Min(1.0, _t / 0.6);
        if (density <= 0f) return;

        for (int col = 0; col < W; col++)
        {
            int headY = (int)_rainY[col];
            int len   = _rainLen[col];
            for (int tr = 0; tr < len; tr++)
            {
                int ry = headY - (len - 1 - tr);
                if (ry < 0 || ry >= H) continue;
                bool isHead = (tr == len - 1);
                Color c = isHead
                    ? new Color((byte)(180 * alpha * density), (byte)(255 * alpha * density), (byte)(180 * alpha * density))
                    : new Color(0, (byte)(70 * ((float)(tr + 1) / len) * alpha * density), 0);
                char ch = isHead ? _rainHead[col]
                    : (char)('!' + (col * 7 + tr * 13 + (int)(_t * 8)) % 93);
                Surface.SetGlyph(col, ry, ch);
                Surface.SetForeground(col, ry, c);
            }
        }
    }

    private void DrawPresentedBy(float alpha)
    {
        if (_t < 0.5) return;
        float t  = (float)Math.Min(1.0, (_t - 0.5) / 1.3);
        byte  a  = (byte)(200 * t * alpha);
        const string Text = "< PRESENTED BY >";
        int   x  = (W - Text.Length) / 2;
        var   cl = new Color(a, a, (byte)(a >> 1));
        for (int i = 0; i < Text.Length; i++)
        {
            Surface.SetGlyph(x + i, PresentedRow, Text[i]);
            Surface.SetForeground(x + i, PresentedRow, cl);
        }
    }

    private void DrawHelao2(float alpha)
    {
        if (_t < 1.8) return;
        float reveal = (float)Math.Min(1.0, (_t - 1.8) / 1.4);
        int   cols   = (int)(40 * reveal);
        var   bright = new Color(0, (byte)(210 * alpha), (byte)(230 * alpha));
        DrawBlockWord(Helao2Idx, WordStartX, Helao2Row, cols, bright);
    }

    private void DrawStudio(float alpha)
    {
        if (_t < 3.5) return;
        float reveal = (float)Math.Min(1.0, (_t - 3.5) / 1.1);
        int   cols   = (int)(40 * reveal);
        byte  gv     = (byte)(220 * alpha);
        var   bright = new Color(gv, (byte)(gv * 0.78f), 0);
        DrawBlockWord(StudioIdx, WordStartX, StudioRow, cols, bright);
    }

    private void DrawBlockWord(int[] indices, int startX, int startY, int maxCols, Color clr)
    {
        // Build composite rows (5 rows, each up to 40 chars: 6×5 letters + 5×2 gaps)
        var rows = new System.Text.StringBuilder[5];
        for (int r = 0; r < 5; r++) rows[r] = new();
        for (int li = 0; li < indices.Length; li++)
        {
            var letter = Letters[indices[li]];
            for (int r = 0; r < 5; r++)
            {
                rows[r].Append(letter[r]);
                if (li < indices.Length - 1) rows[r].Append("  ");
            }
        }
        // Render only up to maxCols columns
        for (int r = 0; r < 5; r++)
        {
            string line = rows[r].ToString();
            int    draw = Math.Min(maxCols, line.Length);
            for (int c = 0; c < draw; c++)
            {
                int sx = startX + c, sy = startY + r;
                if (sx < 0 || sx >= W || sy < 0 || sy >= H) continue;
                char ch = line[c];
                Surface.SetGlyph(sx, sy, ch);
                if (ch != ' ')
                    Surface.SetForeground(sx, sy, clr);
            }
        }
    }

    private void DrawFooter(float alpha)
    {
        if (_t < 5.0) return;
        float t = (float)Math.Min(1.0, (_t - 5.0) / 0.8);

        const string Copy = "© 2025 HELAO2 Studio. All rights reserved.";
        int cx = (W - Copy.Length) / 2;
        byte ca = (byte)(130 * t * alpha);
        for (int i = 0; i < Copy.Length; i++)
        {
            Surface.SetGlyph(cx + i, CopyRow, Copy[i]);
            Surface.SetForeground(cx + i, CopyRow, new Color(ca, ca, ca));
        }

        const string Skip = "[ Press any key to skip ]";
        int sx = (W - Skip.Length) / 2;
        double pulse = Math.Sin(_t * 3.5) * 0.5 + 0.5;
        byte sa = (byte)(160 * t * pulse * alpha);
        for (int i = 0; i < Skip.Length; i++)
        {
            Surface.SetGlyph(sx + i, SkipRow, Skip[i]);
            Surface.SetForeground(sx + i, SkipRow, new Color(sa, sa, sa));
        }
    }

    private char RainChar()
    {
        const string Pool = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789!@#$%^&*+-=<>?|";
        return Pool[_rng.Next(Pool.Length)];
    }
}
