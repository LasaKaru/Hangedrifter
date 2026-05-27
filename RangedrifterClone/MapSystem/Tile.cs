namespace RangedrifterClone.MapSystem;

public class Tile
{
    public TileType Type          { get; set; }
    public bool     IsWalkable    { get; set; }
    public bool     BlocksLight   { get; set; }
    public bool     IsExplored    { get; set; }
    public bool     IsVisible     { get; set; }

    public char     Glyph                { get; set; }
    public SadRogue.Primitives.Color ForegroundVisible  { get; set; }
    public SadRogue.Primitives.Color ForegroundExplored { get; set; }
    public SadRogue.Primitives.Color Background         { get; set; }
    public string   Name                { get; set; } = "";

    // ── Factory methods ──────────────────────────────────────────────────

    public static Tile CreateFloor(MapTheme theme = MapTheme.Dungeon) => theme switch
    {
        MapTheme.Cave   => MakeTile(TileType.Floor, true, false, '.', C(80, 130, 80),   C(30, 55, 30),   C(0,0,0), "Cave Floor"),
        MapTheme.Crypt  => MakeTile(TileType.Floor, true, false, '.', C(90, 90, 130),   C(35, 35, 55),   C(0,0,0), "Crypt Floor"),
        MapTheme.Mines  => MakeTile(TileType.Floor, true, false, '.', C(100, 90, 70),   C(40, 35, 28),   C(0,0,0), "Mine Floor"),
        MapTheme.Forest => MakeTile(TileType.Floor, true, false, '.', C(60, 140, 60),   C(25, 60, 25),   C(0,0,0), "Grass"),
        _               => MakeTile(TileType.Floor, true, false, '.', C(100, 160, 100), C(40, 60, 40),   C(0,0,0), "Floor"),
    };

    public static Tile CreateWall(MapTheme theme = MapTheme.Dungeon) => theme switch
    {
        MapTheme.Cave   => MakeTile(TileType.Wall,  false, true, '#', C(100, 100, 80),  C(40, 40, 32),   C(0,0,0), "Cave Wall"),
        MapTheme.Crypt  => MakeTile(TileType.Wall,  false, true, '#', C(110, 100, 130), C(45, 40, 55),   C(0,0,0), "Bone Wall"),
        MapTheme.Mines  => MakeTile(TileType.Wall,  false, true, '#', C(120, 100, 60),  C(50, 40, 25),   C(0,0,0), "Rock Wall"),
        MapTheme.Forest => MakeTile(TileType.Wall,  false, true, 'T', C(40, 100, 40),   C(18, 45, 18),   C(0,0,0), "Tree"),
        _               => MakeTile(TileType.Wall,  false, true, '#', C(140, 120, 80),  C(60, 50, 35),   C(0,0,0), "Wall"),
    };

    public static Tile CreateEmpty() =>
        MakeTile(TileType.Empty, false, true, ' ', C(0,0,0), C(0,0,0), C(0,0,0), "Void");

    public static Tile CreateBush() =>
        MakeTile(TileType.Bush,  true,  false, '"', C(60,140,60),  C(30,60,30),  C(0,0,0), "Bush");

    public static Tile CreateRock() =>
        MakeTile(TileType.Rock,  false, true,  'o', C(160,150,130), C(70,65,55), C(0,0,0), "Rock");

    public static Tile CreateDoor(bool open = false) => open
        ? MakeTile(TileType.Door,  true,  false, '/', C(200,160,80), C(80,60,30), C(0,0,0), "Open Door")
        : MakeTile(TileType.Door,  false, true,  '+', C(200,160,80), C(80,60,30), C(0,0,0), "Door");

    public static Tile CreateStairsDown() =>
        MakeTile(TileType.StairsDown, true, false, '>', C(200,200,255), C(80,80,120), C(0,0,0), "Stairs Down");

    public static Tile CreateStairsUp() =>
        MakeTile(TileType.StairsUp,   true, false, '<', C(200,200,255), C(80,80,120), C(0,0,0), "Stairs Up");

    public static Tile CreateWater() =>
        MakeTile(TileType.Water, false, false, '~', C(60,120,200), C(25,50,90), C(0,0,0), "Water");

    public static Tile CreateChest() =>
        MakeTile(TileType.Chest, true, false, 'C', C(255,200,50), C(100,80,20), C(0,0,0), "Chest");

    public static Tile CreateTrap() =>
        MakeTile(TileType.Trap,  true, false, '^', C(200,50,50), C(80,20,20), C(0,0,0), "Trap");

    // ── Helpers ──────────────────────────────────────────────────────────
    private static Tile MakeTile(TileType type, bool walk, bool blocksLight, char glyph,
        SadRogue.Primitives.Color fgVis, SadRogue.Primitives.Color fgExp,
        SadRogue.Primitives.Color bg, string name) => new()
    {
        Type = type, IsWalkable = walk, BlocksLight = blocksLight,
        Glyph = glyph, ForegroundVisible = fgVis,
        ForegroundExplored = fgExp, Background = bg, Name = name
    };

    private static SadRogue.Primitives.Color C(byte r, byte g, byte b) =>
        new(r, g, b);
}

public enum TileType
{
    Empty, Floor, Wall, Bush, Rock,
    Door, StairsDown, StairsUp, Water, Chest, Trap
}

public enum MapTheme { Dungeon, Cave, Crypt, Mines, Forest }
