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
    public void SetActiveProject(string? projectRoot, string? projectRulesPath)
    {
        var cleanRoot = projectRoot ?? "";
        var cleanRulesPath = projectRulesPath ?? "";
        var nextScope = BuildProjectScopeKey(string.IsNullOrWhiteSpace(cleanRoot) ? _processService.ContextRoot : cleanRoot);
        var scopeChanged = !string.Equals(_chatHistoryScopeKey, nextScope, StringComparison.OrdinalIgnoreCase);

        if (!scopeChanged
            && string.Equals(_activeProjectRoot, cleanRoot, StringComparison.Ordinal)
            && string.Equals(_activeProjectRulesPath, cleanRulesPath, StringComparison.Ordinal))
        {
            return;
        }

        if (scopeChanged && !_isSwitchingProjectState)
        {
            SaveChatHistory();
        }

        _isSwitchingProjectState = true;
        try
        {
            var rootChanged = SetProperty(ref _activeProjectRoot, cleanRoot, nameof(ActiveProjectRoot));
            var rulesChanged = SetProperty(ref _activeProjectRulesPath, cleanRulesPath, nameof(ActiveProjectRulesPath));
            if (rootChanged)
            {
                OnPropertyChanged(nameof(ActiveProjectRootLabel));
                OnPropertyChanged(nameof(EffectiveProjectRootLabel));
            }

            if (rulesChanged)
            {
                OnPropertyChanged(nameof(ActiveProjectRulesPathLabel));
            }

            _semanticIndex = null;
            _lastFindDiscoveryRequestPaths = [];
            if (scopeChanged)
            {
                _chatHistoryScopeKey = nextScope;
                LoadChatHistory();
                PhaseTitle = "Project chat loaded";
                PhaseDetail = string.IsNullOrWhiteSpace(cleanRoot) ? "ContextControl project memory restored." : cleanRoot;
            }
        }
        finally
        {
            _isSwitchingProjectState = false;
        }
    }

    public void SetClipboardWriter(Func<string, Task> clipboardWriter)
    {
        _clipboardWriter = clipboardWriter;
    }

    public void SetSnippetFileSaver(Func<ChatSnippetViewModel, Task<string?>>? snippetFileSaver)
    {
        _snippetFileSaver = snippetFileSaver;
    }

    public void SetProjectFileOpener(Action<string>? projectFileOpener)
    {
        _projectFileOpener = projectFileOpener;
    }

    public void SetPromptModeWorkspaceRequester(Action? promptModeWorkspaceRequester)
    {
        _promptModeWorkspaceRequester = promptModeWorkspaceRequester;
    }

    private void OpenPatchPlanFile(PatchPlanFileViewModel? file)
    {
        if (file?.CanOpen != true)
        {
            return;
        }

        _projectFileOpener?.Invoke(file.Target);
    }

    public void OpenPrompt()
    {
        IsPromptOpen = true;
    }

    public void ClosePrompt()
    {
        IsPromptOpen = false;
    }

    public void SetPromptTypingActive(bool isActive)
    {
        IsPromptTypingActive = IsPromptOpen && isActive && !IsLargePrompt;
    }

    public int AttachFiles(IEnumerable<string> filePaths)
    {
        var attachedCount = 0;

        foreach (var candidatePath in filePaths ?? [])
        {
            if (string.IsNullOrWhiteSpace(candidatePath))
            {
                continue;
            }

            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(candidatePath);
            }
            catch
            {
                continue;
            }

            if (!File.Exists(fullPath))
            {
                continue;
            }

            var kind = ImageAttachmentExtensions.Contains(Path.GetExtension(fullPath))
                ? "image"
                : "file";
            if (AddAttachment(Path.GetFileName(fullPath), fullPath, kind))
            {
                attachedCount++;
            }
        }

        if (attachedCount > 0)
        {
            PhaseTitle = attachedCount == 1 ? "1 file attached" : $"{attachedCount} files attached";
            PhaseDetail = "Pending attachment list updated for this chat.";
        }

        return attachedCount;
    }

    public async Task CopyCodeContextAsync(IEnumerable<string> requestLines, string sourceLabel)
    {
        await RunBusyAsync("Copy context", async () =>
        {
            var lines = requestLines
                .Select(line => line.Trim())
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (lines.Length == 0)
            {
                PhaseTitle = "Nothing to copy";
                PhaseDetail = "The selected tree item has no shown files.";
                Log("warn", "Copy context cancelled: no request lines.");
                return;
            }

            PhaseTitle = "Copying context";
            PhaseDetail = $"Exporting {lines.Length} {sourceLabel} path(s).";
            var result = await _processService.RunCodeExportAsync(lines, ActiveProjectRoot, ActiveProjectRulesPath);
            LogResult(result);

            if (!result.Succeeded)
            {
                PhaseTitle = "Copy failed";
                PhaseDetail = FirstErrorLine(result);
                return;
            }

            var text = await _processService.ReadOutputFileAsync(_processService.CodeExportPath);
            if (_clipboardWriter is not null)
            {
                await _clipboardWriter(text);
                Log("info", "Context copied to clipboard.");
            }

            AddAttachment(Path.GetFileName(_processService.CodeExportPath), _processService.CodeExportPath, "code");
            LastExportPath = _processService.CodeExportPath;
            PhaseTitle = "Context copied";
            PhaseDetail = $"{lines.Length} path(s) exported and copied.";
        });
    }

    public void RemoveAttachment(ContextControlAttachmentViewModel? attachment)
    {
        if (attachment is null || !Attachments.Remove(attachment))
        {
            return;
        }

        NotifyAttachmentStateChanged();
    }

}
