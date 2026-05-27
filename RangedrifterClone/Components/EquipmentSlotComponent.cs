namespace RangedrifterClone.Components;

/// <summary>
/// Tracks what is currently equipped in each slot.
/// Equipment stats are reflected back into FighterComponent by EquipmentSystem.
/// </summary>
public class EquipmentSlotComponent
{
    public EquipmentEntry? Weapon  { get; set; }
    public EquipmentEntry? Armor   { get; set; }
    public EquipmentEntry? Shield  { get; set; }
    public EquipmentEntry? Ring    { get; set; }
    public EquipmentEntry? Amulet  { get; set; }

    public EquipmentEntry? GetSlot(EquipSlot slot) => slot switch
    {
        EquipSlot.Weapon => Weapon,
        EquipSlot.Armor  => Armor,
        EquipSlot.Shield => Shield,
        EquipSlot.Ring   => Ring,
        EquipSlot.Amulet => Amulet,
        _                => null
    };

    public void SetSlot(EquipSlot slot, EquipmentEntry? entry)
    {
        switch (slot)
        {
            case EquipSlot.Weapon: Weapon = entry; break;
            case EquipSlot.Armor:  Armor  = entry; break;
            case EquipSlot.Shield: Shield = entry; break;
            case EquipSlot.Ring:   Ring   = entry; break;
            case EquipSlot.Amulet: Amulet = entry; break;
        }
    }

    public int TotalBonusDamage  => Sum(e => e.BonusDamage);
    public int TotalBonusDefense => Sum(e => e.BonusDefense);
    public int TotalBonusHp      => Sum(e => e.BonusMaxHp);
    public int TotalBonusMana    => Sum(e => e.BonusMaxMana);

    private int Sum(Func<EquipmentEntry, int> selector)
    {
        int total = 0;
        if (Weapon  != null) total += selector(Weapon);
        if (Armor   != null) total += selector(Armor);
        if (Shield  != null) total += selector(Shield);
        if (Ring    != null) total += selector(Ring);
        if (Amulet  != null) total += selector(Amulet);
        return total;
    }
}

public class EquipmentEntry
{
    public string     ItemId       { get; set; } = "";
    public string     Name         { get; set; } = "";
    public EquipSlot  Slot         { get; set; }
    public int        BonusDamage  { get; set; } = 0;
    public int        BonusDefense { get; set; } = 0;
    public int        BonusMaxHp   { get; set; } = 0;
    public int        BonusMaxMana { get; set; } = 0;
    public bool       IsCursed     { get; set; } = false;
    public char       Glyph        { get; set; } = '?';
    public SadRogue.Primitives.Color Color { get; set; } = SadRogue.Primitives.Color.White;
}

public enum EquipSlot { Weapon, Armor, Shield, Ring, Amulet, None }
