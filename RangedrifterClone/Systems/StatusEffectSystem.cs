using RangedrifterClone.Components;
using RangedrifterClone.Core;

namespace RangedrifterClone.Systems;

/// <summary>
/// Ticks all active status effects each turn, applying damage/healing and
/// counting down durations. Removes expired effects.
/// </summary>
public class StatusEffectSystem
{
    private readonly EntityManager _em;
    private readonly MessageLog    _log;

    public StatusEffectSystem(EntityManager em, MessageLog log) { _em = em; _log = log; }

    public void ProcessAll()
    {
        foreach (var entity in _em.GetEntitiesWith<StatusEffectComponent, FighterComponent>().ToList())
        {
            var fx      = _em.GetComponent<StatusEffectComponent>(entity)!;
            var fighter = _em.GetComponent<FighterComponent>(entity)!;
            var name    = _em.GetComponent<NameComponent>(entity)?.Name ?? "Unknown";

            for (int i = fx.Effects.Count - 1; i >= 0; i--)
            {
                var effect = fx.Effects[i];
                switch (effect.Type)
                {
                    case EffectType.Poisoned:
                    case EffectType.Poisoning:
                        fighter.Hp -= effect.Magnitude;
                        _log.Add($"{name} takes {effect.Magnitude} poison damage.",
                            new SadRogue.Primitives.Color(100, 200, 80));
                        break;

                    case EffectType.Burning:
                        fighter.Hp -= effect.Magnitude;
                        _log.Add($"{name} burns for {effect.Magnitude} damage.",
                            new SadRogue.Primitives.Color(255, 140, 0));
                        break;

                    case EffectType.Regenerating:
                        int healed = Math.Min(effect.Magnitude, fighter.MaxHp - fighter.Hp);
                        fighter.Hp += healed;
                        if (healed > 0)
                            _log.Add($"{name} regenerates {healed} HP.",
                                SadRogue.Primitives.Color.LightGreen);
                        break;
                }

                effect.Duration--;
                if (effect.Duration <= 0)
                {
                    _log.Add($"{name} is no longer {EffectName(effect.Type)}.",
                        SadRogue.Primitives.Color.Gray);
                    fx.Effects.RemoveAt(i);
                }
            }

            // Check death from status effects
            if (fighter.Hp <= 0 && _em.HasComponent<AIComponent>(entity))
            {
                _log.Add($"{name} died from status effects!", SadRogue.Primitives.Color.Orange);
                _em.DestroyEntity(entity);
            }
        }
    }

    private static string EffectName(EffectType t) => t switch
    {
        EffectType.Poisoned     => "Poisoned",
        EffectType.Poisoning    => "Poisoned",
        EffectType.Burning      => "Burning",
        EffectType.Frozen       => "Frozen",
        EffectType.Stunned      => "Stunned",
        EffectType.Hasted       => "Hasted",
        EffectType.Blessed      => "Blessed",
        EffectType.Cursed       => "Cursed",
        EffectType.Slowed       => "Slowed",
        EffectType.Regenerating => "Regenerating",
        _                       => t.ToString()
    };
}
