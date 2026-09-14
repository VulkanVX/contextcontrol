namespace ContextControl.Workbench.ViewModels;

public sealed class WorkbenchModeOptionViewModel(string key, string name) : ObservableObject
{
    private bool _isActive;

    public string Key { get; } = key;
    public string Name { get; } = name;

    public bool IsActive
    {
        get => _isActive;
        set => SetProperty(ref _isActive, value);
    }
}
