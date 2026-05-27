using SadConsole;
using SadConsole.Configuration;
using RangedrifterClone.Core;

// Game constants
const int ScreenWidth = 80;
const int ScreenHeight = 50;

Settings.WindowTitle = "RangedrifterClone v0.1";

Builder gameStartup = new Builder()
    .SetWindowSizeInCells(ScreenWidth, ScreenHeight)
    .SetStartingScreen<RangedrifterClone.UISystem.RootScreen>()
    .IsStartingScreenFocused(true)
    .ConfigureFonts(true)
    .OnStart(Game_Started);

Game.Create(gameStartup);
Game.Instance.Run();
Game.Instance.Dispose();

void Game_Started(object? sender, GameHost host)
{
    // Initialize game systems
    GameEngine.Instance.Initialize(ScreenWidth, ScreenHeight);
}
