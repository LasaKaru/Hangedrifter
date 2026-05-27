namespace RangedrifterClone.Components;

public class FighterComponent
{
    public int Hp { get; set; }
    public int MaxHp { get; set; }
    public int Defense { get; set; }
    public int Strength { get; set; }
    public int DamageDice { get; set; } = 1;
    public int DamageSides { get; set; } = 4;
    public int DamageBonus { get; set; } = 0;

    private static readonly Random _rng = new();

    public int RollDamage()
    {
        int total = DamageBonus;
        for (int i = 0; i < DamageDice; i++)
            total += _rng.Next(1, DamageSides + 1);
        return Math.Max(0, total);
    }

    public string DamageString => $"d{DamageSides}" + (DamageBonus != 0 ? $"+{DamageBonus}" : "");
}
