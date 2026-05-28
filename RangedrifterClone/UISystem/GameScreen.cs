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
        if (_shotTimer > 0) _shotTimer -= delta.TotalSeconds;

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
            RenderSidebar();
        }
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

        (int wr, int wg, int wb) = map.Theme switch {
            MapTheme.Cave   => (108, 122, 140),
            MapTheme.Crypt  => (120, 108, 155),
            MapTheme.Mines  => (145, 122,  80),
            MapTheme.Forest => ( 55, 130,  45),
            _               => (158, 140, 105),
        };

        // ── Wall + ceiling + floor ────────────────────────────────────────
        for (int col = 0; col < viewW; col++)
        {
            int sx = col + 1;
            double camX = 2.0 * col / Math.Max(1, viewW - 1) - 1.0;
            double rdx  = dirX + planX * camX, rdy = dirY + planY * camX;

            var (perpDist, ySide, hitX, hitY) = CastRay(map, posX, posY, rdx, rdy);
            _zBuffer[col] = perpDist;

            int lineH     = Math.Min(viewH, (int)(viewH / perpDist));
            int drawStart = Math.Max(1,     halfH + 1 - lineH / 2);
            int drawEnd   = Math.Min(viewH, halfH + 1 + lineH / 2);

            char wallCh = perpDist < 1.5 ? '█' : perpDist < 3.0 ? '▓'
                        : perpDist < 6.0 ? '▒' : '░';

            bool isTree = map.Theme == MapTheme.Forest &&
                          map.GetTile(hitX, hitY).Type == TileType.Wall;
            if (isTree) wallCh = lineH > viewH / 3 ? '♣' : '│';

            float fade  = (float)Math.Max(0.06, 1.0 - perpDist / 16.0);
            float sideM = ySide ? 0.62f : 1.0f;
            (int br, int bg2, int bb) = isTree ? (45, 118, 35) : (wr, wg, wb);
            var wallClr = new Color(
                (byte)(br * fade * sideM), (byte)(bg2 * fade * sideM), (byte)(bb * fade * sideM));

            // Ceiling
            for (int sy = 1; sy < drawStart; sy++)
            {
                float t = drawStart > 2 ? Math.Clamp((float)(sy - 1) / (float)(drawStart - 2), 0f, 1f) : 0f;
                var cc = new Color((byte)(5 + (int)(10 * t)), (byte)(5 + (int)(10 * t)), (byte)(22 + (int)(48 * t)));
                _mapPanel.SetGlyph(sx, sy, ' ', cc, cc);
            }
            // Wall
            for (int sy = drawStart; sy <= drawEnd; sy++)
                _mapPanel.SetGlyph(sx, sy, wallCh, wallClr, Color.Black);
            // Floor
            for (int sy = drawEnd + 1; sy <= viewH; sy++)
            {
                float t = viewH > drawEnd ? Math.Clamp((float)(sy - drawEnd - 1) / (float)(viewH - drawEnd), 0f, 1f) : 0f;
                float v = 1f - t;
                var fc = new Color((byte)(int)(16 * v), (byte)(int)(28 * v), (byte)(int)(10 * v));
                _mapPanel.SetGlyph(sx, sy, ' ', fc, fc);
            }
        }

        // ── Sprites ───────────────────────────────────────────────────────
        RenderFpSprites(map, pos, posX, posY, dirX, dirY, planX, planY, viewW, viewH, halfH);

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

        // ── Muzzle flash ──────────────────────────────────────────────────
        DrawShotFlash(viewW, viewH);

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
        string hud    = $" [{facing}]  Tab=map  {aimHint}  LClick/Space=fire";
        _mapPanel.Print(1, 1, hud[..Math.Min(hud.Length, MapW - 3)], new Color(200, 180, 100), Color.Black);
        string flLbl = $"Floor {GameEngine.Instance.CurrentFloor}";
        _mapPanel.Print(MapW - flLbl.Length - 1, 1, flLbl, new Color(100, 100, 160), Color.Black);

        // ── Mini-map ───────────────────────────────────────────────────────
        DrawFpMiniMap(map, pos);

        // ── Player body (drawn last so it's always on top) ─────────────────
        DrawPlayerBody();
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
            // Sword arm raised upper-right; heavy plate armour; wide stance.
            case PlayerClass.Warrior:
                // Sword (raised diagonally)
                P(+5, -7, '/', wclr); P(+4, -6, '/', wclr); P(+3, -5, '/', wclr);
                P(+2, -4, '+', wclr);           // crossguard
                // Helmet + face
                P(-1, -5, '[', steel); P(0, -5, '\x01', clr); P(+1, -5, ']', steel);
                // Pauldrons (shoulder guards)
                P(-3, -4, '<', steel); P(-2, -4, '[', steel);
                P(-1, -4, '▄', dstl); P(0, -4, '▄', dstl); P(+1, -4, '▄', dstl);
                P(+2, -4, ']', steel); P(+3, -4, '>', steel);
                // Chest + belt
                P(-2, -3, '|', dark); P(-1, -3, '═', dstl);
                P(0,  -3, '╪', dstl); P(+1, -3, '═', dstl); P(+2, -3, '|', dark);
                // Waist / tassets
                P(-2, -2, '/', dark); P(-1, -2, '[', dstl);
                P(0,  -2, '─', dstl); P(+1, -2, ']', dstl); P(+2, -2, '\\', dark);
                // Upper legs (greaves)
                P(-1, -1, '[', dim);  P(0, -1, ' ', clr);  P(+1, -1, ']', dim);
                // Boots
                P(-2, 0, '/', dstl); P(-1, 0, '_', dstl);
                P(+1, 0, '_', dstl); P(+2, 0, '\\', dstl);
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
    // SPRITE PROJECTION
    // ═════════════════════════════════════════════════════════════════════════

    private readonly record struct SpriteInfo(
        double DistSq, double WX, double WY,
        char TopG, char BodyG, char BotG,
        Color Clr, float HMul, float WMul,
        Entity EntityId   // Entity.None if tile sprite
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
                sprites.Add(new SpriteInfo(dx * dx + dy * dy, p.X + 0.5, p.Y + 0.5,
                    th, tb, tl, hClr, 1.0f, 0.55f, e));
            }
            else if (feat != null)
            {
                (char tg, char bg2, char btg, float hm, float wm) = feat.Type switch {
                    FeatureType.Chest      => ('\xF0', '+', '_', 0.55f, 0.65f),
                    FeatureType.Trap       => ('^',   '^',  '^', 0.30f, 0.50f),
                    FeatureType.StairsDown => ('>',   '>',  '>', 0.38f, 0.55f),
                    FeatureType.StairsUp   => ('<',   '<',  '<', 0.38f, 0.55f),
                    FeatureType.Door       => ('+',   '|',  '_', 1.0f,  0.35f),
                    _                      => (r.Glyph, r.Glyph, r.Glyph, 0.5f, 0.5f),
                };
                sprites.Add(new SpriteInfo(dx * dx + dy * dy, p.X + 0.5, p.Y + 0.5,
                    tg, bg2, btg, r.Foreground, hm, wm, Entity.None));
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
            (char tg, char bg2, Color col, float hm, float wm) = tile.Type switch {
                TileType.Rock  => ('\xF9', '\xF9',  new Color(165, 155, 135), 0.38f, 0.55f),
                TileType.Bush  => ('"',    '"',     new Color( 55, 145,  55), 0.45f, 0.65f),
                TileType.Water => ('\xF7', '\xF7',  new Color( 65, 130, 210), 0.18f, 1.00f),
                _              => ('.',   '.',       Color.White,              0.30f, 0.50f),
            };
            sprites.Add(new SpriteInfo(ddx * ddx + ddy * ddy, wx + 0.5, wy + 0.5,
                tg, bg2, bg2, col, hm, wm, Entity.None));
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
            int topY    = Math.Max(1, halfH + 1 - sprH / 2);
            int botY    = Math.Min(viewH, halfH + 1 + sprH / 2);
            int sprW    = Math.Max(1, (int)(sprH * sp.WMul));
            int leftC   = centCol - sprW / 2;
            int rightC  = centCol + sprW / 2;

            float fade = (float)Math.Max(0.07, 1.0 - txD / 14.0);
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

                for (int sy = topY; sy <= botY; sy++)
                {
                    if (sy < 1 || sy >= MapH - 1) continue;
                    float relY = (float)(sy - topY) / (float)totalRows;
                    char g = relY < 0.28f ? sp.TopG : relY < 0.72f ? sp.BodyG : sp.BotG;
                    _mapPanel.SetGlyph(sx, sy, g, spClr, Color.Black);
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

        string hint = _equipMode          ? "# to equip/unequip"
                    : _abilityDirState > 0 ? $"Dir: ability {_abilityDirState}"
                    : _firstPersonMode     ? "Mouse=aim LClick=fire"
                    : "E=equip .=wait g=get";
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
            { var (dx, dy) = FpAngleToDir(_fpAngle - Math.PI * 0.5); GameEngine.Instance.ProcessPlayerTurn(dx, dy); return true; }
            if (keyboard.IsKeyPressed(Keys.E) && !_equipMode)
            { var (dx, dy) = FpAngleToDir(_fpAngle + Math.PI * 0.5); GameEngine.Instance.ProcessPlayerTurn(dx, dy); return true; }

            if (keyboard.IsKeyPressed(Keys.Left)  || keyboard.IsKeyPressed(Keys.NumPad4))
            { _fpAngle -= 0.15; return true; }
            if (keyboard.IsKeyPressed(Keys.Right) || keyboard.IsKeyPressed(Keys.NumPad6))
            { _fpAngle += 0.15; return true; }
            if (keyboard.IsKeyPressed(Keys.Up)    || keyboard.IsKeyPressed(Keys.NumPad8) || keyboard.IsKeyPressed(Keys.W))
            { var (dx, dy) = FpAngleToDir(_fpAngle); GameEngine.Instance.ProcessPlayerTurn( dx,  dy); return true; }
            if (keyboard.IsKeyPressed(Keys.Down)  || keyboard.IsKeyPressed(Keys.NumPad2) || keyboard.IsKeyPressed(Keys.S))
            { var (dx, dy) = FpAngleToDir(_fpAngle); GameEngine.Instance.ProcessPlayerTurn(-dx, -dy); return true; }
            if (keyboard.IsKeyPressed(Keys.NumPad7))
            { var (dx, dy) = FpAngleToDir(_fpAngle - Math.PI * 0.5); GameEngine.Instance.ProcessPlayerTurn(dx, dy); return true; }
            if (keyboard.IsKeyPressed(Keys.NumPad9))
            { var (dx, dy) = FpAngleToDir(_fpAngle + Math.PI * 0.5); GameEngine.Instance.ProcessPlayerTurn(dx, dy); return true; }
        }
        else
        {
            if (keyboard.IsKeyPressed(Keys.NumPad8) || keyboard.IsKeyPressed(Keys.Up))    { GameEngine.Instance.ProcessPlayerTurn( 0, -1); return true; }
            if (keyboard.IsKeyPressed(Keys.NumPad2) || keyboard.IsKeyPressed(Keys.Down))  { GameEngine.Instance.ProcessPlayerTurn( 0,  1); return true; }
            if (keyboard.IsKeyPressed(Keys.NumPad4) || keyboard.IsKeyPressed(Keys.Left))  { GameEngine.Instance.ProcessPlayerTurn(-1,  0); return true; }
            if (keyboard.IsKeyPressed(Keys.NumPad6) || keyboard.IsKeyPressed(Keys.Right)) { GameEngine.Instance.ProcessPlayerTurn( 1,  0); return true; }
            if (keyboard.IsKeyPressed(Keys.NumPad7))                                      { GameEngine.Instance.ProcessPlayerTurn(-1, -1); return true; }
            if (keyboard.IsKeyPressed(Keys.NumPad9))                                      { GameEngine.Instance.ProcessPlayerTurn( 1, -1); return true; }
            if (keyboard.IsKeyPressed(Keys.NumPad1))                                      { GameEngine.Instance.ProcessPlayerTurn(-1,  1); return true; }
            if (keyboard.IsKeyPressed(Keys.NumPad3))                                      { GameEngine.Instance.ProcessPlayerTurn( 1,  1); return true; }
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
}
