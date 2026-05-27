namespace RangedrifterClone.Components;

/// <summary>Mana / Energy resource consumed by active abilities.</summary>
public class ManaComponent
{
    public int Mana             { get; set; }
    public int MaxMana          { get; set; }
    public int BaseMana          { get; set; }
    public int RegenPerTurn     { get; set; } = 1;
    public int TurnsSinceRegen  { get; set; } = 0;

    public bool CanAfford(int cost) => Mana >= cost;

    public void Spend(int cost)  => Mana = Math.Max(0, Mana - cost);
    public void Restore(int amt) => Mana = Math.Min(MaxMana, Mana + amt);

    public void TickRegen()
    {
        TurnsSinceRegen++;
        if (TurnsSinceRegen >= 2) { Restore(RegenPerTurn); TurnsSinceRegen = 0; }
    }
}
