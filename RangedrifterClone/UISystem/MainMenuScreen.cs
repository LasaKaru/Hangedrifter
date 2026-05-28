using SadConsole;
using SadConsole.Input;
using SadRogue.Primitives;
using RangedrifterClone.Core;

namespace RangedrifterClone.UISystem;

/// <summary>
/// Main menu with inline overlay sub-pages:
///   Main   — title + menu choices
///   HowToPlay — controls, combat, tips
///   About     — project story, motivation, credits, copyright
///   Technical — ECS architecture, rendering pipeline, tech stack
/// </summary>
public class MainMenuScreen : ScreenSurface
{
    // ── Sub-page state ────────────────────────────────────────────────────
    private enum Page { Main, HowToPlay, About, Technical }
    private Page _page     = Page.Main;
    private int  _selected = 0;

    private static readonly string[] MenuItems =
        { "New Game", "How To Play", "About", "Settings", "Quit" };

    // ── Palette ───────────────────────────────────────────────────────────
    private static readonly Color Gold      = new(255, 200,  40);
    private static readonly Color Amber     = new(200, 130,  20);
    private static readonly Color Fire      = new(255, 120,  30);
    private static readonly Color SelClr    = new(100, 220,  60);
    private static readonly Color NormClr   = new(160, 160, 160);
    private static readonly Color DimClr    = new( 70,  70,  70);
    private static readonly Color VerClr    = new( 80,  80,  80);
    private static readonly Color SepClr    = new( 90,  65,  20);
    private static readonly Color WallClr   = new(100,  80,  50);
    private static readonly Color FloorClr  = new( 50,  45,  40);
    private static readonly Color PlayerClr = new(255, 220,  60);
    private static readonly Color EnemyGrn  = new( 80, 200,  80);
    private static readonly Color EnemyRed  = new(200,  80,  80);
    private static readonly Color TorchClr  = new(255, 170,  50);
    private static readonly Color ChromeClr = new( 80,  80, 100);
    private static readonly Color InfoClr   = new(160, 180, 200);
    private static readonly Color HeadClr   = new(220, 200, 100);
    private static readonly Color KeyClr    = new(255, 210,  80);
    private static readonly Color BrandClr  = new(100, 180, 255);
    private static readonly Color CopyClr   = new(120, 120, 140);

    // ── Construction ─────────────────────────────────────────────────────
    public MainMenuScreen()
        : base(GameHost.Instance.ScreenCellsX, GameHost.Instance.ScreenCellsY)
    {
        UseKeyboard = true;
        Render();
    }

    // ── Master render dispatcher ─────────────────────────────────────────
    private void Render()
    {
        this.Clear();
        switch (_page)
        {
            case Page.Main:      RenderMain();      break;
            case Page.HowToPlay: RenderHowToPlay(); break;
            case Page.About:     RenderAbout();     break;
            case Page.Technical: RenderTechnical(); break;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // MAIN MENU PAGE
    // ═══════════════════════════════════════════════════════════════════════
    private void RenderMain()
    {
        DrawDungeonBackdrop();
        DrawAsciiTitle(2, 1);
        DrawMenuPanel();
        this.Print(2, Height - 3,
            "v0.3  ·  SadConsole 10 / MonoGame  ·  © 2026 helao2 (Pvt) Ltd.",
            VerClr, Color.Black);
        this.Print(2, Height - 2, "↑↓ Navigate    Enter/Space Select", DimClr, Color.Black);
    }

    private void DrawAsciiTitle(int ox, int oy)
    {
        var compact = new[]
        {
            @"██████╗  █████╗ ███╗  ██╗ ██████╗ ███████╗",
            @"██╔══██╗██╔══██╗████╗ ██║██╔════╝ ██╔════╝",
            @"███████╗███████║██╔██╗██║██║  ███╗█████╗  ",
            @"██╔══██║██╔══██║██║╚████║██║   ██║██╔══╝  ",
            @"██║  ██║██║  ██║██║ ╚███║╚██████╔╝███████╗",
            @"╚═╝  ╚═╝╚═╝  ╚═╝╚═╝  ╚═╝ ╚═════╝ ╚══════╝",
        };
        Color[] rowColors = { Gold, Gold, Amber, Amber, Fire, new Color(150, 90, 15) };
        for (int r = 0; r < compact.Length; r++)
        {
            if (oy + r >= Height) break;
            PrintColored(ox, oy + r, compact[r], rowColors[r]);
        }
        int subY = oy + compact.Length + 1;
        this.Print(ox, subY, "  I F T E R", Fire, Color.Black);
        this.Print(ox, subY + 1, new string('─', 44), SepClr, Color.Black);
        this.Print(ox + 2, subY + 3,
            "An ASCII Roguelike of Dungeons & Drifters", new Color(130, 110, 70), Color.Black);
    }

    private void DrawMenuPanel()
    {
        int px = 4, py = 20;
        int pw = 28, ph = MenuItems.Length * 2 + 4;
        DrawBox(px, py, pw, ph, new Color(70, 60, 40));
        this.Print(px + 2, py + 1, "── MAIN MENU ──", new Color(120, 100, 50), Color.Black);
        for (int i = 0; i < MenuItems.Length; i++)
        {
            bool sel   = i == _selected;
            int  row   = py + 3 + i * 2;
            var  fg    = sel ? SelClr  : NormClr;
            var  bg    = sel ? new Color(15, 35, 15) : Color.Black;
            var  arrow = sel ? "►" : " ";
            string text = ($" {arrow} {MenuItems[i]}").PadRight(pw - 2);
            this.Print(px + 1, row, text, fg, bg);
        }
    }

    private void DrawDungeonBackdrop()
    {
        var scene = new[]
        {
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
            int sy = oy + r; if (sy >= Height) break;
            for (int c = 0; c < scene[r].Length; c++)
            {
                int sx = ox + c; if (sx >= Width) break;
                char ch = scene[r][c]; if (ch == ' ') continue;
                Color fg = ch switch
                {
                    '@' => PlayerClr, 'g' or 's' => EnemyGrn, 't' => EnemyRed,
                    '≡' => new Color(200, 160, 60), '/' => new Color(180, 140, 80),
                    '┌' or '┐' or '└' or '┘' or '─' or '│'
                        or '┬' or '┴' or '├' or '┤' => WallClr,
                    '.' => FloorClr,
                    _   => DimClr,
                };
                this.SetGlyph(sx, sy, ch, fg, Color.Black);
            }
        }
        int ly = oy + scene.Length + 1;
        if (ly + 2 < Height)
        {
            this.Print(ox, ly,   "  @ You   g Goblin   s Skeleton", DimClr, Color.Black);
            this.Print(ox, ly+1, "  t Trap  ≡ Chest    / Stairs  ", DimClr, Color.Black);
        }
        int[] torchX = { 45, 50, 60, 68, 73 };
        int[] torchY = {  2,  9, 16, 12,  3 };
        for (int i = 0; i < torchX.Length; i++)
            if (torchX[i] < Width && torchY[i] < Height)
                this.SetGlyph(torchX[i], torchY[i], '·', TorchClr, Color.Black);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // HOW TO PLAY PAGE
    // ═══════════════════════════════════════════════════════════════════════
    private void RenderHowToPlay()
    {
        // Full-screen overlay box
        DrawOverlayBox("HOW TO PLAY", new Color(60, 100, 60));

        int x = 3, y = 3;

        Section(ref y, "GOAL", x);
        Info(ref y, "Descend through procedurally-generated dungeon floors,", x);
        Info(ref y, "battle enemies, gather loot, level up, and defeat the", x);
        Info(ref y, "boss lurking on every 5th floor.  Survive as long as", x);
        Info(ref y, "you can — permadeath is not (yet) enabled.", x);
        y++;

        Section(ref y, "VIEW MODES", x);
        Key(ref y, "Tab",     "Toggle 3D first-person ↔ overhead map", x);
        y++;

        Section(ref y, "MOVEMENT  (First-Person)", x);
        Key(ref y, "Mouse ←→",   "Aim / turn camera", x);
        Key(ref y, "Esc",         "Release cursor   |   Click = recapture", x);
        Key(ref y, "W  /  ↑",    "Move forward", x);
        Key(ref y, "S  /  ↓",    "Move backward", x);
        Key(ref y, "Q",           "Strafe left", x);
        Key(ref y, "← / →",      "Turn left / right (keyboard)", x);
        Key(ref y, "NumPad",      "8-directional movement in overhead view", x);
        y++;

        Section(ref y, "COMBAT", x);
        Key(ref y, "Bump enemy",  "Melee attack (walk into enemy tile)", x);
        Key(ref y, "Space / F",   "Fire ranged shot in facing direction", x);
        Key(ref y, "Left-click",  "Ranged shot (while mouse aim active)", x);
        Key(ref y, "1 – 4",       "Use class ability  (costs mana)", x);
        y++;

        Section(ref y, "ACTIONS", x);
        Key(ref y, "G  or  ,",    "Pick up item from the floor", x);
        Key(ref y, "E",           "Open equipment menu  (# to equip/unequip)", x);
        Key(ref y, "U",           "Use first consumable in inventory", x);
        Key(ref y, ".  or  5",    "Wait one turn  (enemies still act)", x);
        Key(ref y, "> or <",      "Descend / ascend stairs (walk onto tile)", x);
        y++;

        Section(ref y, "TIPS", x);
        Tip(ref y, "Shoot first — pull single enemies with ranged before closing.", x);
        Tip(ref y, "Mana regenerates 1 pt/turn.  Conserve abilities for tough fights.", x);
        Tip(ref y, "Chests are sometimes trapped.  Watch HP when opening.", x);
        Tip(ref y, "The mini-map (bottom-right in 3D view) shows explored terrain.", x);

        Footer("Esc = back to menu");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // ABOUT PAGE
    // ═══════════════════════════════════════════════════════════════════════
    private void RenderAbout()
    {
        DrawOverlayBox("ABOUT  RANGEDRIFTER", new Color(60, 60, 110));

        int x = 3, y = 3;

        // helao2 brand badge (top-right corner)
        string brand = "helao2 (Pvt) Ltd.";
        this.Print(Width - brand.Length - 3, 2, brand, BrandClr, Color.Black);

        Section(ref y, "WHAT IS RANGEDRIFTER?", x);
        Info(ref y, "RANGEDRIFTER is a first-person ASCII roguelike built with", x);
        Info(ref y, "SadConsole 10 and MonoGame.  It fuses turn-based dungeon", x);
        Info(ref y, "crawling with a Wolfenstein-style 3D view rendered entirely", x);
        Info(ref y, "in coloured text characters — no textures, no polygons,", x);
        Info(ref y, "only Unicode glyphs and carefully chosen RGB colours.", x);
        y++;

        Section(ref y, "MOTIVATION & VISION", x);
        Info(ref y, "Born from a love of classic roguelikes (NetHack, Angband,", x);
        Info(ref y, "DCSS) and retro 3D shooters (Wolfenstein 3D, DOOM), the", x);
        Info(ref y, "project asks:  can ASCII art be rendered in first-person", x);
        Info(ref y, "perspective and still feel immersive?  Block characters,", x);
        Info(ref y, "per-column distance shading, billboard sprite projection,", x);
        Info(ref y, "and a full ECS game engine say — yes.", x);
        y++;

        Section(ref y, "KEY FEATURES", x);
        Bullet(ref y, "Wolfenstein DDA ray-cast  +  sprite Z-buffer", x);
        Bullet(ref y, "5 procedurally-generated dungeon themes", x);
        Bullet(ref y, "3 playable classes (Warrior · Rogue · Mage)", x);
        Bullet(ref y, "ECS architecture — 9 independent game systems", x);
        Bullet(ref y, "Mouse-aim, ranged shooting, melee, abilities", x);
        Bullet(ref y, "Full FOV · AI state machine · loot · equipment", x);
        y++;

        Section(ref y, "CREDITS", x);
        Info(ref y, "Designed & coded by the helao2 (Pvt) Ltd. dev team.", x);
        Info(ref y, "AI-assisted development powered by Claude (Anthropic).", x);
        Info(ref y, "Engine  :  SadConsole 10.x  +  MonoGame 3.8 DesktopGL", x);
        Info(ref y, "Language:  C# 12  on  .NET 8   (Windows · Linux · macOS)", x);
        y++;

        // Copyright block
        DrawBox(2, y, Width - 4, 4, new Color(50, 50, 80));
        string copy1 = "© 2026  helao2 (Pvt) Ltd.  All Rights Reserved.";
        string copy2 = "RANGEDRIFTER is a trademark of helao2 (Pvt) Ltd.";
        int cx1 = (Width - copy1.Length) / 2, cx2 = (Width - copy2.Length) / 2;
        this.Print(cx1, y + 1, copy1, CopyClr, Color.Black);
        this.Print(cx2, y + 2, copy2, new Color(80, 80, 100), Color.Black);

        Footer("Esc = menu    T = Technical Details");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // TECHNICAL DETAILS PAGE
    // ═══════════════════════════════════════════════════════════════════════
    private void RenderTechnical()
    {
        DrawOverlayBox("TECHNICAL DETAILS", new Color(80, 60, 100));

        int x = 3, y = 3;

        Section(ref y, "RENDERING PIPELINE", x);
        Tech(ref y, "View mode",     "DDA ray-cast, 57 rays/frame, 66° FOV", x);
        Tech(ref y, "Wall chars",    "█▓▒░ by distance; Y-face 30% darker", x);
        Tech(ref y, "Ceiling",       "Per-column black→indigo gradient", x);
        Tech(ref y, "Floor",         "Per-column moss-green→black gradient", x);
        Tech(ref y, "Sprites",       "Billboard projection + per-column Z-buffer", x);
        Tech(ref y, "Player body",   "Class ASCII art, 8 rows, 1-row breathing bob", x);
        Tech(ref y, "Glow FX",       "sin() pulse on class colour; horizon bloom", x);
        Tech(ref y, "Muzzle flash",  "Ring of * + trail '·' + hit column │, 0.18 s", x);
        y++;

        Section(ref y, "ECS ARCHITECTURE", x);
        Tech(ref y, "Entities",      "int IDs managed by EntityManager", x);
        Tech(ref y, "Components",    "Pure data: Fighter, AI, Position, Render, …", x);
        Tech(ref y, "Systems (9)",   "Movement · Combat · AI · FOV · Inventory", x);
        Info(ref y, "               Ability · Equipment · StatusEffect · Feature", x);
        Tech(ref y, "Data files",    "JSON: classes.json · enemies.json · items.json", x);
        y++;

        Section(ref y, "MAP GENERATION", x);
        Tech(ref y, "Algorithm",     "BSP (Binary Space Partitioning)", x);
        Tech(ref y, "Map size",      "200 × 200 tiles per floor", x);
        Tech(ref y, "Min split",     "12 tiles per axis (prevents crash)", x);
        Tech(ref y, "Themes",        "Dungeon · Cave · Crypt · Mines · Forest", x);
        Tech(ref y, "Floor scaling", "Theme rotates every 2 floors", x);
        Tech(ref y, "FOV",           "Recursive shadowcasting, radius 9", x);
        y++;

        Section(ref y, "AI SYSTEM", x);
        Tech(ref y, "State machine", "Idle → Alerted → Hunting → Investigating → Flee", x);
        Tech(ref y, "Behaviours",    "Basic · Passive · Aggressive · Ranged", x);
        Info(ref y, "               Coward · Pack · Boss", x);
        Tech(ref y, "Memory",        "Last-known player position per enemy", x);
        y++;

        Section(ref y, "TECH STACK", x);
        Tech(ref y, "Language",  "C# 12",                              x);
        Tech(ref y, "Runtime",   ".NET 8  (net8.0 — cross-platform)",   x);
        Tech(ref y, "Rendering", "SadConsole 10.x + MonoGame 3.8 DesktopGL", x);
        Tech(ref y, "Primitives","SadRogue.Primitives (Color, Point)",  x);
        Tech(ref y, "Input",     "SadConsole.Input.Keyboard + XInput.Mouse", x);
        Tech(ref y, "Serialise", "System.Text.Json",                   x);

        // Copyright strip
        string copy = "© 2026  helao2 (Pvt) Ltd.  All Rights Reserved.";
        this.Print((Width - copy.Length) / 2, Height - 3, copy, CopyClr, Color.Black);

        Footer("Esc = menu    A = About");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // SHARED OVERLAY HELPERS
    // ═══════════════════════════════════════════════════════════════════════
    private void DrawOverlayBox(string title, Color accentClr)
    {
        // Solid dark background
        for (int ry = 0; ry < Height; ry++)
        for (int rx = 0; rx < Width; rx++)
            this.SetGlyph(rx, ry, ' ', Color.Black, new Color(4, 4, 8));

        // Double-line border
        DrawDoubleBorder(0, 0, Width, Height, accentClr);

        // Title in top border
        string t = $"  ╡ {title} ╞  ";
        int tx = (Width - t.Length) / 2;
        this.Print(tx, 0, t, HeadClr, new Color(4, 4, 8));

        // Thin separator under row 2
        for (int rx = 1; rx < Width - 1; rx++)
            this.SetGlyph(rx, 2, '─', new Color(40, 40, 60), new Color(4, 4, 8));
    }

    private void Section(ref int y, string label, int x)
    {
        if (y >= Height - 3) return;
        string line = $"── {label} ";
        int    fill  = Width - x - line.Length - 2;
        if (fill > 0) line += new string('─', fill);
        this.Print(x, y++, line, HeadClr, new Color(4, 4, 8));
    }

    private void Info(ref int y, string text, int x)
    {
        if (y >= Height - 3) return;
        this.Print(x, y++, text, InfoClr, new Color(4, 4, 8));
    }

    private void Key(ref int y, string key, string desc, int x)
    {
        if (y >= Height - 3) return;
        string kpad = key.PadRight(14);
        this.Print(x,      y, kpad, KeyClr,  new Color(4, 4, 8));
        this.Print(x + 14, y, desc, InfoClr, new Color(4, 4, 8));
        y++;
    }

    private void Tech(ref int y, string label, string desc, int x)
    {
        if (y >= Height - 3) return;
        string lpad = label.PadRight(14);
        this.Print(x,      y, lpad, new Color(160, 200, 160), new Color(4, 4, 8));
        this.Print(x + 14, y, desc, InfoClr,                   new Color(4, 4, 8));
        y++;
    }

    private void Tip(ref int y, string text, int x)
    {
        if (y >= Height - 3) return;
        this.Print(x, y, "▸ ", new Color(200, 160, 60), new Color(4, 4, 8));
        this.Print(x + 2, y++, text, InfoClr, new Color(4, 4, 8));
    }

    private void Bullet(ref int y, string text, int x)
    {
        if (y >= Height - 3) return;
        this.Print(x,     y, "• ", new Color(100, 200, 100), new Color(4, 4, 8));
        this.Print(x + 2, y++, text, InfoClr, new Color(4, 4, 8));
    }

    private void Footer(string hints)
    {
        for (int rx = 1; rx < Width - 1; rx++)
            this.SetGlyph(rx, Height - 2, '─', new Color(40, 40, 60), new Color(4, 4, 8));
        int fx = (Width - hints.Length) / 2;
        this.Print(fx, Height - 2, $" {hints} ", DimClr, new Color(4, 4, 8));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // SHARED DRAW HELPERS
    // ═══════════════════════════════════════════════════════════════════════
    private void PrintColored(int x, int y, string text, Color fg)
    {
        for (int i = 0; i < text.Length; i++)
        { int sx = x + i; if (sx >= Width) break; this.SetGlyph(sx, y, text[i], fg, Color.Black); }
    }

    private void DrawBox(int x, int y, int w, int h, Color clr)
    {
        var bg = new Color(4, 4, 8);
        for (int i = 1; i < w - 1; i++)
        { this.SetGlyph(x+i, y, '─', clr, bg); this.SetGlyph(x+i, y+h-1, '─', clr, bg); }
        for (int j = 1; j < h - 1; j++)
        { this.SetGlyph(x, y+j, '│', clr, bg); this.SetGlyph(x+w-1, y+j, '│', clr, bg); }
        this.SetGlyph(x,     y,     '┌', clr, bg); this.SetGlyph(x+w-1, y,     '┐', clr, bg);
        this.SetGlyph(x,     y+h-1, '└', clr, bg); this.SetGlyph(x+w-1, y+h-1, '┘', clr, bg);
    }

    private void DrawDoubleBorder(int x, int y, int w, int h, Color clr)
    {
        var bg = new Color(4, 4, 8);
        for (int i = 1; i < w - 1; i++)
        { this.SetGlyph(x+i, y, '═', clr, bg); this.SetGlyph(x+i, y+h-1, '═', clr, bg); }
        for (int j = 1; j < h - 1; j++)
        { this.SetGlyph(x, y+j, '║', clr, bg); this.SetGlyph(x+w-1, y+j, '║', clr, bg); }
        this.SetGlyph(x,     y,     '╔', clr, bg); this.SetGlyph(x+w-1, y,     '╗', clr, bg);
        this.SetGlyph(x,     y+h-1, '╚', clr, bg); this.SetGlyph(x+w-1, y+h-1, '╝', clr, bg);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // INPUT — polled every frame
    // ═══════════════════════════════════════════════════════════════════════
    public override void Update(TimeSpan delta)
    {
        base.Update(delta);
        var kb = GameHost.Instance.Keyboard;

        // ── Sub-page navigation ──────────────────────────────────────────
        if (_page != Page.Main)
        {
            if (kb.IsKeyPressed(Keys.Escape))
            { _page = Page.Main; Render(); return; }

            // About ↔ Technical cross-link
            if (_page == Page.About && kb.IsKeyPressed(Keys.T))
            { _page = Page.Technical; Render(); return; }
            if (_page == Page.Technical && kb.IsKeyPressed(Keys.A))
            { _page = Page.About; Render(); return; }

            return; // no further input on sub-pages
        }

        // ── Main menu navigation ─────────────────────────────────────────
        if (kb.IsKeyPressed(Keys.Up) || kb.IsKeyPressed(Keys.NumPad8))
        { _selected = (_selected - 1 + MenuItems.Length) % MenuItems.Length; Render(); }
        else if (kb.IsKeyPressed(Keys.Down) || kb.IsKeyPressed(Keys.NumPad2))
        { _selected = (_selected + 1) % MenuItems.Length; Render(); }
        else if (kb.IsKeyPressed(Keys.Enter) || kb.IsKeyPressed(Keys.Space))
        {
            switch (_selected)
            {
                case 0: GameEngine.Instance.State = GameState.CharacterCreation; break;
                case 1: _page = Page.HowToPlay; Render();                        break;
                case 2: _page = Page.About;     Render();                        break;
                case 3: GameEngine.Instance.State = GameState.Settings;          break;
                case 4: Environment.Exit(0);                                     break;
            }
        }
    }

    public override bool ProcessKeyboard(Keyboard keyboard) => true;
}
