// CC-DESC: Represents a parsed code or ContextControl snippet inside local chat.

namespace ContextControl.Workbench.ViewModels;

public sealed record PatchPlanSnippetRow(
    string FileName,
    string Version,
    string Added,
    string Removed,
    string Loc,
    string Target,
    string Actions);

public sealed record PatchSnippetPreviewRow(
    string FileName,
    string Mode,
    string Detail,
    string BodyLabel,
    string Target);

public sealed class ChatSnippetViewModel(string kind, string language, string text, string suggestedFileName = "") : ObservableObject
{
    private bool _isExpanded;
    private IReadOnlyList<PatchPlanSnippetRow>? _patchPlanRows;
    private IReadOnlyList<PatchSnippetPreviewRow>? _patchPreviewRows;

    public string Kind { get; } = string.IsNullOrWhiteSpace(kind) ? "code" : kind.Trim();
    public string Language { get; } = string.IsNullOrWhiteSpace(language) ? "text" : language.Trim();
    public string Text { get; } = text ?? "";
    public string SuggestedFileName { get; } = ResolveSuggestedFileName(language, text, suggestedFileName);

    public bool IsPatch => string.Equals(Kind, "patch", StringComparison.OrdinalIgnoreCase);
    public bool IsRequestList => string.Equals(Kind, "request", StringComparison.OrdinalIgnoreCase);
    public bool IsSourceRequestList => IsRequestList && string.Equals(Language, "cc-request-source", StringComparison.OrdinalIgnoreCase);
    public bool IsFindRequestList => IsRequestList && string.Equals(Language, "cc-request-find", StringComparison.OrdinalIgnoreCase);
    public bool IsExpandRequestList => IsRequestList && string.Equals(Language, "cc-request-expand", StringComparison.OrdinalIgnoreCase);
    public bool IsPatchPlan => string.Equals(Kind, "patch-plan", StringComparison.OrdinalIgnoreCase)
        || string.Equals(Language, "cc-patch-plan", StringComparison.OrdinalIgnoreCase);
    public bool IsCode => !IsPatch && !IsRequestList && !IsPatchPlan;
    public bool CanSaveAsFile => IsCode && !string.IsNullOrWhiteSpace(Text);
    public bool CanCreateProjectFile => CanSaveAsFile && LooksLikeCompleteFile(Language, Text);
    public bool HasPromptAction => IsPatch || IsRequestList;

    public string TypeLabel => IsPatch
        ? "patch"
        : IsRequestList ? "request" : IsPatchPlan ? "plan" : Language.ToLowerInvariant();

    public string Title => IsPatch
        ? PatchLabel
        : IsRequestList ? ResolveRequestMetaLabel() : IsPatchPlan ? "Patch file plan" : $"Code snippet: {Language}";

    public string ActionLabel => IsPatch
        ? "Send to Prompt"
        : IsRequestList ? "Use for CC" : "Copy";

    public int LineCount => string.IsNullOrEmpty(Text)
        ? 0
        : Text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n').Length;

    public string MetaLabel => IsPatch
        ? PatchLabel
        : IsRequestList ? ResolveRequestMetaLabel() : IsPatchPlan ? "Patch plan" : ResolveCodeMetaLabel();

    public string CompactMetaLabel
    {
        get
        {
            if (IsPatchPlan)
            {
                return "Patch plan";
            }

            if (IsPatch)
            {
                return PatchLabel;
            }

            return IsRequestList ? ResolveRequestMetaLabel() : ResolveCodeMetaLabel();
        }
    }

    private string ResolveRequestMetaLabel()
    {
        if (IsSourceRequestList)
        {
            return "CC source request";
        }

        if (IsFindRequestList)
        {
            return "CC FIND request";
        }

        if (IsExpandRequestList)
        {
            return "CC EXPAND request";
        }

        return "CC request";
    }

    private string ResolveCodeMetaLabel()
    {
        return IsCode && !string.IsNullOrWhiteSpace(SuggestedFileName)
            ? SuggestedFileName
            : Language;
    }

    public string DocumentPath => IsPatch
        ? "snippet.diff"
        : IsRequestList ? "request.md" : SuggestedFileName;

    public double CodePreviewHeight => Math.Clamp(42 + Math.Min(LineCount, 16) * 16, 88, 260);
    public double CollapsedPreviewHeight => IsPatch
        ? Math.Clamp(8 + Math.Max(1, Math.Min(PatchPreviewRows.Count, 4)) * 16, 24, 74)
        : 24;
    public double DisplayHeight => IsExpanded ? CodePreviewHeight : CollapsedPreviewHeight;
    public string DisplayText => IsExpanded ? Text : PreviewText;
    public IReadOnlyList<PatchPlanSnippetRow> PatchPlanRows => IsPatchPlan ? _patchPlanRows ??= ParsePatchPlanRows(Text) : [];
    public IReadOnlyList<PatchSnippetPreviewRow> PatchPreviewRows => IsPatch ? _patchPreviewRows ??= ParsePatchPreviewRows(Text) : [];
    public int PatchBlockCount => IsPatch ? PatchPreviewRows.Count : 0;
    public int PatchTargetCount => IsPatch
        ? PatchPreviewRows
            .Where(row => !string.IsNullOrWhiteSpace(row.Target))
            .Select(row => row.Target)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count()
        : 0;

    private string PatchLabel => IsPatch
        ? PatchBlockCount <= 0
            ? "CC-Replace patch"
            : PatchBlockCount == 1
            ? "CC-Replace patch: 1 block"
            : $"CC-Replace patch: {PatchBlockCount:N0} blocks"
        : "";

    public string PreviewText
    {
        get
        {
            if (IsPatch)
            {
                return BuildPatchPreviewText(PatchPreviewRows);
            }

            var clean = (Text ?? "")
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n')
                .TrimEnd();
            if (string.IsNullOrWhiteSpace(clean))
            {
                return "";
            }

            var line = clean
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(item => item.Trim())
                .FirstOrDefault(item => item.Length > 0) ?? clean.Trim();
            line = string.Join(' ', line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries));
            return line.Length <= 220 ? line : line[..220] + " ...";
        }
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (SetProperty(ref _isExpanded, value))
            {
                OnPropertyChanged(nameof(IsCollapsed));
                OnPropertyChanged(nameof(ToggleLabel));
                OnPropertyChanged(nameof(DisplayHeight));
                OnPropertyChanged(nameof(DisplayText));
            }
        }
    }

    public bool IsCollapsed => !IsExpanded;

    public string ToggleLabel => IsExpanded ? "Collapse" : "Expand";

    public string CollapsedPreview
    {
        get
        {
            var clean = (Text ?? "")
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n')
                .Trim();
            if (clean.Length <= 420)
            {
                return clean;
            }

            return clean[..420] + " ...";
        }
    }

    public void ToggleExpanded()
    {
        IsExpanded = !IsExpanded;
    }

    private static IReadOnlyList<PatchPlanSnippetRow> ParsePatchPlanRows(string text)
    {
        var rows = new List<PatchPlanSnippetRow>();
        foreach (var rawLine in (text ?? "")
                     .Replace("\r\n", "\n", StringComparison.Ordinal)
                     .Replace('\r', '\n')
                     .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = rawLine.Split('\t');
            if (parts.Length < 7)
            {
                continue;
            }

            rows.Add(new PatchPlanSnippetRow(
                parts[0].Trim(),
                parts[1].Trim(),
                parts[2].Trim(),
                parts[3].Trim(),
                parts[4].Trim(),
                parts[5].Trim(),
                parts[6].Trim()));
        }

        return rows;
    }

    private static IReadOnlyList<PatchSnippetPreviewRow> ParsePatchPreviewRows(string text)
    {
        var rows = new List<PatchSnippetPreviewRow>();
        var inBlock = false;
        var inBody = false;
        var target = "";
        var mode = "";
        var name = "";
        var header = "";
        var bodyLines = 0;

        foreach (var rawLine in NormalizeLines(text))
        {
            var line = rawLine.Trim();
            if (line.Equals("BEGIN CC-REPLACE", StringComparison.OrdinalIgnoreCase))
            {
                inBlock = true;
                inBody = false;
                target = "";
                mode = "";
                name = "";
                header = "";
                bodyLines = 0;
                continue;
            }

            if (!inBlock)
            {
                continue;
            }

            if (line.Equals("END CC-REPLACE", StringComparison.OrdinalIgnoreCase))
            {
                var cleanMode = string.IsNullOrWhiteSpace(mode) ? "mode?" : mode;
                var cleanTarget = string.IsNullOrWhiteSpace(target) ? "(unknown target)" : target;
                var detail = !string.IsNullOrWhiteSpace(name)
                    ? name
                    : !string.IsNullOrWhiteSpace(header) ? header : "";
                var bodyLabel = bodyLines > 0 ? $"{bodyLines:N0} LOC" : "bodyless";
                rows.Add(new PatchSnippetPreviewRow(
                    BuildFileName(cleanTarget, cleanMode.Equals("create_directory", StringComparison.OrdinalIgnoreCase)),
                    cleanMode,
                    detail,
                    bodyLabel,
                    cleanTarget));
                inBlock = false;
                continue;
            }

            if (line.Equals("---", StringComparison.Ordinal))
            {
                inBody = true;
                continue;
            }

            if (inBody)
            {
                bodyLines++;
                continue;
            }

            if (line.StartsWith("FILE:", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("DIR:", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("PATH:", StringComparison.OrdinalIgnoreCase))
            {
                var separator = line.IndexOf(':', StringComparison.Ordinal);
                target = separator >= 0 ? line[(separator + 1)..].Trim() : "";
            }
            else if (line.StartsWith("MODE:", StringComparison.OrdinalIgnoreCase))
            {
                mode = line["MODE:".Length..].Trim();
            }
            else if (line.StartsWith("NAME:", StringComparison.OrdinalIgnoreCase))
            {
                name = line["NAME:".Length..].Trim();
            }
            else if (line.StartsWith("HEADER:", StringComparison.OrdinalIgnoreCase))
            {
                header = line["HEADER:".Length..].Trim();
            }
        }

        return rows;
    }

    private static string BuildPatchPreviewText(IReadOnlyList<PatchSnippetPreviewRow> rows)
    {
        if (rows.Count == 0)
        {
            return "CC-Replace patch";
        }

        var lines = rows
            .Take(4)
            .Select(row =>
            {
                var detail = string.IsNullOrWhiteSpace(row.Detail) ? "" : $" :: {row.Detail}";
                return $"{row.FileName}  {row.Mode}{detail}  {row.BodyLabel}";
            })
            .ToList();
        if (rows.Count > lines.Count)
        {
            lines.Add($"+ {rows.Count - lines.Count:N0} more block(s)");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static IEnumerable<string> NormalizeLines(string text)
    {
        return (text ?? "")
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');
    }

    private static string BuildFileName(string target, bool isDirectory)
    {
        var normalized = (target ?? "").Replace('\\', '/').TrimEnd('/');
        var fileName = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = isDirectory ? "directory" : "(unknown target)";
        }

        return isDirectory ? $"{fileName}/" : fileName;
    }

    private static string ResolveSuggestedFileName(string? language, string? text, string? explicitFileName)
    {
        var cleanExplicit = CleanFileName(explicitFileName);
        if (!string.IsNullOrWhiteSpace(cleanExplicit))
        {
            return cleanExplicit;
        }

        var lang = (language ?? "").Trim().ToLowerInvariant();
        if (lang is "html" or "htm")
        {
            return "index.html";
        }

        if (lang is "css" or "scss" or "sass" or "less")
        {
            return "styles.css";
        }

        if (lang is "javascript" or "js" or "jsx" or "mjs")
        {
            return LooksLikeSnakeJavaScript(text) ? "snake.js" : "script.js";
        }

        return lang switch
        {
            "typescript" or "ts" or "tsx" => "script.ts",
            "json" => "data.json",
            "markdown" or "md" => "README.md",
            "python" or "py" => "script.py",
            "powershell" or "pwsh" or "ps1" => "script.ps1",
            "csharp" or "cs" => "Program.cs",
            "java" => "Main.java",
            "go" => "main.go",
            "rust" or "rs" => "main.rs",
            "cpp" or "c++" or "cc" or "cxx" => "main.cpp",
            "c" => "main.c",
            "xml" => "document.xml",
            "yaml" or "yml" => "config.yaml",
            "toml" => "config.toml",
            "sql" => "query.sql",
            "sh" or "bash" or "shell" => "script.sh",
            _ => "snippet.txt"
        };
    }

    private static string CleanFileName(string? fileName)
    {
        var clean = (fileName ?? "").Trim().Trim('`', '\'', '"');
        if (string.IsNullOrWhiteSpace(clean))
        {
            return "";
        }

        clean = clean.Replace('\\', '/');
        clean = clean.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? clean;
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            clean = clean.Replace(invalid, '_');
        }

        return clean;
    }

    private static bool LooksLikeSnakeJavaScript(string? text)
    {
        var clean = text ?? "";
        return clean.Contains("class Snake", StringComparison.OrdinalIgnoreCase)
            || clean.Contains("new Snake", StringComparison.OrdinalIgnoreCase)
            || clean.Contains("gameCanvas", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeCompleteFile(string? language, string? text)
    {
        var clean = (text ?? "").Trim();
        if (clean.Length < 8)
        {
            return false;
        }

        var lang = (language ?? "").Trim().ToLowerInvariant();
        if (lang is "diff" or "patch")
        {
            return false;
        }

        if (clean.Contains("*** Begin Patch", StringComparison.OrdinalIgnoreCase)
            || clean.Contains("<<<<<<<", StringComparison.Ordinal)
            || clean.Contains("@@ ", StringComparison.Ordinal))
        {
            return false;
        }

        return lang is "html" or "htm" or "css" or "scss" or "sass" or "less"
            or "javascript" or "js" or "jsx" or "mjs"
            or "typescript" or "ts" or "tsx" or "json"
            or "python" or "py" or "csharp" or "cs" or "java"
            or "go" or "rust" or "rs" or "cpp" or "c++" or "cc" or "cxx"
            or "c" or "xml" or "yaml" or "yml" or "toml" or "sql"
            or "sh" or "bash" or "shell"
            || clean.Contains("<!doctype html", StringComparison.OrdinalIgnoreCase)
            || clean.Contains("<html", StringComparison.OrdinalIgnoreCase);
    }
}
