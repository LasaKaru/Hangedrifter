namespace RangedrifterClone.MapSystem;

public class Tile
{
    public TileType Type               { get; set; }
    public bool     IsWalkable         { get; set; }
    public bool     BlocksLight        { get; set; }
    public bool     IsExplored         { get; set; }
    public bool     IsVisible          { get; set; }

    public char     Glyph              { get; set; }
    public SadRogue.Primitives.Color ForegroundVisible  { get; set; }
    public SadRogue.Primitives.Color ForegroundExplored { get; set; }
    public SadRogue.Primitives.Color Background         { get; set; }
    public string   Name               { get; set; } = "";

    // ═════════════════════════════════════════════════════════════════════
    // Floor — glyph varies randomly for visual texture (baked at map-gen time)
    // ═════════════════════════════════════════════════════════════════════
    public static Tile CreateFloor(MapTheme theme = MapTheme.Dungeon, Random? rng = null)
    {
        rng ??= Random.Shared;
        double r = rng.NextDouble();

        return theme switch
        {
            MapTheme.Cave =>
                r < 0.65
                ? MT(TileType.Floor,'.', C(100,155,115), C(40,62,46), C(6,10,7),  "Cave Floor")
                : MT(TileType.Floor,',', C( 90,145,105), C(36,58,42), C(6,10,7),  "Cave Floor"),

            MapTheme.Forest =>
                r < 0.55
                ? MT(TileType.Floor,'"', C( 75,185, 75), C(30,74,30), C(5,14,5),  "Grass")
                : r < 0.82
                ? MT(TileType.Floor,'.', C( 80,175, 65), C(32,70,26), C(5,14,5),  "Grass")
                : MT(TileType.Floor,',', C( 70,165, 60), C(28,66,24), C(5,14,5),  "Grass"),

            MapTheme.Crypt =>
                r < 0.78
                ? MT(TileType.Floor,'.', C(110,108,165), C(44,43,66), C(7,7,13),  "Crypt Floor")
                : MT(TileType.Floor,',', C(120,115,175), C(48,46,70), C(7,7,13),  "Crypt Floor"),

            MapTheme.Mines =>
                r < 0.70
                ? MT(TileType.Floor,'.', C(148,128, 95), C(59,51,38), C(10,9,6),  "Mine Floor")
                : r < 0.90
                ? MT(TileType.Floor,',', C(138,118, 88), C(55,47,35), C(10,9,6),  "Mine Floor")
                : MT(TileType.Floor,'\'',C(155,135,100), C(62,54,40), C(10,9,6),  "Mine Floor"),

            _ => // Dungeon — warm green stone
                r < 0.72
                ? MT(TileType.Floor,'.', C(115,160,115), C(46,64,46), C(7,11,7),  "Floor")
                : r < 0.90
                ? MT(TileType.Floor,',', C(105,150,105), C(42,60,42), C(7,11,7),  "Floor")
                : MT(TileType.Floor,'`', C(110,155,110), C(44,62,44), C(7,11,7),  "Floor"),
        };
    }

    // ═════════════════════════════════════════════════════════════════════
    // Wall — pseudo-3D half-block rendering.
    //   Glyph      : '▄' (lower-half block)
    //   Foreground : front/shadow face colour (darker)
    //   Background : top/lit face colour (lighter) — only shows when visible
    //   Interior walls become ' ' in PostProcessWallGlyphs so deep stone
    //   reads as pure black; forest trees stay as '♣' (no 3-D connect).
    // ═════════════════════════════════════════════════════════════════════
    public static Tile CreateWall(MapTheme theme = MapTheme.Dungeon) => theme switch
    {
        // front face (fg), lit top face (bg), explored dim (fg dark)
        MapTheme.Cave   => MW3d(C(108,122,140), C(148,165,185), C(43,49,56), "Cave Wall"),
        MapTheme.Crypt  => MW3d(C(120,108,155), C(162,148,200), C(48,43,62), "Bone Wall"),
        MapTheme.Mines  => MW3d(C(145,122, 80), C(195,165,108), C(58,49,32), "Rock Wall"),
        MapTheme.Forest => MWTree(),
        _               => MW3d(C(158,140,105), C(210,190,150), C(63,56,42), "Wall"),
    };

    /// <summary>
    /// 3-D wall tile using ▄ half-block.
    /// Top half of cell → Background (lit top face).
    /// Bottom half      → Foreground (shaded front face).
    /// Explored dim     → front face only at ~20 % brightness; bg = black.
    /// </summary>
    private static Tile MW3d(
        SadRogue.Primitives.Color front,
        SadRogue.Primitives.Color top,
        SadRogue.Primitives.Color frontDim,
        string name) => new()
    {
        Type = TileType.Wall, IsWalkable = false, BlocksLight = true,
        Glyph              = '▄',
        ForegroundVisible  = front,
        ForegroundExplored = frontDim,
        Background         = top,          // lit top face (only used when visible)
        Name               = name
    };

    /// <summary>Forest tree — standalone ♣ glyph, no half-block 3-D.</summary>
    private static Tile MWTree() => new()
    {
        Type = TileType.Wall, IsWalkable = false, BlocksLight = true,
        Glyph              = '♣',
        ForegroundVisible  = C(45,118,35),
        ForegroundExplored = C(18,47,14),
        Background         = C(0,0,0),
        Name               = "Tree"
    };

    // ═════════════════════════════════════════════════════════════════════
    // Special tiles
    // ═════════════════════════════════════════════════════════════════════
    public static Tile CreateEmpty() =>
        MT(TileType.Empty,' ', C(0,0,0), C(0,0,0), C(0,0,0), "Void", walkable:false, blocksLight:true);

    public static Tile CreateBush() =>
        MT(TileType.Bush, '"', C(55,145,55), C(22,58,22), C(3,11,3),  "Bush");

    public static Tile CreateRock() =>
        MT(TileType.Rock, '\xF9', C(165,155,135), C(66,62,54), C(0,0,0), "Rock",   // · CP437 249
           walkable:false, blocksLight:true);

    public static Tile CreateDoor(bool open = false) => open
        ? MT(TileType.Door, '/', C(210,170,90), C(84,68,36), C(0,0,0), "Open Door")
        : MT(TileType.Door, '+', C(210,170,90), C(84,68,36), C(0,0,0), "Door",
             walkable:false, blocksLight:true);

    public static Tile CreateStairsDown() =>
        MT(TileType.StairsDown, '>', C(220,220,255), C(88,88,128), C(0,0,0), "Stairs Down");

    public static Tile CreateStairsUp() =>
        MT(TileType.StairsUp,   '<', C(220,220,255), C(88,88,128), C(0,0,0), "Stairs Up");

    public static Tile CreateWater() =>
        MT(TileType.Water, '\xF7', C(65,130,210), C(26,52,84), C(0,0,0), "Water"); // ≈ CP437 247

    public static Tile CreateChest() =>
        MT(TileType.Chest, '\xF0', C(255,210,55), C(102,84,22), C(0,0,0), "Chest"); // ≡ CP437 240

    public static Tile CreateTrap() =>
        MT(TileType.Trap,  '^', C(210,55,55), C(84,22,22), C(0,0,0), "Trap");

    // ═════════════════════════════════════════════════════════════════════
    // Helpers
    // ═════════════════════════════════════════════════════════════════════
    private static Tile MT(TileType type, char glyph,
        SadRogue.Primitives.Color fgVis,
        SadRogue.Primitives.Color fgExp,
        SadRogue.Primitives.Color bg,
        string name,
        bool walkable = true, bool blocksLight = false) => new()
    {
        Type = type, IsWalkable = walkable, BlocksLight = blocksLight,
        Glyph = glyph,
        ForegroundVisible  = fgVis,
        ForegroundExplored = fgExp,
        Background         = bg,
        Name               = name
    };


    private static SadRogue.Primitives.Color C(byte r, byte g, byte b) => new(r, g, b);
}

public enum TileType
{
    Empty, Floor, Wall, Bush, Rock,
    Door, StairsDown, StairsUp, Water, Chest, Trap
}

public enum MapTheme { Dungeon, Cave, Crypt, Mines, Forest }
