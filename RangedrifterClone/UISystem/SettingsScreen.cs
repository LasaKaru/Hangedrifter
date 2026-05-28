using SadConsole;
using SadConsole.Input;
using SadRogue.Primitives;
using RangedrifterClone.Core;

namespace RangedrifterClone.UISystem;

/// <summary>
/// Settings / Options screen. All settings are stored in a static
/// <see cref="GameSettings"/> class so they persist between sessions in RAM.
/// </summary>
public class SettingsScreen : ScreenSurface
{
    private int _cursor = 0;

    private static readonly Color HeaderClr  = new(255, 200,  50);
    private static readonly Color SelectClr  = new(100, 220,  80);
    private static readonly Color NormalClr  = new(160, 160, 160);
    private static readonly Color DimClr     = new( 70,  70,  70);
    private static readonly Color ValueClr   = new(100, 180, 220);
    private static readonly Color DivClr     = new( 60,  60,  60);

    // ── Setting entries ──────────────────────────────────────────────────
    private record Setting(string Label, string[] Options, Func<int> Get, Action<int> Set);
    private readonly List<Setting> _settings;

    public SettingsScreen()
        : base(GameHost.Instance.ScreenCellsX, GameHost.Instance.ScreenCellsY)
    {
        UseKeyboard = true;
        _settings = new List<Setting>
        {
            new("Difficulty",       new[]{"Normal","Hard","Nightmare"},
                () => GameSettings.Difficulty,   v => GameSettings.Difficulty   = v),
            new("FOV Radius",       new[]{"7","9","12","Full Map"},
                () => GameSettings.FovRadius,    v => GameSettings.FovRadius    = v),
            new("Message Log Size", new[]{"Small (8)","Medium (13)","Large (18)"},
                () => GameSettings.LogSize,      v => GameSettings.LogSize      = v),
            new("Colour Scheme",    new[]{"Classic","Sepia","Monochrome","Neon"},
                () => GameSettings.ColourScheme, v => GameSettings.ColourScheme = v),
            new("Map Width",        new[]{"120","160","200","250"},
                () => GameSettings.MapSize,      v => GameSettings.MapSize      = v),
            new("Show FPS",         new[]{"Off","On"},
                () => GameSettings.ShowFps ? 1 : 0, v => GameSettings.ShowFps  = v == 1),
            new("Fullscreen",       new[]{"Windowed","Fullscreen"},
                () => GameSettings.Fullscreen ? 1 : 0,
                v => { GameSettings.Fullscreen = v == 1; GameSettings.ApplyFullscreen(); }),
        };
        Render();
    }

    public void Refresh() => Render();

    private void Render()
    {
        this.Clear();
        int w = Width, h = Height;

        // ── Title ────────────────────────────────────────────────────
        this.Print(2, 1, "OPTIONS & SETTINGS", HeaderClr);
        this.Print(2, 2, new string('═', 22), DivClr);

        // ── Settings box ─────────────────────────────────────────────
        DrawBox(4, 4, w - 8, _settings.Count * 3 + 2, DimClr);

        for (int i = 0; i < _settings.Count; i++)
        {
            var s     = _settings[i];
            bool sel  = i == _cursor;
            int  row  = 5 + i * 3;
            int  val  = s.Get();

            var labelClr = sel ? SelectClr : NormalClr;
            var arrow    = sel ? "►" : " ";

            this.Print(6,      row,     $"{arrow} {s.Label}", labelClr);
            DrawOptionBar(6, row + 1, s.Options, val, sel);
        }

        // ── Controls hint ─────────────────────────────────────────────
        int bY = _settings.Count * 3 + 8;
        this.Print(6, bY,     "↑↓  Navigate   ←→  Change value", DimClr);
        this.Print(6, bY + 1, "Esc / Backspace  Return to menu", DimClr);

        // ── ASCII dungeon scene decoration (bottom-right) ─────────────
        DrawDungeonScene(w - 28, h - 16);
    }

    private void DrawOptionBar(int x, int y, string[] opts, int current, bool selected)
    {
        for (int i = 0; i < opts.Length; i++)
        {
            bool active = i == current;
            var  bg     = active  ? (selected ? new Color(30, 60, 30) : new Color(25, 45, 60)) : Color.Black;
            var  fg     = active  ? (selected ? SelectClr : ValueClr) : DimClr;
            var  pre    = active  ? "[" : " ";
            var  suf    = active  ? "]" : " ";
            this.Print(x, y, $"{pre}{opts[i]}{suf}", fg, bg);
            x += opts[i].Length + 3;
        }
    }

    private void DrawDungeonScene(int ox, int oy)
    {
        // Small decorative dungeon layout
        var scene = new[]
        {
            "  .  *  . *   .  *  . * .",
            " *                      * ",
            "   +-----+  +--------+   ",
            "   | . . |  | . . .  |   ",
            "   |  s  +--+  . .   |   ",
            "   | . . . .  g . @  |   ",
            "   |  .  +--+   . .  |   ",
            "   +-----+  +--------+   ",
            " .                      .",
            "  * .  * .  * .  * .  *  ",
        };
        var sceneClr = new Color(55, 55, 55);
        for (int row = 0; row < scene.Length; row++)
        {
            if (oy + row >= Height) break;
            for (int col = 0; col < scene[row].Length; col++)
            {
                if (ox + col >= Width) break;
                char ch = scene[row][col];
                var fg = ch switch
                {
                    '@' => new Color(255, 220,  50),
                    's' => new Color(160, 160, 160),
                    'g' => new Color( 80, 200,  80),
                    '+' or '-' or '|' => new Color(120, 100, 70),
                    '*' => new Color( 80,  80, 120),
                    '.' => new Color( 60,  60,  60),
                    _   => sceneClr
                };
                this.SetGlyph(ox + col, oy + row, ch, fg, Color.Black);
            }
        }
    }

    private void DrawBox(int x, int y, int w, int h, Color clr)
    {
        for (int i = 1; i < w - 1; i++)
        {
            this.SetGlyph(x + i, y,     '─', clr, Color.Black);
            this.SetGlyph(x + i, y + h, '─', clr, Color.Black);
        }
        for (int j = 1; j < h; j++)
        {
            this.SetGlyph(x,         y + j, '│', clr, Color.Black);
            this.SetGlyph(x + w - 1, y + j, '│', clr, Color.Black);
        }
        this.SetGlyph(x,         y,     '┌', clr, Color.Black);
        this.SetGlyph(x + w - 1, y,     '┐', clr, Color.Black);
        this.SetGlyph(x,         y + h, '└', clr, Color.Black);
        this.SetGlyph(x + w - 1, y + h, '┘', clr, Color.Black);
    }

    // ── Input — poll GameHost directly so it works on all keyboards ───────
    public override void Update(TimeSpan delta)
    {
        base.Update(delta);
        var kb = GameHost.Instance.Keyboard;

        if (kb.IsKeyPressed(Keys.Up)   || kb.IsKeyPressed(Keys.NumPad8))
        { _cursor = (_cursor - 1 + _settings.Count) % _settings.Count; Render(); }

        else if (kb.IsKeyPressed(Keys.Down) || kb.IsKeyPressed(Keys.NumPad2))
        { _cursor = (_cursor + 1) % _settings.Count; Render(); }

        else if (kb.IsKeyPressed(Keys.Left) || kb.IsKeyPressed(Keys.NumPad4))
        {
            var s = _settings[_cursor];
            int v = (s.Get() - 1 + s.Options.Length) % s.Options.Length;
            s.Set(v); Render();
        }
        else if (kb.IsKeyPressed(Keys.Right) || kb.IsKeyPressed(Keys.NumPad6))
        {
            var s = _settings[_cursor];
            int v = (s.Get() + 1) % s.Options.Length;
            s.Set(v); Render();
        }
        else if (kb.IsKeyPressed(Keys.Escape) || kb.IsKeyPressed(Keys.Back))
        {
            GameEngine.Instance.State = GameState.MainMenu;
        }
    }

    public override bool ProcessKeyboard(Keyboard keyboard) => true; // handled in Update
}

/// <summary>Global game settings, readable from any system.</summary>
public static class GameSettings
{
    public static int  Difficulty    { get; set; } = 0;  // 0=Normal, 1=Hard, 2=Nightmare
    public static int  FovRadius     { get; set; } = 1;  // index into {7,9,12,Full}
    public static int  LogSize       { get; set; } = 1;  // index into {8,13,18}
    public static int  ColourScheme  { get; set; } = 0;
    public static int  MapSize       { get; set; } = 2;  // index into {120,160,200,250}
    public static bool ShowFps       { get; set; } = false;
    public static bool Fullscreen    { get; set; } = false;

    public static int ActualFovRadius   => FovRadius switch { 0=>7, 1=>9, 2=>12, _=>50 };
    public static int ActualMapWidth    => MapSize  switch { 0=>120, 1=>160, 2=>200, _=>250 };
    public static int ActualLogRows     => LogSize  switch { 0=>8,  1=>13, _=>18 };
    public static float DifficultyMult  => Difficulty switch { 0=>1f, 1=>1.5f, _=>2f };

    public static void ApplyFullscreen()
    {
        var gdm = SadConsole.Host.Global.GraphicsDeviceManager;
        if (gdm != null)
        {
            gdm.IsFullScreen = Fullscreen;
            gdm.ApplyChanges();
        }
    }
}
