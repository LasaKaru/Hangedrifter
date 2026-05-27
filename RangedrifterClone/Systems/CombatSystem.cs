using RangedrifterClone.Data;
using RangedrifterClone.Components;
using RangedrifterClone.Core;

namespace RangedrifterClone.Systems;

/// <summary>
/// Handles attack resolution including crit chances, dodge, damage types,
/// status effect application, XP awards and loot drops.
/// </summary>
public class CombatSystem
{
    private readonly EntityManager _em;
    private readonly MessageLog    _log;
    private readonly DataLoader?   _data;   // optional: for loot drops
    private static readonly Random _rng = new();

    public CombatSystem(EntityManager em, MessageLog log, DataLoader? data = null)
    { _em = em; _log = log; _data = data; }

    public void Attack(Entity attacker, Entity defender) =>
        AttackWithBonus(attacker, defender, 0);

    public void AttackWithBonus(Entity attacker, Entity defender, int extraDamage)
    {
        var atk  = _em.GetComponent<FighterComponent>(attacker);
        var def  = _em.GetComponent<FighterComponent>(defender);
        if (atk == null || def == null) return;

        var atkName = _em.GetComponent<NameComponent>(attacker)?.Name ?? "Unknown";
        var defName = _em.GetComponent<NameComponent>(defender)?.Name ?? "Unknown";
        bool isPlayer = atkName == "Player";

        // ── Dodge ──────────────────────────────────────────────────────
        var defClass = _em.GetComponent<ClassComponent>(defender);
        float dodge  = defClass?.DodgeChance ?? 0f;
        if (_rng.NextDouble() < dodge)
        {
            _log.Add($"{defName} dodges {atkName}'s attack!", SadRogue.Primitives.Color.Cyan);
            return;
        }

        // ── Roll damage ────────────────────────────────────────────────
        int raw    = atk.RollDamageWithBonus(extraDamage);
        bool crit  = false;
        var atkClass = _em.GetComponent<ClassComponent>(attacker);
        if (_rng.NextDouble() < (atkClass?.CritChance ?? 0.05f))
        {
            raw  *= 2;
            crit = true;
        }

        int damage = Math.Max(0, raw - def.EffectiveDefense);

        // ── Apply blessed / cursed modifiers ──────────────────────────
        var atkFx = _em.GetComponent<StatusEffectComponent>(attacker);
        if (atkFx?.HasEffect(EffectType.Blessed) == true) damage += 2;
        if (atkFx?.HasEffect(EffectType.Cursed)  == true) damage = Math.Max(0, damage - 2);

        def.Hp -= damage;

        // ── Log ───────────────────────────────────────────────────────
        var color = isPlayer
            ? SadRogue.Primitives.Color.LightGreen
            : new SadRogue.Primitives.Color(255, 100, 100);

        string critStr = crit ? " CRITICAL HIT!" : "";
        if (damage > 0)
            _log.Add($"{atkName} hits {defName} for {damage} dmg.{critStr}", color);
        else
            _log.Add($"{atkName} attacks {defName} — no damage.", color);

        // ── Death ─────────────────────────────────────────────────────
        if (def.Hp <= 0)
        {
            HandleDeath(attacker, defender, defName, atkName, def);
        }
    }

    private void HandleDeath(Entity killer, Entity victim, string victimName,
        string killerName, FighterComponent victimFighter)
    {
        _log.Add($"{victimName} dies!", SadRogue.Primitives.Color.Orange);

        bool killerIsPlayer = killerName == "Player";

        if (killerIsPlayer)
        {
            // XP award
            int xp = victimFighter.MaxHp * 2 + victimFighter.DamageSides;
            var exp = _em.GetComponent<ExperienceComponent>(killer);
            if (exp != null)
            {
                exp.Experience += xp;
                _log.Add($"+{xp} XP", SadRogue.Primitives.Color.Cyan);

                while (exp.Experience >= exp.NextLevelExp)
                {
                    exp.Level++;
                    exp.NextLevelExp = (int)(exp.NextLevelExp * 1.6);
                    var f = _em.GetComponent<FighterComponent>(killer);
                    if (f != null) { f.MaxHp += 3; f.Hp = f.MaxHp; f.DamageBonus++; }
                    var m = _em.GetComponent<ManaComponent>(killer);
                    if (m != null) { m.MaxMana += 2; m.Restore(2); }
                    _log.Add($"LEVEL UP! Now level {exp.Level}! HP+3, Dmg+1",
                        SadRogue.Primitives.Color.Yellow);
                }
            }
        }

        // Loot drop
        DropLoot(victim, killer);

        _em.DestroyEntity(victim);
    }

    private void DropLoot(Entity victim, Entity killer)
    {
        var loot = _em.GetComponent<LootDropComponent>(victim);
        if (loot == null || loot.PossibleDrops.Count == 0) return;
        if (_rng.NextDouble() > loot.DropChance) return;

        // Weighted random pick
        var pos  = _em.GetComponent<PositionComponent>(victim);
        if (pos == null) return;

        float total = loot.PossibleDrops.Sum(e => e.Weight);
        float roll  = (float)_rng.NextDouble() * total;
        LootEntry? picked = null;
        foreach (var entry in loot.PossibleDrops)
        {
            roll -= entry.Weight;
            if (roll <= 0) { picked = entry; break; }
        }
        picked ??= loot.PossibleDrops[^1];

        int count = _rng.Next(picked.MinCount, picked.MaxCount + 1);

        // Spawn item entity at victim's position
        if (_data != null)
        {
            var itemDef = _data.ItemDefinitions.FirstOrDefault(i => i.Id == picked.ItemId);
            if (itemDef != null)
            {
                var item = _em.CreateEntity();
                _em.AddComponent(item, new PositionComponent { X = pos.X, Y = pos.Y });
                _em.AddComponent(item, new RenderComponent
                {
                    Glyph = itemDef.Glyph, Foreground = itemDef.Color,
                    Background = SadRogue.Primitives.Color.Transparent, RenderLayer = 2
                });
                _em.AddComponent(item, new ItemComponent
                {
                    ItemId = itemDef.Id, Name = itemDef.Name,
                    Category = itemDef.Category, Value = itemDef.Value
                });
                _em.AddComponent(item, new NameComponent { Name = itemDef.Name });
                _log.Add($"{_em.GetComponent<NameComponent>(victim)?.Name ?? "Enemy"} drops {itemDef.Name}.",
                    SadRogue.Primitives.Color.Yellow);
            }
        }
    }
}
