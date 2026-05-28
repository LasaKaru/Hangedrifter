using System.Text.Json;
using RangedrifterClone.Components;

namespace RangedrifterClone.Core;

// ── Serialisable data classes ──────────────────────────────────────────────

public class SaveData
{
    public int    Version          { get; set; } = 1;
    public int    RunSeed          { get; set; }
    public bool   IsDailyChallenge { get; set; }
    public int    CurrentFloor     { get; set; }
    public int    TurnCount        { get; set; }
    public int    KillCount        { get; set; }
    public int    AbilityUseCount  { get; set; }
    public int    TotalRangedShots { get; set; }
    public string ClassId          { get; set; } = "warrior";
    public PlayerSave Player       { get; set; } = new();
}

public class PlayerSave
{
    public int    X           { get; set; }
    public int    Y           { get; set; }
    public int    Hp          { get; set; }
    public int    MaxHp       { get; set; }
    public int    Mana        { get; set; }
    public int    MaxMana     { get; set; }
    public int    Strength    { get; set; }
    public int    Defense     { get; set; }
    public int    Level       { get; set; } = 1;
    public int    Experience  { get; set; }
    public int    NextLevelExp{ get; set; } = 30;
    // Inventory stored as id+count; stats reconstructed from DataLoader
    public List<ItemSave> Inventory { get; set; } = new();
    // Equipment stored as equipped item IDs
    public string? Weapon  { get; set; }
    public string? Armor   { get; set; }
    public string? Shield  { get; set; }
    public string? Ring    { get; set; }
    public string? Amulet  { get; set; }
}

public class ItemSave
{
    public string ItemId { get; set; } = "";
    public int    Count  { get; set; } = 1;
}

public class ScoreRecord
{
    public string PlayerClass  { get; set; } = "";
    public int    Score        { get; set; }
    public int    Floor        { get; set; }
    public int    KillCount    { get; set; }
    public int    TurnCount    { get; set; }
    public bool   IsDaily      { get; set; }
    public int    DailySeed    { get; set; }
    public string Date         { get; set; } = "";
    public string Cause        { get; set; } = "Slain";
}

// ── SaveSystem ─────────────────────────────────────────────────────────────

public static class SaveSystem
{
    private static readonly string SaveDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "RangedrifterClone");

    private static readonly string SaveFile  = Path.Combine(SaveDir, "save.json");
    private static readonly string ScoreFile = Path.Combine(SaveDir, "scores.json");
    private static readonly JsonSerializerOptions Opts = new() { WriteIndented = true };

    // ── Game save ─────────────────────────────────────────────────────────
    public static void SaveGame(GameEngine eng)
    {
        var em     = eng.EntityManager;
        var player = eng.PlayerEntity;
        if (!player.IsValid) return;

        var pos     = em.GetComponent<PositionComponent>(player);
        var fighter = em.GetComponent<FighterComponent>(player);
        var mana    = em.GetComponent<ManaComponent>(player);
        var inv     = em.GetComponent<InventoryComponent>(player);
        var equip   = em.GetComponent<EquipmentSlotComponent>(player);
        var cls     = em.GetComponent<ClassComponent>(player);
        var xp      = em.GetComponent<ExperienceComponent>(player);
        var status  = em.GetComponent<StatusComponent>(player);

        if (pos == null || fighter == null) return;

        var save = new SaveData
        {
            RunSeed          = eng.RunSeed,
            IsDailyChallenge = eng.IsDailyChallenge,
            CurrentFloor     = eng.CurrentFloor,
            TurnCount        = status?.Turn ?? 0,
            KillCount        = eng.KillCount,
            AbilityUseCount  = eng.AbilityUseCount,
            TotalRangedShots = eng.TotalRangedShots,
            ClassId          = cls?.Class.ToString().ToLower() ?? "warrior",
            Player = new PlayerSave
            {
                X = pos.X, Y = pos.Y,
                Hp = fighter.Hp, MaxHp = fighter.MaxHp,
                Mana = mana?.Mana ?? 0, MaxMana = mana?.MaxMana ?? 0,
                Strength = fighter.Strength, Defense = fighter.Defense,
                Level = xp?.Level ?? 1,
                Experience  = xp?.Experience    ?? 0,
                NextLevelExp = xp?.NextLevelExp ?? 30,
                Inventory = inv?.Items.Select(i => new ItemSave
                    { ItemId = i.ItemId, Count = i.Count }).ToList() ?? new(),
                Weapon = equip?.Weapon?.ItemId,
                Armor  = equip?.Armor?.ItemId,
                Shield = equip?.Shield?.ItemId,
                Ring   = equip?.Ring?.ItemId,
                Amulet = equip?.Amulet?.ItemId,
            }
        };

        EnsureDir();
        File.WriteAllText(SaveFile, JsonSerializer.Serialize(save, Opts));
    }

    public static SaveData? LoadSave()
    {
        if (!File.Exists(SaveFile)) return null;
        try { return JsonSerializer.Deserialize<SaveData>(File.ReadAllText(SaveFile)); }
        catch { return null; }
    }

    public static bool HasSave() => File.Exists(SaveFile);
    public static void DeleteSave() { if (File.Exists(SaveFile)) File.Delete(SaveFile); }

    // ── Leaderboard ───────────────────────────────────────────────────────
    public static List<ScoreRecord> LoadScores()
    {
        if (!File.Exists(ScoreFile)) return new();
        try { return JsonSerializer.Deserialize<List<ScoreRecord>>(File.ReadAllText(ScoreFile)) ?? new(); }
        catch { return new(); }
    }

    public static void AddScore(ScoreRecord record)
    {
        var scores = LoadScores();
        scores.Add(record);
        scores = scores.OrderByDescending(s => s.Score).Take(20).ToList();
        EnsureDir();
        File.WriteAllText(ScoreFile, JsonSerializer.Serialize(scores, Opts));
    }

    private static void EnsureDir() => Directory.CreateDirectory(SaveDir);
}
