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
    private void ToggleAttachmentInclude(ContextControlAttachmentViewModel? attachment)
    {
        if (attachment is null)
        {
            return;
        }

        attachment.IncludeInPrompt = !attachment.IncludeInPrompt;
        NotifyAttachmentStateChanged();
    }

    private async Task CopySnippetAsync(ChatSnippetViewModel? snippet)
    {
        if (snippet is null || _clipboardWriter is null)
        {
            return;
        }

        await _clipboardWriter(snippet.Text);
        PhaseTitle = "Snippet copied";
        PhaseDetail = snippet.Title;
        Log("info", $"Copied snippet: {snippet.Title}");
    }

    private async Task SaveSnippetAsAsync(ChatSnippetViewModel? snippet)
    {
        if (snippet?.CanSaveAsFile != true)
        {
            return;
        }

        if (_snippetFileSaver is null)
        {
            PhaseTitle = "Save unavailable";
            PhaseDetail = "The window file picker is not ready yet.";
            return;
        }

        try
        {
            var path = await _snippetFileSaver(snippet);
            if (string.IsNullOrWhiteSpace(path))
            {
                PhaseTitle = "Save cancelled";
                PhaseDetail = snippet.Title;
                return;
            }

            LastExportPath = path;
            PhaseTitle = "Snippet saved";
            PhaseDetail = $"{snippet.Title} saved as {Path.GetFileName(path)}.";
            AppendTerminalOutput($"Snippet saved as file: {path}");
            Log("ok", $"Saved snippet as {Path.GetFileName(path)}");
        }
        catch (Exception ex)
        {
            PhaseTitle = "Save failed";
            PhaseDetail = ex.Message;
            AppendTerminalOutput($"Snippet save failed: {ex.Message}");
            Log("error", ex.Message);
        }
    }

    private async Task CreateProjectFromMessageAsync(LocalLlmChatMessageViewModel? message)
    {
        if (message?.CanCreateProject != true)
        {
            return;
        }

        var snippets = message.Snippets
            .Where(snippet => snippet.CanCreateProjectFile)
            .ToArray();
        if (snippets.Length == 0)
        {
            return;
        }

        try
        {
            var projectName = BuildGeneratedProjectName(message, snippets);
            var projectRoot = GetUniqueGeneratedProjectPath(projectName);
            Directory.CreateDirectory(projectRoot);

            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var snippet in snippets)
            {
                var fileName = GetUniqueProjectFileName(snippet.SuggestedFileName, usedNames);
                var path = Path.Combine(projectRoot, fileName);
                await File.WriteAllTextAsync(path, snippet.Text, new UTF8Encoding(false));
            }

            LastExportPath = projectRoot;
            PhaseTitle = "Project created";
            PhaseDetail = $"{snippets.Length:N0} file(s) written to {projectRoot}.";
            AppendTerminalOutput($"Created project from chat response: {projectRoot}");
            Log("ok", $"Created project: {Path.GetFileName(projectRoot)}");
            OpenFolder(projectRoot);
        }
        catch (Exception ex)
        {
            PhaseTitle = "Project create failed";
            PhaseDetail = ex.Message;
            AppendTerminalOutput($"Project create failed: {ex.Message}");
            Log("error", ex.Message);
        }
    }

    private string GetUniqueGeneratedProjectPath(string projectName)
    {
        var root = Path.Combine(_settings.ContextControlRoot, ".ccWorkbench.generated-projects");
        Directory.CreateDirectory(root);

        var candidate = Path.Combine(root, projectName);
        if (!Directory.Exists(candidate) && !File.Exists(candidate))
        {
            return candidate;
        }

        for (var index = 2; index < 1000; index++)
        {
            candidate = Path.Combine(root, $"{projectName}-{index}");
            if (!Directory.Exists(candidate) && !File.Exists(candidate))
            {
                return candidate;
            }
        }

        return Path.Combine(root, $"{projectName}-{DateTime.Now:HHmmss}");
    }

    private static string BuildGeneratedProjectName(LocalLlmChatMessageViewModel message, IReadOnlyList<ChatSnippetViewModel> snippets)
    {
        var visible = message.VisibleText ?? "";
        var seed = visible
            .Replace("\r\n", " ", StringComparison.Ordinal)
            .Replace('\r', ' ')
            .Replace('\n', ' ');

        var lowerSeed = seed.ToLowerInvariant();
        var baseName = lowerSeed.Contains("snake", StringComparison.Ordinal)
            ? "snake-game"
            : snippets.Count == 1
                ? Path.GetFileNameWithoutExtension(snippets[0].SuggestedFileName)
                : "chat-project";

        return CleanProjectName(string.IsNullOrWhiteSpace(baseName) ? "chat-project" : baseName);
    }

    private static string CleanProjectName(string name)
    {
        var clean = new string((name ?? "")
            .Trim()
            .ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')
            .ToArray());
        clean = string.Join('-', clean.Split('-', StringSplitOptions.RemoveEmptyEntries));
        return string.IsNullOrWhiteSpace(clean) ? "chat-project" : clean;
    }

    private static string GetUniqueProjectFileName(string suggestedFileName, ISet<string> usedNames)
    {
        var clean = CleanProjectFileName(suggestedFileName);
        var extension = Path.GetExtension(clean);
        var stem = Path.GetFileNameWithoutExtension(clean);
        var candidate = clean;
        for (var index = 2; usedNames.Contains(candidate); index++)
        {
            candidate = string.IsNullOrWhiteSpace(extension)
                ? $"{stem}-{index}"
                : $"{stem}-{index}{extension}";
        }

        usedNames.Add(candidate);
        return candidate;
    }

    private static string CleanProjectFileName(string fileName)
    {
        var clean = string.IsNullOrWhiteSpace(fileName) ? "snippet.txt" : fileName.Trim().Trim('`', '\'', '"');
        clean = clean.Replace('\\', '/');
        clean = clean.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "snippet.txt";
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            clean = clean.Replace(invalid, '_');
        }

        return string.IsNullOrWhiteSpace(clean) ? "snippet.txt" : clean;
    }

    private static void OpenFolder(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        });
    }

    private async Task CopyChatTextAsync(LocalLlmChatPartViewModel? part)
    {
        if (part is null || !part.IsText || string.IsNullOrWhiteSpace(part.Text) || _clipboardWriter is null)
        {
            return;
        }

        await _clipboardWriter(part.Text);
        PhaseTitle = "Chat text copied";
        PhaseDetail = "Copied message body text only.";
        Log("info", "Copied chat message text.");
    }

    private async Task ExportChatAsync()
    {
        var messages = ChatMessages.ToArray();
        if (messages.Length == 0)
        {
            PhaseTitle = "Nothing to export";
            PhaseDetail = "The selected chat has no messages yet.";
            return;
        }

        var root = ResolveEffectiveProjectRootPath();
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            root = _processService.ContextRoot;
        }

        try
        {
            Directory.CreateDirectory(root);
            var fileName = $"cc_chat_export_{DateTime.Now:yyyyMMdd_HHmmss}.md";
            var path = Path.Combine(root, fileName);
            var text = BuildChatExportText(messages);
            await File.WriteAllTextAsync(path, text, new UTF8Encoding(false));
            LastExportPath = path;
            PhaseTitle = "Chat exported";
            PhaseDetail = $"{messages.Length:N0} message(s) written to {fileName}.";
            AppendTerminalOutput($"Chat export written: {path}");
            Log("ok", $"Chat export written: {fileName}");
        }
        catch (Exception ex)
        {
            PhaseTitle = "Chat export failed";
            PhaseDetail = ex.Message;
            AppendTerminalOutput($"Chat export failed: {ex.Message}");
            Log("error", ex.Message);
        }
    }

    private string BuildChatExportText(IReadOnlyList<LocalLlmChatMessageViewModel> messages)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# ContextControl chat export");
        builder.AppendLine();
        builder.AppendLine($"Exported: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
        builder.AppendLine($"Project root: {ResolveEffectiveProjectRootPath()}");
        builder.AppendLine($"Session: {SelectedChatSession?.Title ?? "Current chat"}");
        builder.AppendLine($"Session id: {SelectedChatSession?.Id ?? ""}");
        builder.AppendLine($"Selected route: {SelectedRoute}");
        builder.AppendLine($"Selected local model: {SelectedLocalModel?.Id ?? ""}");
        builder.AppendLine($"LLM send mode: {(IsAutopilotEnabled ? "CC flow" : "Raw")}");
        builder.AppendLine($"Status: {PhaseTitle} - {PhaseDetail}");
        builder.AppendLine($"Last export path: {LastExportPath}");
        builder.AppendLine();

        builder.AppendLine("## Current role models");
        builder.AppendLine($"- File request: {FileRequestModelId}");
        builder.AppendLine($"- Patch write: {PatchWriteModelId}");
        builder.AppendLine($"- Patch review: {PatchReviewModelId}");
        builder.AppendLine($"- Chat: {ChatModelId}");
        builder.AppendLine();

        builder.AppendLine("## Current prompt");
        AppendFencedBlock(builder, PromptText, "text");
        builder.AppendLine();

        builder.AppendLine("## Pending attachments for selected chat");
        if (Attachments.Count == 0)
        {
            builder.AppendLine("- none");
        }
        else
        {
            foreach (var attachment in Attachments)
            {
                builder.AppendLine($"- {attachment.DisplayTitle} | kind={attachment.Kind} | include={attachment.IncludeInPrompt} | exists={File.Exists(attachment.Path)} | size={FormatAttachmentSize(attachment.Path)}");
                builder.AppendLine($"  path: {attachment.Path}");
            }
        }

        builder.AppendLine();
        builder.AppendLine("## Messages");
        for (var index = 0; index < messages.Count; index++)
        {
            var message = messages[index];
            builder.AppendLine();
            builder.AppendLine($"### {index + 1}. {message.RoleLabel}");
            builder.AppendLine($"- Time: {message.CreatedUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}");
            builder.AppendLine($"- Model: {message.ModelId}");
            builder.AppendLine($"- Phase: {message.Phase}");
            builder.AppendLine($"- Capsule summary: {message.CapsuleSummary}");
            builder.AppendLine($"- Stats: {message.Stats?.Summary ?? ""}");
            if (message.HasSentAttachments)
            {
                builder.AppendLine("- Sent attachments:");
                foreach (var attachment in message.AttachedFiles)
                {
                    builder.AppendLine($"  - {attachment.DisplayTitle} | kind={attachment.Kind} | include={attachment.IncludeInPrompt} | exists={File.Exists(attachment.Path)} | size={FormatAttachmentSize(attachment.Path)}");
                    builder.AppendLine($"    path: {attachment.Path}");
                }
            }

            builder.AppendLine();
            builder.AppendLine("#### Visible text");
            AppendFencedBlock(builder, message.Text, "text");
            builder.AppendLine();
            builder.AppendLine("#### Raw message");
            AppendFencedBlock(builder, message.RawText, "text");

            if (message.HasDiagnosticPrompt)
            {
                builder.AppendLine();
                builder.AppendLine($"#### {BuildDiagnosticPromptExportHeading(message)}");
                AppendFencedBlock(builder, message.DiagnosticPrompt, "text");
            }

            if (message.Snippets.Count > 0)
            {
                builder.AppendLine();
                builder.AppendLine("#### Snippets");
                foreach (var snippet in message.Snippets)
                {
                    builder.AppendLine($"- {snippet.Title} ({snippet.MetaLabel})");
                    AppendFencedBlock(builder, snippet.Text, "text");
                }
            }

            if (message.HasThinking)
            {
                builder.AppendLine();
                builder.AppendLine("#### Thinking");
                AppendFencedBlock(builder, message.ThinkingText, "text");
            }
        }

        if (!string.IsNullOrWhiteSpace(TerminalOutputText))
        {
            builder.AppendLine();
            builder.AppendLine("## Terminal/status output");
            AppendFencedBlock(builder, TerminalOutputText, "text");
        }

        return builder.ToString().TrimEnd() + Environment.NewLine;
    }

    private static string BuildDiagnosticPromptExportHeading(LocalLlmChatMessageViewModel message)
    {
        if (message.IsCodexGenerated)
        {
            return "Exact Codex harness input";
        }

        if (message.Phase.Equals("raw", StringComparison.OrdinalIgnoreCase))
        {
            return "Exact model input";
        }

        return "Exact local capsule sent to Ollama";
    }

    private static void AppendFencedBlock(StringBuilder builder, string? text, string language)
    {
        var clean = text ?? "";
        var fence = clean.Contains("````", StringComparison.Ordinal) ? "`````" : "````";
        builder.AppendLine($"{fence}{language}");
        builder.AppendLine(string.IsNullOrWhiteSpace(clean) ? "(empty)" : clean.TrimEnd());
        builder.AppendLine(fence);
    }

    private static string FormatAttachmentSize(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return "missing";
            }

            var length = new FileInfo(path).Length;
            return length < 1024
                ? $"{length:N0} B"
                : length < 1024 * 1024
                    ? $"{length / 1024d:N1} KB"
                    : $"{length / 1024d / 1024d:N2} MB";
        }
        catch
        {
            return "unknown";
        }
    }

    private async Task CopyRoutingLogAsync()
    {
        if (_clipboardWriter is null)
        {
            return;
        }

        var text = BuildRoutingLogText();
        if (string.IsNullOrWhiteSpace(text))
        {
            PhaseTitle = "Nothing to copy";
            PhaseDetail = "The routing log and terminal output are empty.";
            return;
        }

        await _clipboardWriter(text);
        PhaseTitle = "Routing log copied";
        PhaseDetail = "Copied terminal output and visible Context Control log entries.";
        Log("info", "Routing log copied to clipboard.");
    }

    private string BuildRoutingLogText()
    {
        var builder = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(TerminalOutputText))
        {
            builder.AppendLine("Terminal output:");
            builder.AppendLine(TerminalOutputText.TrimEnd());
            builder.AppendLine();
        }

        if (LogEntries.Count > 0)
        {
            builder.AppendLine("Context Control log:");
            foreach (var entry in LogEntries.Reverse())
            {
                builder.Append('[');
                builder.Append(entry.Time);
                builder.Append("] ");
                builder.Append(entry.Level);
                builder.Append(": ");
                builder.AppendLine(entry.Message);
            }
        }

        return builder.ToString().TrimEnd();
    }

    private void UseSnippetForCc(ChatSnippetViewModel? snippet)
    {
        if (snippet is null)
        {
            return;
        }

        PromptText = EnsureEndsWithEnd(snippet.Text);
        IsPromptOpen = true;
        PreserveCodexOrUseContextPromptMode();
        PhaseTitle = snippet.IsRequestList ? "CC request loaded" : "Snippet loaded";
        PhaseDetail = snippet.IsRequestList
            ? "Review the request lines, then press CC."
            : "Snippet copied into the prompt.";
    }

    private async Task PreviewSnippetAsync(ChatSnippetViewModel? snippet)
    {
        if (snippet is null || !snippet.IsPatch)
        {
            return;
        }

        _lastAssistantPatchBlocks = snippet.Text;
        PromptText = snippet.Text;
        await RunGoPreviewAsync();
    }

}
