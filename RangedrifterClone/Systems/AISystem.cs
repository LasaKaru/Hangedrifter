using RangedrifterClone.Components;
using RangedrifterClone.Core;
using RangedrifterClone.MapSystem;

namespace RangedrifterClone.Systems;

public class AISystem
{
    private readonly EntityManager _em;
    private readonly MessageLog _log;
    private readonly AStarPathfinder _pathfinder = new();

    public AISystem(EntityManager em, MessageLog log) { _em = em; _log = log; }

    public void ProcessTurns(GameMap map, Entity player,
        CombatSystem combat, MovementSystem movement)
    {
        var playerPos = _em.GetComponent<PositionComponent>(player);
        if (playerPos == null) return;

        foreach (var entity in _em.GetEntitiesWith<AIComponent, PositionComponent, FighterComponent>().ToList())
        {
            var ai  = _em.GetComponent<AIComponent>(entity)!;
            var pos = _em.GetComponent<PositionComponent>(entity)!;

            if (ai.Behavior == AIBehavior.Passive) continue;

            var tile = map.GetTile(pos.X, pos.Y);
            int distX = Math.Abs(pos.X - playerPos.X);
            int distY = Math.Abs(pos.Y - playerPos.Y);

            // Become aware if visible
            if (tile.IsVisible) ai.IsAware = true;

            if (!ai.IsAware) continue;

            if (distX <= 1 && distY <= 1)
            {
                // Adjacent — attack player
                combat.Attack(entity, player);
            }
            else
            {
                // Pathfind toward player
                var path = _pathfinder.FindPath(map,
                    new SadRogue.Primitives.Point(pos.X, pos.Y),
                    new SadRogue.Primitives.Point(playerPos.X, playerPos.Y), 80);

                if (path.Count > 0)
                {
                    int dx = path[0].X - pos.X;
                    int dy = path[0].Y - pos.Y;
                    movement.TryMoveEntity(entity, dx, dy, map);
                }
            }
        }
    }
}
