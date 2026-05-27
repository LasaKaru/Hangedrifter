using SadConsole;
using RangedrifterClone.Core;

namespace RangedrifterClone.UISystem;

/// <summary>
/// Top-level screen. Routes between MainMenu → CharacterCreation → GameScreen
/// based on GameEngine.State, with no tight coupling between panels.
/// </summary>
public class RootScreen : ScreenObject
{
    private MainMenuScreen?          _mainMenu;
    private CharacterCreationScreen? _charCreate;
    private GameScreen?              _gameScreen;
    private GameState                _lastState = GameState.MainMenu;

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

        HideAll();

        switch (state)
        {
            case GameState.MainMenu:
                _mainMenu!.IsVisible = true;
                _mainMenu.IsFocused  = true;
                break;

            case GameState.CharacterCreation:
                if (_charCreate == null)
                {
                    _charCreate = new CharacterCreationScreen();
                    Children.Add(_charCreate);
                }
                _charCreate.Refresh();
                _charCreate.IsVisible = true;
                _charCreate.IsFocused = true;
                break;

            case GameState.Playing:
                if (_gameScreen == null)
                {
                    _gameScreen = new GameScreen();
                    Children.Add(_gameScreen);
                }
                _gameScreen.ResetGameOver();
                _gameScreen.IsVisible = true;
                _gameScreen.IsFocused = true;
                break;

            case GameState.GameOver:
                if (_gameScreen != null)
                {
                    _gameScreen.IsVisible = true;
                    _gameScreen.ShowGameOver();
                }
                break;
        }
    }

    private void HideAll()
    {
        if (_mainMenu   != null) _mainMenu.IsVisible   = false;
        if (_charCreate != null) _charCreate.IsVisible = false;
        if (_gameScreen != null) _gameScreen.IsVisible = false;
    }
}
