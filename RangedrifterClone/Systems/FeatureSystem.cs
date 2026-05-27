using RangedrifterClone.Components;
using RangedrifterClone.Core;
using RangedrifterClone.MapSystem;

namespace RangedrifterClone.Systems;

/// <summary>
/// Handles interaction with map features: opening doors, descending stairs,
/// opening chests, triggering traps.
/// </summary>
public class FeatureSystem
{
    private readonly EntityManager _em;
    private readonly MessageLog    _log;
    private static readonly Random _rng = new();

    public event Action<int>? StairsDescended;  // carries target floor number
    public event Action<int>? StairsAscended;

    public FeatureSystem(EntityManager em, MessageLog log) { _em = em; _log = log; }

    /// <summary>Called when the player steps onto or bumps into a tile.</summary>
    public InteractResult TryInteract(Entity actor, int x, int y, GameMap map)
    {
        // Find a feature entity at this position
        foreach (var e in _em.GetEntitiesWith<FeatureComponent, PositionComponent>())
        {
            var fpos = _em.GetComponent<PositionComponent>(e)!;
            if (fpos.X != x || fpos.Y != y) continue;

            var feat = _em.GetComponent<FeatureComponent>(e)!;
            return feat.Type switch
            {
                FeatureType.Door       => InteractDoor(actor, e, feat, map, x, y),
                FeatureType.StairsDown => InteractStairs(actor, feat, goingDown: true),
                FeatureType.StairsUp   => InteractStairs(actor, feat, goingDown: false),
                FeatureType.Chest      => InteractChest(actor, e, feat),
                FeatureType.Trap       => InteractTrap(actor, e, feat),
                _                      => InteractResult.None
            };
        }
        return InteractResult.None;
    }

    public void CheckTrapAtPosition(Entity actor, int x, int y)
    {
        foreach (var e in _em.GetEntitiesWith<FeatureComponent, PositionComponent>())
        {
            var fpos = _em.GetComponent<PositionComponent>(e)!;
            if (fpos.X != x || fpos.Y != y) continue;
            var feat = _em.GetComponent<FeatureComponent>(e)!;
            if (feat.Type == FeatureType.Trap && feat.IsActive)
                InteractTrap(actor, e, feat);
        }
    }

    // ── Specific feature handlers ─────────────────────────────────────────

    private InteractResult InteractDoor(Entity actor, Entity doorEntity, FeatureComponent feat,
        GameMap map, int x, int y)
    {
        if (feat.IsLocked)
        {
            // Check for key in inventory
            var inv = _em.GetComponent<InventoryComponent>(actor);
            bool hasKey = inv?.Items.Any(i => i.Category == "Key") ?? false;
            if (!hasKey) { _log.Add("The door is locked!", SadRogue.Primitives.Color.Red); return InteractResult.Blocked; }
            feat.IsLocked = false;
            inv!.Items.RemoveAll(i => i.Category == "Key");
            _log.Add("You unlock the door with a key.", SadRogue.Primitives.Color.Yellow);
        }
        feat.IsOpen = !feat.IsOpen;
        map.GetTile(x, y).IsWalkable  = feat.IsOpen;
        map.GetTile(x, y).BlocksLight = !feat.IsOpen;
        map.GetTile(x, y).Glyph       = feat.IsOpen ? '/' : '+';
        _log.Add(feat.IsOpen ? "You open the door." : "You close the door.",
            SadRogue.Primitives.Color.Gray);
        return feat.IsOpen ? InteractResult.Opened : InteractResult.Closed;
    }

    private InteractResult InteractStairs(Entity actor, FeatureComponent feat, bool goingDown)
    {
        var pos = _em.GetComponent<PositionComponent>(actor);
        if (pos == null) return InteractResult.None;

        if (goingDown)
        {
            _log.Add($"You descend to floor {feat.TargetFloor}.", SadRogue.Primitives.Color.Cyan);
            StairsDescended?.Invoke(feat.TargetFloor);
            return InteractResult.StairsUsed;
        }
        else
        {
            _log.Add($"You ascend to floor {feat.TargetFloor}.", SadRogue.Primitives.Color.Cyan);
            StairsAscended?.Invoke(feat.TargetFloor);
            return InteractResult.StairsUsed;
        }
    }

    private InteractResult InteractChest(Entity actor, Entity chestEntity, FeatureComponent feat)
    {
        if (!feat.IsActive) { _log.Add("The chest is empty.", SadRogue.Primitives.Color.Gray); return InteractResult.None; }

        if (feat.IsTrapped)
        {
            feat.IsTrapped = false;
            var fighter = _em.GetComponent<FighterComponent>(actor);
            int dmg = _rng.Next(3, 8);
            if (fighter != null) fighter.Hp -= dmg;
            _log.Add($"The chest was trapped! You take {dmg} damage.", SadRogue.Primitives.Color.Red);
        }

        var inv = _em.GetComponent<InventoryComponent>(actor);
        if (inv != null && inv.Items.Count < inv.MaxItems)
        {
            foreach (var id in feat.ChestContents)
            {
                inv.Items.Add(new InventoryEntry { ItemId = id, Name = id, Category = "Misc" });
            }
            if (feat.ChestContents.Count > 0)
                _log.Add($"Chest contains: {string.Join(", ", feat.ChestContents)}.",
                    SadRogue.Primitives.Color.Yellow);
        }

        feat.IsActive = false;
        _em.GetComponent<RenderComponent>(chestEntity)!.Glyph = '_';
        return InteractResult.Opened;
    }

    private InteractResult InteractTrap(Entity actor, Entity trapEntity, FeatureComponent feat)
    {
        if (!feat.IsActive) return InteractResult.None;

        feat.IsRevealed = true;
        feat.IsActive   = false;
        var fighter = _em.GetComponent<FighterComponent>(actor);
        var fx      = _em.GetComponent<StatusEffectComponent>(actor);

        switch (feat.TrapKind)
        {
            case TrapType.Spike:
                int dmg = _rng.Next(feat.TrapDamage, feat.TrapDamage * 2);
                if (fighter != null) fighter.Hp -= dmg;
                _log.Add($"You triggered a spike trap! {dmg} damage!", SadRogue.Primitives.Color.Red);
                break;
            case TrapType.Poison:
                fx?.Apply(EffectType.Poisoning, 1, 8);
                _log.Add("You triggered a poison trap! You are poisoned!", new SadRogue.Primitives.Color(100, 200, 80));
                break;
            case TrapType.Fire:
                int fdmg = _rng.Next(feat.TrapDamage - 1, feat.TrapDamage + 2);
                if (fighter != null) fighter.Hp -= fdmg;
                fx?.Apply(EffectType.Burning, 1, 3);
                _log.Add($"Fire trap! {fdmg} damage + burning!", new SadRogue.Primitives.Color(255, 140, 0));
                break;
            case TrapType.Alarm:
                _log.Add("You triggered an alarm! Nearby enemies are alerted!", SadRogue.Primitives.Color.Yellow);
                AlertNearbyEnemies(actor, 8);
                break;
        }
        return InteractResult.TrapTriggered;
    }

    private void AlertNearbyEnemies(Entity actor, int radius)
    {
        var pos = _em.GetComponent<PositionComponent>(actor);
        if (pos == null) return;
        foreach (var e in _em.GetEntitiesWith<AIComponent, PositionComponent>())
        {
            var epos = _em.GetComponent<PositionComponent>(e)!;
            int dist = Math.Abs(epos.X - pos.X) + Math.Abs(epos.Y - pos.Y);
            if (dist <= radius)
            {
                var ai = _em.GetComponent<AIComponent>(e)!;
                ai.IsAware         = true;
                ai.State           = AIState.Hunting;
                ai.LastKnownPlayerPos = pos.Point;
            }
        }
    }
}

public enum InteractResult { None, Opened, Closed, Blocked, StairsUsed, TrapTriggered }
