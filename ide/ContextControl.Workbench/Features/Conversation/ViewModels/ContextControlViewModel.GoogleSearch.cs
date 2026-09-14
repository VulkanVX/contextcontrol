using ContextControl.Workbench.Services;

namespace ContextControl.Workbench.ViewModels;

public sealed partial class ContextControlViewModel
{
    private IGoogleResearchBrowser? _googleBrowser;

    public bool IsGoogleSearchEnabled
    {
        get => _settings.GoogleSearchEnabled;
        set
        {
            if (_settings.GoogleSearchEnabled == value) return;
            _settings.GoogleSearchEnabled = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(GoogleSearchLabel));
            OnPropertyChanged(nameof(AutopilotModeToolTip));
            SaveSettingsQuietly();
        }
    }

    public string GoogleSearchLabel => IsGoogleSearchEnabled ? "Google auto" : "Google off";
    public bool CanUseGoogleSearch => IsLocalPromptMode;
    public string GoogleSearchToolTip => "Let the local model search Google, choose links to read, and cite sources when your request needs web research. Only the generated query goes to Google. No API key needed.";
    public RelayCommand<object> ToggleGoogleSearchCommand { get; private set; } = null!;

    public void SetGoogleResearchBrowser(IGoogleResearchBrowser browser) => _googleBrowser = browser;

    private async Task<string> PrepareGooglePromptAsync(string modelId, string question, string prompt, bool enabled,
        ChatSessionViewModel session, LocalLlmChatMessageViewModel assistant, ChatRequestProgressViewModel progress,
        CancellationToken cancellationToken, int contextTokens = 4096)
    {
        if (!enabled) return prompt;
        if (_googleBrowser is null) throw new InvalidOperationException("Google research is unavailable. Open this chat in the ContextControl desktop app, or switch Google off.");
        void UpdateStatus(string status)
        {
            progress.Status = status;
            assistant.UpdateLiveStatus(status);
            RefreshLiveAssistantMessage(session, assistant);
        }
        async Task<string> AskModel(string request, CancellationToken token)
        {
            var answer = await _localLlmService.SendChatAsync(new LocalLlmRequest(modelId, request, "research", [],
                Math.Clamp(contextTokens, 2048, 8192), Think: false, MaxOutputTokens: 256), null, null, token);
            token.ThrowIfCancellationRequested();
            if (!answer.Succeeded) throw new InvalidOperationException("The local research planner failed: " + answer.Status);
            return answer.Message ?? "";
        }
        var result = await GoogleResearchService.ResearchAsync(question, AskModel, _googleBrowser, UpdateStatus, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var prepared = GoogleSearchContext.AugmentPrompt(prompt, result, contextTokens);
        if (result.Search is { } search)
        {
            UpdateStatus("Loading source photo previews…");
            var previews = await GooglePhotoPreviewService.Shared.LoadManyAsync(search.Sources, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            for (var i = 0; i < search.Sources.Count; i++)
            {
                var source = search.Sources[i];
                assistant.AttachedFiles.Add(new ContextControlAttachmentViewModel($"[{i + 1}] {source.Title}", source.Url, "web", previews[i]));
            }
        }
        RefreshLiveAssistantMessage(session, assistant);
        UpdateStatus(result.DidSearch ? "Writing an answer with sources…" : "Writing the answer…");
        return prepared;
    }
}
