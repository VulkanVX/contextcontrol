namespace ContextControl.Workbench.ViewModels;

public sealed class ProjectTabViewModel(
    string id,
    string iconText,
    string name,
    string location,
    string fileCount,
    string directoryCount,
    string commit,
    string? projectRoot = null) : ObservableObject
{
    private bool _isActive;
    private string _location = location;

    public string Id { get; } = id;
    public string IconText { get; } = iconText;
    public string Name { get; } = name;
    public string Location => _location;
    public string ProjectRoot { get; } = projectRoot ?? location;
    public string FileCount { get; } = fileCount;
    public string DirectoryCount { get; } = directoryCount;
    public string Commit { get; } = commit;
    public string CompactStatsLine => $"{Location} | {FileCount} files | {DirectoryCount} dirs | {Commit} commit";

    public bool IsActive
    {
        get => _isActive;
        set => SetProperty(ref _isActive, value);
    }

    public void UpdateLocation(string value)
    {
        SetProperty(ref _location, value, nameof(Location));
        OnPropertyChanged(nameof(CompactStatsLine));
    }
}
