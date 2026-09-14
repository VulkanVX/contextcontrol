// CC-DESC: Describes one captured external file-save version transition.

namespace ContextControl.Workbench.Services;

public sealed record ExternalFileChange(
    string ProjectRoot,
    string RelativePath,
    string FileName,
    string SnapshotPath,
    string PreviousSnapshotPath,
    int VersionBefore,
    int VersionAfter,
    string Timestamp,
    string DisplayDate,
    int AddedLines,
    int RemovedLines,
    long Loc);
