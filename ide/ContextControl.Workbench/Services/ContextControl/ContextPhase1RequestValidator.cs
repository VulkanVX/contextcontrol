// CC-DESC: Validates Phase 1 DIR/FIND/EXPAND/source request snippets before execution.

using System.Text.RegularExpressions;

namespace ContextControl.Workbench.Services;

public static partial class ContextPhase1RequestValidator
{
    private const int MaxFindLength = 120;
    private const int MaxFuncLength = 160;

    public static ContextPhase1ValidationResult Validate(
        ContextRequestLineParseResult parse,
        ContextDirManifest manifest,
        string projectRoot = "",
        IReadOnlyCollection<string>? trustedFindRequestPaths = null)
    {
        var requestLines = parse.RequestLines.ToArray();
        var trustedFindPathSet = BuildTrustedFindPathSet(trustedFindRequestPaths);
        if (parse.ExtraLines.Count > 0)
        {
            return Invalid(requestLines, $"Phase 1 contains non-request text: {parse.ExtraLines[0]}", CandidateList(manifest, parse.ExtraLines[0]));
        }

        if (requestLines.Length == 0)
        {
            return Invalid(requestLines, "Phase 1 needs exact file/FUNCTION/FUNC lines, exactly one FIND line, or exactly one EXPAND line.", CandidateList(manifest, ""));
        }

        if (!parse.EndsWithEnd)
        {
            return Invalid(requestLines, "Phase 1 request must end with END.", CandidateList(manifest, requestLines.LastOrDefault() ?? ""));
        }

        var findLines = requestLines.Where(line => line.StartsWith("FIND:", StringComparison.OrdinalIgnoreCase)).ToArray();
        var expandLines = requestLines.Where(line => line.StartsWith("EXPAND:", StringComparison.OrdinalIgnoreCase)).ToArray();
        var sourceLines = requestLines.Except(findLines, StringComparer.OrdinalIgnoreCase).Except(expandLines, StringComparer.OrdinalIgnoreCase).ToArray();

        if (findLines.Length > 0)
        {
            if (findLines.Length != 1 || expandLines.Length > 0 || sourceLines.Length > 0)
            {
                return Invalid(requestLines, "FIND must be exactly one line followed by END; do not mix FIND with source or EXPAND lines.", []);
            }

            var normalizedFindLines = new List<string>();
            foreach (var findLine in findLines)
            {
                var text = findLine["FIND:".Length..].Trim();
                if (!IsPlainSearchText(text, MaxFindLength))
                {
                    return Invalid(requestLines, "Each FIND text must be plain text under the safe length limit.", []);
                }

                var normalizedFindLine = $"FIND: {text}";
                if (!normalizedFindLines.Contains(normalizedFindLine, StringComparer.OrdinalIgnoreCase))
                {
                    normalizedFindLines.Add(normalizedFindLine);
                }
            }

            return Valid("find", normalizedFindLines);
        }

        if (expandLines.Length > 0)
        {
            if (expandLines.Length != 1 || sourceLines.Length > 0)
            {
                return Invalid(requestLines, "EXPAND must be exactly one line followed by END; do not mix EXPAND with source or FIND lines.", []);
            }

            var scope = NormalizeDirectory(expandLines[0]["EXPAND:".Length..].Trim());
            if (!ContextDirManifestParser.HasRootOrScope(manifest, scope))
            {
                return Invalid(requestLines, $"EXPAND path is not visible in the current DIR manifest: {scope}", CandidateList(manifest, scope));
            }

            return Valid("expand", [$"EXPAND: {scope}"]);
        }

        foreach (var line in sourceLines)
        {
            var error = ValidateSourceLine(line, manifest, projectRoot, trustedFindPathSet);
            if (!string.IsNullOrWhiteSpace(error))
            {
                return Invalid(requestLines, error, CandidateList(manifest, ExtractPathCandidate(line)));
            }
        }

        return Valid("source", requestLines);
    }

    private static string ValidateSourceLine(
        string line,
        ContextDirManifest manifest,
        string projectRoot,
        HashSet<string> trustedFindPathSet)
    {
        if (line.StartsWith("SYMBOL:", StringComparison.OrdinalIgnoreCase))
        {
            return "SYMBOL is disabled; use exact paths, FUNCTION, FUNC, FIND, or EXPAND.";
        }

        if (line.StartsWith("FUNC:", StringComparison.OrdinalIgnoreCase)
            || line.StartsWith("FUNCTION:", StringComparison.OrdinalIgnoreCase))
        {
            var separator = line.IndexOf(':', StringComparison.Ordinal);
            var text = separator >= 0 ? line[(separator + 1)..].Trim() : "";
            return IsPlainSearchText(text, MaxFuncLength)
                ? ""
                : "FUNC text must be a plain symbol/search token under the safe length limit.";
        }

        if (line.StartsWith("FUNCTION ", StringComparison.OrdinalIgnoreCase))
        {
            var functionPath = ExtractFunctionPath(line);
            if (string.IsNullOrWhiteSpace(functionPath))
            {
                return $"Malformed FUNCTION request: {line}";
            }

            if (functionPath.Contains('*', StringComparison.Ordinal) || functionPath.Contains('?', StringComparison.Ordinal))
            {
                return ContextDirManifestParser.HasFamily(manifest, functionPath)
                    ? ""
                    : $"Wildcard FUNCTION path must match a visible FAMILY path: {functionPath}";
            }

            return PathIsVisibleFile(functionPath, manifest, projectRoot, trustedFindPathSet)
                ? ""
                : $"FUNCTION path is not visible in the current DIR manifest: {functionPath}";
        }

        if (LooksLikeDirectory(line, projectRoot))
        {
            return $"Directories are not source request lines. Use EXPAND instead: {NormalizeDirectory(line)}";
        }

        return PathIsVisibleFile(line, manifest, projectRoot, trustedFindPathSet)
            ? ""
            : $"File path is not visible in the current DIR manifest: {line}";
    }

    private static bool PathIsVisibleFile(
        string path,
        ContextDirManifest manifest,
        string projectRoot,
        HashSet<string> trustedFindPathSet)
    {
        if (ContextDirManifestParser.HasFile(manifest, path))
        {
            return true;
        }

        if (PathIsTrustedFindRequestFile(path, trustedFindPathSet, projectRoot))
        {
            return true;
        }

        if (PathIsExistingFileUnderVisibleRoot(path, manifest, projectRoot))
        {
            return true;
        }

        if (manifest.Files.Count > 0)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(projectRoot) || !Directory.Exists(projectRoot))
        {
            return true;
        }

        var clean = ContextDirManifestParser.NormalizePath(path);
        if (Path.IsPathRooted(clean))
        {
            return false;
        }

        var fullPath = Path.GetFullPath(Path.Combine(projectRoot, clean.Replace('/', Path.DirectorySeparatorChar)));
        return fullPath.StartsWith(Path.GetFullPath(projectRoot), StringComparison.OrdinalIgnoreCase) && File.Exists(fullPath);
    }

    private static bool PathIsExistingFileUnderVisibleRoot(string path, ContextDirManifest manifest, string projectRoot)
    {
        if (string.IsNullOrWhiteSpace(projectRoot)
            || !Directory.Exists(projectRoot))
        {
            return false;
        }

        var clean = ContextDirManifestParser.NormalizePath(path);
        if (string.IsNullOrWhiteSpace(clean)
            || clean.Contains('*', StringComparison.Ordinal)
            || clean.Contains('?', StringComparison.Ordinal)
            || Path.IsPathRooted(clean)
            || !PathIsUnderVisibleRootOrScope(clean, manifest))
        {
            return false;
        }

        var root = Path.GetFullPath(projectRoot);
        var fullPath = Path.GetFullPath(Path.Combine(root, clean.Replace('/', Path.DirectorySeparatorChar)));
        var rootWithSeparator = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase)
            && File.Exists(fullPath);
    }

    private static bool PathIsUnderVisibleRootOrScope(string cleanPath, ContextDirManifest manifest)
    {
        foreach (var root in manifest.Roots)
        {
            var rootPath = ContextDirManifestParser.NormalizePath(root.Path);
            if (rootPath.Length == 0)
            {
                continue;
            }

            rootPath = rootPath.EndsWith("/", StringComparison.Ordinal) ? rootPath : rootPath + "/";
            if (cleanPath.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        var scope = ContextDirManifestParser.NormalizePath(manifest.Scope);
        if (scope.Length == 0)
        {
            return false;
        }

        scope = scope.EndsWith("/", StringComparison.Ordinal) ? scope : scope + "/";
        return cleanPath.StartsWith(scope, StringComparison.OrdinalIgnoreCase);
    }

    private static HashSet<string> BuildTrustedFindPathSet(IReadOnlyCollection<string>? trustedFindRequestPaths)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in trustedFindRequestPaths ?? [])
        {
            var clean = ContextDirManifestParser.NormalizePath(path);
            if (!string.IsNullOrWhiteSpace(clean))
            {
                result.Add(clean);
            }
        }

        return result;
    }

    private static bool PathIsTrustedFindRequestFile(string path, HashSet<string> trustedFindPathSet, string projectRoot)
    {
        if (trustedFindPathSet.Count == 0
            || string.IsNullOrWhiteSpace(projectRoot)
            || !Directory.Exists(projectRoot))
        {
            return false;
        }

        var clean = ContextDirManifestParser.NormalizePath(path);
        if (string.IsNullOrWhiteSpace(clean)
            || clean.Contains('*', StringComparison.Ordinal)
            || clean.Contains('?', StringComparison.Ordinal)
            || Path.IsPathRooted(clean)
            || !trustedFindPathSet.Contains(clean))
        {
            return false;
        }

        var root = Path.GetFullPath(projectRoot);
        var fullPath = Path.GetFullPath(Path.Combine(root, clean.Replace('/', Path.DirectorySeparatorChar)));
        var rootWithSeparator = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase)
            && File.Exists(fullPath);
    }

    private static bool LooksLikeDirectory(string path, string projectRoot)
    {
        var clean = ContextDirManifestParser.NormalizePath(path);
        if (clean.EndsWith("/", StringComparison.Ordinal))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(projectRoot) || !Directory.Exists(projectRoot) || Path.IsPathRooted(clean))
        {
            return false;
        }

        try
        {
            var fullPath = Path.GetFullPath(Path.Combine(projectRoot, clean.Replace('/', Path.DirectorySeparatorChar)));
            return fullPath.StartsWith(Path.GetFullPath(projectRoot), StringComparison.OrdinalIgnoreCase)
                && Directory.Exists(fullPath);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsPlainSearchText(string text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > maxLength)
        {
            return false;
        }

        return !text.Contains('\n', StringComparison.Ordinal)
            && !text.Contains('\r', StringComparison.Ordinal)
            && !text.Contains("CC-REPLACE", StringComparison.OrdinalIgnoreCase)
            && !Path.IsPathRooted(text)
            && PlainSearchRegex().IsMatch(text);
    }

    private static string ExtractFunctionPath(string line)
    {
        var body = line["FUNCTION ".Length..].Trim();
        var separator = body.IndexOf(" :: ", StringComparison.Ordinal);
        return separator > 0
            ? ContextDirManifestParser.NormalizePath(body[..separator].Trim())
            : "";
    }

    private static string ExtractPathCandidate(string line)
    {
        if (line.StartsWith("FUNCTION ", StringComparison.OrdinalIgnoreCase))
        {
            return ExtractFunctionPath(line);
        }

        if (line.StartsWith("EXPAND:", StringComparison.OrdinalIgnoreCase))
        {
            return line["EXPAND:".Length..].Trim();
        }

        return line;
    }

    private static string NormalizeDirectory(string path)
    {
        var clean = ContextDirManifestParser.NormalizePath(path);
        return clean.Length > 0 && !clean.EndsWith("/", StringComparison.Ordinal) ? clean + "/" : clean;
    }

    private static IReadOnlyList<string> CandidateList(ContextDirManifest manifest, string query)
    {
        return ContextDirManifestParser.NearestPaths(manifest, query, 6);
    }

    private static ContextPhase1ValidationResult Valid(string kind, IReadOnlyList<string> requestLines)
    {
        return new ContextPhase1ValidationResult(true, kind, requestLines, "", []);
    }

    private static ContextPhase1ValidationResult Invalid(IReadOnlyList<string> requestLines, string error, IReadOnlyList<string> candidates)
    {
        return new ContextPhase1ValidationResult(false, "invalid", requestLines, error, candidates);
    }

    [GeneratedRegex("^[A-Za-z0-9_./:#*?+\\- <>\"']+$")]
    private static partial Regex PlainSearchRegex();
}
