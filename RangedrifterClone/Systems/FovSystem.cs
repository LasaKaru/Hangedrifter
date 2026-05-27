using RangedrifterClone.Components;
using RangedrifterClone.Core;
using RangedrifterClone.MapSystem;

namespace RangedrifterClone.Systems;

/// <summary>Recursive Shadowcasting FOV — industry-standard algorithm for roguelikes.</summary>
public class FovSystem
{
    private readonly EntityManager _em;
    public FovSystem(EntityManager em) { _em = em; }

    public void ComputeFov(GameMap map, Entity viewer, int radius)
    {
        // Reset visibility
        for (int y = 0; y < map.Height; y++)
        for (int x = 0; x < map.Width;  x++)
            map.GetTile(x, y).IsVisible = false;

        var pos = _em.GetComponent<PositionComponent>(viewer);
        if (pos == null) return;

        var origin = map.GetTile(pos.X, pos.Y);
        origin.IsVisible  = true;
        origin.IsExplored = true;

        for (int octant = 0; octant < 8; octant++)
            ScanOctant(map, pos.X, pos.Y, radius, octant, 1, 1.0, 0.0);
    }

    private void ScanOctant(GameMap map, int cx, int cy, int radius, int octant,
        int row, double start, double end)
    {
        if (start < end) return;
        double newStart = 0;

        for (int distance = row; distance <= radius; distance++)
        {
            bool blocked = false;
            for (int col = -distance; col <= 0; col++)
            {
                double leftSlope  = (col - 0.5) / (distance + 0.5);
                double rightSlope = (col + 0.5) / (distance - 0.5);

                if (start < rightSlope) continue;
                if (end   > leftSlope)  break;

                var (dx, dy) = OctantTransform(col, distance, octant);
                int tx = cx + dx, ty = cy + dy;
                if (!map.InBounds(tx, ty)) continue;

                if (dx * dx + dy * dy <= radius * radius)
                {
                    var tile = map.GetTile(tx, ty);
                    tile.IsVisible  = true;
                    tile.IsExplored = true;
                }

                if (blocked)
                {
                    if (map.BlocksLight(tx, ty))
                        newStart = rightSlope;
                    else
                    {
                        blocked = false;
                        start   = newStart;
                    }
                }
                else if (map.BlocksLight(tx, ty) && distance < radius)
                {
                    blocked = true;
                    ScanOctant(map, cx, cy, radius, octant, distance + 1, start, leftSlope);
                    newStart = rightSlope;
                }
            }
            if (blocked) break;
        }
    }

    private static (int dx, int dy) OctantTransform(int col, int row, int octant) => octant switch
    {
        0 => ( col, -row),
        1 => (-row,  col),
        2 => (-row, -col),
        3 => ( col,  row),
        4 => (-col,  row),
        5 => ( row, -col),
        6 => ( row,  col),
        7 => (-col, -row),
        _ => (0, 0)
    };
}
