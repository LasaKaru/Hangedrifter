using SadConsole;
using SadConsole.Input;
using SadRogue.Primitives;
using RangedrifterClone.Core;
using RangedrifterClone.Components;
using RangedrifterClone.MapSystem;

namespace RangedrifterClone.UISystem;

/// <summary>
/// Main gameplay screen.  Two display modes share the same panels:
///
///   TOP-DOWN (Tab to switch back)
///     Map panel (59×37) — camera-scrolled 200×200 dungeon with FOV + glow
///     Sidebar  (21×50)  — stats, HP/MP bars, abilities, inventory
///     Msg log  (59×13)  — rolling combat/event history
///
///   FIRST-PERSON (default when game starts — Tab to toggle)
///     Full 3-D DDA ray-cast in the map panel:
///       • Wolfenstein-style wall slices with █▓▒░ distance shading
///       • Per-theme wall tint (Cave=steel-blue, Crypt=purple, …)
///       • Ceiling gradient (black → dim indigo)
///       • Floor gradient (mossy green → black)
///       • Enemy sprites: ☺ head / class-glyph body / | legs
///       • Item / feature sprites: short floor objects
///       • Rock / Bush / Water tile sprites (floor-feature projection)
///       • Ambient glow pulse around the player silhouette
///       • 15×9 explored mini-map overlay (bottom-right)
///       • Z-buffer — sprites correctly occluded by walls
///
///   Sidebar in both modes shows a "3-D dungeon RPG" HUD panel:
///       class portrait frame, coloured HP/MP bars, abilities grid,
///       equipment slots, and bag — all with box-drawing depth chrome.
/// </summary>
public class GameScreen : ScreenObject
{
    // ── Panel layout ─────────────────────────────────────────────────────────
    private const int TotalW   = 80;
    private const int TotalH   = 50;
    private const int SidebarW = 21;
    private const int MapW     = TotalW - SidebarW;   // 59
    private const int MsgH     = 13;
    private const int MapH     = TotalH - MsgH;       // 37

    private readonly ScreenSurface _mapPanel;
    private readonly ScreenSurface _sidebar;
    private readonly ScreenSurface _msgPanel;

    // ── Runtime state ────────────────────────────────────────────────────────
    private int    _camX, _camY;
    private bool   _gameOverShown;
    private bool   _equipMode;
    private int    _abilityDirState  = 0;
    private double _glowTime         = 0;

    // First-person
    private bool     _firstPersonMode = true;          // default — start in 3-D
    private double   _fpAngle         = 0.0;           // 0=East, π/2=South
    private double[] _zBuffer         = Array.Empty<double>(); // per-column depth

    // ── Sidebar colour palette ───────────────────────────────────────────────
    private static readonly Color LabelClr  = new(140, 140, 140);
    private static readonly Color ValueClr  = new(200, 200, 100);
    private static readonly Color DivClr    = new( 55,  55,  55);
    private static readonly Color ChromeClr = new( 80,  80, 100); // 3-D chrome border
    private static readonly Color HpLblClr  = new(180,  80,  80);
    private static readonly Color ManaClr   = new( 80, 140, 220);
    private static readonly Color PanelBg   = new(  8,   8,  14); // deep sidebar bg

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

    // ── Borders ──────────────────────────────────────────────────────────────
    private void DrawBorders()
    {
        var b = new ColoredGlyph(DivClr, Color.Black);
        _mapPanel.DrawBox(new Rectangle(0, 0, MapW,     MapH),   ShapeParameters.CreateStyledBox(ICellSurface.ConnectedLineThin, b));
        _sidebar .DrawBox(new Rectangle(0, 0, SidebarW, TotalH), ShapeParameters.CreateStyledBox(ICellSurface.ConnectedLineThin, b));
        _msgPanel.DrawBox(new Rectangle(0, 0, MapW,     MsgH),   ShapeParameters.CreateStyledBox(ICellSurface.ConnectedLineThin, b));
    }

    public void ResetGameOver() { _gameOverShown = false; DrawBorders(); }

    // ── Update (every frame) ─────────────────────────────────────────────────
    public override void Update(TimeSpan delta)
    {
        base.Update(delta);
        _glowTime += delta.TotalSeconds;
        var st = GameEngine.Instance.State;
        if (st == GameState.Playing || st == GameState.GameOver)
        {
            if (!_firstPersonMode) UpdateCamera();
            RenderMap();
            RenderSidebar();
        }
    }

    // ── Camera (top-down only) ────────────────────────────────────────────────
    private void UpdateCamera()
    {
        var pos = GameEngine.Instance.EntityManager
            .GetComponent<PositionComponent>(GameEngine.Instance.PlayerEntity);
        var map = GameEngine.Instance.CurrentMap;
        if (pos == null || map == null) return;
        _camX = Math.Clamp(pos.X - (MapW - 2) / 2, 0, Math.Max(0, map.Width  - MapW + 2));
        _camY = Math.Clamp(pos.Y - (MapH - 2) / 2, 0, Math.Max(0, map.Height - MapH + 2));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // MAP RENDERING — dispatches to 3-D or top-down
    // ─────────────────────────────────────────────────────────────────────────
    private void RenderMap()
    {
        var map = GameEngine.Instance.CurrentMap;
        if (map == null) return;

        if (_firstPersonMode) { RenderFirstPerson(map); return; }

        var em = GameEngine.Instance.EntityManager;

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
                _mapPanel.SetGlyph(sx, sy, tile.Glyph, tile.ForegroundVisible,
                    tile.Type is TileType.Wall ? tile.Background : tile.Background);
            else if (tile.IsExplored)
                _mapPanel.SetGlyph(sx, sy, tile.Glyph, tile.ForegroundExplored, Color.Black);
        }

        // Entities
        foreach (var (_, render, pos) in em
            .GetEntitiesWith<RenderComponent, PositionComponent>()
            .Select(e => (e, em.GetComponent<RenderComponent>(e)!, em.GetComponent<PositionComponent>(e)!))
            .OrderBy(t => t.Item2.RenderLayer))
        {
            if (!render.IsVisible) continue;
            var tile = map.GetTile(pos.X, pos.Y);
            if (!tile.IsVisible) continue;
            int sx = pos.X - _camX + 1, sy = pos.Y - _camY + 1;
            if (sx >= 1 && sx < MapW - 1 && sy >= 1 && sy < MapH - 1)
            {
                var bg = render.Background == Color.Transparent ? Color.Black : render.Background;
                _mapPanel.SetGlyph(sx, sy, render.Glyph, render.Foreground, bg);
            }
        }

        ApplyPlayerGlow(map);
        RenderTerrainLabels(map);

        string floorLabel = $"Floor {GameEngine.Instance.CurrentFloor}";
        _mapPanel.Print(MapW - floorLabel.Length - 1, 1, floorLabel, new Color(100, 100, 160));
    }

    // ── Player glow (top-down) ────────────────────────────────────────────────
    private void ApplyPlayerGlow(GameMap map)
    {
        var em     = GameEngine.Instance.EntityManager;
        var player = GameEngine.Instance.PlayerEntity;
        var pos    = em.GetComponent<PositionComponent>(player);
        var rend   = em.GetComponent<RenderComponent>(player);
        if (pos == null || rend == null) return;

        float pulse = (float)(0.55 + 0.45 * Math.Sin(_glowTime * Math.PI * 2.0));
        var c = rend.Foreground;
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
            float intensity = (1f - dist / (Radius + 1f));
            intensity = intensity * intensity * 0.45f * pulse;
            _mapPanel.SetBackground(sx, swy, new Color(
                (byte)(c.R * intensity), (byte)(c.G * intensity), (byte)(c.B * intensity)));
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

    /// <summary>
    /// DDA ray-cast.  Returns perpendicular wall distance, whether the hit
    /// was on the Y-axis face (ySide), and the map cell coordinates.
    /// </summary>
    private static (double dist, bool ySide, int hitX, int hitY) CastRay(
        GameMap map, double posX, double posY, double rayDX, double rayDY)
    {
        int mapX = (int)posX, mapY = (int)posY;

        double ddx = Math.Abs(rayDX) < 1e-10 ? 1e30 : Math.Abs(1.0 / rayDX);
        double ddy = Math.Abs(rayDY) < 1e-10 ? 1e30 : Math.Abs(1.0 / rayDY);

        int stepX, stepY;
        double sideX, sideY;

        if (rayDX < 0) { stepX = -1; sideX = (posX - mapX) * ddx; }
        else           { stepX =  1; sideX = (mapX + 1.0 - posX) * ddx; }
        if (rayDY < 0) { stepY = -1; sideY = (posY - mapY) * ddy; }
        else           { stepY =  1; sideY = (mapY + 1.0 - posY) * ddy; }

        bool hit = false, ySide = false;
        int  steps = 80;
        while (!hit && steps-- > 0)
        {
            if (sideX < sideY) { sideX += ddx; mapX += stepX; ySide = false; }
            else               { sideY += ddy; mapY += stepY; ySide = true;  }
            var tile = map.GetTile(mapX, mapY);
            if (!tile.IsWalkable || tile.Type == TileType.Empty) hit = true;
        }

        if (!hit) return (1e6, false, mapX, mapY);

        double dist = ySide
            ? (mapY - posY + (1 - stepY) * 0.5) / rayDY
            : (mapX - posX + (1 - stepX) * 0.5) / rayDX;

        return (Math.Max(0.15, dist), ySide, mapX, mapY);
    }

    private void RenderFirstPerson(GameMap map)
    {
        var em     = GameEngine.Instance.EntityManager;
        var player = GameEngine.Instance.PlayerEntity;
        var pos    = em.GetComponent<PositionComponent>(player);
        var rend   = em.GetComponent<RenderComponent>(player);
        if (pos == null) return;

        // ── Setup ─────────────────────────────────────────────────────────
        int viewW = MapW - 2;   // 57
        int viewH = MapH - 2;   // 35
        int halfH = viewH / 2;  // 17

        if (_zBuffer.Length != viewW) _zBuffer = new double[viewW];

        for (int sy = 1; sy < MapH - 1; sy++)
        for (int sx = 1; sx < MapW - 1; sx++)
            _mapPanel.SetGlyph(sx, sy, ' ', Color.Black, Color.Black);

        double posX  = pos.X + 0.5, posY = pos.Y + 0.5;
        double dirX  =  Math.Cos(_fpAngle), dirY  = Math.Sin(_fpAngle);
        double planX = -Math.Sin(_fpAngle) * 0.66;
        double planY  =  Math.Cos(_fpAngle) * 0.66;

        // Wall base colour per theme
        (int wr, int wg, int wb) = map.Theme switch {
            MapTheme.Cave   => (108, 122, 140),
            MapTheme.Crypt  => (120, 108, 155),
            MapTheme.Mines  => (145, 122,  80),
            MapTheme.Forest => ( 55, 130,  45),
            _               => (158, 140, 105),
        };
        // Tree pillar colour (Forest only)
        (int tr, int tg, int tb) = (45, 118, 35);

        // ── Per-column ray-cast ───────────────────────────────────────────
        for (int col = 0; col < viewW; col++)
        {
            int sx = col + 1;
            double camX = 2.0 * col / Math.Max(1, viewW - 1) - 1.0;
            double rayDX = dirX + planX * camX;
            double rayDY = dirY + planY * camX;

            var (perpDist, ySide, hitX, hitY) = CastRay(map, posX, posY, rayDX, rayDY);
            _zBuffer[col] = perpDist;

            int lineH      = Math.Min(viewH, (int)(viewH / perpDist));
            int drawStart  = Math.Max(1,     halfH + 1 - lineH / 2);
            int drawEnd    = Math.Min(viewH, halfH + 1 + lineH / 2);

            // Glyph by distance: closer = denser block char
            char wallCh = perpDist < 1.5 ? '█'
                        : perpDist < 3.0 ? '▓'
                        : perpDist < 6.0 ? '▒'
                        :                  '░';

            // Colour: theme tint + distance fade + side-face darken
            float fade  = (float)Math.Max(0.06, 1.0 - perpDist / 16.0);
            float sideM = ySide ? 0.62f : 1.0f;

            bool isTree = map.Theme == MapTheme.Forest &&
                          map.GetTile(hitX, hitY).Type == TileType.Wall;

            var (baseR, baseG, baseB) = isTree ? (tr, tg, tb) : (wr, wg, wb);

            // Trees: alternate ♣ / │ bark chars for texture
            if (isTree)
                wallCh = (lineH > viewH / 3) ? '♣' : '│';

            var wallClr = new Color(
                (byte)(baseR * fade * sideM),
                (byte)(baseG * fade * sideM),
                (byte)(baseB * fade * sideM));

            // ── Ceiling gradient ───────────────────────────────────────
            for (int sy = 1; sy < drawStart; sy++)
            {
                float t = drawStart > 2
                    ? Math.Clamp((float)(sy - 1) / (float)(drawStart - 2), 0f, 1f) : 0f;
                var cc = new Color(
                    (byte)( 5 + (int)(10 * t)),
                    (byte)( 5 + (int)(10 * t)),
                    (byte)(22 + (int)(48 * t)));
                _mapPanel.SetGlyph(sx, sy, ' ', cc, cc);
            }

            // ── Wall slice ─────────────────────────────────────────────
            for (int sy = drawStart; sy <= drawEnd; sy++)
                _mapPanel.SetGlyph(sx, sy, wallCh, wallClr, Color.Black);

            // ── Floor gradient ─────────────────────────────────────────
            for (int sy = drawEnd + 1; sy <= viewH; sy++)
            {
                float t = (viewH > drawEnd)
                    ? Math.Clamp((float)(sy - drawEnd - 1) / (float)(viewH - drawEnd), 0f, 1f) : 0f;
                float v = 1f - t;
                var fc = new Color((byte)(int)(16 * v), (byte)(int)(28 * v), (byte)(int)(10 * v));
                _mapPanel.SetGlyph(sx, sy, ' ', fc, fc);
            }
        }

        // ── Sprite pass ───────────────────────────────────────────────────
        RenderFpSprites(map, pos, posX, posY, dirX, dirY, planX, planY, viewW, viewH, halfH);

        // ── Player glow pulse on the horizon ─────────────────────────────
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
                var existing = _mapPanel.GetCellAppearance(gx, glowRow);
                if (existing == null) continue;
                _mapPanel.SetBackground(gx, glowRow,
                    new Color((byte)(existing.Background.R + (int)(c.R * i)),
                              (byte)(existing.Background.G + (int)(c.G * i)),
                              (byte)(existing.Background.B + (int)(c.B * i))));
            }
        }

        // ── Crosshair ─────────────────────────────────────────────────────
        int crX = MapW / 2, crY = MapH / 2;
        var crossClr = new Color(220, 220, 220);
        _mapPanel.SetGlyph(crX - 1, crY,   '─', crossClr, Color.Black);
        _mapPanel.SetGlyph(crX,     crY,   '+', crossClr, Color.Black);
        _mapPanel.SetGlyph(crX + 1, crY,   '─', crossClr, Color.Black);
        _mapPanel.SetGlyph(crX,     crY-1, '│', crossClr, Color.Black);
        _mapPanel.SetGlyph(crX,     crY+1, '│', crossClr, Color.Black);

        // ── HUD bar ────────────────────────────────────────────────────────
        string facing = FpFacingLabel(_fpAngle);
        string hud    = $" [{facing}]  Tab=map  Arrows=move/turn  1-4=ability";
        _mapPanel.Print(1, 1, hud[..Math.Min(hud.Length, MapW - 3)],
            new Color(200, 180, 100), Color.Black);
        string floorLbl = $"Floor {GameEngine.Instance.CurrentFloor}";
        _mapPanel.Print(MapW - floorLbl.Length - 1, 1, floorLbl, new Color(100, 100, 160), Color.Black);

        // ── Mini-map ────────────────────────────────────────────────────────
        DrawFpMiniMap(map, pos);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SPRITE PROJECTION (Wolfenstein-style billboard rendering)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Sprite descriptor — one entry per world-space object to be rendered.
    /// </summary>
    private readonly record struct SpriteInfo(
        double DistSq,
        double WX, double WY,
        char   TopGlyph, char BodyGlyph, char BotGlyph,
        Color  Clr,
        float  HeightMul,    // 1.0 = full wall height, 0.5 = half, etc.
        float  WidthMul      // 1.0 = same as height, 0.5 = thin
    );

    private void RenderFpSprites(
        GameMap map, PositionComponent playerPos,
        double posX, double posY,
        double dirX, double dirY, double planX, double planY,
        int viewW, int viewH, int halfH)
    {
        var em = GameEngine.Instance.EntityManager;
        var sprites = new List<SpriteInfo>(32);

        // ── 1. Entities ──────────────────────────────────────────────────
        foreach (var e in em.GetEntitiesWith<RenderComponent, PositionComponent>())
        {
            if (e == GameEngine.Instance.PlayerEntity) continue;
            var p = em.GetComponent<PositionComponent>(e)!;
            var r = em.GetComponent<RenderComponent>(e)!;
            if (!r.IsVisible) continue;
            if (!map.GetTile(p.X, p.Y).IsVisible) continue;

            double dx = p.X + 0.5 - posX, dy = p.Y + 0.5 - posY;
            double dSq = dx * dx + dy * dy;

            bool isEnemy   = em.GetComponent<AIComponent>(e) != null;
            bool isFeature = em.GetComponent<FeatureComponent>(e) != null;
            var  feat      = em.GetComponent<FeatureComponent>(e);

            if (isEnemy)
            {
                // Humanoid: ☺ head / glyph torso / | legs — full wall height
                sprites.Add(new SpriteInfo(dSq, p.X + 0.5, p.Y + 0.5,
                    '\x01', r.Glyph, '|',
                    r.Foreground, 1.0f, 0.55f));
            }
            else if (feat != null)
            {
                // Feature sprite
                (char tg, char bg2, char btg, float hm, float wm) = feat.Type switch {
                    FeatureType.Chest     => ('\xF0', '+', '_', 0.55f, 0.65f),  // ≡ chest
                    FeatureType.Trap      => ('^',  '^',  '^', 0.30f, 0.50f),
                    FeatureType.StairsDown=> ('>',  '>',  '>', 0.38f, 0.55f),
                    FeatureType.StairsUp  => ('<',  '<',  '<', 0.38f, 0.55f),
                    FeatureType.Door      => ('+',  '|',  '_', 1.0f,  0.35f),
                    _                     => (r.Glyph, r.Glyph, r.Glyph, 0.5f, 0.5f),
                };
                sprites.Add(new SpriteInfo(dSq, p.X + 0.5, p.Y + 0.5,
                    tg, bg2, btg, r.Foreground, hm, wm));
            }
            else
            {
                // Item on floor — small, near floor
                sprites.Add(new SpriteInfo(dSq, p.X + 0.5, p.Y + 0.5,
                    r.Glyph, r.Glyph, r.Glyph,
                    r.Foreground, 0.42f, 0.45f));
            }
        }

        // ── 2. Visible feature tiles: Rock, Bush, Water, Trap ─────────────
        int scanR = 14;
        for (int dy = -scanR; dy <= scanR; dy++)
        for (int dx = -scanR; dx <= scanR; dx++)
        {
            int wx = playerPos.X + dx, wy = playerPos.Y + dy;
            var tile = map.GetTile(wx, wy);
            if (!tile.IsVisible) continue;
            if (tile.Type is not (TileType.Rock or TileType.Bush or TileType.Water)) continue;

            double ddx = wx + 0.5 - posX, ddy = wy + 0.5 - posY;
            double dSq = ddx * ddx + ddy * ddy;

            (char tg, char bg2, Color col, float hm, float wm) = tile.Type switch {
                TileType.Rock  => ('\xF9', '\xF9',  new Color(165, 155, 135), 0.38f, 0.55f),
                TileType.Bush  => ('"',    '"',     new Color( 55, 145,  55), 0.45f, 0.65f),
                TileType.Water => ('\xF7', '\xF7',  new Color( 65, 130, 210), 0.18f, 1.00f),
                _              => ('.',    '.',     Color.White,               0.30f, 0.50f),
            };
            sprites.Add(new SpriteInfo(dSq, wx + 0.5, wy + 0.5, tg, bg2, bg2, col, hm, wm));
        }

        // ── Sort farthest-first (painter's algorithm) ─────────────────────
        sprites.Sort((a, b) => b.DistSq.CompareTo(a.DistSq));

        double invDet = 1.0 / (planX * dirY - dirX * planY);

        foreach (var sp in sprites)
        {
            double dx  = sp.WX - posX, dy = sp.WY - posY;

            // Camera-space transform
            double txDepth = invDet * ( dirY * dx  -  dirX * dy);   // how far (depth)
            double txHoriz = invDet * (-planY * dx + planX * dy);   // left/right

            if (txDepth <= 0.2) continue; // behind or too close

            // Screen-space centre column of the sprite
            int sprCentreX = (int)((viewW * 0.5) * (1.0 + txHoriz / txDepth));

            // Height
            int sprH  = Math.Max(1, Math.Min(viewH, (int)(viewH * sp.HeightMul / txDepth)));
            int topY  = Math.Max(1, halfH + 1 - sprH / 2);
            int botY  = Math.Min(viewH, halfH + 1 + sprH / 2);

            // Width proportional to height * widthMul
            int sprW     = Math.Max(1, (int)(sprH * sp.WidthMul));
            int leftCol  = sprCentreX - sprW / 2;
            int rightCol = sprCentreX + sprW / 2;

            // Distance fade
            float fade = (float)Math.Max(0.07, 1.0 - txDepth / 14.0);
            var spClr = new Color(
                (byte)(sp.Clr.R * fade),
                (byte)(sp.Clr.G * fade),
                (byte)(sp.Clr.B * fade));

            int totalRows = Math.Max(1, botY - topY);

            for (int stripe = leftCol; stripe <= rightCol; stripe++)
            {
                if (stripe < 0 || stripe >= viewW) continue;
                if (txDepth >= _zBuffer[stripe]) continue;  // occluded by wall
                int sx = stripe + 1;
                if (sx < 1 || sx >= MapW - 1) continue;

                for (int sy = topY; sy <= botY; sy++)
                {
                    if (sy < 1 || sy >= MapH - 1) continue;
                    float relY = (float)(sy - topY) / (float)totalRows;
                    char glyph = relY < 0.30f ? sp.TopGlyph
                               : relY < 0.72f ? sp.BodyGlyph
                               :                sp.BotGlyph;
                    _mapPanel.SetGlyph(sx, sy, glyph, spClr, Color.Black);
                }
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // MINI-MAP (FP mode)
    // ─────────────────────────────────────────────────────────────────────────
    private void DrawFpMiniMap(GameMap map, PositionComponent pos)
    {
        const int MmW = 15, MmH = 9;
        int mxOff = MapW - MmW - 2;  // = 42
        int myOff = MapH - MmH - 2;  // = 26

        int startWX = pos.X - MmW / 2;
        int startWY = pos.Y - MmH / 2;

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
                TileType.Floor                     => ('.', new Color(50, 70, 50),  Color.Black),
                TileType.Wall  or TileType.Empty   => (' ', Color.Black, new Color(22, 22, 22)),
                TileType.Door                      => ('+', new Color(200, 160, 80), Color.Black),
                TileType.StairsDown                => ('>', new Color(200, 200, 255), Color.Black),
                TileType.StairsUp                  => ('<', new Color(200, 200, 255), Color.Black),
                TileType.Water                     => (' ', Color.Black, new Color(15, 50, 110)),
                TileType.Chest                     => ('.', new Color(255, 200, 50), Color.Black),
                _                                  => ('.', new Color(50, 70, 50),  Color.Black),
            };
            _mapPanel.SetGlyph(sx, sy, ch, fg, bg);
        }

        // Border
        var bc = new Color(45, 50, 70);
        void S(int sx, int sy, char g)
        {
            if (sx >= 1 && sx < MapW-1 && sy >= 1 && sy < MapH-1)
                _mapPanel.SetGlyph(sx, sy, g, bc, Color.Black);
        }
        int bL = mxOff-1, bR = mxOff+MmW, bT = myOff-1, bB = myOff+MmH;
        S(bL,bT,'┌'); S(bR,bT,'┐'); S(bL,bB,'└'); S(bR,bB,'┘');
        for (int mx = mxOff; mx < mxOff+MmW; mx++) { S(mx,bT,'─'); S(mx,bB,'─'); }
        for (int my = myOff; my < myOff+MmH; my++) { S(bL,my,'│'); S(bR,my,'│'); }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SIDEBAR — 3-D RPG HUD panel
    // ─────────────────────────────────────────────────────────────────────────
    private void RenderSidebar()
    {
        var em     = GameEngine.Instance.EntityManager;
        var player = GameEngine.Instance.PlayerEntity;

        // Fill with deep dark bg
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

        // ── CLASS PORTRAIT FRAME ─────────────────────────────────────────
        Color clsClr = cls?.Class switch {
            PlayerClass.Warrior => new Color(220, 160,  60),
            PlayerClass.Rogue   => new Color(180, 100, 180),
            PlayerClass.Mage    => new Color( 80, 160, 220),
            _                   => new Color(180, 180, 180),
        };
        char clsIcon = cls?.Class switch {
            PlayerClass.Warrior => '\x02',  // ☻
            PlayerClass.Rogue   => '\x02',
            PlayerClass.Mage    => '\x02',
            _                   => '\x02',
        };

        // Top chrome bar with class name
        DrawChromeLine(y++, "╔", "═", "╗", ChromeClr);
        string clsName = cls != null ? cls.ClassName : "Hero";
        int nameOff = (SidebarW - 2 - clsName.Length) / 2 + 1;
        _sidebar.Print(1, y, "║", ChromeClr, PanelBg);
        _sidebar.Print(nameOff, y, clsName, clsClr, PanelBg);
        _sidebar.Print(SidebarW - 2, y, "║", ChromeClr, PanelBg);
        y++;
        DrawChromeLine(y++, "╚", "═", "╝", ChromeClr);

        // ── LEVEL & XP ───────────────────────────────────────────────────
        if (exp != null)
        {
            _sidebar.Print(1, y, $"Lv{exp.Level}", new Color(255, 210, 80), PanelBg);
            string xpStr = $"XP {exp.Experience}/{exp.NextLevelExp}";
            _sidebar.Print(SidebarW - 1 - xpStr.Length, y, xpStr,
                new Color(120, 120, 80), PanelBg);
            y++;
        }

        // ── HP BAR ───────────────────────────────────────────────────────
        if (fighter != null)
        {
            var hpClr = fighter.Hp < fighter.MaxHp / 3   ? new Color(220, 50, 50)
                      : fighter.Hp < fighter.MaxHp * 2/3 ? new Color(220, 200, 50)
                      : new Color(60, 200, 80);
            _sidebar.Print(1, y, "HP", HpLblClr, PanelBg);
            _sidebar.Print(4, y, $"{fighter.Hp}/{fighter.MaxHpTotal}", hpClr, PanelBg);
            y++;
            DrawFillBar(y++, Math.Max(0, fighter.Hp), fighter.MaxHpTotal,
                hpClr, new Color(50, 8, 8), '█', '░');
        }

        // ── MP BAR ───────────────────────────────────────────────────────
        if (mana != null)
        {
            _sidebar.Print(1, y, "MP", ManaClr, PanelBg);
            _sidebar.Print(4, y, $"{mana.Mana}/{mana.MaxMana}", ManaClr, PanelBg);
            y++;
            DrawFillBar(y++, mana.Mana, mana.MaxMana,
                ManaClr, new Color(8, 8, 50), '█', '░');
        }

        // ── STATUS EFFECTS ────────────────────────────────────────────────
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

        // ── STATS ─────────────────────────────────────────────────────────
        y++;
        DrawChromeLine(y++, "┌", "─", "┐", DivClr);
        if (fighter != null)
        {
            StatRow("DMG",  fighter.DamageString,                ref y);
            StatRow("DEF",  fighter.EffectiveDefense.ToString(), ref y);
            StatRow("STR",  fighter.Strength.ToString(),         ref y);
        }
        if (status != null)
            StatRow("TURN", status.Turn.ToString(), ref y);
        DrawChromeLine(y++, "└", "─", "┘", DivClr);

        // ── EQUIPMENT ─────────────────────────────────────────────────────
        y++;
        _sidebar.Print(1, y, "┌─", ChromeClr, PanelBg);
        _sidebar.Print(3, y, "EQUIP", new Color(200, 200, 100), PanelBg);
        _sidebar.Print(8, y, "─┐", ChromeClr, PanelBg);
        y++;

        if (equip != null)
        {
            PrintEquipRow("⚔", equip.Weapon,  ref y);   // sword
            PrintEquipRow("🛡", equip.Armor,   ref y);   // shield
            PrintEquipRow("🛡", equip.Shield,  ref y);
            PrintEquipRow("💍", equip.Ring,    ref y);
        }
        DrawChromeLine(y++, "└", "─", "┘", DivClr);

        // ── ABILITIES ─────────────────────────────────────────────────────
        y++;
        _sidebar.Print(1, y, "┌─", ChromeClr, PanelBg);
        _sidebar.Print(3, y, "SKILL", new Color(180, 100, 220), PanelBg);
        _sidebar.Print(8, y, "─┐", ChromeClr, PanelBg);
        y++;

        if (abils != null)
        {
            for (int i = 0; i < abils.Abilities.Count && y < TotalH - 6; i++)
            {
                var ab = abils.Abilities[i];
                bool rdy = ab.IsReady;
                var abClr = rdy ? ab.Color : new Color(60, 60, 60);
                string cdStr = rdy ? "rdy" : $"{ab.CurrentCooldown}t";
                string abbr  = ab.Name.Length > 8 ? ab.Name[..8] : ab.Name.PadRight(8);
                _sidebar.Print(1,  y, $"[{i+1}]",  new Color(100, 100, 120), PanelBg);
                _sidebar.Print(5,  y, abbr,          abClr, PanelBg);
                _sidebar.Print(SidebarW - 4, y, cdStr[..Math.Min(3, cdStr.Length)],
                    rdy ? new Color(80, 180, 80) : new Color(180, 60, 60), PanelBg);
                y++;
            }
        }
        DrawChromeLine(y++, "└", "─", "┘", DivClr);

        // ── INVENTORY ─────────────────────────────────────────────────────
        y++;
        if (y < TotalH - 4)
        {
            _sidebar.Print(1, y, "┌─", ChromeClr, PanelBg);
            _sidebar.Print(3, y, "BAG", new Color(160, 160, 180), PanelBg);
            _sidebar.Print(6, y, "─┐", ChromeClr, PanelBg);
            y++;
            if (inv != null)
            {
                for (int i = 0; i < Math.Min(inv.Items.Count, TotalH - y - 3); i++)
                {
                    var item = inv.Items[i];
                    var iClr = item.Category switch {
                        "Weapon"     => new Color(200, 200, 100),
                        "Food"       => new Color(200, 120,  80),
                        "Consumable" => new Color(160, 100, 200),
                        "Armor" or "Shield" or "Ring" or "Amulet" => new Color(100, 180, 200),
                        _            => new Color(160, 160, 160),
                    };
                    string label = $"{item.Glyph} {item.Name}";
                    if (label.Length > SidebarW - 5) label = label[..(SidebarW - 5)];
                    _sidebar.Print(1, y, label, iClr, PanelBg);
                    if (item.Count > 1)
                        _sidebar.Print(SidebarW - 4, y, $"x{item.Count}", DivClr, PanelBg);
                    y++;
                }
            }
        }

        // ── FOOTER hint ───────────────────────────────────────────────────
        string hint = _equipMode          ? "# to equip/unequip"
                    : _abilityDirState > 0 ? $"Dir: ability {_abilityDirState}"
                    : _firstPersonMode     ? "Arrows+move  TAB=2D"
                    : "E=equip .=wait g=get";
        _sidebar.Print(1, TotalH - 2, hint[..Math.Min(hint.Length, SidebarW - 2)], DivClr, PanelBg);
    }

    // ── Sidebar helpers ───────────────────────────────────────────────────────
    private void DrawChromeLine(int y, string left, string mid, string right, Color clr)
    {
        _sidebar.Print(1, y, left, clr, PanelBg);
        for (int cx = 2; cx < SidebarW - 2; cx++)
            _sidebar.Print(cx, y, mid, clr, PanelBg);
        _sidebar.Print(SidebarW - 2, y, right, clr, PanelBg);
    }

    private void DrawFillBar(int y, int cur, int max, Color fillClr, Color emptyClr, char fillCh, char emptyCh)
    {
        int bw = SidebarW - 3;
        int filled = max > 0 ? (int)Math.Round((double)cur / max * bw) : 0;
        filled = Math.Clamp(filled, 0, bw);
        for (int i = 0; i < bw; i++)
            _sidebar.SetGlyph(1 + i, y, i < filled ? fillCh : emptyCh,
                i < filled ? fillClr : emptyClr, PanelBg);
    }

    private void StatRow(string label, string value, ref int y)
    {
        _sidebar.Print(1,  y, label, LabelClr, PanelBg);
        _sidebar.Print(6,  y, value, ValueClr, PanelBg);
        y++;
    }

    private void PrintEquipRow(string icon, EquipmentEntry? entry, ref int y)
    {
        _sidebar.Print(1, y, icon, DivClr, PanelBg);
        if (entry != null)
        {
            string nm = entry.Name.Length > SidebarW - 5 ? entry.Name[..(SidebarW - 5)] : entry.Name;
            _sidebar.Print(3, y, nm, entry.Color, PanelBg);
        }
        else
            _sidebar.Print(3, y, "—", DivClr, PanelBg);
        y++;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // MESSAGES
    // ─────────────────────────────────────────────────────────────────────────
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

    // ─────────────────────────────────────────────────────────────────────────
    // GAME OVER
    // ─────────────────────────────────────────────────────────────────────────
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

    // ─────────────────────────────────────────────────────────────────────────
    // KEYBOARD
    // ─────────────────────────────────────────────────────────────────────────
    public override bool ProcessKeyboard(Keyboard keyboard)
    {
        if (_gameOverShown)
        {
            if (keyboard.IsKeyPressed(Keys.R))
            {
                _gameOverShown   = false;
                _equipMode       = false;
                _abilityDirState = 0;
                _firstPersonMode = true;   // reset to 3-D on new game
                DrawBorders();
                GameEngine.Instance.State = GameState.CharacterCreation;
            }
            return true;
        }

        // ── Equip mode ─────────────────────────────────────────────────────
        if (_equipMode)
        {
            for (int i = 0; i <= 9; i++)
                if (keyboard.IsKeyPressed((Keys)(Keys.D0 + i)))
                { GameEngine.Instance.TryEquipItem(i - 1); _equipMode = false; return true; }
            if (keyboard.IsKeyPressed(Keys.Escape) || keyboard.IsKeyPressed(Keys.E))
                _equipMode = false;
            return true;
        }

        // ── Waiting for ability direction ──────────────────────────────────
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

        // ── Tab: toggle 3-D / top-down ─────────────────────────────────────
        if (keyboard.IsKeyPressed(Keys.Tab)) { _firstPersonMode = !_firstPersonMode; return true; }

        // ── First-person movement ──────────────────────────────────────────
        if (_firstPersonMode)
        {
            if (keyboard.IsKeyPressed(Keys.Left)  || keyboard.IsKeyPressed(Keys.NumPad4))
            { _fpAngle -= 0.15; return true; }
            if (keyboard.IsKeyPressed(Keys.Right) || keyboard.IsKeyPressed(Keys.NumPad6))
            { _fpAngle += 0.15; return true; }
            if (keyboard.IsKeyPressed(Keys.Up)    || keyboard.IsKeyPressed(Keys.NumPad8))
            { var (dx, dy) = FpAngleToDir(_fpAngle); GameEngine.Instance.ProcessPlayerTurn( dx,  dy); return true; }
            if (keyboard.IsKeyPressed(Keys.Down)  || keyboard.IsKeyPressed(Keys.NumPad2))
            { var (dx, dy) = FpAngleToDir(_fpAngle); GameEngine.Instance.ProcessPlayerTurn(-dx, -dy); return true; }
            if (keyboard.IsKeyPressed(Keys.NumPad7))
            { var (dx, dy) = FpAngleToDir(_fpAngle - Math.PI * 0.5); GameEngine.Instance.ProcessPlayerTurn(dx, dy); return true; }
            if (keyboard.IsKeyPressed(Keys.NumPad9))
            { var (dx, dy) = FpAngleToDir(_fpAngle + Math.PI * 0.5); GameEngine.Instance.ProcessPlayerTurn(dx, dy); return true; }
            // Abilities and other keys fall through to shared section below
        }

        // ── Top-down movement ──────────────────────────────────────────────
        if (!_firstPersonMode)
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

        // ── Shared action keys (both modes) ────────────────────────────────
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

    // ─────────────────────────────────────────────────────────────────────────
    // FP HELPERS
    // ─────────────────────────────────────────────────────────────────────────

    private static (int dx, int dy) FpAngleToDir(double angle)
    {
        angle = ((angle % (Math.PI * 2)) + Math.PI * 2) % (Math.PI * 2);
        int s = (int)Math.Round(angle / (Math.PI / 4)) % 8;
        return s switch {
            0 => ( 1,  0), 1 => ( 1,  1), 2 => ( 0,  1), 3 => (-1,  1),
            4 => (-1,  0), 5 => (-1, -1), 6 => ( 0, -1), 7 => ( 1, -1),
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
}
