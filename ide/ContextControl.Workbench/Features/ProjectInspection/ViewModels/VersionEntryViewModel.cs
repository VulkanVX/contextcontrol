using System.Globalization;

namespace ContextControl.Workbench.ViewModels;

public sealed class VersionEntryViewModel(
    string version,
    string date,
    string commit,
    string reason,
    string fileName = "",
    string filePath = "",
    string snapshotPath = "",
    string previousSnapshotPath = "",
    int addedLines = 0,
    int removedLines = 0,
    long loc = 0,
    string currentFilePath = "") : ObservableObject
{
    private bool _isActive;
    private int _addedLines = addedLines;
    private int _removedLines = removedLines;
    private long _loc = loc;

    public string Version { get; } = version;
    public string Date { get; } = date;
    public string Commit { get; } = commit;
    public string Reason { get; } = reason;
    public string FileName { get; } = string.IsNullOrWhiteSpace(fileName)
        ? string.IsNullOrWhiteSpace(filePath) ? "file" : System.IO.Path.GetFileName(filePath)
        : fileName;
    public string FilePath { get; } = filePath;
    public string SnapshotPath { get; } = snapshotPath;
    public string PreviousSnapshotPath { get; } = previousSnapshotPath;
    public string CurrentFilePath { get; } = currentFilePath;
    public int AddedLines => _addedLines;
    public int RemovedLines => _removedLines;
    public long Loc => _loc;
    public string AddedLabel => $"+{FormatLineCount(AddedLines)}";
    public string RemovedLabel => $"-{FormatLineCount(RemovedLines)}";
    public string LocLabel => Loc > 0 ? FormatLineCount(Loc) : "";
    public string TotalLocLabel => Loc > 0 ? $"{FormatLineCount(Loc)} LOC" : "";

    public bool IsActive
    {
        get => _isActive;
        set => SetProperty(ref _isActive, value);
    }

    public void SetStats(int addedLines, int removedLines, long loc)
    {
        if (SetProperty(ref _addedLines, addedLines, nameof(AddedLines)))
        {
            OnPropertyChanged(nameof(AddedLabel));
        }

        if (SetProperty(ref _removedLines, removedLines, nameof(RemovedLines)))
        {
            OnPropertyChanged(nameof(RemovedLabel));
        }

        if (SetProperty(ref _loc, loc, nameof(Loc)))
        {
            OnPropertyChanged(nameof(LocLabel));
            OnPropertyChanged(nameof(TotalLocLabel));
        }
    }

    private static string FormatLineCount(long value)
    {
        return value.ToString("N0", CultureInfo.InvariantCulture);
    }
}
