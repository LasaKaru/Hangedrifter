using SadConsole;
using RangedrifterClone.Core;

namespace RangedrifterClone.UISystem;

/// <summary>
/// Top-level screen object. Owns all sub-screens and routes between them
/// based on the current GameState — no tight coupling between panels.
/// </summary>
public class RootScreen : ScreenObject
{
    private MainMenuScreen? _mainMenu;
    private GameScreen?     _gameScreen;
    private GameState       _lastState = GameState.MainMenu;

    public RootScreen()
    {
        _mainMenu = new MainMenuScreen();
        Children.Add(_mainMenu);
        _mainMenu.IsVisible = true;
        _mainMenu.IsFocused = true;
    }

    public override void Update(TimeSpan delta)
    {
        base.Update(delta);
        var state = GameEngine.Instance.State;
        if (state == _lastState) return;
        _lastState = state;

        switch (state)
        {
            case GameState.Playing:
                _mainMenu!.IsVisible = false;
                if (_gameScreen == null)
                {
                    _gameScreen = new GameScreen();
                    Children.Add(_gameScreen);
                }
                _gameScreen.IsVisible = true;
                _gameScreen.IsFocused = true;
                break;

            case GameState.MainMenu:
                _gameScreen!.IsVisible = false;
                _mainMenu!.IsVisible   = true;
                _mainMenu.IsFocused    = true;
                break;

            case GameState.GameOver:
                _gameScreen?.ShowGameOver();
                break;
        }
    }
}
