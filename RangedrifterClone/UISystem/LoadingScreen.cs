using SadConsole;
using SadConsole.Input;
using SadRogue.Primitives;
using RangedrifterClone.Core;

namespace RangedrifterClone.UISystem;

/// <summary>
/// Animated ASCII art loading screen shown on startup.
/// Four phases:
///   Phase 0 (0–1.2 s)  — star-field fade-in + dungeon map reveal
///   Phase 1 (1.2–2.6 s) — enemy / player entity sprites appear
///   Phase 2 (2.6–4.0 s) — title letters reveal left-to-right
///   Phase 3 (4.0–5.5 s) — rainbow loading bar + "Press any key" pulse
/// After 1.5 s the user can skip by pressing Enter / Space / Esc.
/// </summary>
public class LoadingScreen : ScreenSurface
{
    // ── Palette ────────────────────────────────────────────────────────────
    private static readonly Color Gold      = new(255, 200,  40);
    private static readonly Color Amber     = new(200, 130,  20);
    private static readonly Color Fire      = new(255, 120,  30);
    private static readonly Color DimClr    = new( 55,  55,  55);
    private static readonly Color StarClr   = new(180, 180, 220);
    private static readonly Color WallClr   = new(120, 100,  60);
    private static readonly Color FloorClr  = new( 50,  45,  40);
    private static readonly Color PlayerClr = new(255, 220,  60);
    private static readonly Color GreenClr  = new( 80, 200,  80);
    private static readonly Color RedClr    = new(200,  80,  80);
    private static readonly Color BlueClr   = new( 80, 150, 220);
    private static readonly Color TorchClr  = new(255, 170,  50);
    private static readonly Color BgClr     = new(  4,   4,   8);

    // ── Title art ──────────────────────────────────────────────────────────
    private static readonly string[] Title =
    {
        @"██████╗  █████╗ ███╗  ██╗ ██████╗ ███████╗",
        @"██╔══██╗██╔══██╗████╗ ██║██╔════╝ ██╔════╝",
        @"███████╗███████║██╔██╗██║██║  ███╗█████╗  ",
        @"██╔══██║██╔══██║██║╚████║██║   ██║██╔══╝  ",
        @"██║  ██║██║  ██║██║ ╚███║╚██████╔╝███████╗",
        @"╚═╝  ╚═╝╚═╝  ╚═╝╚═╝  ╚═╝ ╚═════╝ ╚══════╝",
    };

    // Row gradient colours matching the main menu
    private static readonly Color[] TitleRowClr =
        { Gold, Gold, Amber, Amber, Fire, new Color(150, 90, 15) };

    // ── Dungeon backdrop art ───────────────────────────────────────────────
    private static readonly string[] DungeonArt =
    {
        @" ┌──────────────┐  ┌─────────────────┐ ",
        @" │  .  .  .  .  │  │  .  .  .  .  .  │ ",
        @" │  .  t  .  .  ├──┤  .  .  .  .  .  │ ",
        @" │  .  .  .  .  .  .  .  g  .  @  .  │ ",
        @" │  .  .  .  .  ├──┤  .  .  .  .  .  │ ",
        @" │  .  .  .  .  │  │  .  .  .  .  .  │ ",
        @" └───────┬───────┘  └────────┬─────────┘ ",
        @"         │                   │            ",
        @" ┌───────┴───────────────────┘            ",
        @" │  .  .  .  .  .  .  s  .  .            ",
        @" │  .  ≡  .  .  .  .  .  .  .            ",
        @" │  .  .  .  .  .  .  .  .  .            ",
        @" └────────────────────────── /────        ",
    };

    // ── Animation state ────────────────────────────────────────────────────
    private double _elapsed      = 0.0;
    private int    _phase        = 0;
    private bool   _canSkip      = false;
    private bool   _done         = false;

    // Stars: pre-generated
    private readonly (int X, int Y, char G, Color C)[] _stars;

    // Entity sprites that pop in during phase 1
    private static readonly (int X, int Y, char G, Color C, double At)[] Entities =
    {
        (10, 24, '@', PlayerClr,                  1.20),
        (16, 22, 'g', GreenClr,                   1.35),
        (20, 26, 's', new Color(140, 140, 200),   1.50),
        ( 8, 26, 't', RedClr,                     1.65),
        (24, 23, 'D', new Color(200, 80, 80),     1.80),
        (13, 28, '!', TorchClr,                   1.90),
        (28, 25, 'B', new Color(160, 120, 60),    2.05),
        (18, 30, 'Z', new Color(120, 200, 120),   2.20),
    };

    // Title reveal: characters revealed per-column, left to right
    private int _titleRevealed = 0;
    private double _titleRevealTimer = 0.0;

    // Bar fill
    private double _barFill = 0.0;

    // "Press any key" pulse
    private double _pulseTimer = 0.0;

    private static readonly Random _rng = new(42);

    public LoadingScreen() : base(GameHost.Instance.ScreenCellsX, GameHost.Instance.ScreenCellsY)
    {
        UseKeyboard = true;

        // Pre-generate star field
        var stars = new List<(int, int, char, Color)>();
        for (int i = 0; i < 120; i++)
        {
            int sx = _rng.Next(Width);
            int sy = _rng.Next(Height / 2);
            char g = i % 5 == 0 ? '*' : i % 3 == 0 ? '·' : '.';
            int brightness = _rng.Next(80, 220);
            var clr = new Color(brightness, brightness, brightness + _rng.Next(0, 40));
            stars.Add((sx, sy, g, clr));
        }
        _stars = stars.ToArray();

        this.Fill(Color.White, BgClr, 0);
    }

    public override void Update(TimeSpan delta)
    {
        base.Update(delta);
        if (_done) return;

        _elapsed += delta.TotalSeconds;
        if (_elapsed > 1.5) _canSkip = true;

        // ── Skip input ────────────────────────────────────────────────────
        if (_canSkip)
        {
            var kb = GameHost.Instance.Keyboard;
            if (kb.IsKeyPressed(Keys.Enter) || kb.IsKeyPressed(Keys.Space) ||
                kb.IsKeyPressed(Keys.Escape) || kb.KeysPressed.Count > 0)
            {
                TransitionToMenu();
                return;
            }
        }

        // ── Phase transitions ─────────────────────────────────────────────
        if (_elapsed >= 5.5 && _phase < 4)
        {
            TransitionToMenu();
            return;
        }

        // ── Animate ───────────────────────────────────────────────────────
        this.Fill(Color.White, BgClr, 0);
        DrawStars();
        DrawDungeon();

        if (_elapsed >= 1.20) DrawEntities();
        if (_elapsed >= 2.60) DrawTitleReveal(delta.TotalSeconds);
        if (_elapsed >= 4.00) DrawLoadingBar(delta.TotalSeconds);
    }

    private void TransitionToMenu()
    {
        _done = true;
        GameEngine.Instance.State = GameState.MainMenu;
    }

    // ── Stars phase ────────────────────────────────────────────────────────
    private void DrawStars()
    {
        double fade = Math.Min(1.0, _elapsed / 0.8);
        foreach (var (sx, sy, g, c) in _stars)
        {
            int alpha = (int)(fade * c.R);
            if (alpha < 10) continue;
            var fc = new Color(alpha, alpha, (int)(fade * c.B));
            this.SetGlyph(sx, sy, g, fc, BgClr);
        }
    }

    // ── Dungeon map phase ──────────────────────────────────────────────────
    private void DrawDungeon()
    {
        double fade = Math.Clamp((_elapsed - 0.3) / 1.0, 0.0, 1.0);
        if (fade <= 0) return;

        int ox = 2, oy = 14;

        for (int r = 0; r < DungeonArt.Length; r++)
        {
            int sy = oy + r;
            if (sy >= Height) break;
            string line = DungeonArt[r];

            // Reveal top-to-bottom with a wave
            double rowFade = Math.Clamp(fade * DungeonArt.Length - r, 0.0, 1.0);
            if (rowFade <= 0.01) continue;

            for (int c = 0; c < line.Length; c++)
            {
                int sx = ox + c;
                if (sx >= Width) break;
                char ch = line[c];
                if (ch == ' ') continue;

                Color fg = ch switch
                {
                    '@' => PlayerClr,
                    'g' => GreenClr,
                    's' => new Color(140, 140, 200),
                    't' => RedClr,
                    '≡' => new Color(200, 160, 60),
                    '/' => new Color(180, 140, 80),
                    '┌' or '┐' or '└' or '┘' or '─' or '│'
                         or '┬' or '┴' or '├' or '┤' or '┼' => WallClr,
                    '.' => FloorClr,
                    _   => DimClr,
                };

                fg = Scale(fg, (float)rowFade);
                this.SetGlyph(sx, sy, ch, fg, BgClr);
            }
        }
    }

    // ── Entity pop-in phase ────────────────────────────────────────────────
    private void DrawEntities()
    {
        foreach (var (ex, ey, g, c, at) in Entities)
        {
            if (_elapsed < at) continue;
            double pop = Math.Min(1.0, (_elapsed - at) / 0.25);
            var fc = Scale(c, (float)pop);
            if (ex < Width && ey < Height)
                this.SetGlyph(ex, ey, g, fc, BgClr);
        }

        // Torch flicker effect
        double flicker = 0.7 + 0.3 * Math.Sin(_elapsed * 9.0);
        var tColor = Scale(TorchClr, (float)flicker);
        int[] tx = { 5, 14, 25, 31 };
        int[] ty = { 16, 21, 18, 24 };
        for (int i = 0; i < tx.Length; i++)
        {
            if (tx[i] < Width && ty[i] < Height)
                this.SetGlyph(tx[i], ty[i], '¤', tColor, BgClr);
        }
    }

    // ── Title letter reveal ────────────────────────────────────────────────
    private void DrawTitleReveal(double dt)
    {
        int maxCols = Title[0].Length;
        _titleRevealTimer += dt;
        if (_titleRevealTimer > 0.035 && _titleRevealed < maxCols)
        {
            _titleRevealed = Math.Min(maxCols, _titleRevealed + 2);
            _titleRevealTimer = 0;
        }

        int ox = (Width - maxCols) / 2;
        int oy = 2;

        for (int r = 0; r < Title.Length; r++)
        {
            int sy = oy + r;
            if (sy >= Height) break;
            Color rowClr = TitleRowClr[r];

            for (int c = 0; c < Math.Min(_titleRevealed, Title[r].Length); c++)
            {
                int sx = ox + c;
                if (sx >= Width) break;
                char ch = Title[r][c];
                if (ch == ' ') { continue; }
                this.SetGlyph(sx, sy, ch, rowClr, BgClr);
            }
        }

        // "DRIFTER" sub-tagline
        if (_titleRevealed >= maxCols)
        {
            int subY = oy + Title.Length + 1;
            string sub = "D R I F T E R  ─  ASCII Roguelike  ─  First-Person Dungeons";
            int subX = (Width - sub.Length) / 2;
            if (subX >= 0 && subY < Height)
                this.Print(subX, subY, sub, Fire, BgClr);
        }
    }

    // ── Rainbow loading bar ────────────────────────────────────────────────
    private void DrawLoadingBar(double dt)
    {
        _barFill = Math.Min(1.0, _barFill + dt * 0.75);
        _pulseTimer += dt;

        int barW = Width - 10;
        int barX = 5;
        int barY = Height - 8;

        // Border
        this.Print(barX - 1, barY - 1, "Loading" + new string('.', (int)(_elapsed * 4) % 4), DimClr, BgClr);
        this.SetGlyph(barX - 1, barY,    '[', WallClr, BgClr);
        this.SetGlyph(barX + barW, barY, ']', WallClr, BgClr);

        // Fill with rainbow gradient
        int filled = (int)(_barFill * barW);
        for (int i = 0; i < barW; i++)
        {
            char ch = i < filled ? '█' : '░';
            // Rainbow: shift hue across bar width
            float hue = (float)i / barW + (float)_elapsed * 0.3f;
            var clr = i < filled ? HueToColor(hue) : DimClr;
            this.SetGlyph(barX + i, barY, ch, clr, BgClr);
        }

        // Percentage
        string pct = $"{(int)(_barFill * 100)}%";
        this.Print(barX + barW / 2 - pct.Length / 2, barY + 1, pct, Gold, BgClr);

        // "Press any key to continue" pulse
        if (_barFill >= 0.5)
        {
            double pulse = 0.5 + 0.5 * Math.Sin(_pulseTimer * 4.0);
            var pressClr = Scale(new Color(200, 220, 255), (float)pulse);
            string msg = "Press any key to continue";
            int msgX = (Width - msg.Length) / 2;
            this.Print(msgX, barY + 3, msg, pressClr, BgClr);
        }

        // Version + copyright
        string ver = "v0.3  ·  © 2026 helao2 (Pvt) Ltd.";
        this.Print((Width - ver.Length) / 2, Height - 3, ver, DimClr, BgClr);
    }

    // ── Color helpers ──────────────────────────────────────────────────────
    private static Color Scale(Color c, float f)
    {
        f = Math.Clamp(f, 0f, 1f);
        return new Color((int)(c.R * f), (int)(c.G * f), (int)(c.B * f));
    }

    private static Color HueToColor(float hue)
    {
        hue = hue - (float)Math.Floor(hue);
        float h = hue * 6f;
        float x = 1f - Math.Abs(h % 2f - 1f);
        float r, g, b;
        if      (h < 1) { r = 1; g = x; b = 0; }
        else if (h < 2) { r = x; g = 1; b = 0; }
        else if (h < 3) { r = 0; g = 1; b = x; }
        else if (h < 4) { r = 0; g = x; b = 1; }
        else if (h < 5) { r = x; g = 0; b = 1; }
        else            { r = 1; g = 0; b = x; }
        return new Color((int)(r * 230), (int)(g * 230), (int)(b * 230));
    }

    public override bool ProcessKeyboard(Keyboard keyboard) => true;
}
