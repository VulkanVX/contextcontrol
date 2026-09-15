using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using ContextControl.Workbench.Services;
using ContextControl.Workbench.Views;

namespace ContextControl.Workbench.ViewModels;

public sealed partial class ContextControlViewModel
{
    private readonly Dictionary<string, bool> _gameModes = [];
    private GameLabWindow? _gameLab;
    public bool IsGameCreationEnabled
    {
        get => SelectedChatSession is { } session && _gameModes.GetValueOrDefault(session.Id);
        set
        {
            var session = EnsureSelectedChatSession();
            _gameModes[session.Id] = value;
            OnPropertyChanged(); OnPropertyChanged(nameof(GameCreationLabel));
        }
    }
    private void RefreshGameMode()
    {
        if (SelectedChatSession is { } session && !_gameModes.ContainsKey(session.Id))
            _gameModes[session.Id] = ChatMessages.Any(message => GameArtifact.FromMessage(message) is not null);
        OnPropertyChanged(nameof(IsGameCreationEnabled)); OnPropertyChanged(nameof(GameCreationLabel));
    }
    public string GameCreationLabel => IsGameCreationEnabled ? "◈ Game creation on" : "◈ Create game";
    public void PrepareGameRepair(string prompt)
    {
        PromptModeKey = "context";
        IsGameCreationEnabled = true;
        PromptText = prompt;
        IsPromptOpen = true;
    }
    private RelayCommand<object>? _toggleGameCreationCommand;
    public RelayCommand<object> ToggleGameCreationCommand => _toggleGameCreationCommand ??= new(_ => IsGameCreationEnabled = !IsGameCreationEnabled);
    private RelayCommand<LocalLlmChatMessageViewModel>? _runGameCommand;
    public RelayCommand<LocalLlmChatMessageViewModel> RunGameCommand => _runGameCommand ??= new(message =>
    {
        var game = GameArtifact.FromMessage(message);
        if (game is null) return;
        var session = SelectedChatSession;
        var owner = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (_gameLab is null || _gameLab.IsClosed)
        {
            _gameLab = new GameLabWindow();
            if (owner is null) _gameLab.Show(); else _gameLab.Show(owner);
        }
        else if (!_gameLab.IsVisible)
        {
            if (owner is null) _gameLab.Show(); else _gameLab.Show(owner);
        }
        _gameLab.LoadGame(game, prompt =>
        {
            if (session is not null && ChatSessions.Contains(session)) SelectChatSession(session);
            PrepareGameRepair(prompt);
            owner?.Activate();
        });
        _gameLab.Activate();
    }, message => GameArtifact.FromMessage(message) is not null);
}
