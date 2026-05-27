using RangedrifterClone.Components;
using RangedrifterClone.Core;

namespace RangedrifterClone.Systems;

public class CombatSystem
{
    private readonly EntityManager _em;
    private readonly MessageLog _log;

    public CombatSystem(EntityManager em, MessageLog log) { _em = em; _log = log; }

    public void Attack(Entity attacker, Entity defender)
    {
        var atk = _em.GetComponent<FighterComponent>(attacker);
        var def = _em.GetComponent<FighterComponent>(defender);
        if (atk == null || def == null) return;

        var atkName = _em.GetComponent<NameComponent>(attacker)?.Name ?? "Unknown";
        var defName = _em.GetComponent<NameComponent>(defender)?.Name ?? "Unknown";

        int damage = Math.Max(0, atk.RollDamage() - def.Defense);
        def.Hp -= damage;

        var color = atkName == "Player"
            ? SadRogue.Primitives.Color.LightGreen
            : new SadRogue.Primitives.Color(255, 100, 100);

        _log.Add(damage > 0
            ? $"{atkName} hits {defName} for {damage} damage."
            : $"{atkName} attacks {defName} but does no damage.", color);

        if (def.Hp <= 0)
        {
            _log.Add($"{defName} died!", SadRogue.Primitives.Color.Orange);

            // Grant experience to player attacker
            if (atkName == "Player")
            {
                var exp = _em.GetComponent<ExperienceComponent>(attacker);
                if (exp != null)
                {
                    int xpGain = def.MaxHp * 2;
                    exp.Experience += xpGain;
                    _log.Add($"Gained {xpGain} XP.", SadRogue.Primitives.Color.Cyan);

                    while (exp.Experience >= exp.NextLevelExp)
                    {
                        exp.Level++;
                        exp.NextLevelExp = (int)(exp.NextLevelExp * 1.5);
                        var playerFighter = _em.GetComponent<FighterComponent>(attacker);
                        if (playerFighter != null) { playerFighter.MaxHp += 2; playerFighter.Hp = playerFighter.MaxHp; }
                        _log.Add($"Level up! Now level {exp.Level}!", SadRogue.Primitives.Color.Yellow);
                    }
                }
            }
            _em.DestroyEntity(defender);
        }
    }
}
