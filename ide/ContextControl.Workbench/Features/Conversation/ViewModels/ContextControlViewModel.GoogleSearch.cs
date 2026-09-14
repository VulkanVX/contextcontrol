using ContextControl.Workbench.Services;

namespace ContextControl.Workbench.ViewModels;

public sealed partial class ContextControlViewModel
{
    private IGoogleResearchBrowser? _googleBrowser;
    private sealed record ResearchSessionHandle(IGoogleResearchSession Browser, ChatRequestProgressViewModel Progress, System.ComponentModel.PropertyChangedEventHandler Handler);
    private readonly Dictionary<LocalLlmChatMessageViewModel, ResearchSessionHandle> _researchSessions = [];
    private readonly Dictionary<LocalLlmChatMessageViewModel, GoogleLivePhotos> _livePhotos = [];
    private IGoogleResearchBrowser BrowserFor(LocalLlmChatMessageViewModel assistant) => _researchSessions.TryGetValue(assistant, out var session) ? session.Browser
        : _googleBrowser ?? throw new InvalidOperationException("Research browser is unavailable.");
    private void EndGoogleResearch(LocalLlmChatMessageViewModel assistant)
    {
        if (_livePhotos.Remove(assistant, out var photos)) photos.Dispose();
        _messageResearch.Remove(assistant);
        if (!_researchSessions.Remove(assistant, out var session)) return;
        session.Progress.PropertyChanged -= session.Handler;
        session.Browser.Dispose();
    }
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
    public string GoogleSearchToolTip => "Let the local model search Google, read sources, and retry an answer that admits missing public knowledge. Relevant source photos can accompany the answer. Queries go to Google; this does not retrain the model. No API key needed.";
    public RelayCommand<object> ToggleGoogleSearchCommand { get; private set; } = null!;

    public void SetGoogleResearchBrowser(IGoogleResearchBrowser browser) => _googleBrowser = browser;
    private Action<string, string>? _openResearchArticle;
    public void SetResearchArticleOpener(Action<string, string> open) => _openResearchArticle = open;
    private RelayCommand<LocalLlmChatMessageViewModel>? _openResearchArticleCommand;
    public RelayCommand<LocalLlmChatMessageViewModel> OpenResearchArticleCommand => _openResearchArticleCommand ??= new(message =>
    {
        if (!ResearchArticlePage.CanOpen(message)) return;
        var article = ResearchArticlePage.Build(message!);
        _openResearchArticle?.Invoke(article.Title, article.Html);
    });
    public bool IsBrowserActionPreviewEnabled
    {
        get => _settings.BrowserActionPreviewEnabled;
        set { if (_settings.BrowserActionPreviewEnabled == value) return; _settings.BrowserActionPreviewEnabled = value; OnPropertyChanged(); SaveSettingsQuietly(); }
    }

    private async Task<string> PrepareGooglePromptAsync(string modelId, string question, string prompt, bool enabled,
        ChatSessionViewModel session, LocalLlmChatMessageViewModel assistant, ChatRequestProgressViewModel progress,
        CancellationToken cancellationToken, int contextTokens = 4096, bool knowledgeGap = false)
    {
        if (!enabled) return prompt;
        if (_googleBrowser is null) throw new InvalidOperationException("Google research is unavailable. Open this chat in the ContextControl desktop app, or switch Google off.");
        if (_googleBrowser is IGoogleResearchSessionFactory factory && !_researchSessions.ContainsKey(assistant))
        {
            var browser = factory.CreateSession(session.Id, session.Title, () => CancelCodexRequest(progress), cancellationToken);
            System.ComponentModel.PropertyChangedEventHandler handler = (_, e) => { if (e.PropertyName == nameof(progress.Status)) browser.SetStatus(progress.Status); };
            progress.PropertyChanged += handler;
            _researchSessions.Add(assistant, new(browser, progress, handler));
        }
        void UpdateStatus(string status)
        {
            progress.Status = status;
            assistant.IsAwaitingAnswer = true;
            assistant.LiveStage = ChatRequestProgressViewModel.CompactStage(status);
            RefreshLiveAssistantMessage(session, assistant);
        }
        async Task<string> AskModel(string request, CancellationToken token)
        {
            var answer = await _localLlmService.SendChatAsync(new LocalLlmRequest(modelId, request, "research", [],
                Math.Clamp(contextTokens, 2048, 8192), Think: false, MaxOutputTokens: 256), null, null, token);
            token.ThrowIfCancellationRequested();
            return GoogleResearchService.PlannerText(answer);
        }
        var result = await GoogleResearchService.ResearchAsync(question, AskModel, BrowserFor(assistant), UpdateStatus, cancellationToken, knowledgeGap);
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
            if (_livePhotos.Remove(assistant, out var previous)) previous.Dispose();
            _livePhotos[assistant] = new GoogleLivePhotos(result, BrowserFor(assistant), photo =>
            {
                assistant.AttachedFiles.Add(new ContextControlAttachmentViewModel(photo.SourceTitle, photo.SourceUrl, "web", photo.PreviewPath, photo.EntryTitle)
                    { IncludeInPrompt = false, PhotoCaption = photo.Caption, PhotoSection = photo.Section, PhotoKind = photo.Kind, IsSubjectPhoto = photo.IsSubjectPhoto });
                RefreshLiveAssistantMessage(session, assistant);
            }, cancellationToken);
        }
        RefreshLiveAssistantMessage(session, assistant);
        UpdateStatus(result.DidSearch ? "Writing an answer with sources…" : "Writing the answer…");
        if (!result.DidSearch) EndGoogleResearch(assistant);
        return prepared;
    }

    private async Task<LocalLlmChatResult> RecoverGoogleKnowledgeAsync(string question, LocalLlmRequest originalRequest,
        LocalLlmChatResult result, bool enabled, ChatSessionViewModel session, LocalLlmChatMessageViewModel assistant,
        ChatRequestProgressViewModel progress, IProgress<LocalLlmGenerationProgress> downstream,
        IProgress<string> terminal, CancellationToken cancellationToken)
    {
        var alreadySearched = _messageResearch.TryGetValue(assistant, out var research) && research.DidSearch;
        var recovered = await GoogleKnowledgeRecovery.RecoverAsync(question, result, enabled, alreadySearched, async token =>
        {
            terminal.Report("The answer reported missing knowledge; checking Google for public sources.");
            assistant.UpdateContent(result.Message ?? "");
            progress.IsIndeterminate = true;
            var prepared = await PrepareGooglePromptAsync(originalRequest.ModelId, question, originalRequest.Prompt, true,
                session, assistant, progress, token, originalRequest.ContextWindowTokens ?? 4096, knowledgeGap: true);
            return _messageResearch.TryGetValue(assistant, out var lookup) && lookup.DidSearch ? prepared : null;
        }, async (prepared, token) =>
        {
            assistant.UpdateContent("");
            assistant.IsAwaitingAnswer = true;
            assistant.LiveStage = "Writing";
            // Each generation has its own stream accumulator; the first draft must not prefix the corrected answer.
            return await _localLlmService.SendChatAsync(originalRequest with { Prompt = prepared, Think = false },
                CreateLiveAssistantProgress(assistant, downstream), terminal, token);
        }, cancellationToken);
        if (!recovered.Succeeded || !GoogleEvidenceText.HasInterfaceEntry(recovered.Message ?? "")
            || !_messageResearch.TryGetValue(assistant, out var sources)) return recovered;
        progress.Status = "Checking place names against sources…";
        assistant.IsAwaitingAnswer = true;
        assistant.LiveStage = "Checking";
        terminal.Report("A search-interface label was mistaken for a place name; correcting the answer from source evidence.");
        var repairPrompt = GoogleSearchContext.AugmentPrompt(originalRequest.Prompt, sources, originalRequest.ContextWindowTokens ?? 4096);
        return await GoogleEvidenceText.ReviewAsync(recovered, repairPrompt, (prompt, token) =>
            _localLlmService.SendChatAsync(originalRequest with { Prompt = prompt, Think = false, MaxOutputTokens = 1536 },
                CreateLiveAssistantProgress(assistant, downstream), terminal, token), cancellationToken);
    }

    private async Task AttachGoogleEntryPhotosAsync(ChatSessionViewModel session, LocalLlmChatMessageViewModel assistant,
        ChatRequestProgressViewModel progress, CancellationToken cancellationToken)
    {
        if (_googleBrowser is null || !_messageResearch.TryGetValue(assistant, out var research)) return;
        if (GoogleEntryPhotoService.EntryNames(assistant.VisibleText).Count == 0 && research.PhotoSubject is null) return;
        if (!_livePhotos.TryGetValue(assistant, out var photos)) return;
        progress.Status = "Answer ready · finishing photos…";
        await photos.CompleteAsync(assistant.VisibleText);
        var photoCount = photos.PhotoCount;
        AppendTerminalOutput($"Photo lookup: {photoCount} matching source photo(s) attached.");
        var reconciled = GoogleEvidenceText.WithoutPhotoCapabilityClaims(assistant.RawText);
        if (reconciled != assistant.RawText)
        {
            assistant.UpdateContent(reconciled);
            RefreshLiveAssistantMessage(session, assistant);
        }
        if (photoCount == 0 && research.PhotoSubject is not null && !cancellationToken.IsCancellationRequested)
        {
            assistant.UpdateContent(assistant.RawText + "\n\n*A matching photo could not be retrieved from the available sources.*");
            RefreshLiveAssistantMessage(session, assistant);
        }
    }
}
