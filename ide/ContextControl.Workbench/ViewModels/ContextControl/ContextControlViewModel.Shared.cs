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
    private async Task RunBusyAsync(string label, Func<Task> action)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        PhaseTitle = label;
        PhaseDetail = "Starting...";
        Log("run", label);
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            PhaseTitle = $"{label} failed";
            PhaseDetail = ex.Message;
            Log("error", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool AddAttachment(string label, string path, string kind)
    {
        EnsureSelectedChatSession();
        var safePath = path ?? "";
        var safeKind = kind ?? "";
        var safeLabel = string.IsNullOrWhiteSpace(label)
            ? Path.GetFileName(safePath)
            : label;

        var existing = AutoAttachmentKinds.Contains(safeKind)
            ? Attachments.FirstOrDefault(item => string.Equals(item.Kind, safeKind, StringComparison.OrdinalIgnoreCase))
            : Attachments.FirstOrDefault(item =>
                string.Equals(item.Kind, safeKind, StringComparison.OrdinalIgnoreCase)
                && string.Equals(item.Path, safePath, StringComparison.OrdinalIgnoreCase));

        if (existing is null)
        {
            Attachments.Add(new ContextControlAttachmentViewModel(safeLabel, safePath, safeKind));
            NotifyAttachmentStateChanged();
            return true;
        }

        var changed = !string.Equals(existing.Path, safePath, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(existing.Kind, safeKind, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(existing.Label, safeLabel, StringComparison.Ordinal);
        existing.Update(safeLabel, safePath, safeKind);
        if (changed)
        {
            NotifyAttachmentStateChanged();
        }

        return changed;
    }

    private void RemoveAttachmentsByKind(params string[] kinds)
    {
        if (kinds.Length == 0 || Attachments.Count == 0)
        {
            return;
        }

        var kindSet = new HashSet<string>(kinds, StringComparer.OrdinalIgnoreCase);
        var removed = false;
        for (var index = Attachments.Count - 1; index >= 0; index--)
        {
            if (!kindSet.Contains(Attachments[index].Kind))
            {
                continue;
            }

            Attachments.RemoveAt(index);
            removed = true;
        }

        if (removed)
        {
            NotifyAttachmentStateChanged();
        }
    }

    private void NotifyAttachmentStateChanged()
    {
        SavePendingAttachmentsToSelectedChat();
        OnPropertyChanged(nameof(AttachmentSummary));
        OnPropertyChanged(nameof(AttachmentButtonLabel));
        OnPropertyChanged(nameof(PromptFooterSummary));
        OnPropertyChanged(nameof(PromptTokenomicsLabel));
        OnPropertyChanged(nameof(PromptContextPressureLabel));
        OnPropertyChanged(nameof(HasAttachments));
        OnPropertyChanged(nameof(IsDirSendMissingRequestWarning));
        OnPropertyChanged(nameof(PromptSendButtonToolTip));
        RefreshPromptFlowSteps();
        if (!_isSyncingChatAttachments && !_isSwitchingProjectState)
        {
            SaveChatHistory();
        }
    }

    private static string CleanModelId(string? modelId, string fallback)
    {
        return string.IsNullOrWhiteSpace(modelId) ? fallback : modelId.Trim();
    }

    private static string EnsureEndsWithEnd(string text)
    {
        var lines = (text ?? "")
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Where(line => !line.Equals("END", StringComparison.OrdinalIgnoreCase))
            .ToList();
        lines.Add("END");
        return string.Join(Environment.NewLine, lines);
    }

    private static int EstimateComfortableBudget(string? comfortableContext)
    {
        var label = comfortableContext ?? "";
        if (label.Contains("8K", StringComparison.OrdinalIgnoreCase))
        {
            return 8192;
        }

        return 4096;
    }

    private AttachmentTokenBudget EstimateAttachmentBudget()
    {
        var fullTokens = 0;
        var sentTokens = 0;
        var clipped = false;
        var phase = ResolveCapsulePhase(PromptText);
        var requestedContextTokens = ResolveRequestedContextTokens(ResolveModelForPhase(phase), phase);
        var remainingCharacters = ContextCapsuleBuilder.EstimateAttachmentCharacterLimit(requestedContextTokens);

        foreach (var attachment in Attachments.Where(attachment => ShouldIncludeAttachmentForPhase(attachment, phase)))
        {
            var characters = EstimateAttachmentCharacters(attachment);
            if (characters <= 0)
            {
                continue;
            }

            fullTokens += EstimateTokensForCharacterCount(characters);
            if (remainingCharacters <= 0)
            {
                clipped = true;
                continue;
            }

            var sentCharacters = Math.Min(characters, remainingCharacters);
            var clippedThisAttachment = characters > remainingCharacters;
            var billedCharacters = sentCharacters
                + (clippedThisAttachment ? Environment.NewLine.Length + ContextCapsuleBuilder.AttachmentClipMarker.Length : 0);
            sentTokens += EstimateTokensForCharacterCount(billedCharacters);
            remainingCharacters -= Math.Max(0, billedCharacters);
            clipped |= clippedThisAttachment;
        }

        return new AttachmentTokenBudget(sentTokens, fullTokens, clipped);
    }

    private static int EstimateAttachmentCharacters(ContextControlAttachmentViewModel attachment)
    {
        if (attachment is null || string.IsNullOrWhiteSpace(attachment.Path) || !File.Exists(attachment.Path))
        {
            return 0;
        }

        try
        {
            var length = new FileInfo(attachment.Path).Length;
            return length > int.MaxValue ? int.MaxValue : (int)length;
        }
        catch
        {
            return 0;
        }
    }

    private static int EstimateTokensForCharacterCount(long characterCount)
    {
        if (characterCount <= 0)
        {
            return 0;
        }

        var tokens = (long)Math.Ceiling(characterCount / 4d);
        return tokens > int.MaxValue ? int.MaxValue : (int)tokens;
    }

    private sealed record AttachmentTokenBudget(int SentTokens, int FullTokens, bool IsClipped);

    private sealed record DependencyInstallResult(bool Succeeded, string Status, bool IsManaged = false);

    private sealed record DependencyProcessResult(bool Started, int ExitCode, string StandardOutput, string StandardError, bool TimedOut = false);

    private void LogResult(ContextControlCommandResult result)
    {
        var summary = FormatCommandResultSummary(result);
        Log(result.Succeeded ? "ok" : "fail", summary);
        AppendTerminalOutput(summary);
        if (!string.IsNullOrWhiteSpace(result.FallbackReason))
        {
            AppendTerminalOutput($"fallback: {result.FallbackReason}");
        }

        foreach (var line in InterestingLines(result.StandardOutput).Take(5))
        {
            Log("out", line);
            AppendTerminalOutput($"out: {line}");
        }

        foreach (var line in InterestingLines(result.StandardError).Take(3))
        {
            Log("err", line);
            AppendTerminalOutput($"err: {line}");
        }
    }

    private static string FormatCommandResultSummary(ContextControlCommandResult result)
    {
        var runner = string.IsNullOrWhiteSpace(result.Runner) ? "PowerShell" : result.Runner;
        var elapsed = result.Elapsed is { } duration
            ? duration.TotalMilliseconds < 1000
                ? $" in {duration.TotalMilliseconds:0} ms"
                : $" in {duration.TotalSeconds:0.00} s"
            : "";
        return $"{result.Command} via {runner}{elapsed} exited {result.ExitCode}";
    }

    private void Log(string level, string message)
    {
        LogEntries.Insert(0, new ContextControlLogEntryViewModel(level, message));
        while (LogEntries.Count > 80)
        {
            LogEntries.RemoveAt(LogEntries.Count - 1);
        }
    }

    private static IEnumerable<string> InterestingLines(string text)
    {
        return (text ?? "")
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.Length <= 220);
    }

    private static string FirstErrorLine(ContextControlCommandResult result)
    {
        return InterestingLines(result.StandardError).FirstOrDefault()
            ?? InterestingLines(result.StandardOutput).FirstOrDefault()
            ?? $"{result.Command} exited {result.ExitCode}.";
    }

    private static string StripLegacyDirPayload(string promptText)
    {
        var text = promptText ?? "";
        const string header = "Context Control fresh-chat request";
        const string userRequestPrefix = "User request:";
        const string dirPrefix = "DIR export:";

        if (!text.Contains(header, StringComparison.Ordinal))
        {
            return text;
        }

        var userRequestIndex = text.IndexOf(userRequestPrefix, StringComparison.Ordinal);
        if (userRequestIndex < 0)
        {
            return text;
        }

        var userBodyStart = userRequestIndex + userRequestPrefix.Length;
        var dirIndex = text.IndexOf(dirPrefix, userBodyStart, StringComparison.Ordinal);
        var userBodyEnd = dirIndex >= 0 ? dirIndex : text.Length;
        var userRequest = text[userBodyStart..userBodyEnd].Trim();

        return string.Equals(userRequest, "(no request text yet)", StringComparison.Ordinal)
            ? ""
            : userRequest;
    }

    private void RaiseCommandStates()
    {
        (RunDirCommand as RelayCommand<object>)?.RaiseCanExecuteChanged();
        (RunCcCommand as RelayCommand<object>)?.RaiseCanExecuteChanged();
        (RunDirTreeExportCommand as RelayCommand<object>)?.RaiseCanExecuteChanged();
        (RunGoCommand as RelayCommand<object>)?.RaiseCanExecuteChanged();
        (ApplyPatchCommand as RelayCommand<object>)?.RaiseCanExecuteChanged();
        (ApplyAllPatchCommand as RelayCommand<object>)?.RaiseCanExecuteChanged();
        (SendCommand as RelayCommand<object>)?.RaiseCanExecuteChanged();
        (RefreshLocalModelsCommand as RelayCommand<object>)?.RaiseCanExecuteChanged();
        (InstallOllamaCommand as RelayCommand<object>)?.RaiseCanExecuteChanged();
        (InstallBackendDependencyCommand as RelayCommand<LlmBackendDependencyViewModel>)?.RaiseCanExecuteChanged();
        (UninstallBackendDependencyCommand as RelayCommand<LlmBackendDependencyViewModel>)?.RaiseCanExecuteChanged();
        (ConfirmExternalDependencyDeleteCommand as RelayCommand<object>)?.RaiseCanExecuteChanged();
        (PullLocalModelCommand as RelayCommand<LocalLlmModelViewModel>)?.RaiseCanExecuteChanged();
        (InstallCodexCommand as RelayCommand<object>)?.RaiseCanExecuteChanged();
        (OpenCodexGuideCommand as RelayCommand<object>)?.RaiseCanExecuteChanged();
        (OpenCodexLoginCommand as RelayCommand<object>)?.RaiseCanExecuteChanged();
        (LogoutCodexCommand as RelayCommand<object>)?.RaiseCanExecuteChanged();
        (RefreshCodexStatusCommand as RelayCommand<object>)?.RaiseCanExecuteChanged();
        (RunCodexDoctorCommand as RelayCommand<object>)?.RaiseCanExecuteChanged();
        (CancelCodexRequestCommand as RelayCommand<ChatRequestProgressViewModel>)?.RaiseCanExecuteChanged();
    }

    private void SaveSettingsQuietly()
    {
        _ = Task.Run(() =>
        {
            try
            {
                lock (_settingsSaveLock)
                {
                    _settings.Save();
                }
            }
            catch
            {
                // UI state should stay usable even if the settings file is locked.
            }
        });
    }
}
