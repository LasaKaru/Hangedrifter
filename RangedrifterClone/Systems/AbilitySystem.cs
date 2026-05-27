using RangedrifterClone.Components;
using RangedrifterClone.Core;
using RangedrifterClone.MapSystem;

namespace RangedrifterClone.Systems;

/// <summary>
/// Resolves player ability activations. Called by GameEngine when the player
/// presses 1-4. Each AbilityType has its own resolution path.
/// </summary>
public class AbilitySystem
{
    private readonly EntityManager _em;
    private readonly MessageLog    _log;
    private static readonly Random _rng = new();

    public AbilitySystem(EntityManager em, MessageLog log) { _em = em; _log = log; }

    /// <summary>
    /// Attempt to use the ability at <paramref name="index"/> for <paramref name="actor"/>.
    /// Returns true if an action was consumed.
    /// </summary>
    public bool UseAbility(Entity actor, int index, GameMap map,
        CombatSystem combat, int targetDx = 0, int targetDy = 0)
    {
        var abilities = _em.GetComponent<AbilityComponent>(actor);
        var mana      = _em.GetComponent<ManaComponent>(actor);
        if (abilities == null || index >= abilities.Abilities.Count) return false;

        var ability = abilities.Abilities[index];

        if (!ability.IsReady)
        {
            _log.Add($"{ability.Name} is on cooldown ({ability.CurrentCooldown} turns).",
                SadRogue.Primitives.Color.Red);
            return false;
        }
        if (mana != null && !mana.CanAfford(ability.ManaCost))
        {
            _log.Add($"Not enough mana for {ability.Name}! ({mana.Mana}/{ability.ManaCost})",
                SadRogue.Primitives.Color.Red);
            return false;
        }

        mana?.Spend(ability.ManaCost);
        ability.CurrentCooldown = ability.MaxCooldown;

        switch (ability.Type)
        {
            case AbilityType.MeleeAttack:  UseMelee(actor, ability, map, combat); break;
            case AbilityType.AoEDamage:    UseAoE(actor, ability, map, combat);   break;
            case AbilityType.Heal:         UseHeal(actor, ability);               break;
            case AbilityType.Buff:         UseBuff(actor, ability);               break;
            case AbilityType.Debuff:       UseDebuff(actor, ability, map, combat, targetDx, targetDy); break;
            case AbilityType.RangedAttack: UseRanged(actor, ability, map, combat, targetDx, targetDy); break;
            case AbilityType.Teleport:     UseTeleport(actor, ability, map, targetDx, targetDy); break;
            case AbilityType.Dash:         UseDash(actor, ability, map, combat, targetDx, targetDy); break;
        }

        return true;
    }

    // ── Individual ability resolvers ───────────────────────────────────────

    private void UseMelee(Entity actor, Ability ability, GameMap map, CombatSystem combat)
    {
        var pos = _em.GetComponent<PositionComponent>(actor);
        if (pos == null) return;

        bool hit = false;
        // Hit all adjacent enemies
        for (int dy = -1; dy <= 1; dy++)
        for (int dx = -1; dx <= 1; dx++)
        {
            if (dx == 0 && dy == 0) continue;
            var target = GetEntityAt(pos.X + dx, pos.Y + dy);
            if (target.IsValid && _em.HasComponent<FighterComponent>(target)
                               && _em.HasComponent<AIComponent>(target))
            {
                combat.AttackWithBonus(actor, target, ability.Power);
                hit = true;
            }
        }
        if (!hit) _log.Add($"{ability.Name} — no adjacent targets.", SadRogue.Primitives.Color.Yellow);
        else      _log.Add($"Used {ability.Name}!", ability.Color);
    }

    private void UseAoE(Entity actor, Ability ability, GameMap map, CombatSystem combat)
    {
        var pos = _em.GetComponent<PositionComponent>(actor);
        if (pos == null) return;

        int killed = 0;
        foreach (var e in _em.GetEntitiesWith<AIComponent, PositionComponent, FighterComponent>().ToList())
        {
            var epos = _em.GetComponent<PositionComponent>(e)!;
            int dist = Math.Abs(epos.X - pos.X) + Math.Abs(epos.Y - pos.Y);
            if (dist <= ability.Radius)
            {
                combat.AttackWithBonus(actor, e, ability.Power);
                killed++;
            }
        }
        _log.Add($"{ability.Name}! Hit {killed} enemies in radius {ability.Radius}.", ability.Color);
    }

    private void UseHeal(Entity actor, Ability ability)
    {
        var fighter = _em.GetComponent<FighterComponent>(actor);
        if (fighter == null) return;
        int healed = Math.Min(ability.Power, fighter.MaxHp - fighter.Hp);
        fighter.Hp += healed;
        _log.Add($"{ability.Name}: Restored {healed} HP. ({fighter.Hp}/{fighter.MaxHp})",
            SadRogue.Primitives.Color.LightGreen);
    }

    private void UseBuff(Entity actor, Ability ability)
    {
        var fx = _em.GetComponent<StatusEffectComponent>(actor);
        if (fx == null) return;
        if (Enum.TryParse<EffectType>(ability.StatusEffect, out var eff))
        {
            fx.Apply(eff, ability.Power, ability.DurationTurns > 0 ? ability.DurationTurns : 5);
            _log.Add($"{ability.Name}: {ability.StatusEffect} for {ability.DurationTurns} turns!", ability.Color);
        }
    }

    private void UseDebuff(Entity actor, Ability ability, GameMap map,
        CombatSystem combat, int dx, int dy)
    {
        var pos = _em.GetComponent<PositionComponent>(actor);
        if (pos == null) return;

        var target = GetEntityAt(pos.X + dx, pos.Y + dy);
        if (!target.IsValid || !_em.HasComponent<FighterComponent>(target))
        {
            _log.Add($"{ability.Name} — no target.", SadRogue.Primitives.Color.Yellow);
            return;
        }
        var fx = _em.GetComponent<StatusEffectComponent>(target);
        if (fx == null) return;
        if (Enum.TryParse<EffectType>(ability.StatusEffect, out var eff))
        {
            fx.Apply(eff, ability.Power, ability.DurationTurns > 0 ? ability.DurationTurns : 3);
            var name = _em.GetComponent<NameComponent>(target)?.Name ?? "Enemy";
            _log.Add($"{ability.Name}: {name} is now {ability.StatusEffect}!", ability.Color);
        }
    }

    private void UseRanged(Entity actor, Ability ability, GameMap map,
        CombatSystem combat, int dx, int dy)
    {
        var pos = _em.GetComponent<PositionComponent>(actor);
        if (pos == null) return;

        // Trace a line from actor in direction dx/dy up to Range tiles
        int cx = pos.X + dx, cy = pos.Y + dy;
        for (int step = 0; step < ability.Range; step++)
        {
            if (!map.InBounds(cx, cy) || map.BlocksLight(cx, cy)) break;
            var hit = GetEntityAt(cx, cy);
            if (hit.IsValid && _em.HasComponent<FighterComponent>(hit)
                            && _em.HasComponent<AIComponent>(hit))
            {
                combat.AttackWithBonus(actor, hit, ability.Power);
                _log.Add($"{ability.Name} hits!", ability.Color);
                return;
            }
            cx += dx; cy += dy;
        }
        _log.Add($"{ability.Name} hits nothing.", SadRogue.Primitives.Color.Yellow);
    }

    private void UseTeleport(Entity actor, Ability ability, GameMap map, int dx, int dy)
    {
        var pos = _em.GetComponent<PositionComponent>(actor);
        if (pos == null) return;

        // Teleport up to Range tiles in direction; land on first walkable free cell
        int tx = pos.X, ty = pos.Y;
        for (int step = 0; step < ability.Range; step++)
        {
            int nx = tx + dx, ny = ty + dy;
            if (!map.InBounds(nx, ny) || !map.IsWalkable(nx, ny)) break;
            if (GetEntityAt(nx, ny).IsValid) break;
            tx = nx; ty = ny;
        }
        pos.X = tx; pos.Y = ty;
        _log.Add($"{ability.Name}! Teleported!", ability.Color);
    }

    private void UseDash(Entity actor, Ability ability, GameMap map,
        CombatSystem combat, int dx, int dy)
    {
        var pos = _em.GetComponent<PositionComponent>(actor);
        if (pos == null) return;

        int moved = 0;
        for (int step = 0; step < ability.Range; step++)
        {
            int nx = pos.X + dx, ny = pos.Y + dy;
            if (!map.InBounds(nx, ny) || !map.IsWalkable(nx, ny)) break;
            var blocker = GetEntityAt(nx, ny);
            if (blocker.IsValid && _em.HasComponent<FighterComponent>(blocker))
            {
                combat.AttackWithBonus(actor, blocker, ability.Power);
                break;
            }
            pos.X = nx; pos.Y = ny;
            moved++;
        }
        _log.Add($"{ability.Name}! Dashed {moved} tiles!", ability.Color);
    }

    private Entity GetEntityAt(int x, int y)
    {
        foreach (var e in _em.GetEntitiesWith<PositionComponent>())
        {
            var p = _em.GetComponent<PositionComponent>(e)!;
            if (p.X == x && p.Y == y) return e;
        }
        return Entity.None;
    }
}
