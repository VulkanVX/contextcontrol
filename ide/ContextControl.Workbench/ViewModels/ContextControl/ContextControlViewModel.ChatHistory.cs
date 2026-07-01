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
using Avalonia.Threading;
using ContextControl.Workbench.Services;

namespace ContextControl.Workbench.ViewModels;

public sealed partial class ContextControlViewModel
{
    private void LoadChatHistory()
    {
        var document = _chatHistoryService.Load(
            ResolveConversationScopeKey(_chatHistoryScopeKey, _activeConversationKind),
            _activeConversationKind,
            includeFallbacks: IsChatConversationKind(_activeConversationKind));
        ChatSessions.Clear();
        ChatMessages.Clear();
        SelectedChatSession = null;
        OnPropertyChanged(nameof(HasChatSessions));
        OnPropertyChanged(nameof(ChatHistoryPanelTitle));
        OnPropertyChanged(nameof(ChatHistorySummary));
        RestoreProjectPromptState(document);
        foreach (var session in document.Sessions
                     .OrderByDescending(session => session.UpdatedUtc)
                     .Select(session => new ChatSessionViewModel(session)))
        {
            ChatSessions.Add(session);
        }

        if (ChatSessions.Count == 0)
        {
            CreateNewChatSession(save: false, resetWorkflow: false);
            return;
        }

        var selected = !string.IsNullOrWhiteSpace(document.SelectedSessionId)
            ? ChatSessions.FirstOrDefault(session => string.Equals(session.Id, document.SelectedSessionId, StringComparison.OrdinalIgnoreCase))
            : null;
        SelectChatSession(selected ?? ChatSessions[0], save: false);
    }

    private void RestoreProjectPromptState(ChatHistoryDocument document)
    {
        Attachments.Clear();
        _legacyPendingAttachments = (document.Attachments ?? [])
            .Where(attachment => !AutoAttachmentKinds.Contains(attachment.Kind)
                && !string.IsNullOrWhiteSpace(attachment.Path)
                && File.Exists(attachment.Path))
            .ToArray();

        _lastAssistantPatchBlocks = "";
        _lastUserRequest = document.LastUserRequest ?? "";
        _semanticIndex = null;
        LastExportPath = "";
        IsPatchPlanReady = false;
        PatchSummary = "No patch loaded.";
        UpdatePatchPlanActions(null);

        var route = string.IsNullOrWhiteSpace(document.SelectedRoute) ? _settings.SelectedAiRoute : document.SelectedRoute;
        if (!string.IsNullOrWhiteSpace(route) && RouteOptions.Contains(route))
        {
            SelectedRoute = route;
        }

        if (!string.IsNullOrWhiteSpace(document.SelectedLocalModelId))
        {
            _settings.SelectedLocalModel = document.SelectedLocalModelId;
            var installed = InstalledLocalModels.FirstOrDefault(model => string.Equals(model.Id, document.SelectedLocalModelId, StringComparison.OrdinalIgnoreCase));
            if (installed is not null)
            {
                SelectedLocalModel = installed;
            }
        }

        var selectedImageModelId = document.SelectedImageModelId;
        if (string.IsNullOrWhiteSpace(selectedImageModelId) && IsImageGenConversationKind(_activeConversationKind))
        {
            selectedImageModelId = string.IsNullOrWhiteSpace(_settings.SelectedImageModel)
                ? document.SelectedLocalModelId
                : _settings.SelectedImageModel;
        }

        if (!string.IsNullOrWhiteSpace(selectedImageModelId))
        {
            var installedImage = InstalledImageGenerationModels.FirstOrDefault(model => string.Equals(model.Id, selectedImageModelId, StringComparison.OrdinalIgnoreCase));
            installedImage ??= InstalledImageGenerationModels.FirstOrDefault(model =>
                string.Equals(model.Id, _settings.SelectedImageModel, StringComparison.OrdinalIgnoreCase));
            if (installedImage is not null)
            {
                _settings.SelectedImageModel = installedImage.Id;
                SelectedImageGenerationModel = installedImage;
            }
        }

        _legacyPromptText = document.PromptText ?? "";
        PromptText = "";
        if (!_isSwitchingConversationKind)
        {
            var restoredPromptMode = IsImageGenConversationKind(_activeConversationKind)
                ? "imagegen"
                : NormalizePromptModeKey(document.PromptModeKey) == "imagegen"
                    ? "context"
                    : document.PromptModeKey;
            if (!string.IsNullOrWhiteSpace(restoredPromptMode))
            {
                PromptModeKey = restoredPromptMode;
            }
        }

        IsAutopilotEnabled = document.IsAutopilotEnabled ?? _settings.IsAutopilotEnabled;
        IsPromptOpen = document.IsPromptOpen || _settings.PromptBarOpenByDefault;
        TerminalOutputText = document.TerminalOutputText ?? "";
        NotifyAttachmentStateChanged();
    }

    private void CreateNewChatSession()
    {
        CreateNewChatSession(save: true, resetWorkflow: true);
    }

    private void CreateNewChatSession(bool save)
    {
        CreateNewChatSession(save, resetWorkflow: true);
    }

    private void CreateNewChatSession(bool save, bool resetWorkflow)
    {
        var session = ChatSessionViewModel.CreateNew();
        ChatSessions.Insert(0, session);
        OnPropertyChanged(nameof(HasChatSessions));
        OnPropertyChanged(nameof(ChatHistorySummary));
        SelectChatSession(session, save: false);

        if (resetWorkflow)
        {
            ResetChatWorkflowState();
        }

        if (save)
        {
            SaveChatHistory();
        }
    }

    private void SelectChatSession(ChatSessionViewModel? session)
    {
        SelectChatSession(session, save: true);
    }

    private void SelectChatSession(ChatSessionViewModel? session, bool save)
    {
        if (session is null)
        {
            return;
        }

        if (SelectedChatSession is { } previous && !ReferenceEquals(previous, session))
        {
            previous.SetPendingAttachments(Attachments);
            previous.SetDraftPromptText(PromptText);
        }

        _isSwitchingChatSession = true;
        try
        {
            foreach (var item in ChatSessions)
            {
                item.IsActive = ReferenceEquals(item, session);
            }

            SelectedChatSession = session;
            ChatMessages.Load(session);

            LoadPromptDraftForSession(session);
            LoadPendingAttachmentsForSession(session);

            _lastAssistantPatchBlocks = session.FindLastPatchText();
            if (!string.IsNullOrWhiteSpace(session.WorkflowTaskText))
            {
                _lastUserRequest = session.WorkflowTaskText;
            }

            PhaseTitle = "Chat selected";
            PhaseDetail = session.Title;
        }
        finally
        {
            _isSwitchingChatSession = false;
        }

        if (save)
        {
            SaveChatHistory();
        }
    }

    private void LoadPendingAttachmentsForSession(ChatSessionViewModel session)
    {
        _isSyncingChatAttachments = true;
        try
        {
            Attachments.Clear();
            var pending = session.CreatePendingAttachments()
                .Where(attachment => !string.IsNullOrWhiteSpace(attachment.Path) && File.Exists(attachment.Path))
                .ToArray();
            if (pending.Length == 0 && _legacyPendingAttachments.Count > 0)
            {
                pending = _legacyPendingAttachments
                    .Select(attachment =>
                    {
                        var viewModel = new ContextControlAttachmentViewModel(
                            string.IsNullOrWhiteSpace(attachment.Label) ? Path.GetFileName(attachment.Path) : attachment.Label,
                            attachment.Path,
                            attachment.Kind);
                        viewModel.IncludeInPrompt = attachment.IncludeInPrompt;
                        return viewModel;
                    })
                    .ToArray();
                session.SetPendingAttachments(pending);
                _legacyPendingAttachments = [];
            }

            foreach (var attachment in pending)
            {
                Attachments.Add(attachment);
            }
        }
        finally
        {
            _isSyncingChatAttachments = false;
        }

        NotifyAttachmentStateChanged();
    }

    private void LoadPromptDraftForSession(ChatSessionViewModel session)
    {
        _isSyncingChatDraft = true;
        try
        {
            var draft = session.DraftPromptText;
            if (string.IsNullOrWhiteSpace(draft) && !string.IsNullOrWhiteSpace(_legacyPromptText))
            {
                draft = _legacyPromptText;
                session.SetDraftPromptText(draft);
                _legacyPromptText = "";
            }

            PromptText = draft;
        }
        finally
        {
            _isSyncingChatDraft = false;
        }
    }

    private void RemoveChatSession(ChatSessionViewModel? session)
    {
        if (session is null || !ChatSessions.Contains(session))
        {
            return;
        }

        var wasSelected = ReferenceEquals(SelectedChatSession, session);
        var removedIndex = ChatSessions.IndexOf(session);
        ChatSessions.Remove(session);
        OnPropertyChanged(nameof(HasChatSessions));
        OnPropertyChanged(nameof(ChatHistorySummary));

        if (ChatSessions.Count == 0)
        {
            CreateNewChatSession(save: false, resetWorkflow: true);
            SaveChatHistory();
            PhaseTitle = "Chat removed";
            PhaseDetail = "Started a fresh chat.";
            return;
        }

        if (wasSelected)
        {
            var nextIndex = Math.Clamp(removedIndex, 0, ChatSessions.Count - 1);
            SelectChatSession(ChatSessions[nextIndex], save: false);
        }

        SaveChatHistory();
        PhaseTitle = "Chat removed";
        PhaseDetail = session.Title;
    }

    private void ResetChatWorkflowState()
    {
        Attachments.Clear();
        _lastAssistantPatchBlocks = "";
        _lastUserRequest = "";
        SelectedChatSession?.SetWorkflowTaskText("");
        _semanticIndex = null;
        PromptText = "";
        LastExportPath = "";
        IsPatchPlanReady = false;
        PatchSummary = "No patch loaded.";
        UpdatePatchPlanActions(null);
        MoveToCcStage(CcStageRequest);
        NotifyAttachmentStateChanged();
        PhaseTitle = "New chat";
        PhaseDetail = "Fresh local chat with no DIR, CC, patch, or previous request context.";
    }

    private void SavePendingAttachmentsToSelectedChat()
    {
        if (_isSyncingChatAttachments || _isSwitchingChatSession)
        {
            return;
        }

        SelectedChatSession?.SetPendingAttachments(Attachments);
    }

    private void SavePromptDraftToSelectedChat()
    {
        if (_isSyncingChatDraft || _isSwitchingChatSession)
        {
            return;
        }

        SelectedChatSession?.SetDraftPromptText(PromptText);
    }

    private void AppendChatMessage(LocalLlmChatMessageViewModel message)
    {
        AppendChatMessageToSession(EnsureSelectedChatSession(), message);
    }

    private ChatSessionViewModel EnsureSelectedChatSession()
    {
        if (SelectedChatSession is { } selected)
        {
            return selected;
        }

        CreateNewChatSession(save: false, resetWorkflow: false);
        return SelectedChatSession ?? ChatSessions[0];
    }

    private void AppendChatMessageToSession(ChatSessionViewModel session, LocalLlmChatMessageViewModel message)
    {
        if (!ChatSessions.Contains(session))
        {
            return;
        }

        session.Append(message);
        if (ReferenceEquals(SelectedChatSession, session))
        {
            ChatMessages.Add(message);
            OnPropertyChanged(nameof(ChatWorkspaceSubtitle));
        }

        var index = ChatSessions.IndexOf(session);
        if (index > 0)
        {
            ChatSessions.Move(index, 0);
        }

        OnPropertyChanged(nameof(ChatHistorySummary));
        SaveChatHistory();
    }

    private async Task AppendChatMessageToCapturedConversationAsync(
        string scopeKey,
        string conversationKind,
        ChatSessionViewModel session,
        LocalLlmChatMessageViewModel message)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            AppendChatMessageToCapturedConversation(scopeKey, conversationKind, session, message);
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
            AppendChatMessageToCapturedConversation(scopeKey, conversationKind, session, message));
    }

    private void AppendChatMessageToCapturedConversation(
        string scopeKey,
        string conversationKind,
        ChatSessionViewModel session,
        LocalLlmChatMessageViewModel message)
    {
        var capturedScope = ResolveConversationScopeKey(scopeKey, conversationKind);
        var currentScope = ResolveConversationScopeKey(_chatHistoryScopeKey, _activeConversationKind);
        if (string.Equals(capturedScope, currentScope, StringComparison.OrdinalIgnoreCase)
            && string.Equals(conversationKind, _activeConversationKind, StringComparison.OrdinalIgnoreCase))
        {
            var liveSession = ChatSessions.FirstOrDefault(item => ReferenceEquals(item, session))
                ?? ChatSessions.FirstOrDefault(item => string.Equals(item.Id, session.Id, StringComparison.OrdinalIgnoreCase));
            if (liveSession is null)
            {
                liveSession = session;
                ChatSessions.Insert(0, liveSession);
                OnPropertyChanged(nameof(HasChatSessions));
                OnPropertyChanged(nameof(ChatHistorySummary));
                if (SelectedChatSession is null)
                {
                    SelectChatSession(liveSession, save: false);
                }
            }

            AppendChatMessageToSession(liveSession, message);
            return;
        }

        AppendChatMessageToStoredConversation(capturedScope, conversationKind, session, message);
    }

    private void AppendChatMessageToStoredConversation(
        string resolvedScopeKey,
        string conversationKind,
        ChatSessionViewModel session,
        LocalLlmChatMessageViewModel message)
    {
        try
        {
            var document = _chatHistoryService.Load(
                resolvedScopeKey,
                conversationKind,
                includeFallbacks: IsChatConversationKind(conversationKind));
            var sessionData = document.Sessions.FirstOrDefault(item =>
                    string.Equals(item.Id, session.Id, StringComparison.OrdinalIgnoreCase))
                ?? session.ToData();
            var storedSession = new ChatSessionViewModel(sessionData);
            storedSession.Append(message);

            document.ConversationKind = conversationKind;
            document.SelectedSessionId = string.IsNullOrWhiteSpace(document.SelectedSessionId)
                ? storedSession.Id
                : document.SelectedSessionId;
            document.Sessions.RemoveAll(item => string.Equals(item.Id, storedSession.Id, StringComparison.OrdinalIgnoreCase));
            document.Sessions.Insert(0, storedSession.ToData());

            _chatHistoryService.Save(
                document,
                resolvedScopeKey,
                conversationKind,
                mirrorDefaultScope: IsChatConversationKind(conversationKind));
        }
        catch (Exception ex)
        {
            Log("warn", $"ImageGen response history update skipped: {ex.Message}");
        }
    }

    private void UpdatePatchPlanActions(PatchPlanSummary? summary)
    {
        PatchPlanActions.Clear();
        PatchPlanFiles.Clear();
        if (summary is not null)
        {
            foreach (var action in summary.Actions.Take(24))
            {
                PatchPlanActions.Add(new PatchPlanActionViewModel(action));
            }

            foreach (var group in BuildPatchPlanFileGroups(summary, 16))
            {
                PatchPlanFiles.Add(group);
            }
        }

        OnPropertyChanged(nameof(HasPatchPlanActions));
        OnPropertyChanged(nameof(HasPatchPlanFiles));
        (OpenPatchPlanFileCommand as RelayCommand<PatchPlanFileViewModel>)?.RaiseCanExecuteChanged();
    }

    private static string BuildPatchPlanChatText(PatchPlanSummary summary)
    {
        var builder = new StringBuilder();
        builder.AppendLine("ccReplace | GO preview COMPLETE.");
        builder.AppendLine(summary.CompactLabel);
        if (summary.Actions.Count > 0)
        {
            var fileGroups = BuildPatchPlanFileGroups(summary, 12)
                .Where(file => !file.IsDirectory)
                .ToArray();
            if (fileGroups.Length > 0)
            {
                builder.AppendLine();
                AppendPatchPlanFence(builder, fileGroups);
            }

            var directories = summary.Actions.Where(action => action.IsDirectory).Take(12).ToArray();
            if (directories.Length > 0)
            {
                builder.AppendLine();
                builder.AppendLine("Directories:");
                foreach (var action in directories)
                {
                    builder.AppendLine($"  [{action.KindLabel}] {action.FileLabel}");
                }
            }
        }

        builder.AppendLine();
        builder.AppendLine("Preview only: source files are unchanged. Apply effective writes non-duplicate edits through ccReplace.");
        return builder.ToString().TrimEnd();
    }

    private static IReadOnlyList<PatchPlanFileViewModel> BuildPatchPlanFileGroups(PatchPlanSummary summary, int take)
    {
        return summary.Actions
            .Where(action => !string.IsNullOrWhiteSpace(action.FileLabel))
            .GroupBy(action => action.FileLabel, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Any(action => action.IsDirectory))
            .ThenBy(group => BucketOrder(group.FirstOrDefault()?.BucketLabel ?? ""))
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(1, take))
            .Select(group => new PatchPlanFileViewModel(group.Key, group.ToArray()))
            .ToArray();
    }

    private static void AppendPatchPlanFence(StringBuilder builder, IReadOnlyList<PatchPlanFileViewModel> files)
    {
        builder.AppendLine("```cc-patch-plan");
        foreach (var file in files)
        {
            builder.AppendLine(string.Join('\t',
                CleanPatchPlanCell(file.FileName),
                CleanPatchPlanCell(file.Version),
                CleanPatchPlanCell(file.AddedLabel),
                CleanPatchPlanCell(file.RemovedLabel),
                CleanPatchPlanCell(file.LocLabel),
                CleanPatchPlanCell(file.Target),
                CleanPatchPlanCell(file.ActionSummary)));
        }

        builder.AppendLine("```");
    }

    private static string CleanPatchPlanCell(string value)
    {
        return (value ?? "")
            .Replace('\t', ' ')
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();
    }

    private static int BucketOrder(string bucket)
    {
        return bucket.ToLowerInvariant() switch
        {
            "created" => 0,
            "changed" => 1,
            "removed" => 2,
            "duplicate" => 3,
            _ => 9
        };
    }

    private static string BuildPatchFailureChatText(string title, ContextControlCommandResult result, PatchPlanSummary summary)
    {
        var detail = BuildPatchFailureDetail(result, summary);
        var builder = new StringBuilder();
        builder.AppendLine($"{title}.");
        builder.AppendLine(detail);
        builder.AppendLine();
        builder.AppendLine("GO needs raw BEGIN/END CC-REPLACE blocks with FILE and MODE headers.");
        builder.AppendLine("For unmarked files, use whole_file with the complete replacement file:");
        builder.AppendLine("BEGIN CC-REPLACE");
        builder.AppendLine("FILE: path/relative/to/project");
        builder.AppendLine("MODE: whole_file");
        builder.AppendLine("---");
        builder.AppendLine("complete file contents");
        builder.AppendLine("END CC-REPLACE");
        builder.AppendLine();
        builder.AppendLine("Use replace_region only when the source contains CC-REPLACE-BEGIN/END markers for NAME:");
        builder.AppendLine("BEGIN CC-REPLACE");
        builder.AppendLine("FILE: path/relative/to/project");
        builder.AppendLine("MODE: replace_region");
        builder.AppendLine("NAME: exact_marker_name");
        builder.AppendLine("---");
        builder.AppendLine("replacement region contents");
        builder.AppendLine("END CC-REPLACE");
        return builder.ToString().TrimEnd();
    }

    private static string BuildPatchShapeFailureChatText(string detail)
    {
        var builder = new StringBuilder();
        builder.AppendLine("GO preview cancelled before ccReplace.");
        builder.AppendLine(detail);
        builder.AppendLine();
        builder.AppendLine("Valid minimum shape for an unmarked file:");
        builder.AppendLine("BEGIN CC-REPLACE");
        builder.AppendLine("FILE: path/relative/to/project");
        builder.AppendLine("MODE: whole_file");
        builder.AppendLine("---");
        builder.AppendLine("complete file contents");
        builder.AppendLine("END CC-REPLACE");
        builder.AppendLine();
        builder.AppendLine("Use MODE: replace_region only when the source contains CC-REPLACE-BEGIN/END markers for NAME.");
        builder.AppendLine();
        builder.AppendLine("For includes:");
        builder.AppendLine("BEGIN CC-REPLACE");
        builder.AppendLine("FILE: path/relative/to/project");
        builder.AppendLine("MODE: insert_include");
        builder.AppendLine("HEADER: <memory>");
        builder.AppendLine("END CC-REPLACE");
        return builder.ToString().TrimEnd();
    }

    private static string BuildPatchFailureDetail(ContextControlCommandResult result, PatchPlanSummary summary)
    {
        var commandLines = InterestingLines(result.StandardError)
            .Concat(InterestingLines(result.StandardOutput))
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Take(10)
            .ToArray();
        var commandDetail = commandLines.Length > 0 ? string.Join(Environment.NewLine, commandLines) : "";
        var summaryError = summary.Error ?? "";

        if (!string.IsNullOrWhiteSpace(commandDetail)
            && (summaryError.Equals("No patch plan returned.", StringComparison.OrdinalIgnoreCase)
                || summaryError.Equals("Plan JSON could not be parsed cleanly.", StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(summaryError)
                || !result.Succeeded))
        {
            return commandDetail;
        }

        return string.IsNullOrWhiteSpace(summaryError)
            ? FirstErrorLine(result)
            : summaryError;
    }

    private static string BuildPatchApplyChatText(
        ContextControlCommandResult result,
        string decision,
        IReadOnlyList<PatchPlanFileViewModel>? plannedFiles = null)
    {
        if (result.Succeeded)
        {
            var builder = new StringBuilder();
            builder.AppendLine("ccReplace | GO apply COMPLETE.");

            if (plannedFiles is { Count: > 0 })
            {
                builder.AppendLine();
                AppendPatchPlanFence(builder, plannedFiles);
            }

            return builder.ToString().TrimEnd();
        }

        return $"GO apply failed.{Environment.NewLine}{FirstErrorLine(result)}";
    }

    private void SaveChatHistory()
    {
        if (_isSwitchingProjectState || _isSwitchingChatSession || _isSwitchingConversationKind)
        {
            return;
        }

        try
        {
            SavePendingAttachmentsToSelectedChat();
            SavePromptDraftToSelectedChat();
            if (ChatSessions.Count == 0)
            {
                return;
            }

            _chatHistoryService.Save(new ChatHistoryDocument
            {
                ConversationKind = _activeConversationKind,
                SelectedSessionId = SelectedChatSession?.Id,
                PromptText = "",
                PromptModeKey = PromptModeKey,
                IsAutopilotEnabled = IsAutopilotEnabled,
                IsPromptOpen = IsPromptOpen,
                LastUserRequest = _lastUserRequest,
                SelectedRoute = SelectedRoute,
                SelectedLocalModelId = SelectedLocalModel?.Id ?? _settings.SelectedLocalModel,
                SelectedImageModelId = SelectedImageGenerationModel?.Id ?? _settings.SelectedImageModel,
                TerminalOutputText = TerminalOutputText,
                Sessions = ChatSessions.Select(session => session.ToData()).ToList()
            },
            ResolveConversationScopeKey(_chatHistoryScopeKey, _activeConversationKind),
            _activeConversationKind,
            mirrorDefaultScope: IsChatConversationKind(_activeConversationKind));
        }
        catch (Exception ex)
        {
            Log("warn", $"Chat history save skipped: {ex.Message}");
        }
    }

    private void SwitchConversationKindForPromptMode(bool saveCurrent = true)
    {
        var nextKind = ResolveConversationKindForPromptMode(PromptModeKey);
        if (string.Equals(_activeConversationKind, nextKind, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (saveCurrent)
        {
            SaveChatHistory();
        }

        _isSwitchingConversationKind = true;
        try
        {
            _activeConversationKind = nextKind;
            LoadChatHistory();
        }
        finally
        {
            _isSwitchingConversationKind = false;
        }

        PhaseTitle = IsImageGenConversationKind(nextKind) ? "ImageGen chat loaded" : "Chat loaded";
        PhaseDetail = IsImageGenConversationKind(nextKind)
            ? "Image generation has its own chat history."
            : "Regular chat history restored.";
        OnPropertyChanged(nameof(ChatHistoryPanelTitle));
        OnPropertyChanged(nameof(ChatHistorySummary));
    }

    private static string ResolveConversationKindForPromptMode(string? promptModeKey)
    {
        return string.Equals(NormalizePromptModeKey(promptModeKey), "imagegen", StringComparison.OrdinalIgnoreCase)
            ? ImageGenConversationKind
            : ChatConversationKind;
    }

    private static bool IsChatConversationKind(string? conversationKind)
    {
        return !IsImageGenConversationKind(conversationKind);
    }

    private static bool IsImageGenConversationKind(string? conversationKind)
    {
        return string.Equals(conversationKind, ImageGenConversationKind, StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveConversationScopeKey(string projectScopeKey, string conversationKind)
    {
        var cleanScope = string.IsNullOrWhiteSpace(projectScopeKey) ? "default" : projectScopeKey.Trim();
        return IsImageGenConversationKind(conversationKind)
            ? $"{cleanScope}::imagegen"
            : cleanScope;
    }

}
