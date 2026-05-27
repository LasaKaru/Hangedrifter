namespace RangedrifterClone.MapSystem;

public class GameMap
{
    public int Width { get; }
    public int Height { get; }
    private readonly Tile[] _tiles;

    public SadRogue.Primitives.Point StartPosition { get; set; }
    public List<SadRogue.Primitives.Point> EnemySpawnPoints { get; } = new();
    public List<SadRogue.Primitives.Point> ItemSpawnPoints { get; } = new();

    public GameMap(int width, int height)
    {
        Width = width;
        Height = height;
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
        if (!InBounds(x, y)) return;
        _tiles[y * Width + x] = tile;
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
}
