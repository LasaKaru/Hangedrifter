namespace RangedrifterClone.MapSystem;

public class Tile
{
    public TileType Type { get; set; }
    public bool IsWalkable { get; set; }
    public bool BlocksLight { get; set; }
    public bool IsExplored { get; set; }
    public bool IsVisible { get; set; }

    public char Glyph { get; set; }
    public SadRogue.Primitives.Color ForegroundVisible { get; set; }
    public SadRogue.Primitives.Color ForegroundExplored { get; set; }
    public SadRogue.Primitives.Color Background { get; set; }
    public string Name { get; set; } = "";

    public static Tile CreateFloor() => new Tile
    {
        Type = TileType.Floor,
        IsWalkable = true,
        BlocksLight = false,
        Glyph = '.',
        ForegroundVisible = new SadRogue.Primitives.Color(100, 160, 100),
        ForegroundExplored = new SadRogue.Primitives.Color(40, 60, 40),
        Background = SadRogue.Primitives.Color.Black,
        Name = "Floor"
    };

    public static Tile CreateWall() => new Tile
    {
        Type = TileType.Wall,
        IsWalkable = false,
        BlocksLight = true,
        Glyph = '#',
        ForegroundVisible = new SadRogue.Primitives.Color(140, 120, 80),
        ForegroundExplored = new SadRogue.Primitives.Color(60, 50, 35),
        Background = SadRogue.Primitives.Color.Black,
        Name = "Wall"
    };

    public static Tile CreateEmpty() => new Tile
    {
        Type = TileType.Empty,
        IsWalkable = false,
        BlocksLight = true,
        Glyph = ' ',
        ForegroundVisible = SadRogue.Primitives.Color.Black,
        ForegroundExplored = SadRogue.Primitives.Color.Black,
        Background = SadRogue.Primitives.Color.Black,
        Name = "Void"
    };

    public static Tile CreateBush() => new Tile
    {
        Type = TileType.Bush,
        IsWalkable = true,
        BlocksLight = false,
        Glyph = '"',
        ForegroundVisible = new SadRogue.Primitives.Color(60, 140, 60),
        ForegroundExplored = new SadRogue.Primitives.Color(30, 60, 30),
        Background = SadRogue.Primitives.Color.Black,
        Name = "Bush"
    };

    public static Tile CreateRock() => new Tile
    {
        Type = TileType.Rock,
        IsWalkable = false,
        BlocksLight = true,
        Glyph = 'o',
        ForegroundVisible = new SadRogue.Primitives.Color(160, 150, 130),
        ForegroundExplored = new SadRogue.Primitives.Color(70, 65, 55),
        Background = SadRogue.Primitives.Color.Black,
        Name = "Rock"
    };
}

public enum TileType { Empty, Floor, Wall, Bush, Rock }
