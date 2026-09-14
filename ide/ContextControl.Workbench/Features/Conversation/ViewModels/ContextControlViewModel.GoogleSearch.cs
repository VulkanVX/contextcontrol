using ContextControl.Workbench.Services;

namespace ContextControl.Workbench.ViewModels;

public sealed partial class ContextControlViewModel
{
    private IGoogleResearchBrowser? _googleBrowser;
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<LocalLlmChatMessageViewModel, GoogleResearchResult> _messageResearch = new();

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
            for (var i = 0; i < search.Sources.Count; i++)
            {
                var source = search.Sources[i];
                assistant.AttachedFiles.Add(new ContextControlAttachmentViewModel($"[{i + 1}] {source.Title}", source.Url, "web"));
            }
            _messageResearch.Remove(assistant);
            _messageResearch.Add(assistant, result);
        }
        RefreshLiveAssistantMessage(session, assistant);
        UpdateStatus(result.DidSearch ? "Writing an answer with sources…" : "Writing the answer…");
        return prepared;
    }

    private async Task AttachGoogleEntryPhotosAsync(ChatSessionViewModel session, LocalLlmChatMessageViewModel assistant,
        ChatRequestProgressViewModel progress, CancellationToken cancellationToken)
    {
        if (_googleBrowser is null || !_messageResearch.TryGetValue(assistant, out var research)) return;
        _messageResearch.Remove(assistant);
        if (GoogleEntryPhotoService.EntryNames(assistant.VisibleText).Count == 0) return;
        progress.Status = "Answer ready · finding photos for its entries…";
        await GoogleEntryPhotoService.LoadAsync(assistant.VisibleText, research, _googleBrowser, photo =>
        {
            assistant.AttachedFiles.Add(new ContextControlAttachmentViewModel(photo.SourceTitle, photo.SourceUrl, "web", photo.PreviewPath, photo.EntryTitle)
                { IncludeInPrompt = false });
            RefreshLiveAssistantMessage(session, assistant);
        }, cancellationToken);
    }
}
