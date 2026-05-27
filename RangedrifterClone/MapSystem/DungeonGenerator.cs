namespace RangedrifterClone.MapSystem;

/// <summary>
/// BSP dungeon generator with themes, special rooms (treasury / shrine /
/// boss chamber), doors, traps, chests, and multi-floor scaling.
/// </summary>
public class DungeonGenerator
{
    private readonly int    _width, _height;
    private readonly Random _rng;
    private readonly int    _floor;
    private readonly MapTheme _theme;
    private GameMap _map = null!;

    private class BspNode
    {
        public int X, Y, W, H;
        public BspNode? Left, Right;
        public RoomRect? Room;
        public RoomTag   Tag = RoomTag.Normal;
        public BspNode(int x, int y, int w, int h) { X=x; Y=y; W=w; H=h; }
    }
    public record struct RoomRect(int X, int Y, int Width, int Height);
    private enum RoomTag { Normal, Start, Boss, Treasury, Shrine }

    public DungeonGenerator(int width, int height, int floor = 1,
        MapTheme theme = MapTheme.Dungeon, int? seed = null)
    {
        _width  = width;  _height = height;
        _floor  = floor;  _theme  = theme;
        _rng = seed.HasValue ? new Random(seed.Value) : new Random();
    }

    // ── Theme picker (called by GameEngine based on floor) ──────────────
    public static MapTheme ThemeForFloor(int floor) => floor switch
    {
        1       => MapTheme.Dungeon,
        2 or 3  => MapTheme.Cave,
        4 or 5  => MapTheme.Mines,
        6 or 7  => MapTheme.Crypt,
        _       => MapTheme.Forest
    };

    public GameMap Generate()
    {
        _map = new GameMap(_width, _height, _floor, _theme);
        var root = new BspNode(1, 1, _width - 2, _height - 2);
        SplitNode(root, 0);
        CreateRooms(root);
        ConnectRooms(root);
        PlaceWalls();
        PostProcessWallGlyphs();   // replace '#' with box-drawing chars

        var rooms = new List<(BspNode node, RoomRect rect)>();
        CollectRooms(root, rooms);
        if (rooms.Count == 0) return _map;

        TagRooms(rooms);
        PlaceStairs(rooms);
        PlaceSpawnPoints(rooms);
        PlaceDoors();
        PlaceChestsAndTraps(rooms);
        AddScenery();
        return _map;
    }

    // ── BSP splitting ────────────────────────────────────────────────────
    // MinSplitSize: both axes need at least this many cells before we'll try
    // to split along that axis.  Keeps leaf nodes large enough for rooms.
    private const int MinSplitSize = 12;

    private void SplitNode(BspNode node, int depth)
    {
        bool canH = node.H >= MinSplitSize;
        bool canV = node.W >= MinSplitSize;
        if (depth >= 6 || (!canH && !canV)) return;

        bool splitH;
        if (canH && canV)
            splitH = node.H > node.W || (node.W == node.H && _rng.NextDouble() > 0.5);
        else
            splitH = canH;   // only one axis is large enough

        if (splitH)
        {
            // margin of 6 on each side guarantees children have H >= 6
            int split = _rng.Next(6, node.H - 6 + 1);   // inclusive range [6 .. H-6]
            node.Left  = new BspNode(node.X, node.Y, node.W, split);
            node.Right = new BspNode(node.X, node.Y + split, node.W, node.H - split);
        }
        else
        {
            int split = _rng.Next(6, node.W - 6 + 1);
            node.Left  = new BspNode(node.X, node.Y, split, node.H);
            node.Right = new BspNode(node.X + split, node.Y, node.W - split, node.H);
        }
        SplitNode(node.Left,  depth + 1);
        SplitNode(node.Right, depth + 1);
    }

    private void CreateRooms(BspNode node)
    {
        if (node.Left == null && node.Right == null)
        {
            // Minimum room: 5 wide × 4 tall. Skip leaf nodes that are too small.
            // (node.W - 2) must be >= 5  →  node.W >= 7
            // (node.H - 2) must be >= 4  →  node.H >= 6
            if (node.W < 7 || node.H < 6) return;

            int maxRw = Math.Min(node.W - 2, 20);
            int maxRh = Math.Min(node.H - 2, 16);
            // Next(min, max) where min == max returns min, so this is always safe.
            int rw = _rng.Next(5, maxRw + 1);   // [5 .. maxRw]
            int rh = _rng.Next(4, maxRh + 1);   // [4 .. maxRh]

            // Margin so the room doesn't touch the node edge.
            int maxOfsX = Math.Max(1, node.W - rw - 1);
            int maxOfsY = Math.Max(1, node.H - rh - 1);
            int rx = node.X + _rng.Next(1, maxOfsX + 1);
            int ry = node.Y + _rng.Next(1, maxOfsY + 1);

            node.Room = new RoomRect(rx, ry, rw, rh);
            CarveRoom(node.Room.Value);
            return;
        }
        if (node.Left  != null) CreateRooms(node.Left);
        if (node.Right != null) CreateRooms(node.Right);
    }

    private void CarveRoom(RoomRect r)
    {
        for (int y = r.Y; y < r.Y + r.Height; y++)
        for (int x = r.X; x < r.X + r.Width;  x++)
            _map.SetTile(x, y, Tile.CreateFloor(_theme, _rng));
    }

    private void ConnectRooms(BspNode node)
    {
        if (node.Left == null || node.Right == null) return;
        ConnectRooms(node.Left);
        ConnectRooms(node.Right);
        var l = GetRoom(node.Left);
        var r = GetRoom(node.Right);
        if (l == null || r == null) return;
        var p1 = new SadRogue.Primitives.Point(l.Value.X + l.Value.Width / 2, l.Value.Y + l.Value.Height / 2);
        var p2 = new SadRogue.Primitives.Point(r.Value.X + r.Value.Width / 2, r.Value.Y + r.Value.Height / 2);
        CarveCorridorL(p1, p2);
    }

    private void CarveCorridorL(SadRogue.Primitives.Point a, SadRogue.Primitives.Point b)
    {
        int x = a.X, y = a.Y;
        while (x != b.X) { _map.SetTile(x, y, Tile.CreateFloor(_theme, _rng)); x += (b.X > x) ? 1 : -1; }
        while (y != b.Y) { _map.SetTile(x, y, Tile.CreateFloor(_theme, _rng)); y += (b.Y > y) ? 1 : -1; }
        _map.SetTile(x, y, Tile.CreateFloor(_theme, _rng));
    }

    private RoomRect? GetRoom(BspNode node)
    {
        if (node.Room != null) return node.Room;
        var l = node.Left  != null ? GetRoom(node.Left)  : null;
        var r = node.Right != null ? GetRoom(node.Right) : null;
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
                if (_map.GetTile(x+dx, y+dy).Type == TileType.Floor) adj = true;
            }
            if (adj) _map.SetTile(x, y, Tile.CreateWall(_theme));
        }
    }

    // ── Special room tagging ─────────────────────────────────────────────
    private void TagRooms(List<(BspNode node, RoomRect rect)> rooms)
    {
        rooms[0].node.Tag = RoomTag.Start;
        if (rooms.Count > 2)
        {
            rooms[rooms.Count - 1].node.Tag = RoomTag.Boss;
            if (rooms.Count > 4)
            {
                int ti = _rng.Next(1, rooms.Count - 2);
                rooms[ti].node.Tag = RoomTag.Treasury;
            }
            if (rooms.Count > 5)
            {
                int si = _rng.Next(1, rooms.Count - 2);
                rooms[si].node.Tag = RoomTag.Shrine;
            }
        }
    }

    // ── Stairs ───────────────────────────────────────────────────────────
    private void PlaceStairs(List<(BspNode node, RoomRect rect)> rooms)
    {
        var start = rooms[0].rect;
        _map.StartPosition = new SadRogue.Primitives.Point(
            start.X + start.Width / 2, start.Y + start.Height / 2);

        // Stairs up in start room (except floor 1)
        if (_floor > 1)
        {
            int ux = start.X + 2, uy = start.Y + 2;
            _map.SetTile(ux, uy, Tile.CreateStairsUp());
            _map.StairsUpPos = new SadRogue.Primitives.Point(ux, uy);
        }

        // Stairs down in last room (boss room)
        if (rooms.Count > 1)
        {
            var last = rooms[rooms.Count - 1].rect;
            int dx = last.X + last.Width / 2, dy = last.Y + last.Height / 2;
            _map.SetTile(dx, dy, Tile.CreateStairsDown());
            _map.StairsDownPos = new SadRogue.Primitives.Point(dx, dy);
        }
    }

    // ── Enemy / Item spawn ───────────────────────────────────────────────
    private void PlaceSpawnPoints(List<(BspNode node, RoomRect rect)> rooms)
    {
        int difficulty = _floor; // scales with floor

        for (int i = 1; i < rooms.Count; i++)
        {
            var (node, rect) = rooms[i];
            if (rect.Width < 3 || rect.Height < 3) continue;   // safety guard

            bool isBoss = node.Tag == RoomTag.Boss;

            int enemyCount = isBoss
                ? 1 + difficulty / 2   // boss room: 1 strong enemy + extras
                : _rng.Next(1, 2 + difficulty / 3);

            for (int e = 0; e < enemyCount; e++)
            {
                int ex = rect.X + _rng.Next(1, rect.Width  - 1);
                int ey = rect.Y + _rng.Next(1, rect.Height - 1);
                if (_map.IsWalkable(ex, ey))
                    _map.EnemySpawnPoints.Add(new SadRogue.Primitives.Point(ex, ey));
            }

            // Items only in normal / treasury rooms
            if (node.Tag != RoomTag.Boss)
            {
                int itemCount = node.Tag == RoomTag.Treasury ? _rng.Next(2, 5) : (_rng.NextDouble() > 0.5 ? 1 : 0);
                for (int it = 0; it < itemCount; it++)
                {
                    int ix = rect.X + _rng.Next(1, rect.Width  - 1);
                    int iy = rect.Y + _rng.Next(1, rect.Height - 1);
                    if (_map.IsWalkable(ix, iy))
                        _map.ItemSpawnPoints.Add(new SadRogue.Primitives.Point(ix, iy));
                }
            }
        }
    }

    // ── Doors ────────────────────────────────────────────────────────────
    private void PlaceDoors()
    {
        for (int y = 1; y < _height - 1; y++)
        for (int x = 1; x < _width  - 1; x++)
        {
            if (_map.GetTile(x, y).Type != TileType.Floor) continue;
            // A tile is a good door candidate if it has walls on two opposite sides (corridor choke)
            bool hChoke = _map.GetTile(x, y-1).Type == TileType.Wall && _map.GetTile(x, y+1).Type == TileType.Wall;
            bool vChoke = _map.GetTile(x-1, y).Type == TileType.Wall && _map.GetTile(x+1, y).Type == TileType.Wall;

            if ((hChoke || vChoke) && _rng.NextDouble() < 0.20)
            {
                bool locked = _rng.NextDouble() < 0.10;
                var door = Tile.CreateDoor(false);
                door.IsWalkable = locked ? false : false; // closed doors block movement
                _map.SetTile(x, y, door);
                _map.DoorPositions.Add(new SadRogue.Primitives.Point(x, y));
            }
        }
    }

    // ── Chests & Traps ───────────────────────────────────────────────────
    private void PlaceChestsAndTraps(List<(BspNode node, RoomRect rect)> rooms)
    {
        foreach (var (node, rect) in rooms.Skip(1))
        {
            if (rect.Width < 3 || rect.Height < 3) continue;   // safety guard

            // Chests in treasury and occasionally normal rooms
            if (node.Tag == RoomTag.Treasury || _rng.NextDouble() < 0.15)
            {
                int cx = rect.X + _rng.Next(1, rect.Width - 1);
                int cy = rect.Y + _rng.Next(1, rect.Height - 1);
                if (_map.IsWalkable(cx, cy))
                {
                    _map.SetTile(cx, cy, Tile.CreateChest());
                    _map.ChestPositions.Add(new SadRogue.Primitives.Point(cx, cy));
                }
            }

            // Traps in corridors / normal rooms
            if (node.Tag == RoomTag.Normal && _rng.NextDouble() < 0.20)
            {
                int tx = rect.X + _rng.Next(1, rect.Width - 1);
                int ty = rect.Y + _rng.Next(1, rect.Height - 1);
                if (_map.IsWalkable(tx, ty) && _map.GetTile(tx, ty).Type == TileType.Floor)
                {
                    _map.SetTile(tx, ty, Tile.CreateTrap());
                    _map.TrapPositions.Add(new SadRogue.Primitives.Point(tx, ty));
                }
            }
        }
    }

    // ── Scenery ──────────────────────────────────────────────────────────
    private void AddScenery()
    {
        for (int y = 0; y < _height; y++)
        for (int x = 0; x < _width;  x++)
        {
            if (_map.GetTile(x, y).Type != TileType.Floor) continue;
            double r = _rng.NextDouble();
            if      (r < 0.005) _map.SetTile(x, y, Tile.CreateRock());
            else if (r < 0.015 && _theme != MapTheme.Crypt) _map.SetTile(x, y, Tile.CreateBush());
            else if (r < 0.020 && _theme == MapTheme.Cave)  _map.SetTile(x, y, Tile.CreateWater());
        }
    }

    // ── Wall glyph post-processing ───────────────────────────────────────
    /// <summary>
    /// Replaces each wall tile's '#' glyph with the appropriate box-drawing
    /// character based on its 4 cardinal neighbours.  Forest trees are left
    /// as-is (they don't "connect" to each other like stone walls).
    /// Interior walls (surrounded entirely by other walls / void) are set to
    /// a space so they render as pure dark background — giving a clean look.
    /// </summary>
    private void PostProcessWallGlyphs()
    {
        // Forest trees are standalone ♣ — no half-block 3-D needed.
        if (_theme == MapTheme.Forest) return;

        for (int y = 0; y < _height; y++)
        for (int x = 0; x < _width;  x++)
        {
            var tile = _map.GetTile(x, y);
            if (tile.Type != TileType.Wall) continue;

            // Interior walls (no floor on any of 8 neighbours) → space.
            // They show as pure black — the dungeon feels deep and solid.
            bool anyFloor = false;
            for (int dy = -1; dy <= 1 && !anyFloor; dy++)
            for (int dx = -1; dx <= 1 && !anyFloor; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                if (IsFloor(x + dx, y + dy)) anyFloor = true;
            }
            if (!anyFloor)
            {
                tile.Glyph = ' ';
                continue;
            }

            // Boundary wall → keep the '▄' half-block set by CreateWall().
            // The lighter Background (top face) + darker Foreground (front face)
            // creates a pseudo-3-D raised-block appearance.
            // tile.Glyph is already '▄' — nothing to change.
        }
    }

    private bool IsFloor(int x, int y) =>
        _map.InBounds(x, y) && _map.GetTile(x, y).Type == TileType.Floor;

    private bool IsWallOrVoid(int x, int y) =>
        !_map.InBounds(x, y) || _map.GetTile(x, y).Type is TileType.Wall or TileType.Empty;

    // ── Utilities ────────────────────────────────────────────────────────
    private void CollectRooms(BspNode node, List<(BspNode, RoomRect)> rooms)
    {
        if (node.Room != null) { rooms.Add((node, node.Room.Value)); return; }
        if (node.Left  != null) CollectRooms(node.Left,  rooms);
        if (node.Right != null) CollectRooms(node.Right, rooms);
    }
}
