// CC-DESC: View-model item for queued external version transitions with selectable/adapted flow labels.

using ContextControl.Workbench.Services;

namespace ContextControl.Workbench.ViewModels;

public sealed class ExternalChangeItemViewModel : ObservableObject
{
    private bool _isSelected = true;
    private int _effectiveVersionBefore;
    private string _effectivePreviousSnapshotPath;
    private bool _isAdapted;

    public ExternalChangeItemViewModel(ExternalFileChange change)
    {
        Change = change;
        _effectiveVersionBefore = change.VersionBefore;
        _effectivePreviousSnapshotPath = change.PreviousSnapshotPath;
    }

    public event EventHandler? SelectionChanged;

    public ExternalFileChange Change { get; }
    public string QueueId => $"{RelativePath}|{Change.VersionAfter}";
    public string FileName => Change.FileName;
    public string RelativePath => Change.RelativePath;
    public string VersionFlow => IsAdapted
        ? $"v{EffectiveVersionBefore} > v{Change.VersionAfter} adapted"
        : $"v{EffectiveVersionBefore} > v{Change.VersionAfter}";
    public string Date => Change.DisplayDate;
    public string AddedLabel => $"+{Change.AddedLines:N0}";
    public string RemovedLabel => $"-{Change.RemovedLines:N0}";
    public string LocLabel => $"{Change.Loc:N0}";
    public string SnapshotPath => Change.SnapshotPath;
    public string EffectivePreviousSnapshotPath => _effectivePreviousSnapshotPath;
    public int EffectiveVersionBefore => _effectiveVersionBefore;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetProperty(ref _isSelected, value))
            {
                SelectionChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public bool IsAdapted
    {
        get => _isAdapted;
        private set
        {
            if (SetProperty(ref _isAdapted, value))
            {
                OnPropertyChanged(nameof(VersionFlow));
            }
        }
    }

    public void SetEffectiveBase(int versionBefore, string previousSnapshotPath)
    {
        var changed = false;
        if (_effectiveVersionBefore != versionBefore)
        {
            _effectiveVersionBefore = versionBefore;
            changed = true;
            OnPropertyChanged(nameof(EffectiveVersionBefore));
        }

        if (!string.Equals(_effectivePreviousSnapshotPath, previousSnapshotPath, StringComparison.OrdinalIgnoreCase))
        {
            _effectivePreviousSnapshotPath = previousSnapshotPath;
            changed = true;
            OnPropertyChanged(nameof(EffectivePreviousSnapshotPath));
        }

        IsAdapted = versionBefore != Change.VersionBefore;
        if (changed)
        {
            OnPropertyChanged(nameof(VersionFlow));
        }
    }
}
