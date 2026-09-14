namespace ContextControl.Workbench.ViewModels;

public sealed class BrowserTabViewModel(string id, string url, string title) : ObservableObject
{
    private string _url = url;
    private string _title = title;
    private bool _isActive;

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
