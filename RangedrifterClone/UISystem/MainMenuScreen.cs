using SadConsole;
using SadConsole.Input;
using SadRogue.Primitives;
using RangedrifterClone.Core;

namespace RangedrifterClone.UISystem;

/// <summary>
/// Mimics the Rangedrifter main menu: title + arrow-key selectable items + version string.
/// </summary>
public class MainMenuScreen : ScreenSurface
{
    private int _selected = 0;
    private readonly string[] _items = { "New Game", "Options", "Quit" };

    private static readonly Color TitleColor   = new(255, 200,  50);
    private static readonly Color SelectColor  = new(100, 200,  50);
    private static readonly Color NormalColor  = new(160, 160, 160);
    private static readonly Color VersionColor = new( 80,  80,  80);

    public MainMenuScreen()
        : base(GameHost.Instance.ScreenCellsX, GameHost.Instance.ScreenCellsY)
    {
        Render();
    }

    private void Render()
    {
        this.Clear();
        // ── Title ──────────────────────────────────────────────────
        this.Print(2, 2, "RANGEDRIFTER", TitleColor, Color.Black);
        this.Print(2, 3, new string('─', 14), new Color(80, 60, 20));

        // ── Menu items ─────────────────────────────────────────────
        for (int i = 0; i < _items.Length; i++)
        {
            bool sel  = i == _selected;
            var  fg   = sel ? SelectColor : NormalColor;
            var  pre  = sel ? ">" : " ";
            this.Print(4, 10 + i * 2, $"{pre} {_items[i]}", fg, Color.Black);
        }

        // ── Version ────────────────────────────────────────────────
        this.Print(2, Height - 3, "v0.20", VersionColor, Color.Black);

        // ── Controls hint ──────────────────────────────────────────
        this.Print(2, Height - 2, "Arrow keys / Enter to select", VersionColor, Color.Black);
    }

    public override bool ProcessKeyboard(Keyboard keyboard)
    {
        if (keyboard.IsKeyPressed(Keys.Up) || keyboard.IsKeyPressed(Keys.NumPad8))
        {
            _selected = (_selected - 1 + _items.Length) % _items.Length;
            Render();
            return true;
        }
        if (keyboard.IsKeyPressed(Keys.Down) || keyboard.IsKeyPressed(Keys.NumPad2))
        {
            _selected = (_selected + 1) % _items.Length;
            Render();
            return true;
        }
        if (keyboard.IsKeyPressed(Keys.Enter) || keyboard.IsKeyPressed(Keys.Space))
        {
            switch (_selected)
            {
                case 0: GameEngine.Instance.StartNewGame(); break;
                case 1: /* Options — future */ break;
                case 2: Environment.Exit(0); break;
            }
            return true;
        }
        return base.ProcessKeyboard(keyboard);
    }
}
