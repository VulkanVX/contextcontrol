namespace ContextControl.Workbench.ViewModels;

public sealed partial class ContextControlViewModel
{
    public System.Collections.ObjectModel.ObservableCollection<LocalLlmChatMessageViewModel> ReasoningMessages { get; } = [];
    private bool _isReasoningPaneOpen;
    private LocalLlmChatMessageViewModel? _selectedReasoningMessage;
    public bool IsReasoningPaneOpen
    {
        get => _isReasoningPaneOpen;
        set { if (SetProperty(ref _isReasoningPaneOpen, value) && value) RefreshReasoningMessages(); }
    }
    public LocalLlmChatMessageViewModel? SelectedReasoningMessage => _selectedReasoningMessage;
    public void OpenReasoning(LocalLlmChatMessageViewModel? message)
    {
        message ??= ChatMessages.LastOrDefault(item => item.HasThinking || item.IsAwaitingAnswer);
        if (_selectedReasoningMessage is not null) _selectedReasoningMessage.IsReasoningSelected = false;
        _selectedReasoningMessage = message;
        if (message is not null) message.IsReasoningSelected = true;
        IsReasoningPaneOpen = true;
        OnPropertyChanged(nameof(SelectedReasoningMessage));
    }

    private void InitializeReasoning() => ChatMessages.CollectionChanged += (_, _) => { if (IsReasoningPaneOpen) RefreshReasoningMessages(); };
    private void RefreshReasoningMessages()
    {
        ReasoningMessages.Clear();
        var prompt = "";
        var reply = 0;
        foreach (var message in ChatMessages)
        {
            if (message.IsUser) { prompt = message.VisibleText; continue; }
            message.ReasoningContext = $"Reply {++reply} · " + (prompt.Length > 120 ? prompt[..120] + "…" : prompt);
            ReasoningMessages.Add(message);
        }
    }
}
