namespace RangedrifterClone.Components;

/// <summary>
/// Represents interactive map features: Doors, Stairs, Chests, Traps.
/// One component covers all to keep the entity count manageable.
/// </summary>
public class FeatureComponent
{
    public FeatureType Type       { get; set; }
    public bool        IsActive   { get; set; } = true;   // false = used/opened/triggered
    public bool        IsRevealed { get; set; } = false;  // for traps

    // Door
    public bool IsOpen  { get; set; } = false;
    public bool IsLocked{ get; set; } = false;

    // Stairs
    public bool GoesDown   { get; set; } = true;
    public int  TargetFloor{ get; set; } = 1;

    // Chest
    public List<string> ChestContents { get; set; } = new();
    public bool         IsTrapped     { get; set; } = false;

    // Trap
    public TrapType TrapKind   { get; set; } = TrapType.Spike;
    public int      TrapDamage { get; set; } = 3;
}

public enum FeatureType { Door, StairsDown, StairsUp, Chest, Trap, HiddenDoor }
public enum TrapType    { Spike, Poison, Fire, Alarm }
