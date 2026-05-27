using RangedrifterClone.Components;
using RangedrifterClone.Core;
using RangedrifterClone.MapSystem;

namespace RangedrifterClone.Systems;

/// <summary>
/// Full AI state machine.
///
/// States:
///  Idle         — wander/patrol; doesn't know about player
///  Alerted      — noticed something (noise, pack call); moving toward it
///  Hunting      — player in FOV; actively chasing + attacking
///  Investigating— player left FOV; moving toward last known position
///  Fleeing      — low HP or Coward archetype; running away from player
///
/// Memory: LastKnownPlayerPos persists across state transitions so the
/// enemy searches the area where the player was last seen, then gives up.
///
/// Pack behavior: when one pack-member enters Hunting state, nearby
/// pack-mates with the same PackTag are alerted.
/// </summary>
public class AISystem
{
    private readonly EntityManager  _em;
    private readonly MessageLog     _log;
    private readonly AStarPathfinder _pf = new();
    private static readonly Random   _rng = new();

    // How many turns an enemy investigates before returning to Idle
    private const int InvestigateTurns = 8;

    public AISystem(EntityManager em, MessageLog log) { _em = em; _log = log; }

    public void ProcessTurns(GameMap map, Entity player,
        CombatSystem combat, MovementSystem movement)
    {
        var playerPos = _em.GetComponent<PositionComponent>(player);
        if (playerPos == null) return;
        var playerFighter = _em.GetComponent<FighterComponent>(player);

        // Snapshot so mutations during iteration are safe
        var enemies = _em.GetEntitiesWith<AIComponent, PositionComponent, FighterComponent>()
            .ToList();

        foreach (var entity in enemies)
        {
            var ai      = _em.GetComponent<AIComponent>(entity)!;
            var pos     = _em.GetComponent<PositionComponent>(entity)!;
            var fighter = _em.GetComponent<FighterComponent>(entity)!;

            if (ai.Behavior == AIBehavior.Passive) continue;

            // Skip if crowd-controlled
            var fx = _em.GetComponent<StatusEffectComponent>(entity);
            if (fx != null && fx.IsCrowdControlled) { DecrementCC(fx); continue; }

            bool playerVisible = map.GetTile(pos.X, pos.Y).IsVisible &&
                                 CanSeePlayer(map, pos, playerPos, ai.AlertRadius);

            // ── State transitions ─────────────────────────────────────────
            UpdateState(entity, ai, fighter, pos, playerPos, playerVisible, map);

            // ── State actions ─────────────────────────────────────────────
            ExecuteState(entity, ai, fighter, pos, playerPos, playerVisible,
                map, player, combat, movement);
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    private void UpdateState(Entity entity, AIComponent ai, FighterComponent fighter,
        PositionComponent pos, PositionComponent playerPos, bool playerVisible, GameMap map)
    {
        // Fleeing check overrides everything
        if (ai.CanFlee || ai.Behavior == AIBehavior.Coward)
        {
            float hpPct = (float)fighter.Hp / fighter.MaxHp;
            if (hpPct <= ai.FleeThreshold && ai.State != AIState.Fleeing)
            {
                ai.State     = AIState.Fleeing;
                ai.StateTimer = 0;
                return;
            }
        }

        switch (ai.State)
        {
            case AIState.Idle:
                if (playerVisible)
                {
                    ai.State              = AIState.Hunting;
                    ai.LastKnownPlayerPos = playerPos.Point;
                    ai.StateTimer         = 0;
                    AlertPackMates(entity, ai, playerPos.Point);
                }
                break;

            case AIState.Alerted:
                ai.StateTimer++;
                if (playerVisible)
                {
                    ai.State              = AIState.Hunting;
                    ai.LastKnownPlayerPos = playerPos.Point;
                    ai.StateTimer         = 0;
                }
                else if (ai.StateTimer > 4)
                {
                    ai.State = AIState.Idle;
                }
                break;

            case AIState.Hunting:
                if (playerVisible)
                {
                    ai.LastKnownPlayerPos = playerPos.Point;
                    ai.TurnsSincePlayerSeen = 0;
                }
                else
                {
                    ai.TurnsSincePlayerSeen++;
                    if (ai.TurnsSincePlayerSeen > 3)
                    {
                        ai.State     = AIState.Investigating;
                        ai.StateTimer = 0;
                    }
                }
                break;

            case AIState.Investigating:
                ai.StateTimer++;
                if (playerVisible)
                {
                    ai.State              = AIState.Hunting;
                    ai.LastKnownPlayerPos = playerPos.Point;
                    ai.StateTimer         = 0;
                }
                else if (ai.StateTimer > InvestigateTurns || ai.LastKnownPlayerPos == null)
                {
                    ai.State              = AIState.Idle;
                    ai.LastKnownPlayerPos = null;
                    ai.StateTimer         = 0;
                }
                break;

            case AIState.Fleeing:
                // Recover once far enough or HP recovered
                float hp = (float)fighter.Hp / fighter.MaxHp;
                if (hp > ai.FleeThreshold + 0.10f)
                    ai.State = playerVisible ? AIState.Hunting : AIState.Idle;
                break;
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    private void ExecuteState(Entity entity, AIComponent ai, FighterComponent fighter,
        PositionComponent pos, PositionComponent playerPos, bool playerVisible,
        GameMap map, Entity player, CombatSystem combat, MovementSystem movement)
    {
        int distX = Math.Abs(pos.X - playerPos.X);
        int distY = Math.Abs(pos.Y - playerPos.Y);
        int dist  = Math.Max(distX, distY); // Chebyshev

        switch (ai.State)
        {
            case AIState.Idle:
                // Wander randomly (20% chance per turn)
                if (_rng.NextDouble() < 0.2)
                    Wander(entity, pos, map, movement);
                break;

            case AIState.Alerted:
                // Move toward last known sound / pack alert position
                if (ai.LastKnownPlayerPos != null)
                    MoveToward(entity, pos, ai.LastKnownPlayerPos.Value, map, movement);
                break;

            case AIState.Hunting:
                if (dist <= ai.AttackRange)
                {
                    if (ai.IsRanged)
                        RangedAttack(entity, player, combat, dist);
                    else
                        combat.Attack(entity, player);
                }
                else
                    MoveToward(entity, pos,
                        new SadRogue.Primitives.Point(playerPos.X, playerPos.Y), map, movement);
                break;

            case AIState.Investigating:
                if (ai.LastKnownPlayerPos != null)
                {
                    if (pos.X == ai.LastKnownPlayerPos.Value.X &&
                        pos.Y == ai.LastKnownPlayerPos.Value.Y)
                        ai.LastKnownPlayerPos = null; // reached it, now idle
                    else
                        MoveToward(entity, pos, ai.LastKnownPlayerPos.Value, map, movement);
                }
                break;

            case AIState.Fleeing:
                FleeFrom(entity, pos, playerPos, map, movement);
                break;
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    private void MoveToward(Entity e, PositionComponent pos,
        SadRogue.Primitives.Point target, GameMap map, MovementSystem movement)
    {
        var path = _pf.FindPath(map,
            new SadRogue.Primitives.Point(pos.X, pos.Y), target, 60);
        if (path.Count > 0)
        {
            int dx = path[0].X - pos.X;
            int dy = path[0].Y - pos.Y;
            movement.TryMoveEntity(e, dx, dy, map);
        }
    }

    private void Wander(Entity e, PositionComponent pos, GameMap map, MovementSystem movement)
    {
        int dx = _rng.Next(-1, 2);
        int dy = _rng.Next(-1, 2);
        if (dx != 0 || dy != 0)
            movement.TryMoveEntity(e, dx, dy, map);
    }

    private void FleeFrom(Entity e, PositionComponent pos, PositionComponent threat,
        GameMap map, MovementSystem movement)
    {
        // Move in direction opposite to the threat
        int dx = Math.Sign(pos.X - threat.X);
        int dy = Math.Sign(pos.Y - threat.Y);
        if (!movement.TryMoveEntity(e, dx, dy, map))
        {
            // Try perpendicular escape
            movement.TryMoveEntity(e, dy,  dx, map);
            movement.TryMoveEntity(e, -dy, -dx, map);
        }
    }

    private void RangedAttack(Entity attacker, Entity defender, CombatSystem combat, int dist)
    {
        // Simple ranged: deal damage if within range (no line-of-sight check for simplicity)
        combat.Attack(attacker, defender);
    }

    private void AlertPackMates(Entity caller, AIComponent ai,
        SadRogue.Primitives.Point playerPos)
    {
        if (string.IsNullOrEmpty(ai.PackTag)) return;
        var callerPos = _em.GetComponent<PositionComponent>(caller);
        if (callerPos == null) return;

        foreach (var e in _em.GetEntitiesWith<AIComponent, PositionComponent>())
        {
            if (e == caller) continue;
            var other = _em.GetComponent<AIComponent>(e)!;
            if (other.PackTag != ai.PackTag) continue;

            var opos = _em.GetComponent<PositionComponent>(e)!;
            int dist = Math.Abs(opos.X - callerPos.X) + Math.Abs(opos.Y - callerPos.Y);
            if (dist <= ai.AlertRadius * 2 && other.State == AIState.Idle)
            {
                other.State              = AIState.Alerted;
                other.LastKnownPlayerPos = playerPos;
                other.StateTimer         = 0;
            }
        }
    }

    private bool CanSeePlayer(GameMap map, PositionComponent from,
        PositionComponent to, int range)
    {
        int dist = Math.Max(Math.Abs(from.X - to.X), Math.Abs(from.Y - to.Y));
        return dist <= range && map.GetTile(to.X, to.Y).IsVisible;
    }

    private static void DecrementCC(StatusEffectComponent fx)
    {
        var frozen  = fx.Effects.FirstOrDefault(e => e.Type == EffectType.Frozen);
        var stunned = fx.Effects.FirstOrDefault(e => e.Type == EffectType.Stunned);
        if (frozen  != null) frozen.Duration--;
        if (stunned != null) stunned.Duration--;
    }
}
