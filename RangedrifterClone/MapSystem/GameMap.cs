namespace RangedrifterClone.MapSystem;

public class GameMap
{
    public int       Width       { get; }
    public int       Height      { get; }
    public int       FloorNumber { get; set; } = 1;
    public MapTheme  Theme       { get; set; } = MapTheme.Dungeon;

    private readonly Tile[] _tiles;

    public SadRogue.Primitives.Point StartPosition { get; set; }
    public SadRogue.Primitives.Point StairsUpPos   { get; set; }
    public SadRogue.Primitives.Point StairsDownPos { get; set; }

    public List<SadRogue.Primitives.Point> EnemySpawnPoints { get; } = new();
    public List<SadRogue.Primitives.Point> ItemSpawnPoints  { get; } = new();
    public List<SadRogue.Primitives.Point> ChestPositions   { get; } = new();
    public List<SadRogue.Primitives.Point> TrapPositions    { get; } = new();
    public List<SadRogue.Primitives.Point> DoorPositions    { get; } = new();

    public GameMap(int width, int height, int floor = 1, MapTheme theme = MapTheme.Dungeon)
    {
        Width = width; Height = height;
        FloorNumber = floor; Theme = theme;
        _tiles = new Tile[width * height];
        for (int i = 0; i < _tiles.Length; i++)
            _tiles[i] = Tile.CreateEmpty();
    }

    public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

    public Tile GetTile(int x, int y)
    {
        if (!InBounds(x, y)) return Tile.CreateEmpty();
        return _tiles[y * Width + x];
    }

    public void SetTile(int x, int y, Tile tile)
    {
        if (InBounds(x, y)) _tiles[y * Width + x] = tile;
    }

    public bool IsWalkable(int x, int y)
    {
        if (!InBounds(x, y)) return false;
        return _tiles[y * Width + x].IsWalkable;
    }

    public bool BlocksLight(int x, int y)
    {
        if (!InBounds(x, y)) return true;
        return _tiles[y * Width + x].BlocksLight;
    }

    /// <summary>Reset explored/visible state — used when re-entering a floor.</summary>
    public void ResetVisibility()
    {
        for (int i = 0; i < _tiles.Length; i++)
        {
            _tiles[i].IsVisible  = false;
            _tiles[i].IsExplored = false;
        }
    }
}
