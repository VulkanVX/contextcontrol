using System.Collections.ObjectModel;
using Avalonia;

namespace ContextControl.Workbench.ViewModels;

public sealed class ProjectNodeViewModel(
    string name,
    string path,
    bool isFolder,
    string versionLabel,
    IEnumerable<ProjectNodeViewModel>? children = null,
    bool isExternal = false,
    bool canIncludeExternal = false,
    long loc = 0,
    int fileCount = 0,
    int diskFileCount = 0,
    int directDiskFileCount = 0) : ObservableObject
{
    private bool _isExpanded;
    private bool _isCurrent;
    private bool _isExternal = isExternal;
    private bool _canIncludeExternal = canIncludeExternal;
    private int _depth;
    private bool _isLast;
    private IReadOnlyList<bool> _ancestorContinues = [];
    private string _versionLabel = versionLabel;
    private long _loc = loc;
    private int _fileCount = fileCount;
    private int _diskFileCount = diskFileCount;
    private readonly int _directDiskFileCount = directDiskFileCount;
    private const double IndentStep = 8.0;
    private const double ArrowColumnWidth = 15.0;
    private const double ConnectorTail = 3.0;

    public string Name { get; } = name;
    public string Path { get; } = path;
    public bool IsFolder { get; } = isFolder;
    public bool IsFile => !IsFolder;
    public bool IsExternal => _isExternal;
    public bool CanIncludeExternal => _canIncludeExternal;
    public bool IsRegularFolder => IsFolder && !IsExternal;
    public bool HasChildren => Children.Count > 0;
    public bool HasExpandedChildren => HasChildren && IsExpanded;
    public string VersionLabel => _versionLabel;
    public long Loc => _loc;
    public int FileCount => _fileCount;
    public int DiskFileCount => _diskFileCount;
    public int DirectDiskFileCount => _directDiskFileCount;
    public int DisplayFileCount => IsRegularFolder ? Math.Max(FileCount, DiskFileCount) : FileCount;
    public string LocLabel => Loc > 0 ? Loc.ToString("N0") : "";
    public string LocMetricLabel => Loc > 0 ? $"LOC {Loc:N0}" : "";
    public string FileCountLabel => IsRegularFolder && DisplayFileCount > 0 ? $"F {DisplayFileCount:N0}" : "";
    public string DirectoryStatsLabel => IsRegularFolder
        ? string.IsNullOrWhiteSpace(LocMetricLabel) ? FileCountLabel : $"{FileCountLabel} {LocMetricLabel}"
        : "";
    public string DisplayName => IsFolder ? $"{Name}/" : Name;
    public string NodeRoleLabel => IsExternal ? "skip" : LocMetricLabel;
    public string NodeBadgeText => $"[{NodeRoleLabel.PadRight(4)[..4]}]";
    public string ConnectorText => BuildConnectorText();
    public string SpacerText => BuildSpacerText();
    public string KindLabel => IsFolder ? "/" : ".";
    public double ArrowAngle => IsExpanded ? 90.0 : 0.0;
    public double ConnectorWidth => Depth * IndentStep + ArrowColumnWidth + ConnectorTail;
    public Thickness ArrowMargin => new(Depth * IndentStep + 1.5, 0, 0, 0);
    public ObservableCollection<ProjectNodeViewModel> Children { get; } = new(SortChildren(children ?? []));

    public bool IsCurrent
    {
        get => _isCurrent;
        set => SetProperty(ref _isCurrent, value);
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (SetProperty(ref _isExpanded, value))
            {
                OnPropertyChanged(nameof(ArrowAngle));
                OnPropertyChanged(nameof(HasExpandedChildren));
                OnPropertyChanged(nameof(ConnectorText));
                OnPropertyChanged(nameof(SpacerText));
            }
        }
    }

    public int Depth
    {
        get => _depth;
        private set
        {
            if (SetProperty(ref _depth, value))
            {
                OnPropertyChanged(nameof(ConnectorWidth));
                OnPropertyChanged(nameof(ArrowMargin));
                OnPropertyChanged(nameof(ConnectorText));
                OnPropertyChanged(nameof(SpacerText));
            }
        }
    }

    public bool IsLast
    {
        get => _isLast;
        private set
        {
            if (SetProperty(ref _isLast, value))
            {
                OnPropertyChanged(nameof(ConnectorText));
                OnPropertyChanged(nameof(SpacerText));
            }
        }
    }

    public IReadOnlyList<bool> AncestorContinues
    {
        get => _ancestorContinues;
        private set
        {
            if (SetProperty(ref _ancestorContinues, value))
            {
                OnPropertyChanged(nameof(ConnectorText));
                OnPropertyChanged(nameof(SpacerText));
            }
        }
    }

    public void SetTreeState(int depth, bool isLast, IReadOnlyList<bool> ancestorContinues)
    {
        Depth = depth;
        IsLast = isLast;
        AncestorContinues = ancestorContinues;
    }

    public void UpdateVersionAndLoc(string versionLabel, long loc)
    {
        if (SetProperty(ref _versionLabel, versionLabel, nameof(VersionLabel)))
        {
            OnPropertyChanged(nameof(NodeRoleLabel));
            OnPropertyChanged(nameof(NodeBadgeText));
        }

        if (SetProperty(ref _loc, loc, nameof(Loc)))
        {
            OnPropertyChanged(nameof(LocLabel));
            OnPropertyChanged(nameof(LocMetricLabel));
            OnPropertyChanged(nameof(DirectoryStatsLabel));
            OnPropertyChanged(nameof(NodeRoleLabel));
            OnPropertyChanged(nameof(NodeBadgeText));
        }
    }

    public void SetExternalState(bool isExternal, bool canIncludeExternal = false)
    {
        if (SetProperty(ref _isExternal, isExternal, nameof(IsExternal)))
        {
            OnPropertyChanged(nameof(IsRegularFolder));
            OnPropertyChanged(nameof(DisplayFileCount));
            OnPropertyChanged(nameof(FileCountLabel));
            OnPropertyChanged(nameof(LocMetricLabel));
            OnPropertyChanged(nameof(DirectoryStatsLabel));
            OnPropertyChanged(nameof(NodeRoleLabel));
            OnPropertyChanged(nameof(NodeBadgeText));
        }

        if (SetProperty(ref _canIncludeExternal, canIncludeExternal, nameof(CanIncludeExternal)))
        {
            OnPropertyChanged(nameof(NodeRoleLabel));
            OnPropertyChanged(nameof(NodeBadgeText));
        }
    }

    public long RecalculateDirectoryLoc()
    {
        if (!IsFolder)
        {
            return Loc;
        }

        var activeChildren = Children.Where(child => !child.IsExternal).ToArray();
        var total = activeChildren.Sum(child => child.RecalculateDirectoryLoc());
        var fileTotal = activeChildren.Sum(child => child.FileCount);
        var diskFileTotal = _directDiskFileCount + activeChildren
            .Where(child => child.IsFolder)
            .Sum(child => child.DiskFileCount);
        if (SetProperty(ref _loc, total, nameof(Loc)))
        {
            OnPropertyChanged(nameof(LocLabel));
            OnPropertyChanged(nameof(LocMetricLabel));
            OnPropertyChanged(nameof(DirectoryStatsLabel));
            OnPropertyChanged(nameof(NodeRoleLabel));
            OnPropertyChanged(nameof(NodeBadgeText));
        }

        if (SetProperty(ref _fileCount, fileTotal, nameof(FileCount)))
        {
            OnPropertyChanged(nameof(DisplayFileCount));
            OnPropertyChanged(nameof(FileCountLabel));
            OnPropertyChanged(nameof(DirectoryStatsLabel));
        }

        if (SetProperty(ref _diskFileCount, diskFileTotal, nameof(DiskFileCount)))
        {
            OnPropertyChanged(nameof(DisplayFileCount));
            OnPropertyChanged(nameof(FileCountLabel));
            OnPropertyChanged(nameof(DirectoryStatsLabel));
        }

        return total;
    }

    private static IEnumerable<ProjectNodeViewModel> SortChildren(IEnumerable<ProjectNodeViewModel> nodes)
    {
        return nodes
            .OrderBy(node => node.IsFolder)
            .ThenBy(node => node.Name, StringComparer.OrdinalIgnoreCase);
    }

    private string BuildConnectorText()
    {
        if (Depth <= 0)
        {
            return HasChildren ? (IsExpanded ? "v---" : ">---") : "----";
        }

        var text = BuildAncestorPrefix();
        text.Append("|---");

        return text.ToString();
    }

    private string BuildSpacerText()
    {
        if (Depth <= 0)
        {
            return "|";
        }

        var text = BuildAncestorPrefix();
        text.Append("|");
        return text.ToString();
    }

    private System.Text.StringBuilder BuildAncestorPrefix()
    {
        var text = new System.Text.StringBuilder();
        for (var i = 0; i < Depth - 1; i++)
        {
            text.Append(i < AncestorContinues.Count && AncestorContinues[i] ? "|   " : "    ");
        }

        return text;
    }
}
