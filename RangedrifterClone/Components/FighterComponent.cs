namespace RangedrifterClone.Components;

/// <summary>Combat statistics. Equipment bonuses are stored separately and
/// summed at attack/defense time via EquipBonusDamage / EquipBonusDefense.</summary>
public class FighterComponent
{
    // ── Base stats ────────────────────────────────────────────────────────
    public int Hp      { get; set; }
    public int MaxHp   { get; set; }
    public int Defense { get; set; }
    public int Strength{ get; set; }

    public int DamageDice  { get; set; } = 1;
    public int DamageSides { get; set; } = 4;
    public int DamageBonus { get; set; } = 0;

    // ── Equipment additive bonuses (recalculated by EquipmentSystem) ──────
    public int EquipBonusDamage  { get; set; } = 0;
    public int EquipBonusDefense { get; set; } = 0;
    public int EquipBonusHp      { get; set; } = 0;

    // ── Effective totals ──────────────────────────────────────────────────
    public int EffectiveDefense => Defense + EquipBonusDefense;
    public int MaxHpTotal       => MaxHp   + EquipBonusHp;

    // ── Dice rolling ──────────────────────────────────────────────────────
    private static readonly Random _rng = new();

    public int RollDamage()
    {
        int total = DamageBonus + EquipBonusDamage;
        for (int i = 0; i < DamageDice; i++)
            total += _rng.Next(1, DamageSides + 1);
        return Math.Max(1, total);
    }

    public int RollDamageWithBonus(int extra)
    {
        int total = DamageBonus + EquipBonusDamage + extra;
        for (int i = 0; i < DamageDice; i++)
            total += _rng.Next(1, DamageSides + 1);
        return Math.Max(1, total);
    }

    public string DamageString
    {
        get
        {
            int bonus = DamageBonus + EquipBonusDamage;
            return $"d{DamageSides}" + (bonus != 0 ? $"+{bonus}" : "");
        }
    }
}
