// CC-DESC: Defines structured semantic index and resolver result records for CC file routing.

namespace ContextControl.Workbench.Services;

public sealed record ContextSemanticMapBuildResult(
    string SemanticMapText,
    ContextSemanticIndex Index);

public sealed record ContextSemanticIndex(
    string ProjectRoot,
    IReadOnlyList<ContextFileSignal> Files)
{
    public static ContextSemanticIndex Empty(string projectRoot = "") => new(projectRoot ?? "", []);

    public bool IsEmpty => Files.Count == 0;
}

public sealed record ContextFileSignal(
    string Path,
    string Extension,
    string Role,
    int Score,
    IReadOnlyList<string> PathTokens,
    IReadOnlyList<string> ContentTokens,
    IReadOnlyList<string> Markers,
    IReadOnlyList<string> Members,
    IReadOnlyList<string> Strings,
    IReadOnlyList<string> Bindings);

public sealed record ContextFileResolveCandidate(
    string RequestLine,
    int Score,
    IReadOnlyList<string> Reasons,
    bool IsExactPath);

public sealed record ContextFileResolveResult(
    IReadOnlyList<string> RequestLines,
    string Confidence,
    string FallbackKind,
    IReadOnlyList<ContextFileResolveCandidate> Candidates,
    IReadOnlyList<string> Reasons)
{
    public bool HasRequestLines => RequestLines.Count > 0;

    public bool UsesFindTerms => RequestLines.Any(line => line.StartsWith("FIND:", StringComparison.OrdinalIgnoreCase));

    public string RequestText => HasRequestLines
        ? string.Join(Environment.NewLine, RequestLines.Append("END"))
        : "";
}

public sealed record ContextDirManifest(
    bool IsV2,
    string Lod,
    string Scope,
    IReadOnlyList<ContextDirManifestFile> Files,
    IReadOnlyList<ContextDirManifestRoot> Roots,
    IReadOnlyList<ContextDirManifestFamily> Families)
{
    public static ContextDirManifest Empty { get; } = new(false, "", "", [], [], []);
}

public sealed record ContextDirManifestFile(
    string Path,
    string Tier,
    string Kind,
    string Role,
    string Exports);

public sealed record ContextDirManifestRoot(
    string Path,
    string Role,
    int Files);

public sealed record ContextDirManifestFamily(
    string Path,
    string Role,
    string Exports);

public sealed record ContextRequestLineParseResult(
    IReadOnlyList<string> RequestLines,
    IReadOnlyList<string> ExtraLines,
    bool EndsWithEnd);

public sealed record ContextPhase1ValidationResult(
    bool IsValid,
    string Kind,
    IReadOnlyList<string> RequestLines,
    string Error,
    IReadOnlyList<string> Candidates)
{
    public string ExpandScope => Kind.Equals("expand", StringComparison.OrdinalIgnoreCase) && RequestLines.Count == 1
        ? RequestLines[0]["EXPAND:".Length..].Trim()
        : "";
}
