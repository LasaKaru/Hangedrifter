using SadConsole;
using SadConsole.Input;
using SadRogue.Primitives;
using RangedrifterClone.Core;
using RangedrifterClone.Components;
using RangedrifterClone.MapSystem;

namespace RangedrifterClone.UISystem;

/// <summary>
/// Main gameplay screen. Partitioned into three SadConsole surfaces
/// to match the Rangedrifter reference screenshots exactly:
///   • Map panel      — camera-scrolled view of the 200×200 dungeon
///   • Sidebar panel  — Status / HP / Inventory (right edge)
///   • Message panel  — rolling log (bottom of map area)
/// </summary>
public class GameScreen : ScreenObject
{
    // ── Layout constants (80×50 total) ──────────────────────────────────────
    private const int TotalW        = 80;
    private const int TotalH        = 50;
    private const int SidebarW      = 21;
    private const int MapW          = TotalW - SidebarW;   // 59
    private const int MsgH         = 13;
    private const int MapH          = TotalH - MsgH;       // 37

    private readonly ScreenSurface _mapPanel;
    private readonly ScreenSurface _sidebar;
    private readonly ScreenSurface _msgPanel;

    // Camera top-left (map coordinates)
    private int _camX, _camY;
    private bool _gameOverShown;

    // Colours used repeatedly in sidebar
    private static readonly Color LabelColor  = new(140, 140, 140);
    private static readonly Color ValueColor  = new(200, 200, 100);
    private static readonly Color DivColor    = new( 60,  60,  60);
    private static readonly Color HpLabelClr  = new(180,  80,  80);

    public GameScreen()
    {
        _mapPanel = new ScreenSurface(MapW,     MapH)     { Position = new Point(0,    0) };
        _sidebar  = new ScreenSurface(SidebarW, TotalH)   { Position = new Point(MapW, 0) };
        _msgPanel = new ScreenSurface(MapW,     MsgH)     { Position = new Point(0, MapH) };

        Children.Add(_mapPanel);
        Children.Add(_sidebar);
        Children.Add(_msgPanel);

        DrawBorders();

        // Subscribe to message log — event-aggregator pattern (no tight coupling)
        GameEngine.Instance.MessageLog.MessageAdded += (_, _) => RefreshMessages();
    }

    // ── Borders ─────────────────────────────────────────────────────────────
    private void DrawBorders()
    {
        var borderGlyph = new ColoredGlyph(new Color(60, 60, 60), Color.Black);

        _mapPanel.DrawBox(new Rectangle(0, 0, MapW,     MapH),
            ShapeParameters.CreateStyledBox(ICellSurface.ConnectedLineThin, borderGlyph));

        _sidebar.DrawBox(new Rectangle(0, 0, SidebarW, TotalH),
            ShapeParameters.CreateStyledBox(ICellSurface.ConnectedLineThin, borderGlyph));

        _msgPanel.DrawBox(new Rectangle(0, 0, MapW, MsgH),
            ShapeParameters.CreateStyledBox(ICellSurface.ConnectedLineThin, borderGlyph));
    }

    // ── Update (called every frame by SadConsole game loop) ─────────────────
    public override void Update(TimeSpan delta)
    {
        base.Update(delta);
        if (GameEngine.Instance.State == GameState.Playing ||
            GameEngine.Instance.State == GameState.GameOver)
        {
            UpdateCamera();
            RenderMap();
            RenderSidebar();
        }
    }

    // ── Camera ───────────────────────────────────────────────────────────────
    private void UpdateCamera()
    {
        var pos = GameEngine.Instance.EntityManager
            .GetComponent<PositionComponent>(GameEngine.Instance.PlayerEntity);
        if (pos == null) return;

        var map = GameEngine.Instance.CurrentMap;
        if (map == null) return;

        int innerW = MapW - 2;   // inside border
        int innerH = MapH - 2;

        _camX = Math.Clamp(pos.X - innerW / 2, 0, Math.Max(0, map.Width  - innerW));
        _camY = Math.Clamp(pos.Y - innerH / 2, 0, Math.Max(0, map.Height - innerH));
    }

    // ── Map rendering ────────────────────────────────────────────────────────
    private void RenderMap()
    {
        var map = GameEngine.Instance.CurrentMap;
        if (map == null) return;
        var em = GameEngine.Instance.EntityManager;

        // Clear interior
        for (int sy = 1; sy < MapH - 1; sy++)
        for (int sx = 1; sx < MapW - 1; sx++)
            _mapPanel.SetGlyph(sx, sy, ' ', Color.Black, Color.Black);

        // Tiles
        for (int sy = 1; sy < MapH - 1; sy++)
        for (int sx = 1; sx < MapW - 1; sx++)
        {
            int mx = _camX + sx - 1;
            int my = _camY + sy - 1;
            var tile = map.GetTile(mx, my);

            if (tile.IsVisible)
                _mapPanel.SetGlyph(sx, sy, tile.Glyph,
                    tile.ForegroundVisible, tile.Background);
            else if (tile.IsExplored)
                _mapPanel.SetGlyph(sx, sy, tile.Glyph,
                    tile.ForegroundExplored, tile.Background);
        }

        // Entities — lowest layer drawn first, highest on top
        var entities = em.GetEntitiesWith<RenderComponent, PositionComponent>()
            .Select(e => (e,
                Render: em.GetComponent<RenderComponent>(e)!,
                Pos:    em.GetComponent<PositionComponent>(e)!))
            .OrderBy(t => t.Render.RenderLayer)
            .ToList();

        foreach (var (_, render, pos) in entities)
        {
            var tile = map.GetTile(pos.X, pos.Y);
            if (!tile.IsVisible) continue;

            int sx = pos.X - _camX + 1;
            int sy = pos.Y - _camY + 1;
            if (sx >= 1 && sx < MapW - 1 && sy >= 1 && sy < MapH - 1)
                _mapPanel.SetGlyph(sx, sy, render.Glyph, render.Foreground, Color.Black);
        }

        // Terrain / feature labels (bottom-right corner of map panel, like screenshots)
        RenderTerrainLabels(map);
    }

    private void RenderTerrainLabels(GameMap map)
    {
        var pos = GameEngine.Instance.EntityManager
            .GetComponent<PositionComponent>(GameEngine.Instance.PlayerEntity);
        if (pos == null) return;

        var nearby = new List<string>();
        for (int dy = -3; dy <= 3 && nearby.Count < 2; dy++)
        for (int dx = -3; dx <= 3 && nearby.Count < 2; dx++)
        {
            var t = map.GetTile(pos.X + dx, pos.Y + dy);
            if ((t.Type == TileType.Rock || t.Type == TileType.Bush)
                && t.IsVisible && !nearby.Contains(t.Name))
                nearby.Add(t.Name);
        }

        int labelX = MapW - 9;
        int labelY = MapH - 2;
        foreach (var name in nearby)
            _mapPanel.Print(labelX, labelY--, name, new Color(160, 160, 100));
    }

    // ── Sidebar rendering ────────────────────────────────────────────────────
    private void RenderSidebar()
    {
        var em     = GameEngine.Instance.EntityManager;
        var player = GameEngine.Instance.PlayerEntity;

        // Clear interior
        for (int cy = 1; cy < TotalH - 1; cy++)
        for (int cx = 1; cx < SidebarW - 1; cx++)
            _sidebar.SetGlyph(cx, cy, ' ', Color.Black, Color.Black);

        var fighter = em.GetComponent<FighterComponent>(player);
        var inv     = em.GetComponent<InventoryComponent>(player);
        var status  = em.GetComponent<StatusComponent>(player);
        var exp     = em.GetComponent<ExperienceComponent>(player);

        int y = 1;
        var div = new string('─', SidebarW - 2);

        // ── Status block ──────────────────────────────────────────
        _sidebar.Print(1, y++, "Status", new Color(180, 180, 180));
        _sidebar.Print(1, y++, div, DivColor);

        if (fighter != null)
        {
            PrintStat("Damage",   fighter.DamageString, ref y);
            PrintStat("Armor",    fighter.Defense.ToString(), ref y);
            PrintStat("Strength", fighter.Strength.ToString(), ref y);
            PrintStat("Defense",  fighter.Defense.ToString(), ref y);
        }
        if (exp != null)
        {
            PrintStat("Vitality", "1", ref y);
            _sidebar.Print(1, y++, "Experience", LabelColor);
            _sidebar.Print(3, y++, $"{exp.Experience}/{exp.NextLevelExp}",
                new Color(100, 180, 100));
        }
        if (status != null)
            PrintStat("Turn", status.Turn.ToString(), ref y);

        y++;
        _sidebar.Print(1, y++, div, DivColor);

        // ── HP block ──────────────────────────────────────────────
        if (fighter != null)
        {
            var hpClr = fighter.Hp < fighter.MaxHp / 3 ? Color.Red
                      : fighter.Hp < fighter.MaxHp * 2 / 3 ? Color.Yellow
                      : Color.LightGreen;

            _sidebar.Print(1, y, "hp",
                HpLabelClr, Color.Black);
            _sidebar.Print(4, y++, $"{fighter.Hp}/{fighter.MaxHp}", hpClr, Color.Black);

            // HP bar
            int barW   = SidebarW - 3;
            int filled = (int)Math.Round((double)fighter.Hp / fighter.MaxHp * barW);
            for (int i = 0; i < barW; i++)
                _sidebar.SetGlyph(1 + i, y, '█',
                    i < filled ? hpClr : new Color(50, 20, 20), Color.Black);
            y += 2;
        }

        // Weapon quality (like "Dull edge" in screenshot)
        if (status != null)
            _sidebar.Print(1, y++, status.WeaponQuality, new Color(110, 110, 110));

        y++;
        _sidebar.Print(1, y++, div, DivColor);

        // ── Inventory / Bag block ─────────────────────────────────
        _sidebar.Print(1, y++, "Bag", new Color(180, 180, 180));

        if (inv != null)
        {
            for (int i = 0; i < Math.Min(inv.Items.Count, 8); i++)
            {
                var item    = inv.Items[i];
                bool equip  = i == inv.EquippedWeaponIndex || i == inv.EquippedArmorIndex;
                var  pre    = equip ? ">" : " ";
                var  itemFg = item.Category switch
                {
                    "Weapon"     => new Color(200, 200, 100),
                    "Food"       => new Color(200, 120,  80),
                    "Consumable" => new Color(160, 100, 200),
                    _            => new Color(180, 180, 180)
                };

                string label = $"{pre} {item.Glyph} {item.Name}";
                if (label.Length > SidebarW - 5) label = label[..(SidebarW - 5)];
                _sidebar.Print(1, y, label, itemFg, Color.Black);

                if (item.Count > 1)
                    _sidebar.Print(SidebarW - 5, y, $"x{item.Count}",
                        new Color(120, 120, 120), Color.Black);
                y++;
            }
        }

        // ── Status effects + level ────────────────────────────────
        if (status?.IsHungry == true)
            _sidebar.Print(1, TotalH - 5, "Hungry", new Color(200, 150, 50));
        if (exp != null)
            _sidebar.Print(1, TotalH - 4, $"lv {exp.Level}", LabelColor);

        // ── Controls hint ─────────────────────────────────────────
        _sidebar.Print(1, TotalH - 3, "g=pickup .=wait", DivColor);
        _sidebar.Print(1, TotalH - 2, "numpad/arrows=move", DivColor);
    }

    private void PrintStat(string label, string value, ref int y)
    {
        _sidebar.Print(1,  y, label, LabelColor, Color.Black);
        _sidebar.Print(11, y, value, ValueColor, Color.Black);
        y++;
    }

    // ── Message log ──────────────────────────────────────────────────────────
    private void RefreshMessages()
    {
        var msgs    = GameEngine.Instance.MessageLog.Messages;
        int visible = MsgH - 2;

        for (int y = 1; y < MsgH - 1; y++)
        for (int x = 1; x < MapW  - 1; x++)
            _msgPanel.SetGlyph(x, y, ' ', Color.Black, Color.Black);

        var toShow = msgs.Skip(Math.Max(0, msgs.Count - visible)).ToList();
        for (int i = 0; i < toShow.Count; i++)
        {
            string text = toShow[i].Text;
            if (text.Length > MapW - 3) text = text[..(MapW - 3)];
            _msgPanel.Print(1, 1 + i, text, toShow[i].Color, Color.Black);
        }
    }

    // ── Game Over overlay ────────────────────────────────────────────────────
    public void ShowGameOver()
    {
        if (_gameOverShown) return;
        _gameOverShown = true;

        int cx = MapW / 2 - 7;
        int cy = MapH / 2;
        _mapPanel.Print(cx,     cy,     "╔═══════════════╗", Color.Red,    Color.Black);
        _mapPanel.Print(cx,     cy + 1, "║  YOU HAVE DIED ║", Color.Red,    Color.Black);
        _mapPanel.Print(cx,     cy + 2, "╚═══════════════╝", Color.Red,    Color.Black);
        _mapPanel.Print(cx - 1, cy + 4, "Press R to restart", Color.Yellow, Color.Black);
    }

    // ── Keyboard ─────────────────────────────────────────────────────────────
    public override bool ProcessKeyboard(Keyboard keyboard)
    {
        if (_gameOverShown)
        {
            if (keyboard.IsKeyPressed(Keys.R))
            {
                _gameOverShown = false;
                DrawBorders();
                GameEngine.Instance.StartNewGame();
            }
            return true;
        }

        // 8-directional movement (numpad + arrows)
        if (keyboard.IsKeyPressed(Keys.NumPad8) || keyboard.IsKeyPressed(Keys.Up))
            { GameEngine.Instance.ProcessPlayerTurn(0, -1); return true; }
        if (keyboard.IsKeyPressed(Keys.NumPad2) || keyboard.IsKeyPressed(Keys.Down))
            { GameEngine.Instance.ProcessPlayerTurn(0,  1); return true; }
        if (keyboard.IsKeyPressed(Keys.NumPad4) || keyboard.IsKeyPressed(Keys.Left))
            { GameEngine.Instance.ProcessPlayerTurn(-1, 0); return true; }
        if (keyboard.IsKeyPressed(Keys.NumPad6) || keyboard.IsKeyPressed(Keys.Right))
            { GameEngine.Instance.ProcessPlayerTurn( 1, 0); return true; }
        if (keyboard.IsKeyPressed(Keys.NumPad7))
            { GameEngine.Instance.ProcessPlayerTurn(-1, -1); return true; }
        if (keyboard.IsKeyPressed(Keys.NumPad9))
            { GameEngine.Instance.ProcessPlayerTurn( 1, -1); return true; }
        if (keyboard.IsKeyPressed(Keys.NumPad1))
            { GameEngine.Instance.ProcessPlayerTurn(-1,  1); return true; }
        if (keyboard.IsKeyPressed(Keys.NumPad3))
            { GameEngine.Instance.ProcessPlayerTurn( 1,  1); return true; }

        // Actions
        if (keyboard.IsKeyPressed(Keys.G) || keyboard.IsKeyPressed(Keys.OemComma))
            { GameEngine.Instance.ProcessAction(PlayerAction.PickUp); return true; }
        if (keyboard.IsKeyPressed(Keys.OemPeriod) || keyboard.IsKeyPressed(Keys.NumPad5))
            { GameEngine.Instance.ProcessAction(PlayerAction.Wait); return true; }

        return base.ProcessKeyboard(keyboard);
    }
}
