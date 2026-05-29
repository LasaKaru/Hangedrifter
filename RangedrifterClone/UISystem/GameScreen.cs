using SadConsole;
using SadConsole.Input;
using SadRogue.Primitives;
using RangedrifterClone.Core;
using RangedrifterClone.Components;
using RangedrifterClone.MapSystem;
using XnaInput = Microsoft.Xna.Framework.Input;

namespace RangedrifterClone.UISystem;

/// <summary>
/// Main gameplay screen — two rendering modes:
///
///   FIRST-PERSON (default, Tab to swap)
///     DDA ray-cast with Wolfenstein-style wall slices + sprite projection.
///     Mouse X turns the camera (cursor locked to screen centre).
///     Left-click or Space fires a ranged shot.
///
///   TOP-DOWN (Tab)
///     Classic roguelike grid view with camera scrolling and FOV.
///
///   Sidebar is a "3-D RPG HUD" in both modes:
///     HP / MP bars, class chrome frame, abilities, equipment, bag.
/// </summary>
public class GameScreen : ScreenObject
{
    // ── Layout ───────────────────────────────────────────────────────────────
    private const int TotalW   = 80;
    private const int TotalH   = 50;
    private const int SidebarW = 21;
    private const int MapW     = TotalW - SidebarW;   // 59
    private const int MsgH     = 13;
    private const int MapH     = TotalH - MsgH;       // 37

    private readonly ScreenSurface _mapPanel;
    private readonly ScreenSurface _sidebar;
    private readonly ScreenSurface _msgPanel;

    // ── State ────────────────────────────────────────────────────────────────
    private int    _camX, _camY;
    private bool   _gameOverShown;
    private bool   _equipMode;
    private int    _abilityDirState = 0;
    private double _glowTime        = 0;

    // First-person
    private bool     _firstPersonMode = true;
    private double   _fpAngle         = 0.0;           // 0=East, π/2=South
    private double[] _zBuffer         = Array.Empty<double>();

    // Mouse aim
    private int  _prevMouseX     = -1;      // last frame's X; -1 = not yet sampled
    private bool _mouseAimActive = true;    // Esc releases; click re-captures
    private XnaInput.ButtonState _prevLMB = XnaInput.ButtonState.Released;

    // Gun flash
    private double _shotTimer  = 0.0;  // counts down from 0.15 s
    private bool   _shotHit    = false;
    private int    _shotWX, _shotWY;   // world cell of impact

    // Incoming enemy ranged shot visual
    private double _incomingShotTimer = 0.0;
    private int    _incomingShotFromX, _incomingShotFromY;

    // Inventory overlay
    private bool _inventoryOpen  = false;
    private int  _invSelectedIdx = 0;

    // Achievement toast
    private double _toastTimer = 0;
    private string _toastName  = "";
    private string _toastDesc  = "";
    private char   _toastIcon  = '!';
    private static readonly SadRogue.Primitives.Color ToastGold = new(255, 200, 50);
    private static readonly SadRogue.Primitives.Color ToastBg   = new(20, 15, 5);

    // Melee hit flash
    private double _meleeHitTimer = 0;
    private int    _prevPlayerHp  = -1;

    // ── Feature 1: enemy death dissolve particles ─────────────────────────
    private sealed class DeathPart
    {
        public double WX, WY, VX, VY;
        public char   Glyph;
        public Color  Color;
        public float  Life; // 1 → 0
    }
    private readonly List<DeathPart>                         _deathParts   = new();
    private readonly Dictionary<Entity, (double wx, double wy)> _enemyLastPos = new();
    private HashSet<Entity>                                  _prevEnemySet = new();

    // ── Feature 2: moving projectile trail ────────────────────────────────
    private double _projWX, _projWY, _projVX, _projVY;
    private float  _projLife;
    private bool   _projActive;

    // ── Feature 7: ambient sound timer ────────────────────────────────────
    private double _ambientTimer = 5.0;

    // ── Sidebar palette ──────────────────────────────────────────────────────
    private static readonly Color LabelClr  = new(140, 140, 140);
    private static readonly Color ValueClr  = new(200, 200, 100);
    private static readonly Color DivClr    = new( 55,  55,  55);
    private static readonly Color ChromeClr = new( 80,  80, 110);
    private static readonly Color HpLblClr  = new(180,  80,  80);
    private static readonly Color ManaClr   = new( 80, 140, 220);
    private static readonly Color PanelBg   = new(  8,   8,  14);

    // ── Constructor ──────────────────────────────────────────────────────────
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

    private void DrawBorders()
    {
        var b = new ColoredGlyph(DivClr, Color.Black);
        _mapPanel.DrawBox(new Rectangle(0, 0, MapW,     MapH),   ShapeParameters.CreateStyledBox(ICellSurface.ConnectedLineThin, b));
        _sidebar .DrawBox(new Rectangle(0, 0, SidebarW, TotalH), ShapeParameters.CreateStyledBox(ICellSurface.ConnectedLineThin, b));
        _msgPanel.DrawBox(new Rectangle(0, 0, MapW,     MsgH),   ShapeParameters.CreateStyledBox(ICellSurface.ConnectedLineThin, b));
    }

    public void ResetGameOver() { _gameOverShown = false; DrawBorders(); }

    // ── Per-frame update ─────────────────────────────────────────────────────
    public override void Update(TimeSpan delta)
    {
        base.Update(delta);
        _glowTime += delta.TotalSeconds;
        if (_shotTimer         > 0) _shotTimer         -= delta.TotalSeconds;
        if (_incomingShotTimer > 0) _incomingShotTimer -= delta.TotalSeconds;
        if (_toastTimer        > 0) _toastTimer        -= delta.TotalSeconds;
        if (_meleeHitTimer     > 0) _meleeHitTimer     -= delta.TotalSeconds;

        // Dequeue next achievement toast
        if (_toastTimer <= 0 &&
            GameEngine.Instance.Achievements.TryDequeueToast(out var toast))
        {
            _toastName  = toast.Name;
            _toastDesc  = toast.Desc;
            _toastIcon  = toast.Icon;
            _toastTimer = 3.0;
        }

        // Pick up any ranged shot an enemy just fired this turn
        if (GameEngine.Instance.HasEnemyShot)
        {
            _incomingShotTimer = 0.30;
            _incomingShotFromX = GameEngine.Instance.EnemyShotX;
            _incomingShotFromY = GameEngine.Instance.EnemyShotY;
            GameEngine.Instance.ClearEnemyShot();
            MusicPlayer.PlayGrowl();
        }

        // Ambient dungeon drip
        if (GameEngine.Instance.State == GameState.Playing)
        {
            _ambientTimer -= delta.TotalSeconds;
            if (_ambientTimer <= 0)
            {
                MusicPlayer.PlayAmbientDrip();
                _ambientTimer = 4.0 + new Random().NextDouble() * 5.0;
            }
        }

        // Projectile trail
        if (_projActive)
        {
            _projWX    += _projVX * delta.TotalSeconds;
            _projWY    += _projVY * delta.TotalSeconds;
            _projLife  -= (float)(delta.TotalSeconds / 0.22);
            if (_projLife <= 0) { _projActive = false; }
            else
            {
                var m = GameEngine.Instance.CurrentMap;
                if (m != null && !m.IsWalkable((int)_projWX, (int)_projWY))
                    _projActive = false;
            }
        }

        // Death dissolve — track enemies, spawn particles on death
        if (GameEngine.Instance.State == GameState.Playing)
        {
            var em  = GameEngine.Instance.EntityManager;
            var cur = new HashSet<Entity>(em.GetEntitiesWith<AIComponent>());
            foreach (var e in _prevEnemySet)
            {
                if (!cur.Contains(e) && _enemyLastPos.TryGetValue(e, out var lp))
                    SpawnDeathParticles(lp.wx, lp.wy);
            }
            foreach (var e in cur)
            {
                var p = em.GetComponent<PositionComponent>(e);
                if (p != null) _enemyLastPos[e] = (p.X + 0.5, p.Y + 0.5);
            }
            _enemyLastPos.Keys.ToList()
                .Where(k => !cur.Contains(k)).ToList()
                .ForEach(k => _enemyLastPos.Remove(k));
            _prevEnemySet = cur;
        }

        // Advance death particles
        for (int i = _deathParts.Count - 1; i >= 0; i--)
        {
            var p = _deathParts[i];
            p.Life -= (float)(delta.TotalSeconds / 0.7);
            p.WX   += p.VX * delta.TotalSeconds;
            p.WY   += p.VY * delta.TotalSeconds;
            if (p.Life <= 0) _deathParts.RemoveAt(i);
        }

        // Melee hit detection: HP decreased this frame without a ranged shot
        {
            var hpC = GameEngine.Instance.EntityManager
                .GetComponent<FighterComponent>(GameEngine.Instance.PlayerEntity);
            if (hpC != null && GameEngine.Instance.State == GameState.Playing)
            {
                if (_prevPlayerHp > 0 && hpC.Hp < _prevPlayerHp && _incomingShotTimer < 0.29)
                    _meleeHitTimer = 0.5;
                _prevPlayerHp = hpC.Hp;
            }
        }

        var st = GameEngine.Instance.State;
        if (st == GameState.Playing || st == GameState.GameOver)
        {
            // ── Mouse aim (FP only) ────────────────────────────────────
            if (_firstPersonMode && st == GameState.Playing)
            {
                try
                {
                    var ms = XnaInput.Mouse.GetState();

                    if (_mouseAimActive)
                    {
                        // Delta-only tracking — no cursor lock, mouse moves freely
                        if (_prevMouseX >= 0)
                        {
                            int mdx = ms.X - _prevMouseX;
                            // Ignore huge jumps (window focus change, etc.)
                            if (Math.Abs(mdx) < 300)
                                _fpAngle += mdx * 0.004;
                        }
                        _prevMouseX = ms.X;

                        // Left-click to fire
                        if (ms.LeftButton == XnaInput.ButtonState.Pressed &&
                            _prevLMB      == XnaInput.ButtonState.Released)
                            TriggerRangedShot();
                    }
                    else
                    {
                        // Aim is released — left-click re-captures without firing
                        if (ms.LeftButton == XnaInput.ButtonState.Pressed &&
                            _prevLMB      == XnaInput.ButtonState.Released)
                        {
                            _mouseAimActive = true;
                            _prevMouseX     = ms.X; // seed so no jump on recapture
                        }
                    }

                    _prevLMB = ms.LeftButton;
                }
                catch { }
            }

            if (!_firstPersonMode) UpdateCamera();
            RenderMap();
            if (_inventoryOpen) DrawInventoryOverlay();
            RenderSidebar();
            if (_toastTimer > 0) DrawAchievementToast();
        }
    }

    private void DrawAchievementToast()
    {
        double fade  = Math.Min(1.0, _toastTimer / 0.4);     // fade in fast
        double fade2 = Math.Min(1.0, _toastTimer * 2.5);     // fade out last 0.4 s
        float  f     = (float)Math.Min(fade, fade2);

        int tw  = 36, th = 4;
        int tx  = (_mapPanel.Width - tw) / 2;
        int ty  = 2;

        var bg   = Scale(ToastBg,   f);
        var gold = Scale(ToastGold, f);
        var gray = Scale(new SadRogue.Primitives.Color(160, 160, 160), f);

        // Box
        for (int ry = ty; ry < ty + th; ry++)
        for (int rx = tx; rx < tx + tw; rx++)
            _mapPanel.SetGlyph(rx, ry, ' ', SadRogue.Primitives.Color.White, bg);

        // Border top/bottom
        for (int rx = tx + 1; rx < tx + tw - 1; rx++)
        {
            _mapPanel.SetGlyph(rx, ty,         '─', gold, bg);
            _mapPanel.SetGlyph(rx, ty + th - 1, '─', gold, bg);
        }
        _mapPanel.SetGlyph(tx,          ty,          '┌', gold, bg);
        _mapPanel.SetGlyph(tx + tw - 1, ty,          '┐', gold, bg);
        _mapPanel.SetGlyph(tx,          ty + th - 1, '└', gold, bg);
        _mapPanel.SetGlyph(tx + tw - 1, ty + th - 1, '┘', gold, bg);

        // Icon + "Achievement Unlocked!"
        _mapPanel.SetGlyph(tx + 2, ty + 1, _toastIcon, gold, bg);
        string header = " Achievement Unlocked!";
        _mapPanel.Print(tx + 3, ty + 1, header, gold, bg);

        // Name + desc
        string nameStr = _toastName.PadRight(tw - 4);
        string descStr = _toastDesc.Length > tw - 4 ? _toastDesc[..(tw - 4)] : _toastDesc.PadRight(tw - 4);
        _mapPanel.Print(tx + 2, ty + 2, nameStr, gray, bg);
    }

    private static SadRogue.Primitives.Color Scale(SadRogue.Primitives.Color c, float f)
    {
        f = Math.Clamp(f, 0f, 1f);
        return new SadRogue.Primitives.Color((int)(c.R * f), (int)(c.G * f), (int)(c.B * f));
    }

    // ── Top-down camera ───────────────────────────────────────────────────────
    private void UpdateCamera()
    {
        var pos = GameEngine.Instance.EntityManager
            .GetComponent<PositionComponent>(GameEngine.Instance.PlayerEntity);
        var map = GameEngine.Instance.CurrentMap;
        if (pos == null || map == null) return;
        _camX = Math.Clamp(pos.X - (MapW - 2) / 2, 0, Math.Max(0, map.Width  - MapW + 2));
        _camY = Math.Clamp(pos.Y - (MapH - 2) / 2, 0, Math.Max(0, map.Height - MapH + 2));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // MAP DISPATCH
    // ═════════════════════════════════════════════════════════════════════════
    private void RenderMap()
    {
        var map = GameEngine.Instance.CurrentMap;
        if (map == null) return;

        if (_firstPersonMode) { RenderFirstPerson(map); return; }

        var em = GameEngine.Instance.EntityManager;

        for (int sy = 1; sy < MapH - 1; sy++)
        for (int sx = 1; sx < MapW - 1; sx++)
            _mapPanel.SetGlyph(sx, sy, ' ', Color.Black, Color.Black);

        for (int sy = 1; sy < MapH - 1; sy++)
        for (int sx = 1; sx < MapW - 1; sx++)
        {
            var tile = map.GetTile(_camX + sx - 1, _camY + sy - 1);
            if (tile.IsVisible)
                _mapPanel.SetGlyph(sx, sy, tile.Glyph, tile.ForegroundVisible,
                    tile.Type == TileType.Wall ? tile.Background : tile.Background);
            else if (tile.IsExplored)
                _mapPanel.SetGlyph(sx, sy, tile.Glyph, tile.ForegroundExplored, Color.Black);
        }

        foreach (var (_, render, pos) in em
            .GetEntitiesWith<RenderComponent, PositionComponent>()
            .Select(e => (e, em.GetComponent<RenderComponent>(e)!, em.GetComponent<PositionComponent>(e)!))
            .OrderBy(t => t.Item2.RenderLayer))
        {
            if (!render.IsVisible) continue;
            if (!map.GetTile(pos.X, pos.Y).IsVisible) continue;
            int sx = pos.X - _camX + 1, sy = pos.Y - _camY + 1;
            if (sx >= 1 && sx < MapW - 1 && sy >= 1 && sy < MapH - 1)
            {
                var bg = render.Background == Color.Transparent ? Color.Black : render.Background;
                _mapPanel.SetGlyph(sx, sy, render.Glyph, render.Foreground, bg);
            }
        }

        ApplyPlayerGlow(map);
        RenderTerrainLabels(map);
        string fl = $"Floor {GameEngine.Instance.CurrentFloor}";
        _mapPanel.Print(MapW - fl.Length - 1, 1, fl, new Color(100, 100, 160));
    }

    // ── Top-down: player glow ────────────────────────────────────────────────
    private void ApplyPlayerGlow(GameMap map)
    {
        var em     = GameEngine.Instance.EntityManager;
        var player = GameEngine.Instance.PlayerEntity;
        var pos    = em.GetComponent<PositionComponent>(player);
        var rend   = em.GetComponent<RenderComponent>(player);
        if (pos == null || rend == null) return;

        float pulse = (float)(0.55 + 0.45 * Math.Sin(_glowTime * Math.PI * 2.0));
        var c = rend.Foreground;
        const int R = 3;

        for (int dy = -R; dy <= R; dy++)
        for (int dx = -R; dx <= R; dx++)
        {
            if (dx == 0 && dy == 0) continue;
            float dist = MathF.Sqrt(dx * dx + dy * dy);
            if (dist > R + 0.5f) continue;
            int wx = pos.X + dx, wy = pos.Y + dy;
            if (!map.GetTile(wx, wy).IsVisible) continue;
            int sx = wx - _camX + 1, sy = wy - _camY + 1;
            if (sx < 1 || sx >= MapW - 1 || sy < 1 || sy >= MapH - 1) continue;
            float i = (1f - dist / (R + 1f));
            i = i * i * 0.45f * pulse;
            _mapPanel.SetBackground(sx, sy, new Color(
                (byte)(c.R * i), (byte)(c.G * i), (byte)(c.B * i)));
        }

        int psx = pos.X - _camX + 1, psy = pos.Y - _camY + 1;
        if (psx >= 1 && psx < MapW - 1 && psy >= 1 && psy < MapH - 1)
        {
            float bright = 0.80f + 0.20f * pulse;
            var fg = new Color(
                (byte)Math.Min(255, (int)(c.R * bright) + (int)(50 * pulse)),
                (byte)Math.Min(255, (int)(c.G * bright) + (int)(35 * pulse)),
                (byte)Math.Min(255, (int)(c.B * bright) + (int)(20 * pulse)));
            var bg = new Color(
                (byte)(c.R * 0.28f * pulse), (byte)(c.G * 0.22f * pulse), (byte)(c.B * 0.18f * pulse));
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
            if (t.IsVisible && t.Type is TileType.Rock or TileType.Bush or TileType.StairsDown
                or TileType.StairsUp or TileType.Chest or TileType.Trap or TileType.Door or TileType.Water)
                if (!nearby.Contains(t.Name)) nearby.Add(t.Name);
        }
        int ly = MapH - 2;
        foreach (var name in nearby)
            _mapPanel.Print(MapW - name.Length - 2, ly--, name, new Color(160, 160, 100));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // FIRST-PERSON RENDERER
    // ═════════════════════════════════════════════════════════════════════════

    private static (double dist, bool ySide, int hx, int hy) CastRay(
        GameMap map, double px, double py, double rdx, double rdy)
    {
        int mx = (int)px, my = (int)py;
        double ddx = Math.Abs(rdx) < 1e-10 ? 1e30 : Math.Abs(1.0 / rdx);
        double ddy = Math.Abs(rdy) < 1e-10 ? 1e30 : Math.Abs(1.0 / rdy);
        int sx, sy;
        double sidX, sidY;
        if (rdx < 0) { sx = -1; sidX = (px - mx) * ddx; }
        else         { sx =  1; sidX = (mx + 1.0 - px) * ddx; }
        if (rdy < 0) { sy = -1; sidY = (py - my) * ddy; }
        else         { sy =  1; sidY = (my + 1.0 - py) * ddy; }
        bool hit = false, ySide = false;
        int steps = 80;
        while (!hit && steps-- > 0)
        {
            if (sidX < sidY) { sidX += ddx; mx += sx; ySide = false; }
            else             { sidY += ddy; my += sy; ySide = true;  }
            var t = map.GetTile(mx, my);
            if (!t.IsWalkable || t.Type == TileType.Empty) hit = true;
        }
        if (!hit) return (1e6, false, mx, my);
        double dist = ySide ? (my - py + (1 - sy) * 0.5) / rdy
                             : (mx - px + (1 - sx) * 0.5) / rdx;
        return (Math.Max(0.15, dist), ySide, mx, my);
    }

    private void RenderFirstPerson(GameMap map)
    {
        var em     = GameEngine.Instance.EntityManager;
        var player = GameEngine.Instance.PlayerEntity;
        var pos    = em.GetComponent<PositionComponent>(player);
        var rend   = em.GetComponent<RenderComponent>(player);
        if (pos == null) return;

        int viewW = MapW - 2, viewH = MapH - 2, halfH = viewH / 2;
        if (_zBuffer.Length != viewW) _zBuffer = new double[viewW];

        for (int sy = 1; sy < MapH - 1; sy++)
        for (int sx = 1; sx < MapW - 1; sx++)
            _mapPanel.SetGlyph(sx, sy, ' ', Color.Black, Color.Black);

        double posX  = pos.X + 0.5, posY = pos.Y + 0.5;
        double dirX  =  Math.Cos(_fpAngle), dirY  = Math.Sin(_fpAngle);
        double planX = -Math.Sin(_fpAngle) * 0.66;
        double planY  =  Math.Cos(_fpAngle) * 0.66;

        // ── Low-poly flat-shaded palette ─────────────────────────────────
        var (wallFront, wallSide, skyNear, skyFar, floorNear, floorFar2) =
            FpThemePalette(map.Theme);

        // Cache door tile positions for O(1) per-column lookup
        var doorTiles = new HashSet<(int, int)>();
        foreach (var de in em.GetEntitiesWith<FeatureComponent, PositionComponent>())
        {
            var ft = em.GetComponent<FeatureComponent>(de)!;
            var dp = em.GetComponent<PositionComponent>(de)!;
            if (ft.Type == FeatureType.Door) doorTiles.Add((dp.X, dp.Y));
        }

        // ── Per-column: half-block wall + flat sky + floor ────────────────
        for (int col = 0; col < viewW; col++)
        {
            int    sx   = col + 1;
            double camX = 2.0 * col / Math.Max(1, viewW - 1) - 1.0;
            double rdx  = dirX + planX * camX;
            double rdy  = dirY + planY * camX;

            var (perpDist, ySide, hitX, hitY) = CastRay(map, posX, posY, rdx, rdy);
            _zBuffer[col] = perpDist;

            // ── Flat-shaded wall color: face direction + fog ──────────────
            float distFade = (float)Math.Max(0.12, 1.0 - perpDist / 22.0);
            float faceMul  = ySide ? 0.58f : 1.0f;
            float flicker  = 0.93f + 0.07f * (float)Math.Sin(_glowTime * 7.3 + col * 0.41);
            float fi       = distFade * faceMul * flicker;

            Color wallClr;
            if (doorTiles.Contains((hitX, hitY)))
            {
                wallClr = FpC(ySide ? 138 : 185, ySide ? 84 : 112, ySide ? 36 : 50, fi);
            }
            else if (map.Theme == MapTheme.Forest)
            {
                // Tree trunks on X-face, leafy canopy on Y-face
                wallClr = ySide ? FpC(42, 90, 26, fi) : FpC(60, 40, 18, fi);
            }
            else
            {
                wallClr = ySide
                    ? FpC(wallSide.R,  wallSide.G,  wallSide.B,  fi)
                    : FpC(wallFront.R, wallFront.G, wallFront.B, fi);
            }

            // ── Half-pixel wall extent (2 half-pixels per cell row) ───────
            double lineHf    = viewH / Math.Max(0.01, perpDist);
            int    wallTopHp = (int)Math.Max(0,          viewH - lineHf);
            int    wallBotHp = (int)Math.Min(viewH * 2,  viewH + lineHf);

            for (int sy = 1; sy <= viewH; sy++)
            {
                int  tHp = (sy - 1) * 2;
                int  bHp = tHp + 1;
                bool tw  = tHp >= wallTopHp && tHp < wallBotHp;
                bool bw  = bHp >= wallTopHp && bHp < wallBotHp;

                if (tw && bw)
                {
                    // Pure wall: solid flat-shaded cell
                    _mapPanel.SetGlyph(sx, sy, ' ', wallClr, wallClr);
                }
                else if (!tw && !bw)
                {
                    if (tHp < wallTopHp)
                    {
                        // Ceiling: deep void at top → dark horizon
                        float t  = halfH > 0 ? Math.Clamp((float)(sy - 1) / halfH, 0f, 1f) : 0f;
                        var   cc = LerpC(skyFar, skyNear, t);
                        _mapPanel.SetGlyph(sx, sy, ' ', cc, cc);
                    }
                    else
                    {
                        // Floor: bright horizon → dark far
                        float t  = (viewH - halfH) > 0
                            ? Math.Clamp((float)(sy - halfH - 1) / (viewH - halfH), 0f, 1f) : 0f;
                        var   fc = LerpC(floorNear, floorFar2, t);
                        _mapPanel.SetGlyph(sx, sy, ' ', fc, fc);
                    }
                }
                else if (!tw && bw)
                {
                    // Ceiling → Wall boundary: ▄ fg=wallClr bg=skyColor
                    float t  = halfH > 0 ? Math.Clamp((float)(sy - 1) / halfH, 0f, 1f) : 0f;
                    _mapPanel.SetGlyph(sx, sy, '▄', wallClr, LerpC(skyFar, skyNear, t));
                }
                else
                {
                    // Wall → Floor boundary: ▀ fg=wallClr bg=floorColor
                    float t  = (viewH - halfH) > 0
                        ? Math.Clamp((float)(sy - halfH - 1) / (viewH - halfH), 0f, 1f) : 0f;
                    _mapPanel.SetGlyph(sx, sy, '▀', wallClr, LerpC(floorNear, floorFar2, t));
                }
            }
        }

        // ── Sprites ───────────────────────────────────────────────────────
        RenderFpSprites(map, pos, posX, posY, dirX, dirY, planX, planY, viewW, viewH, halfH);

        // ── Death dissolve particles ───────────────────────────────────────
        DrawDeathParticles(posX, posY, dirX, dirY, planX, planY, viewW, viewH);

        // ── Moving projectile trail ────────────────────────────────────────
        DrawProjectileTrail(posX, posY, dirX, dirY, planX, planY, viewW, viewH);

        // ── Horizon glow ──────────────────────────────────────────────────
        if (rend != null)
        {
            float pulse = (float)(0.55 + 0.45 * Math.Sin(_glowTime * Math.PI * 2.0));
            var c = rend.Foreground;
            int glowRow = halfH + 1;
            for (int gx = 1; gx < MapW - 1; gx++)
            {
                float dist = MathF.Abs(gx - MapW / 2f);
                float t    = Math.Clamp(1f - dist / (viewW * 0.38f), 0f, 1f);
                float i    = t * t * 0.18f * pulse;
                if (i < 0.02f) continue;
                var ex = _mapPanel.GetCellAppearance(gx, glowRow);
                if (ex == null) continue;
                _mapPanel.SetBackground(gx, glowRow,
                    new Color((byte)(ex.Background.R + (int)(c.R * i)),
                              (byte)(ex.Background.G + (int)(c.G * i)),
                              (byte)(ex.Background.B + (int)(c.B * i))));
            }
        }

        // ── Status effect vignette ────────────────────────────────────────
        DrawStatusEffectVignette(viewW, viewH);

        // ── Muzzle flash ──────────────────────────────────────────────────
        DrawShotFlash(viewW, viewH);
        DrawIncomingShotFlash(viewW, viewH);
        DrawMeleeHitFlash(viewW, viewH);

        // ── Crosshair ─────────────────────────────────────────────────────
        int crX = MapW / 2, crY = MapH / 2;
        var crossClr = new Color(220, 220, 220);
        _mapPanel.SetGlyph(crX - 1, crY,   '─', crossClr, Color.Black);
        _mapPanel.SetGlyph(crX,     crY,   '+', crossClr,  Color.Black);
        _mapPanel.SetGlyph(crX + 1, crY,   '─', crossClr, Color.Black);
        _mapPanel.SetGlyph(crX,     crY-1, '│', crossClr, Color.Black);
        _mapPanel.SetGlyph(crX,     crY+1, '│', crossClr, Color.Black);

        // ── HUD bar ────────────────────────────────────────────────────────
        string facing = FpFacingLabel(_fpAngle);
        string aimHint = _mouseAimActive ? "Mouse=aim  Esc=free" : "Click=recapture";
        // Show pickup hint when standing on an item
        string pickHint = "";
        foreach (var pie in em.GetEntitiesWith<ItemComponent, PositionComponent>())
        {
            var pep = em.GetComponent<PositionComponent>(pie)!;
            if (pep.X == pos.X && pep.Y == pos.Y)
            { pickHint = $"  G={em.GetComponent<NameComponent>(pie)?.Name ?? "item"}"; break; }
        }
        string hud = $" [{facing}]  Tab=map  {aimHint}  LClick=fire{pickHint}";
        _mapPanel.Print(1, 1, hud[..Math.Min(hud.Length, MapW - 3)], new Color(200, 180, 100), Color.Black);
        string flLbl = $"Floor {GameEngine.Instance.CurrentFloor}";
        _mapPanel.Print(MapW - flLbl.Length - 1, 1, flLbl, new Color(100, 100, 160), Color.Black);

        // ── Mini-map ───────────────────────────────────────────────────────
        DrawFpMiniMap(map, pos);

        // ── Weapon viewmodel (ranged/magic weapons only) ──────────────────
        DrawWeaponViewModel();

        // ── Player body (drawn last so it's always on top) ─────────────────
        DrawPlayerBody();

        // ── Boss HP bar (always on top, drawn over everything) ────────────
        DrawBossHpBar(viewW);

        // ── CRT scanline overlay (very last) ──────────────────────────────
        DrawCrtScanlines(viewH);
    }

    // ── Muzzle flash + impact flash ───────────────────────────────────────────
    private void DrawShotFlash(int viewW, int viewH)
    {
        if (_shotTimer <= 0) return;

        float t = (float)(_shotTimer / 0.18);  // 1→0
        int crX = MapW / 2, crY = MapH / 2;

        // Muzzle ring around crosshair
        byte br = (byte)(255 * t), bg = (byte)(200 * t), bb = (byte)(80 * t);
        var flashClr = new Color(br, bg, bb);
        int ring = (int)(2 + 2 * t);
        for (int ry = -ring; ry <= ring; ry++)
        for (int rx = -ring * 2; rx <= ring * 2; rx++)
        {
            float d = MathF.Sqrt(rx * rx * 0.25f + ry * ry);
            if (d < ring - 0.5f || d > ring + 0.8f) continue;
            int sx = crX + rx, sy = crY + ry;
            if (sx >= 1 && sx < MapW - 1 && sy >= 1 && sy < MapH - 1)
                _mapPanel.SetGlyph(sx, sy, '*', flashClr, Color.Black);
        }

        // Bullet trail — horizontal line out from crosshair
        int trailLen = (int)((viewW / 2 - 2) * t);
        for (int tx = 1; tx <= trailLen; tx++)
        {
            int sx = crX + tx;
            if (sx < 1 || sx >= MapW - 1) break;
            float ti = (float)(trailLen - tx) / Math.Max(1, trailLen);
            byte tc = (byte)(200 * ti * t);
            _mapPanel.SetGlyph(sx, crY, '·', new Color(tc, (byte)(tc * 0.8f), 0), Color.Black);
        }

        // If hit: flash at the screen column nearest the impact
        if (_shotHit && GameEngine.Instance.CurrentMap != null)
        {
            var playerPos = GameEngine.Instance.EntityManager
                .GetComponent<PositionComponent>(GameEngine.Instance.PlayerEntity);
            if (playerPos != null)
            {
                double posX = playerPos.X + 0.5, posY = playerPos.Y + 0.5;
                double dx   = _shotWX + 0.5 - posX, dy = _shotWY + 0.5 - posY;
                double dirX = Math.Cos(_fpAngle), dirY = Math.Sin(_fpAngle);
                double planX = -Math.Sin(_fpAngle) * 0.66;
                double planY  =  Math.Cos(_fpAngle) * 0.66;
                double invDet = 1.0 / (planX * dirY - dirX * planY);
                double txD    = invDet * ( dirY * dx  -  dirX * dy);
                double txH    = invDet * (-planY * dx + planX * dy);
                if (txD > 0)
                {
                    int screenCol = (int)((viewW * 0.5) * (1.0 + txH / txD)) + 1;
                    int flashH    = Math.Min(viewH, (int)(viewH * 0.6 / txD));
                    int fTop      = Math.Max(1, MapH / 2 - flashH / 2);
                    int fBot      = Math.Min(MapH - 2, MapH / 2 + flashH / 2);
                    for (int sy = fTop; sy <= fBot; sy++)
                    {
                        int sx = Math.Clamp(screenCol, 1, MapW - 2);
                        byte ic = (byte)(220 * t);
                        _mapPanel.SetGlyph(sx, sy, '│', new Color(ic, (byte)(ic * 0.7f), 0), Color.Black);
                    }
                }
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PLAYER BODY OVERLAY  (FP mode — drawn at bottom of map panel)
    // ─────────────────────────────────────────────────────────────────────────
    /// <summary>
    /// Draws a class-specific ASCII-art character body at the bottom centre of
    /// the first-person view panel, mimicking the "you can see your own body"
    /// look of old dungeon-crawlers (Eye of the Beholder, Dungeon Master, etc.).
    ///
    /// Layout: the figure is 8 rows tall, ~11 cells wide, centred at MapW/2.
    /// The whole figure bobs 1 row on a slow sine wave (breathing animation).
    /// The equipped weapon glyph drives the weapon colour.
    /// </summary>
    private void DrawPlayerBody()
    {
        var em     = GameEngine.Instance.EntityManager;
        var player = GameEngine.Instance.PlayerEntity;
        var rend   = em.GetComponent<RenderComponent>(player);
        var cls    = em.GetComponent<ClassComponent>(player);
        var equip  = em.GetComponent<EquipmentSlotComponent>(player);
        if (rend == null) return;

        Color clr   = rend.Foreground;
        Color dim   = Dim(clr, 0.55f);
        Color dark  = Dim(clr, 0.28f);
        Color steel = new Color(160, 165, 178);   // armour metal
        Color dstl  = new Color( 88,  92, 102);   // dark metal / shadow
        Color wclr  = equip?.Weapon?.Color ?? new Color(200, 190, 80);

        // Breathing bob: body rises 1 row at the peak of a 1.6 Hz sine wave
        bool bUp = Math.Sin(_glowTime * 1.6) > 0.4;
        int  cx  = MapW / 2;                      // = 29, horizontal centre
        int  cy  = MapH - 2 - (bUp ? 1 : 0);     // = 34 or 35

        // Local helper: place a glyph relative to (cx, cy), clipped to panel
        void P(int dx, int dy, char g, Color fg)
        {
            int sx = cx + dx, sy = cy + dy;
            if (sx >= 1 && sx < MapW - 1 && sy >= 1 && sy < MapH - 1)
                _mapPanel.SetGlyph(sx, sy, g, fg, Color.Black);
        }

        var pClass = cls?.Class ?? PlayerClass.Warrior;
        switch (pClass)
        {
            // ── WARRIOR ─────────────────────────────────────────────────────
            //      /          ← sword tip
            //    /
            // [☺]     +      ← head [☺]; crossguard + at head level (right)
            // <[▄▄▄]>        ← pauldrons + breastplate
            // | ═╪═ |        ← belt + centre clasp
            // / [─] \        ← waist tassets
            // [   ] [   ]    ← two separate greaves
            // /__ \ /__ \    ← heavy boots
            case PlayerClass.Warrior:
                // Sword raised up-right (blade above, crossguard at head level)
                P(+5, -7, '/', wclr); P(+4, -6, '/', wclr);
                // Helmet + face; crossguard at same row, to the right
                P(-1, -5, '[', steel); P(0, -5, '\x01', clr); P(+1, -5, ']', steel);
                P(+3, -5, '+', wclr);                              // crossguard
                // Pauldrons (shoulder guards)
                P(-3, -4, '<', steel); P(-2, -4, '[', steel);
                P(-1, -4, '▄', dstl);  P(0, -4, '▄', dstl);  P(+1, -4, '▄', dstl);
                P(+2, -4, ']', steel); P(+3, -4, '>', steel);
                // Chest plate + belt clasp
                P(-2, -3, '|', dark); P(-1, -3, '═', dstl);
                P( 0, -3, '╪', dstl); P(+1, -3, '═', dstl); P(+2, -3, '|', dark);
                // Waist tassets
                P(-2, -2, '/', dark); P(-1, -2, '[', dstl);
                P( 0, -2, '─', dstl); P(+1, -2, ']', dstl); P(+2, -2, '\\', dark);
                // Two separate armoured greaves
                P(-3, -1, '[', dim); P(-2, -1, ' ', dim); P(-1, -1, ']', dim);
                P(+1, -1, '[', dim); P(+2, -1, ' ', dim); P(+3, -1, ']', dim);
                // Heavy boots (symmetric)
                P(-4, 0, '/', dstl); P(-3, 0, '_', dstl); P(-2, 0, '_', dstl); P(-1, 0, '\\', dstl);
                P(+1, 0, '/', dstl); P(+2, 0, '_', dstl); P(+3, 0, '_', dstl); P(+4, 0, '\\', dstl);
                break;

            // ── MAGE ────────────────────────────────────────────────────────
            // Floating staff + orb; flowing robes; slim silhouette.
            case PlayerClass.Mage:
                var orbClr  = new Color(170, 150, 255);
                var staffClr = new Color(160, 125, 65);
                // Staff + orb (floats because it's animated via bob)
                P(+4, -8, '*', orbClr);
                P(+3, -7, '|', staffClr); P(+3, -6, '|', staffClr);
                P(+2, -5, '/', staffClr);
                // Head in cowl
                P(-1, -5, '(', dim); P(0, -5, '\x01', clr); P(+1, -5, ')', dim);
                // Robe shoulders
                P(-2, -4, '(', dim);  P(-1, -4, '|', dim);
                P(0,  -4, '|', dim);  P(+1, -4, '|', dim);  P(+2, -4, ')', dim);
                // Robe body
                P(-2, -3, '|', dim);  P(-1, -3, '|', dark);
                P(0,  -3, '|', dark); P(+1, -3, '|', dark); P(+2, -3, '|', dim);
                P(-2, -2, '(', dark); P(-1, -2, '|', dark);
                P(0,  -2, '|', dark); P(+1, -2, '|', dark); P(+2, -2, ')', dark);
                // Robe hem (wider + flared)
                P(-3, -1, '\\', dark); P(-2, -1, '~', dim); P(-1, -1, '~', dim);
                P(0,  -1, '~', dim);   P(+1, -1, '~', dim); P(+2, -1, '~', dim);
                P(+3, -1, '/', dark);
                P(-2, 0, '~', dark); P(-1, 0, '~', dark);
                P(0,  0, '~', dark);  P(+1, 0, '~', dark); P(+2, 0, '~', dark);
                break;

            // ── ROGUE ────────────────────────────────────────────────────────
            // Twin daggers; hooded; slim cloaked form; crouched stance.
            case PlayerClass.Rogue:
                // Left dagger
                P(-5, -6, '+', wclr); P(-4, -6, '-', wclr); P(-4, -5, '\\', wclr);
                // Right dagger
                P(+5, -6, '+', wclr); P(+4, -6, '-', wclr); P(+4, -5, '/', wclr);
                // Hooded head
                P(-1, -5, ',', dim); P(0, -5, '\x01', clr); P(+1, -5, ',', dim);
                // Cloak — upper
                P(-2, -4, '{', dim);  P(-1, -4, '|', dark);
                P(0,  -4, '|', dark); P(+1, -4, '|', dark); P(+2, -4, '}', dim);
                // Cloak — mid
                P(-3, -3, '{', dark); P(-2, -3, '|', dark);
                P(-1, -3, '|', dark); P(0,  -3, '|', dark);
                P(+1, -3, '|', dark); P(+2, -3, '|', dark); P(+3, -3, '}', dark);
                // Cloak — lower (flared)
                P(-3, -2, '/', dark); P(-2, -2, '|', dark);
                P(-1, -2, '|', dark); P(0,  -2, '|', dark);
                P(+1, -2, '|', dark); P(+2, -2, '|', dark); P(+3, -2, '\\', dark);
                // Crouched legs (Rogue leans forward)
                P(-1, -1, '/', dim);  P(+1, -1, '\\', dim);
                P(-1,  0, '/', dark); P(+1,  0, '\\', dark);
                break;
        }

        // Ground shadow line at the very bottom row — subtle dark band
        for (int gx = cx - 5; gx <= cx + 5; gx++)
        {
            int sy = cy + 1;
            if (gx >= 1 && gx < MapW - 1 && sy >= 1 && sy < MapH - 1)
            {
                float dist = MathF.Abs(gx - cx) / 5f;
                byte  alpha = (byte)(35 - (int)(30 * dist));
                _mapPanel.SetBackground(gx, sy, new Color(alpha, alpha, alpha));
            }
        }
    }

    private static Color Dim(Color c, float f) =>
        new Color((byte)(c.R * f), (byte)(c.G * f), (byte)(c.B * f));

    // ═════════════════════════════════════════════════════════════════════════
    // DETAILED ENEMY BODY  (called per-pixel inside RenderFpSprites)
    // ═════════════════════════════════════════════════════════════════════════
    /// <summary>
    /// Returns the glyph and pre-faded colour for a single cell of an enemy
    /// sprite, based on archetype (boss / undead / beast / mage / ranged /
    /// default humanoid), relative X position within the sprite (0=left edge,
    /// 1=right edge) and relative Y position (0=top, 1=bottom).
    /// </summary>
    private (char g, Color fg) GetDetailedEnemyGlyph(
        Entity eid, float relX, float relY, float fade)
    {
        var em   = GameEngine.Instance.EntityManager;
        var r    = em.GetComponent<RenderComponent>(eid);
        var ai   = em.GetComponent<AIComponent>(eid);
        var name = em.GetComponent<NameComponent>(eid)?.Name?.ToLowerInvariant() ?? "";
        var bc   = r?.Foreground ?? Color.White;

        bool isBoss   = ai?.Behavior == AIBehavior.Boss;
        bool isUndead = name.Contains("skeleton") || name.Contains("zombie")
                     || name.Contains("lich")     || name.Contains("ghost")
                     || name.Contains("wraith");
        bool isBeast  = name.Contains("wolf")  || name.Contains("rat")
                     || name.Contains("spider") || name.Contains("bat")
                     || name.Contains("slime");
        bool isMage   = name.Contains("mage")  || name.Contains("wizard")
                     || name.Contains("witch")  || name.Contains("sorceress")
                     || name.Contains("sorcerer");
        bool isRanged = ai?.IsRanged == true && !isMage;

        bool isL = relX < 0.30f, isR = relX > 0.70f;

        // Helper: build a pre-faded colour from explicit RGB values
        Color Fc(byte r2, byte g2, byte b2) =>
            new Color((byte)(r2 * fade), (byte)(g2 * fade), (byte)(b2 * fade));
        // Helper: pre-fade the entity's own base colour (optional dim factor)
        Color Fb(float dim = 1f) =>
            new Color((byte)(bc.R * fade * dim), (byte)(bc.G * fade * dim), (byte)(bc.B * fade * dim));

        var steel = Fc(140, 145, 158);
        var dark  = Fc(70,  70,  70);

        if (isBoss)
        {
            if (relY < 0.12f) return (isL || isR ? '(' : '\x0F',  Fc(255,  60,  60)); // crown ☼
            if (relY < 0.28f) return (isL ? '[' : isR ? ']' : '\x01', Fc(220,  60,  60)); // head
            if (relY < 0.52f) return (isL ? '▐' : isR ? '▌' : '█', Fc(180,  40,  40)); // armoured chest
            if (relY < 0.68f) return (isL ? '\\' : isR ? '/' : '╪', Fc(130,  50,  50)); // belt
            if (relY < 0.86f) return (isL ? '/' : isR ? '\\' : '|', Fc(100,  40,  40)); // legs
            return ('_', Fc(80, 30, 30));
        }
        if (isUndead)
        {
            if (relY < 0.18f) return (isL ? '(' : isR ? ')' : '\x01', Fc( 90, 200,  70)); // skull
            if (relY < 0.35f) return (isL ? '|' : isR ? '|' : '±',   Fc( 70, 170,  55)); // ribcage
            if (relY < 0.60f) return (isL ? '|' : isR ? '|' : 'H',   Fc( 60, 150,  45)); // spine
            if (relY < 0.80f) return ('!', Fc(50, 120, 40));                               // ragged legs
            return ('_', Fc(40, 100, 30));
        }
        if (isBeast)
        {
            if (relY < 0.20f) return (isL ? '/' : isR ? '\\' : 'v', Fb());        // ears/snout
            if (relY < 0.45f) return (isL ? '/' : isR ? '\\' : '\x01', Fb());     // head
            if (relY < 0.72f) return (isL ? '|' : isR ? '|'  : '#', Fb(0.80f));   // body
            return (isL ? '/' : isR ? '\\' : 'w', Fb(0.65f));                      // paws
        }
        if (isMage)
        {
            if (relY < 0.14f) return (isL || isR ? ' ' : '*', Fc(110, 110, 255));          // orb
            if (relY < 0.30f) return (isL ? '(' : isR ? ')' : '\x01', Fc(110, 110, 255));  // cowled head
            if (relY < 0.55f) return (isL ? '(' : isR ? ')' : '|',   Fc( 80,  80, 200));  // upper robe
            if (relY < 0.78f) return (isL ? '(' : isR ? ')' : '|',   Fc( 60,  60, 170));  // lower robe
            return (isL ? '\\' : isR ? '/' : '~', Fc(50, 50, 140));                        // hem
        }
        if (isRanged)
        {
            if (relY < 0.20f) return (isL ? '(' : isR ? ')' : '-', Fc(180, 140,  60)); // bow
            if (relY < 0.36f) return (isL ? '<' : isR ? '>' : '\x01', Fb());           // hooded head
            if (relY < 0.58f) return (isL ? '|' : isR ? '|' : 'H',   Fb());           // torso
            if (relY < 0.80f) return (isL ? '/' : isR ? '\\' : '|',  Fb(0.8f));       // legs
            return (isL ? '/' : isR ? '\\' : '_', Fb(0.65f));
        }
        // Default: armoured humanoid warrior
        if (relY < 0.16f) return (isR ? '/' : ' ', Fc(200, 190, 80));              // sword raised right
        if (relY < 0.30f) return (isL ? '[' : isR ? ']' : '\x01', Fb());           // helmeted head
        if (relY < 0.52f) return (isL ? '[' : isR ? ']' : '█', steel);             // armoured chest
        if (relY < 0.68f) return (isL ? '\\' : isR ? '/' : '═', Fc(110, 115, 125)); // belt
        if (relY < 0.86f) return (isL ? '[' : isR ? ']' : '|', dark);              // greaves
        return (isL ? '/' : isR ? '\\' : '_', dark);                               // boots
    }

    // ═════════════════════════════════════════════════════════════════════════
    // INCOMING SHOT FLASH  (enemy fires back at the player)
    // ═════════════════════════════════════════════════════════════════════════
    private void DrawIncomingShotFlash(int viewW, int viewH)
    {
        if (_incomingShotTimer <= 0) return;
        var pos = GameEngine.Instance.EntityManager
            .GetComponent<PositionComponent>(GameEngine.Instance.PlayerEntity);
        if (pos == null) return;

        float t    = (float)(_incomingShotTimer / 0.30);
        int   crX  = MapW / 2, crY = MapH / 2;

        double posX  = pos.X + 0.5, posY = pos.Y + 0.5;
        double dx    = _incomingShotFromX + 0.5 - posX;
        double dy    = _incomingShotFromY + 0.5 - posY;
        double dirX  = Math.Cos(_fpAngle), dirY  = Math.Sin(_fpAngle);
        double planX = -Math.Sin(_fpAngle) * 0.66;
        double planY =  Math.Cos(_fpAngle) * 0.66;
        double invDet = 1.0 / (planX * dirY - dirX * planY);
        double txD    = invDet * ( dirY * dx - dirX * dy);
        double txH    = invDet * (-planY * dx + planX * dy);

        if (txD > 0.1)
        {
            // Enemy is in front — draw incoming bolt from their screen column to crosshair
            int srcCol = Math.Clamp((int)((viewW * 0.5) * (1.0 + txH / txD)) + 1, 1, MapW - 2);
            int step   = srcCol < crX ? 1 : -1;
            for (int sx = srcCol; sx != crX; sx += step)
            {
                if (sx < 1 || sx >= MapW - 1) break;
                float frac = 1f - (float)Math.Abs(sx - srcCol) /
                    Math.Max(1, (float)Math.Abs(crX - srcCol));
                byte tc = (byte)(200 * frac * t);
                _mapPanel.SetGlyph(sx, crY, '·', new Color(tc, (byte)(tc * 0.25f), 0), Color.Black);
            }
        }

        // Red impact splash at crosshair (visible regardless of facing)
        if (t > 0.45f)
        {
            byte hr = (byte)(200 * t);
            _mapPanel.SetGlyph(crX, crY, 'X', new Color(hr, 0, 0), Color.Black);
            for (int ry = -2; ry <= 2; ry++)
            for (int rx = -4; rx <= 4; rx++)
            {
                float dd = MathF.Sqrt(rx * rx * 0.25f + ry * ry);
                if (dd < 1.2f || dd > 2.6f) continue;
                int ssx = crX + rx, ssy = crY + ry;
                if (ssx >= 1 && ssx < MapW - 1 && ssy >= 1 && ssy < MapH - 1)
                    _mapPanel.SetGlyph(ssx, ssy, '*',
                        new Color((byte)(hr * 0.65f), 0, 0), Color.Black);
            }
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // MELEE HIT FLASH  (red vignette border when hit by an enemy)
    // ═════════════════════════════════════════════════════════════════════════
    private void DrawMeleeHitFlash(int viewW, int viewH)
    {
        if (_meleeHitTimer <= 0) return;
        float t = (float)Math.Min(1.0, _meleeHitTimer / 0.5);
        byte r = (byte)(175 * t), d = (byte)(r >> 1);
        // Two-pixel-wide red border around the whole FP view
        for (int vx = 1; vx <= viewW; vx++)
        {
            _mapPanel.SetBackground(vx, 1,          new Color(r, 0, 0));
            _mapPanel.SetBackground(vx, 2,          new Color(d, 0, 0));
            _mapPanel.SetBackground(vx, viewH,      new Color(r, 0, 0));
            _mapPanel.SetBackground(vx, viewH - 1,  new Color(d, 0, 0));
        }
        for (int vy = 1; vy <= viewH; vy++)
        {
            _mapPanel.SetBackground(1,          vy, new Color(r, 0, 0));
            _mapPanel.SetBackground(2,          vy, new Color(d, 0, 0));
            _mapPanel.SetBackground(viewW,      vy, new Color(r, 0, 0));
            _mapPanel.SetBackground(viewW - 1,  vy, new Color(d, 0, 0));
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // STATUS EFFECT VIGNETTE
    // ═════════════════════════════════════════════════════════════════════════
    private void DrawStatusEffectVignette(int viewW, int viewH)
    {
        var em   = GameEngine.Instance.EntityManager;
        var sfx  = em.GetComponent<StatusEffectComponent>(GameEngine.Instance.PlayerEntity);
        if (sfx == null || sfx.Effects.Count == 0) return;

        Color vigClr = Color.Transparent;
        float vigA   = 0f;
        foreach (var eff in sfx.Effects)
        {
            float pulse = (float)(0.4 + 0.6 * Math.Sin(_glowTime * 4.0));
            (Color c, float a) = eff.Type switch
            {
                EffectType.Poisoned
                or EffectType.Poisoning => (new Color(30, 160, 30),  0.55f * pulse),
                EffectType.Burning      => (new Color(210, 80,  20), 0.65f * pulse),
                EffectType.Frozen       => (new Color(60,  120, 220), 0.55f),
                EffectType.Stunned      => (new Color(200, 200, 200), 0.45f * pulse),
                EffectType.Blessed      => (new Color(220, 200, 60),  0.32f),
                EffectType.Cursed       => (new Color(150, 30,  150), 0.50f * pulse),
                EffectType.Slowed       => (new Color(40,  40,  180), 0.40f * pulse),
                _                       => (Color.Transparent, 0f),
            };
            if (a > vigA) { vigA = a; vigClr = c; }
        }
        if (vigA < 0.04f) return;

        byte vr = (byte)(vigClr.R * vigA), vg = (byte)(vigClr.G * vigA), vb = (byte)(vigClr.B * vigA);
        byte vr2 = (byte)(vr >> 1), vg2 = (byte)(vg >> 1), vb2 = (byte)(vb >> 1);
        for (int vx = 1; vx <= viewW; vx++)
        {
            _mapPanel.SetBackground(vx, 1,          new Color(vr,  vg,  vb));
            _mapPanel.SetBackground(vx, 2,          new Color(vr2, vg2, vb2));
            _mapPanel.SetBackground(vx, viewH,      new Color(vr,  vg,  vb));
            _mapPanel.SetBackground(vx, viewH - 1,  new Color(vr2, vg2, vb2));
        }
        for (int vy = 1; vy <= viewH; vy++)
        {
            _mapPanel.SetBackground(1,          vy, new Color(vr,  vg,  vb));
            _mapPanel.SetBackground(2,          vy, new Color(vr2, vg2, vb2));
            _mapPanel.SetBackground(viewW,      vy, new Color(vr,  vg,  vb));
            _mapPanel.SetBackground(viewW - 1,  vy, new Color(vr2, vg2, vb2));
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // BOSS HP BAR
    // ═════════════════════════════════════════════════════════════════════════
    private void DrawBossHpBar(int viewW)
    {
        var em        = GameEngine.Instance.EntityManager;
        var playerPos = em.GetComponent<PositionComponent>(GameEngine.Instance.PlayerEntity);
        if (playerPos == null) return;

        Entity bossE  = Entity.None;
        double bestD  = double.MaxValue;
        var    curMap = GameEngine.Instance.CurrentMap;
        if (curMap == null) return;

        foreach (var e in em.GetEntitiesWith<AIComponent, FighterComponent, PositionComponent>())
        {
            var ai = em.GetComponent<AIComponent>(e)!;
            if (ai.Behavior != AIBehavior.Boss) continue;
            var bp = em.GetComponent<PositionComponent>(e)!;
            if (!curMap.GetTile(bp.X, bp.Y).IsVisible) continue;
            double d = Math.Sqrt(Math.Pow(bp.X - playerPos.X, 2) + Math.Pow(bp.Y - playerPos.Y, 2));
            if (d < bestD) { bestD = d; bossE = e; }
        }
        if (!bossE.IsValid) return;

        var f    = em.GetComponent<FighterComponent>(bossE)!;
        var nm   = em.GetComponent<NameComponent>(bossE)?.Name?.ToUpperInvariant() ?? "BOSS";
        float pc = f.MaxHpTotal > 0 ? (float)f.Hp / f.MaxHpTotal : 0f;
        float pu = (float)(0.70 + 0.30 * Math.Sin(_glowTime * 2.5));

        int bx = 2, by2 = 2, bw = viewW - 2;
        string lbl = $" \x0F {nm}  HP {f.Hp}/{f.MaxHpTotal} ";
        _mapPanel.Print(bx, by2, lbl[..Math.Min(lbl.Length, bw)],
            new Color((byte)(255 * pu), 30, 30), new Color(20, 0, 0));

        int filled = (int)(bw * pc);
        for (int i = 0; i < bw; i++)
        {
            bool on = i < filled;
            char ch = on ? '█' : '░';
            var fg  = on  ? new Color((byte)(200 * pu), 20, 20)
                          : new Color(40, 20, 20);
            _mapPanel.SetGlyph(bx + i, by2 + 1, ch, fg, new Color(10, 0, 0));
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // CRT SCANLINES
    // ═════════════════════════════════════════════════════════════════════════
    private void DrawCrtScanlines(int viewH)
    {
        for (int sy = 2; sy <= viewH; sy += 2)
        {
            for (int sx = 1; sx < MapW - 1; sx++)
            {
                var cell = _mapPanel.GetCellAppearance(sx, sy);
                if (cell == null) continue;
                var bg = cell.Background;
                var fg = cell.Foreground;
                _mapPanel.SetBackground(sx, sy,
                    new Color((byte)(bg.R * 0.84f), (byte)(bg.G * 0.84f), (byte)(bg.B * 0.84f)));
                _mapPanel.SetForeground(sx, sy,
                    new Color((byte)(fg.R * 0.88f), (byte)(fg.G * 0.88f), (byte)(fg.B * 0.88f)));
            }
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // DEATH DISSOLVE PARTICLES
    // ═════════════════════════════════════════════════════════════════════════
    private static readonly char[] _deathChars = { '*', '+', '·', '×', '░', '▒', '%', '$' };
    private static readonly Random _dpRng = new();

    private void SpawnDeathParticles(double wx, double wy)
    {
        for (int i = 0; i < 14; i++)
        {
            float angle = (float)(_dpRng.NextDouble() * Math.PI * 2);
            float speed = 0.4f + (float)_dpRng.NextDouble() * 1.4f;
            _deathParts.Add(new DeathPart
            {
                WX    = wx, WY    = wy,
                VX    = MathF.Cos(angle) * speed,
                VY    = MathF.Sin(angle) * speed,
                Glyph = _deathChars[_dpRng.Next(_deathChars.Length)],
                Color = new Color(
                    (byte)_dpRng.Next(120, 220),
                    (byte)_dpRng.Next(20,  80),
                    (byte)_dpRng.Next(0,   30)),
                Life  = 1.0f,
            });
        }
    }

    private void DrawDeathParticles(double posX, double posY,
        double dirX, double dirY, double planX, double planY, int viewW, int viewH)
    {
        if (_deathParts.Count == 0) return;
        double invDet = 1.0 / (planX * dirY - dirX * planY);
        int halfH = viewH / 2;
        foreach (var dp in _deathParts)
        {
            double dx  = dp.WX - posX, dy = dp.WY - posY;
            double txD = invDet * ( dirY * dx - dirX * dy);
            double txH = invDet * (-planY * dx + planX * dy);
            if (txD < 0.2) continue;
            int sc = (int)((viewW * 0.5) * (1.0 + txH / txD));
            int sr = halfH + 1 + (int)(viewH * 0.0 / txD);
            if (sc < 0 || sc >= viewW || sr < 1 || sr >= MapH - 1) continue;
            if (txD >= _zBuffer[sc]) continue;
            float fade = (float)Math.Max(0.1, 1.0 - txD / 12.0);
            byte  pr   = (byte)(dp.Color.R * dp.Life * fade);
            byte  pg   = (byte)(dp.Color.G * dp.Life * fade);
            byte  pb   = (byte)(dp.Color.B * dp.Life * fade);
            _mapPanel.SetGlyph(sc + 1, sr, dp.Glyph, new Color(pr, pg, pb), Color.Black);
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // PROJECTILE TRAIL
    // ═════════════════════════════════════════════════════════════════════════
    private void DrawProjectileTrail(double posX, double posY,
        double dirX, double dirY, double planX, double planY, int viewW, int viewH)
    {
        if (!_projActive) return;
        double dx  = _projWX - posX, dy = _projWY - posY;
        double invDet = 1.0 / (planX * dirY - dirX * planY);
        double txD = invDet * ( dirY * dx - dirX * dy);
        double txH = invDet * (-planY * dx + planX * dy);
        if (txD < 0.2) return;
        int col = (int)((viewW * 0.5) * (1.0 + txH / txD));
        if (col < 0 || col >= viewW) return;
        if (txD >= _zBuffer[col]) return;
        int row = viewH / 2;
        byte br = (byte)(255 * _projLife), bg = (byte)(200 * _projLife), bb = (byte)(60 * _projLife);
        _mapPanel.SetGlyph(col + 1, row, '●', new Color(br, bg, bb), Color.Black);
        // Short tail — draw 3 cells behind along screen column
        for (int t = 1; t <= 3; t++)
        {
            float ta = _projLife * (1f - t * 0.3f);
            if (ta <= 0) break;
            byte tc = (byte)(180 * ta);
            if (col + 1 - t >= 1)
                _mapPanel.SetGlyph(col + 1 - t, row, '·', new Color(tc, (byte)(tc * 0.6f), 0), Color.Black);
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // WEAPON VIEW-MODEL  (bottom-right: ranged/magic weapon held in hand)
    // ═════════════════════════════════════════════════════════════════════════
    private void DrawWeaponViewModel()
    {
        var em    = GameEngine.Instance.EntityManager;
        var equip = em.GetComponent<EquipmentSlotComponent>(GameEngine.Instance.PlayerEntity);
        if (equip?.Weapon == null) return;

        var wName = equip.Weapon.Name.ToLowerInvariant();
        bool isRanged = wName.Contains("bow") || wName.Contains("crossbow")
                     || wName.Contains("gun") || wName.Contains("sling");
        bool isMagic  = wName.Contains("staff") || wName.Contains("wand") || wName.Contains("rod");
        if (!isRanged && !isMagic) return;   // melee shown in player body

        float flash = _shotTimer > 0 ? (float)(_shotTimer / 0.18) : 0f;

        // Anchor bottom-right, beside the player body
        int bx = MapW / 2 + 10, by = MapH - 3;

        void W(int dx, int dy, char g, Color fg)
        {
            int sx = bx + dx, sy = by + dy;
            if (sx >= 1 && sx < MapW - 1 && sy >= 1 && sy < MapH - 1)
                _mapPanel.SetGlyph(sx, sy, g, fg, Color.Black);
        }

        if (isRanged)
        {
            var bowClr = new Color(155, 115, 55);
            var strClr = new Color(210, 195, 165);
            var hand   = new Color(120, 90, 55);
            // Bow limbs — vertical arc
            W(0, -6, '(', bowClr); W(0, -5, '|', bowClr);
            W(0, -4, '<', bowClr); W(0, -3, '|', bowClr); W(0, -2, '(', bowClr);
            // Hand on grip
            W(1, -4, ')', hand); W(1, -3, '|', hand); W(1, -2, ')', hand);
            // Arrow nocked across bow
            W(-3, -4, '─', strClr); W(-2, -4, '─', strClr); W(-1, -4, '>', strClr);
            // Muzzle flash / release spark
            if (flash > 0)
            {
                W(-4, -4, '~', new Color((byte)(255*flash), (byte)(180*flash), 0));
                W(-5, -4, '≡', new Color((byte)(200*flash), (byte)(120*flash), 0));
                W(-6, -4, '·', new Color((byte)(150*flash), (byte)(80*flash),  0));
            }
        }
        else  // magic staff
        {
            var stfClr = new Color(140, 108, 58);
            var orbClr = new Color(
                (byte)Math.Min(255, 80  + (int)(175 * flash)),
                (byte)Math.Min(255, 70  + (int)(130 * flash)),
                255);
            // Orb tip
            W(0, -7, '*', orbClr);
            W(-1,-7, '·', orbClr); W(1, -7, '·', orbClr);
            // Shaft diagonal
            W(0, -6, '|', stfClr); W(0, -5, '|', stfClr);
            W(-1,-4, '/', stfClr); W(0, -4, '|', stfClr);
            W(-2,-3, '/', stfClr); W(-3, -2, '/', stfClr);
            // Spell burst on fire
            if (flash > 0)
            {
                W(-1,-8, '*', new Color((byte)(255*flash), (byte)(80*flash),  (byte)(255*flash)));
                W( 1,-8, '*', new Color((byte)(180*flash), (byte)(50*flash),  (byte)(255*flash)));
                W( 0,-8, '☼', new Color((byte)(255*flash), (byte)(200*flash), (byte)(255*flash)));
            }
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // INVENTORY OVERLAY
    // ═════════════════════════════════════════════════════════════════════════
    private void DrawInventoryOverlay()
    {
        var em     = GameEngine.Instance.EntityManager;
        var player = GameEngine.Instance.PlayerEntity;
        var inv    = em.GetComponent<InventoryComponent>(player);
        var equip  = em.GetComponent<EquipmentSlotComponent>(player);
        var fighter= em.GetComponent<FighterComponent>(player);

        const int OX = 1, OY = 1;
        const int IW = MapW - 2;  // 57
        const int IH = MapH - 2;  // 35
        var ovBg   = new Color(5, 7, 16);
        var borClr = new Color(75, 75, 125);
        int divX   = OX + 27;     // left panel uses cols OX..divX-1
        int detX   = divX + 1;    // right panel starts here

        // ── Local helpers ──────────────────────────────────────────────────
        void B(int x, int y, char g)
        {
            if (x >= 0 && x < MapW && y >= 0 && y < MapH)
                _mapPanel.SetGlyph(x, y, g, borClr, ovBg);
        }
        void HLine(int y2, int x1, int x2)
        {
            for (int x = x1; x <= x2; x++)
                if (y2 >= 0 && y2 < MapH) _mapPanel.SetGlyph(x, y2, '─', new Color(48,48,80), ovBg);
        }
        void Row(int y2, string label, string val, Color valClr)
        {
            if (y2 < 0 || y2 >= MapH) return;
            _mapPanel.Print(detX,      y2, label + ":", new Color(110,110,145), ovBg);
            _mapPanel.Print(detX + 9,  y2, val,         valClr,                 ovBg);
        }
        static string Tr(string s, int max) => s.Length > max ? s[..max] : s;
        bool IsEq(InventoryEntry it) =>
            equip != null && (equip.Weapon?.ItemId  == it.ItemId ||
                              equip.Armor?.ItemId   == it.ItemId ||
                              equip.Shield?.ItemId  == it.ItemId ||
                              equip.Ring?.ItemId    == it.ItemId ||
                              equip.Amulet?.ItemId  == it.ItemId);

        // ── Background + borders ───────────────────────────────────────────
        for (int y = OY; y < OY + IH; y++)
        for (int x = OX; x < OX + IW; x++)
            _mapPanel.SetGlyph(x, y, ' ', Color.Black, ovBg);

        B(OX, OY, '╔'); B(OX+IW-1, OY, '╗');
        B(OX, OY+IH-1, '╚'); B(OX+IW-1, OY+IH-1, '╝');
        for (int x = OX+1; x < OX+IW-1; x++) { B(x, OY, '═'); B(x, OY+IH-1, '═'); }
        for (int y = OY+1; y < OY+IH-1; y++) { B(OX, y, '║'); B(OX+IW-1, y, '║'); }

        const string title = "═ INVENTORY ═";
        _mapPanel.Print(OX + (IW - title.Length) / 2, OY, title, new Color(200,200,255), ovBg);

        // Vertical divider between list and detail panels
        B(divX, OY, '╦'); B(divX, OY+IH-1, '╩');
        for (int y = OY+1; y < OY+IH-1; y++) B(divX, y, '║');

        // ── LEFT PANEL: Item list ──────────────────────────────────────────
        int listX   = OX + 1;
        int listTop = OY + 2;
        int listH   = IH - 5;
        int items   = inv?.Items.Count ?? 0;
        if (items > 0 && _invSelectedIdx >= items) _invSelectedIdx = items - 1;
        if (_invSelectedIdx < 0) _invSelectedIdx = 0;

        _mapPanel.Print(listX, OY+1, " #  Item               Qt",
            new Color(100,100,145), ovBg);

        int scrollOff = Math.Max(0, _invSelectedIdx - listH / 2);
        for (int i = scrollOff; i < items && (i - scrollOff) < listH; i++)
        {
            int  ry    = listTop + i - scrollOff;
            if (ry >= OY+IH-2) break;
            bool sel   = i == _invSelectedIdx;
            var  item  = inv!.Items[i];
            var  rowBg = sel ? new Color(22, 22, 50) : ovBg;
            var  iClr  = ItemClr(item.Category);
            var  nmClr = sel ? Color.White : iClr;

            for (int x = listX; x < divX; x++) _mapPanel.SetGlyph(x, ry, ' ', Color.Black, rowBg);

            _mapPanel.Print(listX,     ry, sel ? ">" : " ", sel ? new Color(255,255,80) : new Color(25,25,25), rowBg);
            _mapPanel.Print(listX + 1, ry, $"{i+1,2}", new Color(65,65,95), rowBg);
            _mapPanel.SetGlyph(listX + 4, ry, item.Glyph, iClr, rowBg);
            _mapPanel.Print(listX + 6, ry, Tr(item.Name, 14), nmClr, rowBg);
            _mapPanel.Print(divX  - 5, ry, item.Count > 1 ? $"x{item.Count,2}" : " x1", new Color(80,80,80), rowBg);
            if (IsEq(item)) _mapPanel.Print(divX - 8, ry, "EQ", new Color(75,215,75), rowBg);
        }

        if (items == 0)
            _mapPanel.Print(listX + 2, listTop, "(empty)", new Color(55,55,80), ovBg);

        // ── RIGHT PANEL: Item details ──────────────────────────────────────
        int dy = OY + 1;
        if (inv != null && items > 0 && _invSelectedIdx < items)
        {
            var item = inv.Items[_invSelectedIdx];
            var iClr = ItemClr(item.Category);

            // Title row: glyph + name
            _mapPanel.SetGlyph(detX, dy, item.Glyph, item.Color, ovBg);
            _mapPanel.Print(detX + 2, dy++, Tr(item.Name, IW - 33), iClr, ovBg);
            dy++;
            HLine(dy++, detX, OX+IW-2);

            Row(dy++, "Type",  item.Category,              new Color(150,150,200));
            if (item.Count > 1) Row(dy++, "Stack", $"x{item.Count}",    new Color(140,140,140));
            if (item.Value > 0) Row(dy++, "Value", $"{item.Value} gold", new Color(200,200, 80));

            if (!string.IsNullOrEmpty(item.UseEffect))
            {
                dy++;
                HLine(dy++, detX, OX+IW-2);
                _mapPanel.Print(detX, dy++, "USE EFFECT:", new Color(90, 210, 90), ovBg);
                _mapPanel.Print(detX+2, dy++,
                    Tr(item.UseEffect.Replace(",", " | "), IW - 33), new Color(70, 185, 70), ovBg);
            }

            bool hasStats = item.BonusDamage  != 0 || item.BonusDefense != 0 ||
                            item.BonusMaxHp   != 0 || item.BonusMaxMana  != 0;
            if (hasStats && dy < OY+IH-9)
            {
                dy++;
                HLine(dy++, detX, OX+IW-2);
                _mapPanel.Print(detX, dy++, "STATS:", new Color(175,175,95), ovBg);
                if (item.BonusDamage  != 0) Row(dy++, "Damage",  $"+{item.BonusDamage}",  new Color(220,150, 80));
                if (item.BonusDefense != 0) Row(dy++, "Defense", $"+{item.BonusDefense}", new Color(100,180,220));
                if (item.BonusMaxHp   != 0) Row(dy++, "Max HP",  $"+{item.BonusMaxHp}",  new Color(100,215,100));
                if (item.BonusMaxMana != 0) Row(dy++, "Max MP",  $"+{item.BonusMaxMana}", new Color(100,145,255));

                // Comparison vs currently equipped
                if (fighter != null && dy < OY+IH-5 && item.BonusDamage != 0)
                {
                    int diff = item.BonusDamage - (equip?.Weapon?.BonusDamage ?? 0);
                    var dc = diff > 0 ? new Color(80,215,80) : diff < 0 ? new Color(215,80,80) : new Color(100,100,100);
                    _mapPanel.Print(detX+2, dy++,
                        $"(vs equipped: {(diff >= 0 ? "+" : "")}{diff})", dc, ovBg);
                }
            }

            // Equipped loadout summary
            if (equip != null && dy < OY+IH-5)
            {
                dy++;
                HLine(dy++, detX, OX+IW-2);
                _mapPanel.Print(detX, dy++, "EQUIPPED:", new Color(130,130,150), ovBg);
                if (equip.Weapon != null && dy < OY+IH-3)
                    _mapPanel.Print(detX+2, dy++, "W: " + Tr(equip.Weapon.Name, IW-37),
                        new Color(200,200, 80), ovBg);
                if (equip.Armor  != null && dy < OY+IH-3)
                    _mapPanel.Print(detX+2, dy++, "A: " + Tr(equip.Armor.Name, IW-37),
                        new Color(100,180,200), ovBg);
                if (equip.Shield != null && dy < OY+IH-3)
                    _mapPanel.Print(detX+2, dy++, "S: " + Tr(equip.Shield.Name, IW-37),
                        new Color(100,180,200), ovBg);
            }
        }
        else if (items == 0)
        {
            _mapPanel.Print(detX + 2, OY + 4, "No items.", new Color(55,55,80), ovBg);
        }

        // ── Action bar ─────────────────────────────────────────────────────
        int actionY = OY + IH - 3;
        HLine(actionY++, OX+1, OX+IW-2);
        bool canEquip = items > 0 && _invSelectedIdx < items &&
            inv!.Items[_invSelectedIdx].Category is "Weapon" or "Armor" or "Shield" or "Ring" or "Amulet";
        bool canUse = items > 0 && _invSelectedIdx < items &&
            !string.IsNullOrEmpty(inv!.Items[_invSelectedIdx].UseEffect);
        string actStr = (canEquip ? "[E]quip  " : "")
                      + (canUse   ? "[U]se  "   : "")
                      + (items > 0 ? "[D]rop  "  : "")
                      + "[I/Esc] Close";
        _mapPanel.Print(OX + Math.Max(1, (IW - actStr.Length) / 2), actionY,
            actStr, new Color(175,175,215), ovBg);
        _mapPanel.Print(OX + 2, actionY + 1,
            "W/S or ↑↓ navigate", new Color(70,70,105), ovBg);
    }

    private static Color ItemClr(string category) => category switch
    {
        "Weapon"              => new Color(200, 200, 100),
        "Armor" or "Shield"   => new Color(100, 180, 200),
        "Ring"  or "Amulet"   => new Color(200, 100, 200),
        "Food"                => new Color(200, 120,  80),
        "Consumable"          => new Color(160, 100, 200),
        _                     => new Color(160, 160, 160),
    };

    // ═════════════════════════════════════════════════════════════════════════
    // SPRITE PROJECTION
    // ═════════════════════════════════════════════════════════════════════════

    private readonly record struct SpriteInfo(
        double DistSq, double WX, double WY,
        char TopG, char BodyG, char BotG,
        Color Clr, float HMul, float WMul,
        Entity EntityId,   // Entity.None if tile sprite
        int VertOff = 0    // positive = shift down (floor items)
    );

    private void RenderFpSprites(
        GameMap map, PositionComponent playerPos,
        double posX, double posY,
        double dirX, double dirY, double planX, double planY,
        int viewW, int viewH, int halfH)
    {
        var em = GameEngine.Instance.EntityManager;
        var sprites = new List<SpriteInfo>(32);

        // ── 1. Entity sprites ──────────────────────────────────────────────
        foreach (var e in em.GetEntitiesWith<RenderComponent, PositionComponent>())
        {
            if (e == GameEngine.Instance.PlayerEntity) continue;
            var p = em.GetComponent<PositionComponent>(e)!;
            var r = em.GetComponent<RenderComponent>(e)!;
            if (!r.IsVisible) continue;
            if (!map.GetTile(p.X, p.Y).IsVisible) continue;

            double dx = p.X + 0.5 - posX, dy = p.Y + 0.5 - posY;
            bool isEnemy   = em.GetComponent<AIComponent>(e) != null;
            var  feat      = em.GetComponent<FeatureComponent>(e);

            if (isEnemy)
            {
                var (th, tb, tl, hClr) = GetEnemySpriteStyle(e);
                int bobV = (int)(Math.Sin(_glowTime * 2.5 + p.X * 0.7 + p.Y * 1.3) * 1.5);
                sprites.Add(new SpriteInfo(dx * dx + dy * dy, p.X + 0.5, p.Y + 0.5,
                    th, tb, tl, hClr, 1.4f, 0.65f, e, bobV));
            }
            else if (feat != null)
            {
                (char tg, char bg2, char btg, float hm, float wm) = feat.Type switch {
                    FeatureType.Chest      => ('▄', '░', '_', 0.55f, 0.70f),
                    FeatureType.Trap       => ('▲', '▲', '.', 0.22f, 0.45f),
                    FeatureType.StairsDown => ('▓', '▒', '░', 0.52f, 0.62f),
                    FeatureType.StairsUp   => ('░', '▒', '▓', 0.52f, 0.62f),
                    FeatureType.Door       => ('║', '█', '─', 1.05f, 0.38f),
                    _                      => (r.Glyph, r.Glyph, r.Glyph, 0.5f, 0.5f),
                };
                var featClr = feat.Type switch {
                    FeatureType.Door       => new Color(180, 110,  50),
                    FeatureType.Chest      => new Color(200, 160,  50),
                    FeatureType.Trap       => new Color(220,  50,  50),
                    FeatureType.StairsDown => new Color(140, 135, 155),
                    FeatureType.StairsUp   => new Color(140, 135, 155),
                    _                      => r.Foreground,
                };
                sprites.Add(new SpriteInfo(dx * dx + dy * dy, p.X + 0.5, p.Y + 0.5,
                    tg, bg2, btg, featClr, hm, wm, Entity.None));
            }
            else if (em.GetComponent<ItemComponent>(e) != null)
            {
                // Floor item: small, shifted below horizon to sit on the ground
                sprites.Add(new SpriteInfo(dx * dx + dy * dy, p.X + 0.5, p.Y + 0.5,
                    r.Glyph, r.Glyph, r.Glyph, r.Foreground, 0.30f, 0.28f, e, halfH / 4));
            }
            else
            {
                sprites.Add(new SpriteInfo(dx * dx + dy * dy, p.X + 0.5, p.Y + 0.5,
                    r.Glyph, r.Glyph, r.Glyph, r.Foreground, 0.42f, 0.45f, Entity.None));
            }
        }

        // ── 2. Feature tiles (Rock, Bush, Water) ───────────────────────────
        const int ScanR = 14;
        for (int dy = -ScanR; dy <= ScanR; dy++)
        for (int dx = -ScanR; dx <= ScanR; dx++)
        {
            int wx = playerPos.X + dx, wy = playerPos.Y + dy;
            var tile = map.GetTile(wx, wy);
            if (!tile.IsVisible) continue;
            if (tile.Type is not (TileType.Rock or TileType.Bush or TileType.Water)) continue;
            double ddx = wx + 0.5 - posX, ddy = wy + 0.5 - posY;
            (char topG, char midG, char botG, Color col, float hm, float wm) = tile.Type switch {
                // Rock: boulder — shaded cube top/side/base
                TileType.Rock  => ('▓', '▒', '▄', new Color(165, 155, 135), 0.48f, 0.60f),
                // Bush/Tree: canopy triangle ▲ + trunk ║ + ground base ▄
                TileType.Bush  => ('▲', '║', '▄', new Color( 55, 145,  55), 0.88f, 0.40f),
                // Water: animated ripple glyphs
                TileType.Water => ('≈', '~', '~', new Color( 65, 130, 210), 0.18f, 1.00f),
                _              => ('.', '.', '.', Color.White,               0.30f, 0.50f),
            };
            sprites.Add(new SpriteInfo(ddx * ddx + ddy * ddy, wx + 0.5, wy + 0.5,
                topG, midG, botG, col, hm, wm, Entity.None));
        }

        sprites.Sort((a, b) => b.DistSq.CompareTo(a.DistSq));

        double invDet = 1.0 / (planX * dirY - dirX * planY);

        foreach (var sp in sprites)
        {
            double dx = sp.WX - posX, dy = sp.WY - posY;
            double txD = invDet * ( dirY * dx  -  dirX * dy);
            double txH = invDet * (-planY * dx + planX * dy);
            if (txD <= 0.2) continue;

            int centCol = (int)((viewW * 0.5) * (1.0 + txH / txD));
            int sprH    = Math.Max(1, Math.Min(viewH, (int)(viewH * sp.HMul / txD)));
            int topY    = Math.Max(1, halfH + 1 - sprH / 2 + sp.VertOff);
            int botY    = Math.Min(viewH, halfH + 1 + sprH / 2 + sp.VertOff);
            int sprW    = Math.Max(1, (int)(sprH * sp.WMul));
            int leftC   = centCol - sprW / 2;
            int rightC  = centCol + sprW / 2;

            bool isEnemySprite = sp.EntityId.IsValid &&
                em.GetComponent<AIComponent>(sp.EntityId) != null;

            float fade = isEnemySprite
                ? (float)Math.Max(0.28, 1.0 - txD / 20.0)
                : (float)Math.Max(0.15, 1.0 - txD / 16.0);
            var spClr = new Color(
                (byte)(sp.Clr.R * fade), (byte)(sp.Clr.G * fade), (byte)(sp.Clr.B * fade));

            int totalRows = Math.Max(1, botY - topY);

            // HP bar for visible enemies at ≤8 tiles
            if (sp.EntityId.IsValid && txD <= 8.0)
            {
                var eFighter = em.GetComponent<FighterComponent>(sp.EntityId);
                if (eFighter != null)
                    DrawEnemyHpBar(centCol, topY - 2, sprW, eFighter, txD);
            }

            for (int stripe = leftC; stripe <= rightC; stripe++)
            {
                if (stripe < 0 || stripe >= viewW) continue;
                if (txD >= _zBuffer[stripe]) continue;
                int sx = stripe + 1;
                if (sx < 1 || sx >= MapW - 1) continue;

                float relX = sprW > 1 ? (float)(stripe - leftC) / (float)(sprW - 1) : 0.5f;

                for (int sy = topY; sy <= botY; sy++)
                {
                    if (sy < 1 || sy >= MapH - 1) continue;
                    float relY = (float)(sy - topY) / (float)totalRows;
                    char  dg; Color dc;
                    if (isEnemySprite)
                        (dg, dc) = GetDetailedEnemyGlyph(sp.EntityId, relX, relY, fade);
                    else
                    {
                        dg = relY < 0.28f ? sp.TopG : relY < 0.72f ? sp.BodyG : sp.BotG;
                        // Two-tone tree: green canopy ▲ / brown trunk ║
                        if (sp.TopG == '▲' && sp.BodyG == '║')
                            dc = relY < 0.48f
                                ? new Color((byte)(50 * fade), (byte)(148 * fade), (byte)(38 * fade))
                                : new Color((byte)(85 * fade), (byte)(56 * fade),  (byte)(22 * fade));
                        else
                            dc = spClr;
                    }
                    var spBg = new Color((byte)(dc.R >> 3), (byte)(dc.G >> 3), (byte)(dc.B >> 3));
                    _mapPanel.SetGlyph(sx, sy, dg, dc, spBg);
                }
            }
        }
    }

    // ── Enemy HP bar (drawn just above the sprite) ────────────────────────────
    private void DrawEnemyHpBar(int centCol, int barRow, int sprW, FighterComponent f, double dist)
    {
        if (barRow < 1 || barRow >= MapH - 1) return;
        int bw     = Math.Max(3, sprW);
        int filled = f.MaxHp > 0 ? (int)Math.Round((double)f.Hp / f.MaxHp * bw) : 0;
        filled     = Math.Clamp(filled, 0, bw);
        float fade = (float)Math.Max(0.3, 1.0 - dist / 10.0);

        for (int i = 0; i < bw; i++)
        {
            int sx = centCol - bw / 2 + i + 1;
            if (sx < 1 || sx >= MapW - 1) continue;
            var clr = i < filled
                ? new Color((byte)(40 * fade), (byte)(180 * fade), (byte)(40 * fade))
                : new Color((byte)(80 * fade), (byte)(20 * fade),  (byte)(20 * fade));
            _mapPanel.SetGlyph(sx, barRow, '─', clr, Color.Black);
        }
    }

    // ── Enemy sprite style per archetype ─────────────────────────────────────
    private static (char head, char body, char legs, Color headColor)
        GetEnemySpriteStyle(Entity entityId)
    {
        var em   = GameEngine.Instance.EntityManager;
        var r    = em.GetComponent<RenderComponent>(entityId)!;
        var ai   = em.GetComponent<AIComponent>(entityId);
        var name = em.GetComponent<NameComponent>(entityId)?.Name?.ToLowerInvariant() ?? "";

        bool isBoss   = ai?.Behavior == AIBehavior.Boss;
        bool isUndead = name.Contains("skeleton") || name.Contains("zombie")
                     || name.Contains("lich") || name.Contains("ghost") || name.Contains("wraith");
        bool isBeast  = name.Contains("wolf") || name.Contains("rat") || name.Contains("spider")
                     || name.Contains("bat")  || name.Contains("slime");
        bool isMage   = name.Contains("mage") || name.Contains("wizard") || name.Contains("witch")
                     || name.Contains("sorceress") || name.Contains("sorcerer");
        bool isArcher = ai?.IsRanged == true;

        if (isBoss)
            return ('\x0F', r.Glyph, '\x0F', new Color(255, 60, 60));    // ☼ boss crown
        if (isUndead)
            return ('\x01', r.Glyph, '!',   new Color(100, 210, 80));    // green tint undead
        if (isBeast)
            return ('v',   r.Glyph, '.',    r.Foreground);               // crouched animal
        if (isMage)
            return ('\x01', r.Glyph, '~',   new Color(110, 110, 255));   // flowing robe hem
        if (isArcher)
            return ('\x01', r.Glyph, '/',   r.Foreground);               // bent-knee stance
        return ('\x01', r.Glyph, '|', r.Foreground);                     // default humanoid
    }

    // ── Mini-map ──────────────────────────────────────────────────────────────
    private void DrawFpMiniMap(GameMap map, PositionComponent pos)
    {
        const int MmW = 15, MmH = 9;
        int mxOff = MapW - MmW - 2, myOff = MapH - MmH - 2;
        int startWX = pos.X - MmW / 2, startWY = pos.Y - MmH / 2;

        for (int my = 0; my < MmH; my++)
        for (int mx = 0; mx < MmW; mx++)
        {
            int wx = startWX + mx, wy = startWY + my;
            int sx = mxOff + mx,   sy = myOff + my;
            if (sx < 1 || sx >= MapW - 1 || sy < 1 || sy >= MapH - 1) continue;
            var tile = map.GetTile(wx, wy);
            if (wx == pos.X && wy == pos.Y)
            { _mapPanel.SetGlyph(sx, sy, '\x02', new Color(255, 255, 80), Color.Black); continue; }
            if (!tile.IsExplored)
            { _mapPanel.SetGlyph(sx, sy, ' ', Color.Black, new Color(8, 8, 8)); continue; }
            (char ch, Color fg, Color bg) = tile.Type switch {
                TileType.Floor               => ('.', new Color(50, 70, 50),  Color.Black),
                TileType.Wall or TileType.Empty => (' ', Color.Black, new Color(22, 22, 22)),
                TileType.Door                => ('+', new Color(200, 160, 80), Color.Black),
                TileType.StairsDown          => ('>', new Color(200, 200, 255), Color.Black),
                TileType.StairsUp            => ('<', new Color(200, 200, 255), Color.Black),
                TileType.Water               => (' ', Color.Black, new Color(15, 50, 110)),
                TileType.Chest               => ('.', new Color(255, 200, 50), Color.Black),
                _                            => ('.', new Color(50, 70, 50),  Color.Black),
            };
            _mapPanel.SetGlyph(sx, sy, ch, fg, bg);
        }
        var bc = new Color(45, 50, 70);
        void S(int sx, int sy, char g)
        {
            if (sx >= 1 && sx < MapW-1 && sy >= 1 && sy < MapH-1)
                _mapPanel.SetGlyph(sx, sy, g, bc, Color.Black);
        }
        int bL=mxOff-1, bR=mxOff+MmW, bT=myOff-1, bB=myOff+MmH;
        S(bL,bT,'┌'); S(bR,bT,'┐'); S(bL,bB,'└'); S(bR,bB,'┘');
        for (int mx=mxOff; mx<mxOff+MmW; mx++) { S(mx,bT,'─'); S(mx,bB,'─'); }
        for (int my=myOff; my<myOff+MmH; my++) { S(bL,my,'│'); S(bR,my,'│'); }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // SIDEBAR — 3-D RPG HUD
    // ═════════════════════════════════════════════════════════════════════════
    private void RenderSidebar()
    {
        var em     = GameEngine.Instance.EntityManager;
        var player = GameEngine.Instance.PlayerEntity;

        for (int cy = 1; cy < TotalH - 1; cy++)
        for (int cx = 1; cx < SidebarW - 1; cx++)
            _sidebar.SetGlyph(cx, cy, ' ', Color.Black, PanelBg);

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

        Color clsClr = cls?.Class switch {
            PlayerClass.Warrior => new Color(220, 160,  60),
            PlayerClass.Rogue   => new Color(180, 100, 180),
            PlayerClass.Mage    => new Color( 80, 160, 220),
            _                   => new Color(180, 180, 180),
        };

        DrawChromeLine(y++, "╔", "═", "╗", ChromeClr);
        string clsName  = cls != null ? cls.ClassName : "Hero";
        int    nameOff  = (SidebarW - 2 - clsName.Length) / 2 + 1;
        _sidebar.Print(1, y, "║", ChromeClr, PanelBg);
        _sidebar.Print(nameOff, y, clsName, clsClr, PanelBg);
        _sidebar.Print(SidebarW - 2, y, "║", ChromeClr, PanelBg);
        y++;
        DrawChromeLine(y++, "╚", "═", "╝", ChromeClr);

        if (exp != null)
        {
            _sidebar.Print(1, y, $"Lv{exp.Level}", new Color(255, 210, 80), PanelBg);
            string xpStr = $"{exp.Experience}/{exp.NextLevelExp}XP";
            _sidebar.Print(SidebarW - 1 - xpStr.Length, y, xpStr, new Color(110, 110, 70), PanelBg);
            y++;
        }

        if (fighter != null)
        {
            var hpClr = fighter.Hp < fighter.MaxHp / 3   ? new Color(220, 50, 50)
                      : fighter.Hp < fighter.MaxHp * 2/3 ? new Color(220, 200, 50)
                      : new Color(60, 200, 80);
            _sidebar.Print(1, y, "HP", HpLblClr, PanelBg);
            _sidebar.Print(4, y++, $"{fighter.Hp}/{fighter.MaxHpTotal}", hpClr, PanelBg);
            DrawFillBar(y++, Math.Max(0, fighter.Hp), fighter.MaxHpTotal, hpClr, new Color(50, 8, 8));
        }
        if (mana != null)
        {
            _sidebar.Print(1, y, "MP", ManaClr, PanelBg);
            _sidebar.Print(4, y++, $"{mana.Mana}/{mana.MaxMana}", ManaClr, PanelBg);
            DrawFillBar(y++, mana.Mana, mana.MaxMana, ManaClr, new Color(8, 8, 50));
        }

        if (fxComp != null && fxComp.Effects.Count > 0)
        {
            y++;
            foreach (var fx in fxComp.Effects.Take(3))
            {
                var fxClr = fx.Type switch {
                    EffectType.Poisoned or EffectType.Poisoning => new Color(100, 200, 80),
                    EffectType.Burning   => new Color(255, 140, 0),
                    EffectType.Frozen    => new Color(100, 200, 240),
                    EffectType.Stunned   => new Color(180, 180, 180),
                    EffectType.Blessed   => new Color(255, 220, 80),
                    EffectType.Cursed    => new Color(160, 80, 200),
                    EffectType.Regenerating => Color.LightGreen,
                    _                    => new Color(160, 160, 160),
                };
                _sidebar.Print(1, y++, $" ~ {fx.Type} ({fx.Duration}t)", fxClr, PanelBg);
            }
        }

        y++;
        DrawChromeLine(y++, "┌", "─", "┐", DivClr);
        if (fighter != null)
        {
            StatRow("DMG",  fighter.DamageString,                ref y);
            StatRow("DEF",  fighter.EffectiveDefense.ToString(), ref y);
            StatRow("STR",  fighter.Strength.ToString(),         ref y);
        }
        if (status != null) StatRow("TURN", status.Turn.ToString(), ref y);
        DrawChromeLine(y++, "└", "─", "┘", DivClr);

        y++;
        SectionHeader(y++, "EQUIP", new Color(200, 200, 100));
        if (equip != null)
        {
            PrintEquipRow("W", equip.Weapon,  ref y);
            PrintEquipRow("A", equip.Armor,   ref y);
            PrintEquipRow("S", equip.Shield,  ref y);
            PrintEquipRow("R", equip.Ring,    ref y);
        }

        y++;
        SectionHeader(y++, "SKILL", new Color(180, 100, 220));
        if (abils != null)
        {
            for (int i = 0; i < abils.Abilities.Count && y < TotalH - 6; i++)
            {
                var ab = abils.Abilities[i];
                bool rdy = ab.IsReady;
                var abClr = rdy ? ab.Color : new Color(60, 60, 60);
                string cd  = rdy ? "rdy" : $"{ab.CurrentCooldown}t";
                string abbr = ab.Name.Length > 8 ? ab.Name[..8] : ab.Name.PadRight(8);
                _sidebar.Print(1, y, $"[{i+1}]", new Color(90, 90, 110), PanelBg);
                _sidebar.Print(5, y, abbr, abClr, PanelBg);
                _sidebar.Print(SidebarW - 4, y, cd[..Math.Min(3, cd.Length)],
                    rdy ? new Color(80, 180, 80) : new Color(180, 60, 60), PanelBg);
                y++;
            }
        }

        if (y < TotalH - 5)
        {
            y++;
            SectionHeader(y++, "BAG", new Color(160, 160, 180));
            if (inv != null)
            {
                for (int i = 0; i < Math.Min(inv.Items.Count, TotalH - y - 3); i++)
                {
                    var item  = inv.Items[i];
                    var iClr  = item.Category switch {
                        "Weapon"     => new Color(200, 200, 100),
                        "Food"       => new Color(200, 120,  80),
                        "Consumable" => new Color(160, 100, 200),
                        "Armor" or "Shield" or "Ring" or "Amulet" => new Color(100, 180, 200),
                        _            => new Color(160, 160, 160),
                    };
                    string lbl = $"{item.Glyph} {item.Name}";
                    if (lbl.Length > SidebarW - 5) lbl = lbl[..(SidebarW - 5)];
                    _sidebar.Print(1, y, lbl, iClr, PanelBg);
                    if (item.Count > 1) _sidebar.Print(SidebarW - 4, y, $"x{item.Count}", DivClr, PanelBg);
                    y++;
                }
            }
        }

        string hint = _inventoryOpen       ? "↑↓ nav  E=eq U=use D=drop"
                    : _equipMode           ? "# to equip/unequip"
                    : _abilityDirState > 0 ? $"Dir: ability {_abilityDirState}"
                    : _firstPersonMode     ? "I=inv  Mouse=aim  LClick=fire"
                    : "I=inv  E=equip  g=get";
        _sidebar.Print(1, TotalH - 2, hint[..Math.Min(hint.Length, SidebarW - 2)], DivClr, PanelBg);
    }

    // ── Sidebar helpers ───────────────────────────────────────────────────────
    private void DrawChromeLine(int y, string l, string m, string r, Color clr)
    {
        _sidebar.Print(1, y, l, clr, PanelBg);
        for (int cx = 2; cx < SidebarW - 2; cx++) _sidebar.Print(cx, y, m, clr, PanelBg);
        _sidebar.Print(SidebarW - 2, y, r, clr, PanelBg);
    }

    private void SectionHeader(int y, string label, Color clr)
    {
        _sidebar.Print(1, y, "┌─", ChromeClr, PanelBg);
        _sidebar.Print(3, y, label, clr, PanelBg);
        _sidebar.Print(3 + label.Length, y, "─┐", ChromeClr, PanelBg);
    }

    private void DrawFillBar(int y, int cur, int max, Color fillClr, Color emptyClr)
    {
        int bw = SidebarW - 3;
        int filled = max > 0 ? Math.Clamp((int)Math.Round((double)cur / max * bw), 0, bw) : 0;
        for (int i = 0; i < bw; i++)
            _sidebar.SetGlyph(1 + i, y,
                i < filled ? '█' : '░',
                i < filled ? fillClr : emptyClr, PanelBg);
    }

    private void StatRow(string label, string value, ref int y)
    {
        _sidebar.Print(1, y, label, LabelClr, PanelBg);
        _sidebar.Print(6, y, value, ValueClr, PanelBg);
        y++;
    }

    private void PrintEquipRow(string prefix, EquipmentEntry? entry, ref int y)
    {
        _sidebar.Print(1, y, prefix, DivClr, PanelBg);
        if (entry != null)
        {
            string nm = entry.Name.Length > SidebarW - 5 ? entry.Name[..(SidebarW - 5)] : entry.Name;
            _sidebar.Print(3, y, nm, entry.Color, PanelBg);
        }
        else _sidebar.Print(3, y, "—", DivClr, PanelBg);
        y++;
    }

    // ═════════════════════════════════════════════════════════════════════════
    // MESSAGES
    // ═════════════════════════════════════════════════════════════════════════
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

    // ═════════════════════════════════════════════════════════════════════════
    // GAME OVER
    // ═════════════════════════════════════════════════════════════════════════
    public void ShowGameOver()
    {
        if (_gameOverShown) return;
        _gameOverShown = true;
        int cx = MapW / 2 - 9, cy = MapH / 2;
        _mapPanel.Print(cx,   cy,   "╔══════════════════╗", Color.Red);
        _mapPanel.Print(cx,   cy+1, "║   YOU HAVE DIED  ║", Color.Red);
        _mapPanel.Print(cx,   cy+2, "╚══════════════════╝", Color.Red);
        _mapPanel.Print(cx-1, cy+4, "Press R to restart", Color.Yellow);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // GUN FIRE
    // ═════════════════════════════════════════════════════════════════════════
    private void TriggerRangedShot()
    {
        if (GameEngine.Instance.State != GameState.Playing) return;
        var (hit, wx, wy) = GameEngine.Instance.FireRangedShot(_fpAngle);
        _shotTimer = 0.18;
        _shotHit   = hit;
        _shotWX    = wx;
        _shotWY    = wy;

        // Launch moving projectile trail
        var ppos = GameEngine.Instance.EntityManager
            .GetComponent<PositionComponent>(GameEngine.Instance.PlayerEntity);
        if (ppos != null)
        {
            const double Speed = 14.0;
            _projWX = ppos.X + 0.5;
            _projWY = ppos.Y + 0.5;
            _projVX = Math.Cos(_fpAngle) * Speed;
            _projVY = Math.Sin(_fpAngle) * Speed;
            _projLife  = 1.0f;
            _projActive = true;
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // KEYBOARD
    // ═════════════════════════════════════════════════════════════════════════
    public override bool ProcessKeyboard(Keyboard keyboard)
    {
        if (_gameOverShown)
        {
            if (keyboard.IsKeyPressed(Keys.R))
            {
                _gameOverShown   = false;
                _equipMode       = false;
                _abilityDirState = 0;
                _firstPersonMode = true;
                _mouseAimActive  = true;
                _prevMouseX      = -1;
                DrawBorders();
                GameEngine.Instance.State = GameState.CharacterCreation;
            }
            return true;
        }

        // Inventory overlay: consume all input while open
        if (_inventoryOpen)
        {
            var invComp = GameEngine.Instance.EntityManager
                .GetComponent<InventoryComponent>(GameEngine.Instance.PlayerEntity);
            int iCount = invComp?.Items.Count ?? 0;
            if (_invSelectedIdx >= iCount) _invSelectedIdx = Math.Max(0, iCount - 1);

            if (keyboard.IsKeyPressed(Keys.Escape) || keyboard.IsKeyPressed(Keys.I))
            { _inventoryOpen = false; return true; }
            if ((keyboard.IsKeyPressed(Keys.Up)   || keyboard.IsKeyPressed(Keys.NumPad8)
              || keyboard.IsKeyPressed(Keys.W)) && _invSelectedIdx > 0)
            { _invSelectedIdx--; return true; }
            if ((keyboard.IsKeyPressed(Keys.Down)  || keyboard.IsKeyPressed(Keys.NumPad2)
              || keyboard.IsKeyPressed(Keys.S)) && _invSelectedIdx < iCount - 1)
            { _invSelectedIdx++; return true; }
            if (keyboard.IsKeyPressed(Keys.E) && iCount > 0)
            { GameEngine.Instance.TryEquipItem(_invSelectedIdx); return true; }
            if (keyboard.IsKeyPressed(Keys.U) && iCount > 0)
            { GameEngine.Instance.UseInventoryItem(_invSelectedIdx); return true; }
            if (keyboard.IsKeyPressed(Keys.D) && iCount > 0)
            {
                GameEngine.Instance.DropItem(_invSelectedIdx);
                if (_invSelectedIdx >= iCount - 1) _invSelectedIdx = Math.Max(0, iCount - 2);
                return true;
            }
            return true; // swallow all other keys while inventory is open
        }

        if (_equipMode)
        {
            for (int i = 0; i <= 9; i++)
                if (keyboard.IsKeyPressed((Keys)(Keys.D0 + i)))
                { GameEngine.Instance.TryEquipItem(i - 1); _equipMode = false; return true; }
            if (keyboard.IsKeyPressed(Keys.Escape) || keyboard.IsKeyPressed(Keys.E))
                _equipMode = false;
            return true;
        }

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
            else GameEngine.Instance.UseAbility(idx);
            return true;
        }

        // Escape: release mouse aim (cursor becomes free again)
        if (keyboard.IsKeyPressed(Keys.Escape) && _firstPersonMode)
        {
            _mouseAimActive = false;
            _prevMouseX     = -1;   // prevent angle jump on next recapture
            return true;
        }

        // Tab: toggle 3-D / overhead
        if (keyboard.IsKeyPressed(Keys.Tab))
        {
            _firstPersonMode = !_firstPersonMode;
            // Re-enable aim when switching back to FP
            if (_firstPersonMode) { _mouseAimActive = true; _prevMouseX = -1; }
            return true;
        }

        // Space / F: ranged shot
        if (keyboard.IsKeyPressed(Keys.Space) || keyboard.IsKeyPressed(Keys.F))
        { TriggerRangedShot(); return true; }

        // FP movement
        if (_firstPersonMode)
        {
            // Q / E strafe
            if (keyboard.IsKeyPressed(Keys.Q))
            { var (dx, dy) = FpAngleToDir(_fpAngle - Math.PI * 0.5); MusicPlayer.PlayFootstep(); GameEngine.Instance.ProcessPlayerTurn(dx, dy); return true; }
            if (keyboard.IsKeyPressed(Keys.E) && !_equipMode)
            { var (dx, dy) = FpAngleToDir(_fpAngle + Math.PI * 0.5); MusicPlayer.PlayFootstep(); GameEngine.Instance.ProcessPlayerTurn(dx, dy); return true; }

            if (keyboard.IsKeyPressed(Keys.Left)  || keyboard.IsKeyPressed(Keys.NumPad4))
            { _fpAngle -= 0.15; return true; }
            if (keyboard.IsKeyPressed(Keys.Right) || keyboard.IsKeyPressed(Keys.NumPad6))
            { _fpAngle += 0.15; return true; }
            if (keyboard.IsKeyPressed(Keys.Up)    || keyboard.IsKeyPressed(Keys.NumPad8) || keyboard.IsKeyPressed(Keys.W))
            { var (dx, dy) = FpAngleToDir(_fpAngle); MusicPlayer.PlayFootstep(); GameEngine.Instance.ProcessPlayerTurn( dx,  dy); return true; }
            if (keyboard.IsKeyPressed(Keys.Down)  || keyboard.IsKeyPressed(Keys.NumPad2) || keyboard.IsKeyPressed(Keys.S))
            { var (dx, dy) = FpAngleToDir(_fpAngle); MusicPlayer.PlayFootstep(); GameEngine.Instance.ProcessPlayerTurn(-dx, -dy); return true; }
            if (keyboard.IsKeyPressed(Keys.NumPad7))
            { var (dx, dy) = FpAngleToDir(_fpAngle - Math.PI * 0.5); MusicPlayer.PlayFootstep(); GameEngine.Instance.ProcessPlayerTurn(dx, dy); return true; }
            if (keyboard.IsKeyPressed(Keys.NumPad9))
            { var (dx, dy) = FpAngleToDir(_fpAngle + Math.PI * 0.5); MusicPlayer.PlayFootstep(); GameEngine.Instance.ProcessPlayerTurn(dx, dy); return true; }

            // X: examine wall ahead for secret passages (FP only)
            if (keyboard.IsKeyPressed(Keys.X))
            {
                var ppos = GameEngine.Instance.EntityManager
                    .GetComponent<PositionComponent>(GameEngine.Instance.PlayerEntity);
                if (ppos != null)
                {
                    var (fdx, fdy) = FpAngleToDir(_fpAngle);
                    GameEngine.Instance.TryRevealHiddenDoor(ppos.X + fdx, ppos.Y + fdy);
                }
                return true;
            }
        }
        else
        {
            if (keyboard.IsKeyPressed(Keys.NumPad8) || keyboard.IsKeyPressed(Keys.Up))    { MusicPlayer.PlayFootstep(); GameEngine.Instance.ProcessPlayerTurn( 0, -1); return true; }
            if (keyboard.IsKeyPressed(Keys.NumPad2) || keyboard.IsKeyPressed(Keys.Down))  { MusicPlayer.PlayFootstep(); GameEngine.Instance.ProcessPlayerTurn( 0,  1); return true; }
            if (keyboard.IsKeyPressed(Keys.NumPad4) || keyboard.IsKeyPressed(Keys.Left))  { MusicPlayer.PlayFootstep(); GameEngine.Instance.ProcessPlayerTurn(-1,  0); return true; }
            if (keyboard.IsKeyPressed(Keys.NumPad6) || keyboard.IsKeyPressed(Keys.Right)) { MusicPlayer.PlayFootstep(); GameEngine.Instance.ProcessPlayerTurn( 1,  0); return true; }
            if (keyboard.IsKeyPressed(Keys.NumPad7))                                      { MusicPlayer.PlayFootstep(); GameEngine.Instance.ProcessPlayerTurn(-1, -1); return true; }
            if (keyboard.IsKeyPressed(Keys.NumPad9))                                      { MusicPlayer.PlayFootstep(); GameEngine.Instance.ProcessPlayerTurn( 1, -1); return true; }
            if (keyboard.IsKeyPressed(Keys.NumPad1))                                      { MusicPlayer.PlayFootstep(); GameEngine.Instance.ProcessPlayerTurn(-1,  1); return true; }
            if (keyboard.IsKeyPressed(Keys.NumPad3))                                      { MusicPlayer.PlayFootstep(); GameEngine.Instance.ProcessPlayerTurn( 1,  1); return true; }
        }

        // Shared action keys
        if (keyboard.IsKeyPressed(Keys.D1)) { QueueAbility(1); return true; }
        if (keyboard.IsKeyPressed(Keys.D2)) { QueueAbility(2); return true; }
        if (keyboard.IsKeyPressed(Keys.D3)) { QueueAbility(3); return true; }
        if (keyboard.IsKeyPressed(Keys.D4)) { QueueAbility(4); return true; }
        if (keyboard.IsKeyPressed(Keys.G) || keyboard.IsKeyPressed(Keys.OemComma))
        { GameEngine.Instance.ProcessAction(PlayerAction.PickUp); return true; }
        if (keyboard.IsKeyPressed(Keys.OemPeriod) || keyboard.IsKeyPressed(Keys.NumPad5))
        { GameEngine.Instance.ProcessAction(PlayerAction.Wait); return true; }
        if (keyboard.IsKeyPressed(Keys.U))
        { GameEngine.Instance.ProcessAction(PlayerAction.UseItem); return true; }
        if (keyboard.IsKeyPressed(Keys.E))
        { _equipMode = true; return true; }

        // I: open inventory overlay
        if (keyboard.IsKeyPressed(Keys.I))
        { _inventoryOpen = true; _invSelectedIdx = 0; return true; }

        return base.ProcessKeyboard(keyboard);
    }

    private void QueueAbility(int number)
    {
        var em   = GameEngine.Instance.EntityManager;
        var abil = em.GetComponent<AbilityComponent>(GameEngine.Instance.PlayerEntity);
        if (abil == null || number > abil.Abilities.Count) return;
        var ab = abil.Abilities[number - 1];
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
    // FP HELPERS
    // ═════════════════════════════════════════════════════════════════════════
    private static (int dx, int dy) FpAngleToDir(double angle)
    {
        angle = ((angle % (Math.PI * 2)) + Math.PI * 2) % (Math.PI * 2);
        int s = (int)Math.Round(angle / (Math.PI / 4)) % 8;
        return s switch {
            0=>( 1, 0),1=>( 1, 1),2=>( 0, 1),3=>(-1, 1),
            4=>(-1, 0),5=>(-1,-1),6=>( 0,-1),7=>( 1,-1),_=>( 1,0),
        };
    }

    private static string FpFacingLabel(double angle)
    {
        angle = ((angle % (Math.PI * 2)) + Math.PI * 2) % (Math.PI * 2);
        int s = (int)Math.Round(angle / (Math.PI / 4)) % 8;
        return s switch { 0=>"East",1=>"SE",2=>"South",3=>"SW",4=>"West",5=>"NW",6=>"North",7=>"NE",_=>"East" };
    }

    // ── Low-poly colour helpers ───────────────────────────────────────────────
    private static (Color wallFront, Color wallSide,
                    Color skyNear,   Color skyFar,
                    Color floorNear, Color floorFar)
        FpThemePalette(MapTheme theme) => theme switch
    {
        // Cave: grey-brown stone walls, dark rocky ceiling, warm floor
        MapTheme.Cave   => (new Color(110, 100,  82), new Color( 74,  67,  55),
                            new Color( 18,  20,  32), new Color(  6,   6,  12),
                            new Color( 30,  24,  16), new Color( 12,   9,   6)),
        // Crypt: pale violet stone, deep purple void ceiling, cold floor
        MapTheme.Crypt  => (new Color(124, 116, 140), new Color( 85,  80, 102),
                            new Color( 24,  14,  42), new Color(  8,   5,  18),
                            new Color( 22,  16,  34), new Color(  8,   6,  14)),
        // Mines: dark earth walls, coal-black ceiling, gritty floor
        MapTheme.Mines  => (new Color( 96,  84,  56), new Color( 64,  56,  38),
                            new Color( 14,  12,  16), new Color(  5,   4,   6),
                            new Color( 24,  18,  11), new Color(  9,   7,   4)),
        // Forest: green-brown trees, dark-blue night sky, green earth floor
        MapTheme.Forest => (new Color( 74,  98,  50), new Color( 54,  70,  36),
                            new Color( 22,  40,  70), new Color(  8,  18,  44),
                            new Color( 36,  52,  20), new Color( 14,  22,   7)),
        // Default dungeon: warm tan stone, dark-blue void, warm stone floor
        _               => (new Color(160, 145, 108), new Color(110,  98,  74),
                            new Color( 28,  32,  54), new Color(  8,  10,  22),
                            new Color( 54,  44,  30), new Color( 18,  14,   9)),
    };

    private static Color FpC(int r, int g, int b, float fi)
    {
        fi = Math.Clamp(fi, 0f, 1f);
        return new Color((byte)(r * fi), (byte)(g * fi), (byte)(b * fi));
    }

    private static Color LerpC(Color a, Color b, float t) => new Color(
        (int)(a.R + (b.R - a.R) * t),
        (int)(a.G + (b.G - a.G) * t),
        (int)(a.B + (b.B - a.B) * t));
}
