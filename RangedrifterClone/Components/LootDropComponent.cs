namespace RangedrifterClone.Components;

/// <summary>Defines what an enemy drops on death.</summary>
public class LootDropComponent
{
    public List<LootEntry> PossibleDrops { get; set; } = new();
    public float DropChance { get; set; } = 0.35f;
}

public class LootEntry
{
    public string ItemId  { get; set; } = "";
    public float  Weight  { get; set; } = 1.0f;   // relative probability weight
    public int    MinCount{ get; set; } = 1;
    public int    MaxCount{ get; set; } = 1;
}
