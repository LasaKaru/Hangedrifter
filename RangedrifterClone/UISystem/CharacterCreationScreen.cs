using SadConsole;
using SadConsole.Input;
using SadRogue.Primitives;
using RangedrifterClone.Core;
using RangedrifterClone.Data;

namespace RangedrifterClone.UISystem;

/// <summary>
/// Class selection screen shown between Main Menu and game start.
/// Shows three class cards (Warrior / Rogue / Mage) with stats and abilities.
/// </summary>
public class CharacterCreationScreen : ScreenSurface
{
    private int _selected = 0;
    private List<DataLoader.ClassDefinition> _classes = new();

    private static readonly Color HeaderColor  = new(255, 200,  50);
    private static readonly Color SelectBg     = new( 30,  30,  60);
    private static readonly Color NormalBg     = Color.Black;
    private static readonly Color DivColor     = new( 60,  60,  60);

    public CharacterCreationScreen()
        : base(GameHost.Instance.ScreenCellsX, GameHost.Instance.ScreenCellsY)
    {
        IsFocused = true;
    }

    public void Refresh()
    {
        _classes = GameEngine.Instance.DataLoader.ClassDefinitions;
        Render();
    }

    private void Render()
    {
        this.Clear();

        this.Print(2, 1, "CHOOSE YOUR CLASS", HeaderColor, Color.Black);
        this.Print(2, 2, new string('═', 22), DivColor, Color.Black);

        if (_classes.Count == 0) return;

        // Three columns: each class gets ~26 cols
        int colW = 26;
        for (int i = 0; i < Math.Min(_classes.Count, 3); i++)
        {
            var cls     = _classes[i];
            bool sel    = i == _selected;
            int   cx    = 1 + i * colW;

            // Border highlight
            var borderClr = sel ? new Color(200, 200, 50) : DivColor;
            DrawBox(cx, 4, colW - 1, Height - 8, borderClr);

            var titleClr = sel ? new Color(255, 220, 80) : new Color(160, 160, 160);
            string arrow = sel ? "►" : " ";

            this.Print(cx + 2, 5, $"{arrow} {cls.Name}", titleClr, Color.Black);
            this.Print(cx + 2, 6, new string('─', colW - 5), DivColor, Color.Black);

            // Stats
            int y = 7;
            PrintStat(cx, y++, "HP",       cls.StartHp.ToString());
            PrintStat(cx, y++, "Mana",     cls.StartMana.ToString());
            PrintStat(cx, y++, "Damage",   $"d{cls.StartDamageSides}+{cls.StartDamageBonus}");
            PrintStat(cx, y++, "Defense",  cls.StartDefense.ToString());
            PrintStat(cx, y++, "Crit",     $"{cls.CritChance*100:0}%");
            PrintStat(cx, y++, "Dodge",    $"{cls.DodgeChance*100:0}%");
            y++;

            // Description (word-wrap)
            var desc = cls.Description;
            foreach (var line in WordWrap(desc, colW - 4))
            {
                this.Print(cx + 2, y++, line, new Color(140, 140, 140), Color.Black);
                if (y >= Height - 10) break;
            }
            y++;

            // Abilities
            this.Print(cx + 2, y++, "Abilities:", new Color(180, 180, 100));
            foreach (var ab in cls.Abilities.Take(4))
            {
                var abClr = sel ? ab.Color : new Color(120, 120, 120);
                this.Print(cx + 2, y++, $"[{ab.Glyph}] {ab.Name}", abClr, Color.Black);
                if (y >= Height - 5) break;
            }
        }

        // Footer
        int fY = Height - 4;
        this.Print(2, fY, "← → Select class", new Color(100, 100, 100), Color.Black);
        this.Print(2, fY + 1, "Enter = Begin your adventure!", new Color(200, 200, 50), Color.Black);
        this.Print(2, fY + 2, "Esc   = Back to main menu",    new Color(100, 100, 100), Color.Black);
    }

    private void PrintStat(int cx, int y, string label, string value)
    {
        this.Print(cx + 2, y, label, new Color(140, 140, 140), Color.Black);
        this.Print(cx + 12, y, value, new Color(200, 200, 100), Color.Black);
    }

    private void DrawBox(int x, int y, int w, int h, Color clr)
    {
        // Top/bottom
        for (int i = 1; i < w - 1; i++)
        {
            this.SetGlyph(x + i, y,     '─', clr, Color.Black);
            this.SetGlyph(x + i, y + h, '─', clr, Color.Black);
        }
        // Sides
        for (int j = 1; j < h; j++)
        {
            this.SetGlyph(x,         y + j, '│', clr, Color.Black);
            this.SetGlyph(x + w - 1, y + j, '│', clr, Color.Black);
        }
        // Corners
        this.SetGlyph(x,         y,     '┌', clr, Color.Black);
        this.SetGlyph(x + w - 1, y,     '┐', clr, Color.Black);
        this.SetGlyph(x,         y + h, '└', clr, Color.Black);
        this.SetGlyph(x + w - 1, y + h, '┘', clr, Color.Black);
    }

    private static IEnumerable<string> WordWrap(string text, int width)
    {
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

    public override bool ProcessKeyboard(Keyboard keyboard)
    {
        if (_classes.Count == 0) return base.ProcessKeyboard(keyboard);

        if (keyboard.IsKeyPressed(Keys.Left)  || keyboard.IsKeyPressed(Keys.NumPad4))
        { _selected = (_selected - 1 + _classes.Count) % _classes.Count; Render(); return true; }
        if (keyboard.IsKeyPressed(Keys.Right) || keyboard.IsKeyPressed(Keys.NumPad6))
        { _selected = (_selected + 1) % _classes.Count; Render(); return true; }
        if (keyboard.IsKeyPressed(Keys.Enter) || keyboard.IsKeyPressed(Keys.Space))
        {
            GameEngine.Instance.StartNewGame(_classes[_selected].Id);
            return true;
        }
        if (keyboard.IsKeyPressed(Keys.Escape))
        {
            GameEngine.Instance.State = GameState.MainMenu;
            return true;
        }
        return base.ProcessKeyboard(keyboard);
    }
}
