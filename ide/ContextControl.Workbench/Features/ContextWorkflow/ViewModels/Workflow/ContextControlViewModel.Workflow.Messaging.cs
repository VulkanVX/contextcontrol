// CC-DESC: Extracted ContextControlViewModel system slice.
// CC-DESC: Owns Context Control workflow state, prompt bar state, and DIR/CC/GO commands.

using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
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
    private const double CodexUsageBarTrackWidth = 98;

    private async Task SendAsync()
    {
        var currentMessage = PromptText.Trim();
        var explicitAction = ChatActionCatalog.Parse(currentMessage).Action?.Command;
        var explicitSearch = explicitAction == "/search";
        if (!DispatchChatAction(ref currentMessage)) return;
        if (IsImageGenPromptMode && IsMessagePromptMode)
        {
            await SendImageGenerationAsync(currentMessage);
            return;
        }

        if (IsAutopilotEnabled
            && !string.IsNullOrWhiteSpace(currentMessage)
            && IsLikelyCcRequestList(currentMessage)
            && _promptBuilder.BuildCodeExportRequestLines(currentMessage).Count > 0
            && !HasIncludedAttachmentKind("code")
            && !HasIncludedAttachmentKind("patch"))
        {
            PhaseTitle = "CC request detected";
            PhaseDetail = "Send detected file/function lines and is running CC export instead of asking the model again.";
            AppendTerminalOutput("Send detected a CC request list; running CC export.");
            await RunCcAsync();
            return;
        }

        if (IsCodexPromptMode)
        {
            var codexMessage = currentMessage;
            if (string.IsNullOrWhiteSpace(codexMessage))
            {
                codexMessage = IsAutopilotEnabled ? BuildEmptyLocalSendMessage() : "";
                if (string.IsNullOrWhiteSpace(codexMessage))
                {
                    PhaseTitle = "Nothing to send";
                    PhaseDetail = IsAutopilotEnabled
                        ? "Write a prompt, run DIR, or attach CC context before sending to Codex."
                        : "Write a prompt first.";
                    return;
                }
            }

            if (IsAutopilotEnabled)
            {
                await SendCodexChatAsync(codexMessage);
            }
            else
            {
                await SendRawCodexChatAsync(codexMessage);
            }

            return;
        }

        if (IsChatPromptMode || SelectedRoute.StartsWith("Local:", StringComparison.OrdinalIgnoreCase))
        {
            var localMessage = currentMessage;
            if (string.IsNullOrWhiteSpace(localMessage))
            {
                localMessage = IsAutopilotEnabled ? BuildEmptyLocalSendMessage() : "";
                if (string.IsNullOrWhiteSpace(localMessage))
                {
                    PhaseTitle = "Nothing to send";
                    PhaseDetail = IsAutopilotEnabled
                        ? "Write a prompt, run DIR, or attach CC context first."
                        : "Write a prompt first.";
                    return;
                }
            }

            _ = SendLocalChatAsync(localMessage, explicitSearch, allowGameDetection: explicitAction is null);
            return;
        }

        await RunBusyAsync("Send prompt", async () =>
        {
            var message = PromptText.Trim();
            if (string.IsNullOrWhiteSpace(message))
            {
                PhaseTitle = "Nothing to send";
                PhaseDetail = "Write or generate a prompt first.";
                return;
            }

            var request = new AiSendRequest(SelectedRoute, message, Attachments.Select(item => item.Path).ToArray());
            var service = SelectedRoute.StartsWith("API:", StringComparison.OrdinalIgnoreCase)
                ? _apiConnection
                : _browserConnection;
            var result = await service.SendAsync(request);

            if (result.PreparedMessage is not null && _clipboardWriter is not null)
            {
                await _clipboardWriter(result.PreparedMessage);
                Log("info", "Prepared message copied to clipboard.");
            }

            ProviderStatus = result.Status;
            PhaseTitle = result.Succeeded ? "Prompt prepared" : "Route needs setup";
            PhaseDetail = result.Status;
            Log(result.Succeeded ? "info" : "warn", result.Status);
        });
    }

    private bool TryResolveDirRequestLocally(string currentMessage)
    {
        var hasDir = Attachments.Any(attachment => attachment.Kind.Equals("dir", StringComparison.OrdinalIgnoreCase) && attachment.IncludeInPrompt);
        var hasCode = Attachments.Any(attachment => attachment.Kind.Equals("code", StringComparison.OrdinalIgnoreCase) && attachment.IncludeInPrompt);
        var hasPatch = Attachments.Any(attachment => attachment.Kind.Equals("patch", StringComparison.OrdinalIgnoreCase) && attachment.IncludeInPrompt);
        if (!hasDir || hasCode || hasPatch)
        {
            return false;
        }

        var resolverSource = SelectResolverSourceText(currentMessage);
        if (!IsMeaningfulTaskPrompt(resolverSource))
        {
            return false;
        }

        var result = ResolveFileRequestFromIndex(resolverSource);
        if (!result.HasRequestLines)
        {
            return false;
        }

        RememberWorkflowTask(resolverSource);
        PromptText = EnsureEndsWithEnd(result.RequestText);
        IsPromptOpen = true;
        PreserveCodexOrUseContextPromptMode();
        SelectDockPanel("chat");

        var exactCount = result.RequestLines.Count(line => !line.StartsWith("FIND:", StringComparison.OrdinalIgnoreCase));
        var findCount = result.RequestLines.Count - exactCount;
        PhaseTitle = result.UsesFindTerms ? "Resolver needs discovery" : "Resolver suggested files";
        PhaseDetail = result.UsesFindTerms
            ? $"Loaded {findCount:N0} FIND request line(s). Press Send or CC to run discovery."
            : $"Loaded {exactCount:N0} exact file request line(s). Press Send or CC to export source.";
        AppendTerminalOutput(result.UsesFindTerms
            ? $"Resolver loaded FIND fallback from DIR context: {findCount:N0} line(s)."
            : $"Resolver loaded exact CC request from DIR context: {exactCount:N0} file(s).");

        var targetSession = EnsureSelectedChatSession();
        var attachmentSnapshot = BuildPendingAttachmentSnapshotForKinds("dir");
        AppendChatMessageToSession(targetSession, new LocalLlmChatMessageViewModel(
            "user",
            resolverSource,
            "ContextControl",
            "file resolver",
            attachments: attachmentSnapshot));
        ConsumeSentAttachments(attachmentSnapshot);
        AppendChatMessageToSession(targetSession, new LocalLlmChatMessageViewModel(
            "assistant",
            BuildResolverChatText(result),
            "ContextControl",
            "file resolver"));
        return true;
    }

    private string SelectResolverSourceText(string currentMessage)
    {
        return currentMessage;
    }

    private ContextFileResolveResult ResolveFileRequestFromIndex(string userMessage)
    {
        return _fileResolver.Resolve(userMessage, _semanticIndex);
    }

    private static string BuildResolverChatText(ContextFileResolveResult result)
    {
        var builder = new StringBuilder();
        builder.AppendLine(result.UsesFindTerms
            ? "ContextControl generated focused discovery lines from the DIR semantic index."
            : "ContextControl resolved exact CC request lines from the DIR semantic index.");
        builder.AppendLine();
        builder.AppendLine("```cc-request");
        builder.AppendLine(result.RequestText);
        builder.AppendLine("```");

        if (result.Reasons.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Reasons:");
            foreach (var reason in result.Reasons.Take(6))
            {
                builder.AppendLine($"- {reason}");
            }
        }

        return builder.ToString().TrimEnd();
    }

    private string BuildEmptyLocalSendMessage()
    {
        if (Attachments.Any(attachment => attachment.Kind.Equals("code", StringComparison.OrdinalIgnoreCase) && attachment.IncludeInPrompt))
        {
            var task = ResolveWorkflowTaskText();
            if (string.IsNullOrWhiteSpace(task))
            {
                PhaseTitle = "Task missing";
                PhaseDetail = "Source context is attached, but there is no saved workflow task. Type the task or run DIR with a concrete request first.";
                AppendTerminalOutput("Empty send cancelled: CC source is attached, but no workflow task is saved for this session.");
                return "";
            }

            return $"Task: {task}{Environment.NewLine}{Environment.NewLine}Use the attached CC source export. If more context is needed, return only the next narrow CC request list ending with END. If enough context is present, emit GO-ready CC-REPLACE blocks.";
        }

        if (Attachments.Any(attachment => attachment.Kind.Equals("patch", StringComparison.OrdinalIgnoreCase) && attachment.IncludeInPrompt))
        {
            return "Review the attached patch context using the ContextControl patch review flow.";
        }

        if (Attachments.Any(attachment => attachment.Kind.Equals("dir", StringComparison.OrdinalIgnoreCase) && attachment.IncludeInPrompt))
        {
            return "";
        }

        return "";
    }

    private async Task SendCodexChatAsync(string message)
    {
        await SendCodexChatAsync(message, 0, "");
    }

    private async Task SendCodexChatAsync(string message, int retryAttempt, string retryCorrection)
    {
        if (IsBusy)
        {
            return;
        }

        var availability = await _codexHarnessService.CheckAvailabilityAsync();
        ApplyCodexAvailability(availability);
        if (!availability.Available || !availability.IsAuthenticated)
        {
            IsPromptOpen = true;
            PromptModeKey = "codex";
            PhaseTitle = availability.RequiresLogin ? "Codex login required" : "Codex unavailable";
            PhaseDetail = availability.RequiresLogin ? CodexPromptLoginMessage : availability.Status;
            ProviderStatus = availability.Status;
            AppendTerminalOutput(availability.Status);
            return;
        }

        var codexCancellation = new CancellationTokenSource();
        _codexCancellation = codexCancellation;
        IsBusy = true;
        IsCodexRequestRunning = true;
        CodexStatus = "Preparing Codex capsule...";
        ChatRequestProgressViewModel? progressItem = null;
        LocalLlmChatMessageViewModel? liveAssistant = null;
        ChatSessionViewModel? targetSession = null;
        string? retryAfterCompletion = null;
        IReadOnlyList<ContextCapsuleAttachment> retryAttachments = [];
        try
        {
            targetSession = EnsureSelectedChatSession();
            var phase = ResolveCapsulePhase(message);
            var codexExecutionSettings = ResolveCodexExecutionSettings(phase);
            var codexExecutionStatus = BuildCodexExecutionStatus(phase);
            var codexModelLabel = BuildCodexChatModelLabel();
            var capsuleMessage = phase is ContextCapsulePhase.PatchWrite or ContextCapsulePhase.PatchReview
                ? ResolvePatchTaskMessage(message)
                : message;
            if (!string.IsNullOrWhiteSpace(retryCorrection))
            {
                capsuleMessage = $"{retryCorrection.Trim()}{Environment.NewLine}{Environment.NewLine}Original user task:{Environment.NewLine}{message.Trim()}";
            }

            if (phase == ContextCapsulePhase.FileRequest && IsMeaningfulTaskPrompt(message))
            {
                RememberWorkflowTask(message);
            }

            MoveToCcStage(phase switch
            {
                ContextCapsulePhase.FileRequest => CcStageResolve,
                ContextCapsulePhase.SourceAudit or ContextCapsulePhase.PatchWrite or ContextCapsulePhase.PatchReview => CcStagePatch,
                _ => CcStageRequest
            });

            var capsuleAttachments = await BuildCapsuleAttachmentsAsync(phase);
            var codexRequest = new CodexHarnessRequest(
                capsuleMessage,
                phase,
                _processService.ContextRoot,
                capsuleAttachments,
                _skillbookService.BuildCodexInstructionText(phase),
                _skillbookService.BuildEnabledInstructionText(),
                codexExecutionSettings.Model,
                codexExecutionSettings.ReasoningEffort);
            var diagnosticPrompt = CodexHarnessService.BuildPrompt(codexRequest);
            SkillbookCodexCapsuleText = diagnosticPrompt;
            var attachmentSnapshot = BuildSentAttachmentSnapshot(capsuleAttachments);

            AppendChatMessageToSession(targetSession, new LocalLlmChatMessageViewModel(
                "user",
                capsuleMessage,
                codexModelLabel,
                FormatCodexPhase(phase),
                BuildCodexCapsuleSummary(diagnosticPrompt, capsuleAttachments),
                attachments: attachmentSnapshot,
                diagnosticPrompt: diagnosticPrompt));
            ConsumeSentAttachments(attachmentSnapshot);
            liveAssistant = new LocalLlmChatMessageViewModel(
                "assistant",
                "Codex is preparing the CC capsule...",
                codexModelLabel,
                FormatCodexPhase(phase),
                "live Codex response",
                canShowLiveThinkingPlaceholder: false);
            AppendChatMessageToSession(targetSession, liveAssistant);
            liveAssistant.UpdateLiveStatus(LiveThinkingPlaceholder);
            PromptText = "";
            PhaseTitle = "Codex CC chat";
            PhaseDetail = $"{FormatCodexPhase(phase)} through read-only Codex harness; {codexExecutionStatus}.";
            ProviderStatus = $"Codex CLI read-only harness; {codexExecutionStatus}";
            CodexStatus = $"Running {FormatCodexPhase(phase)}...";
            CodexUsageSummary = "Codex prompt running; waiting for token usage...";
            CodexRateLimitSummary = "Waiting for Codex to report 5h and weekly limit windows.";

            var generationProgress = CreateGenerationProgress(targetSession, codexModelLabel, FormatCodexPhase(phase), isCancellable: true);
            progressItem = generationProgress.Item;
            _codexProgressItem = progressItem;
            var terminal = CreateTerminalProgress();
            terminal.Report($"Sending {FormatCodexPhase(phase)} capsule to Codex CLI...");
            terminal.Report($"Codex model settings: {codexExecutionStatus}.");
            terminal.Report("Codex is instructed to use CC attachments only and avoid repo navigation/actions.");
            ReportCapsuleAttachments(terminal, capsuleAttachments);
            var codexProgress = CreateLiveCodexProgress(liveAssistant, generationProgress.Progress);

            var result = await _codexHarnessService.SendAsync(
                codexRequest,
                codexProgress,
                terminal,
                codexCancellation.Token);
            var auditContext = BuildCodexPhaseAuditContext(phase, capsuleAttachments);
            var audit = string.IsNullOrWhiteSpace(result.Message)
                ? null
                : CodexPhaseAuditor.Audit(phase, result.Message, _promptBuilder, diagnosticPrompt, auditContext);
            if (audit is not null)
            {
                ReportCodexPhaseAudit(audit);
            }

            if (phase == ContextCapsulePhase.FileRequest
                && audit is { Passed: false }
                && retryAttempt == 0
                && capsuleAttachments.Any(attachment => attachment.Included && attachment.Kind.Equals("dir", StringComparison.OrdinalIgnoreCase)))
            {
                retryAfterCompletion = BuildCodexFileRequestRetryInstruction(audit);
                retryAttachments = capsuleAttachments;
                AppendTerminalOutput("Codex DIR request retry queued: previous output failed manifest validation.");
            }

            var assistantText = BuildCodexAssistantText(result, audit);
            var phaseHandled = false;
            if (!string.IsNullOrWhiteSpace(assistantText))
            {
                liveAssistant.UpdateContent(
                    assistantText,
                    BuildCodexResponseSummary(result),
                    result.UsageSnapshot?.LastTokenUsage?.ToLocalLlmUsageStats());
                RefreshLiveAssistantMessage(targetSession, liveAssistant);
                var latestPatchBlocks = _promptBuilder.ExtractPatchBlocks(result.Message);
                if (!string.IsNullOrWhiteSpace(latestPatchBlocks)
                    && ReferenceEquals(SelectedChatSession, targetSession)
                    && audit?.Passed != false)
                {
                    _lastAssistantPatchBlocks = latestPatchBlocks;
                }

                if (phase == ContextCapsulePhase.FileRequest)
                {
                    if (audit?.Passed == false)
                    {
                        PhaseTitle = retryAfterCompletion is not null ? "Retrying CC request" : "CC request invalid";
                        PhaseDetail = audit.Summary;
                        AppendTerminalOutput("Invalid Codex file-request output was not loaded into the prompt.");
                    }
                    else
                    {
                        HandleFileRequestAnswer(targetSession, liveAssistant);
                    }

                    phaseHandled = true;
                }
                else if (phase == ContextCapsulePhase.PatchWrite && LooksLikeWrongPatchWriteAnswer(liveAssistant))
                {
                    PhaseTitle = "Patch answer missing";
                    PhaseDetail = "Codex answered like DIR + Request even though CC source context was attached.";
                    AppendTerminalOutput("Codex patch write warning: no CC-REPLACE patch blocks were returned.");
                    phaseHandled = true;
                }
            }

            ProviderStatus = result.Status;
            CodexStatus = result.Status;
            if (result.UsageSnapshot is not null)
            {
                ApplyCodexUsageSnapshot(result.UsageSnapshot);
            }

            if (result.UsageSnapshot?.RateLimits is null)
            {
                CodexRateLimitSummary = "Codex did not report 5h/weekly limits for this exec run.";
            }

            if (!result.Succeeded && CodexHarnessService.IsLoginRequiredText(result.Status))
            {
                IsCodexAuthenticated = false;
                IsCodexLoginRequired = true;
                CodexStatus = "Codex login required. Click Login Codex, complete auth, then Refresh Codex.";
            }

            if (!phaseHandled)
            {
                PhaseTitle = result.Succeeded
                    ? audit is { Passed: false }
                        ? "Codex phase mismatch"
                        : audit is { HasWarnings: true }
                            ? "Codex answer needs review"
                            : "Codex answer ready"
                    : CodexHarnessService.IsLoginRequiredText(result.Status) ? "Codex login required"
                    : result.Status.Contains("stopped", StringComparison.OrdinalIgnoreCase) ? "Codex stopped" : "Codex failed";
                PhaseDetail = CodexHarnessService.IsLoginRequiredText(result.Status)
                    ? CodexStatus
                    : audit?.Summary ?? result.Status;
            }

            Log(result.Succeeded ? "ok" : "warn", result.Status);
        }
        catch (OperationCanceledException)
        {
            CodexStatus = "Codex stopped by user.";
            ProviderStatus = CodexStatus;
            PhaseTitle = "Codex stopped";
            PhaseDetail = CodexStatus;
            liveAssistant?.UpdateContent(CodexStatus);
            RefreshLiveAssistantMessage(targetSession, liveAssistant);
            Log("warn", CodexStatus);
        }
        catch (Exception ex)
        {
            CodexStatus = ex.Message;
            ProviderStatus = ex.Message;
            PhaseTitle = "Codex failed";
            PhaseDetail = ex.Message;
            liveAssistant?.UpdateContent($"Codex failed: {ex.Message}");
            RefreshLiveAssistantMessage(targetSession, liveAssistant);
            Log("error", ex.Message);
        }
        finally
        {
            if (progressItem is not null)
            {
                CompleteGenerationProgress(progressItem);
            }

            _codexProgressItem = null;
            _codexCancellation = null;
            codexCancellation.Dispose();
            IsCodexRequestRunning = false;
            IsBusy = false;
        }

        if (!string.IsNullOrWhiteSpace(retryAfterCompletion))
        {
            RestoreIncludedAttachmentsForRetry(retryAttachments, "dir");
            await SendCodexChatAsync(message, retryAttempt + 1, retryAfterCompletion);
        }
    }

    private async Task SendRawCodexChatAsync(string message)
    {
        if (IsBusy)
        {
            return;
        }

        var availability = await _codexHarnessService.CheckAvailabilityAsync();
        ApplyCodexAvailability(availability);
        if (!availability.Available || !availability.IsAuthenticated)
        {
            IsPromptOpen = true;
            PromptModeKey = "codex";
            PhaseTitle = availability.RequiresLogin ? "Codex login required" : "Codex unavailable";
            PhaseDetail = availability.RequiresLogin ? CodexPromptLoginMessage : availability.Status;
            ProviderStatus = availability.Status;
            AppendTerminalOutput(availability.Status);
            return;
        }

        var codexCancellation = new CancellationTokenSource();
        _codexCancellation = codexCancellation;
        IsBusy = true;
        IsCodexRequestRunning = true;
        CodexStatus = "Preparing raw Codex prompt...";
        ChatRequestProgressViewModel? progressItem = null;
        LocalLlmChatMessageViewModel? liveAssistant = null;
        ChatSessionViewModel? targetSession = null;
        try
        {
            targetSession = EnsureSelectedChatSession();
            var codexExecutionSettings = ResolveRawCodexExecutionSettings();
            var codexExecutionStatus = BuildRawCodexExecutionStatus(codexExecutionSettings);
            var codexModelLabel = BuildRawCodexChatModelLabel(codexExecutionSettings);
            var workingDirectory = ResolveEffectiveProjectRootPath();
            SkillbookCodexCapsuleText = "";

            AppendChatMessageToSession(targetSession, new LocalLlmChatMessageViewModel(
                "user",
                message,
                codexModelLabel,
                "Codex raw",
                "raw Codex prompt"));
            liveAssistant = new LocalLlmChatMessageViewModel(
                "assistant",
                "Codex is running raw...",
                codexModelLabel,
                "Codex raw",
                "live raw Codex response",
                canShowLiveThinkingPlaceholder: false);
            AppendChatMessageToSession(targetSession, liveAssistant);
            liveAssistant.UpdateLiveStatus(LiveThinkingPlaceholder);
            PromptText = "";
            MoveToCcStage(CcStageRequest);
            PhaseTitle = "Raw Codex chat";
            PhaseDetail = $"Sending only your prompt text to Codex CLI; {codexExecutionStatus}.";
            ProviderStatus = $"Codex CLI raw; {codexExecutionStatus}";
            CodexStatus = "Running raw Codex prompt...";
            CodexUsageSummary = "Codex prompt running; waiting for token usage...";
            CodexRateLimitSummary = "Waiting for Codex to report 5h and weekly limit windows.";

            var generationProgress = CreateGenerationProgress(targetSession, codexModelLabel, "Codex raw", isCancellable: true);
            progressItem = generationProgress.Item;
            _codexProgressItem = progressItem;
            var terminal = CreateTerminalProgress();
            terminal.Report("Sending raw prompt to Codex CLI...");
            terminal.Report($"Codex model settings: {codexExecutionStatus}.");
            terminal.Report($"Codex working directory: {workingDirectory}");

            var codexProgress = CreateLiveCodexProgress(liveAssistant, generationProgress.Progress);
            var result = await _codexHarnessService.SendRawAsync(
                new CodexRawRequest(
                    message,
                    _processService.ContextRoot,
                    workingDirectory,
                    codexExecutionSettings.Model,
                    codexExecutionSettings.ReasoningEffort),
                codexProgress,
                terminal,
                codexCancellation.Token);

            var assistantText = BuildCodexAssistantText(result, null);
            if (!string.IsNullOrWhiteSpace(assistantText))
            {
                liveAssistant.UpdateContent(
                    assistantText,
                    BuildCodexResponseSummary(result),
                    result.UsageSnapshot?.LastTokenUsage?.ToLocalLlmUsageStats());
                RefreshLiveAssistantMessage(targetSession, liveAssistant);
            }
            else
            {
                liveAssistant.UpdateContent(result.Status, "raw Codex prompt", result.UsageSnapshot?.LastTokenUsage?.ToLocalLlmUsageStats());
                RefreshLiveAssistantMessage(targetSession, liveAssistant);
            }

            ProviderStatus = result.Status;
            CodexStatus = result.Status;
            if (result.UsageSnapshot is not null)
            {
                ApplyCodexUsageSnapshot(result.UsageSnapshot);
            }

            if (result.UsageSnapshot?.RateLimits is null)
            {
                CodexRateLimitSummary = "Codex did not report 5h/weekly limits for this exec run.";
            }

            if (!result.Succeeded && CodexHarnessService.IsLoginRequiredText(result.Status))
            {
                IsCodexAuthenticated = false;
                IsCodexLoginRequired = true;
                CodexStatus = "Codex login required. Click Login Codex, complete auth, then Refresh Codex.";
            }

            PhaseTitle = result.Succeeded
                ? "Raw Codex answer ready"
                : CodexHarnessService.IsLoginRequiredText(result.Status) ? "Codex login required"
                : result.Status.Contains("stopped", StringComparison.OrdinalIgnoreCase) ? "Codex stopped" : "Raw Codex failed";
            PhaseDetail = CodexHarnessService.IsLoginRequiredText(result.Status)
                ? CodexStatus
                : result.Status;
            Log(result.Succeeded ? "ok" : "warn", result.Status);
        }
        catch (OperationCanceledException)
        {
            CodexStatus = "Codex stopped by user.";
            ProviderStatus = CodexStatus;
            PhaseTitle = "Codex stopped";
            PhaseDetail = CodexStatus;
            liveAssistant?.UpdateContent(CodexStatus);
            RefreshLiveAssistantMessage(targetSession, liveAssistant);
            Log("warn", CodexStatus);
        }
        catch (Exception ex)
        {
            CodexStatus = ex.Message;
            ProviderStatus = ex.Message;
            PhaseTitle = "Raw Codex failed";
            PhaseDetail = ex.Message;
            liveAssistant?.UpdateContent($"Codex failed: {ex.Message}");
            RefreshLiveAssistantMessage(targetSession, liveAssistant);
            Log("error", ex.Message);
        }
        finally
        {
            if (progressItem is not null)
            {
                CompleteGenerationProgress(progressItem);
            }

            _codexProgressItem = null;
            _codexCancellation = null;
            codexCancellation.Dispose();
            IsCodexRequestRunning = false;
            IsBusy = false;
        }
    }

    private bool CanCancelCodexRequest(ChatRequestProgressViewModel? item)
    {
        return (IsCodexRequestRunning
                && _codexCancellation is { IsCancellationRequested: false }
                && (item is null || ReferenceEquals(item, _codexProgressItem)))
            || (item is not null
                && _localChatRequests.TryGetValue(item, out var local)
                && !local.Cancellation.IsCancellationRequested);
    }

    private void CancelCodexRequest(ChatRequestProgressViewModel? item)
    {
        if (!CanCancelCodexRequest(item))
        {
            return;
        }

        if (item is not null && _localChatRequests.TryGetValue(item, out var local))
        {
            item.Status = "Stopping local response...";
            PhaseTitle = "Stopping local response";
            PhaseDetail = "Cancelling the local model request.";
            ProviderStatus = "Stopping local response...";
            AppendTerminalOutput("Local response cancellation requested.");
            local.Cancellation.Cancel();
        }
        else
        {
            CodexStatus = "Stopping Codex...";
            PhaseTitle = "Stopping Codex";
            PhaseDetail = "Cancelling the Codex CLI process.";
            AppendTerminalOutput("Codex cancellation requested.");
            _codexCancellation?.Cancel();
        }

        (CancelCodexRequestCommand as RelayCommand<ChatRequestProgressViewModel>)?.RaiseCanExecuteChanged();
    }

    private void RegisterLocalChatRequest(
        ChatRequestProgressViewModel item,
        CancellationTokenSource cancellation,
        ChatSessionViewModel session,
        LocalLlmChatMessageViewModel liveAssistant)
    {
        _localChatRequests[item] = new LocalChatRequestHandle(cancellation, session, liveAssistant);
        (CancelCodexRequestCommand as RelayCommand<ChatRequestProgressViewModel>)?.RaiseCanExecuteChanged();
    }

    private void UnregisterLocalChatRequest(ChatRequestProgressViewModel item)
    {
        if (_localChatRequests.Remove(item, out var handle))
        {
            EndGoogleResearch(handle.LiveAssistant);
            handle.Cancellation.Dispose();
            (CancelCodexRequestCommand as RelayCommand<ChatRequestProgressViewModel>)?.RaiseCanExecuteChanged();
        }
    }

    private void RefreshLiveAssistantMessage(ChatSessionViewModel? targetSession, LocalLlmChatMessageViewModel? liveAssistant)
    {
        if (targetSession is null || liveAssistant is null || !ChatSessions.Contains(targetSession))
        {
            return;
        }

        targetSession.RefreshMessage(liveAssistant);
        MarkGeneratedResponseIfReady(targetSession, liveAssistant);
        OnPropertyChanged(nameof(ChatHistorySummary));
        if (ReferenceEquals(SelectedChatSession, targetSession))
        {
            OnPropertyChanged(nameof(ChatWorkspaceSubtitle));
        }

        SaveChatHistory();
    }

    private IProgress<LocalLlmGenerationProgress> CreateLiveCodexProgress(
        LocalLlmChatMessageViewModel liveAssistant,
        IProgress<LocalLlmGenerationProgress> downstream)
    {
        var liveResponse = new StringBuilder();
        var hasLiveResponseText = false;
        return new Progress<LocalLlmGenerationProgress>(progress =>
        {
            if (!string.IsNullOrWhiteSpace(progress.Status))
            {
                var displayStatus = NormalizeCodexLiveStatus(progress.Status);
                CodexStatus = displayStatus;
                if (!hasLiveResponseText)
                {
                    liveAssistant.UpdateLiveStatus(displayStatus);
                }
            }

            if (progress.CodexUsage is not null)
            {
                ApplyCodexUsageSnapshot(progress.CodexUsage);
            }

            if (!string.IsNullOrWhiteSpace(progress.ThinkingDelta))
            {
                liveAssistant.AppendLiveThinking(progress.ThinkingDelta);
            }

            if (!string.IsNullOrWhiteSpace(progress.Delta))
            {
                var delta = progress.Delta.Trim();
                var current = liveResponse.ToString();
                if (current.Length > 0 && delta.StartsWith(current, StringComparison.Ordinal))
                {
                    liveResponse.Clear();
                    liveResponse.Append(delta);
                }
                else if (current.Length == 0 || !current.Contains(delta, StringComparison.Ordinal))
                {
                    liveResponse.Append(delta);
                }
                hasLiveResponseText = true;
                liveAssistant.UpdateLiveStatus(liveResponse.ToString().Trim());
            }

            downstream.Report(progress);
        });
    }

    private IProgress<LocalLlmGenerationProgress> CreateLiveAssistantProgress(
        LocalLlmChatMessageViewModel liveAssistant,
        IProgress<LocalLlmGenerationProgress> downstream)
    {
        var answer = new StringBuilder();
        var thinking = new StringBuilder();
        var streamedRaw = new StringBuilder();
        var lastInlineThinking = "";
        var reportedInlineThinkingLength = 0;
        var status = "";

        return new Progress<LocalLlmGenerationProgress>(progress =>
        {
            var inlineThinkingDeltaForProgress = "";
            if (!string.IsNullOrWhiteSpace(progress.Status))
            {
                status = NormalizeLocalLiveStatus(progress.Status);
            }

            if (!string.IsNullOrEmpty(progress.ThinkingDelta))
            {
                thinking.Append(progress.ThinkingDelta);
                liveAssistant.AppendLiveThinking(progress.ThinkingDelta);
            }

            if (!string.IsNullOrEmpty(progress.Delta))
            {
                streamedRaw.Append(progress.Delta);
                var parsed = SplitLiveAssistantStream(streamedRaw.ToString());
                answer.Clear();
                answer.Append(parsed.Visible);
                lastInlineThinking = parsed.Thinking;
                if (!string.IsNullOrWhiteSpace(parsed.Thinking))
                {
                    liveAssistant.UpdateLiveThinking(parsed.Thinking);
                    if (parsed.Thinking.Length > reportedInlineThinkingLength)
                    {
                        inlineThinkingDeltaForProgress = parsed.Thinking[reportedInlineThinkingLength..];
                        reportedInlineThinkingLength = parsed.Thinking.Length;
                    }
                }
            }

            if (answer.Length > 0)
            {
                liveAssistant.IsAwaitingAnswer = false;
                liveAssistant.UpdateLiveStatus(answer.ToString());
                if (_livePhotos.TryGetValue(liveAssistant, out var photos)) photos.Observe(answer.ToString());
            }
            else
            {
                liveAssistant.IsAwaitingAnswer = true;
                liveAssistant.LiveStage = ChatRequestProgressViewModel.CompactStage(status);
            }

            var downstreamProgress = string.IsNullOrEmpty(inlineThinkingDeltaForProgress)
                ? progress
                : progress with
                {
                    ThinkingDelta = string.IsNullOrEmpty(progress.ThinkingDelta)
                        ? inlineThinkingDeltaForProgress
                        : $"{progress.ThinkingDelta}{inlineThinkingDeltaForProgress}"
                };
            downstream.Report(downstreamProgress);
        });
    }

    private static string BuildLiveAssistantText(StringBuilder answer, StringBuilder thinking, string status)
    {
        var parts = new List<string>();
        var thinkingText = thinking.ToString().Trim();
        if (!string.IsNullOrWhiteSpace(thinkingText))
        {
            parts.Add($"<think>{Environment.NewLine}{thinkingText}{Environment.NewLine}</think>");
        }

        var answerText = answer.ToString().Trim();
        if (!string.IsNullOrWhiteSpace(answerText))
        {
            parts.Add(answerText);
        }
        else if (!string.IsNullOrWhiteSpace(status))
        {
            parts.Add(NormalizeLocalLiveStatus(status));
        }

        return parts.Count == 0
            ? LiveThinkingPlaceholder
            : string.Join(Environment.NewLine + Environment.NewLine, parts);
    }

    private static (string Visible, string Thinking) SplitLiveAssistantStream(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return ("", "");
        }

        var visible = new StringBuilder(text.Length);
        var thinking = new StringBuilder();
        var cursor = 0;
        while (cursor < text.Length)
        {
            var open = text.IndexOf("<think>", cursor, StringComparison.OrdinalIgnoreCase);
            if (open < 0)
            {
                visible.Append(text, cursor, text.Length - cursor);
                break;
            }

            visible.Append(text, cursor, open - cursor);
            var thinkingStart = open + "<think>".Length;
            var close = text.IndexOf("</think>", thinkingStart, StringComparison.OrdinalIgnoreCase);
            if (close < 0)
            {
                thinking.Append(text, thinkingStart, text.Length - thinkingStart);
                break;
            }

            thinking.Append(text, thinkingStart, close - thinkingStart);
            cursor = close + "</think>".Length;
        }

        return (visible.ToString(), thinking.ToString());
    }

    private const string LiveThinkingPlaceholder = "Thinking...";
    private const string UserStoppedResponseMessage = "Response was stopped by the user.";

    private static bool IsUserStoppedResponse(string status)
    {
        return string.Equals((status ?? "").Trim(), UserStoppedResponseMessage, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeCodexLiveStatus(string status)
    {
        var clean = (status ?? "").Trim();
        if (string.IsNullOrWhiteSpace(clean))
        {
            return LiveThinkingPlaceholder;
        }

        if (LooksLikeRawCodexEventStatus(clean)
            || clean.StartsWith("Starting Codex", StringComparison.OrdinalIgnoreCase)
            || clean.StartsWith("Codex event received", StringComparison.OrdinalIgnoreCase))
        {
            return LiveThinkingPlaceholder;
        }

        return clean;
    }

    private static string NormalizeLocalLiveStatus(string status)
    {
        var clean = (status ?? "").Trim();
        if (string.IsNullOrWhiteSpace(clean)
            || clean.StartsWith("Loading model", StringComparison.OrdinalIgnoreCase)
            || clean.StartsWith("Generating local response", StringComparison.OrdinalIgnoreCase)
            || clean.StartsWith("Preparing", StringComparison.OrdinalIgnoreCase))
        {
            return LiveThinkingPlaceholder;
        }

        return clean;
    }

    private static bool LooksLikeRawCodexEventStatus(string status)
    {
        var clean = (status ?? "").Trim();
        if (string.IsNullOrWhiteSpace(clean))
        {
            return false;
        }

        var head = clean.Split([' ', ':'], 2, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? clean;
        if (head.Contains('.', StringComparison.Ordinal)
            && head.All(character => char.IsLetterOrDigit(character) || character is '.' or '_' or '-'))
        {
            return true;
        }

        return clean.StartsWith("turn.", StringComparison.OrdinalIgnoreCase)
            || clean.StartsWith("response.", StringComparison.OrdinalIgnoreCase)
            || clean.StartsWith("message.", StringComparison.OrdinalIgnoreCase)
            || clean.StartsWith("item.", StringComparison.OrdinalIgnoreCase)
            || clean.StartsWith("task.", StringComparison.OrdinalIgnoreCase)
            || clean.StartsWith("codex.event", StringComparison.OrdinalIgnoreCase);
    }

    private void ApplyCodexUsageSnapshot(CodexUsageSnapshot snapshot)
    {
        if (!string.IsNullOrWhiteSpace(snapshot.UsageSummary))
        {
            CodexUsageSummary = snapshot.UsageSummary;
        }

        if (!string.IsNullOrWhiteSpace(snapshot.RateLimitSummary))
        {
            CodexRateLimitSummary = snapshot.RateLimitSummary;
        }

        if (snapshot.RateLimits is not null)
        {
            ApplyCodexRateLimitSnapshot(snapshot.RateLimits);
        }
    }

    private void ApplyCodexRateLimitSnapshot(CodexRateLimitSnapshot? rateLimits)
    {
        var fiveHour = FindCodexRateLimitWindow(rateLimits, 300);
        var weekly = FindCodexRateLimitWindow(rateLimits, 10080);

        var fiveHourUsage = BuildCodexUsageLeft(fiveHour);
        CodexFiveHourPercentLeftLabel = fiveHourUsage.Label;
        CodexFiveHourPercentLeftValue = fiveHourUsage.Value;
        CodexFiveHourUsageBarFillWidth = BuildCodexUsageBarFillWidth(fiveHourUsage.Value);
        IsCodexFiveHourLimitDepleted = fiveHourUsage.IsDepleted;

        var weeklyUsage = BuildCodexUsageLeft(weekly);
        CodexWeeklyPercentLeftLabel = weeklyUsage.Label;
        CodexWeeklyPercentLeftValue = weeklyUsage.Value;
        CodexWeeklyUsageBarFillWidth = BuildCodexUsageBarFillWidth(weeklyUsage.Value);
        IsCodexWeeklyLimitDepleted = weeklyUsage.IsDepleted;

        CodexFiveHourResetLabel = FormatCodexUsageReset("5h", fiveHour);
        CodexWeeklyResetLabel = FormatCodexUsageReset("weekly", weekly);
        CodexUsageResetLabel = CodexFiveHourResetLabel;
    }

    private static CodexRateLimitWindow? FindCodexRateLimitWindow(CodexRateLimitSnapshot? rateLimits, int windowMinutes)
    {
        if (rateLimits is null)
        {
            return null;
        }

        if (rateLimits.Primary?.WindowMinutes == windowMinutes)
        {
            return rateLimits.Primary;
        }

        if (rateLimits.Secondary?.WindowMinutes == windowMinutes)
        {
            return rateLimits.Secondary;
        }

        return windowMinutes == 300
            ? rateLimits.Primary
            : rateLimits.Secondary;
    }

    private static (string Label, double Value, bool IsDepleted) BuildCodexUsageLeft(CodexRateLimitWindow? window)
    {
        if (window?.UsedPercent is not { } usedPercent)
        {
            return ("--", 0, true);
        }

        if (window.ResetsAt is { } resetAt && resetAt < DateTimeOffset.Now.AddMinutes(-1))
        {
            return ("100%", 100, false);
        }

        var left = Math.Clamp(100 - usedPercent, 0, 100);
        return ($"{left:0.#}%", left, left <= 0.05);
    }

    private static double BuildCodexUsageBarFillWidth(double value)
    {
        return CodexUsageBarTrackWidth * Math.Clamp(value, 0, 100) / 100;
    }

    private static string FormatCodexUsageReset(string label, CodexRateLimitWindow? window)
    {
        if (window?.ResetsAt is not { } resetAt)
        {
            return $"{label} reset --";
        }

        var normalizedReset = NormalizeCodexReset(resetAt, window.WindowMinutes);
        var formatted = normalizedReset.ToLocalTime().ToString("MMM d HH:mm", CultureInfo.InvariantCulture);
        return $"{label} reset {formatted}";
    }

    private static DateTimeOffset NormalizeCodexReset(DateTimeOffset resetAt, int? windowMinutes)
    {
        if (windowMinutes is not > 0)
        {
            return resetAt;
        }

        var normalized = resetAt;
        var now = DateTimeOffset.Now.AddMinutes(-1);
        while (normalized < now)
        {
            normalized = normalized.AddMinutes(windowMinutes.Value);
        }

        return normalized;
    }

    private void PreserveCodexOrUseContextPromptMode()
    {
        if (!IsCodexPromptMode)
        {
            PromptModeKey = "context";
        }
    }

    private async Task RefreshCodexUsageFromLogsAsync(bool showChecking = true)
    {
        if (_isRefreshingCodexUsage)
        {
            return;
        }

        _isRefreshingCodexUsage = true;
        if (showChecking)
        {
            CodexUsageSummary = "Checking local Codex usage snapshots...";
            CodexRateLimitSummary = "Checking for Codex 5h and weekly limit snapshots...";
        }

        try
        {
            var snapshot = await Task.Run(() => CodexHarnessService.TryReadLatestUsageSnapshotFromSessionLogs());
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (snapshot is null)
                {
                    CodexUsageSummary = "No local Codex usage snapshot found yet.";
                    CodexRateLimitSummary = "Codex CLI has not reported 5h/weekly limits in local session logs.";
                    return;
                }

                ApplyCodexUsageSnapshot(snapshot);
                if (snapshot.RateLimits is null)
                {
                    CodexRateLimitSummary = "Latest Codex session reported tokens, but no 5h/weekly limit windows.";
                }
            });
        }
        finally
        {
            _isRefreshingCodexUsage = false;
        }
    }

    private async Task RefreshCodexStatusAsync()
    {
        if (IsRefreshingCodexStatus)
        {
            return;
        }

        IsRefreshingCodexStatus = true;
        try
        {
            var result = await _codexHarnessService.CheckAvailabilityAsync();
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                IsRefreshingCodexStatus = false;
                ApplyCodexAvailability(result);
                if (IsCodexUsagePanelVisible)
                {
                    _ = RefreshCodexUsageFromLogsAsync(false);
                }
            });
        }
        catch (Exception ex)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                IsRefreshingCodexStatus = false;
                var status = $"Codex CLI status check failed: {ex.Message}";
                CodexStatus = status;
                IsCodexCliInstalled = false;
                IsCodexAuthenticated = false;
                IsCodexLoginRequired = true;
                Log("warn", status);
            });
        }
    }

    private void ApplyCodexAvailability(CodexAvailabilityResult result)
    {
        if (IsCodexRequestRunning)
        {
            return;
        }

        CodexStatus = result.Status;
        IsCodexCliInstalled = result.Available && !result.RequiresInstall;
        IsCodexAuthenticated = result.IsAuthenticated;
        IsCodexLoginRequired = result.RequiresLogin || (result.Available && !result.IsAuthenticated);
        if (IsCodexPromptAuthBlocked)
        {
            ShowCodexPromptAuthRequired();
        }

        Log(result.Available && result.IsAuthenticated ? "ok" : "warn", result.Status);
    }

    private void SwitchPromptModeFromButton(string promptModeKey)
    {
        PromptModeKey = promptModeKey;
        _promptModeWorkspaceRequester?.Invoke();
    }

    private void ActivateCodexPromptMode()
    {
        PromptModeKey = "codex";
        if (IsCodexUsagePanelExpanded)
        {
            _ = RefreshCodexUsageFromLogsAsync(false);
        }

        if (IsCodexPromptAuthBlocked)
        {
            ShowCodexPromptAuthRequired();
            _ = RefreshCodexStatusAsync();
        }
    }

    private void ShowCodexPromptAuthRequired()
    {
        PhaseTitle = IsCodexCliInstalled ? "Codex login required" : "Codex install required";
        PhaseDetail = CodexPromptAuthTitle;
        CodexStatus = CodexPromptAuthMessage;
        ProviderStatus = CodexPromptAuthMessage;
        IsCodexLoginRequired = true;
    }

    private void InstallCodex()
    {
        IsPromptOpen = true;
        PromptModeKey = "codex";
        IsInstallingCodex = true;
        var result = _codexHarnessService.LaunchInstall();
        PhaseTitle = result.Succeeded ? "Codex installer opened" : "Codex install setup";
        PhaseDetail = result.Status;
        ProviderStatus = result.Status;
        CodexStatus = result.Status;
        AppendTerminalOutput(result.Status);
        Log(result.Succeeded ? "info" : "warn", result.Status);
        if (result.Succeeded)
        {
            StartCodexLoginWatcher();
        }

        IsInstallingCodex = false;
    }

    private void OpenCodexGuide()
    {
        var result = _codexHarnessService.OpenOfficialGuide();
        PhaseTitle = result.Succeeded ? "Codex guide opened" : "Codex guide";
        PhaseDetail = result.Status;
        ProviderStatus = result.Status;
        AppendTerminalOutput(result.Status);
        Log(result.Succeeded ? "info" : "warn", result.Status);
    }

    private void OpenCodexLogin()
    {
        IsPromptOpen = true;
        PromptModeKey = "codex";
        if (!IsCodexCliInstalled)
        {
            InstallCodex();
            return;
        }

        var result = _codexHarnessService.LaunchInteractiveLogin();
        CodexStatus = result.Status;
        IsCodexAuthenticated = false;
        IsCodexLoginRequired = true;
        PhaseTitle = result.Succeeded ? "Codex login opened" : "Codex login setup";
        PhaseDetail = result.Succeeded ? CodexPromptAuthorizeMessage : result.Status;
        ProviderStatus = result.Status;
        AppendTerminalOutput(result.Status);
        Log(result.Succeeded ? "info" : "warn", result.Status);
        if (result.Succeeded)
        {
            StartCodexLoginWatcher();
        }
    }

    private async Task LogoutCodexAsync()
    {
        if (IsBusy || IsRefreshingCodexStatus)
        {
            return;
        }

        StopCodexLoginWatcher();
        IsRefreshingCodexStatus = true;
        PhaseTitle = "Codex logout";
        PhaseDetail = "Removing Codex CLI authentication.";
        ProviderStatus = "Codex logout running";
        try
        {
            var result = await _codexHarnessService.LogoutAsync();
            IsCodexAuthenticated = false;
            IsCodexLoginRequired = true;
            CodexStatus = result.Succeeded
                ? CodexPromptLoginMessage
                : result.Status;
            PhaseTitle = result.Succeeded ? "Codex logged out" : "Codex logout failed";
            PhaseDetail = result.Succeeded ? CodexPromptLoginMessage : result.Status;
            ProviderStatus = CodexStatus;
            AppendTerminalOutput(result.Status);
            Log(result.Succeeded ? "info" : "warn", result.Status);
        }
        finally
        {
            IsRefreshingCodexStatus = false;
            if (IsCodexPromptAuthBlocked)
            {
                ShowCodexPromptAuthRequired();
            }
        }
    }

    private void StartCodexLoginWatcher()
    {
        StopCodexLoginWatcher();
        var watcher = new CancellationTokenSource();
        _codexAuthWatchCancellation = watcher;
        _ = WatchCodexLoginAsync(watcher);
    }

    private void StopCodexLoginWatcher()
    {
        try
        {
            _codexAuthWatchCancellation?.Cancel();
        }
        catch
        {
            // Auth polling is best-effort.
        }
        finally
        {
            _codexAuthWatchCancellation = null;
        }
    }

    private async Task WatchCodexLoginAsync(CancellationTokenSource watcher)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(watcher.Token);
            timeout.CancelAfter(TimeSpan.FromMinutes(10));
            while (!timeout.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(4), timeout.Token);
                var availability = await _codexHarnessService.CheckAvailabilityAsync(timeout.Token);
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    ApplyCodexAvailability(availability);
                    if (availability.IsAuthenticated)
                    {
                        PhaseTitle = "Codex login ready";
                        PhaseDetail = "Codex mode is ready.";
                        ProviderStatus = availability.Status;
                    }
                });

                if (availability.IsAuthenticated)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal when the user logs out, starts a new login, or the watch timeout expires.
        }
        finally
        {
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (ReferenceEquals(_codexAuthWatchCancellation, watcher))
                {
                    _codexAuthWatchCancellation = null;
                }

                watcher.Dispose();
            });
        }
    }

    private async Task RunCodexDoctorAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        IsPromptOpen = true;
        PromptModeKey = "terminal";
        PhaseTitle = "Codex doctor";
        PhaseDetail = "Running Codex doctor for install/auth/runtime diagnostics.";
        ProviderStatus = "Codex doctor running";
        var terminal = CreateTerminalProgress();
        try
        {
            var result = await _codexHarnessService.RunDoctorAsync(terminal);
            PhaseTitle = result.Succeeded ? "Codex doctor ready" : "Codex doctor issue";
            PhaseDetail = result.Status;
            ProviderStatus = result.Status;
            CodexStatus = result.Status;
            AppendTerminalOutput(result.Status);
            Log(result.Succeeded ? "ok" : "warn", result.Status);
        }
        finally
        {
            IsBusy = false;
            _ = RefreshCodexStatusAsync();
        }
    }

    private async Task SendLocalChatAsync(string message, bool explicitSearch = false, bool allowGameDetection = true)
    {
        if (!IsAutopilotEnabled || IsGameCreationEnabled)
        {
            await SendRawLocalChatAsync(message, explicitSearch, allowGameDetection);
            return;
        }

        var useGoogle = IsGoogleSearchEnabled;
        var targetSession = EnsureSelectedChatSession();
        var phase = ResolveCapsulePhase(message);
        var capsuleMessage = phase is ContextCapsulePhase.PatchWrite or ContextCapsulePhase.PatchReview
            ? ResolvePatchTaskMessage(message)
            : message;
        if (phase == ContextCapsulePhase.FileRequest && IsMeaningfulTaskPrompt(message))
        {
            RememberWorkflowTask(message);
        }

        MoveToCcStage(phase switch
        {
            ContextCapsulePhase.FileRequest => CcStageResolve,
            ContextCapsulePhase.SourceAudit or ContextCapsulePhase.PatchWrite or ContextCapsulePhase.PatchReview => CcStagePatch,
            _ => CcStageRequest
        });

        var model = ResolveModelForPhase(phase);
        if (model is not { IsInstalled: true })
        {
            PhaseTitle = "No local model";
            PhaseDetail = "Pull a model from the LLMs tab, then select it for chat.";
            Log("warn", "Local chat cancelled: no installed model selected.");
            return;
        }

        var capsuleAttachments = await BuildCapsuleAttachmentsAsync(phase);
        ContextControlAttachmentViewModel[] imageAttachmentSnapshot = model.IsImageModel
            ? BuildPendingAttachmentSnapshotForKinds("image")
            : [];
        model.RefreshAvailableMemory();
        var requestedContextTokens = ResolveRequestedContextTokens(model, phase,
            capsuleMessage + string.Concat(capsuleAttachments.Where(a => a.Included).Select(a => a.Text)));
        var capsule = _capsuleBuilder.Build(new ContextCapsuleBuildRequest(
            capsuleMessage,
            phase,
            model.Id,
            model.ComfortableContext,
            requestedContextTokens,
            _skillbookService.BuildFlowInstructionText(phase),
            _skillbookService.BuildEnabledInstructionText(),
            capsuleAttachments));

        var attachmentSnapshot = BuildSentAttachmentSnapshot(capsuleAttachments);
        var displayedAttachmentSnapshot = attachmentSnapshot
            .Concat(imageAttachmentSnapshot)
            .ToArray();
        AppendChatMessageToSession(targetSession, new LocalLlmChatMessageViewModel(
            "user",
            capsuleMessage,
            model.Id,
            FormatCapsulePhase(phase),
            capsule.Summary,
            attachments: displayedAttachmentSnapshot,
            diagnosticPrompt: capsule.Text));
        ConsumeSentAttachments(attachmentSnapshot);
        ConsumeSentAttachments(imageAttachmentSnapshot);
        var requestThinking = ShouldRequestLocalThinking(model);
        var liveAssistant = new LocalLlmChatMessageViewModel(
            "assistant",
            "Preparing local reasoning prompt...",
            model.Id,
            FormatCapsulePhase(phase),
            "live local response",
            canShowLiveThinkingPlaceholder: false) { IsAwaitingAnswer = true, LiveStage = "Loading" };
        AppendChatMessageToSession(targetSession, liveAssistant);
        liveAssistant.UpdateLiveStatus(LiveThinkingPlaceholder);
        PromptText = "";
        PhaseTitle = "Local CC chat";
        PhaseDetail = $"{FormatCapsulePhase(phase)} with {model.DisplayName}; {capsule.Summary}.";
        ProviderStatus = $"Local: {model.DisplayName}";
        var chatCancellation = new CancellationTokenSource();
        var generationProgress = CreateGenerationProgress(targetSession, model.DisplayName, FormatCapsulePhase(phase), isCancellable: true);
        RegisterLocalChatRequest(generationProgress.Item, chatCancellation, targetSession, liveAssistant);
        var terminal = CreateTerminalProgress();
        try
        {
            terminal.Report($"Sending {FormatCapsulePhase(phase)} capsule to {model.DisplayName} ({model.Id})...");
            terminal.Report($"Requested local context window: {capsule.RequestedContextTokens:N0} tokens.");
            ReportCapsuleAttachments(terminal, capsuleAttachments);
            foreach (var imageAttachment in imageAttachmentSnapshot)
            {
                terminal.Report($"image: {imageAttachment.Path}");
            }

            var preparedPrompt = await PrepareGooglePromptAsync(model.Id, message, capsule.Text, useGoogle,
                targetSession, liveAssistant, generationProgress.Item, chatCancellation.Token, requestedContextTokens);
            var liveProgress = CreateLiveAssistantProgress(liveAssistant, generationProgress.Progress);
            var chatRequest = new LocalLlmRequest(
                    model.Id,
                    preparedPrompt,
                    FormatCapsulePhase(phase),
                    displayedAttachmentSnapshot.Select(attachment => attachment.DisplayTitle).ToArray(),
                    capsule.RequestedContextTokens,
                    imageAttachmentSnapshot.Select(attachment => attachment.Path).ToArray(),
                    requestThinking);
            var result = await _localLlmService.SendChatAsync(
                chatRequest,
                liveProgress,
                terminal,
                chatCancellation.Token);
            if (!result.Succeeded && requestThinking && IsUnsupportedThinkingError(result.Status))
            {
                model.MarkThinkingUnsupported();
                NotifyLocalThinkingCapabilityChanged();
                liveAssistant.DisableLiveThinkingPlaceholder();
                terminal.Report($"{model.DisplayName} rejected the thinking option; retrying with thinking off.");
                chatCancellation.Token.ThrowIfCancellationRequested();
                result = await _localLlmService.SendChatAsync(
                    new LocalLlmRequest(
                        model.Id,
                        preparedPrompt,
                        FormatCapsulePhase(phase),
                        displayedAttachmentSnapshot.Select(attachment => attachment.DisplayTitle).ToArray(),
                        capsule.RequestedContextTokens,
                        imageAttachmentSnapshot.Select(attachment => attachment.Path).ToArray(),
                        false),
                    liveProgress,
                    terminal,
                    chatCancellation.Token);
            }

            result = await RecoverGoogleKnowledgeAsync(message, chatRequest with { Prompt = capsule.Text }, result, useGoogle,
                targetSession, liveAssistant, generationProgress.Item, generationProgress.Progress, terminal, chatCancellation.Token);

            if (result.Succeeded && !string.IsNullOrWhiteSpace(result.Message))
            {
                liveAssistant.UpdateContent(
                    result.Message,
                    capsule.Summary,
                    result.Stats);
                RefreshLiveAssistantMessage(targetSession, liveAssistant);
                await AttachGoogleEntryPhotosAsync(targetSession, liveAssistant, generationProgress.Item, chatCancellation.Token);
                var latestPatchBlocks = _promptBuilder.ExtractPatchBlocks(result.Message);
                if (!string.IsNullOrWhiteSpace(latestPatchBlocks) && ReferenceEquals(SelectedChatSession, targetSession))
                {
                    _lastAssistantPatchBlocks = latestPatchBlocks;
                }

                if (phase == ContextCapsulePhase.FileRequest)
                {
                    HandleFileRequestAnswer(targetSession, liveAssistant);
                }
                else if (phase == ContextCapsulePhase.PatchWrite && LooksLikeWrongPatchWriteAnswer(liveAssistant))
                {
                    PhaseTitle = "Patch answer missing";
                    PhaseDetail = "The model answered like DIR + Request even though CC source context was attached.";
                    AppendTerminalOutput("Patch write warning: model returned DIR/request-list output instead of CC-REPLACE patch blocks.");
                }

                if (liveAssistant.HasThinking)
                {
                    model.MarkThinkingDetected();
                    NotifyLocalThinkingCapabilityChanged();
                }
            }
            else
            {
                liveAssistant.UpdateContent(result.Message ?? result.Status, capsule.Summary, result.Stats);
                RefreshLiveAssistantMessage(targetSession, liveAssistant);
            }

            ProviderStatus = result.Status;
            PhaseTitle = result.Succeeded ? "Local answer ready" : IsUserStoppedResponse(result.Status) ? "Local response stopped" : "Local chat failed";
            PhaseDetail = result.Status;
            Log(result.Succeeded ? "ok" : "warn", result.Status);
        }
        catch (OperationCanceledException)
        {
            liveAssistant.UpdateContent(UserStoppedResponseMessage, capsule.Summary);
            RefreshLiveAssistantMessage(targetSession, liveAssistant);
            ProviderStatus = UserStoppedResponseMessage;
            PhaseTitle = "Local response stopped";
            PhaseDetail = UserStoppedResponseMessage;
            Log("warn", UserStoppedResponseMessage);
        }
        catch (Exception ex)
        {
            liveAssistant.UpdateContent(ex.Message, capsule.Summary);
            RefreshLiveAssistantMessage(targetSession, liveAssistant);
            ProviderStatus = ex.Message;
            PhaseTitle = "Local chat failed";
            PhaseDetail = ex.Message;
            Log("error", ex.Message);
        }
        finally
        {
            UnregisterLocalChatRequest(generationProgress.Item);
            CompleteGenerationProgress(generationProgress.Item);
        }
    }

    private async Task SendRawLocalChatAsync(string message, bool explicitSearch = false, bool allowGameDetection = true)
    {
        var targetSession = EnsureSelectedChatSession();
        var previousGame = ChatMessages.Select(GameArtifact.FromMessage).LastOrDefault(game => game is not null);
        if (explicitSearch || GameArtifact.RequestsNativeRuntime(message)) IsGameCreationEnabled = false;
        else if (allowGameDetection && GameArtifact.IsCreationRequest(message)) IsGameCreationEnabled = true;
        var creatingGame = !explicitSearch && (IsGameCreationEnabled || allowGameDetection && GameArtifact.IsCreationRequest(message));
        var gamePrompt = creatingGame && !message.StartsWith("Create a playable browser game for ContextControl Game Lab.", StringComparison.Ordinal)
            ? GameArtifact.Prompt(message, previousGame) : message;
        var useGoogle = IsGoogleSearchEnabled && !creatingGame;
        var model = ResolveModelForPhase(ContextCapsulePhase.Chat);
        if (model is not { IsInstalled: true })
        {
            PhaseTitle = "No local model";
            PhaseDetail = "Pull a model from the LLMs tab, then select it for chat.";
            Log("warn", "Raw chat cancelled: no installed model selected.");
            return;
        }

        AppendChatMessageToSession(targetSession, new LocalLlmChatMessageViewModel(
            "user",
            message,
            model.Id,
            "raw",
            "clean chat"));
        var requestThinking = ShouldRequestLocalThinking(model);
        var liveAssistant = new LocalLlmChatMessageViewModel(
            "assistant",
            "Preparing local reasoning prompt...",
            model.Id,
            "raw",
            "live local response",
            canShowLiveThinkingPlaceholder: false) { IsAwaitingAnswer = true, LiveStage = "Loading" };
        AppendChatMessageToSession(targetSession, liveAssistant);
        liveAssistant.UpdateLiveStatus(LiveThinkingPlaceholder);
        PromptText = "";
        MoveToCcStage(CcStageRequest);
        PhaseTitle = "Raw chat";
        PhaseDetail = $"Sending clean chat to {model.DisplayName}.";
        ProviderStatus = $"Local: {model.DisplayName}";

        model.RefreshAvailableMemory();
        var requestedContextTokens = ResolveRequestedContextTokens(model, ContextCapsulePhase.Chat, gamePrompt, creatingGame);
        var chatCancellation = new CancellationTokenSource();
        var generationProgress = CreateGenerationProgress(targetSession, model.DisplayName, "raw", isCancellable: true);
        RegisterLocalChatRequest(generationProgress.Item, chatCancellation, targetSession, liveAssistant);
        var terminal = CreateTerminalProgress();
        try
        {
            terminal.Report($"Sending raw prompt to {model.DisplayName} ({model.Id})...");
            terminal.Report(creatingGame ? "Game Lab: complete offline HTML game requested." : "No ContextControl capsule, attachments, skillbook, or workflow instructions included.");
            terminal.Report($"Requested local context window: {requestedContextTokens:N0} tokens.");

            var preparedPrompt = await PrepareGooglePromptAsync(model.Id, message, gamePrompt, useGoogle,
                targetSession, liveAssistant, generationProgress.Item, chatCancellation.Token, requestedContextTokens, forceSearch: explicitSearch);
            requestedContextTokens = ResolveRequestedContextTokens(model, ContextCapsulePhase.Chat, preparedPrompt, creatingGame);
            RequirePromptRoom(preparedPrompt, requestedContextTokens);
            var liveProgress = CreateLiveAssistantProgress(liveAssistant, generationProgress.Progress);
            var chatRequest = new LocalLlmRequest(
                    model.Id,
                    preparedPrompt,
                    "raw",
                    [],
                    requestedContextTokens,
                    Think: requestThinking);
            var result = await _localLlmService.SendChatAsync(
                chatRequest,
                liveProgress,
                terminal,
                chatCancellation.Token);
            if (!result.Succeeded && requestThinking && IsUnsupportedThinkingError(result.Status))
            {
                model.MarkThinkingUnsupported();
                NotifyLocalThinkingCapabilityChanged();
                liveAssistant.DisableLiveThinkingPlaceholder();
                terminal.Report($"{model.DisplayName} rejected the thinking option; retrying with thinking off.");
                chatCancellation.Token.ThrowIfCancellationRequested();
                result = await _localLlmService.SendChatAsync(
                    new LocalLlmRequest(
                        model.Id,
                        preparedPrompt,
                        "raw",
                        [],
                        requestedContextTokens,
                        Think: false),
                    liveProgress,
                    terminal,
                    chatCancellation.Token);
            }

            result = await RecoverGoogleKnowledgeAsync(message, chatRequest with { Prompt = message }, result, useGoogle,
                targetSession, liveAssistant, generationProgress.Item, generationProgress.Progress, terminal, chatCancellation.Token);

            if (creatingGame) result = await ReviewGeneratedGameAsync(result, message, model, targetSession,
                liveAssistant, generationProgress.Item, generationProgress.Progress, terminal, chatCancellation.Token);

            if (result.Succeeded && !string.IsNullOrWhiteSpace(result.Message))
            {
                liveAssistant.UpdateContent(
                    result.Message,
                    "clean chat",
                    result.Stats);
                RefreshLiveAssistantMessage(targetSession, liveAssistant);
                await AttachGoogleEntryPhotosAsync(targetSession, liveAssistant, generationProgress.Item, chatCancellation.Token);
                if (liveAssistant.HasThinking)
                {
                    model.MarkThinkingDetected();
                    NotifyLocalThinkingCapabilityChanged();
                }
            }
            else
            {
                liveAssistant.UpdateContent(result.Message ?? result.Status, "clean chat", result.Stats);
                RefreshLiveAssistantMessage(targetSession, liveAssistant);
            }

            ProviderStatus = result.Status;
            PhaseTitle = result.Succeeded ? "Raw answer ready" : IsUserStoppedResponse(result.Status) ? "Local response stopped" : "Raw chat failed";
            PhaseDetail = result.Status;
            Log(result.Succeeded ? "ok" : "warn", result.Status);
        }
        catch (OperationCanceledException)
        {
            liveAssistant.UpdateContent(UserStoppedResponseMessage, "clean chat");
            RefreshLiveAssistantMessage(targetSession, liveAssistant);
            ProviderStatus = UserStoppedResponseMessage;
            PhaseTitle = "Local response stopped";
            PhaseDetail = UserStoppedResponseMessage;
            Log("warn", UserStoppedResponseMessage);
        }
        catch (Exception ex)
        {
            liveAssistant.UpdateContent(ex.Message, "clean chat");
            RefreshLiveAssistantMessage(targetSession, liveAssistant);
            ProviderStatus = ex.Message;
            PhaseTitle = "Raw chat failed";
            PhaseDetail = ex.Message;
            Log("error", ex.Message);
        }
        finally
        {
            UnregisterLocalChatRequest(generationProgress.Item);
            CompleteGenerationProgress(generationProgress.Item);
        }
    }

    private bool ShouldRequestLocalThinking(LocalLlmModelViewModel model)
    {
        return IsLocalThinkingEnabled && model.SupportsNativeThinkingRequest;
    }

    private static bool IsUnsupportedThinkingError(string status)
    {
        return (status ?? "").Contains("does not support thinking", StringComparison.OrdinalIgnoreCase);
    }

    private async Task SendImageGenerationAsync(string prompt)
    {
        if (!IsImageGenConversationKind(_activeConversationKind))
        {
            SwitchConversationKindForPromptMode();
        }

        var targetSession = EnsureSelectedChatSession();
        var capturedConversationKind = ImageGenConversationKind;
        var capturedScopeKey = _chatHistoryScopeKey;
        var model = SelectedImageGenerationModel;
        if (model is null || (!model.IsInstalled && !model.CanUseManualBackend))
        {
            PhaseTitle = "No image gen model";
            PhaseDetail = "Install or ready the required image generation backend, then select a model in ImageGen mode.";
            Log("warn", "Image generation cancelled: no ready image generation model selected.");
            return;
        }

        if (string.IsNullOrWhiteSpace(prompt))
        {
            PhaseTitle = "No image prompt";
            PhaseDetail = "Describe the image you want to generate.";
            Log("warn", "Image generation cancelled: empty prompt.");
            return;
        }

        AppendChatMessageToSession(targetSession, new LocalLlmChatMessageViewModel(
            "user",
            prompt,
            model.Id,
            "image gen",
            "prompt-only image generation"));
        PromptText = "";
        PhaseTitle = "ImageGen";
        PhaseDetail = $"Generating with {model.DisplayName}.";
        ProviderStatus = model.CanUseManualBackend
            ? $"{model.BackendRequirementLabel} image gen: {model.Id}"
            : $"Local Ollama image gen: {model.Id}";
        var generationProgress = CreateGenerationProgress(targetSession, model.DisplayName, "image gen");
        var terminal = CreateTerminalProgress();
        var outputDirectory = "";
        var generationStartedUtc = DateTime.UtcNow.AddSeconds(-5);
        try
        {
            outputDirectory = ResolveImageGenerationOutputDirectory();
            terminal.Report($"Generating image with {model.DisplayName} ({model.Id})...");
            if (model.UsesHuggingFaceHubDownload && !HasHuggingFaceToken)
            {
                terminal.Report(model.HuggingFaceTokenWarning);
            }

            var result = await _localLlmService.GenerateImageAsync(
                model.Id,
                prompt,
                outputDirectory,
                generationProgress.Progress,
                terminal);
            result = IncludeFallbackDetectedImages(result, outputDirectory, generationStartedUtc, model.Id);

            var hasGeneratedImages = result.ImagePaths.Count > 0;
            var generatedAttachments = result.ImagePaths
                .Select(path => new ContextControlAttachmentViewModel(Path.GetFileName(path), path, "image")
                {
                    IncludeInPrompt = false
                })
                .ToArray();
            var responseText = BuildImageGenerationChatText(result);
            var assistant = new LocalLlmChatMessageViewModel(
                "assistant",
                responseText,
                model.Id,
                result.Succeeded ? "image gen" : hasGeneratedImages ? "image gen warning" : "image gen failed",
                hasGeneratedImages
                    ? $"{result.ImagePaths.Count:N0} generated image(s)"
                    : "no generated image detected",
                attachments: generatedAttachments);
            await AppendChatMessageToCapturedConversationAsync(capturedScopeKey, capturedConversationKind, targetSession, assistant);

            if (hasGeneratedImages)
            {
                LastExportPath = result.ImagePaths.First();
            }

            ProviderStatus = result.Status;
            PhaseTitle = hasGeneratedImages || result.Succeeded ? "Image ready" : "Image generation failed";
            PhaseDetail = result.Status;
            Log(hasGeneratedImages || result.Succeeded ? "ok" : "warn", result.Status);
        }
        catch (Exception ex)
        {
            var fallbackImages = FindRecentGeneratedImageFiles(outputDirectory, generationStartedUtc);
            if (fallbackImages.Count > 0)
            {
                var result = new LocalLlmImageGenerationResult(
                    true,
                    $"Image generated, but the response update hit an error: {ex.Message}",
                    fallbackImages,
                    outputDirectory);
                var assistant = new LocalLlmChatMessageViewModel(
                    "assistant",
                    BuildImageGenerationChatText(result),
                    model.Id,
                    "image gen warning",
                    $"{fallbackImages.Count:N0} generated image(s)",
                    attachments: fallbackImages
                        .Select(path => new ContextControlAttachmentViewModel(Path.GetFileName(path), path, "image")
                        {
                            IncludeInPrompt = false
                        })
                        .ToArray());
                await AppendChatMessageToCapturedConversationAsync(capturedScopeKey, capturedConversationKind, targetSession, assistant);
                LastExportPath = fallbackImages.First();
            }

            ProviderStatus = ex.Message;
            PhaseTitle = "Image generation failed";
            PhaseDetail = ex.Message;
            Log("error", ex.Message);
        }
        finally
        {
            CompleteGenerationProgress(generationProgress.Item);
        }
    }

    private static LocalLlmImageGenerationResult IncludeFallbackDetectedImages(
        LocalLlmImageGenerationResult result,
        string outputDirectory,
        DateTime generationStartedUtc,
        string modelId)
    {
        if (result.ImagePaths.Count > 0)
        {
            return result;
        }

        var fallbackImages = FindRecentGeneratedImageFiles(outputDirectory, generationStartedUtc);
        return fallbackImages.Count == 0
            ? result
            : result with
            {
                Succeeded = true,
                Status = $"Generated {fallbackImages.Count:N0} image(s) with {modelId}.",
                ImagePaths = fallbackImages
            };
    }

    private static IReadOnlyList<string> FindRecentGeneratedImageFiles(string outputDirectory, DateTime generationStartedUtc)
    {
        if (string.IsNullOrWhiteSpace(outputDirectory) || !Directory.Exists(outputDirectory))
        {
            return [];
        }

        try
        {
            return Directory.EnumerateFiles(outputDirectory)
                .Select(path => new FileInfo(path))
                .Where(file => ImageAttachmentExtensions.Contains(file.Extension)
                    && file.LastWriteTimeUtc >= generationStartedUtc)
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .Take(4)
                .Select(file => file.FullName)
                .ToArray();
        }
        catch
        {
            return [];
        }
    }

    private string ResolveImageGenerationOutputDirectory()
    {
        var root = string.IsNullOrWhiteSpace(_processService.ContextRoot)
            ? AppContext.BaseDirectory
            : _processService.ContextRoot;
        var directory = Path.Combine(root, "image-gen");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static string BuildImageGenerationChatText(LocalLlmImageGenerationResult result)
    {
        var builder = new StringBuilder();
        if (result.ImagePaths.Count > 0)
        {
            if (result.Succeeded)
            {
                builder.AppendLine(result.Status);
            }
            else
            {
                builder.AppendLine("Image generated, but the backend reported a warning.");
                builder.AppendLine(result.Status);
            }

            builder.AppendLine(result.ImagePaths.Count == 1
                ? "Generated image preview attached."
                : "Generated image previews attached.");
        }
        else
        {
            builder.AppendLine(result.Succeeded
                ? result.Status
                : $"Image generation failed: {result.Status}");
        }

        return builder.ToString().TrimEnd();
    }

    private static bool LooksLikeWrongPatchWriteAnswer(LocalLlmChatMessageViewModel assistant)
    {
        if (assistant.Snippets.Any(snippet => snippet.IsPatch))
        {
            return false;
        }

        if (assistant.Snippets.Any(snippet => snippet.IsRequestList))
        {
            return true;
        }

        var text = (assistant.Text ?? "").Trim();
        return text.StartsWith("DIR", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("FIND:", StringComparison.OrdinalIgnoreCase)
            || text.Equals("END", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildCodexAssistantText(CodexHarnessResult result, CodexPhaseAuditResult? audit)
    {
        var builder = new StringBuilder();
        var thinking = BuildCodexThinkingText(result, audit);
        if (!string.IsNullOrWhiteSpace(thinking))
        {
            builder.AppendLine("<think>");
            builder.AppendLine(thinking.Trim());
            builder.AppendLine("</think>");
            builder.AppendLine();
        }

        builder.AppendLine(!string.IsNullOrWhiteSpace(result.Message)
            ? result.Message.Trim()
            : result.Status);
        return builder.ToString().TrimEnd();
    }

    private static string BuildCodexThinkingText(CodexHarnessResult result, CodexPhaseAuditResult? audit)
    {
        var parts = new List<string>();
        if (audit is not null)
        {
            parts.Add(audit.ToDiagnosticText());
        }

        if (!string.IsNullOrWhiteSpace(result.Thinking))
        {
            parts.Add(result.Thinking.Trim());
        }

        return string.Join(Environment.NewLine + Environment.NewLine, parts);
    }

    private static string BuildCodexResponseSummary(CodexHarnessResult result)
    {
        if (result.UsageSnapshot?.LastTokenUsage is not null)
        {
            return "Codex CLI response";
        }

        return string.IsNullOrWhiteSpace(result.EventTrace)
            ? "Codex CLI response"
            : "Codex CLI event stream captured";
    }

    private void ReportCodexPhaseAudit(CodexPhaseAuditResult audit)
    {
        AppendTerminalOutput(audit.Summary);
        foreach (var detail in audit.Details.Take(6))
        {
            AppendTerminalOutput($"  - {detail}");
        }
    }

    private static string FormatCodexPhase(ContextCapsulePhase phase)
    {
        return $"Codex {FormatCapsulePhase(phase)}";
    }

    private string BuildCodexChatModelLabel()
    {
        var model = string.IsNullOrWhiteSpace(_codexModelId) ? "Codex" : _codexModelId;
        return string.IsNullOrWhiteSpace(_codexReasoningEffort)
            ? model
            : $"{model} / {_codexReasoningEffort}";
    }

    private (string Model, string ReasoningEffort) ResolveRawCodexExecutionSettings()
    {
        var model = string.IsNullOrWhiteSpace(_codexModelId)
            || string.Equals(_codexModelId, CustomFlowCodexModelId, StringComparison.OrdinalIgnoreCase)
            ? DefaultCodexModelId
            : _codexModelId;
        var reasoning = string.IsNullOrWhiteSpace(_codexReasoningEffort)
            ? DefaultCodexReasoningEffort
            : _codexReasoningEffort;
        return (model, reasoning);
    }

    private static string BuildRawCodexExecutionStatus((string Model, string ReasoningEffort) settings)
    {
        return $"{settings.Model}; {settings.ReasoningEffort} reasoning";
    }

    private static string BuildRawCodexChatModelLabel((string Model, string ReasoningEffort) settings)
    {
        return string.IsNullOrWhiteSpace(settings.ReasoningEffort)
            ? settings.Model
            : $"{settings.Model} / {settings.ReasoningEffort}";
    }

    private CodexPhaseAuditContext? BuildCodexPhaseAuditContext(
        ContextCapsulePhase phase,
        IReadOnlyList<ContextCapsuleAttachment> attachments)
    {
        if (phase != ContextCapsulePhase.FileRequest)
        {
            return null;
        }

        var dirText = attachments
            .FirstOrDefault(attachment => attachment.Included && attachment.Kind.Equals("dir", StringComparison.OrdinalIgnoreCase))
            ?.Text;
        if (string.IsNullOrWhiteSpace(dirText))
        {
            return null;
        }

        return new CodexPhaseAuditContext(
            ContextDirManifestParser.Parse(dirText),
            ResolveEffectiveProjectRootPath(),
            _lastFindDiscoveryRequestPaths);
    }

    private static string BuildCodexFileRequestRetryInstruction(CodexPhaseAuditResult audit)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Previous DIR request output was invalid for the attached DIR manifest.");
        foreach (var detail in audit.Details.Take(6))
        {
            builder.AppendLine($"- {detail}");
        }

        builder.AppendLine();
        builder.AppendLine("Retry once. Output only valid CC request lines ending with END.");
        builder.AppendLine("Copy EXPAND paths exactly from visible ROOT/SCOPE records. Do not use EXPAND: ., EXPAND: ./, absolute paths, markdown, prose, or patch blocks.");
        return builder.ToString().TrimEnd();
    }

    private void RestoreIncludedAttachmentsForRetry(
        IReadOnlyList<ContextCapsuleAttachment> attachments,
        params string[] kinds)
    {
        var kindSet = new HashSet<string>(kinds ?? [], StringComparer.OrdinalIgnoreCase);
        foreach (var attachment in attachments ?? [])
        {
            if (!attachment.Included
                || !kindSet.Contains(attachment.Kind)
                || string.IsNullOrWhiteSpace(attachment.Path)
                || !File.Exists(attachment.Path)
                || Attachments.Any(existing =>
                    existing.Kind.Equals(attachment.Kind, StringComparison.OrdinalIgnoreCase)
                    && existing.Path.Equals(attachment.Path, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            AddAttachment(Path.GetFileName(attachment.Path), attachment.Path, attachment.Kind);
        }
    }

    private static string BuildCodexCapsuleSummary(string diagnosticPrompt, IReadOnlyList<ContextCapsuleAttachment> attachments)
    {
        var promptTokens = ContextCapsuleBuilder.EstimateTokens(diagnosticPrompt);
        var attachmentTokens = attachments
            .Where(attachment => attachment.Included)
            .Sum(attachment => ContextCapsuleBuilder.EstimateTokens(attachment.Text));
        return $"{promptTokens:N0} prompt tok; {attachmentTokens:N0} attachment tok; read-only harness";
    }

    private void HandleFileRequestAnswer(ChatSessionViewModel targetSession, LocalLlmChatMessageViewModel assistant)
    {
        var requestSnippet = assistant.Snippets.LastOrDefault(snippet => snippet.IsRequestList);
        if (requestSnippet is null)
        {
            PhaseTitle = "No CC lines";
            PhaseDetail = "The model did not return file/FUNCTION lines. Refine the request or paste exact lines from DIR, then press CC.";
            AppendTerminalOutput("File request stopped: model returned no usable CC request lines.");
            return;
        }

        if (!ReferenceEquals(SelectedChatSession, targetSession))
        {
            return;
        }

        PromptText = EnsureEndsWithEnd(requestSnippet.Text);
        IsPromptOpen = true;
        PreserveCodexOrUseContextPromptMode();
        PhaseTitle = IsFindOnlyRequestList(requestSnippet.Text) ? "Discovery request ready" : "CC request ready";
        PhaseDetail = IsFindOnlyRequestList(requestSnippet.Text)
            ? "Review the FIND lines, then press CC. FIND returns candidate files only, not source bodies."
            : "Review the file/FUNCTION lines, then press CC to export source.";
        AppendTerminalOutput(IsFindOnlyRequestList(requestSnippet.Text)
            ? "File request ready: FIND discovery lines loaded into the selected chat prompt."
            : "File request ready: exact CC request lines loaded into the selected chat prompt.");
    }

    private static void ReportCapsuleAttachments(IProgress<string> terminal, IReadOnlyList<ContextCapsuleAttachment> attachments)
    {
        var included = attachments.Where(attachment => attachment.Included).ToArray();
        if (included.Length == 0)
        {
            terminal.Report("No raw attachments included in this capsule.");
            return;
        }

        foreach (var attachment in included)
        {
            var text = attachment.Text ?? "";
            terminal.Report($"Raw attachment included: {attachment.Label} ({attachment.Kind}, {text.Length:N0} chars, ~{ContextCapsuleBuilder.EstimateTokens(text):N0} tok)");
        }
    }

}
