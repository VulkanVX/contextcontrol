using ContextControl.Workbench.Services;

sealed class DirExportQualityOptions
{
    public string Name { get; init; } = "";
    public int RelevantFileCount { get; init; }
    public int MaxL0Lines { get; init; } = 120;
    public bool AllowDocsHeavy { get; init; }
    public bool AllowToolsHeavy { get; init; }
    public bool StrictRoleSpecificity { get; init; }
    public bool ExpectAsset { get; init; }
    public bool BuildFocused { get; init; }

    public DirExportQualityOptions WithDefaults(string name, int relevantFileCount)
    {
        return new DirExportQualityOptions
        {
            Name = string.IsNullOrWhiteSpace(Name) ? name : Name,
            RelevantFileCount = RelevantFileCount > 0 ? RelevantFileCount : relevantFileCount,
            MaxL0Lines = MaxL0Lines,
            AllowDocsHeavy = AllowDocsHeavy,
            AllowToolsHeavy = AllowToolsHeavy,
            StrictRoleSpecificity = StrictRoleSpecificity,
            ExpectAsset = ExpectAsset,
            BuildFocused = BuildFocused
        };
    }
}

sealed class DirExportQualityReport
{
    public string Name { get; init; } = "";
    public ContextDirManifest Manifest { get; init; } = ContextDirManifest.Empty;
    public IReadOnlyList<string> Failures { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public int LineCount { get; init; }
    public int MaxL0Lines { get; init; }
    public int FileCount { get; init; }
    public int RootCount { get; init; }
    public int FamilyCount { get; init; }
    public int NonDocFileCount { get; init; }
    public int CoveredFileCount { get; init; }
    public int CoveredFamilyCount { get; init; }
    public int BuildCount { get; init; }
    public int BuildMax { get; init; }
    public int BuildPreferredMax { get; init; }
    public int UnrootedBuildConfigCount { get; init; }
    public int RepoRootFileCount { get; init; }
    public int RepoRootCoveredCount { get; init; }
    public int RepresentativeCount { get; init; }
    public int RepresentativeMin { get; init; }
    public int LargestRootShare { get; init; }
    public string LargestRootName { get; init; } = "";
    public int MaxRootShare { get; init; }
    public int GenericRoleCount { get; init; }
    public int GenericRoleMax { get; init; }
    public string RepoClass { get; init; } = "";
}
