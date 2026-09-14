using ContextControl.Workbench.Services;
using System.Runtime.CompilerServices;

namespace ContextControl.Workbench.ViewModels;

public sealed partial class ContextControlViewModel
{
    private readonly ConditionalWeakTable<ChatSessionViewModel, ChatMonitorRecord> _monitorScopes = new();
    private readonly Dictionary<ChatRequestProgressViewModel, ChatSessionViewModel> _generationSessions = [];
    public ChatMonitorService ChatMonitor { get; }
    public ChatRequestProgressViewModel? SelectedChatActivity => ChatRequestProgressItems.LastOrDefault(request => request.SessionId == SelectedChatSession?.Id);
    public bool HasSelectedChatActivity => SelectedChatActivity is not null;

    public void FlushPendingChatDraft()
    {
        _promptDraftSaveTimer.Stop();
        SaveChatHistory();
    }

    private void RegisterChatMonitorScope(ChatSessionViewModel session) => _monitorScopes.GetValue(session, _ => new ChatMonitorRecord
    {
        ProjectRoot = string.IsNullOrWhiteSpace(_activeProjectRoot) ? _processService.ContextRoot : _activeProjectRoot,
        ScopeKey = _chatHistoryScopeKey, ConversationKind = _activeConversationKind
    });

    private ChatMonitorEntryViewModel TrackChatSession(ChatSessionViewModel session)
    {
        RegisterChatMonitorScope(session);
        var identity = _monitorScopes.GetValue(session, _ => throw new InvalidOperationException("Missing chat scope."));
        return ChatMonitor.Track(session, identity.ProjectRoot, identity.ScopeKey, identity.ConversationKind);
    }

    public bool IsChatMonitored(ChatSessionViewModel session) => ChatMonitor.Contains(session.Id, _chatHistoryScopeKey, _activeConversationKind);

    public void ToggleChatMonitorSession(ChatSessionViewModel session)
    {
        var entry = ChatMonitor.Find(session.Id, _chatHistoryScopeKey, _activeConversationKind);
        if (entry is not null) ChatMonitor.Remove(entry);
        else
        {
            entry = TrackChatSession(session);
            foreach (var request in ChatRequestProgressItems.Where(item => item.SessionId == session.Id)) entry.AddRequest(request);
        }
    }

    public bool IsMonitorTargetSelected(ChatMonitorEntryViewModel entry) =>
        string.Equals(_chatHistoryScopeKey, entry.ScopeKey, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(_activeConversationKind, entry.ConversationKind, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(SelectedChatSession?.Id, entry.SessionId, StringComparison.OrdinalIgnoreCase);

    public bool OpenMonitoredChat(ChatMonitorEntryViewModel entry)
    {
        if (!string.Equals(_chatHistoryScopeKey, entry.ScopeKey, StringComparison.OrdinalIgnoreCase)) return false;
        if (!string.Equals(_activeConversationKind, entry.ConversationKind, StringComparison.OrdinalIgnoreCase))
        {
            SaveChatHistory();
            _isSwitchingConversationKind = true;
            try { _activeConversationKind = entry.ConversationKind; LoadChatHistory(); }
            finally { _isSwitchingConversationKind = false; }
        }
        var session = ChatSessions.FirstOrDefault(item => string.Equals(item.Id, entry.SessionId, StringComparison.OrdinalIgnoreCase));
        if (session is null) return false;
        SelectChatSession(session);
        OpenPrompt();
        _promptModeWorkspaceRequester?.Invoke();
        return IsMonitorTargetSelected(entry);
    }

    private void RefreshSelectedChatActivity()
    {
        OnPropertyChanged(nameof(SelectedChatActivity));
        OnPropertyChanged(nameof(HasSelectedChatActivity));
    }
}
