// CC-DESC: Represents one station in the Context Control workflow timeline.

namespace ContextControl.Workbench.ViewModels;

public sealed class CcTimelineStageViewModel(string key, string title, string detail, string capsule) : ObservableObject
{
    private bool _isCurrent;
    private bool _isComplete;

    public string Key { get; } = key;
    public string Title { get; } = title;
    public string Detail { get; } = detail;
    public string Capsule { get; } = capsule;

    public bool IsCurrent
    {
        get => _isCurrent;
        private set
        {
            if (SetProperty(ref _isCurrent, value))
            {
                OnPropertyChanged(nameof(Marker));
            }
        }
    }

    public bool IsComplete
    {
        get => _isComplete;
        private set
        {
            if (SetProperty(ref _isComplete, value))
            {
                OnPropertyChanged(nameof(Marker));
            }
        }
    }

    public string Marker => IsCurrent ? ">" : IsComplete ? "*" : "-";

    public string ToolTipText => $"{Title}: {Detail}{Environment.NewLine}Capsule: {Capsule}";

    public void ApplyState(bool isCurrent, bool isComplete)
    {
        IsCurrent = isCurrent;
        IsComplete = isComplete && !isCurrent;
    }
}
