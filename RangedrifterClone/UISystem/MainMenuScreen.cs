using SadConsole;
using SadConsole.Input;
using SadRogue.Primitives;
using RangedrifterClone.Core;

namespace RangedrifterClone.UISystem;

public class MainMenuScreen : ScreenSurface
{
    private int _selected = 0;
    private readonly string[] _items = { "New Game", "Settings", "Quit" };

    // ── Palette ──────────────────────────────────────────────────────────
    private static readonly Color TitlePrimary   = new(255, 200,  40);
    private static readonly Color TitleSecondary = new(200, 130,  20);
    private static readonly Color TitleAccent    = new(255, 120,  30);
    private static readonly Color SelectColor    = new(100, 220,  60);
    private static readonly Color NormalColor    = new(160, 160, 160);
    private static readonly Color DimColor       = new( 70,  70,  70);
    private static readonly Color VersionColor   = new( 80,  80,  80);
    private static readonly Color SeparatorColor = new( 90,  65,  20);
    private static readonly Color DungeonWall    = new(100,  80,  50);
    private static readonly Color DungeonFloor   = new( 50,  45,  40);
    private static readonly Color DungeonAccent  = new( 60,  55, 100);
    private static readonly Color PlayerColor    = new(255, 220,  60);
    private static readonly Color EnemyGreen     = new( 80, 200,  80);
    private static readonly Color EnemyRed       = new(200,  80,  80);
    private static readonly Color TorchColor     = new(255, 170,  50);

    public MainMenuScreen()
        : base(GameHost.Instance.ScreenCellsX, GameHost.Instance.ScreenCellsY)
    {
        UseKeyboard = true;
        Render();
    }

    // ── Render ───────────────────────────────────────────────────────────
    private void Render()
    {
        this.Clear();

        DrawDungeonBackdrop();
        DrawAsciiTitle(2, 1);
        DrawMenuPanel();
        DrawFooter();
    }

    // ── ASCII art title — "RANGEDRIFTER" built from block glyphs ─────────
    private void DrawAsciiTitle(int ox, int oy)
    {
        // Big 3-row pixel art letters for "RANGEDRIFTER"
        // Each letter is 5 cols wide + 1 gap
        var lines = new[]
        {
            @" ██████╗  █████╗ ███╗  ██╗ ██████╗ ███████╗██████╗ ██████╗ ██╗███████╗████████╗███████╗██████╗ ",
            @" ██╔══██╗██╔══██╗████╗ ██║██╔════╝ ██╔════╝██╔══██╗██╔══██╗██║██╔════╝╚══██╔══╝██╔════╝██╔══██╗",
            @" ███████╗███████║██╔██╗██║██║  ███╗█████╗  ██║  ██║██████╔╝██║█████╗     ██║   █████╗  ██████╔╝",
            @" ██╔══██║██╔══██║██║╚████║██║   ██║██╔══╝  ██║  ██║██╔══██╗██║██╔══╝     ██║   ██╔══╝  ██╔══██╗",
            @" ██║  ██║██║  ██║██║ ╚███║╚██████╔╝███████╗██████╔╝██║  ██║██║██║        ██║   ███████╗██║  ██║",
            @" ╚═╝  ╚═╝╚═╝  ╚═╝╚═╝  ╚═╝ ╚═════╝ ╚══════╝╚═════╝ ╚═╝  ╚═╝╚═╝╚═╝        ╚═╝   ╚══════╝╚═╝  ╚═╝",
        };

        // Because the full block font art is too wide for 80 cols we render a
        // compact hand-crafted version that fits in ~76 chars.
        var compact = new[]
        {
            @"██████╗  █████╗ ███╗  ██╗ ██████╗ ███████╗",
            @"██╔══██╗██╔══██╗████╗ ██║██╔════╝ ██╔════╝",
            @"███████╗███████║██╔██╗██║██║  ███╗█████╗  ",
            @"██╔══██║██╔══██║██║╚████║██║   ██║██╔══╝  ",
            @"██║  ██║██║  ██║██║ ╚███║╚██████╔╝███████╗",
            @"╚═╝  ╚═╝╚═╝  ╚═╝╚═╝  ╚═╝ ╚═════╝ ╚══════╝",
        };
        var compact2 = new[]
        {
            @"██████╗ ██████╗ ██╗███████╗████████╗███████╗██████╗ ",
            @"██╔══██╗██╔══██╗██║██╔════╝╚══██╔══╝██╔════╝██╔══██╗",
            @"██║  ██║██████╔╝██║█████╗     ██║   █████╗  ██████╔╝",
            @"██║  ██║██╔══██╗██║██╔══╝     ██║   ██╔══╝  ██╔══██╗",
            @"██████╔╝██║  ██║██║██║        ██║   ███████╗██║  ██║",
            @"╚═════╝ ╚═╝  ╚═╝╚═╝╚═╝        ╚═╝   ╚══════╝╚═╝  ╚═╝",
        };

        // Colour gradient: top rows lighter/yellower, lower rows dimmer/orange
        Color[] rowColors =
        {
            TitlePrimary,
            TitlePrimary,
            TitleSecondary,
            TitleSecondary,
            TitleAccent,
            new Color(150, 90, 15),
        };

        // Row 1: RANGEDR
        for (int r = 0; r < compact.Length; r++)
        {
            if (oy + r >= Height) break;
            PrintColored(ox, oy + r, compact[r], rowColors[r]);
        }

        // Row 2: IFTER — placed just below with 1-row gap between for breathing room
        // Actually let's print them side by side on same rows, offset by compact width
        // compact lines are up to 44 chars; let's just do a 2-line stacked subtitle
        // For a cleaner look, show subtitle text below the block art
        int subY = oy + compact.Length + 1;
        this.Print(ox, subY,
            "  I F T E R",
            TitleAccent, Color.Black);
        this.Print(ox, subY + 1,
            new string('─', 44), SeparatorColor, Color.Black);

        // Subtitle
        this.Print(ox + 2, subY + 3,
            "An ASCII Roguelike of Dungeons & Drifters",
            new Color(130, 110, 70), Color.Black);
    }

    // ── Menu panel ───────────────────────────────────────────────────────
    private void DrawMenuPanel()
    {
        int px = 4, py = 20;
        int pw = 28, ph = _items.Length * 2 + 4;

        // Box
        DrawBox(px, py, pw, ph, new Color(70, 60, 40));

        // Header inside box
        this.Print(px + 2, py + 1, "── MAIN MENU ──", new Color(120, 100, 50), Color.Black);

        for (int i = 0; i < _items.Length; i++)
        {
            bool sel    = i == _selected;
            int  row    = py + 3 + i * 2;
            var  fgClr  = sel ? SelectColor : NormalColor;
            var  bgClr  = sel ? new Color(15, 35, 15) : Color.Black;
            var  arrow  = sel ? "►" : " ";
            string text = $" {arrow} {_items[i]}";

            // Pad to fill box width for background highlight
            text = text.PadRight(pw - 2);
            this.Print(px + 1, row, text, fgClr, bgClr);
        }
    }

    // ── Footer ───────────────────────────────────────────────────────────
    private void DrawFooter()
    {
        this.Print(2, Height - 3, "v0.21 — ECS Roguelike  ·  SadConsole 10 / MonoGame", VersionColor, Color.Black);
        this.Print(2, Height - 2, "↑↓ Navigate   Enter/Space Select", DimColor, Color.Black);
    }

    // ── Decorative dungeon scene (right side) ────────────────────────────
    private void DrawDungeonBackdrop()
    {
        // Small ASCII dungeon in the right half of the screen
        var scene = new[]
        {
            // 0         1         2         3
            // 0123456789012345678901234567890123
            @"                                  ",
            @"  ┌──────────┐  ┌─────────────┐  ",
            @"  │  . . . . │  │  . . . . .  │  ",
            @"  │  . . . . │  │  . . . . .  │  ",
            @"  │  . t . . ├──┤  . . . . .  │  ",
            @"  │  . . . . . . . . g . . .  │  ",
            @"  │  . . . . ├──┤  . . @ . .  │  ",
            @"  │  . . . . │  │  . . . . .  │  ",
            @"  └────┬─────┘  └──────┬──────┘  ",
            @"       │               │          ",
            @"  ┌────┴──────────────┬┘          ",
            @"  │  . . . . . . s .  │           ",
            @"  │  . ≡ . . . . . .  │           ",
            @"  │  . . . . . . . .  │           ",
            @"  └─────────────────/─┘           ",
        };

        int ox = 44, oy = 14;

        for (int r = 0; r < scene.Length; r++)
        {
            int screenY = oy + r;
            if (screenY >= Height) break;
            for (int c = 0; c < scene[r].Length; c++)
            {
                int screenX = ox + c;
                if (screenX >= Width) break;
                char ch = scene[r][c];
                if (ch == ' ') continue;

                Color fg = ch switch
                {
                    '@'                      => PlayerColor,
                    'g' or 's'               => EnemyGreen,
                    't'                      => EnemyRed,
                    '≡'                      => new Color(200, 160, 60),   // chest
                    '/'                      => new Color(180, 140, 80),   // stairs
                    '┌' or '┐' or '└' or '┘'
                        or '─' or '│' or '┬'
                        or '┴' or '├' or '┤'
                        or '╔' or '╗' or '╚'
                        or '╝' or '═' or '║' => DungeonWall,
                    '.'                      => DungeonFloor,
                    _                        => DimColor,
                };

                this.SetGlyph(screenX, screenY, ch, fg, Color.Black);
            }
        }

        // Legend below the dungeon scene
        int ly = oy + scene.Length + 1;
        if (ly + 4 < Height)
        {
            this.Print(ox, ly,     "  @ Player   g Goblin   s Skeleton", DimColor, Color.Black);
            this.Print(ox, ly + 1, "  t Trap     ≡ Chest    / Stairs  ", DimColor, Color.Black);
        }

        // Torchlight dots — decorative
        int[] torchX = { 45, 50, 60, 68, 73 };
        int[] torchY = {  2,  9, 16, 12,  3 };
        for (int i = 0; i < torchX.Length; i++)
        {
            int tx = torchX[i], ty = torchY[i];
            if (tx < Width && ty < Height)
                this.SetGlyph(tx, ty, '·', TorchColor, Color.Black);
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────
    private void PrintColored(int x, int y, string text, Color fg)
    {
        for (int i = 0; i < text.Length; i++)
        {
            int sx = x + i;
            if (sx >= Width) break;
            this.SetGlyph(sx, y, text[i], fg, Color.Black);
        }
    }

    private void DrawBox(int x, int y, int w, int h, Color clr)
    {
        for (int i = 1; i < w - 1; i++)
        {
            this.SetGlyph(x + i, y,         '─', clr, Color.Black);
            this.SetGlyph(x + i, y + h - 1, '─', clr, Color.Black);
        }
        for (int j = 1; j < h - 1; j++)
        {
            this.SetGlyph(x,         y + j, '│', clr, Color.Black);
            this.SetGlyph(x + w - 1, y + j, '│', clr, Color.Black);
        }
        this.SetGlyph(x,         y,         '┌', clr, Color.Black);
        this.SetGlyph(x + w - 1, y,         '┐', clr, Color.Black);
        this.SetGlyph(x,         y + h - 1, '└', clr, Color.Black);
        this.SetGlyph(x + w - 1, y + h - 1, '┘', clr, Color.Black);
    }

    // ── Input — polled every frame so it works on all keyboards ──────────
    public override void Update(TimeSpan delta)
    {
        base.Update(delta);

        var kb = GameHost.Instance.Keyboard;

        if (kb.IsKeyPressed(Keys.Up) || kb.IsKeyPressed(Keys.NumPad8))
        { _selected = (_selected - 1 + _items.Length) % _items.Length; Render(); }

        else if (kb.IsKeyPressed(Keys.Down) || kb.IsKeyPressed(Keys.NumPad2))
        { _selected = (_selected + 1) % _items.Length; Render(); }

        else if (kb.IsKeyPressed(Keys.Enter) || kb.IsKeyPressed(Keys.Space))
        {
            switch (_selected)
            {
                case 0: GameEngine.Instance.State = GameState.CharacterCreation; break;
                case 1: GameEngine.Instance.State = GameState.Settings;          break;
                case 2: Environment.Exit(0);                                     break;
            }
        }
    }

    // ProcessKeyboard is kept but all real input is handled in Update above.
    public override bool ProcessKeyboard(Keyboard keyboard) => true;
}
