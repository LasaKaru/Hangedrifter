namespace RangedrifterClone.Components;

public class StatusComponent
{
    public int Turn { get; set; } = 0;
    public bool IsHungry { get; set; } = false;
    public bool IsPoisoned { get; set; } = false;
    public bool IsConfused { get; set; } = false;
    public string EquippedWeaponName { get; set; } = "Fists";
    public string WeaponQuality { get; set; } = "Dull edge";
}
