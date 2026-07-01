// CC-DESC: Represents a parsed local LLM chat transcript row with snippets and stats.

using System.Collections.ObjectModel;
using System.Text;
using System.Text.RegularExpressions;
using ContextControl.Workbench.Services;

namespace ContextControl.Workbench.ViewModels;

public sealed partial class LocalLlmChatMessageViewModel : ObservableObject
{
    private string _rawText;
    private string _capsuleSummary;
    private string _visibleText = "";
    private string _thinkingText = "";
    private LocalLlmUsageStats? _stats;
    private bool _isThinkingExpanded;
    private bool _isDiagnosticExpanded;

    public LocalLlmChatMessageViewModel(
        string role,
        string text,
        string modelId = "",
        string phase = "",
        string capsuleSummary = "",
        LocalLlmUsageStats? stats = null,
        IReadOnlyList<ContextControlAttachmentViewModel>? attachments = null,
        DateTime? createdUtc = null,
        string diagnosticPrompt = "")
    {
        Role = string.IsNullOrWhiteSpace(role) ? "assistant" : role.Trim();
        ModelId = modelId ?? "";
        Phase = phase ?? "";
        _capsuleSummary = capsuleSummary ?? "";
        _stats = stats;
        DiagnosticPrompt = diagnosticPrompt ?? "";
        AttachedFiles = new ObservableCollection<ContextControlAttachmentViewModel>(attachments ?? []);
        CreatedUtc = createdUtc ?? DateTime.UtcNow;
        Time = CreatedUtc.ToLocalTime().ToString("HH:mm");

        _rawText = text ?? "";
        var parsed = ParseMessage(_rawText, ShouldExtractRequestSnippets(Role, Phase, _rawText));
        _thinkingText = parsed.Thinking;
        _visibleText = parsed.VisibleText;
        Parts = new ObservableCollection<LocalLlmChatPartViewModel>(parsed.Parts);
        Snippets = new ObservableCollection<ChatSnippetViewModel>(parsed.Snippets);
    }

    public string Role { get; }
    public DateTime CreatedUtc { get; }
    public string Time { get; }
    public string ModelId { get; }
    public string Phase { get; }
    public string CapsuleSummary => _capsuleSummary;
    public LocalLlmUsageStats? Stats => _stats;
    public string DiagnosticPrompt { get; }
    public string VisibleText => _visibleText;
    public string ThinkingText => _thinkingText;
    public ObservableCollection<LocalLlmChatPartViewModel> Parts { get; }
    public ObservableCollection<ChatSnippetViewModel> Snippets { get; }
    public ObservableCollection<ContextControlAttachmentViewModel> AttachedFiles { get; }

    public bool IsUser => string.Equals(Role, "user", StringComparison.OrdinalIgnoreCase);
    public bool IsContextControlGenerated => string.Equals(ModelId, "ContextControl", StringComparison.OrdinalIgnoreCase)
        || string.Equals(ModelId, "ccReplace", StringComparison.OrdinalIgnoreCase);
    public bool IsCodexGenerated => Phase.StartsWith("Codex", StringComparison.OrdinalIgnoreCase)
        || ModelId.Contains("codex", StringComparison.OrdinalIgnoreCase);
    public string RoleLabel => IsUser ? "You" : IsContextControlGenerated ? ModelId : IsCodexGenerated ? "Codex" : "Local";
    public string HeaderTitle => RoleLabel;
    public string ModelLabel => SplitModelAndEffort(ModelId).Model;
    public string EffortLabel => SplitModelAndEffort(ModelId).Effort;
    public string FlowLabel => BuildFlowLabel(Phase);
    public bool HasStats => Stats is not null;
    public bool HasDiagnosticPrompt => !string.IsNullOrWhiteSpace(DiagnosticPrompt);
    public bool HasThinking => !string.IsNullOrWhiteSpace(ThinkingText);
    public bool HasAttachments => AttachedFiles.Count > 0;
    public bool HasSentAttachments => IsUser && HasAttachments;
    public bool HasSnippets => Snippets.Count > 0;
    public bool CanCreateProject => !IsUser && Snippets.Count(snippet => snippet.CanCreateProjectFile) > 0;
    public string Text => VisibleText;
    public string RawText => _rawText;
    public LocalLlmChatPartViewModel? PrimaryTextPart => Parts.FirstOrDefault(part => part.IsText);

    public string MetaLabel
    {
        get
        {
            var parts = new List<string>();
            var model = ModelLabel;
            if (!string.IsNullOrWhiteSpace(model)
                && !model.Equals(RoleLabel, StringComparison.OrdinalIgnoreCase)
                && !model.Equals("ContextControl", StringComparison.OrdinalIgnoreCase))
            {
                parts.Add(model);
            }

            if (!string.IsNullOrWhiteSpace(EffortLabel))
            {
                parts.Add(EffortLabel);
            }

            if (!string.IsNullOrWhiteSpace(FlowLabel))
            {
                parts.Add(FlowLabel);
            }

            if (Stats is not null)
            {
                parts.Add($"tokens {Stats.Summary}");
            }
            else if (!string.IsNullOrWhiteSpace(CapsuleSummary))
            {
                parts.Add(CapsuleSummary);
            }

            return string.Join(" | ", parts);
        }
    }

    public bool IsThinkingExpanded
    {
        get => _isThinkingExpanded;
        set => SetProperty(ref _isThinkingExpanded, value);
    }

    public bool IsDiagnosticExpanded
    {
        get => _isDiagnosticExpanded;
        set => SetProperty(ref _isDiagnosticExpanded, value);
    }

    public void ToggleThinking()
    {
        IsThinkingExpanded = !IsThinkingExpanded;
    }

    public void ToggleDiagnostic()
    {
        IsDiagnosticExpanded = !IsDiagnosticExpanded;
    }

    public void UpdateContent(
        string text,
        string capsuleSummary = "",
        LocalLlmUsageStats? stats = null)
    {
        _rawText = text ?? "";
        if (!string.IsNullOrWhiteSpace(capsuleSummary))
        {
            _capsuleSummary = capsuleSummary;
        }

        if (stats is not null)
        {
            _stats = stats;
        }

        var parsed = ParseMessage(_rawText, ShouldExtractRequestSnippets(Role, Phase, _rawText));
        _thinkingText = parsed.Thinking;
        _visibleText = parsed.VisibleText;
        Parts.Clear();
        foreach (var part in parsed.Parts)
        {
            Parts.Add(part);
        }

        Snippets.Clear();
        foreach (var snippet in parsed.Snippets)
        {
            Snippets.Add(snippet);
        }

        RaiseContentChanged();
    }

    public void UpdateLiveStatus(string status)
    {
        var clean = (status ?? "").Trim();
        if (string.IsNullOrWhiteSpace(clean))
        {
            return;
        }

        _rawText = clean;
        _visibleText = clean;
        Parts.Clear();
        Parts.Add(new LocalLlmChatPartViewModel("text", clean));
        RaiseContentChanged();
    }

    public void AppendLiveThinking(string thinkingDelta)
    {
        var clean = (thinkingDelta ?? "").Trim();
        if (string.IsNullOrWhiteSpace(clean))
        {
            return;
        }

        _thinkingText = string.IsNullOrWhiteSpace(_thinkingText)
            ? clean
            : $"{_thinkingText}{Environment.NewLine}{clean}";
        if (!_isThinkingExpanded)
        {
            IsThinkingExpanded = true;
        }

        OnPropertyChanged(nameof(ThinkingText));
        OnPropertyChanged(nameof(HasThinking));
    }

    private void RaiseContentChanged()
    {
        OnPropertyChanged(nameof(CapsuleSummary));
        OnPropertyChanged(nameof(Stats));
        OnPropertyChanged(nameof(HasStats));
        OnPropertyChanged(nameof(VisibleText));
        OnPropertyChanged(nameof(ThinkingText));
        OnPropertyChanged(nameof(HasThinking));
        OnPropertyChanged(nameof(Text));
        OnPropertyChanged(nameof(RawText));
        OnPropertyChanged(nameof(Parts));
        OnPropertyChanged(nameof(Snippets));
        OnPropertyChanged(nameof(HasSnippets));
        OnPropertyChanged(nameof(CanCreateProject));
        OnPropertyChanged(nameof(PrimaryTextPart));
        OnPropertyChanged(nameof(HeaderTitle));
        OnPropertyChanged(nameof(ModelLabel));
        OnPropertyChanged(nameof(EffortLabel));
        OnPropertyChanged(nameof(FlowLabel));
        OnPropertyChanged(nameof(MetaLabel));
    }

    private static (string Model, string Effort) SplitModelAndEffort(string value)
    {
        var clean = CleanHeaderSegment(value);
        if (string.IsNullOrWhiteSpace(clean))
        {
            return ("", "");
        }

        var separator = clean.IndexOf(" / ", StringComparison.Ordinal);
        if (separator < 0)
        {
            return (clean, "");
        }

        return (
            clean[..separator].Trim(),
            clean[(separator + 3)..].Trim());
    }

    private static string BuildFlowLabel(string phase)
    {
        var clean = CleanHeaderSegment(phase);
        if (string.IsNullOrWhiteSpace(clean))
        {
            return "";
        }

        var normalized = clean.StartsWith("Codex ", StringComparison.OrdinalIgnoreCase)
            ? clean["Codex ".Length..].Trim()
            : clean;
        return normalized.ToLowerInvariant() switch
        {
            "file request" => "DIR + Request",
            "dir request" => "DIR + Request",
            "dir + request" => "DIR + Request",
            "cc" => "CC",
            "source audit" => "CC",
            "patch write" => "CC patch",
            "patch review" => "Patch Review",
            "go preview" => "GO preview",
            "go apply" => "GO apply",
            "chat" => "Chat",
            _ => normalized
        };
    }

    private static string CleanHeaderSegment(string value)
    {
        var clean = (value ?? "")
            .Replace("\r\n", " ", StringComparison.Ordinal)
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();
        return string.Join(' ', clean.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries));
    }

    private static bool ShouldExtractRequestSnippets(string role, string phase, string text)
    {
        if (string.Equals(role, "user", StringComparison.OrdinalIgnoreCase))
        {
            return IsMostlyRequestList(text ?? "");
        }

        var cleanPhase = (phase ?? "").Trim();
        if (cleanPhase.StartsWith("Codex", StringComparison.OrdinalIgnoreCase)
            && (text ?? "").Contains("Codex phase audit: Error", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !cleanPhase.EndsWith("chat", StringComparison.OrdinalIgnoreCase);
    }

    private static ParsedMessage ParseMessage(string text, bool allowRequestSnippets)
    {
        var clean = text ?? "";
        var thinking = ExtractThinking(clean, out clean);
        var snippets = new List<ChatSnippetViewModel>();
        var parts = new List<LocalLlmChatPartViewModel>();

        var patchMatches = PatchBlockRegex().Matches(clean).Cast<Match>().ToArray();
        if (patchMatches.Length > 0)
        {
            var aggregatePatch = string.Join(
                Environment.NewLine + Environment.NewLine,
                patchMatches.Select(patch => patch.Value.Trim()));
            snippets.Add(new ChatSnippetViewModel("patch", "cc-replace", aggregatePatch));
        }

        clean = PatchBlockRegex().Replace(clean, Environment.NewLine);

        var codeFences = ParseCodeFences(clean);
        var requestScanText = RemoveCodeFences(clean, codeFences);
        requestScanText = PatchPlaceholderRegex().Replace(requestScanText, Environment.NewLine);

        var cursor = 0;
        foreach (var fence in codeFences)
        {
            var precedingText = clean[cursor..fence.Index];
            AddTextPart(precedingText, parts);
            var language = fence.Language;
            var code = fence.Code.Trim();
            if (PatchPlaceholderRegex().IsMatch(code))
            {
                cursor = fence.Index + fence.Length;
                continue;
            }

            var requestFromFence = allowRequestSnippets && IsMostlyRequestList(code)
                ? ExtractRequestSnippets(code)
                : [];
            if (requestFromFence.Count > 0)
            {
                AddRequestSnippetParts(requestFromFence, parts, snippets);
            }
            else
            {
                var snippet = string.Equals(language.Trim(), "cc-patch-plan", StringComparison.OrdinalIgnoreCase)
                    ? new ChatSnippetViewModel("patch-plan", "cc-patch-plan", code)
                    : new ChatSnippetViewModel(
                        "code",
                        language,
                        code,
                        InferSuggestedFileName(language, precedingText));
                snippets.Add(snippet);
                parts.Add(new LocalLlmChatPartViewModel("snippet", "", snippet));
            }

            cursor = fence.Index + fence.Length;
        }

        AddTextPart(clean[cursor..], parts);

        var requestSnippets = allowRequestSnippets
            ? ExtractRequestSnippets(requestScanText)
            : [];
        if (requestSnippets.Count > 0 && snippets.All(snippet => !snippet.IsRequestList))
        {
            AddRequestSnippetParts(requestSnippets, parts, snippets);
        }

        foreach (var patch in snippets.Where(snippet => snippet.IsPatch))
        {
            if (parts.All(part => !ReferenceEquals(part.Snippet, patch)))
            {
                parts.Add(new LocalLlmChatPartViewModel("snippet", "", patch));
            }
        }

        var visible = string.Join(Environment.NewLine, parts.Where(part => part.IsText).Select(part => part.Text)).Trim();
        return new ParsedMessage(visible, thinking, parts, snippets);
    }

    private static void AddTextPart(string text, List<LocalLlmChatPartViewModel> parts)
    {
        var clean = text.Trim();
        if (!string.IsNullOrWhiteSpace(clean))
        {
            parts.Add(new LocalLlmChatPartViewModel("text", clean));
        }
    }

    private static void AddRequestSnippetParts(
        IReadOnlyList<ChatSnippetViewModel> requestSnippets,
        List<LocalLlmChatPartViewModel> parts,
        List<ChatSnippetViewModel> snippets)
    {
        if (requestSnippets.Count > 1
            && parts.All(part => !string.Equals(part.Text, "Source and FIND run separately.", StringComparison.OrdinalIgnoreCase)))
        {
            parts.Add(new LocalLlmChatPartViewModel("text", "Source and FIND run separately."));
        }

        foreach (var snippet in requestSnippets)
        {
            snippets.Add(snippet);
            parts.Add(new LocalLlmChatPartViewModel("snippet", "", snippet));
        }
    }

    private static IReadOnlyList<CodeFenceBlock> ParseCodeFences(string text)
    {
        var blocks = new List<CodeFenceBlock>();
        var index = 0;
        while (index < text.Length)
        {
            var line = ReadLine(text, index, out var lineEnd, out var nextLineStart);
            if (!TryParseFenceOpen(line, out var fence, out var language))
            {
                index = nextLineStart;
                continue;
            }

            var codeStart = nextLineStart;
            var scan = codeStart;
            while (scan < text.Length)
            {
                var closeLine = ReadLine(text, scan, out _, out var afterCloseLine);
                if (IsFenceClose(closeLine, fence))
                {
                    blocks.Add(new CodeFenceBlock(index, afterCloseLine - index, language, text[codeStart..scan]));
                    index = afterCloseLine;
                    break;
                }

                scan = afterCloseLine;
            }

            if (scan >= text.Length)
            {
                blocks.Add(new CodeFenceBlock(index, text.Length - index, language, text[codeStart..]));
                index = text.Length;
            }
        }

        return blocks;
    }

    private static string RemoveCodeFences(string text, IReadOnlyList<CodeFenceBlock> fences)
    {
        if (fences.Count == 0)
        {
            return text;
        }

        var builder = new StringBuilder(text.Length);
        var cursor = 0;
        foreach (var fence in fences)
        {
            builder.Append(text, cursor, fence.Index - cursor);
            builder.AppendLine();
            cursor = fence.Index + fence.Length;
        }

        builder.Append(text, cursor, text.Length - cursor);
        return builder.ToString();
    }

    private static bool TryParseFenceOpen(string line, out string fence, out string language)
    {
        fence = "";
        language = "";
        var trimmed = line.TrimStart(' ', '\t');
        if (trimmed.Length < 3 || (trimmed[0] != '`' && trimmed[0] != '~'))
        {
            return false;
        }

        var fenceChar = trimmed[0];
        var count = 0;
        while (count < trimmed.Length && trimmed[count] == fenceChar)
        {
            count++;
        }

        if (count < 3)
        {
            return false;
        }

        fence = new string(fenceChar, count);
        language = trimmed[count..].Trim();
        return true;
    }

    private static bool IsFenceClose(string line, string fence)
    {
        return line.Trim(' ', '\t').Equals(fence, StringComparison.Ordinal);
    }

    private static string ReadLine(string text, int start, out int lineEnd, out int nextLineStart)
    {
        lineEnd = text.IndexOf('\n', start);
        if (lineEnd < 0)
        {
            lineEnd = text.Length;
            nextLineStart = text.Length;
        }
        else
        {
            nextLineStart = lineEnd + 1;
            if (lineEnd > start && text[lineEnd - 1] == '\r')
            {
                lineEnd--;
            }
        }

        return text[start..lineEnd];
    }

    private static string ExtractThinking(string text, out string withoutThinking)
    {
        var builder = new StringBuilder();
        withoutThinking = ThinkRegex().Replace(text, match =>
        {
            if (builder.Length > 0)
            {
                builder.AppendLine();
            }

            builder.Append(match.Groups["body"].Value.Trim());
            return "";
        });

        return builder.ToString().Trim();
    }

    private static IReadOnlyList<ChatSnippetViewModel> ExtractRequestSnippets(string text)
    {
        var lines = (text ?? "")
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToArray();

        var requestLines = new List<string>();
        foreach (var line in lines)
        {
            var normalized = ContextPromptBuilder.NormalizeCodeExportRequestLine(line);
            if (normalized.Equals("END", StringComparison.OrdinalIgnoreCase))
            {
                if (requestLines.Count > 0)
                {
                    return BuildRequestSnippets(requestLines);
                }

                continue;
            }

            if (IsRequestLine(normalized))
            {
                requestLines.Add(normalized);
            }
        }

        if (requestLines.Count == 0)
        {
            return [];
        }

        return BuildRequestSnippets(requestLines);
    }

    private static IReadOnlyList<ChatSnippetViewModel> BuildRequestSnippets(IReadOnlyList<string> requestLines)
    {
        var clean = requestLines
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Where(line => !line.Equals("END", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (clean.Length == 0)
        {
            return [];
        }

        var findLines = clean
            .Where(line => line.StartsWith("FIND:", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var expandLines = clean
            .Where(line => line.StartsWith("EXPAND:", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var sourceLines = clean
            .Except(findLines, StringComparer.OrdinalIgnoreCase)
            .Except(expandLines, StringComparer.OrdinalIgnoreCase)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var snippets = new List<ChatSnippetViewModel>();
        if (sourceLines.Length > 0)
        {
            snippets.Add(BuildRequestSnippet(sourceLines, "cc-request-source"));
        }

        if (expandLines.Length > 0)
        {
            snippets.Add(BuildRequestSnippet([expandLines[0]], "cc-request-expand"));
        }

        if (findLines.Length > 0)
        {
            snippets.Add(BuildRequestSnippet(findLines, "cc-request-find"));
        }

        return snippets.Count > 0
            ? snippets
            : [BuildRequestSnippet(clean.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), "cc-request")];
    }

    private static ChatSnippetViewModel BuildRequestSnippet(IReadOnlyList<string> requestLines, string language)
    {
        var finalLines = requestLines
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Concat(["END"]);
        return new ChatSnippetViewModel("request", language, string.Join(Environment.NewLine, finalLines));
    }

    private static bool IsMostlyRequestList(string text)
    {
        var lines = (text ?? "")
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToArray();
        if (lines.Length == 0)
        {
            return false;
        }

        var requestLike = lines.Count(line =>
        {
            var normalized = ContextPromptBuilder.NormalizeCodeExportRequestLine(line);
            return normalized.Equals("END", StringComparison.OrdinalIgnoreCase)
                || IsRequestLine(normalized);
        });
        return requestLike == lines.Length && lines.Any(line => IsRequestLine(ContextPromptBuilder.NormalizeCodeExportRequestLine(line)));
    }

    private static bool IsRequestLine(string line)
    {
        return ContextPromptBuilder.IsCodeExportRequestLine(line);
    }

    private static string InferSuggestedFileName(string language, string precedingText)
    {
        var context = TakeTailLines(precedingText, 8);
        foreach (Match match in FileNameHintRegex().Matches(context).Cast<Match>().Reverse())
        {
            var fileName = match.Groups["file"].Value.Trim();
            if (IsPlausibleSnippetFileName(fileName, language))
            {
                return fileName;
            }
        }

        return "";
    }

    private static string TakeTailLines(string text, int maxLines)
    {
        var lines = (text ?? "")
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');
        return string.Join('\n', lines.TakeLast(Math.Max(1, maxLines)));
    }

    private static bool IsPlausibleSnippetFileName(string fileName, string language)
    {
        var clean = (fileName ?? "").Trim().Trim('`', '\'', '"');
        if (string.IsNullOrWhiteSpace(clean) || clean.Length > 160)
        {
            return false;
        }

        var leaf = clean.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "";
        if (string.IsNullOrWhiteSpace(leaf)
            || leaf.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || !leaf.Contains('.', StringComparison.Ordinal))
        {
            return false;
        }

        var extension = Path.GetExtension(leaf);
        if (string.IsNullOrWhiteSpace(extension))
        {
            return false;
        }

        var lang = (language ?? "").Trim().ToLowerInvariant();
        return lang switch
        {
            "html" or "htm" => extension.Equals(".html", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".htm", StringComparison.OrdinalIgnoreCase),
            "javascript" or "js" or "jsx" or "mjs" => extension.Equals(".js", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".mjs", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".jsx", StringComparison.OrdinalIgnoreCase),
            "typescript" or "ts" or "tsx" => extension.Equals(".ts", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".tsx", StringComparison.OrdinalIgnoreCase),
            "css" or "scss" or "sass" or "less" => extension.Equals(".css", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".scss", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".sass", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".less", StringComparison.OrdinalIgnoreCase),
            _ => true
        };
    }

    private sealed record ParsedMessage(
        string VisibleText,
        string Thinking,
        IReadOnlyList<LocalLlmChatPartViewModel> Parts,
        IReadOnlyList<ChatSnippetViewModel> Snippets);

    private sealed record CodeFenceBlock(int Index, int Length, string Language, string Code);

    [GeneratedRegex("(?ms)<think>\\s*(?<body>.*?)\\s*</think>")]
    private static partial Regex ThinkRegex();

    [GeneratedRegex("(?ms)^\\s*BEGIN\\s+CC-REPLACE\\s*$.*?^\\s*END\\s+CC-REPLACE\\s*$")]
    private static partial Regex PatchBlockRegex();

    [GeneratedRegex("^\\s*\\[CC-REPLACE patch(?: block(?: \\d+)?|: \\d+(?:,\\d{3})* blocks)\\]\\s*$")]
    private static partial Regex PatchPlaceholderRegex();

    [GeneratedRegex(@"(?i)(?:\(|`|file\s+named\s+|file\s+|named\s+|save\s+(?:it|this|as)?\s*)(?<file>[A-Z0-9._/\- ]+\.[A-Z0-9]{1,12})(?:\)|`|:)?")]
    private static partial Regex FileNameHintRegex();
}
