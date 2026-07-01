// CC-DESC: Parses CC-DIR-MANIFEST-V2 records and legacy visual DIR trees.

using System.Text.RegularExpressions;

namespace ContextControl.Workbench.Services;

public static partial class ContextDirManifestParser
{
    public static ContextDirManifest Parse(string? text)
    {
        var value = text ?? "";
        var isV2 = value.Contains("CC-DIR-MANIFEST-V2", StringComparison.OrdinalIgnoreCase);
        var lod = "";
        var scope = "";
        var files = new List<ContextDirManifestFile>();
        var roots = new List<ContextDirManifestRoot>();
        var families = new List<ContextDirManifestFamily>();

        foreach (var rawLine in NormalizeLines(value))
        {
            var line = rawLine.Trim();
            if (line.StartsWith("LOD:", StringComparison.OrdinalIgnoreCase))
            {
                lod = line["LOD:".Length..].Trim();
                continue;
            }

            if (line.StartsWith("SCOPE:", StringComparison.OrdinalIgnoreCase))
            {
                scope = NormalizePath(line["SCOPE:".Length..].Trim());
                if (scope.Length > 0 && !scope.EndsWith("/", StringComparison.Ordinal))
                {
                    scope += "/";
                }

                continue;
            }

            if (line.StartsWith("FILE ", StringComparison.OrdinalIgnoreCase))
            {
                var fields = ParseFields(line);
                var path = NormalizePath(ReadField(fields, "path"));
                if (!string.IsNullOrWhiteSpace(path))
                {
                    files.Add(new ContextDirManifestFile(
                        path,
                        ReadField(fields, "tier"),
                        ReadField(fields, "kind"),
                        ReadField(fields, "role"),
                        ReadField(fields, "exports")));
                }

                continue;
            }

            if (line.StartsWith("ROOT ", StringComparison.OrdinalIgnoreCase))
            {
                var fields = ParseFields(line);
                var path = NormalizePath(ReadField(fields, "path"));
                if (!string.IsNullOrWhiteSpace(path))
                {
                    if (!path.EndsWith("/", StringComparison.Ordinal))
                    {
                        path += "/";
                    }

                    roots.Add(new ContextDirManifestRoot(
                        path,
                        ReadField(fields, "role"),
                        int.TryParse(ReadField(fields, "files"), out var fileCount) ? fileCount : 0));
                }

                continue;
            }

            if (line.StartsWith("FAMILY ", StringComparison.OrdinalIgnoreCase))
            {
                var fields = ParseFields(line);
                var path = NormalizePath(ReadField(fields, "path"));
                if (!string.IsNullOrWhiteSpace(path))
                {
                    families.Add(new ContextDirManifestFamily(
                        path,
                        ReadField(fields, "role"),
                        ReadField(fields, "exports")));
                }
            }
        }

        if (!isV2 && files.Count == 0)
        {
            files.AddRange(ParseLegacyTreeFiles(value));
        }

        return new ContextDirManifest(
            isV2,
            lod,
            scope,
            files.DistinctBy(file => file.Path, StringComparer.OrdinalIgnoreCase).ToArray(),
            roots.DistinctBy(root => root.Path, StringComparer.OrdinalIgnoreCase).ToArray(),
            families.DistinctBy(family => family.Path, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    public static bool HasFile(ContextDirManifest manifest, string path)
    {
        var clean = NormalizePath(path);
        return manifest.Files.Any(file => file.Path.Equals(clean, StringComparison.OrdinalIgnoreCase));
    }

    public static bool HasRootOrScope(ContextDirManifest manifest, string path)
    {
        var clean = NormalizePath(path);
        if (clean.Length > 0 && !clean.EndsWith("/", StringComparison.Ordinal))
        {
            clean += "/";
        }

        return manifest.Roots.Any(root => root.Path.Equals(clean, StringComparison.OrdinalIgnoreCase))
            || (!string.IsNullOrWhiteSpace(manifest.Scope) && manifest.Scope.Equals(clean, StringComparison.OrdinalIgnoreCase));
    }

    public static bool HasFamily(ContextDirManifest manifest, string path)
    {
        var clean = NormalizePath(path);
        return manifest.Families.Any(family => family.Path.Equals(clean, StringComparison.OrdinalIgnoreCase));
    }

    public static IReadOnlyList<string> NearestPaths(ContextDirManifest manifest, string path, int count = 6)
    {
        var clean = NormalizePath(path);
        var leaf = Path.GetFileName(clean);
        var candidates = manifest.Files.Select(file => file.Path)
            .Concat(manifest.Roots.Select(root => root.Path))
            .Concat(manifest.Families.Select(family => family.Path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(candidate => new
            {
                Path = candidate,
                Score = ScoreCandidate(clean, leaf, candidate)
            })
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Path, StringComparer.OrdinalIgnoreCase)
            .Take(count)
            .Select(candidate => candidate.Path)
            .ToArray();

        return candidates;
    }

    internal static string NormalizePath(string path)
    {
        var clean = (path ?? "").Trim().Trim('"', '\'', '`').Replace('\\', '/');
        while (clean.StartsWith("./", StringComparison.Ordinal))
        {
            clean = clean[2..];
        }

        return clean.Trim('/');
    }

    private static int ScoreCandidate(string query, string leaf, string candidate)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return 0;
        }

        var score = 0;
        if (candidate.Equals(query, StringComparison.OrdinalIgnoreCase))
        {
            score += 1000;
        }

        if (!string.IsNullOrWhiteSpace(leaf)
            && Path.GetFileName(candidate).Equals(leaf, StringComparison.OrdinalIgnoreCase))
        {
            score += 200;
        }

        var queryParts = query.Split('/', StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in queryParts)
        {
            if (candidate.Contains(part, StringComparison.OrdinalIgnoreCase))
            {
                score += 20;
            }
        }

        return score;
    }

    private static IEnumerable<ContextDirManifestFile> ParseLegacyTreeFiles(string text)
    {
        var stack = new Dictionary<int, string>();
        foreach (var rawLine in NormalizeLines(text))
        {
            var line = rawLine.TrimEnd();
            var match = TreeLineRegex().Match(line);
            if (!match.Success)
            {
                continue;
            }

            var depth = match.Groups["prefix"].Value.Length / 4;
            var name = match.Groups["name"].Value.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            if (name.EndsWith("/", StringComparison.Ordinal))
            {
                stack[depth] = name.TrimEnd('/');
                foreach (var key in stack.Keys.Where(key => key > depth).ToArray())
                {
                    stack.Remove(key);
                }

                continue;
            }

            var parts = new List<string>();
            for (var index = 0; index < depth; index++)
            {
                if (stack.TryGetValue(index, out var part))
                {
                    parts.Add(part);
                }
            }

            parts.Add(name);
            var path = NormalizePath(string.Join("/", parts));
            yield return new ContextDirManifestFile(path, "legacy", "", "", "");
        }
    }

    private static IReadOnlyDictionary<string, string> ParseFields(string line)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in FieldRegex().Matches(line))
        {
            var key = match.Groups["key"].Value;
            var value = match.Groups["quoted"].Success
                ? match.Groups["quoted"].Value
                : match.Groups["bare"].Value;
            fields[key] = value;
        }

        return fields;
    }

    private static string ReadField(IReadOnlyDictionary<string, string> fields, string key)
    {
        return fields.TryGetValue(key, out var value) ? value.Trim() : "";
    }

    private static IEnumerable<string> NormalizeLines(string text)
    {
        return (text ?? "")
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');
    }

    [GeneratedRegex("(?<key>[A-Za-z_][A-Za-z0-9_]*)=(?:\"(?<quoted>[^\"]*)\"|(?<bare>\\S+))")]
    private static partial Regex FieldRegex();

    [GeneratedRegex("^(?<prefix>(?:[│ ]{4})*)(?:├──|└──)\\s+(?<name>.+)$")]
    private static partial Regex TreeLineRegex();
}
