using System.Text.Json;
using System.Text.Json.Serialization;
using RangedrifterClone.Components;

namespace RangedrifterClone.Data;

public class DataLoader
{
    public List<EnemyDefinition>  EnemyDefinitions  { get; private set; } = new();
    public List<ItemDefinition>   ItemDefinitions   { get; private set; } = new();
    public List<ClassDefinition>  ClassDefinitions  { get; private set; } = new();

    private static readonly JsonSerializerOptions Opts = new()
        { PropertyNameCaseInsensitive = true };

    public void Load()
    {
        EnemyDefinitions  = LoadFile<List<EnemyDefinition>>("enemies.json")   ?? DefaultEnemies();
        ItemDefinitions   = LoadFile<List<ItemDefinition>>("items.json")      ?? DefaultItems();
        ClassDefinitions  = LoadFile<List<ClassDefinition>>("classes.json")   ?? DefaultClasses();
    }

    private T? LoadFile<T>(string filename)
    {
        string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", filename);
        if (!File.Exists(path)) return default;
        try { return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Opts); }
        catch { return default; }
    }

    // ── Colour helper ─────────────────────────────────────────────────────
    public static SadRogue.Primitives.Color ParseHex(string hex)
    {
        hex = hex?.TrimStart('#') ?? "FFFFFF";
        if (hex.Length < 6) return SadRogue.Primitives.Color.White;
        return new SadRogue.Primitives.Color(
            Convert.ToByte(hex[0..2], 16),
            Convert.ToByte(hex[2..4], 16),
            Convert.ToByte(hex[4..6], 16));
    }

    // ── Fallback defaults (used when JSON files are missing) ──────────────
    private static List<EnemyDefinition> DefaultEnemies() => new()
    {
        new(){ Id="goblin",   Name="Goblin",   GlyphStr="g", ColorHex="#60C060",
               MaxHp=5, Defense=0, Strength=1, DamageDice=1, DamageSides=4 },
        new(){ Id="skeleton", Name="Skeleton", GlyphStr="s", ColorHex="#A0A0A0",
               MaxHp=8, Defense=1, Strength=2, DamageDice=1, DamageSides=4 },
        new(){ Id="troll",    Name="Troll",    GlyphStr="T", ColorHex="#C08040",
               MaxHp=20, Defense=2, Strength=4, DamageDice=1, DamageSides=8, DamageBonus=2, Behavior="Aggressive" },
    };

    private static List<ItemDefinition> DefaultItems() => new()
    {
        new(){ Id="meat",   Name="Meat",   GlyphStr="%", ColorHex="#E06060", Category="Food",       Value=5,  UseEffect="Heal:6" },
        new(){ Id="potion", Name="Potion", GlyphStr="!", ColorHex="#8040C0", Category="Consumable", Value=8,  UseEffect="Heal:10" },
        new(){ Id="axe",    Name="Axe",    GlyphStr=")", ColorHex="#C0C0C0", Category="Weapon",     Value=10, BonusDamage=2 },
    };

    private static List<ClassDefinition> DefaultClasses() => new()
    {
        new(){ Id="warrior", Name="Warrior",
               StartHp=16, StartMana=8, StartDamageDice=1, StartDamageSides=6, StartDamageBonus=2,
               StartDefense=2, StartStrength=4, CritChance=0.08f, DodgeChance=0.03f }
    };

    // ═════════════════════════════════════════════════════════════════════
    // Definition classes
    // ═════════════════════════════════════════════════════════════════════

    public class EnemyDefinition
    {
        public string Id          { get; set; } = "";
        public string Name        { get; set; } = "";
        public string GlyphStr    { get; set; } = "?";
        public string ColorHex    { get; set; } = "#FFFFFF";
        public int    MaxHp       { get; set; } = 5;
        public int    Defense     { get; set; } = 0;
        public int    Strength    { get; set; } = 1;
        public int    DamageDice  { get; set; } = 1;
        public int    DamageSides { get; set; } = 4;
        public int    DamageBonus { get; set; } = 0;
        public string Behavior    { get; set; } = "BasicMonster";
        public bool   IsRanged    { get; set; } = false;
        public int    AttackRange { get; set; } = 1;
        public int    AlertRadius { get; set; } = 6;
        public string PackTag     { get; set; } = "";
        public bool   CanFlee     { get; set; } = false;
        public float  FleeThreshold { get; set; } = 0.20f;
        public string StatusOnHit { get; set; } = "";
        public int    StatusDuration  { get; set; } = 0;
        public int    StatusMagnitude { get; set; } = 1;
        public List<LootEntry>? LootDrops { get; set; }
        public float  DropChance   { get; set; } = 0.30f;

        [JsonIgnore] public char Glyph => GlyphStr?.Length > 0 ? GlyphStr[0] : '?';
        [JsonIgnore] public SadRogue.Primitives.Color Color => ParseHex(ColorHex);
        [JsonIgnore] public AIBehavior AiBehavior => Behavior switch
        {
            "Aggressive" => AIBehavior.Aggressive,
            "Passive"    => AIBehavior.Passive,
            "Ranged"     => AIBehavior.Ranged,
            "Coward"     => AIBehavior.Coward,
            "Pack"       => AIBehavior.Pack,
            "Boss"       => AIBehavior.Boss,
            _            => AIBehavior.BasicMonster
        };
    }

    public class ItemDefinition
    {
        public string Id           { get; set; } = "";
        public string Name         { get; set; } = "";
        public string GlyphStr     { get; set; } = "?";
        public string ColorHex     { get; set; } = "#FFFFFF";
        public string Category     { get; set; } = "Misc";
        public int    Value        { get; set; } = 0;
        public int    BonusDamage  { get; set; } = 0;
        public int    BonusDefense { get; set; } = 0;
        public int    BonusMaxHp   { get; set; } = 0;
        public int    BonusMaxMana { get; set; } = 0;
        public string UseEffect    { get; set; } = "";

        [JsonIgnore] public char Glyph => GlyphStr?.Length > 0 ? GlyphStr[0] : '?';
        [JsonIgnore] public SadRogue.Primitives.Color Color => ParseHex(ColorHex);
    }

    public class LootEntry
    {
        public string ItemId   { get; set; } = "";
        public float  Weight   { get; set; } = 1f;
        public int    MinCount { get; set; } = 1;
        public int    MaxCount { get; set; } = 1;
    }

    public class AbilityDefinition
    {
        public string Id           { get; set; } = "";
        public string Name         { get; set; } = "";
        public string Description  { get; set; } = "";
        public string GlyphStr     { get; set; } = "*";
        public string ColorHex     { get; set; } = "#FFFFFF";
        public string Type         { get; set; } = "MeleeAttack";
        public int    ManaCost     { get; set; } = 0;
        public int    MaxCooldown  { get; set; } = 0;
        public int    Power        { get; set; } = 5;
        public int    Range        { get; set; } = 1;
        public int    Radius       { get; set; } = 0;
        public int    DurationTurns{ get; set; } = 0;
        public string StatusEffect { get; set; } = "";

        [JsonIgnore] public char Glyph => GlyphStr?.Length > 0 ? GlyphStr[0] : '*';
        [JsonIgnore] public SadRogue.Primitives.Color Color => ParseHex(ColorHex);
        [JsonIgnore] public AbilityType AbilType =>
            Enum.TryParse<AbilityType>(Type, true, out var t) ? t : AbilityType.MeleeAttack;
    }

    public class ClassDefinition
    {
        public string Id              { get; set; } = "";
        public string Name            { get; set; } = "";
        public string Description     { get; set; } = "";
        public int    StartHp         { get; set; } = 12;
        public int    StartMana       { get; set; } = 8;
        public int    StartDamageDice { get; set; } = 1;
        public int    StartDamageSides{ get; set; } = 6;
        public int    StartDamageBonus{ get; set; } = 0;
        public int    StartDefense    { get; set; } = 1;
        public int    StartStrength   { get; set; } = 2;
        public int    BonusDamage     { get; set; } = 0;
        public int    BonusDefense    { get; set; } = 0;
        public int    BonusMaxHp      { get; set; } = 0;
        public int    BonusMaxMana    { get; set; } = 0;
        public float  CritChance      { get; set; } = 0.05f;
        public float  DodgeChance     { get; set; } = 0.05f;
        public List<string>             StartItems { get; set; } = new();
        public List<AbilityDefinition>  Abilities  { get; set; } = new();
    }
}
