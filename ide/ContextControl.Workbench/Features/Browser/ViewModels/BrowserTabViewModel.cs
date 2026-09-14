namespace ContextControl.Workbench.ViewModels;

public sealed class BrowserTabViewModel(string id, string url, string title) : ObservableObject
{
    private string _url = url;
    private string _title = title;
    private bool _isActive;
    private bool _isAgentWorking;
    private string _agentStatus = "Ready";
    private readonly Queue<string> _actions = new();
    private Avalonia.Media.Imaging.Bitmap? _actionPreviewImage;
    public Avalonia.Media.Imaging.Bitmap? ActionPreviewImage
    {
        get => _actionPreviewImage;
        set { var old = _actionPreviewImage; if (SetProperty(ref _actionPreviewImage, value)) old?.Dispose(); }
    }
    public string ActionPreview => string.Join(Environment.NewLine, _actions);
    public bool IsResearch { get; init; }
    public string? DocumentHtml { get; init; }
    public Action? CancelResearch { get; set; }
    public bool IsAgentWorking { get => _isAgentWorking; set { if (SetProperty(ref _isAgentWorking, value)) OnPropertyChanged(nameof(CloseToolTip)); } }
    public string AgentStatus
    {
        get => _agentStatus;
        set
        {
            if (!SetProperty(ref _agentStatus, value)) return;
            _actions.Enqueue(value); while (_actions.Count > 4) _actions.Dequeue();
            OnPropertyChanged(nameof(ActionPreview));
        }
    }
    public string CloseToolTip => IsAgentWorking ? "Stop this research chat and close tab" : "Close tab";

    public string Id { get; } = id;

    public string Url
    {
        get => _url;
        set => SetProperty(ref _url, value ?? "");
    }

    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, string.IsNullOrWhiteSpace(value) ? "New tab" : value.Trim());
    }

    public bool IsActive
    {
        get => _isActive;
        set => SetProperty(ref _isActive, value);
    }
}
