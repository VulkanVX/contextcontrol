// CC-DESC: Builds a compact semantic navigation map and structured file signals from a DIR export.

using System.Text;
using System.Text.RegularExpressions;

namespace ContextControl.Workbench.Services;

public sealed partial class ContextSemanticMapBuilder
{
    private const int MaxSignalFileReadCharacters = 240_000;
    private const int MaxHighSignalFiles = 90;
    private const int MaxPathIndexFiles = 280;
    private const int MaxContentTokens = 260;
    private const int MaxMarkers = 18;
    private const int MaxMembers = 42;
    private const int MaxStrings = 42;
    private const int MaxBindings = 42;

    private static readonly string[] IgnoredPathParts =
    [
        ".git",
        ".tmp",
        ".ccReplace.versions",
        "bin",
        "obj",
        "build",
        "vcpkg_installed",
        "vendor",
        "external",
        "third_party"
    ];

    private static readonly string[] IgnoredFileNames =
    [
        "cc_project_dir.md",
        "cc_semantic_map.md",
        "cc_code_export.md",
        "patch.txt",
        "mainwindow_axaml.txt",
        ".ccWorkbench.chat-history.json"
    ];

    private static readonly string[] SignalWords =
    [
        "prompt",
        "send",
        "button",
        "chat",
        "llm",
        "ollama",
        "model",
        "gpu",
        "dir",
        "cc",
        "go",
        "replace",
        "patch",
        "skillbook",
        "settings",
        "theme",
        "style",
        "window",
        "browser",
        "terminal",
        "attachment",
        "token",
        "context",
        "install",
        "installer",
        "progress"
    ];

    public async Task<string> BuildAsync(
        string projectRoot,
        string directoryExportText,
        CancellationToken cancellationToken = default)
    {
        var result = await BuildIndexAsync(projectRoot, directoryExportText, null, cancellationToken).ConfigureAwait(false);
        return result.SemanticMapText;
    }

    public async Task<ContextSemanticMapBuildResult> BuildIndexAsync(
        string projectRoot,
        string directoryExportText,
        ProjectFileRules? rules = null,
        CancellationToken cancellationToken = default)
    {
        var files = ExtractFilesFromDirExport(directoryExportText)
            .Select(NormalizePath)
            .Where(path => IsSemanticMapCandidate(projectRoot, path, rules))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (files.Count == 0)
        {
            files = EnumerateProjectFiles(projectRoot, rules)
                .Where(path => IsSemanticMapCandidate(projectRoot, path, rules))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        var signals = new List<ContextFileSignal>();
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            signals.Add(await BuildSignalAsync(projectRoot, file, cancellationToken).ConfigureAwait(false));
        }

        var index = new ContextSemanticIndex(projectRoot ?? "", signals);
        return new ContextSemanticMapBuildResult(BuildSemanticMap(index, files.Count), index);
    }

    private static string BuildSemanticMap(ContextSemanticIndex index, int indexedFileCount)
    {
        var highSignal = index.Files
            .Where(signal => signal.Score > 0)
            .OrderByDescending(signal => signal.Score)
            .ThenBy(signal => signal.Path, StringComparer.OrdinalIgnoreCase)
            .Take(MaxHighSignalFiles)
            .ToArray();

        var builder = new StringBuilder();
        builder.AppendLine("# ContextControl semantic map");
        builder.AppendLine();
        builder.AppendLine("Purpose: choose exact paths for cc.ps1 from project structure and local file signals.");
        builder.AppendLine("Rules: copy paths exactly; prefer concrete files over FIND when a likely owner is listed; use FIND only when no listed path is clearly relevant.");
        builder.AppendLine();
        builder.AppendLine($"Project root: {index.ProjectRoot}");
        builder.AppendLine($"Indexed files: {indexedFileCount:N0}");
        builder.AppendLine();

        AppendSection(builder, "Prompt / Chat / Local LLM", highSignal, "prompt", "chat", "llm", "ollama", "model", "attachment", "token", "context", "terminal", "progress");
        AppendSection(builder, "UI / Theme / Buttons", highSignal, "button", "style", "theme", "window", "send");
        AppendSection(builder, "CC Workflow / Replace", highSignal, "dir", "cc", "go", "replace", "patch");
        AppendSection(builder, "Settings / Skillbook / Browser", highSignal, "settings", "skillbook", "browser");

        builder.AppendLine("## High-signal path index");
        foreach (var signal in highSignal)
        {
            builder.Append("- ");
            builder.Append(signal.Path);
            builder.Append(" - ");
            builder.Append(signal.Role);
            if (signal.ContentTokens.Count > 0)
            {
                builder.Append("; signals: ");
                builder.Append(string.Join(", ", signal.ContentTokens.Take(8)));
            }

            if (signal.Markers.Count > 0)
            {
                builder.Append("; markers: ");
                builder.Append(string.Join(", ", signal.Markers.Take(6)));
            }

            if (signal.Bindings.Count > 0)
            {
                builder.Append("; bindings: ");
                builder.Append(string.Join(", ", signal.Bindings.Take(4)));
            }

            builder.AppendLine();
        }

        builder.AppendLine();
        builder.AppendLine("## Exact file paths");
        foreach (var path in index.Files.Select(signal => signal.Path).Take(MaxPathIndexFiles))
        {
            builder.AppendLine(path);
        }

        if (index.Files.Count > MaxPathIndexFiles)
        {
            builder.AppendLine($"... {index.Files.Count - MaxPathIndexFiles:N0} more file(s) omitted from compact path index.");
        }

        return builder.ToString().TrimEnd() + Environment.NewLine;
    }

    private static void AppendSection(StringBuilder builder, string title, IReadOnlyList<ContextFileSignal> signals, params string[] terms)
    {
        var items = signals
            .Where(signal => terms.Any(term => SignalContainsTerm(signal, term)))
            .Take(16)
            .ToArray();
        if (items.Length == 0)
        {
            return;
        }

        builder.AppendLine($"## {title}");
        foreach (var item in items)
        {
            builder.Append("- ");
            builder.Append(item.Path);
            builder.Append(" - ");
            builder.AppendLine(item.Role);
        }

        builder.AppendLine();
    }

    private static async Task<ContextFileSignal> BuildSignalAsync(string projectRoot, string relativePath, CancellationToken cancellationToken)
    {
        var pathText = NormalizePath(relativePath);
        var extension = Path.GetExtension(pathText).ToLowerInvariant();
        var combined = pathText;
        var xamlMarkers = new List<string>();
        var members = new List<string>();
        var strings = new List<string>();
        var bindings = new List<string>();

        var fullPath = ResolveProjectFile(projectRoot, pathText);
        if (!string.IsNullOrWhiteSpace(fullPath) && File.Exists(fullPath) && IsTextSignalFile(pathText))
        {
            try
            {
                var text = await File.ReadAllTextAsync(fullPath, cancellationToken).ConfigureAwait(false);
                if (text.Length > MaxSignalFileReadCharacters)
                {
                    text = text[..MaxSignalFileReadCharacters];
                }

                combined += "\n" + text;
                xamlMarkers.AddRange(ExtractXamlMarkers(text));
                members.AddRange(ExtractMembers(text));
                strings.AddRange(ExtractStrings(text));
                bindings.AddRange(ExtractBindings(text));
            }
            catch
            {
                xamlMarkers.Add("read skipped");
            }
        }

        var pathTokens = DistinctOrdered(ExtractSemanticTokens(pathText).Where(IsUsefulSemanticToken), 80);
        var contentTokens = DistinctOrdered(ExtractSemanticTokens(combined).Where(IsUsefulSemanticToken), MaxContentTokens);
        var markers = DistinctOrdered(xamlMarkers.Concat(bindings).Concat(members).Where(IsUsefulMarker), MaxMarkers);
        var cleanMembers = DistinctOrdered(members.Where(IsUsefulMarker), MaxMembers);
        var cleanStrings = DistinctOrdered(strings.Where(IsUsefulString), MaxStrings);
        var cleanBindings = DistinctOrdered(bindings.Where(value => !string.IsNullOrWhiteSpace(value)), MaxBindings);

        var score = SignalWords.Count(word => combined.Contains(word, StringComparison.OrdinalIgnoreCase));
        score += markers.Length * 2;
        score += cleanBindings.Length > 0 ? 4 : 0;
        score += pathText.Contains("MainWindow", StringComparison.OrdinalIgnoreCase) ? 8 : 0;
        score += pathText.Contains("WorkbenchDesign", StringComparison.OrdinalIgnoreCase) ? 8 : 0;
        score += pathText.Contains("ContextControlViewModel", StringComparison.OrdinalIgnoreCase) ? 8 : 0;
        score += pathText.Contains("ContextCapsuleBuilder", StringComparison.OrdinalIgnoreCase) ? 6 : 0;
        score += pathText.Contains("LocalLlm", StringComparison.OrdinalIgnoreCase) ? 5 : 0;

        var signal = new ContextFileSignal(
            pathText,
            extension,
            "",
            score,
            pathTokens,
            contentTokens,
            markers,
            cleanMembers,
            cleanStrings,
            cleanBindings);

        return signal with { Role = BuildRole(signal) };
    }

    private static string BuildRole(ContextFileSignal signal)
    {
        var name = Path.GetFileName(signal.Path);
        var role = signal.Extension.ToLowerInvariant() switch
        {
            ".axaml" => "Avalonia UI layout/resources",
            ".xaml" => "XAML UI layout/resources",
            ".cs" => "C# workbench logic",
            ".ps1" => "ContextControl PowerShell workflow",
            ".md" => "documentation/instructions",
            ".csproj" => "project/build configuration",
            _ => "project file"
        };

        if (name.Equals("MainWindow.axaml", StringComparison.OrdinalIgnoreCase))
        {
            return "main workbench layout; prompt/chat/browser panes and command buttons";
        }

        if (name.Equals("WorkbenchDesign.axaml", StringComparison.OrdinalIgnoreCase))
        {
            return "global workbench styles and button/control classes";
        }

        if (name.Equals("WorkbenchThemeResources.cs", StringComparison.OrdinalIgnoreCase))
        {
            return "theme palette/resource owner for workbench colors and brushes";
        }

        if (name.Equals("WorkbenchViewModel.cs", StringComparison.OrdinalIgnoreCase))
        {
            return "workbench settings, theme options, and selected appearance state";
        }

        if (name.Equals("CodeEditor.Palette.cs", StringComparison.OrdinalIgnoreCase))
        {
            return "code editor and minimap syntax palette colors";
        }

        if (name.Equals("ThemeSettingsWindow.axaml", StringComparison.OrdinalIgnoreCase))
        {
            return "settings appearance page and theme picker UI";
        }

        if (name.Equals("ContextControlViewModel.cs", StringComparison.OrdinalIgnoreCase))
        {
            return "DIR/CC/GO/send commands, prompt state, local chat workflow";
        }

        if (signal.Markers.Any(marker => marker.Contains("cc-prompt-send", StringComparison.OrdinalIgnoreCase))
            || signal.Bindings.Any(binding => binding.Contains("SendCommand", StringComparison.OrdinalIgnoreCase)))
        {
            return "prompt Send button styling, binding, or layout";
        }

        if (SignalContainsTerm(signal, "llm") || SignalContainsTerm(signal, "ollama") || SignalContainsTerm(signal, "model"))
        {
            return "local LLM/Ollama workflow";
        }

        if (SignalContainsTerm(signal, "replace") || SignalContainsTerm(signal, "patch"))
        {
            return "patch/replace workflow";
        }

        if (SignalContainsTerm(signal, "settings") || SignalContainsTerm(signal, "skillbook"))
        {
            return "settings/skillbook workflow";
        }

        return role;
    }

    private static IEnumerable<string> ExtractFilesFromDirExport(string directoryExportText)
    {
        var manifest = ContextDirManifestParser.Parse(directoryExportText);
        if (manifest.Files.Count > 0)
        {
            foreach (var file in manifest.Files)
            {
                yield return file.Path;
            }

            if (manifest.IsV2)
            {
                yield break;
            }
        }

        var stack = new Dictionary<int, string>();
        foreach (var rawLine in (directoryExportText ?? "").Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n'))
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
            yield return string.Join("/", parts);
        }
    }

    private static IEnumerable<string> EnumerateProjectFiles(string projectRoot, ProjectFileRules? rules)
    {
        if (string.IsNullOrWhiteSpace(projectRoot) || !Directory.Exists(projectRoot))
        {
            yield break;
        }

        var root = new DirectoryInfo(projectRoot);
        var pending = new Stack<DirectoryInfo>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            IEnumerable<DirectoryInfo> childDirectories;
            try
            {
                childDirectories = directory.EnumerateDirectories();
            }
            catch
            {
                childDirectories = [];
            }

            foreach (var child in childDirectories)
            {
                var relative = NormalizePath(Path.GetRelativePath(projectRoot, child.FullName));
                if (IgnoredPathParts.Contains(child.Name, StringComparer.OrdinalIgnoreCase)
                    || rules?.ShouldSkipDirectory(child.Name, relative) == true)
                {
                    continue;
                }

                pending.Push(child);
            }

            IEnumerable<FileInfo> files;
            try
            {
                files = directory.EnumerateFiles();
            }
            catch
            {
                files = [];
            }

            foreach (var file in files)
            {
                var relative = NormalizePath(Path.GetRelativePath(projectRoot, file.FullName));
                if (rules is not null && !rules.GetTrackDecision(file.FullName).ShouldTrack)
                {
                    continue;
                }

                yield return relative;
            }
        }
    }

    private static bool IsSemanticMapCandidate(string projectRoot, string relativePath, ProjectFileRules? rules)
    {
        var normalized = NormalizePath(relativePath);
        var fileName = Path.GetFileName(normalized);
        if (IgnoredFileNames.Contains(fileName, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        var parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Any(part => IgnoredPathParts.Contains(part, StringComparer.OrdinalIgnoreCase)))
        {
            return false;
        }

        if (rules is null)
        {
            return true;
        }

        var fullPath = ResolveProjectFile(projectRoot, normalized);
        if (!string.IsNullOrWhiteSpace(fullPath) && File.Exists(fullPath))
        {
            return rules.GetTrackDecision(fullPath).ShouldTrack;
        }

        return rules.ShouldTrackFile(normalized, fileName, Path.GetExtension(normalized));
    }

    private static string ResolveProjectFile(string projectRoot, string relativePath)
    {
        try
        {
            var root = Path.GetFullPath(projectRoot);
            var fullPath = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
            var rootWithSeparator = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return fullPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase) ? fullPath : "";
        }
        catch
        {
            return "";
        }
    }

    private static bool IsTextSignalFile(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() is ".cs" or ".axaml" or ".xaml" or ".ps1" or ".md"
            or ".csproj" or ".json" or ".txt" or ".xml" or ".props" or ".targets" or ".cmd"
            or ".cpp" or ".cc" or ".cxx" or ".c" or ".h" or ".hpp" or ".hxx" or ".inl"
            or ".glsl" or ".vert" or ".frag" or ".comp" or ".hlsl"
            or ".ts" or ".tsx" or ".js" or ".jsx" or ".py" or ".rs" or ".toml" or ".yaml" or ".yml";
    }

    private static bool SignalContainsTerm(ContextFileSignal signal, string term)
    {
        return signal.PathTokens.Contains(term, StringComparer.OrdinalIgnoreCase)
            || signal.ContentTokens.Contains(term, StringComparer.OrdinalIgnoreCase)
            || signal.Markers.Any(marker => marker.Contains(term, StringComparison.OrdinalIgnoreCase))
            || signal.Members.Any(member => member.Contains(term, StringComparison.OrdinalIgnoreCase))
            || signal.Strings.Any(value => value.Contains(term, StringComparison.OrdinalIgnoreCase))
            || signal.Bindings.Any(binding => binding.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<string> ExtractXamlMarkers(string text)
    {
        foreach (Match match in XamlSignalRegex().Matches(text))
        {
            var value = match.Groups["name"].Value.Trim();
            if (!string.IsNullOrWhiteSpace(value))
            {
                yield return value;
            }
        }
    }

    private static IEnumerable<string> ExtractMembers(string text)
    {
        foreach (Match match in CSharpTypeRegex().Matches(text))
        {
            var value = match.Groups["name"].Value.Trim();
            if (!string.IsNullOrWhiteSpace(value))
            {
                yield return value;
            }
        }

        foreach (Match match in CSharpMemberRegex().Matches(text))
        {
            var value = match.Groups["name"].Value.Trim();
            if (!string.IsNullOrWhiteSpace(value))
            {
                yield return value;
            }
        }
    }

    private static IEnumerable<string> ExtractStrings(string text)
    {
        foreach (Match match in StringLiteralRegex().Matches(text))
        {
            var value = match.Groups["value"].Value.Trim();
            if (IsUsefulString(value))
            {
                yield return value;
            }
        }
    }

    private static IEnumerable<string> ExtractBindings(string text)
    {
        foreach (Match match in BindingRegex().Matches(text))
        {
            var value = match.Groups["name"].Value.Trim();
            if (!string.IsNullOrWhiteSpace(value))
            {
                yield return value;
            }
        }
    }

    private static bool IsUsefulMarker(string marker)
    {
        return marker.Contains("prompt", StringComparison.OrdinalIgnoreCase)
            || marker.Contains("send", StringComparison.OrdinalIgnoreCase)
            || marker.Contains("chat", StringComparison.OrdinalIgnoreCase)
            || marker.Contains("llm", StringComparison.OrdinalIgnoreCase)
            || marker.Contains("model", StringComparison.OrdinalIgnoreCase)
            || marker.Contains("ollama", StringComparison.OrdinalIgnoreCase)
            || marker.Contains("replace", StringComparison.OrdinalIgnoreCase)
            || marker.Contains("patch", StringComparison.OrdinalIgnoreCase)
            || marker.Contains("progress", StringComparison.OrdinalIgnoreCase)
            || marker.Contains("install", StringComparison.OrdinalIgnoreCase)
            || marker.Contains("settings", StringComparison.OrdinalIgnoreCase)
            || marker.Contains("skillbook", StringComparison.OrdinalIgnoreCase)
            || marker.Contains("cc-", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsUsefulString(string value)
    {
        var clean = (value ?? "").Trim();
        return clean.Length is >= 3 and <= 100
            && !clean.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            && !clean.Contains("\\", StringComparison.Ordinal);
    }

    private static bool IsUsefulSemanticToken(string token)
    {
        var lower = (token ?? "").Trim().ToLowerInvariant();
        if (lower.Length < 2)
        {
            return false;
        }

        return lower is not ("the" or "and" or "for" or "with" or "this" or "that" or "true" or "false"
            or "null" or "new" or "get" or "set" or "var" or "public" or "private" or "static" or "readonly"
            or "string" or "object" or "return" or "using" or "namespace");
    }

    private static string[] DistinctOrdered(IEnumerable<string> values, int max)
    {
        return values
            .Select(value => (value ?? "").Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(max)
            .ToArray();
    }

    private static IEnumerable<string> ExtractSemanticTokens(string text)
    {
        var builder = new StringBuilder();
        var previousWasLower = false;
        foreach (var character in text ?? "")
        {
            if (char.IsUpper(character) && previousWasLower && builder.Length > 0)
            {
                yield return builder.ToString().ToLowerInvariant();
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
                yield return builder.ToString().ToLowerInvariant();
                builder.Clear();
            }

            previousWasLower = false;
        }

        if (builder.Length > 0)
        {
            yield return builder.ToString().ToLowerInvariant();
        }
    }

    private static string NormalizePath(string path)
    {
        return (path ?? "").Replace('\\', '/').Trim().TrimStart('/');
    }

    [GeneratedRegex("^(?<prefix>(?:(?:│| )   )*)(?:├──|└──) (?<name>.+)$")]
    private static partial Regex TreeLineRegex();

    [GeneratedRegex("(?i)(?:Classes|Selector|Content|ToolTip\\.Tip|Name|x:Name)\\s*=\\s*\"(?<name>[^\"]+)\"")]
    private static partial Regex XamlSignalRegex();

    [GeneratedRegex("\\b(?:class|record|struct|interface|enum)\\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)")]
    private static partial Regex CSharpTypeRegex();

    [GeneratedRegex("\\b(?<name>[A-Za-z_][A-Za-z0-9_]*(?:Command|Label|Text|Async|Model|Builder|Service|ViewModel|Window|Button|Prompt|Send|Token|Attachment|Progress|Install|Installer|Settings|Skillbook))\\b")]
    private static partial Regex CSharpMemberRegex();

    [GeneratedRegex("\"(?<value>[^\"\\r\\n]{3,100})\"")]
    private static partial Regex StringLiteralRegex();

    [GeneratedRegex("\\{Binding\\s+(?<name>[A-Za-z_][A-Za-z0-9_.]*)")]
    private static partial Regex BindingRegex();
}
