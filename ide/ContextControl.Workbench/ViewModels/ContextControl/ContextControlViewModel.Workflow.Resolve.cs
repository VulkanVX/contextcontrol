// CC-DESC: Extracted ContextControlViewModel system slice.
// CC-DESC: Owns Context Control workflow state, prompt bar state, and DIR/CC/GO commands.

using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows.Input;
using Avalonia.Collections;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using ContextControl.Workbench.Services;

namespace ContextControl.Workbench.ViewModels;

public sealed partial class ContextControlViewModel
{
    private ContextCapsulePhase ResolveCapsulePhase(string message)
    {
        var hasPatch = HasIncludedAttachmentKind("patch");
        var hasCode = HasIncludedAttachmentKind("code");
        var hasDir = HasIncludedAttachmentKind("dir");
        var wantsPatchReview = message.Contains("review", StringComparison.OrdinalIgnoreCase)
            || message.Contains("repair", StringComparison.OrdinalIgnoreCase)
            || message.Contains("fix patch", StringComparison.OrdinalIgnoreCase);

        if (hasPatch && wantsPatchReview)
        {
            return ContextCapsulePhase.PatchReview;
        }

        if (hasCode)
        {
            return WantsPatchWrite(message)
                ? ContextCapsulePhase.PatchWrite
                : ContextCapsulePhase.SourceAudit;
        }

        if (hasDir)
        {
            return ContextCapsulePhase.FileRequest;
        }

        return ContextCapsulePhase.Chat;
    }

    private static bool WantsPatchWrite(string message)
    {
        var text = (message ?? "").Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return text.Contains("CC-REPLACE", StringComparison.OrdinalIgnoreCase)
            || text.Contains("patch", StringComparison.OrdinalIgnoreCase)
            || text.Contains("implement", StringComparison.OrdinalIgnoreCase)
            || text.Contains("modify", StringComparison.OrdinalIgnoreCase)
            || text.Contains("change", StringComparison.OrdinalIgnoreCase)
            || text.Contains("fix", StringComparison.OrdinalIgnoreCase)
            || text.Contains("add ", StringComparison.OrdinalIgnoreCase)
            || text.Contains("remove ", StringComparison.OrdinalIgnoreCase)
            || text.Contains("replace", StringComparison.OrdinalIgnoreCase)
            || text.Contains("update", StringComparison.OrdinalIgnoreCase);
    }

    private bool HasIncludedAttachmentKind(string kind)
    {
        return Attachments.Any(attachment =>
            attachment.IncludeInPrompt
            && attachment.Kind.Equals(kind, StringComparison.OrdinalIgnoreCase));
    }

    private string ResolvePatchTaskMessage(string message)
    {
        if (!IsLikelyCcRequestList(message))
        {
            RememberWorkflowTask(message);
            return message;
        }

        var savedTask = ResolveWorkflowTaskText();
        return string.IsNullOrWhiteSpace(savedTask)
            ? message
            : savedTask;
    }

    private string ResolveWorkflowTaskText()
    {
        if (SelectedChatSession is { } session && !string.IsNullOrWhiteSpace(session.WorkflowTaskText))
        {
            return session.WorkflowTaskText;
        }

        return _lastUserRequest;
    }

    private void RememberWorkflowTask(string text, bool save = true)
    {
        var clean = NormalizeWorkflowTaskTextForMemory(text);
        if (!IsMeaningfulTaskPrompt(clean))
        {
            return;
        }

        _lastUserRequest = clean;
        SelectedChatSession?.SetWorkflowTaskText(clean);
        if (save)
        {
            SaveChatHistory();
        }
    }

    private static string NormalizeWorkflowTaskTextForMemory(string text)
    {
        var clean = (text ?? "").Trim();
        if (!clean.StartsWith("Task:", StringComparison.OrdinalIgnoreCase))
        {
            return clean;
        }

        var body = clean["Task:".Length..].Trim();
        var instructionSeparator = body.IndexOf($"{Environment.NewLine}{Environment.NewLine}", StringComparison.Ordinal);
        if (instructionSeparator < 0)
        {
            instructionSeparator = body.IndexOf("\n\n", StringComparison.Ordinal);
        }

        if (instructionSeparator >= 0)
        {
            body = body[..instructionSeparator];
        }

        return body.Trim();
    }

    private static bool IsLikelyCcRequestList(string text)
    {
        var lines = (text ?? "")
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0)
        {
            return false;
        }

        var requestLike = lines.Count(line =>
        {
            var clean = ContextPromptBuilder.NormalizeCodeExportRequestLine(line);
            return clean.Equals("END", StringComparison.OrdinalIgnoreCase)
                || ContextPromptBuilder.IsCodeExportRequestLine(clean);
        });
        var realRequestLines = lines.Count(line =>
        {
            var clean = ContextPromptBuilder.NormalizeCodeExportRequestLine(line);
            return !clean.Equals("END", StringComparison.OrdinalIgnoreCase)
                && ContextPromptBuilder.IsCodeExportRequestLine(clean);
        });
        return realRequestLines > 0 && requestLike >= Math.Max(1, lines.Length - 1);
    }

    private static bool IsMeaningfulTaskPrompt(string text)
    {
        return !IsLikelyCcRequestList(text)
            && !LooksLikeAttachmentDiagnostic(text)
            && !LooksLikeContextOnlyPrompt(text);
    }

    private static bool LooksLikeContextOnlyPrompt(string text)
    {
        var words = ExtractSearchWords(text).Select(word => word.ToLowerInvariant()).ToArray();
        if (words.Length == 0)
        {
            return true;
        }

        var taskWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "add", "apply", "build", "change", "create", "delete", "edit", "fix", "implement",
            "make", "modify", "move", "red", "remove", "replace", "review", "set", "update"
        };
        if (words.Any(taskWords.Contains))
        {
            return false;
        }

        return words.All(word => word is "attach" or "attached" or "attachment" or "context" or "dir" or "file"
            or "here" or "map" or "semantic" or "sent" or "this" or "with");
    }

    private void AppendFileRequestFallbackIfNeeded(ChatSessionViewModel targetSession, LocalLlmChatMessageViewModel assistant, string userMessage)
    {
        var requestSnippet = assistant.Snippets.LastOrDefault(snippet => snippet.IsRequestList);
        if (requestSnippet is not null && !IsFindOnlyRequestList(requestSnippet.Text))
        {
            return;
        }

        var fallbackSource = SelectFallbackSourceText(userMessage);
        var fallback = BuildFileRequestFallback(fallbackSource, out var fallbackKind);
        if (requestSnippet is not null
            && (!fallbackKind.Equals("semantic path", StringComparison.OrdinalIgnoreCase)
                || RequestListsMatch(requestSnippet.Text, fallback)))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(fallback))
        {
            PhaseTitle = "No file request";
            PhaseDetail = "The model returned no CC request lines. Try a more specific file, UI label, or FIND term.";
            return;
        }

        PhaseTitle = "File request fallback";
        if (ReferenceEquals(SelectedChatSession, targetSession))
        {
            PromptText = EnsureEndsWithEnd(fallback);
            IsPromptOpen = true;
            PreserveCodexOrUseContextPromptMode();
        }
        PhaseDetail = fallbackKind.Equals("semantic path", StringComparison.OrdinalIgnoreCase)
            ? "Semantic fallback loaded for the request. Select that chat, then press Send or CC to export those files."
            : "Discovery fallback loaded for the request. Select that chat, then press Send or CC to run FIND.";
        AppendTerminalOutput(requestSnippet is null
            ? $"File request {fallbackKind} fallback loaded into prompt because the model returned no usable paths."
            : $"File request {fallbackKind} fallback loaded into prompt because the model returned only FIND discovery lines.");
    }

    private static bool IsFindOnlyRequestList(string text)
    {
        var lines = (text ?? "")
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Where(line => !line.Equals("END", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return lines.Length > 0 && lines.All(line => line.StartsWith("FIND:", StringComparison.OrdinalIgnoreCase));
    }

    private string SelectFallbackSourceText(string userMessage)
    {
        var savedTask = ResolveWorkflowTaskText();
        if ((LooksLikeAttachmentDiagnostic(userMessage) || LooksLikeContextOnlyPrompt(userMessage)) && !string.IsNullOrWhiteSpace(savedTask))
        {
            return savedTask;
        }

        var currentFallback = ResolveFileRequestFromIndex(userMessage);
        if (currentFallback.HasRequestLines)
        {
            return userMessage;
        }

        return string.IsNullOrWhiteSpace(savedTask)
            ? userMessage
            : savedTask;
    }

    private static bool LooksLikeAttachmentDiagnostic(string text)
    {
        var lower = (text ?? "").ToLowerInvariant();
        return (lower.Contains("receive", StringComparison.Ordinal) || lower.Contains("received", StringComparison.Ordinal))
            && (lower.Contains("attachment", StringComparison.Ordinal) || lower.Contains("context", StringComparison.Ordinal));
    }

    private static bool RequestListsMatch(string left, string right)
    {
        static string Normalize(string text)
        {
            return string.Join(
                "\n",
                (text ?? "")
                    .Replace("\r\n", "\n", StringComparison.Ordinal)
                    .Replace('\r', '\n')
                    .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                    .Select(ContextPromptBuilder.NormalizeCodeExportRequestLine));
        }

        return string.Equals(Normalize(left), Normalize(right), StringComparison.OrdinalIgnoreCase);
    }

    private string BuildFileRequestFallback(string userMessage, out string fallbackKind)
    {
        var resolved = ResolveFileRequestFromIndex(userMessage);
        if (resolved.HasRequestLines)
        {
            fallbackKind = resolved.UsesFindTerms ? "discovery" : "semantic path";
            return resolved.RequestText;
        }

        fallbackKind = "discovery";
        return "";
    }

    private static IEnumerable<string> ExtractSearchWords(string text)
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

    private static IReadOnlyList<string> ExtractMatchedFileRequestsFromFindExport(string exportText)
    {
        var results = new List<string>();
        var inMatchedFiles = false;
        foreach (var rawLine in (exportText ?? "")
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Equals("Matched code files:", StringComparison.OrdinalIgnoreCase))
            {
                inMatchedFiles = true;
                continue;
            }

            if (!inMatchedFiles)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                inMatchedFiles = false;
                continue;
            }

            if (!line.StartsWith("- ", StringComparison.Ordinal))
            {
                continue;
            }

            var requestLine = ContextPromptBuilder.NormalizeCodeExportRequestLine(line[2..]);
            if (ContextPromptBuilder.IsCodeExportRequestLine(requestLine)
                && !requestLine.StartsWith("FIND:", StringComparison.OrdinalIgnoreCase))
            {
                results.Add(requestLine);
            }
        }

        return results.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string BuildFindDiscoveryChatText(IReadOnlyList<string> matchedFiles)
    {
        var builder = new StringBuilder();
        builder.AppendLine("FIND discovery returned candidate files only; source bodies were not exported yet.");
        builder.AppendLine("Review these exact request lines, then press CC again:");
        builder.AppendLine();
        builder.AppendLine("```cc-request");
        foreach (var file in matchedFiles)
        {
            builder.AppendLine(file);
        }

        builder.AppendLine("END");
        builder.AppendLine("```");
        return builder.ToString().TrimEnd();
    }

    private IReadOnlyList<string> FindMissingRequestPaths(IReadOnlyList<string> requestLines)
    {
        var root = ResolveEffectiveProjectRootPath();
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            return [];
        }

        var missing = new List<string>();
        foreach (var line in requestLines)
        {
            var requestPath = ExtractRequestPathForValidation(line);
            if (string.IsNullOrWhiteSpace(requestPath))
            {
                continue;
            }

            if (!RequestPathExists(root, requestPath))
            {
                missing.Add(requestPath);
            }
        }

        return missing.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private async Task<ContextDirManifest> LoadCurrentDirManifestAsync()
    {
        try
        {
            if (!File.Exists(_processService.DirectoryExportPath))
            {
                return ContextDirManifest.Empty;
            }

            var text = await _processService.ReadOutputFileAsync(_processService.DirectoryExportPath);
            return ContextDirManifestParser.Parse(text);
        }
        catch (Exception ex)
        {
            Log("warn", $"DIR manifest validation skipped: {ex.Message}");
            return ContextDirManifest.Empty;
        }
    }

    private string ResolveEffectiveProjectRootPath()
    {
        return string.IsNullOrWhiteSpace(ActiveProjectRoot)
            ? _processService.ContextRoot
            : ActiveProjectRoot;
    }

    private static string BuildProjectScopeKey(string projectRoot)
    {
        if (string.IsNullOrWhiteSpace(projectRoot))
        {
            return "default";
        }

        try
        {
            return Path.GetFullPath(projectRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return projectRoot.Trim();
        }
    }

    private static string ExtractRequestPathForValidation(string requestLine)
    {
        var clean = ContextPromptBuilder.NormalizeCodeExportRequestLine(requestLine);
        if (string.IsNullOrWhiteSpace(clean)
            || clean.StartsWith("FIND:", StringComparison.OrdinalIgnoreCase)
            || clean.StartsWith("FUNC:", StringComparison.OrdinalIgnoreCase)
            || clean.StartsWith("SYMBOL:", StringComparison.OrdinalIgnoreCase))
        {
            return "";
        }

        if (clean.StartsWith("FUNCTION ", StringComparison.OrdinalIgnoreCase))
        {
            var body = clean["FUNCTION ".Length..].Trim();
            var separator = body.IndexOf(" :: ", StringComparison.Ordinal);
            return separator > 0 ? body[..separator].Trim() : "";
        }

        return clean;
    }

    private static bool RequestPathExists(string projectRoot, string requestPath)
    {
        try
        {
            if (requestPath.Contains('*', StringComparison.Ordinal) || requestPath.Contains('?', StringComparison.Ordinal))
            {
                return RequestWildcardPathExists(projectRoot, requestPath);
            }

            var fullPath = ResolveProjectRelativePath(projectRoot, requestPath);
            return !string.IsNullOrWhiteSpace(fullPath) && File.Exists(fullPath);
        }
        catch
        {
            return false;
        }
    }

    private static bool RequestWildcardPathExists(string projectRoot, string requestPath)
    {
        var normalized = requestPath.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        var directoryPart = Path.GetDirectoryName(normalized) ?? "";
        var pattern = Path.GetFileName(normalized);
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return false;
        }

        var searchRoot = string.IsNullOrWhiteSpace(directoryPart)
            ? Path.GetFullPath(projectRoot)
            : ResolveProjectRelativePath(projectRoot, directoryPart);
        if (string.IsNullOrWhiteSpace(searchRoot) || !Directory.Exists(searchRoot))
        {
            return false;
        }

        return Directory.EnumerateFiles(searchRoot, pattern, SearchOption.TopDirectoryOnly).Any();
    }

    private static string ResolveProjectRelativePath(string projectRoot, string relativePath)
    {
        var root = Path.GetFullPath(projectRoot);
        var normalizedRelative = relativePath.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        var fullPath = Path.GetFullPath(Path.Combine(root, normalizedRelative));
        var rootWithSeparator = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase)
            ? fullPath
            : "";
    }

}
