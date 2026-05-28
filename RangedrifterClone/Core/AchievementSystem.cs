using System.Text.Json;

namespace RangedrifterClone.Core;

public record Achievement(string Id, string Name, string Desc, char Icon);

public record AchievementToast(char Icon, string Name, string Desc);

public class AchievementSystem
{
    private static readonly string AchFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "RangedrifterClone", "achievements.json");

    private readonly HashSet<string>         _unlocked;
    private readonly Queue<AchievementToast> _toasts = new();

    // ── All achievements ──────────────────────────────────────────────────
    public static readonly Achievement[] All =
    {
        new("first_blood",  "First Blood",      "Kill your first enemy",               '!'),
        new("floor_3",      "Explorer",         "Reach dungeon floor 3",               '>'),
        new("floor_5",      "Deep Delver",      "Reach dungeon floor 5",               '>'),
        new("floor_10",     "Abyss Walker",     "Reach dungeon floor 10",              '>'),
        new("kills_10",     "Slayer",           "Slay 10 enemies",                     '+'),
        new("kills_50",     "Butcher",          "Slay 50 enemies",                     '+'),
        new("hoarder",      "Hoarder",          "Carry 10 items at once",              'I'),
        new("equipped_3",   "Well Equipped",    "Fill 3 or more equipment slots",      'E'),
        new("low_hp",       "Iron Will",        "Survive with 1 HP remaining",         '\x03'), // ♥
        new("ranged_20",    "Sharpshooter",     "Fire 20 ranged shots in one run",     '-'),
        new("ability_20",   "Spellcaster",      "Use abilities 20 times in one run",   '*'),
        new("turns_500",    "Long Haul",        "Survive 500 turns in one run",        '.'),
        new("boss_kill",    "Boss Slayer",       "Defeat the boss on floor 5",         '\x0F'), // ☼
        new("level_5",      "Veteran",          "Reach character level 5",             '^'),
        new("daily_done",   "Daily Champion",   "Complete a daily challenge run",      '\x0F'),
    };

    public AchievementSystem()
    {
        if (File.Exists(AchFile))
        {
            try
            {
                _unlocked = JsonSerializer.Deserialize<HashSet<string>>(
                    File.ReadAllText(AchFile)) ?? new();
            }
            catch { _unlocked = new(); }
        }
        else _unlocked = new();
    }

    public bool IsUnlocked(string id) => _unlocked.Contains(id);
    public int  UnlockedCount => _unlocked.Count;

    // Called by GameEngine whenever a milestone might be hit
    public void Check(string id)
    {
        if (_unlocked.Contains(id)) return;
        var ach = Array.Find(All, a => a.Id == id);
        if (ach == null) return;
        _unlocked.Add(id);
        _toasts.Enqueue(new AchievementToast(ach.Icon, ach.Name, ach.Desc));
        Persist();
    }

    public bool TryDequeueToast(out AchievementToast toast) =>
        _toasts.TryDequeue(out toast!);

    // ── Bulk check called each EndTurn ────────────────────────────────────
    public void CheckAll(int kills, int floor, int level, int abilities,
        int turns, int invCount, int equippedSlots, int minHp,
        int rangedShots, bool bossKilled, bool dailyComplete)
    {
        if (kills  >= 1)  Check("first_blood");
        if (kills  >= 10) Check("kills_10");
        if (kills  >= 50) Check("kills_50");
        if (floor  >= 3)  Check("floor_3");
        if (floor  >= 5)  Check("floor_5");
        if (floor  >= 10) Check("floor_10");
        if (level  >= 5)  Check("level_5");
        if (abilities >= 20) Check("ability_20");
        if (turns  >= 500) Check("turns_500");
        if (invCount >= 10) Check("hoarder");
        if (equippedSlots >= 3) Check("equipped_3");
        if (minHp <= 1 && kills > 0) Check("low_hp");
        if (rangedShots >= 20) Check("ranged_20");
        if (bossKilled) Check("boss_kill");
        if (dailyComplete) Check("daily_done");
    }

    private void Persist()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(AchFile)!);
            File.WriteAllText(AchFile, JsonSerializer.Serialize(_unlocked));
        }
        catch { }
    }
}
