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

    private int  _camX, _camY;
    private bool _gameOverShown;
    private bool _equipMode;
    private int  _abilityDirState = 0; // 0 = none, 1-4 = waiting for direction

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
        var em  = GameEngine.Instance.EntityManager;

        // Clear
        for (int sy = 1; sy < MapH - 1; sy++)
        for (int sx = 1; sx < MapW - 1; sx++)
            _mapPanel.SetGlyph(sx, sy, ' ', Color.Black, Color.Black);

        // Tiles
        for (int sy = 1; sy < MapH - 1; sy++)
        for (int sx = 1; sx < MapW - 1; sx++)
        {
            var tile = map.GetTile(_camX + sx - 1, _camY + sy - 1);
            if (tile.IsVisible)
            {
                // Visible tiles: full colour + subtle tinted background for floor
                var bg = tile.Type == TileType.Floor || tile.Type == TileType.Bush
                       ? tile.Background   // already has slight tint
                       : Color.Black;
                _mapPanel.SetGlyph(sx, sy, tile.Glyph, tile.ForegroundVisible, bg);
            }
            else if (tile.IsExplored)
            {
                // Explored-but-dark: heavily dimmed, no background tint
                _mapPanel.SetGlyph(sx, sy, tile.Glyph, tile.ForegroundExplored, Color.Black);
            }
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
                _mapPanel.SetGlyph(sx, sy, render.Glyph, render.Foreground, Color.Black);
        }

        // Floor / terrain labels (bottom-right corner)
        RenderTerrainLabels(map);

        // Floor number
        string floorLabel = $"Floor {GameEngine.Instance.CurrentFloor}";
        _mapPanel.Print(MapW - floorLabel.Length - 1, 1, floorLabel, new Color(100, 100, 160));
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
        string hint = _equipMode    ? "Pick # to equip/unequip"
                    : _abilityDirState > 0 ? $"Dir for ability {_abilityDirState}"
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

        // ── Normal movement ───────────────────────────────────────────
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
}
