using SadConsole;
using SadConsole.Input;
using SadRogue.Primitives;
using RangedrifterClone.Core;
using RangedrifterClone.Components;
using RangedrifterClone.MapSystem;

namespace RangedrifterClone.UISystem;

/// <summary>
/// Main gameplay screen — three SadConsole panels matching Rangedrifter layout:
///   Map panel (59×37)  — camera-scrolled 200×200 dungeon with FOV
///   Sidebar  (21×50)   — class, status, HP, mana, equipment, inventory
///   Msg log  (59×13)   — rolling combat/event history
///
/// Ability bar: keys 1-4 fire abilities; ability name + cooldown shown in sidebar.
/// Equipment: key E opens equip prompt; inventory items with stats shown in colour.
/// </summary>
public class GameScreen : ScreenObject
{
    private const int TotalW   = 80;
    private const int TotalH   = 50;
    private const int SidebarW = 21;
    private const int MapW     = TotalW - SidebarW;  // 59
    private const int MsgH     = 13;
    private const int MapH     = TotalH - MsgH;      // 37

    private readonly ScreenSurface _mapPanel;
    private readonly ScreenSurface _sidebar;
    private readonly ScreenSurface _msgPanel;

    private int    _camX, _camY;
    private bool   _gameOverShown;
    private bool   _equipMode;
    private int    _abilityDirState  = 0;   // 0 = none, 1-4 = waiting for direction
    private double _glowTime         = 0;   // accumulated seconds — drives the glow pulse
    private bool   _firstPersonMode  = false;
    private double _fpAngle          = 0.0; // radians: 0=East(+X), π/2=South(+Y)

    private static readonly Color LabelClr  = new(140, 140, 140);
    private static readonly Color ValueClr  = new(200, 200, 100);
    private static readonly Color DivClr    = new( 60,  60,  60);
    private static readonly Color HpLblClr  = new(180,  80,  80);
    private static readonly Color ManaClr   = new( 80, 140, 220);

    public GameScreen()
    {
        _mapPanel = new ScreenSurface(MapW,     MapH)   { Position = new Point(0,    0) };
        _sidebar  = new ScreenSurface(SidebarW, TotalH) { Position = new Point(MapW, 0) };
        _msgPanel = new ScreenSurface(MapW,     MsgH)   { Position = new Point(0, MapH) };
        Children.Add(_mapPanel);
        Children.Add(_sidebar);
        Children.Add(_msgPanel);
        DrawBorders();
        GameEngine.Instance.MessageLog.MessageAdded += (_, _) => RefreshMessages();
    }

    // ── Borders ──────────────────────────────────────────────────────────
    private void DrawBorders()
    {
        var b = new ColoredGlyph(DivClr, Color.Black);
        _mapPanel.DrawBox(new Rectangle(0, 0, MapW,     MapH),   ShapeParameters.CreateStyledBox(ICellSurface.ConnectedLineThin, b));
        _sidebar .DrawBox(new Rectangle(0, 0, SidebarW, TotalH), ShapeParameters.CreateStyledBox(ICellSurface.ConnectedLineThin, b));
        _msgPanel.DrawBox(new Rectangle(0, 0, MapW,     MsgH),   ShapeParameters.CreateStyledBox(ICellSurface.ConnectedLineThin, b));
    }

    public void ResetGameOver() { _gameOverShown = false; DrawBorders(); }

    // ── Update (every frame) ──────────────────────────────────────────────
    public override void Update(TimeSpan delta)
    {
        base.Update(delta);
        _glowTime += delta.TotalSeconds;   // always tick so glow animates smoothly
        var st = GameEngine.Instance.State;
        if (st == GameState.Playing || st == GameState.GameOver)
        {
            UpdateCamera();
            RenderMap();
            RenderSidebar();
        }
    }

    // ── Camera ────────────────────────────────────────────────────────────
    private void UpdateCamera()
    {
        var pos = GameEngine.Instance.EntityManager
            .GetComponent<PositionComponent>(GameEngine.Instance.PlayerEntity);
        var map = GameEngine.Instance.CurrentMap;
        if (pos == null || map == null) return;
        _camX = Math.Clamp(pos.X - (MapW - 2) / 2, 0, Math.Max(0, map.Width  - MapW + 2));
        _camY = Math.Clamp(pos.Y - (MapH - 2) / 2, 0, Math.Max(0, map.Height - MapH + 2));
    }

    // ── Map panel ─────────────────────────────────────────────────────────
    private void RenderMap()
    {
        var map = GameEngine.Instance.CurrentMap;
        if (map == null) return;

        // ── First-person ray-cast mode takes over the whole map panel ──
        if (_firstPersonMode) { RenderFirstPerson(map); return; }

        var em  = GameEngine.Instance.EntityManager;

        // Clear
        for (int sy = 1; sy < MapH - 1; sy++)
        for (int sx = 1; sx < MapW - 1; sx++)
            _mapPanel.SetGlyph(sx, sy, ' ', Color.Black, Color.Black);

        // ── Tiles ─────────────────────────────────────────────────────
        // Unexplored : pure black (automatic — cleared to black above)
        // Explored   : tile glyph at ~20% brightness, black background
        // Visible    : full colour; walls get the lit top-face background
        //              (half-block ▄ trick → two-colour 3-D block illusion)
        for (int sy = 1; sy < MapH - 1; sy++)
        for (int sx = 1; sx < MapW - 1; sx++)
        {
            var tile = map.GetTile(_camX + sx - 1, _camY + sy - 1);
            if (tile.IsVisible)
            {
                // Walls: bg = lit top-face; floors: subtle tint bg
                var bg = tile.Type is TileType.Wall
                       ? tile.Background           // lighter top-face for 3-D effect
                       : tile.Background;          // slight tint already set in Tile
                _mapPanel.SetGlyph(sx, sy, tile.Glyph, tile.ForegroundVisible, bg);
            }
            else if (tile.IsExplored)
            {
                // Dim ghost — no 3-D top-face, just the silhouette
                _mapPanel.SetGlyph(sx, sy, tile.Glyph, tile.ForegroundExplored, Color.Black);
            }
            // else: unexplored stays black (cleared at start of RenderMap)
        }

        // Entities (sorted by layer — lowest drawn first)
        foreach (var (_, render, pos) in em
            .GetEntitiesWith<RenderComponent, PositionComponent>()
            .Select(e => (e, em.GetComponent<RenderComponent>(e)!, em.GetComponent<PositionComponent>(e)!))
            .OrderBy(t => t.Item2.RenderLayer))
        {
            if (!render.IsVisible) continue;
            var tile = map.GetTile(pos.X, pos.Y);

            // Traps: only show if revealed
            bool isTrap = em.GetComponent<FeatureComponent>(
                em.GetEntitiesWith<FeatureComponent, PositionComponent>()
                  .FirstOrDefault(e => { var p = em.GetComponent<PositionComponent>(e)!; return p.X == pos.X && p.Y == pos.Y; }))
                ?.Type == FeatureType.Trap;

            if (!tile.IsVisible) continue;

            int sx = pos.X - _camX + 1, sy = pos.Y - _camY + 1;
            if (sx >= 1 && sx < MapW - 1 && sy >= 1 && sy < MapH - 1)
            {
                // Use the entity's own Background (robot glow halo, or Transparent → black)
                var entBg = render.Background == Color.Transparent ? Color.Black : render.Background;
                _mapPanel.SetGlyph(sx, sy, render.Glyph, render.Foreground, entBg);
            }
        }

        // Glow aura (applied after tiles + entities so it tints backgrounds)
        ApplyPlayerGlow(map);

        // Floor / terrain labels (bottom-right corner)
        RenderTerrainLabels(map);

        // Floor number
        string floorLabel = $"Floor {GameEngine.Instance.CurrentFloor}";
        _mapPanel.Print(MapW - floorLabel.Length - 1, 1, floorLabel, new Color(100, 100, 160));
    }

    // ── Player glow aura ──────────────────────────────────────────────────
    /// <summary>
    /// Simulates a bloom/glow effect in text mode.  Each frame:
    ///   • Adjacent cells (radius 0-3) receive a tinted background that
    ///     fades to black as distance increases.
    ///   • The player cell itself pulses brighter at ~2 Hz via a sin wave.
    /// All colours are derived from the player's own class colour so the
    /// glow matches: amber for Warrior, violet for Rogue, blue for Mage.
    /// </summary>
    private void ApplyPlayerGlow(GameMap map)
    {
        var em     = GameEngine.Instance.EntityManager;
        var player = GameEngine.Instance.PlayerEntity;
        var pos    = em.GetComponent<PositionComponent>(player);
        var rend   = em.GetComponent<RenderComponent>(player);
        if (pos == null || rend == null) return;

        // Pulse: smoothly oscillates 0.55 → 1.0 at ~2 Hz
        float pulse = (float)(0.55 + 0.45 * Math.Sin(_glowTime * Math.PI * 2.0));

        var c = rend.Foreground;   // class colour is the glow source

        // ── Halo rings ───────────────────────────────────────────────
        const int Radius = 3;
        for (int dy = -Radius; dy <= Radius; dy++)
        for (int dx = -Radius; dx <= Radius; dx++)
        {
            if (dx == 0 && dy == 0) continue;

            float dist = MathF.Sqrt(dx * dx + dy * dy);
            if (dist > Radius + 0.5f) continue;

            int wx = pos.X + dx, wy = pos.Y + dy;
            if (!map.GetTile(wx, wy).IsVisible) continue;

            int sx = wx - _camX + 1, swy = wy - _camY + 1;
            if (sx < 1 || sx >= MapW - 1 || swy < 1 || swy >= MapH - 1) continue;

            // Intensity: strong nearby, falls off quadratically, modulated by pulse
            float intensity = (1f - dist / (Radius + 1f));
            intensity = intensity * intensity * 0.45f * pulse;

            _mapPanel.SetBackground(sx, swy, new Color(
                (byte)(c.R * intensity),
                (byte)(c.G * intensity),
                (byte)(c.B * intensity)));
        }

        // ── Player cell: pulsing bright glyph ────────────────────────
        int psx = pos.X - _camX + 1, psy = pos.Y - _camY + 1;
        if (psx >= 1 && psx < MapW - 1 && psy >= 1 && psy < MapH - 1)
        {
            float bright = 0.80f + 0.20f * pulse;
            var fg = new Color(
                (byte)Math.Min(255, (int)(c.R * bright) + (int)(50 * pulse)),
                (byte)Math.Min(255, (int)(c.G * bright) + (int)(35 * pulse)),
                (byte)Math.Min(255, (int)(c.B * bright) + (int)(20 * pulse)));
            var bg = new Color(
                (byte)(c.R * 0.28f * pulse),
                (byte)(c.G * 0.22f * pulse),
                (byte)(c.B * 0.18f * pulse));
            _mapPanel.SetGlyph(psx, psy, rend.Glyph, fg, bg);
        }
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
            if (t.IsVisible && t.Type is TileType.Rock or TileType.Bush
                or TileType.StairsDown or TileType.StairsUp or TileType.Chest or TileType.Trap
                or TileType.Door or TileType.Water)
                if (!nearby.Contains(t.Name)) nearby.Add(t.Name);
        }
        int ly = MapH - 2;
        foreach (var name in nearby)
            _mapPanel.Print(MapW - name.Length - 2, ly--, name, new Color(160, 160, 100));
    }

    // ── Sidebar ───────────────────────────────────────────────────────────
    private void RenderSidebar()
    {
        var em     = GameEngine.Instance.EntityManager;
        var player = GameEngine.Instance.PlayerEntity;

        for (int cy = 1; cy < TotalH - 1; cy++)
        for (int cx = 1; cx < SidebarW - 1; cx++)
            _sidebar.SetGlyph(cx, cy, ' ', Color.Black, Color.Black);

        var fighter = em.GetComponent<FighterComponent>(player);
        var inv     = em.GetComponent<InventoryComponent>(player);
        var mana    = em.GetComponent<ManaComponent>(player);
        var status  = em.GetComponent<StatusComponent>(player);
        var exp     = em.GetComponent<ExperienceComponent>(player);
        var cls     = em.GetComponent<ClassComponent>(player);
        var equip   = em.GetComponent<EquipmentSlotComponent>(player);
        var abils   = em.GetComponent<AbilityComponent>(player);
        var fxComp  = em.GetComponent<StatusEffectComponent>(player);

        int y = 1;
        string div = new string('─', SidebarW - 2);

        // Class header
        if (cls != null)
        {
            var clsClr = cls.Class switch
            {
                PlayerClass.Warrior => new Color(220, 160, 60),
                PlayerClass.Rogue   => new Color(180, 100, 180),
                PlayerClass.Mage    => new Color(80, 160, 220),
                _                   => new Color(180, 180, 180)
            };
            _sidebar.Print(1, y++, cls.ClassName, clsClr, Color.Black);
        }
        _sidebar.Print(1, y++, div, DivClr);

        // ── Stats block ──────────────────────────────────────────────
        _sidebar.Print(1, y++, "Status", new Color(180, 180, 180));

        if (fighter != null)
        {
            Stat("Damage",   fighter.DamageString,            ref y);
            Stat("Armor",    fighter.EffectiveDefense.ToString(), ref y);
            Stat("Strength", fighter.Strength.ToString(),     ref y);
        }
        if (exp != null)
        {
            Stat("Level",    exp.Level.ToString(),            ref y);
            Stat("XP",       $"{exp.Experience}/{exp.NextLevelExp}", ref y);
        }
        if (status != null)
            Stat("Turn",     status.Turn.ToString(),          ref y);

        y++;
        _sidebar.Print(1, y++, div, DivClr);

        // ── HP + Mana bars ───────────────────────────────────────────
        if (fighter != null)
        {
            var hpClr = fighter.Hp < fighter.MaxHp / 3 ? Color.Red
                      : fighter.Hp < fighter.MaxHp * 2 / 3 ? Color.Yellow
                      : Color.LightGreen;
            _sidebar.Print(1, y, "hp", HpLblClr);
            _sidebar.Print(4, y++, $"{fighter.Hp}/{fighter.MaxHpTotal}", hpClr);
            int bw = SidebarW - 3;
            int filled = (int)Math.Round((double)Math.Max(0, fighter.Hp) / Math.Max(1, fighter.MaxHpTotal) * bw);
            for (int i = 0; i < bw; i++)
                _sidebar.SetGlyph(1 + i, y, '█', i < filled ? hpClr : new Color(50, 20, 20));
            y += 2;
        }
        if (mana != null)
        {
            _sidebar.Print(1, y, "mp", ManaClr);
            _sidebar.Print(4, y++, $"{mana.Mana}/{mana.MaxMana}", ManaClr);
            int bw = SidebarW - 3;
            int filled = (int)Math.Round((double)mana.Mana / Math.Max(1, mana.MaxMana) * bw);
            for (int i = 0; i < bw; i++)
                _sidebar.SetGlyph(1 + i, y, '█', i < filled ? ManaClr : new Color(20, 20, 50));
            y += 2;
        }

        // ── Status effects ───────────────────────────────────────────
        if (fxComp != null && fxComp.Effects.Count > 0)
        {
            foreach (var fx in fxComp.Effects.Take(3))
            {
                var fxClr = fx.Type switch
                {
                    EffectType.Poisoned or EffectType.Poisoning => new Color(100, 200, 80),
                    EffectType.Burning   => new Color(255, 140, 0),
                    EffectType.Frozen    => new Color(100, 200, 240),
                    EffectType.Stunned   => new Color(180, 180, 180),
                    EffectType.Blessed   => new Color(255, 220, 80),
                    EffectType.Cursed    => new Color(160, 80, 200),
                    EffectType.Regenerating => Color.LightGreen,
                    _                    => new Color(160, 160, 160)
                };
                _sidebar.Print(1, y++, $"~ {fx.Type} ({fx.Duration}t)", fxClr);
            }
            y++;
        }

        // ── Equipment ────────────────────────────────────────────────
        _sidebar.Print(1, y++, div, DivClr);
        _sidebar.Print(1, y++, "Equipment", new Color(180, 180, 100));
        if (equip != null)
        {
            PrintEquipSlot("W:", equip.Weapon,  ref y);
            PrintEquipSlot("A:", equip.Armor,   ref y);
            PrintEquipSlot("S:", equip.Shield,  ref y);
            PrintEquipSlot("R:", equip.Ring,    ref y);
        }

        // ── Abilities ────────────────────────────────────────────────
        _sidebar.Print(1, y++, div, DivClr);
        _sidebar.Print(1, y++, "Abilities", new Color(180, 100, 180));
        if (abils != null)
        {
            for (int i = 0; i < abils.Abilities.Count && y < TotalH - 6; i++)
            {
                var ab = abils.Abilities[i];
                bool rdy = ab.IsReady;
                var abClr = rdy ? ab.Color : new Color(80, 80, 80);
                string cdStr = rdy ? "  " : $"{ab.CurrentCooldown}t";
                string prefix = $"[{i + 1}]";
                string name = ab.Name.Length > 9 ? ab.Name[..9] : ab.Name;
                _sidebar.Print(1, y, prefix, new Color(120, 120, 120));
                _sidebar.Print(4, y, name, abClr);
                _sidebar.Print(SidebarW - 4, y++, cdStr, rdy ? DivClr : Color.Red);
            }
        }

        // ── Inventory ────────────────────────────────────────────────
        _sidebar.Print(1, y++, div, DivClr);
        _sidebar.Print(1, y++, "Bag", new Color(180, 180, 180));
        if (inv != null)
        {
            for (int i = 0; i < Math.Min(inv.Items.Count, TotalH - y - 2); i++)
            {
                var item   = inv.Items[i];
                var itemFg = item.Category switch
                {
                    "Weapon"     => new Color(200, 200, 100),
                    "Food"       => new Color(200, 120,  80),
                    "Consumable" => new Color(160, 100, 200),
                    "Armor" or "Shield" or "Ring" or "Amulet" => new Color(100, 180, 200),
                    _            => new Color(180, 180, 180)
                };
                string label = $"{item.Glyph} {item.Name}";
                if (label.Length > SidebarW - 5) label = label[..(SidebarW - 5)];
                _sidebar.Print(1, y, label, itemFg);
                if (item.Count > 1)
                    _sidebar.Print(SidebarW - 4, y, $"x{item.Count}", DivClr);
                y++;
            }
        }

        // Footer: mode hints
        string hint = _equipMode          ? "Pick # to equip/unequip"
                    : _abilityDirState > 0 ? $"Dir for ability {_abilityDirState}"
                    : _firstPersonMode     ? "Arrows=move  TAB=2D map"
                    : "E=equip .=wait g=pick";
        _sidebar.Print(1, TotalH - 2, hint[..Math.Min(hint.Length, SidebarW - 2)], DivClr);
    }

    private void Stat(string label, string value, ref int y)
    {
        _sidebar.Print(1,  y, label, LabelClr);
        _sidebar.Print(11, y, value, ValueClr);
        y++;
    }

    private void PrintEquipSlot(string prefix, EquipmentEntry? entry, ref int y)
    {
        _sidebar.Print(1, y, prefix, DivClr);
        if (entry != null)
        {
            string name = entry.Name.Length > SidebarW - 5 ? entry.Name[..(SidebarW - 5)] : entry.Name;
            _sidebar.Print(3, y, name, entry.Color);
        }
        else
            _sidebar.Print(3, y, "—", DivClr);
        y++;
    }

    // ── Messages ─────────────────────────────────────────────────────────
    private void RefreshMessages()
    {
        var msgs    = GameEngine.Instance.MessageLog.Messages;
        int visible = MsgH - 2;

        for (int my = 1; my < MsgH - 1; my++)
        for (int mx = 1; mx < MapW  - 1; mx++)
            _msgPanel.SetGlyph(mx, my, ' ', Color.Black, Color.Black);

        var toShow = msgs.Skip(Math.Max(0, msgs.Count - visible)).ToList();
        for (int i = 0; i < toShow.Count; i++)
        {
            string text = toShow[i].Text;
            if (text.Length > MapW - 3) text = text[..(MapW - 3)];
            _msgPanel.Print(1, 1 + i, text, toShow[i].Color);
        }
    }

    // ── Game Over overlay ─────────────────────────────────────────────────
    public void ShowGameOver()
    {
        if (_gameOverShown) return;
        _gameOverShown = true;
        int cx = MapW / 2 - 9, cy = MapH / 2;
        _mapPanel.Print(cx,     cy,     "╔══════════════════╗", Color.Red);
        _mapPanel.Print(cx,     cy + 1, "║   YOU HAVE DIED  ║", Color.Red);
        _mapPanel.Print(cx,     cy + 2, "╚══════════════════╝", Color.Red);
        _mapPanel.Print(cx - 1, cy + 4, "Press R to restart", Color.Yellow);
    }

    // ── Keyboard ─────────────────────────────────────────────────────────
    public override bool ProcessKeyboard(Keyboard keyboard)
    {
        if (_gameOverShown)
        {
            if (keyboard.IsKeyPressed(Keys.R))
            {
                _gameOverShown  = false;
                _equipMode      = false;
                _abilityDirState = 0;
                DrawBorders();
                GameEngine.Instance.State = GameState.CharacterCreation;
            }
            return true;
        }

        // ── Equip mode ────────────────────────────────────────────────
        if (_equipMode)
        {
            for (int i = 0; i <= 9; i++)
            {
                if (keyboard.IsKeyPressed((Keys)(Keys.D0 + i)))
                {
                    GameEngine.Instance.TryEquipItem(i - 1);
                    _equipMode = false;
                    return true;
                }
            }
            if (keyboard.IsKeyPressed(Keys.Escape) || keyboard.IsKeyPressed(Keys.E))
                _equipMode = false;
            return true;
        }

        // ── Waiting for ability direction ─────────────────────────────
        if (_abilityDirState > 0)
        {
            int idx = _abilityDirState - 1;
            _abilityDirState = 0;
            if      (keyboard.IsKeyPressed(Keys.NumPad8) || keyboard.IsKeyPressed(Keys.Up))    GameEngine.Instance.UseAbility(idx,  0, -1);
            else if (keyboard.IsKeyPressed(Keys.NumPad2) || keyboard.IsKeyPressed(Keys.Down))  GameEngine.Instance.UseAbility(idx,  0,  1);
            else if (keyboard.IsKeyPressed(Keys.NumPad4) || keyboard.IsKeyPressed(Keys.Left))  GameEngine.Instance.UseAbility(idx, -1,  0);
            else if (keyboard.IsKeyPressed(Keys.NumPad6) || keyboard.IsKeyPressed(Keys.Right)) GameEngine.Instance.UseAbility(idx,  1,  0);
            else if (keyboard.IsKeyPressed(Keys.NumPad7)) GameEngine.Instance.UseAbility(idx, -1, -1);
            else if (keyboard.IsKeyPressed(Keys.NumPad9)) GameEngine.Instance.UseAbility(idx,  1, -1);
            else if (keyboard.IsKeyPressed(Keys.NumPad1)) GameEngine.Instance.UseAbility(idx, -1,  1);
            else if (keyboard.IsKeyPressed(Keys.NumPad3)) GameEngine.Instance.UseAbility(idx,  1,  1);
            else GameEngine.Instance.UseAbility(idx); // AoE/Heal: no direction needed
            return true;
        }

        // ── First-person: Tab toggle + turn/move ─────────────────────
        if (keyboard.IsKeyPressed(Keys.Tab)) { _firstPersonMode = !_firstPersonMode; return true; }

        if (_firstPersonMode)
        {
            // Arrow keys / numpad turn camera; Up/Down strafe forward/backward
            if (keyboard.IsKeyPressed(Keys.Left)  || keyboard.IsKeyPressed(Keys.NumPad4))
            { _fpAngle -= 0.15; return true; }
            if (keyboard.IsKeyPressed(Keys.Right) || keyboard.IsKeyPressed(Keys.NumPad6))
            { _fpAngle += 0.15; return true; }
            if (keyboard.IsKeyPressed(Keys.Up)    || keyboard.IsKeyPressed(Keys.NumPad8))
            { var (dx, dy) = FpAngleToDir(_fpAngle);  GameEngine.Instance.ProcessPlayerTurn( dx,  dy); return true; }
            if (keyboard.IsKeyPressed(Keys.Down)  || keyboard.IsKeyPressed(Keys.NumPad2))
            { var (dx, dy) = FpAngleToDir(_fpAngle);  GameEngine.Instance.ProcessPlayerTurn(-dx, -dy); return true; }
            // Diagonal strafe
            if (keyboard.IsKeyPressed(Keys.NumPad7))
            { var (dx, dy) = FpAngleToDir(_fpAngle - Math.PI * 0.5); GameEngine.Instance.ProcessPlayerTurn(dx, dy); return true; }
            if (keyboard.IsKeyPressed(Keys.NumPad9))
            { var (dx, dy) = FpAngleToDir(_fpAngle + Math.PI * 0.5); GameEngine.Instance.ProcessPlayerTurn(dx, dy); return true; }
        }

        // ── Normal movement (top-down mode) ───────────────────────────
        if (keyboard.IsKeyPressed(Keys.NumPad8) || keyboard.IsKeyPressed(Keys.Up))    { GameEngine.Instance.ProcessPlayerTurn( 0, -1); return true; }
        if (keyboard.IsKeyPressed(Keys.NumPad2) || keyboard.IsKeyPressed(Keys.Down))  { GameEngine.Instance.ProcessPlayerTurn( 0,  1); return true; }
        if (keyboard.IsKeyPressed(Keys.NumPad4) || keyboard.IsKeyPressed(Keys.Left))  { GameEngine.Instance.ProcessPlayerTurn(-1,  0); return true; }
        if (keyboard.IsKeyPressed(Keys.NumPad6) || keyboard.IsKeyPressed(Keys.Right)) { GameEngine.Instance.ProcessPlayerTurn( 1,  0); return true; }
        if (keyboard.IsKeyPressed(Keys.NumPad7))                                      { GameEngine.Instance.ProcessPlayerTurn(-1, -1); return true; }
        if (keyboard.IsKeyPressed(Keys.NumPad9))                                      { GameEngine.Instance.ProcessPlayerTurn( 1, -1); return true; }
        if (keyboard.IsKeyPressed(Keys.NumPad1))                                      { GameEngine.Instance.ProcessPlayerTurn(-1,  1); return true; }
        if (keyboard.IsKeyPressed(Keys.NumPad3))                                      { GameEngine.Instance.ProcessPlayerTurn( 1,  1); return true; }

        // ── Abilities 1-4 ─────────────────────────────────────────────
        if (keyboard.IsKeyPressed(Keys.D1)) { QueueAbility(1); return true; }
        if (keyboard.IsKeyPressed(Keys.D2)) { QueueAbility(2); return true; }
        if (keyboard.IsKeyPressed(Keys.D3)) { QueueAbility(3); return true; }
        if (keyboard.IsKeyPressed(Keys.D4)) { QueueAbility(4); return true; }

        // ── Other actions ─────────────────────────────────────────────
        if (keyboard.IsKeyPressed(Keys.G) || keyboard.IsKeyPressed(Keys.OemComma))
        { GameEngine.Instance.ProcessAction(PlayerAction.PickUp);  return true; }
        if (keyboard.IsKeyPressed(Keys.OemPeriod) || keyboard.IsKeyPressed(Keys.NumPad5))
        { GameEngine.Instance.ProcessAction(PlayerAction.Wait);    return true; }
        if (keyboard.IsKeyPressed(Keys.U))
        { GameEngine.Instance.ProcessAction(PlayerAction.UseItem); return true; }
        if (keyboard.IsKeyPressed(Keys.E))
        { _equipMode = true; return true; }

        return base.ProcessKeyboard(keyboard);
    }

    private void QueueAbility(int number)
    {
        var em   = GameEngine.Instance.EntityManager;
        var abil = em.GetComponent<AbilityComponent>(GameEngine.Instance.PlayerEntity);
        if (abil == null || number > abil.Abilities.Count) return;

        var ab = abil.Abilities[number - 1];
        // AoE and Heal don't need a direction; everything else does
        if (ab.Type is AbilityType.AoEDamage or AbilityType.Heal or AbilityType.Buff)
            GameEngine.Instance.UseAbility(number - 1);
        else
        {
            _abilityDirState = number;
            GameEngine.Instance.MessageLog.Add(
                $"{ab.Name}: choose a direction (numpad/arrows).", ab.Color);
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // First-person Wolfenstein-style ray-cast renderer
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Snaps a continuous angle (radians) to the nearest of 8 grid directions
    /// and returns the corresponding (dx, dy) step for use with ProcessPlayerTurn.
    /// Coordinate convention: angle=0 → East (+X), angle=π/2 → South (+Y).
    /// </summary>
    private static (int dx, int dy) FpAngleToDir(double angle)
    {
        angle = ((angle % (Math.PI * 2)) + Math.PI * 2) % (Math.PI * 2);
        int s = (int)Math.Round(angle / (Math.PI / 4)) % 8;
        return s switch {
            0 => ( 1,  0), // E
            1 => ( 1,  1), // SE
            2 => ( 0,  1), // S
            3 => (-1,  1), // SW
            4 => (-1,  0), // W
            5 => (-1, -1), // NW
            6 => ( 0, -1), // N
            7 => ( 1, -1), // NE
            _ => ( 1,  0),
        };
    }

    private static string FpFacingLabel(double angle)
    {
        angle = ((angle % (Math.PI * 2)) + Math.PI * 2) % (Math.PI * 2);
        int s = (int)Math.Round(angle / (Math.PI / 4)) % 8;
        return s switch {
            0 => "East",  1 => "SE",    2 => "South", 3 => "SW",
            4 => "West",  5 => "NW",    6 => "North", 7 => "NE",
            _ => "East",
        };
    }

    /// <summary>
    /// DDA (Digital Differential Analysis) ray-cast loop.
    /// Casts one ray per screen column and returns the perpendicular wall distance,
    /// which column face was hit (X-side vs Y-side), and the map cell that was hit.
    /// </summary>
    private static (double dist, bool ySide, int hitX, int hitY) CastRay(
        GameMap map, double posX, double posY,
        double rayDirX, double rayDirY)
    {
        int mapX = (int)posX, mapY = (int)posY;

        double deltaDistX = Math.Abs(rayDirX) < 1e-10 ? 1e30 : Math.Abs(1.0 / rayDirX);
        double deltaDistY = Math.Abs(rayDirY) < 1e-10 ? 1e30 : Math.Abs(1.0 / rayDirY);

        int stepX, stepY;
        double sideDistX, sideDistY;

        if (rayDirX < 0) { stepX = -1; sideDistX = (posX - mapX) * deltaDistX; }
        else             { stepX =  1; sideDistX = (mapX + 1.0 - posX) * deltaDistX; }
        if (rayDirY < 0) { stepY = -1; sideDistY = (posY - mapY) * deltaDistY; }
        else             { stepY =  1; sideDistY = (mapY + 1.0 - posY) * deltaDistY; }

        bool hit = false, ySide = false;
        int  steps = 80;
        while (!hit && steps-- > 0)
        {
            if (sideDistX < sideDistY)
            { sideDistX += deltaDistX; mapX += stepX; ySide = false; }
            else
            { sideDistY += deltaDistY; mapY += stepY; ySide = true;  }

            var t = map.GetTile(mapX, mapY);
            if (!t.IsWalkable || t.Type == TileType.Empty) hit = true;
        }

        if (!hit) return (1e6, false, mapX, mapY);

        double dist = ySide
            ? (mapY - posY + (1 - stepY) * 0.5) / rayDirY
            : (mapX - posX + (1 - stepX) * 0.5) / rayDirX;

        return (Math.Max(0.15, dist), ySide, mapX, mapY);
    }

    /// <summary>
    /// Full first-person render: ceiling gradient, DDA wall slices, floor gradient,
    /// crosshair, HUD bar, and an explored mini-map overlay.
    /// </summary>
    private void RenderFirstPerson(GameMap map)
    {
        var em     = GameEngine.Instance.EntityManager;
        var player = GameEngine.Instance.PlayerEntity;
        var pos    = em.GetComponent<PositionComponent>(player);
        if (pos == null) return;

        // ── Clear ──────────────────────────────────────────────────────────
        for (int sy = 1; sy < MapH - 1; sy++)
        for (int sx = 1; sx < MapW - 1; sx++)
            _mapPanel.SetGlyph(sx, sy, ' ', Color.Black, Color.Black);

        int viewW = MapW - 2;   // 57 usable columns
        int viewH = MapH - 2;   // 35 usable rows
        int halfH = viewH / 2;  // ~17 — horizon line

        double posX  = pos.X + 0.5, posY = pos.Y + 0.5;
        double dirX  =  Math.Cos(_fpAngle), dirY  = Math.Sin(_fpAngle);
        double planX = -Math.Sin(_fpAngle) * 0.66;
        double planY =  Math.Cos(_fpAngle) * 0.66;

        // Wall tint per theme (RGB base at full brightness)
        (int wr, int wg, int wb) = map.Theme switch {
            MapTheme.Cave   => (108, 122, 140),
            MapTheme.Crypt  => (120, 108, 155),
            MapTheme.Mines  => (145, 122,  80),
            MapTheme.Forest => ( 45, 118,  35),
            _               => (158, 140, 105),
        };

        // ── Cast one ray per column ────────────────────────────────────────
        for (int col = 0; col < viewW; col++)
        {
            int sx = col + 1;

            // cameraX: -1 (left edge) → +1 (right edge)
            double cameraX = 2.0 * col / Math.Max(1, viewW - 1) - 1.0;
            double rayDX = dirX + planX * cameraX;
            double rayDY = dirY + planY * cameraX;

            var (perpDist, ySide, _, _) = CastRay(map, posX, posY, rayDX, rayDY);

            // ── Wall slice height ────────────────────────────────────────
            int lineH     = Math.Min(viewH, (int)(viewH / perpDist));
            int drawStart = Math.Max(1,     halfH + 1 - lineH / 2);
            int drawEnd   = Math.Min(viewH, halfH + 1 + lineH / 2);

            // Wall glyph: coarser/lighter chars at distance
            char wallCh = perpDist < 1.5 ? '█'
                        : perpDist < 3.0 ? '▓'
                        : perpDist < 6.0 ? '▒'
                        :                  '░';

            // Distance fade + Y-side (horizontal face) darkening
            float fade  = (float)Math.Max(0.06, 1.0 - perpDist / 15.0);
            float sideM = ySide ? 0.68f : 1.0f;
            var wallClr = new Color(
                (byte)(wr * fade * sideM),
                (byte)(wg * fade * sideM),
                (byte)(wb * fade * sideM));

            // ── Ceiling (rows 1 … drawStart-1) ─────────────────────────
            // Gradient: deep black at top → dim indigo just above wall
            for (int sy = 1; sy < drawStart; sy++)
            {
                float t = drawStart > 2
                    ? Math.Clamp((float)(sy - 1) / (float)(drawStart - 2), 0f, 1f) : 0f;
                var cc = new Color(
                    (byte)( 6 + (int)(12 * t)),
                    (byte)( 6 + (int)(12 * t)),
                    (byte)(28 + (int)(52 * t)));
                _mapPanel.SetGlyph(sx, sy, ' ', cc, cc);
            }

            // ── Wall slice ─────────────────────────────────────────────
            for (int sy = drawStart; sy <= drawEnd; sy++)
                _mapPanel.SetGlyph(sx, sy, wallCh, wallClr, Color.Black);

            // ── Floor (rows drawEnd+1 … viewH) ─────────────────────────
            // Gradient: dim mossy green just below wall → pure black at bottom
            for (int sy = drawEnd + 1; sy <= viewH; sy++)
            {
                float t = (viewH > drawEnd)
                    ? Math.Clamp((float)(sy - drawEnd - 1) / (float)(viewH - drawEnd), 0f, 1f) : 0f;
                float v = 1f - t;
                var fc = new Color(
                    (byte)(int)(18 * v),
                    (byte)(int)(30 * v),
                    (byte)(int)(12 * v));
                _mapPanel.SetGlyph(sx, sy, ' ', fc, fc);
            }
        }

        // ── Crosshair ──────────────────────────────────────────────────────
        int crX = MapW / 2, crY = MapH / 2;
        var crossClr = new Color(220, 220, 220);
        _mapPanel.SetGlyph(crX - 1, crY, '─', crossClr, Color.Black);
        _mapPanel.SetGlyph(crX,     crY, '+', crossClr,  Color.Black);
        _mapPanel.SetGlyph(crX + 1, crY, '─', crossClr, Color.Black);
        _mapPanel.SetGlyph(crX,   crY - 1, '│', crossClr, Color.Black);
        _mapPanel.SetGlyph(crX,   crY + 1, '│', crossClr, Color.Black);

        // ── HUD bar (row 1) ─────────────────────────────────────────────────
        string facing = FpFacingLabel(_fpAngle);
        string hud    = $" [{facing}]  Tab=overhead  Arrows=move+turn  1-4=ability";
        _mapPanel.Print(1, 1, hud[..Math.Min(hud.Length, MapW - 3)],
            new Color(200, 180, 100), Color.Black);

        string floorLabel = $"Floor {GameEngine.Instance.CurrentFloor}";
        _mapPanel.Print(MapW - floorLabel.Length - 1, 1, floorLabel,
            new Color(100, 100, 160), Color.Black);

        // ── Mini-map overlay (explored tiles, bottom-right corner) ─────────
        DrawFpMiniMap(map, pos);
    }

    /// <summary>
    /// 15×9 explored-tile mini-map rendered in the bottom-right corner of the
    /// map panel.  The player is shown as a smiley (☻) glyph.
    /// </summary>
    private void DrawFpMiniMap(GameMap map, PositionComponent pos)
    {
        const int MmW = 15, MmH = 9;
        // Place inside the map panel with a 1-cell gap from the right/bottom borders
        int mxOff = MapW - MmW - 2;  // first column of the mini-map content (= 42)
        int myOff = MapH - MmH - 2;  // first row of the mini-map content    (= 26)

        int startWX = pos.X - MmW / 2;
        int startWY = pos.Y - MmH / 2;

        // Fill tiles
        for (int my = 0; my < MmH; my++)
        for (int mx = 0; mx < MmW; mx++)
        {
            int wx = startWX + mx, wy = startWY + my;
            int sx = mxOff + mx,   sy = myOff + my;
            if (sx < 1 || sx >= MapW - 1 || sy < 1 || sy >= MapH - 1) continue;

            var tile = map.GetTile(wx, wy);

            // Player marker (smiley ☻ using CP437 index 2)
            if (wx == pos.X && wy == pos.Y)
            {
                _mapPanel.SetGlyph(sx, sy, '\x02', new Color(255, 255, 80), Color.Black);
                continue;
            }

            if (!tile.IsExplored)
            {
                _mapPanel.SetGlyph(sx, sy, ' ', Color.Black, new Color(8, 8, 8));
                continue;
            }

            (char ch, Color fg, Color bg) = tile.Type switch {
                TileType.Floor                     => ('.', new Color(55, 75, 55), Color.Black),
                TileType.Wall or TileType.Empty    => (' ', Color.Black, new Color(25, 25, 25)),
                TileType.Door                      => ('+', new Color(200, 160, 80), Color.Black),
                TileType.StairsDown                => ('>', new Color(200, 200, 255), Color.Black),
                TileType.StairsUp                  => ('<', new Color(200, 200, 255), Color.Black),
                TileType.Water                     => (' ', Color.Black, new Color(20, 55, 120)),
                TileType.Chest                     => ('.', new Color(255, 200, 50), Color.Black),
                _                                  => ('.', new Color(55, 75, 55), Color.Black),
            };
            _mapPanel.SetGlyph(sx, sy, ch, fg, bg);
        }

        // Border around the mini-map
        var borderClr = new Color(50, 55, 75);
        int bL = mxOff - 1, bR = mxOff + MmW;
        int bT = myOff - 1, bB = myOff + MmH;

        // Guard: only draw border cells that are inside the safe panel area
        void SafeGlyph(int sx, int sy, char glyph)
        {
            if (sx >= 1 && sx < MapW - 1 && sy >= 1 && sy < MapH - 1)
                _mapPanel.SetGlyph(sx, sy, glyph, borderClr, Color.Black);
        }

        SafeGlyph(bL, bT, '┌');  SafeGlyph(bR, bT, '┐');
        SafeGlyph(bL, bB, '└');  SafeGlyph(bR, bB, '┘');
        for (int mx = mxOff; mx < mxOff + MmW; mx++) { SafeGlyph(mx, bT, '─'); SafeGlyph(mx, bB, '─'); }
        for (int my = myOff; my < myOff + MmH; my++) { SafeGlyph(bL, my, '│'); SafeGlyph(bR, my, '│'); }
    }
}
