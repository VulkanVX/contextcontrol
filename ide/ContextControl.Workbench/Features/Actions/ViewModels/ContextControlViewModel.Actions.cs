using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using ContextControl.Workbench.Services;
using ContextControl.Workbench.Views;

namespace ContextControl.Workbench.ViewModels;

public sealed partial class ContextControlViewModel
{
    private RelayCommand<object>? _showActions;
    public RelayCommand<object> ShowActionsCommand => _showActions ??= new(_ => ShowActionLibrary());
    private void ShowActionLibrary()
    {
        var owner = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        var window = new ActionLibraryWindow { Selected = action => { PromptText = action.Command + " "; IsPromptOpen = true; owner?.Activate(); } };
        WorkbenchThemeResources.Apply(window, _settings.ThemeKey, uiFontSize: _settings.UiFontSize);
        if (owner is null) window.Show(); else window.Show(owner);
    }
    // Dispatch once, before route selection. Command text never becomes an instruction the model must guess.
    private bool DispatchChatAction(ref string message)
    {
        if (!message.StartsWith('/')) return true;
        var (action, argument) = ChatActionCatalog.Parse(message);
        if (action is null) { PhaseTitle = "Unknown action"; PhaseDetail = "Type / to see actions, or open the Actions library."; return false; }
        if (action.Command == "/actions") { PromptText = argument; ShowActionLibrary(); return false; }
        if (action.Command == "/run")
        {
            var latest = ChatMessages.LastOrDefault(m => GameArtifact.FromMessage(m) is not null);
            if (latest is null) { PhaseTitle = "No complete game yet"; PhaseDetail = "Use /game to create a game first."; }
            else { RunGameCommand.Execute(latest); PromptText = argument; }
            return false;
        }
        if (argument.Length == 0) { PhaseTitle = action.Title; PhaseDetail = action.Description + " Example: " + action.Example; return false; }
        if (action.Command == "/game" && GameArtifact.RequestsNativeRuntime(argument))
        { PhaseTitle = "Game Lab uses HTML"; PhaseDetail = "Use /context for a native-engine project, or request a browser game with /game."; return false; }
        PromptModeKey = action.Command == "/image" ? "imagegen" : "context";
        IsGameCreationEnabled = action.Command == "/game";
        IsAutopilotEnabled = action.Command == "/context";
        IsGoogleSearchEnabled = action.Command == "/search";
        message = argument;
        PromptText = argument;
        return true;
    }
}
