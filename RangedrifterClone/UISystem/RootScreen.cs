using SadConsole;
using RangedrifterClone.Core;

namespace RangedrifterClone.UISystem;

/// <summary>
/// Top-level screen. Routes between MainMenu → CharacterCreation → GameScreen → Settings
/// based on GameEngine.State, with no tight coupling between panels.
/// </summary>
public class RootScreen : ScreenObject
{
    private LoadingScreen?            _loading;
    private MainMenuScreen?           _mainMenu;
    private CharacterCreationScreen?  _charCreate;
    private GameScreen?               _gameScreen;
    private SettingsScreen?           _settings;
    private GameState                 _lastState = GameState.Loading;

    public RootScreen()
    {
        _loading = new LoadingScreen();
        Children.Add(_loading);
        _loading.IsVisible = true;
        _loading.IsFocused = true;
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
            case GameState.Loading:
                if (_loading == null)
                {
                    _loading = new LoadingScreen();
                    Children.Add(_loading);
                }
                _loading.IsVisible = true;
                _loading.IsFocused = true;
                break;

            case GameState.MainMenu:
                if (_mainMenu == null)
                {
                    _mainMenu = new MainMenuScreen();
                    Children.Add(_mainMenu);
                }
                _mainMenu.IsVisible = true;
                _mainMenu.IsFocused = true;
                break;

            case GameState.Settings:
                if (_settings == null)
                {
                    _settings = new SettingsScreen();
                    Children.Add(_settings);
                }
                _settings.Refresh();
                _settings.IsVisible = true;
                _settings.IsFocused = true;
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
        if (_loading    != null) _loading.IsVisible    = false;
        if (_mainMenu   != null) _mainMenu.IsVisible   = false;
        if (_settings   != null) _settings.IsVisible   = false;
        if (_charCreate != null) _charCreate.IsVisible = false;
        if (_gameScreen != null) _gameScreen.IsVisible = false;
    }
}
