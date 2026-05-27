namespace RangedrifterClone.Components;

public class InventoryComponent
{
    public List<InventoryEntry> Items { get; } = new();
    public int MaxItems { get; set; } = 10;
    public int EquippedWeaponIndex { get; set; } = -1;
    public int EquippedArmorIndex { get; set; } = -1;
}

public class InventoryEntry
{
    public string ItemId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public int Count { get; set; } = 1;
    public int Value { get; set; } = 0;
    public char Glyph { get; set; } = '?';
    public SadRogue.Primitives.Color Color { get; set; } = SadRogue.Primitives.Color.White;
}
