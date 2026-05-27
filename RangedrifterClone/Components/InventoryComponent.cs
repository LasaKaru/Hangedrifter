namespace RangedrifterClone.Components;

/// <summary>Item bag. Equipment slots are in EquipmentSlotComponent.</summary>
public class InventoryComponent
{
    public List<InventoryEntry> Items   { get; } = new();
    public int                  MaxItems{ get; set; } = 16;
}

public class InventoryEntry
{
    public string   ItemId      { get; set; } = "";
    public string   Name        { get; set; } = "";
    public string   Category    { get; set; } = "Misc";
    public int      Count       { get; set; } = 1;
    public int      Value       { get; set; } = 0;
    public char     Glyph       { get; set; } = '?';
    public SadRogue.Primitives.Color Color { get; set; } = SadRogue.Primitives.Color.White;

    // Equipment stats (0 for non-equipment items)
    public int BonusDamage  { get; set; } = 0;
    public int BonusDefense { get; set; } = 0;
    public int BonusMaxHp   { get; set; } = 0;
    public int BonusMaxMana { get; set; } = 0;

    // Consumable effect
    public string UseEffect  { get; set; } = "";   // e.g. "Heal:8", "Mana:5", "Regen:3:5"
}
