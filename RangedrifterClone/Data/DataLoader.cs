using System.Text.Json;
using System.Text.Json.Serialization;
using RangedrifterClone.Components;

namespace RangedrifterClone.Data;

public class DataLoader
{
    public List<EnemyDefinition> EnemyDefinitions { get; private set; } = new();
    public List<ItemDefinition>  ItemDefinitions  { get; private set; } = new();

    private static readonly JsonSerializerOptions JsonOpts = new()
        { PropertyNameCaseInsensitive = true };

    public void Load()
    {
        LoadEnemies();
        LoadItems();
    }

    private void LoadEnemies()
    {
        string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "enemies.json");
        if (File.Exists(path))
        {
            try
            {
                EnemyDefinitions = JsonSerializer.Deserialize<List<EnemyDefinition>>(
                    File.ReadAllText(path), JsonOpts) ?? new();
                return;
            }
            catch { /* fall through */ }
        }
        LoadDefaultEnemies();
    }

    private void LoadItems()
    {
        string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "items.json");
        if (File.Exists(path))
        {
            try
            {
                ItemDefinitions = JsonSerializer.Deserialize<List<ItemDefinition>>(
                    File.ReadAllText(path), JsonOpts) ?? new();
                return;
            }
            catch { /* fall through */ }
        }
        LoadDefaultItems();
    }

    private void LoadDefaultEnemies()
    {
        EnemyDefinitions = new List<EnemyDefinition>
        {
            new() { Id="skeleton", Name="Skeleton", GlyphStr="s", ColorHex="#A0A0A0",
                MaxHp=8, Defense=1, Strength=2, DamageDice=1, DamageSides=4, DamageBonus=0,
                Behavior="BasicMonster" },
            new() { Id="goblin", Name="Goblin", GlyphStr="g", ColorHex="#60C060",
                MaxHp=5, Defense=0, Strength=1, DamageDice=1, DamageSides=4, DamageBonus=0,
                Behavior="BasicMonster" },
            new() { Id="troll", Name="Troll", GlyphStr="T", ColorHex="#C08040",
                MaxHp=16, Defense=2, Strength=4, DamageDice=1, DamageSides=8, DamageBonus=1,
                Behavior="Aggressive" },
        };
    }

    private void LoadDefaultItems()
    {
        ItemDefinitions = new List<ItemDefinition>
        {
            new() { Id="meat",     Name="Meat",     GlyphStr="%", ColorHex="#E06060",
                Category="Food",       Value=5  },
            new() { Id="axe",      Name="Axe",      GlyphStr=")", ColorHex="#C0C0C0",
                Category="Weapon",     Value=10 },
            new() { Id="potion",   Name="Potion",   GlyphStr="!", ColorHex="#8040C0",
                Category="Consumable", Value=8  },
            new() { Id="stone",    Name="Stone",    GlyphStr="*", ColorHex="#A09080",
                Category="Misc",       Value=1  },
            new() { Id="medicine", Name="Medicine", GlyphStr="+", ColorHex="#40C040",
                Category="Consumable", Value=12 },
        };
    }

    private static SadRogue.Primitives.Color ParseHex(string hex)
    {
        hex = hex.TrimStart('#');
        if (hex.Length != 6) return SadRogue.Primitives.Color.White;
        byte r = Convert.ToByte(hex[0..2], 16);
        byte g = Convert.ToByte(hex[2..4], 16);
        byte b = Convert.ToByte(hex[4..6], 16);
        return new SadRogue.Primitives.Color(r, g, b);
    }

    public class EnemyDefinition
    {
        public string Id         { get; set; } = "";
        public string Name       { get; set; } = "";
        public string GlyphStr   { get; set; } = "?";
        public string ColorHex   { get; set; } = "#FFFFFF";
        public int    MaxHp      { get; set; } = 5;
        public int    Defense    { get; set; } = 0;
        public int    Strength   { get; set; } = 1;
        public int    DamageDice { get; set; } = 1;
        public int    DamageSides{ get; set; } = 4;
        public int    DamageBonus{ get; set; } = 0;
        public string Behavior   { get; set; } = "BasicMonster";

        [JsonIgnore] public char Glyph =>
            GlyphStr?.Length > 0 ? GlyphStr[0] : '?';

        [JsonIgnore] public SadRogue.Primitives.Color Color =>
            ParseHex(ColorHex);

        [JsonIgnore] public AIBehavior AiBehavior => Behavior switch
        {
            "Aggressive" => AIBehavior.Aggressive,
            "Passive"    => AIBehavior.Passive,
            _            => AIBehavior.BasicMonster
        };
    }

    public class ItemDefinition
    {
        public string Id       { get; set; } = "";
        public string Name     { get; set; } = "";
        public string GlyphStr { get; set; } = "?";
        public string ColorHex { get; set; } = "#FFFFFF";
        public string Category { get; set; } = "Misc";
        public int    Value    { get; set; } = 0;

        [JsonIgnore] public char Glyph =>
            GlyphStr?.Length > 0 ? GlyphStr[0] : '?';

        [JsonIgnore] public SadRogue.Primitives.Color Color =>
            ParseHex(ColorHex);
    }
}
