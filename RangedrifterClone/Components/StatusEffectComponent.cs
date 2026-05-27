namespace RangedrifterClone.Components;

/// <summary>Ongoing status effects ticked each turn by StatusEffectSystem.</summary>
public class StatusEffectComponent
{
    public List<ActiveEffect> Effects { get; } = new();

    public bool HasEffect(EffectType type) =>
        Effects.Any(e => e.Type == type);

    public void Apply(EffectType type, int magnitude, int duration)
    {
        var existing = Effects.FirstOrDefault(e => e.Type == type);
        if (existing != null)
        {
            // Refresh duration, keep highest magnitude
            existing.Duration   = Math.Max(existing.Duration, duration);
            existing.Magnitude  = Math.Max(existing.Magnitude, magnitude);
        }
        else
            Effects.Add(new ActiveEffect { Type = type, Magnitude = magnitude, Duration = duration });
    }

    public void Remove(EffectType type) =>
        Effects.RemoveAll(e => e.Type == type);

    public bool IsCrowdControlled =>
        HasEffect(EffectType.Frozen) || HasEffect(EffectType.Stunned);
}

public class ActiveEffect
{
    public EffectType Type      { get; set; }
    public int        Magnitude { get; set; }   // damage per turn, stat modifier, etc.
    public int        Duration  { get; set; }   // turns remaining
}

public enum EffectType
{
    Poisoned,   // -Magnitude HP/turn, green
    Burning,    // -Magnitude HP/turn, orange; spreads
    Frozen,     // can't move for Duration turns, blue
    Stunned,    // skip next turn, grey
    Hasted,     // +1 extra action per turn (future), cyan
    Blessed,    // +Magnitude to all rolls, yellow
    Cursed,     // -Magnitude to all rolls, purple
    Slowed,     // move every 2 turns, dark blue
    Poisoning,  // same as Poisoned but from trap
    Regenerating// +Magnitude HP/turn, bright green
}
