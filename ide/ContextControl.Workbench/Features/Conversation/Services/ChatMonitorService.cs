using System.Collections.ObjectModel;
using System.Text.Json;
using Avalonia.Threading;
using ContextControl.Workbench.ViewModels;

namespace ContextControl.Workbench.Services;

public sealed class ChatMonitorRecord
{
    public string SessionId { get; set; } = "";
    public string ProjectRoot { get; set; } = "";
    public string ScopeKey { get; set; } = "";
    public string ConversationKind { get; set; } = "chat";
    public string Title { get; set; } = "New chat";
}

/// <summary>Persistent membership, with live request state owned by the existing chat pipeline.</summary>
public sealed class ChatMonitorService : IDisposable
{
    private readonly string _path;
    private readonly DispatcherTimer _saveTimer;
    public ObservableCollection<ChatMonitorEntryViewModel> Entries { get; } = [];

    public ChatMonitorService(string contextRoot)
    {
        _path = Path.Combine(contextRoot, ".ccWorkbench.chat-monitor.json");
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _saveTimer.Tick += (_, _) => Flush();
        try
        {
            if (File.Exists(_path))
                foreach (var record in JsonSerializer.Deserialize<List<ChatMonitorRecord>>(File.ReadAllText(_path)) ?? [])
                    if (!string.IsNullOrWhiteSpace(record.SessionId) && !Contains(record.SessionId, record.ScopeKey, record.ConversationKind))
                        Entries.Add(new ChatMonitorEntryViewModel(record));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
    }

    public bool Contains(string sessionId, string scope, string kind) => Find(sessionId, scope, kind) is not null;
    public ChatMonitorEntryViewModel? Find(string sessionId, string scope, string kind) => Entries.FirstOrDefault(entry =>
        string.Equals(entry.SessionId, sessionId, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(entry.ScopeKey, scope, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(entry.ConversationKind, kind, StringComparison.OrdinalIgnoreCase));

    public ChatMonitorEntryViewModel Track(ChatSessionViewModel session, string projectRoot, string scope, string kind)
    {
        var entry = Find(session.Id, scope, kind);
        if (entry is null)
        {
            entry = new ChatMonitorEntryViewModel(new ChatMonitorRecord { SessionId = session.Id, ProjectRoot = projectRoot, ScopeKey = scope, ConversationKind = kind, Title = session.Title });
            Entries.Insert(0, entry);
        }
        entry.Bind(session);
        ScheduleSave();
        return entry;
    }

    public void BindIfTracked(ChatSessionViewModel session, string scope, string kind) => Find(session.Id, scope, kind)?.Bind(session);

    public void Remove(ChatMonitorEntryViewModel entry)
    {
        if (!Entries.Remove(entry)) return;
        entry.Dispose();
        ScheduleSave();
    }

    public void CompleteRequest(ChatRequestProgressViewModel request)
    {
        foreach (var entry in Entries) entry.CompleteRequest(request);
        ScheduleSave();
    }

    private void ScheduleSave() { _saveTimer.Stop(); _saveTimer.Start(); }
    public void Flush()
    {
        _saveTimer.Stop();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temporaryPath = _path + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(Entries.Select(entry => entry.Record), new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporaryPath, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    public void Dispose() { Flush(); foreach (var entry in Entries) entry.Dispose(); }
}
