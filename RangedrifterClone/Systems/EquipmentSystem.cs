using RangedrifterClone.Components;
using RangedrifterClone.Core;

namespace RangedrifterClone.Systems;

/// <summary>
/// Handles equipping/unequipping items from inventory.
/// After any equipment change, recalculates FighterComponent derived stats.
/// </summary>
public class EquipmentSystem
{
    private readonly EntityManager _em;
    private readonly MessageLog    _log;

    public EquipmentSystem(EntityManager em, MessageLog log) { _em = em; _log = log; }

    /// <summary>Equip an inventory item by index. Auto-unequips the current slot occupant.</summary>
    public bool TryEquip(Entity actor, int invIndex)
    {
        var inv  = _em.GetComponent<InventoryComponent>(actor);
        var equip= _em.GetComponent<EquipmentSlotComponent>(actor);
        if (inv == null || equip == null) return false;
        if (invIndex < 0 || invIndex >= inv.Items.Count) return false;

        var entry = inv.Items[invIndex];
        var slot  = CategoryToSlot(entry.Category);
        if (slot == EquipSlot.None)
        {
            _log.Add($"{entry.Name} cannot be equipped.", SadRogue.Primitives.Color.Red);
            return false;
        }

        // Unequip current occupant back to inventory
        var current = equip.GetSlot(slot);
        if (current != null)
        {
            inv.Items.Add(new InventoryEntry
            {
                ItemId   = current.ItemId,
                Name     = current.Name,
                Category = SlotToCategory(slot),
                Glyph    = current.Glyph,
                Color    = current.Color
            });
            equip.SetSlot(slot, null);
        }

        // Move from inventory to equipment slot
        inv.Items.RemoveAt(invIndex);
        equip.SetSlot(slot, new EquipmentEntry
        {
            ItemId       = entry.ItemId,
            Name         = entry.Name,
            Slot         = slot,
            BonusDamage  = entry.BonusDamage,
            BonusDefense = entry.BonusDefense,
            BonusMaxHp   = entry.BonusMaxHp,
            BonusMaxMana = entry.BonusMaxMana,
            Glyph        = entry.Glyph,
            Color        = entry.Color
        });

        RecalculateStats(actor);
        _log.Add($"Equipped {entry.Name}.", SadRogue.Primitives.Color.LightGreen);

        // Update weapon quality display
        var status = _em.GetComponent<StatusComponent>(actor);
        if (status != null && slot == EquipSlot.Weapon)
            status.EquippedWeaponName = entry.Name;

        return true;
    }

    public void RecalculateStats(Entity actor)
    {
        var fighter = _em.GetComponent<FighterComponent>(actor);
        var equip   = _em.GetComponent<EquipmentSlotComponent>(actor);
        var cls     = _em.GetComponent<ClassComponent>(actor);
        if (fighter == null || equip == null) return;

        fighter.EquipBonusDamage  = equip.TotalBonusDamage  + (cls?.BonusDamage  ?? 0);
        fighter.EquipBonusDefense = equip.TotalBonusDefense + (cls?.BonusDefense ?? 0);

        // Adjust MaxHp (add bonus hp from equipment)
        int hpBonus = equip.TotalBonusHp + (cls?.BonusMaxHp ?? 0);
        fighter.EquipBonusHp = hpBonus;

        // Mana bonus
        var mana = _em.GetComponent<ManaComponent>(actor);
        if (mana != null)
            mana.MaxMana = mana.BaseMana + equip.TotalBonusMana + (cls?.BonusMaxMana ?? 0);
    }

    private static EquipSlot CategoryToSlot(string category) => category switch
    {
        "Weapon" => EquipSlot.Weapon,
        "Armor"  => EquipSlot.Armor,
        "Shield" => EquipSlot.Shield,
        "Ring"   => EquipSlot.Ring,
        "Amulet" => EquipSlot.Amulet,
        _        => EquipSlot.None
    };

    private static string SlotToCategory(EquipSlot slot) => slot switch
    {
        EquipSlot.Weapon => "Weapon",
        EquipSlot.Armor  => "Armor",
        EquipSlot.Shield => "Shield",
        EquipSlot.Ring   => "Ring",
        EquipSlot.Amulet => "Amulet",
        _                => "Misc"
    };
}
