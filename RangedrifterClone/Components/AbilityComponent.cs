namespace RangedrifterClone.Components;

/// <summary>Holds the entity's active abilities (powers) and tracks cooldowns.</summary>
public class AbilityComponent
{
    public List<Ability> Abilities    { get; } = new();
    public int SelectedIndex          { get; set; } = 0;

    public Ability? Selected =>
        Abilities.Count > 0 ? Abilities[SelectedIndex] : null;

    public void TickCooldowns()
    {
        foreach (var a in Abilities)
            if (a.CurrentCooldown > 0) a.CurrentCooldown--;
    }
}

public class Ability
{
    public string      Id              { get; set; } = "";
    public string      Name            { get; set; } = "";
    public string      Description     { get; set; } = "";
    public char        Glyph           { get; set; } = '*';
    public SadRogue.Primitives.Color Color { get; set; } = SadRogue.Primitives.Color.White;
    public int         ManaCost        { get; set; } = 0;
    public int         MaxCooldown     { get; set; } = 0;
    public int         CurrentCooldown { get; set; } = 0;
    public AbilityType Type            { get; set; } = AbilityType.MeleeAttack;
    public int         Power           { get; set; } = 5;
    public int         Range           { get; set; } = 1;
    public int         Radius          { get; set; } = 0;    // 0 = single target
    public int         DurationTurns   { get; set; } = 0;    // for buff/debuff
    public string      StatusEffect    { get; set; } = "";   // e.g. "Poisoned"
    public bool        IsReady         => CurrentCooldown == 0;
}

public enum AbilityType
{
    MeleeAttack,   // hit adjacent enemies for Power damage
    RangedAttack,  // projectile to Range tiles
    AoEDamage,     // damages all enemies within Radius
    Heal,          // restores Power HP
    Buff,          // applies positive status to self
    Debuff,        // applies negative status to target
    Teleport,      // blink to target tile within Range
    Dash,          // move Range tiles in a direction, damaging anything in path
}
