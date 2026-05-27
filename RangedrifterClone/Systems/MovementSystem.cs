using RangedrifterClone.Components;
using RangedrifterClone.Core;
using RangedrifterClone.MapSystem;

namespace RangedrifterClone.Systems;

public class MovementSystem
{
    private readonly EntityManager _em;
    public MovementSystem(EntityManager em) { _em = em; }

    public bool TryMovePlayer(Entity player, int dx, int dy, GameMap map,
        CombatSystem combat, InventorySystem inventory)
    {
        if (dx == 0 && dy == 0) return true; // wait

        var pos = _em.GetComponent<PositionComponent>(player);
        if (pos == null) return false;

        int nx = pos.X + dx, ny = pos.Y + dy;

        // Check for enemy at target — attack instead of move
        var target = GetEntityAt(nx, ny);
        if (target.IsValid && _em.HasComponent<FighterComponent>(target)
                           && _em.HasComponent<AIComponent>(target))
        {
            combat.Attack(player, target);
            return true;
        }

        if (!map.IsWalkable(nx, ny)) return false;

        pos.X = nx;
        pos.Y = ny;
        return true;
    }

    public bool TryMoveEntity(Entity entity, int dx, int dy, GameMap map)
    {
        var pos = _em.GetComponent<PositionComponent>(entity);
        if (pos == null) return false;

        int nx = pos.X + dx, ny = pos.Y + dy;
        if (!map.IsWalkable(nx, ny)) return false;

        // Don't walk into another entity
        var blocker = GetEntityAt(nx, ny);
        if (blocker.IsValid && blocker != entity) return false;

        pos.X = nx;
        pos.Y = ny;
        return true;
    }

    public Entity GetEntityAt(int x, int y)
    {
        foreach (var e in _em.GetEntitiesWith<PositionComponent>())
        {
            var p = _em.GetComponent<PositionComponent>(e)!;
            if (p.X == x && p.Y == y) return e;
        }
        return Entity.None;
    }
}
