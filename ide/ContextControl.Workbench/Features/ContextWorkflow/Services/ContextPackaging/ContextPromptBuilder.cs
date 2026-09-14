// CC-DESC: Builds Context Control prompt payloads and extracts patch blocks.

using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ContextControl.Workbench.Services;

public sealed record PatchPlanSummary(
    int EffectiveCount,
    int DuplicateCount,
    int FileCount,
    int DirectoryCount,
    int CreatedCount,
    int ChangedCount,
    int RemovedCount,
    int Added,
    int Removed,
    IReadOnlyList<PatchPlanActionSummary> Actions,
    string Error = "")
{
    private bool HasProvidedBucketCounts => CreatedCount > 0 || ChangedCount > 0 || RemovedCount > 0;

    public int DisplayCreatedCount => HasProvidedBucketCounts ? CreatedCount : Actions.Count(action => action.BucketLabel.Equals("created", StringComparison.OrdinalIgnoreCase));
    public int DisplayChangedCount => HasProvidedBucketCounts ? ChangedCount : Actions.Count(action => action.BucketLabel.Equals("changed", StringComparison.OrdinalIgnoreCase));
    public int DisplayRemovedCount => HasProvidedBucketCounts ? RemovedCount : Actions.Count(action => action.BucketLabel.Equals("removed", StringComparison.OrdinalIgnoreCase));
    public int DisplayDirectoryCount => DirectoryCount > 0 ? DirectoryCount : Actions.Count(action => action.IsDirectory);

    public string CompactLabel
    {
        get
        {
            var delta = Added > 0 || Removed > 0 ? $", +{Added:N0} / -{Removed:N0}" : "";
            var directories = DisplayDirectoryCount > 0 ? $", {DisplayDirectoryCount:N0} dirs" : "";
            var error = string.IsNullOrWhiteSpace(Error) ? "" : $" {Error}";
            return $"Patch plan: {EffectiveCount:N0} effective, {DuplicateCount:N0} duplicate, {FileCount:N0} files{directories}{delta}.{error}".Trim();
        }
    }
}

public sealed record PatchPlanActionSummary(
    string Mode,
    string Target,
    string Name,
    string Part,
    string Action,
    string Kind,
    string Bucket,
    int Added,
    int Removed,
    int TotalLocAfter,
    bool IsDirectory,
    bool IsDuplicate,
    bool IsEffective,
    bool CreatesFile,
    bool CreatesDirectory,
    string DuplicateAction,
    string DuplicateTarget,
    int VersionBefore,
    int VersionAfter)
{
    public string FileLabel => string.IsNullOrWhiteSpace(Target) ? "(unknown target)" : Target;
    public string PartLabel => string.IsNullOrWhiteSpace(Part) ? Mode : Part;
    public string ActionLabel => string.IsNullOrWhiteSpace(Action) ? Mode : Action;
    public string KindLabel => string.IsNullOrWhiteSpace(Kind) ? InferKind() : Kind;
    public string BucketLabel => string.IsNullOrWhiteSpace(Bucket) ? InferBucket(KindLabel) : Bucket;
    public string StatusLabel => IsDuplicate ? "duplicate" : IsEffective ? "effective" : "planned";
    public string AddedLabel => $"+{Added:N0}";
    public string RemovedLabel => $"-{Removed:N0}";
    public string DeltaLabel => $"{AddedLabel} {RemovedLabel}";
    public string LocLabel => TotalLocAfter > 0 ? $"{TotalLocAfter:N0} LOC" : "0 LOC";
    public string VersionLabel => IsDirectory ? "" : $"v{VersionBefore:N0} > v{VersionAfter:N0}";

    private string InferKind()
    {
        if (IsDuplicate)
        {
            return "DUP";
        }

        if (IsDirectory)
        {
            return CreatesDirectory ? "CREATE DIR" : "CHANGE DIR";
        }

        if (CreatesFile)
        {
            return "CREATE FILE";
        }

        return Mode switch
        {
            "delete_function" => "REMOVE",
            "insert_after_function" => "CREATE",
            "insert_before_function" => "CREATE",
            "append_to_file" => "CREATE",
            "insert_include" => "CREATE",
            _ => "CHANGE"
        };
    }

    private static string InferBucket(string kind)
    {
        if (kind.StartsWith("DUP", StringComparison.OrdinalIgnoreCase))
        {
            return "duplicate";
        }

        if (kind.StartsWith("CREATE", StringComparison.OrdinalIgnoreCase))
        {
            return "created";
        }

        if (kind.StartsWith("REMOVE", StringComparison.OrdinalIgnoreCase))
        {
            return "removed";
        }

        return "changed";
    }
}

public sealed partial class ContextPromptBuilder
{
    public string BuildFreshChatPrompt(string userPrompt, string directoryExportText, string routeLabel)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Context Control fresh-chat request");
        builder.AppendLine();
        builder.AppendLine($"Route: {routeLabel}");
        builder.AppendLine("Flow: read the DIR manifest, ask only for final source request lines, exactly one FIND, or one EXPAND ending with END, then wait for source export before producing patch work.");
        builder.AppendLine();
        builder.AppendLine("User request:");
        builder.AppendLine(string.IsNullOrWhiteSpace(userPrompt) ? "(no request text yet)" : userPrompt.Trim());
        builder.AppendLine();

        if (!string.IsNullOrWhiteSpace(directoryExportText))
        {
            builder.AppendLine("DIR export:");
            builder.AppendLine(directoryExportText.TrimEnd());
        }

        return builder.ToString();
    }

    public IReadOnlyList<string> BuildCodeExportRequestLines(string promptText)
    {
        return ParsePhase1RequestLines(promptText).RequestLines;
    }

    public ContextRequestLineParseResult ParsePhase1RequestLines(string promptText)
    {
        return (promptText ?? "")
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Aggregate(
                new
                {
                    RequestLines = new List<string>(),
                    ExtraLines = new List<string>(),
                    MeaningfulLines = new List<string>()
                },
                (state, rawLine) =>
                {
                    var normalized = NormalizeCodeExportRequestLine(rawLine);
                    if (string.IsNullOrWhiteSpace(normalized))
                    {
                        return state;
                    }

                    state.MeaningfulLines.Add(normalized);
                    if (normalized.Equals("END", StringComparison.OrdinalIgnoreCase))
                    {
                        return state;
                    }

                    if (IsCodeExportRequestLine(normalized))
                    {
                        if (!state.RequestLines.Contains(normalized, StringComparer.OrdinalIgnoreCase))
                        {
                            state.RequestLines.Add(normalized);
                        }
                    }
                    else
                    {
                        state.ExtraLines.Add(rawLine.Trim());
                    }

                    return state;
                },
                state => new ContextRequestLineParseResult(
                    state.RequestLines.ToArray(),
                    state.ExtraLines.ToArray(),
                    state.MeaningfulLines.LastOrDefault()?.Equals("END", StringComparison.OrdinalIgnoreCase) == true));
    }

    public static string NormalizeCodeExportRequestLine(string? line)
    {
        var clean = (line ?? "").Trim();
        if (string.IsNullOrWhiteSpace(clean))
        {
            return "";
        }

        if (clean.StartsWith("- ", StringComparison.Ordinal)
            || clean.StartsWith("* ", StringComparison.Ordinal)
            || clean.StartsWith("+ ", StringComparison.Ordinal))
        {
            clean = clean[2..].Trim();
        }

        clean = StripOrderedListPrefix(clean);
        clean = clean.Trim().TrimStart('[').TrimEnd(']').Trim();
        clean = clean.TrimEnd(',', ';').Trim();
        clean = clean.Trim('"', '\'', '`').Trim();
        clean = clean.TrimEnd(',', ';').Trim();
        clean = StripInlineCodeWrapper(clean);
        clean = CleanFindLine(clean);
        clean = CleanPrefixLine(clean, "EXPAND:");
        clean = CleanPrefixLine(clean, "FUNCTION:");
        clean = CleanPrefixLine(clean, "FUNC:");

        return clean;
    }

    private static string StripOrderedListPrefix(string text)
    {
        var index = 0;
        while (index < text.Length && char.IsDigit(text[index]))
        {
            index++;
        }

        if (index == 0 || index >= text.Length)
        {
            return text;
        }

        if (text[index] is not ('.' or ')'))
        {
            return text;
        }

        var next = index + 1;
        if (next >= text.Length || !char.IsWhiteSpace(text[next]))
        {
            return text;
        }

        return text[next..].Trim();
    }

    private static string StripInlineCodeWrapper(string text)
    {
        var clean = text.Trim();
        if (clean.Length >= 2 && clean[0] == '`' && clean[^1] == '`')
        {
            return clean[1..^1].Trim();
        }

        return clean;
    }

    private static string CleanFindLine(string text)
    {
        return CleanPrefixLine(text, "FIND:");
    }

    private static string CleanPrefixLine(string text, string prefix)
    {
        if (!text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return text;
        }

        var value = StripInlineCodeWrapper(text[prefix.Length..].Trim().Trim('"', '\'', '`').Trim());
        return string.IsNullOrWhiteSpace(value) ? prefix : $"{prefix} {value}";
    }

    public static bool IsCodeExportRequestLine(string? line)
    {
        var clean = NormalizeCodeExportRequestLine(line);
        if (string.IsNullOrWhiteSpace(clean))
        {
            return false;
        }

        if (clean.StartsWith("//", StringComparison.Ordinal)
            || clean.StartsWith("#", StringComparison.Ordinal)
            || clean.StartsWith("/*", StringComparison.Ordinal)
            || clean.StartsWith("*", StringComparison.Ordinal)
            || clean.Contains("CC-REPLACE", StringComparison.OrdinalIgnoreCase)
            || LooksLikeUnsupportedRequestHeader(clean)
            || LooksLikeRootedPath(clean)
            || clean.Equals("BEGIN", StringComparison.OrdinalIgnoreCase)
            || clean.Equals("END", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return clean.StartsWith("FUNCTION ", StringComparison.OrdinalIgnoreCase)
            || clean.StartsWith("FUNCTION:", StringComparison.OrdinalIgnoreCase)
            || clean.StartsWith("FUNC:", StringComparison.OrdinalIgnoreCase)
            || clean.StartsWith("FIND:", StringComparison.OrdinalIgnoreCase)
            || clean.StartsWith("EXPAND:", StringComparison.OrdinalIgnoreCase)
            || clean.Equals("CMakeLists.txt", StringComparison.OrdinalIgnoreCase)
            || clean.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
            || clean.EndsWith(".axaml", StringComparison.OrdinalIgnoreCase)
            || clean.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase)
            || clean.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase)
            || clean.EndsWith(".cpp", StringComparison.OrdinalIgnoreCase)
            || clean.EndsWith(".cc", StringComparison.OrdinalIgnoreCase)
            || clean.EndsWith(".cxx", StringComparison.OrdinalIgnoreCase)
            || clean.EndsWith(".c", StringComparison.OrdinalIgnoreCase)
            || clean.EndsWith(".h", StringComparison.OrdinalIgnoreCase)
            || clean.EndsWith(".hpp", StringComparison.OrdinalIgnoreCase)
            || clean.EndsWith(".hlsl", StringComparison.OrdinalIgnoreCase)
            || clean.EndsWith(".glsl", StringComparison.OrdinalIgnoreCase)
            || clean.EndsWith(".vert", StringComparison.OrdinalIgnoreCase)
            || clean.EndsWith(".frag", StringComparison.OrdinalIgnoreCase)
            || clean.EndsWith(".comp", StringComparison.OrdinalIgnoreCase)
            || LooksLikePathRequest(clean);
    }

    private static bool LooksLikeUnsupportedRequestHeader(string clean)
    {
        if (clean.StartsWith("FIND:", StringComparison.OrdinalIgnoreCase)
            || clean.StartsWith("FUNC:", StringComparison.OrdinalIgnoreCase)
            || clean.StartsWith("FUNCTION:", StringComparison.OrdinalIgnoreCase)
            || clean.StartsWith("EXPAND:", StringComparison.OrdinalIgnoreCase)
            || clean.StartsWith("SYMBOL:", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var separator = clean.IndexOf(':', StringComparison.Ordinal);
        if (separator <= 0)
        {
            return false;
        }

        var key = clean[..separator].Trim();
        if (key.Equals("PATH", StringComparison.OrdinalIgnoreCase)
            || key.Equals("FILE", StringComparison.OrdinalIgnoreCase)
            || key.Equals("DIR", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return separator + 1 < clean.Length
            && char.IsWhiteSpace(clean[separator + 1])
            && key.All(character => char.IsLetterOrDigit(character) || character is '_' or '-' or '/' or '\\');
    }

    private static bool LooksLikeRootedPath(string clean)
    {
        return clean.StartsWith("/", StringComparison.Ordinal)
            || clean.StartsWith("\\", StringComparison.Ordinal)
            || (clean.Length >= 3
                && char.IsLetter(clean[0])
                && clean[1] == ':'
                && clean[2] is '/' or '\\');
    }

    private static bool LooksLikePathRequest(string clean)
    {
        if (!clean.Contains('/', StringComparison.Ordinal) && !clean.Contains('\\', StringComparison.Ordinal))
        {
            return false;
        }

        return !clean.Any(char.IsWhiteSpace);
    }

    public string ExtractPatchBlocks(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "";
        }

        var matches = PatchBlockRegex().Matches(text);
        if (matches.Count == 0)
        {
            return "";
        }

        return string.Join(Environment.NewLine + Environment.NewLine, matches.Select(match => match.Value.Trim()));
    }

    public string ValidatePatchBlocks(string patchText)
    {
        var matches = PatchBlockRegex().Matches(patchText ?? "");
        if (matches.Count == 0)
        {
            return "No BEGIN/END CC-REPLACE block was found.";
        }

        for (var index = 0; index < matches.Count; index++)
        {
            var blockNumber = index + 1;
            var lines = matches[index].Value
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n')
                .Split('\n');
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var hasSeparator = false;

            foreach (var rawLine in lines.Skip(1))
            {
                var line = rawLine.Trim();
                if (line.Equals("---", StringComparison.Ordinal))
                {
                    hasSeparator = true;
                    break;
                }

                if (line.Equals("END CC-REPLACE", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                var separator = line.IndexOf(':', StringComparison.Ordinal);
                if (separator <= 0)
                {
                    continue;
                }

                var key = line[..separator].Trim();
                var value = line[(separator + 1)..].Trim();
                if (!string.IsNullOrWhiteSpace(key))
                {
                    headers[key] = value;
                }
            }

            if (!headers.ContainsKey("FILE") && !headers.ContainsKey("DIR") && !headers.ContainsKey("PATH"))
            {
                return $"CC-REPLACE block {blockNumber} has no FILE:, DIR:, or PATH: header. A bare path after BEGIN CC-REPLACE is not valid for GO.";
            }

            if (!headers.TryGetValue("MODE", out var mode) || string.IsNullOrWhiteSpace(mode))
            {
                return $"CC-REPLACE block {blockNumber} has no MODE: header.";
            }

            mode = mode.Trim().ToLowerInvariant();
            var bodylessMode = mode is "insert_include" or "delete_function" or "create_directory";
            if (!bodylessMode && !hasSeparator)
            {
                return $"CC-REPLACE block {blockNumber} uses MODE:{mode} but has no --- separator and replacement body.";
            }

            if (mode is "function" or "replace_region" or "insert_after_function" or "insert_before_function" or "delete_function"
                && (!headers.TryGetValue("NAME", out var name) || string.IsNullOrWhiteSpace(name)))
            {
                return $"CC-REPLACE block {blockNumber} uses MODE:{mode} but has no NAME: header.";
            }

            if (mode == "insert_include"
                && (!headers.TryGetValue("HEADER", out var header) || string.IsNullOrWhiteSpace(header)))
            {
                return $"CC-REPLACE block {blockNumber} uses MODE:insert_include but has no HEADER: line.";
            }
        }

        return "";
    }

    public string BuildPatchSummary(string jsonPlanOutput)
    {
        return ParsePatchPlanSummary(jsonPlanOutput).CompactLabel;
    }

    public PatchPlanSummary ParsePatchPlanSummary(string jsonPlanOutput)
    {
        if (string.IsNullOrWhiteSpace(jsonPlanOutput))
        {
            return new PatchPlanSummary(0, 0, 0, 0, 0, 0, 0, 0, 0, [], "No patch plan returned.");
        }

        try
        {
            using var document = JsonDocument.Parse(jsonPlanOutput);
            var root = document.RootElement;
            var actions = new List<PatchPlanActionSummary>();
            if (root.TryGetProperty("Actions", out var actionElements)
                && actionElements.ValueKind == JsonValueKind.Array)
            {
                foreach (var action in actionElements.EnumerateArray())
                {
                    actions.Add(new PatchPlanActionSummary(
                        ReadString(action, "Mode"),
                        ReadString(action, "Target"),
                        ReadString(action, "Name"),
                        ReadString(action, "Part"),
                        ReadString(action, "Action"),
                        ReadString(action, "Kind"),
                        ReadString(action, "Bucket"),
                        ReadInt(action, "Added"),
                        ReadInt(action, "Removed"),
                        ReadInt(action, "TotalLocAfter"),
                        ReadBool(action, "IsDirectory"),
                        ReadBool(action, "IsDuplicate"),
                        ReadBool(action, "IsEffective"),
                        ReadBool(action, "CreatesFile"),
                        ReadBool(action, "CreatesDirectory"),
                        ReadString(action, "DuplicateAction"),
                        ReadString(action, "DuplicateTarget"),
                        ReadInt(action, "VersionBefore"),
                        ReadInt(action, "VersionAfter")));
                }
            }

            return new PatchPlanSummary(
                ReadInt(root, "EffectiveCount"),
                ReadInt(root, "DuplicateCount"),
                ReadInt(root, "FileCount"),
                ReadInt(root, "DirectoryCount"),
                ReadInt(root, "CreatedCount"),
                ReadInt(root, "ChangedCount"),
                ReadInt(root, "RemovedCount"),
                ReadInt(root, "Added"),
                ReadInt(root, "Removed"),
                actions,
                ReadString(root, "Error"));
        }
        catch
        {
            var effectiveMatch = Regex.Match(jsonPlanOutput, "\"EffectiveCount\"\\s*:\\s*(\\d+)");
            var duplicateMatch = Regex.Match(jsonPlanOutput, "\"DuplicateCount\"\\s*:\\s*(\\d+)");
            var fileMatch = Regex.Match(jsonPlanOutput, "\"FileCount\"\\s*:\\s*(\\d+)");

            var effective = effectiveMatch.Success && int.TryParse(effectiveMatch.Groups[1].Value, out var effectiveValue)
                ? effectiveValue
                : 0;
            var duplicates = duplicateMatch.Success && int.TryParse(duplicateMatch.Groups[1].Value, out var duplicateValue)
                ? duplicateValue
                : 0;
            var files = fileMatch.Success && int.TryParse(fileMatch.Groups[1].Value, out var fileValue)
                ? fileValue
                : 0;

            return new PatchPlanSummary(effective, duplicates, files, 0, 0, 0, 0, 0, 0, [], "Plan JSON could not be parsed cleanly.");
        }
    }

    private static int ReadInt(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) && value.TryGetInt32(out var parsed)
            ? parsed
            : 0;
    }

    private static bool ReadBool(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return false;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => false
        };
    }

    private static string ReadString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";
    }

    [GeneratedRegex("(?ms)^\\s*BEGIN\\s+CC-REPLACE\\s*$.*?^\\s*END\\s+CC-REPLACE\\s*$")]
    private static partial Regex PatchBlockRegex();
}
