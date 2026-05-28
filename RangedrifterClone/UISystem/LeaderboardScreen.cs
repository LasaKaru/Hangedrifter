using SadConsole;
using SadConsole.Input;
using SadRogue.Primitives;
using RangedrifterClone.Core;

namespace RangedrifterClone.UISystem;

/// <summary>
/// Local high-score leaderboard. Shows top-20 runs, separated into
/// Normal and Daily tabs. Esc/Backspace returns to main menu.
/// </summary>
public class LeaderboardScreen : ScreenSurface
{
    private static readonly Color Gold    = new(255, 200,  40);
    private static readonly Color Silver  = new(200, 200, 210);
    private static readonly Color Bronze  = new(200, 140,  60);
    private static readonly Color Dim     = new( 70,  70,  70);
    private static readonly Color Head    = new(220, 200, 100);
    private static readonly Color Info    = new(160, 180, 200);
    private static readonly Color BgClr   = new(  4,   4,   8);
    private static readonly Color DailyClr= new(100, 200, 255);
    private static readonly Color NormClr = new(180, 180, 180);

    private bool _showDaily = false;

    public LeaderboardScreen()
        : base(GameHost.Instance.ScreenCellsX, GameHost.Instance.ScreenCellsY)
    {
        UseKeyboard = true;
    }

    public void Refresh() => Render();

    private void Render()
    {
        this.Clear();
        var bg = BgClr;

        // Background fill
        for (int ry = 0; ry < Height; ry++)
        for (int rx = 0; rx < Width;  rx++)
            this.SetGlyph(rx, ry, ' ', Color.Black, bg);

        // Double border
        DrawDoubleBorder(0, 0, Width, Height, new Color(60, 50, 30));

        // Title
        string title = "  ╡ TOP SCORES ╞  ";
        int tx = (Width - title.Length) / 2;
        this.Print(tx, 0, title, Head, bg);

        // Tab bar
        var normClr  = !_showDaily ? new Color(100, 220, 60) : Dim;
        var dailyClr = _showDaily  ? DailyClr : Dim;
        var normBg   = !_showDaily ? new Color(15, 40, 15) : bg;
        var dailyBg  = _showDaily  ? new Color(10, 30, 50) : bg;
        this.Print(3, 2, " [N] Normal Runs ", normClr,  normBg);
        this.Print(22, 2, " [D] Daily Challenge ", dailyClr, dailyBg);

        var allScores = SaveSystem.LoadScores();
        var scores    = allScores.Where(s => s.IsDaily == _showDaily)
                                  .OrderByDescending(s => s.Score)
                                  .Take(15)
                                  .ToList();

        // Column headers
        int y = 4;
        this.Print(2,  y, "#",      Dim,  bg);
        this.Print(4,  y, "Class",  Dim,  bg);
        this.Print(14, y, "Score",  Dim,  bg);
        this.Print(22, y, "Floor",  Dim,  bg);
        this.Print(29, y, "Kills",  Dim,  bg);
        this.Print(36, y, "Turns",  Dim,  bg);
        this.Print(44, y, "Date",   Dim,  bg);
        this.Print(55, y, "Cause",  Dim,  bg);
        for (int rx = 1; rx < Width - 1; rx++)
            this.SetGlyph(rx, y + 1, '─', new Color(40, 40, 60), bg);
        y += 2;

        if (scores.Count == 0)
        {
            string msg = _showDaily
                ? "No daily challenge runs recorded yet."
                : "No runs recorded yet. Start a new game!";
            this.Print((Width - msg.Length) / 2, y + 4, msg, Dim, bg);
        }
        else
        {
            for (int i = 0; i < scores.Count; i++)
            {
                var s    = scores[i];
                var rank = i + 1;
                var rowClr = rank switch { 1 => Gold, 2 => Silver, 3 => Bronze, _ => NormClr };
                var medal  = rank switch { 1 => "★", 2 => "★", 3 => "★", _ => " " };

                string rankStr  = $"{rank,2}";
                string cls      = (s.PlayerClass ?? "?").PadRight(8);
                string score    = $"{s.Score,7:N0}";
                string floor    = $"F{s.Floor,2}";
                string kills    = $"{s.KillCount,4}";
                string turns    = $"{s.TurnCount,5}";
                string date     = (s.Date ?? "").PadRight(10);
                string cause    = (s.Cause ?? "").PadRight(14);

                this.Print(2,  y, rankStr,  rowClr, bg);
                this.Print(4,  y, medal,    rowClr, bg);
                this.Print(6,  y, cls,      rowClr, bg);
                this.Print(14, y, score,    rowClr, bg);
                this.Print(22, y, floor,    rowClr, bg);
                this.Print(29, y, kills,    rowClr, bg);
                this.Print(36, y, turns,    rowClr, bg);
                this.Print(44, y, date,     Dim,    bg);
                this.Print(55, y, cause,    Dim,    bg);
                y++;
            }
        }

        // Achievement summary
        var achievements = new AchievementSystem();
        string achLine = $"Achievements unlocked: {achievements.UnlockedCount} / {AchievementSystem.All.Length}";
        this.Print(2, Height - 4, achLine, new Color(100, 160, 100), bg);

        // Achievement list (unlocked ones)
        int ax = 2;
        foreach (var ach in AchievementSystem.All)
        {
            bool got = achievements.IsUnlocked(ach.Id);
            var aClr = got ? new Color(200, 200, 60) : Dim;
            char icon = got ? ach.Icon : '?';
            if (ax + ach.Name.Length + 3 > Width - 2) break;
            this.SetGlyph(ax, Height - 3, icon, aClr, bg);
            ax++;
        }

        // Footer
        for (int rx = 1; rx < Width - 1; rx++)
            this.SetGlyph(rx, Height - 2, '─', new Color(40, 40, 60), bg);
        string footer = " N=Normal  D=Daily  Esc=Menu ";
        int fx = (Width - footer.Length) / 2;
        this.Print(fx, Height - 2, footer, Dim, bg);
    }

    public override void Update(TimeSpan delta)
    {
        base.Update(delta);
        var kb = GameHost.Instance.Keyboard;

        if (kb.IsKeyPressed(Keys.Escape) || kb.IsKeyPressed(Keys.Back))
        {
            GameEngine.Instance.State = GameState.MainMenu;
            return;
        }
        if (kb.IsKeyPressed(Keys.N)) { _showDaily = false; Render(); }
        if (kb.IsKeyPressed(Keys.D)) { _showDaily = true;  Render(); }
    }

    public override bool ProcessKeyboard(Keyboard keyboard) => true;

    private void DrawDoubleBorder(int x, int y, int w, int h, Color clr)
    {
        var bg = BgClr;
        for (int i = 1; i < w - 1; i++)
        { this.SetGlyph(x+i, y, '═', clr, bg); this.SetGlyph(x+i, y+h-1, '═', clr, bg); }
        for (int j = 1; j < h - 1; j++)
        { this.SetGlyph(x, y+j, '║', clr, bg); this.SetGlyph(x+w-1, y+j, '║', clr, bg); }
        this.SetGlyph(x,     y,     '╔', clr, bg); this.SetGlyph(x+w-1, y,     '╗', clr, bg);
        this.SetGlyph(x,     y+h-1, '╚', clr, bg); this.SetGlyph(x+w-1, y+h-1, '╝', clr, bg);
    }
}
