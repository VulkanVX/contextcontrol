namespace ContextControl.Workbench.ViewModels;

public sealed class FileRuleEntryViewModel(string kind, string value) : ObservableObject
{
    private string _value = value;

    public string Kind { get; } = kind;

    public string Value
    {
        get => _value;
        set => SetProperty(ref _value, value ?? "");
    }
}
