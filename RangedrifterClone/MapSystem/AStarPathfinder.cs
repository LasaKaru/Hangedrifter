namespace RangedrifterClone.MapSystem;

public class AStarPathfinder
{
    private class Node
    {
        public int X, Y;
        public float G, H, F;
        public Node? Parent;
        public Node(int x, int y) { X = x; Y = y; }
    }

    public List<SadRogue.Primitives.Point> FindPath(
        GameMap map,
        SadRogue.Primitives.Point start,
        SadRogue.Primitives.Point end,
        int maxSteps = 200)
    {
        var open   = new List<Node>();
        var closed = new HashSet<(int, int)>();

        var startNode = new Node(start.X, start.Y)
            { G = 0, H = Heuristic(start, end) };
        startNode.F = startNode.H;
        open.Add(startNode);

        int steps = 0;
        while (open.Count > 0 && steps++ < maxSteps)
        {
            var current = open.OrderBy(n => n.F).First();
            open.Remove(current);

            if (current.X == end.X && current.Y == end.Y)
                return ReconstructPath(current);

            closed.Add((current.X, current.Y));

            foreach (var (dx, dy) in Neighbors())
            {
                int nx = current.X + dx;
                int ny = current.Y + dy;
                if (!map.InBounds(nx, ny))              continue;
                if (closed.Contains((nx, ny)))           continue;
                if (!map.IsWalkable(nx, ny) && !(nx == end.X && ny == end.Y)) continue;

                float g = current.G + (dx != 0 && dy != 0 ? 1.414f : 1f);
                float h = Heuristic(new SadRogue.Primitives.Point(nx, ny), end);

                var existing = open.FirstOrDefault(n => n.X == nx && n.Y == ny);
                if (existing != null)
                {
                    if (g < existing.G) { existing.G = g; existing.F = g + h; existing.Parent = current; }
                    continue;
                }
                open.Add(new Node(nx, ny) { G = g, H = h, F = g + h, Parent = current });
            }
        }
        return new List<SadRogue.Primitives.Point>();
    }

    private static float Heuristic(SadRogue.Primitives.Point a, SadRogue.Primitives.Point b)
        => MathF.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    private static List<SadRogue.Primitives.Point> ReconstructPath(Node end)
    {
        var path = new List<SadRogue.Primitives.Point>();
        var cur  = end;
        while (cur.Parent != null)
        {
            path.Add(new SadRogue.Primitives.Point(cur.X, cur.Y));
            cur = cur.Parent;
        }
        path.Reverse();
        return path;
    }

    private static IEnumerable<(int, int)> Neighbors() => new[]
    {
        (0, -1), (0, 1), (-1, 0), (1, 0),
        (-1, -1), (1, -1), (-1, 1), (1, 1)
    };
}
