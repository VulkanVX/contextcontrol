using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using ContextControl.Workbench.Services;
using ContextControl.Workbench.Views;

namespace ContextControl.Workbench.ViewModels;

public sealed partial class ContextControlViewModel
{
    public bool AutoCheckGames
    {
        get => _settings.AutoCheckGames;
        set { if (_settings.AutoCheckGames == value) return; _settings.AutoCheckGames = value; OnPropertyChanged(); SaveSettingsQuietly(); }
    }
    private async Task<LocalLlmChatResult> ReviewGeneratedGameAsync(LocalLlmChatResult result, string request,
        LocalLlmModelViewModel model, ChatSessionViewModel session, LocalLlmChatMessageViewModel assistant,
        ChatRequestProgressViewModel progress, IProgress<LocalLlmGenerationProgress> downstream,
        IProgress<string> terminal, CancellationToken token)
    {
        if (!AutoCheckGames || !result.Succeeded && !result.OutputLimited) return result;
        var owner = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        var lab = new GameLabWindow { ShowActivated = false };
        var directory = Path.Combine(_settings.ContextControlRoot, ".ccWorkbench.generated-projects",
            "game-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            if (owner is null) lab.Show(); else lab.Show(owner);
            var review = await GameCreationReview.RunAsync(result, request, directory,
                (game, cancellation) => lab.ValidateAsync(game, cancellation), async (prompt, cancellation) =>
                {
                    assistant.UpdateContent(""); assistant.IsAwaitingAnswer = true; assistant.LiveStage = "Repairing";
                    model.RefreshAvailableMemory();
                    var context = ResolveRequestedContextTokens(model, ContextCapsulePhase.Chat, prompt, game: true);
                    RequirePromptRoom(prompt, context);
                    return await _localLlmService.SendChatAsync(new(model.Id, prompt, "game repair", [], context,
                        Think: ShouldRequestLocalThinking(model)), CreateLiveAssistantProgress(assistant, downstream), terminal, cancellation);
                }, status =>
                {
                    progress.Status = status; assistant.IsAwaitingAnswer = true;
                    assistant.LiveStage = status.StartsWith("Repairing") ? "Repairing" : "Checking";
                    RefreshLiveAssistantMessage(session, assistant); terminal.Report(status);
                }, token);
            terminal.Report("Game versions and checks saved: " + directory);
            if (!token.IsCancellationRequested && !lab.IsClosed && GameArtifact.Parse(review.Response.Message ?? "") is { } game)
            {
                lab.LoadGame(game, prompt => { if (ChatSessions.Contains(session)) SelectChatSession(session); PrepareGameRepair(prompt); owner?.Activate(); });
                lab.Activate();
            }
            else if (!lab.IsClosed) lab.Close();
            return review.Response;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or OperationCanceledException)
        {
            if (!lab.IsClosed) lab.Close();
            terminal.Report("Game check unavailable: " + ex.Message);
            return result with { Message = (result.Message ?? "") + "\n\n**Game not checked.** " + ex.Message };
        }
    }
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
