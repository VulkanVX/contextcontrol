// CC-DESC: Holds project-stack scanner state.

namespace ContextControl.Workbench.Services;

public static partial class ProjectStackScanner
{
    private sealed class ScanState(string projectRoot, ProjectFileRules rules)
    {
        public string ProjectRoot { get; } = projectRoot;
        public ProjectFileRules Rules { get; } = rules;
        public int DirectoriesVisited { get; set; }
        public int DirectoriesExcluded { get; set; }
        public int DirectoriesSkippedByRules { get; set; }
        public int FilesSeen { get; set; }
        public int FilesSkippedByRules { get; set; }
        public int VisibleFiles { get; set; }
        public int TrackedFiles { get; set; }
        public int UnsupportedVisibleFiles { get; set; }
        public int TextSignalFilesScanned { get; set; }
        public CancellationToken CancellationToken { get; init; }
        public IProgress<ProjectScanProgress>? Progress { get; init; }
        public System.Diagnostics.Stopwatch ProgressClock { get; } = System.Diagnostics.Stopwatch.StartNew();
        public List<ProjectScanFile> Files { get; } = [];
        public List<string> Notices { get; } = [];
        public bool IsComplete { get; set; } = true;
        public Dictionary<string, int> ExtensionCounts { get; } = new(NameComparer);
        public Dictionary<string, int> UnsupportedExtensionCounts { get; } = new(NameComparer);
        public Dictionary<string, int> SkippedExtensionCounts { get; } = new(NameComparer);
        public Dictionary<string, int> LanguageCounts { get; } = new(NameComparer);
        public Dictionary<string, SortedSet<string>> StackReasons { get; } = new(NameComparer);
        public Dictionary<string, SortedSet<string>> UseReasons { get; } = new(NameComparer);
        public SortedSet<string> AutoSkippedDirectoryRules { get; } = new(NameComparer);
        public List<string> ManifestSamples { get; } = [];
        public List<string> SkippedDirectorySamples { get; } = [];
        public List<string> SkippedFileSamples { get; } = [];
    }
}
