using SadConsole;
using SadConsole.Input;
using SadRogue.Primitives;
using RangedrifterClone.Core;
using RangedrifterClone.Data;

namespace RangedrifterClone.UISystem;

/// <summary>
/// Class selection screen.  Shows three mini-tab cards at the top and a
/// large expanded panel for the currently selected class below.
/// Keyboard is polled directly in Update() so it works on all hardware.
/// </summary>
public class CharacterCreationScreen : ScreenSurface
{
    private int _selected = 0;
    private List<DataLoader.ClassDefinition> _classes = new();

    // ── Palette ──────────────────────────────────────────────────────────
    private static readonly Color HeaderColor = new(255, 200,  50);
    private static readonly Color SelectFg    = new(100, 220,  60);
    private static readonly Color NormalFg    = new(140, 140, 140);
    private static readonly Color DimFg       = new( 70,  70,  70);
    private static readonly Color StatLabel   = new(130, 130, 130);
    private static readonly Color StatValue   = new(200, 200, 100);
    private static readonly Color DivColor    = new( 60,  60,  60);
    private static readonly Color DescColor   = new(120, 120, 120);
    private static readonly Color SelectBg    = new( 20,  40,  20);

    // Per-class accent colours
    private static readonly Color[] Accents =
    {
        new(220, 140,  40),   // Warrior — amber
        new(180,  80, 210),   // Rogue   — violet
        new( 60, 150, 230),   // Mage    — sky blue
    };

    private Color Accent(int i) =>
        i >= 0 && i < Accents.Length ? Accents[i] : HeaderColor;

    // ─────────────────────────────────────────────────────────────────────
    public CharacterCreationScreen()
        : base(GameHost.Instance.ScreenCellsX, GameHost.Instance.ScreenCellsY)
    {
        UseKeyboard = true;
    }

    public void Refresh()
    {
        _classes = GameEngine.Instance.DataLoader.ClassDefinitions;
        if (_selected >= _classes.Count) _selected = 0;
        Render();
    }

    // ── Render ────────────────────────────────────────────────────────────
    private void Render()
    {
        this.Clear();

        // Title bar
        this.Print(2, 0, "CHOOSE YOUR CLASS", HeaderColor, Color.Black);
        this.Print(2, 1, new string('═', Width - 4), DivColor, Color.Black);

        if (_classes.Count == 0)
        {
            this.Print(2, 4, "ERROR: No classes loaded.", new Color(220, 60, 60));
            this.Print(2, 5, "Make sure Data/classes.json is in the output folder.", DimFg);
            return;
        }

        DrawClassTabs();
        DrawSelectedCard();
        DrawFooter();
    }

    // ── Three mini-tab cards at the top ──────────────────────────────────
    private void DrawClassTabs()
    {
        // Distribute tabs evenly across width
        int count = Math.Min(_classes.Count, 3);
        int tabW  = (Width - 4) / count;      // e.g. 76 / 3 = 25
        int tabH  = 5;

        for (int i = 0; i < count; i++)
        {
            var  cls    = _classes[i];
            bool sel    = i == _selected;
            int  tx     = 2 + i * tabW;
            var  accent = Accent(i);
            var  border = sel ? accent : DivColor;

            DrawBox(tx, 2, tabW - 1, tabH, border);

            // Class name row
            var nameFg = sel ? accent     : NormalFg;
            var nameBg = sel ? SelectBg   : Color.Black;
            string label = sel ? $"▶ {cls.Name} ◀" : $"  {cls.Name}";
            if (label.Length > tabW - 3) label = label[..(tabW - 3)];
            this.Print(tx + 2, 3, label, nameFg, nameBg);

            // Quick stats
            this.Print(tx + 2, 4, $"HP:{cls.StartHp,-3}  MP:{cls.StartMana}", StatValue, Color.Black);
            this.Print(tx + 2, 5, $"Dmg:d{cls.StartDamageSides}+{cls.StartDamageBonus} Def:{cls.StartDefense}", StatLabel, Color.Black);
            this.Print(tx + 2, 6, $"Crit:{cls.CritChance*100:0}%  Dg:{cls.DodgeChance*100:0}%", DimFg, Color.Black);
        }
    }

    // ── Large info card for the currently selected class ─────────────────
    private void DrawSelectedCard()
    {
        var  cls    = _classes[_selected];
        var  accent = Accent(_selected);
        int  cx     = 2;
        int  cy     = 9;                         // top of card
        int  cw     = Width  - 4;               // 76
        int  ch     = Height - cy - 6;          // leaves room for footer

        DrawBox(cx, cy, cw, ch, accent);

        int y = cy + 1;   // cursor inside the card

        // Class name banner
        string banner = $"  ══  {cls.Name.ToUpper()}  ══";
        this.Print(cx + 2, y, banner, accent, Color.Black);
        y++;
        this.Print(cx + 2, y, new string('─', cw - 4), DivColor, Color.Black);
        y++;

        // Stats row (6 stats across)
        PrintStat(cx +  2, y, "HP",      cls.StartHp.ToString());
        PrintStat(cx + 12, y, "Mana",    cls.StartMana.ToString());
        PrintStat(cx + 22, y, "Damage",  $"d{cls.StartDamageSides}+{cls.StartDamageBonus}");
        PrintStat(cx + 36, y, "Defense", cls.StartDefense.ToString());
        PrintStat(cx + 48, y, "Crit",    $"{cls.CritChance*100:0}%");
        PrintStat(cx + 58, y, "Dodge",   $"{cls.DodgeChance*100:0}%");
        y++;
        this.Print(cx + 2, y, new string('─', cw - 4), DivColor, Color.Black);
        y++;

        // Description (word-wrapped to card width)
        if (!string.IsNullOrWhiteSpace(cls.Description))
        {
            foreach (var line in WordWrap(cls.Description, cw - 4))
            {
                this.Print(cx + 2, y, line, DescColor, Color.Black);
                y++;
                if (y >= cy + ch - 2) break;
            }
        }
        y++;
        this.Print(cx + 2, y, new string('─', cw - 4), DivColor, Color.Black);
        y++;

        // Abilities section
        this.Print(cx + 2, y, "ABILITIES", accent, Color.Black);
        y++;

        if (cls.Abilities.Count == 0)
        {
            this.Print(cx + 4, y, "(none defined — check classes.json abilities array)", DimFg);
            y++;
        }
        else
        {
            for (int a = 0; a < Math.Min(cls.Abilities.Count, 4); a++)
            {
                if (y >= cy + ch - 2) break;
                var ab = cls.Abilities[a];

                // [1] [B] Name            MP:2  CD:2  Description...
                string slotStr  = $"[{a + 1}]";
                string glyphStr = $"[{ab.Glyph}]";
                string nameStr  = ab.Name.Length > 14 ? ab.Name[..14] : ab.Name.PadRight(14);
                string costStr  = $"MP:{ab.ManaCost,-2} CD:{ab.MaxCooldown,-2}";
                int    descMaxW = cw - 42;
                string descStr  = ab.Description.Length > descMaxW
                                    ? ab.Description[..descMaxW]
                                    : ab.Description;

                this.Print(cx +  2, y, slotStr,  new Color(180, 180, 60),  Color.Black);
                this.Print(cx +  6, y, glyphStr, ab.Color,                 Color.Black);
                this.Print(cx + 10, y, nameStr,  accent,                   Color.Black);
                this.Print(cx + 25, y, costStr,  StatValue,                Color.Black);
                this.Print(cx + 38, y, descStr,  DescColor,                Color.Black);
                y++;
            }
        }

        // Starting items
        y++;
        if (y < cy + ch - 1 && cls.StartItems.Count > 0)
        {
            this.Print(cx + 2,  y, "START ITEMS:", StatLabel, Color.Black);
            this.Print(cx + 15, y, string.Join("  ", cls.StartItems), StatValue, Color.Black);
        }
    }

    // ── Footer ────────────────────────────────────────────────────────────
    private void DrawFooter()
    {
        int fy = Height - 4;
        this.Print(2, fy,
            "◄ ►  Select class",
            DimFg, Color.Black);
        this.Print(2, fy + 1,
            "Enter / Space  —  Begin your adventure!",
            new Color(100, 220, 60), Color.Black);
        this.Print(2, fy + 2,
            "Esc            —  Back to main menu",
            DimFg, Color.Black);
    }

    // ── Helpers ───────────────────────────────────────────────────────────
    private void PrintStat(int x, int y, string label, string value)
    {
        this.Print(x,                     y, label + ":", StatLabel, Color.Black);
        this.Print(x + label.Length + 1,  y, value,       StatValue, Color.Black);
    }

    private void DrawBox(int x, int y, int w, int h, Color clr)
    {
        for (int i = 1; i < w - 1; i++)
        {
            this.SetGlyph(x + i, y,         '─', clr, Color.Black);
            this.SetGlyph(x + i, y + h,     '─', clr, Color.Black);
        }
        for (int j = 1; j < h; j++)
        {
            this.SetGlyph(x,         y + j, '│', clr, Color.Black);
            this.SetGlyph(x + w - 1, y + j, '│', clr, Color.Black);
        }
        this.SetGlyph(x,         y,     '┌', clr, Color.Black);
        this.SetGlyph(x + w - 1, y,     '┐', clr, Color.Black);
        this.SetGlyph(x,         y + h, '└', clr, Color.Black);
        this.SetGlyph(x + w - 1, y + h, '┘', clr, Color.Black);
    }

    private static IEnumerable<string> WordWrap(string text, int width)
    {
        if (string.IsNullOrWhiteSpace(text)) yield break;
        var words = text.Split(' ');
        var line  = "";
        foreach (var word in words)
        {
            if (line.Length + word.Length + 1 > width)
            {
                if (line.Length > 0) yield return line;
                line = word;
            }
            else line = line.Length == 0 ? word : line + " " + word;
        }
        if (line.Length > 0) yield return line;
    }

    // ── Input — poll GameHost directly, works on every keyboard ──────────
    public override void Update(TimeSpan delta)
    {
        base.Update(delta);
        if (_classes.Count == 0) return;

        var kb = GameHost.Instance.Keyboard;

        if (kb.IsKeyPressed(Keys.Left)  || kb.IsKeyPressed(Keys.NumPad4))
        { _selected = (_selected - 1 + _classes.Count) % _classes.Count; Render(); }

        else if (kb.IsKeyPressed(Keys.Right) || kb.IsKeyPressed(Keys.NumPad6))
        { _selected = (_selected + 1) % _classes.Count; Render(); }

        else if (kb.IsKeyPressed(Keys.Enter) || kb.IsKeyPressed(Keys.Space))
        {
            var eng = GameEngine.Instance;
            if (eng.IsDailyPending)
            {
                eng.IsDailyPending = false;
                eng.StartDailyChallenge(_classes[_selected].Id);
            }
            else
            {
                eng.StartNewGame(_classes[_selected].Id);
            }
        }

        else if (kb.IsKeyPressed(Keys.Escape))
        { GameEngine.Instance.State = GameState.MainMenu; }
    }

    // All input is handled in Update — ProcessKeyboard is a no-op.
    public override bool ProcessKeyboard(Keyboard keyboard) => true;
}
