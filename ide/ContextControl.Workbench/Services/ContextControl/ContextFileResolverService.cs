// CC-DESC: Resolves user task text into deterministic CC file/FIND request lines.

using System.Text;

namespace ContextControl.Workbench.Services;

public sealed class ContextFileResolverService
{
    private const int HighConfidenceScore = 75;
    private const int ExactPathScore = 55;
    private const int MaxRequestLines = 5;

    public ContextFileResolveResult Resolve(string userMessage, ContextSemanticIndex? index)
    {
        var queryWords = ExtractQueryWords(userMessage).ToArray();
        if (index is null || index.IsEmpty || queryWords.Length == 0)
        {
            return BuildFindResult(userMessage, queryWords, "low", "no semantic index");
        }

        var intent = ResolveIntent(userMessage, queryWords);
        var candidates = index.Files
            .Select(signal => ScoreSignal(signal, queryWords, intent))
            .Where(candidate => candidate.Score > 0)
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.RequestLine, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var top = candidates.FirstOrDefault();
        if (top is not null && top.Score >= HighConfidenceScore)
        {
            var selected = candidates
                .Where(candidate => candidate.IsExactPath && candidate.Score >= Math.Max(ExactPathScore, top.Score - 45))
                .Take(MaxRequestLines)
                .ToList();
            ExpandRelatedFiles(selected, candidates, intent);
            selected = selected
                .DistinctBy(candidate => candidate.RequestLine, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(candidate => candidate.Score)
                .ThenBy(candidate => candidate.RequestLine, StringComparer.OrdinalIgnoreCase)
                .Take(MaxRequestLines)
                .ToList();

            if (selected.Count > 0)
            {
                return new ContextFileResolveResult(
                    selected.Select(candidate => candidate.RequestLine).ToArray(),
                    "high",
                    "exact path",
                    selected,
                    BuildResultReasons(selected, "high-confidence semantic file match"));
            }
        }

        var confidence = top is not null && top.Score >= 38 ? "medium" : "low";
        return BuildFindResult(userMessage, queryWords, confidence, "semantic score below exact-path threshold");
    }

    private static ContextFileResolveCandidate ScoreSignal(ContextFileSignal signal, IReadOnlyList<string> queryWords, RequestIntent intent)
    {
        var score = 0;
        var reasons = new List<string>();
        var pathTokens = signal.PathTokens.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var contentTokens = signal.ContentTokens.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var markerTokens = TokenizeMany(signal.Markers).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var memberTokens = TokenizeMany(signal.Members.Concat(signal.Bindings)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var stringTokens = TokenizeMany(signal.Strings).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var lowerPath = signal.Path.ToLowerInvariant();
        var lowerRole = signal.Role.ToLowerInvariant();

        foreach (var word in queryWords)
        {
            if (pathTokens.Contains(word))
            {
                score += 24;
                AddReason(reasons, $"path:{word}");
            }

            if (markerTokens.Contains(word))
            {
                score += 30;
                AddReason(reasons, $"marker:{word}");
            }

            if (memberTokens.Contains(word))
            {
                score += 24;
                AddReason(reasons, $"member:{word}");
            }

            if (stringTokens.Contains(word))
            {
                score += 20;
                AddReason(reasons, $"text:{word}");
            }

            if (contentTokens.Contains(word))
            {
                score += 8;
                AddReason(reasons, $"signal:{word}");
            }

            if (lowerPath.Contains(word, StringComparison.Ordinal))
            {
                score += 5;
            }

            if (lowerRole.Contains(word, StringComparison.Ordinal))
            {
                score += 5;
                AddReason(reasons, $"role:{word}");
            }

            if (IsNearAny(word, pathTokens))
            {
                score += 12;
                AddReason(reasons, $"near path:{word}");
            }

            if (IsNearAny(word, markerTokens) || IsNearAny(word, memberTokens))
            {
                score += 14;
                AddReason(reasons, $"near symbol:{word}");
            }
        }

        ApplyIntentBoosts(signal, intent, ref score, reasons);
        return new ContextFileResolveCandidate(signal.Path, score, reasons.Take(8).ToArray(), true);
    }

    private static void ApplyIntentBoosts(ContextFileSignal signal, RequestIntent intent, ref int score, List<string> reasons)
    {
        var lowerPath = signal.Path.ToLowerInvariant();
        var extension = signal.Extension.ToLowerInvariant();
        var isXaml = extension is ".axaml" or ".xaml";
        var isCSharp = extension is ".cs";

        if (intent.Ui)
        {
            if (isXaml || lowerPath.Contains("/views/", StringComparison.Ordinal) || lowerPath.Contains("/styles/", StringComparison.Ordinal))
            {
                score += 12;
                AddReason(reasons, "ui file");
            }
        }

        if (intent.VisualStyle)
        {
            if (intent.ThemeSystem)
            {
                if (lowerPath.Contains("workbenchthemeresources", StringComparison.Ordinal))
                {
                    score += 90;
                    AddReason(reasons, "theme palette owner");
                }

                if (lowerPath.Contains("workbenchviewmodel", StringComparison.Ordinal))
                {
                    score += 72;
                    AddReason(reasons, "theme option list");
                }

                if (lowerPath.Contains("codeeditor.palette", StringComparison.Ordinal))
                {
                    score += 64;
                    AddReason(reasons, "syntax palette owner");
                }

                if (lowerPath.Contains("themesettingswindow", StringComparison.Ordinal))
                {
                    score += 46;
                    AddReason(reasons, "theme picker UI");
                }

                if (lowerPath.Contains("workbenchdesign", StringComparison.Ordinal))
                {
                    score += 42;
                    AddReason(reasons, "global style resources");
                }
            }

            if (isXaml)
            {
                score += 28;
                AddReason(reasons, "visual/style");
            }

            if (lowerPath.Contains("/styles/", StringComparison.Ordinal)
                || lowerPath.Contains("design", StringComparison.Ordinal)
                || signal.Role.Contains("style", StringComparison.OrdinalIgnoreCase))
            {
                score += 24;
                AddReason(reasons, "style owner");
            }

            if (signal.Markers.Any(marker => marker.Contains("cc-", StringComparison.OrdinalIgnoreCase)
                    || marker.Contains("button", StringComparison.OrdinalIgnoreCase)))
            {
                score += 14;
                AddReason(reasons, "control marker");
            }

            if (isCSharp && !intent.Behavior && !intent.ThemeSystem)
            {
                score -= 30;
            }

            if (lowerPath.Contains("/services/", StringComparison.Ordinal) && !intent.LocalLlm)
            {
                score -= 14;
            }
        }

        if (intent.Button && SignalHasAny(signal, "button", "command", "send"))
        {
            score += 16;
            AddReason(reasons, "button/send");
        }

        if (intent.Behavior)
        {
            if (isCSharp)
            {
                score += 22;
                AddReason(reasons, "behavior code");
            }

            if (lowerPath.Contains("viewmodel", StringComparison.Ordinal) || lowerPath.Contains("/services/", StringComparison.Ordinal))
            {
                score += 12;
                AddReason(reasons, "logic owner");
            }
        }

        if (intent.LocalLlm)
        {
            if (lowerPath.Contains("localllm", StringComparison.Ordinal)
                || SignalHasAny(signal, "llm", "ollama", "model", "gpu", "token"))
            {
                score += 34;
                AddReason(reasons, "local LLM");
            }

            if (lowerPath.Contains("settings", StringComparison.Ordinal)
                || lowerPath.Contains("contextcontrolviewmodel", StringComparison.Ordinal))
            {
                score += 10;
                AddReason(reasons, "LLM caller/settings");
            }
        }

        if (intent.InstallOrProgress && SignalHasAny(signal, "install", "installer", "progress", "download", "pull", "transfer"))
        {
            score += 24;
            AddReason(reasons, "install/progress");
        }

        if (intent.PatchOrReplace)
        {
            if (SignalHasAny(signal, "patch", "replace", "go", "plan", "apply")
                || lowerPath.Contains("replace", StringComparison.Ordinal)
                || lowerPath.Contains("promptbuilder", StringComparison.Ordinal))
            {
                score += 30;
                AddReason(reasons, "patch/replace");
            }
        }
    }

    private static void ExpandRelatedFiles(
        List<ContextFileResolveCandidate> selected,
        IReadOnlyList<ContextFileResolveCandidate> allCandidates,
        RequestIntent intent)
    {
        if (!intent.Ui || selected.Count >= MaxRequestLines)
        {
            return;
        }

        if (intent.VisualStyle)
        {
            if (intent.ThemeSystem)
            {
                AddBestRelated(selected, allCandidates, candidate =>
                    candidate.RequestLine.EndsWith("WorkbenchThemeResources.cs", StringComparison.OrdinalIgnoreCase));
                AddBestRelated(selected, allCandidates, candidate =>
                    candidate.RequestLine.EndsWith("WorkbenchViewModel.cs", StringComparison.OrdinalIgnoreCase));
                AddBestRelated(selected, allCandidates, candidate =>
                    candidate.RequestLine.EndsWith("CodeEditor.Palette.cs", StringComparison.OrdinalIgnoreCase));
                AddBestRelated(selected, allCandidates, candidate =>
                    candidate.RequestLine.EndsWith("ThemeSettingsWindow.axaml", StringComparison.OrdinalIgnoreCase));
                AddBestRelated(selected, allCandidates, candidate =>
                    candidate.RequestLine.EndsWith("WorkbenchDesign.axaml", StringComparison.OrdinalIgnoreCase));
                return;
            }

            AddBestRelated(selected, allCandidates, candidate =>
                candidate.RequestLine.EndsWith(".axaml", StringComparison.OrdinalIgnoreCase)
                && candidate.RequestLine.Contains("/views/", StringComparison.OrdinalIgnoreCase));
            AddBestRelated(selected, allCandidates, candidate =>
                candidate.RequestLine.EndsWith(".axaml", StringComparison.OrdinalIgnoreCase)
                && (candidate.RequestLine.Contains("/styles/", StringComparison.OrdinalIgnoreCase)
                    || candidate.RequestLine.Contains("design", StringComparison.OrdinalIgnoreCase)));
            return;
        }

        if (intent.Behavior)
        {
            AddBestRelated(selected, allCandidates, candidate =>
                candidate.RequestLine.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                && candidate.RequestLine.Contains("ViewModel", StringComparison.OrdinalIgnoreCase));
        }
    }

    private static void AddBestRelated(
        ICollection<ContextFileResolveCandidate> selected,
        IReadOnlyList<ContextFileResolveCandidate> allCandidates,
        Func<ContextFileResolveCandidate, bool> predicate)
    {
        if (selected.Count >= MaxRequestLines)
        {
            return;
        }

        var existing = selected.Select(candidate => candidate.RequestLine).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var candidate = allCandidates
            .Where(predicate)
            .Where(candidate => !existing.Contains(candidate.RequestLine))
            .OrderByDescending(candidate => candidate.Score)
            .FirstOrDefault();
        if (candidate is not null && candidate.Score >= 28)
        {
            selected.Add(candidate);
        }
    }

    private static ContextFileResolveResult BuildFindResult(
        string userMessage,
        IReadOnlyList<string> queryWords,
        string confidence,
        string reason)
    {
        var lines = BuildFindLines(userMessage, queryWords).ToArray();
        var candidates = lines
            .Select(line => new ContextFileResolveCandidate(line, 0, [reason], false))
            .ToArray();
        return new ContextFileResolveResult(lines, confidence, "find", candidates, [reason]);
    }

    private static IEnumerable<string> BuildFindLines(string userMessage, IReadOnlyList<string> queryWords)
    {
        var words = queryWords.Where(IsUsefulFindWord).ToArray();
        for (var index = 0; index < words.Length - 1; index++)
        {
            var next = words[index + 1];
            if (next is "button" or "window" or "panel" or "pane" or "tab" or "view" or "style" or "theme"
                or "progress" or "installer" or "install" or "model" or "token")
            {
                yield return $"FIND: {words[index]} {next}";
                yield break;
            }
        }

        var word = words.FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(word))
        {
            yield return $"FIND: {word}";
            yield break;
        }

        var fallback = ExtractRawWords(userMessage).FirstOrDefault(rawWord => rawWord.Length >= 3) ?? "request";
        yield return $"FIND: {fallback}";
    }

    private static RequestIntent ResolveIntent(string userMessage, IReadOnlyList<string> queryWords)
    {
        var words = queryWords.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var lower = (userMessage ?? "").ToLowerInvariant();
        var visual = words.Any(IsVisualStyleWord)
            || lower.Contains("background", StringComparison.Ordinal)
            || lower.Contains("foreground", StringComparison.Ordinal);
        var themeSystem = visual
            && words.Overlaps(["theme", "themes", "skin", "skins", "palette", "palettes", "syntax", "appearance", "color", "colors", "colour", "colours"]);
        var ui = visual
            || words.Overlaps(["ui", "button", "prompt", "window", "panel", "pane", "tab", "style", "theme", "layout", "screen"]);
        var behavior = words.Overlaps(["click", "clicked", "command", "handler", "event", "run", "runs", "stuck", "fail", "fails", "fix", "logic", "state"]);
        var localLlm = words.Overlaps(["llm", "ollama", "model", "models", "gpu", "token", "tokens", "chat", "context"]);
        var installOrProgress = words.Overlaps(["install", "installer", "download", "progress", "pull", "speed", "transfer", "stuck"]);
        var patchOrReplace = words.Overlaps(["patch", "replace", "ccreplace", "go", "apply", "preview", "diff"]);
        var button = words.Contains("button") || words.Contains("send") || lower.Contains("send button", StringComparison.Ordinal);
        return new RequestIntent(visual, ui, behavior, localLlm, installOrProgress, patchOrReplace, button, themeSystem);
    }

    private static string[] ExtractQueryWords(string text)
    {
        return ExtractRawWords(text)
            .SelectMany(SplitCamelCaseWord)
            .Select(word => word.ToLowerInvariant())
            .Where(IsUsefulQueryWord)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IEnumerable<string> ExtractRawWords(string? text)
    {
        var current = new StringBuilder();
        foreach (var character in text ?? "")
        {
            if (char.IsLetterOrDigit(character) || character is '_' or '-')
            {
                current.Append(character);
                continue;
            }

            if (current.Length > 0)
            {
                yield return current.ToString();
                current.Clear();
            }
        }

        if (current.Length > 0)
        {
            yield return current.ToString();
        }
    }

    private static IEnumerable<string> SplitCamelCaseWord(string word)
    {
        var builder = new StringBuilder();
        var previousWasLower = false;
        foreach (var character in word ?? "")
        {
            if (char.IsUpper(character) && previousWasLower && builder.Length > 0)
            {
                yield return builder.ToString();
                builder.Clear();
            }

            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
                previousWasLower = char.IsLower(character);
                continue;
            }

            if (builder.Length > 0)
            {
                yield return builder.ToString();
                builder.Clear();
            }

            previousWasLower = false;
        }

        if (builder.Length > 0)
        {
            yield return builder.ToString();
        }
    }

    private static IEnumerable<string> TokenizeMany(IEnumerable<string> values)
    {
        foreach (var value in values)
        {
            foreach (var token in ExtractQueryWords(value))
            {
                yield return token;
            }
        }
    }

    private static bool IsUsefulQueryWord(string word)
    {
        var lower = (word ?? "").Trim().ToLowerInvariant();
        if (lower.Length < 2)
        {
            return false;
        }

        return lower is not ("make" or "change" or "set" or "the" or "and" or "for" or "with" or "from"
            or "into" or "that" or "this" or "should" or "please" or "to" or "a" or "an" or "of"
            or "semantic" or "map" or "attached" or "attachment" or "capsule" or "local");
    }

    private static bool IsUsefulFindWord(string word)
    {
        var lower = (word ?? "").Trim().ToLowerInvariant();
        return lower.Length >= 3
            && lower is not ("color" or "colour" or "red" or "blue" or "green" or "black" or "white"
                or "yellow" or "purple" or "orange" or "context" or "llm" or "model");
    }

    private static bool IsVisualStyleWord(string word)
    {
        return word.Equals("color", StringComparison.OrdinalIgnoreCase)
            || word.Equals("colour", StringComparison.OrdinalIgnoreCase)
            || word.Equals("red", StringComparison.OrdinalIgnoreCase)
            || word.Equals("green", StringComparison.OrdinalIgnoreCase)
            || word.Equals("blue", StringComparison.OrdinalIgnoreCase)
            || word.Equals("black", StringComparison.OrdinalIgnoreCase)
            || word.Equals("white", StringComparison.OrdinalIgnoreCase)
            || word.Equals("yellow", StringComparison.OrdinalIgnoreCase)
            || word.Equals("purple", StringComparison.OrdinalIgnoreCase)
            || word.Equals("orange", StringComparison.OrdinalIgnoreCase)
            || word.Equals("style", StringComparison.OrdinalIgnoreCase)
            || word.Equals("theme", StringComparison.OrdinalIgnoreCase)
            || word.Equals("background", StringComparison.OrdinalIgnoreCase)
            || word.Equals("foreground", StringComparison.OrdinalIgnoreCase)
            || word.Equals("border", StringComparison.OrdinalIgnoreCase);
    }

    private static bool SignalHasAny(ContextFileSignal signal, params string[] words)
    {
        var tokens = signal.PathTokens
            .Concat(signal.ContentTokens)
            .Concat(TokenizeMany(signal.Markers))
            .Concat(TokenizeMany(signal.Members))
            .Concat(TokenizeMany(signal.Strings))
            .Concat(TokenizeMany(signal.Bindings))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return words.Any(tokens.Contains);
    }

    private static bool IsNearAny(string word, IEnumerable<string> candidates)
    {
        if (word.Length < 4)
        {
            return false;
        }

        foreach (var candidate in candidates)
        {
            if (Math.Abs(candidate.Length - word.Length) > 2)
            {
                continue;
            }

            var distance = EditDistanceAtMost(word, candidate, word.Length <= 6 ? 1 : 2);
            if (distance)
            {
                return true;
            }
        }

        return false;
    }

    private static bool EditDistanceAtMost(string left, string right, int maxDistance)
    {
        if (string.Equals(left, right, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (Math.Abs(left.Length - right.Length) > maxDistance)
        {
            return false;
        }

        var previous = new int[right.Length + 1];
        var current = new int[right.Length + 1];
        for (var column = 0; column <= right.Length; column++)
        {
            previous[column] = column;
        }

        for (var row = 1; row <= left.Length; row++)
        {
            current[0] = row;
            var best = current[0];
            for (var column = 1; column <= right.Length; column++)
            {
                var cost = char.ToLowerInvariant(left[row - 1]) == char.ToLowerInvariant(right[column - 1]) ? 0 : 1;
                current[column] = Math.Min(
                    Math.Min(current[column - 1] + 1, previous[column] + 1),
                    previous[column - 1] + cost);
                best = Math.Min(best, current[column]);
            }

            if (best > maxDistance)
            {
                return false;
            }

            (previous, current) = (current, previous);
        }

        return previous[right.Length] <= maxDistance;
    }

    private static void AddReason(ICollection<string> reasons, string reason)
    {
        if (!string.IsNullOrWhiteSpace(reason) && !reasons.Contains(reason, StringComparer.OrdinalIgnoreCase))
        {
            reasons.Add(reason);
        }
    }

    private static string[] BuildResultReasons(IReadOnlyList<ContextFileResolveCandidate> selected, string headline)
    {
        return selected
            .SelectMany(candidate => candidate.Reasons.Select(reason => $"{candidate.RequestLine}: {reason}"))
            .Prepend(headline)
            .Take(10)
            .ToArray();
    }

    private sealed record RequestIntent(
        bool VisualStyle,
        bool Ui,
        bool Behavior,
        bool LocalLlm,
        bool InstallOrProgress,
        bool PatchOrReplace,
        bool Button,
        bool ThemeSystem);
}
