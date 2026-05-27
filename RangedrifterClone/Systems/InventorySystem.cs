using RangedrifterClone.Components;
using RangedrifterClone.Core;
using RangedrifterClone.MapSystem;

namespace RangedrifterClone.Systems;

public class InventorySystem
{
    private readonly EntityManager _em;
    private readonly MessageLog _log;

    public InventorySystem(EntityManager em, MessageLog log) { _em = em; _log = log; }

    public bool TryPickUp(Entity actor, GameMap map)
    {
        var inv = _em.GetComponent<InventoryComponent>(actor);
        var pos = _em.GetComponent<PositionComponent>(actor);
        if (inv == null || pos == null) return false;

        if (inv.Items.Count >= inv.MaxItems)
        {
            _log.Add("Inventory full!", SadRogue.Primitives.Color.Red);
            return false;
        }

        Entity? found = null;
        foreach (var e in _em.GetEntitiesWith<ItemComponent, PositionComponent>())
        {
            var ipos = _em.GetComponent<PositionComponent>(e)!;
            if (ipos.X == pos.X && ipos.Y == pos.Y) { found = e; break; }
        }

        if (found == null || !found.Value.IsValid) return false;

        var item   = _em.GetComponent<ItemComponent>(found.Value)!;
        var render = _em.GetComponent<RenderComponent>(found.Value);

        var existing = inv.Items.FirstOrDefault(i => i.ItemId == item.ItemId);
        if (existing != null)
            existing.Count++;
        else
            inv.Items.Add(new InventoryEntry
            {
                ItemId   = item.ItemId,
                Name     = item.Name,
                Category = item.Category,
                Value    = item.Value,
                Glyph    = render?.Glyph ?? '?',
                Color    = render?.Foreground ?? SadRogue.Primitives.Color.White
            });

        _log.Add($"Picked up {item.Name}.", SadRogue.Primitives.Color.LightGreen);
        _em.DestroyEntity(found.Value);
        return true;
    }
}
