using System.ComponentModel;
using ContextControl.Workbench.Services;

namespace ContextControl.Workbench.ViewModels;

public sealed class ChatMonitorEntryViewModel : ObservableObject, IDisposable
{
    private ChatSessionViewModel? _session;
    private readonly List<ChatRequestProgressViewModel> _requests = [];
    public ChatMonitorEntryViewModel(ChatMonitorRecord record) => Record = record;
    public ChatMonitorRecord Record { get; }
    public string SessionId => Record.SessionId;
    public string ProjectRoot => Record.ProjectRoot;
    public string ScopeKey => Record.ScopeKey;
    public string ConversationKind => Record.ConversationKind;
    public string Title => _session?.Title ?? Record.Title;
    public bool IsActive => _requests.Count > 0;
    public bool HasNewResponse => _session?.HasNewResponse == true;
    public string Status => _requests.LastOrDefault()?.Status ?? (HasNewResponse ? "Response ready" : "Ready");
    public string Stats => _requests.LastOrDefault() is { } request
        ? $"{request.SizeLabel} · {request.SpeedLabel} · {request.ElapsedLabel}"
        : _session is { } session ? $"{session.MessageCount} messages" : "Saved chat";
    public string DisplayLine => $"{Compact(Title, 24)}  ·  {Compact(Status, 20)}" + (IsActive ? $"  ·  {Stats}" : "");
    public string Details => $"{Title}\n{ProjectRoot}\n{Status} · {Stats}";
    private static string Compact(string text, int length) => text.Length <= length ? text : text[..(length - 1)] + "…";

    public void Bind(ChatSessionViewModel session)
    {
        if (ReferenceEquals(_session, session)) return;
        if (_session is not null) _session.PropertyChanged -= Changed;
        _session = session;
        _session.PropertyChanged += Changed;
        Record.Title = session.Title;
        Refresh();
    }

    public void AddRequest(ChatRequestProgressViewModel request)
    {
        if (_requests.Contains(request)) return;
        _requests.Add(request);
        request.PropertyChanged += Changed;
        Refresh();
    }

    public void CompleteRequest(ChatRequestProgressViewModel request)
    {
        if (_requests.Remove(request)) request.PropertyChanged -= Changed;
        Refresh();
    }

    private void Changed(object? sender, PropertyChangedEventArgs e) => Refresh();
    private void Refresh()
    {
        Record.Title = Title;
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(IsActive));
        OnPropertyChanged(nameof(HasNewResponse));
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(Stats));
        OnPropertyChanged(nameof(DisplayLine));
        OnPropertyChanged(nameof(Details));
    }

    public void Dispose()
    {
        if (_session is not null) _session.PropertyChanged -= Changed;
        foreach (var request in _requests) request.PropertyChanged -= Changed;
        _requests.Clear();
    }
}
