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
                ? MT(TileType.Floor,'.', C(72,115,85), C(28,44,33), C(4,7,5),   "Cave Floor")
                : MT(TileType.Floor,',', C(65,108,78), C(25,42,30), C(4,7,5),   "Cave Floor"),

            MapTheme.Forest =>
                r < 0.55
                ? MT(TileType.Floor,'"', C(55,148,55), C(22,60,22), C(3,11,3),  "Grass")
                : r < 0.82
                ? MT(TileType.Floor,'.', C(60,138,50), C(24,55,20), C(3,11,3),  "Grass")
                : MT(TileType.Floor,',', C(50,128,45), C(20,51,18), C(3,11,3),  "Grass"),

            MapTheme.Crypt =>
                r < 0.78
                ? MT(TileType.Floor,'.', C(82,82,128), C(33,33,51), C(5,5,10),  "Crypt Floor")
                : MT(TileType.Floor,',', C(90,88,135), C(36,35,54), C(5,5,10),  "Crypt Floor"),

            MapTheme.Mines =>
                r < 0.70
                ? MT(TileType.Floor,'.', C(115,100,74), C(46,40,30), C(8,7,5),  "Mine Floor")
                : r < 0.90
                ? MT(TileType.Floor,',', C(108,93,68),  C(43,37,27), C(8,7,5),  "Mine Floor")
                : MT(TileType.Floor,'\'',C(120,106,76), C(48,42,30), C(8,7,5),  "Mine Floor"),

            _ => // Dungeon
                r < 0.72
                ? MT(TileType.Floor,'.', C(90,128,90), C(36,51,36), C(5,9,5),   "Floor")
                : r < 0.90
                ? MT(TileType.Floor,',', C(82,118,82), C(33,47,33), C(5,9,5),   "Floor")
                : MT(TileType.Floor,'`', C(85,122,85), C(34,49,34), C(5,9,5),   "Floor"),
        };
    }

    // ═════════════════════════════════════════════════════════════════════
    // Wall — base glyph '#'; DungeonGenerator.PostProcessWallGlyphs()
    //        replaces it with box-drawing chars after the map is built.
    //        Forest theme uses '♣' (trees) and is not post-processed.
    // ═════════════════════════════════════════════════════════════════════
    public static Tile CreateWall(MapTheme theme = MapTheme.Dungeon) => theme switch
    {
        MapTheme.Cave   => MW(TileType.Wall,'#', C(95,108,122), C(38,43,49), "Cave Wall"),
        MapTheme.Crypt  => MW(TileType.Wall,'#', C(108,96,132), C(43,38,53), "Bone Wall"),
        MapTheme.Mines  => MW(TileType.Wall,'#', C(128,108,68), C(51,43,27), "Rock Wall"),
        MapTheme.Forest => MW(TileType.Wall,'\x05', C(45,118,35), C(18,47,14), "Tree"),  // ♣ CP437
        _               => MW(TileType.Wall,'#', C(148,132,98), C(59,53,39), "Wall"),
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

    /// <summary>Wall helper — always non-walkable, always blocks light.</summary>
    private static Tile MW(TileType type, char glyph,
        SadRogue.Primitives.Color fgVis,
        SadRogue.Primitives.Color fgExp,
        string name) => new()
    {
        Type = type, IsWalkable = false, BlocksLight = true,
        Glyph = glyph,
        ForegroundVisible  = fgVis,
        ForegroundExplored = fgExp,
        Background         = new(0, 0, 0),
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
