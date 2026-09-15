// CC-DESC: Searchable, virtualized project source inventory and cancellable scan state.
using System.Windows.Input;
using ContextControl.Workbench.Services;

namespace ContextControl.Workbench.ViewModels;

public sealed partial class WorkbenchViewModel
{
    private CancellationTokenSource? _projectScanCancellation;
    private CancellationTokenSource? _projectScanFilterCancellation;
    private IReadOnlyList<ProjectScanFile> _projectScanFiles = [];
    private IReadOnlyList<ProjectScanFile> _projectScanFilteredFiles = [];
    private IReadOnlyList<string> _projectScanNotices = [];
    private string _projectScanSearchText = "";
    private string _projectScanFilter = "All code";
    private string _projectScanProgressText = "";
    private string _projectScanCoverage = "Run Scan to discover this project's code.";
    private ProjectScanFile? _selectedProjectScanFile;
    private ICommand? _cancelProjectScanCommand;

    public IReadOnlyList<ProjectScanFile> ProjectScanFiles => _projectScanFiles;
    public IReadOnlyList<ProjectScanFile> ProjectScanFilteredFiles => _projectScanFilteredFiles;
    public IReadOnlyList<string> ProjectScanNotices => _projectScanNotices;
    public bool HasProjectScanNotices => _projectScanNotices.Count > 0;
    public IReadOnlyList<string> ProjectScanFilters { get; } = ["All code", "Hidden code", "Included files", "All files"];
    public string ProjectScanCoverage => _projectScanCoverage;
    public string ProjectScanInventoryCount => $"{_projectScanFilteredFiles.Count:N0} shown / {_projectScanFiles.Count:N0} discovered files";
    public bool HasNoProjectScanMatches => !IsProjectScanRunning && _projectScanFilteredFiles.Count == 0;
    public string ProjectScanProgressText { get => _projectScanProgressText; private set => SetProperty(ref _projectScanProgressText, value); }
    public string ProjectScanSearchText
    {
        get => _projectScanSearchText;
        set { if (SetProperty(ref _projectScanSearchText, value ?? "")) RefreshProjectScanFilter(); }
    }
    public string ProjectScanFilter
    {
        get => _projectScanFilter;
        set { if (SetProperty(ref _projectScanFilter, value ?? "All code")) RefreshProjectScanFilter(); }
    }
    public ProjectScanFile? SelectedProjectScanFile
    {
        get => _selectedProjectScanFile;
        set => SetProperty(ref _selectedProjectScanFile, value);
    }
    public bool CanCancelProjectScan => _projectScanCancellation is not null && IsProjectScanRunning;
    public ICommand CancelProjectScanCommand => _cancelProjectScanCommand ??= new RelayCommand<object>(_ => _projectScanCancellation?.Cancel());

    private void BeginProjectScan()
    {
        _projectScanCancellation?.Dispose();
        _projectScanCancellation = new CancellationTokenSource();
        IsProjectScanRunning = true;
        ProjectScanProgressText = "Discovering project files…";
        OnPropertyChanged(nameof(CanCancelProjectScan));
        OnPropertyChanged(nameof(HasNoProjectScanMatches));
    }

    private void EndProjectScan()
    {
        _projectScanCancellation?.Dispose();
        _projectScanCancellation = null;
        IsProjectScanRunning = false;
        ProjectScanProgressText = "";
        OnPropertyChanged(nameof(CanCancelProjectScan));
        OnPropertyChanged(nameof(HasNoProjectScanMatches));
    }

    private void SetProjectScanInventory(ProjectStackScanResult? result)
    {
        _projectScanFiles = result?.Files ?? [];
        _projectScanNotices = result?.Notices ?? [];
        var code = _projectScanFiles.Count(file => file.IsCode);
        var missing = _projectScanFiles.Count(file => file.IsCode && !file.IsTracked);
        _projectScanCoverage = result is null ? "Run Scan to discover this project's code."
            : $"{code:N0} code files · {missing:N0} excluded by current rules · {(result.IsComplete ? "Scan complete" : "Partial scan — see notices")}";
        SelectedProjectScanFile = null;
        OnPropertyChanged(nameof(ProjectScanFiles));
        OnPropertyChanged(nameof(ProjectScanCoverage));
        OnPropertyChanged(nameof(ProjectScanNotices));
        OnPropertyChanged(nameof(HasProjectScanNotices));
        RefreshProjectScanFilter();
    }

    private async void RefreshProjectScanFilter()
    {
        _projectScanFilterCancellation?.Cancel();
        _projectScanFilterCancellation?.Dispose();
        var cancellation = _projectScanFilterCancellation = new CancellationTokenSource();
        var token = cancellation.Token;
        var files = _projectScanFiles;
        var search = ProjectScanSearchText.Trim();
        var filter = ProjectScanFilter;
        try
        {
            if (files.Count > 1000) await Task.Delay(120, token);
            var result = await Task.Run(() => files.Where(file =>
            {
                token.ThrowIfCancellationRequested();
                var inFilter = filter switch
                {
                    "All code" => file.IsCode,
                    "Hidden code" => file.IsCode && !file.IsTracked,
                    "Included files" => file.IsTracked,
                    _ => true
                };
                return inFilter && (search.Length == 0 || file.RelativePath.Contains(search, StringComparison.OrdinalIgnoreCase)
                    || file.Language.Contains(search, StringComparison.OrdinalIgnoreCase));
            }).ToArray(), token);
            if (token.IsCancellationRequested) return;
            _projectScanFilteredFiles = result;
            OnPropertyChanged(nameof(ProjectScanFilteredFiles));
            OnPropertyChanged(nameof(ProjectScanInventoryCount));
            OnPropertyChanged(nameof(HasNoProjectScanMatches));
        }
        catch (OperationCanceledException) { }
    }

    public void OpenSelectedScannerFile()
    {
        if (SelectedProjectScanFile is not { } file || CurrentProject is null) return;
        var fullPath = Path.GetFullPath(Path.Combine(CurrentProject.ProjectRoot, file.RelativePath));
        var root = Path.GetFullPath(CurrentProject.ProjectRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(fullPath)) return;
        var node = new ProjectNodeViewModel(file.Name, file.RelativePath, false, "file");
        OpenDocument(node);
    }
}
