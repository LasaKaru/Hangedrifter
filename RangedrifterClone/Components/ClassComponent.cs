namespace RangedrifterClone.Components;

/// <summary>Tracks the player's chosen class and its passive bonuses.</summary>
public class ClassComponent
{
    public PlayerClass Class       { get; set; } = PlayerClass.Warrior;
    public string ClassName        { get; set; } = "Warrior";
    public string ClassDescription { get; set; } = "";

    // Passive modifiers applied on top of base stats
    public int BonusDamage  { get; set; } = 0;
    public int BonusDefense { get; set; } = 0;
    public int BonusMaxHp   { get; set; } = 0;
    public int BonusMaxMana { get; set; } = 0;
    public float CritChance { get; set; } = 0.05f; // 5% default
    public float DodgeChance{ get; set; } = 0.05f;
}

public enum PlayerClass { Warrior, Rogue, Mage }
