namespace RangedrifterClone.MapSystem;

public class DungeonGenerator
{
    private readonly int _width;
    private readonly int _height;
    private readonly Random _rng;
    private GameMap _map = null!;

    private class BspNode
    {
        public int X, Y, W, H;
        public BspNode? Left, Right;
        public RoomRect? Room;
        public BspNode(int x, int y, int w, int h) { X = x; Y = y; W = w; H = h; }
    }

    public record struct RoomRect(int X, int Y, int Width, int Height);

    public DungeonGenerator(int width, int height, int? seed = null)
    {
        _width = width;
        _height = height;
        _rng = seed.HasValue ? new Random(seed.Value) : new Random();
    }

    public GameMap Generate()
    {
        _map = new GameMap(_width, _height);
        var root = new BspNode(1, 1, _width - 2, _height - 2);
        SplitNode(root, 0);
        CreateRooms(root);
        ConnectRooms(root);
        PlaceWalls();
        PlaceSpawnPoints(root);
        AddScenery();
        return _map;
    }

    private void SplitNode(BspNode node, int depth)
    {
        if (depth >= 6 || (node.W < 16 && node.H < 16)) return;

        bool splitH = node.H > node.W || (node.W == node.H && _rng.NextDouble() > 0.5);
        if (splitH && node.H < 16) splitH = false;
        if (!splitH && node.W < 16) splitH = true;

        if (splitH)
        {
            int split = _rng.Next(6, node.H - 6);
            node.Left  = new BspNode(node.X, node.Y, node.W, split);
            node.Right = new BspNode(node.X, node.Y + split, node.W, node.H - split);
        }
        else
        {
            int split = _rng.Next(6, node.W - 6);
            node.Left  = new BspNode(node.X, node.Y, split, node.H);
            node.Right = new BspNode(node.X + split, node.Y, node.W - split, node.H);
        }

        SplitNode(node.Left, depth + 1);
        SplitNode(node.Right, depth + 1);
    }

    private void CreateRooms(BspNode node)
    {
        if (node.Left == null && node.Right == null)
        {
            int rw = _rng.Next(5, Math.Min(node.W - 2, 20));
            int rh = _rng.Next(4, Math.Min(node.H - 2, 16));
            int rx = node.X + _rng.Next(1, Math.Max(2, node.W - rw - 1));
            int ry = node.Y + _rng.Next(1, Math.Max(2, node.H - rh - 1));
            node.Room = new RoomRect(rx, ry, rw, rh);
            CarveRoom(node.Room.Value);
            return;
        }
        if (node.Left  != null) CreateRooms(node.Left);
        if (node.Right != null) CreateRooms(node.Right);
    }

    private void CarveRoom(RoomRect room)
    {
        for (int y = room.Y; y < room.Y + room.Height; y++)
        for (int x = room.X; x < room.X + room.Width;  x++)
            _map.SetTile(x, y, Tile.CreateFloor());
    }

    private void ConnectRooms(BspNode node)
    {
        if (node.Left == null || node.Right == null) return;
        ConnectRooms(node.Left);
        ConnectRooms(node.Right);

        var leftRoom  = GetRoom(node.Left);
        var rightRoom = GetRoom(node.Right);
        if (leftRoom == null || rightRoom == null) return;

        var p1 = new SadRogue.Primitives.Point(
            leftRoom.Value.X  + leftRoom.Value.Width  / 2,
            leftRoom.Value.Y  + leftRoom.Value.Height / 2);
        var p2 = new SadRogue.Primitives.Point(
            rightRoom.Value.X + rightRoom.Value.Width  / 2,
            rightRoom.Value.Y + rightRoom.Value.Height / 2);

        CarveCorridorL(p1, p2);
    }

    private void CarveCorridorL(SadRogue.Primitives.Point a, SadRogue.Primitives.Point b)
    {
        int x = a.X, y = a.Y;
        while (x != b.X) { _map.SetTile(x, y, Tile.CreateFloor()); x += (b.X > x) ? 1 : -1; }
        while (y != b.Y) { _map.SetTile(x, y, Tile.CreateFloor()); y += (b.Y > y) ? 1 : -1; }
        _map.SetTile(x, y, Tile.CreateFloor());
    }

    private RoomRect? GetRoom(BspNode node)
    {
        if (node.Room != null) return node.Room;
        RoomRect? l = node.Left  != null ? GetRoom(node.Left)  : null;
        RoomRect? r = node.Right != null ? GetRoom(node.Right) : null;
        if (l == null) return r;
        if (r == null) return l;
        return _rng.NextDouble() > 0.5 ? l : r;
    }

    private void PlaceWalls()
    {
        for (int y = 0; y < _height; y++)
        for (int x = 0; x < _width;  x++)
        {
            if (_map.GetTile(x, y).Type != TileType.Empty) continue;
            bool adj = false;
            for (int dy = -1; dy <= 1 && !adj; dy++)
            for (int dx = -1; dx <= 1 && !adj; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                if (_map.GetTile(x + dx, y + dy).Type == TileType.Floor) adj = true;
            }
            if (adj) _map.SetTile(x, y, Tile.CreateWall());
        }
    }

    private void PlaceSpawnPoints(BspNode root)
    {
        var rooms = new List<RoomRect>();
        CollectRooms(root, rooms);
        if (rooms.Count == 0) return;

        var startRoom = rooms[0];
        _map.StartPosition = new SadRogue.Primitives.Point(
            startRoom.X + startRoom.Width  / 2,
            startRoom.Y + startRoom.Height / 2);

        for (int i = 1; i < rooms.Count; i++)
        {
            int enemies = _rng.Next(1, 4);
            for (int e = 0; e < enemies; e++)
            {
                int ex = rooms[i].X + _rng.Next(1, rooms[i].Width  - 1);
                int ey = rooms[i].Y + _rng.Next(1, rooms[i].Height - 1);
                if (_map.IsWalkable(ex, ey))
                    _map.EnemySpawnPoints.Add(new SadRogue.Primitives.Point(ex, ey));
            }

            if (_rng.NextDouble() > 0.5)
            {
                int ix = rooms[i].X + _rng.Next(1, rooms[i].Width  - 1);
                int iy = rooms[i].Y + _rng.Next(1, rooms[i].Height - 1);
                if (_map.IsWalkable(ix, iy))
                    _map.ItemSpawnPoints.Add(new SadRogue.Primitives.Point(ix, iy));
            }
        }
    }

    private void AddScenery()
    {
        for (int y = 0; y < _height; y++)
        for (int x = 0; x < _width;  x++)
        {
            if (_map.GetTile(x, y).Type != TileType.Floor) continue;
            double r = _rng.NextDouble();
            if      (r < 0.008) _map.SetTile(x, y, Tile.CreateRock());
            else if (r < 0.025) _map.SetTile(x, y, Tile.CreateBush());
        }
    }

    private void CollectRooms(BspNode node, List<RoomRect> rooms)
    {
        if (node.Room != null) { rooms.Add(node.Room.Value); return; }
        if (node.Left  != null) CollectRooms(node.Left,  rooms);
        if (node.Right != null) CollectRooms(node.Right, rooms);
    }
}
