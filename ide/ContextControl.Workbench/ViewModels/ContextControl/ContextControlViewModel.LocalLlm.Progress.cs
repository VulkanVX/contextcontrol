// CC-DESC: Extracted ContextControlViewModel system slice.
// CC-DESC: Owns Context Control workflow state, prompt bar state, and DIR/CC/GO commands.

using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Input;
using Avalonia.Collections;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using ContextControl.Workbench.Services;

namespace ContextControl.Workbench.ViewModels;

public sealed partial class ContextControlViewModel
{
    private IProgress<LocalLlmTransferProgress> CreateTransferProgress(
        string initialTitle,
        CancellationTokenSource? cancellationSource = null,
        bool revealTerminal = true)
    {
        BeginTransferProgress(initialTitle, cancellationSource, revealTerminal);
        return new Progress<LocalLlmTransferProgress>(UpdateTransferProgress);
    }

    public IProgress<LocalLlmTransferProgress> CreateShellTransferProgress(
        string initialTitle,
        CancellationTokenSource? cancellationSource = null,
        bool revealTerminal = true)
    {
        return CreateTransferProgress(initialTitle, cancellationSource, revealTerminal);
    }

    public void CompleteShellTransferProgress(string status, bool succeeded, bool keepVisible = false)
    {
        if (keepVisible)
        {
            CompleteDependencyTransferProgress(status, succeeded);
        }
        else
        {
            CompleteTransferProgress(status, succeeded);
        }
    }

    private IProgress<string> CreateTerminalProgress()
    {
        return new Progress<string>(AppendTerminalOutput);
    }

    private (ChatRequestProgressViewModel Item, IProgress<LocalLlmGenerationProgress> Progress) CreateGenerationProgress(
        ChatSessionViewModel session,
        string modelName,
        string phase,
        bool isCancellable = false)
    {
        IsPromptOpen = true;
        var item = new ChatRequestProgressViewModel(session.Id, $"{phase} with {modelName}", isCancellable);
        ChatRequestProgressItems.Add(item);

        return (item, new Progress<LocalLlmGenerationProgress>(progress =>
        {
            var elapsed = item.ElapsedSeconds;
            item.Status = progress.Status;
            if (!string.IsNullOrWhiteSpace(progress.ThinkingDelta))
            {
                item.AppendThinking(progress.ThinkingDelta);
            }

            item.SizeLabel = BuildGenerationSizeLabel(progress);
            item.SpeedLabel = BuildGenerationSpeedLabel(progress, elapsed);
            item.RefreshElapsed();
            item.IsIndeterminate = !progress.Done;
            if (progress.Done)
            {
                item.Value = 100;
            }
        }));
    }

    private void CompleteGenerationProgress(ChatRequestProgressViewModel item)
    {
        item.StopElapsedTimer();
        ChatRequestProgressItems.Remove(item);
    }

    private static string BuildGenerationSpeedLabel(LocalLlmGenerationProgress progress, double elapsedSeconds)
    {
        var evalSeconds = progress.EvalDurationNanoseconds is > 0
            ? progress.EvalDurationNanoseconds.Value / 1_000_000_000d
            : elapsedSeconds;
        if (progress.EvalCount is > 0 && evalSeconds > 0)
        {
            return $"{progress.EvalCount.Value / evalSeconds:0.#} tok/s";
        }

        return elapsedSeconds > 0 ? $"pending {elapsedSeconds:0.0}s" : "speed pending";
    }

    private static string BuildGenerationSizeLabel(LocalLlmGenerationProgress progress)
    {
        if (progress.PromptEvalCount is { } promptCount && progress.EvalCount is { } evalCount)
        {
            return $"{promptCount:N0} in / {evalCount:N0} out";
        }

        if (progress.EvalCount is { } outputCount)
        {
            return $"{outputCount:N0} output tok";
        }

        return "loading";
    }

    private void AppendTerminalOutput(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        var normalized = line
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (normalized.Length == 0)
        {
            return;
        }

        foreach (var item in normalized)
        {
            var cleanItem = NormalizeTerminalDisplayText(item);
            if (string.IsNullOrWhiteSpace(cleanItem))
            {
                continue;
            }

            _terminalOutputBuilder.Append('[');
            _terminalOutputBuilder.Append(DateTime.Now.ToString("HH:mm:ss"));
            _terminalOutputBuilder.Append("] ");
            _terminalOutputBuilder.AppendLine(cleanItem);
        }

        const int maxTerminalCharacters = 20000;
        if (_terminalOutputBuilder.Length > maxTerminalCharacters)
        {
            _terminalOutputBuilder.Remove(0, _terminalOutputBuilder.Length - maxTerminalCharacters);
        }

        TerminalOutputText = _terminalOutputBuilder.ToString();
    }

    private static string NormalizeTerminalDisplayText(string text)
    {
        var clean = AnsiControlRegex().Replace(text ?? "", "")
            .Replace('\0', ' ')
            .Replace('\b', ' ')
            .Trim();
        return RepairUtf8Mojibake(clean);
    }

    private static string RepairUtf8Mojibake(string text)
    {
        if (string.IsNullOrWhiteSpace(text) ||
            (!text.Contains('â', StringComparison.Ordinal) &&
             !text.Contains('Ð', StringComparison.Ordinal) &&
             !text.Contains('Ñ', StringComparison.Ordinal)))
        {
            return text;
        }

        var bytes = new byte[text.Length];
        var count = 0;
        foreach (var ch in text)
        {
            if (!TryMapWindows1252Byte(ch, out var value))
            {
                return text;
            }

            bytes[count++] = value;
        }

        try
        {
            var repaired = Encoding.UTF8.GetString(bytes, 0, count);
            return ReadabilityScore(repaired) > ReadabilityScore(text) ? repaired : text;
        }
        catch
        {
            return text;
        }
    }

    private static bool TryMapWindows1252Byte(char ch, out byte value)
    {
        if (ch <= 0x7F || (ch >= 0xA0 && ch <= 0xFF))
        {
            value = (byte)ch;
            return true;
        }

        value = ch switch
        {
            '€' => 0x80,
            '‚' => 0x82,
            'ƒ' => 0x83,
            '„' => 0x84,
            '…' => 0x85,
            '†' => 0x86,
            '‡' => 0x87,
            'ˆ' => 0x88,
            '‰' => 0x89,
            'Š' => 0x8A,
            '‹' => 0x8B,
            'Œ' => 0x8C,
            'Ž' => 0x8E,
            '‘' => 0x91,
            '’' => 0x92,
            '“' => 0x93,
            '”' => 0x94,
            '•' => 0x95,
            '–' => 0x96,
            '—' => 0x97,
            '˜' => 0x98,
            '™' => 0x99,
            'š' => 0x9A,
            '›' => 0x9B,
            'œ' => 0x9C,
            'ž' => 0x9E,
            'Ÿ' => 0x9F,
            _ => 0
        };
        return value != 0;
    }

    private static int ReadabilityScore(string text)
    {
        var score = 0;
        foreach (var ch in text)
        {
            if (ch is >= 'А' and <= 'я' or 'ё' or 'Ё' or >= '\u2500' and <= '\u259F')
            {
                score += 4;
            }
            else if (ch == '\uFFFD')
            {
                score -= 10;
            }
            else if (ch is 'â' or 'Ð' or 'Ñ')
            {
                score -= 2;
            }
            else if (!char.IsControl(ch))
            {
                score++;
            }
        }

        return score;
    }

    [GeneratedRegex(@"\x1B\[[0-?]*[ -/]*[@-~]|\x1B\][^\a]*(?:\a|\x1B\\)", RegexOptions.CultureInvariant)]
    private static partial Regex AnsiControlRegex();

    private void ClearTerminalOutput()
    {
        _terminalOutputBuilder.Clear();
        TerminalOutputText = "";
    }

    private void MirrorPhaseStatusToTerminal()
    {
        if (string.IsNullOrWhiteSpace(PhaseTitle) && string.IsNullOrWhiteSpace(PhaseDetail))
        {
            return;
        }

        var line = string.IsNullOrWhiteSpace(PhaseDetail)
            ? $"STATUS [{CurrentCcStationLabel}] {PhaseTitle}"
            : $"STATUS [{CurrentCcStationLabel}] {PhaseTitle}: {PhaseDetail}";
        if (line.Equals(_lastMirroredStatusLine, StringComparison.Ordinal))
        {
            return;
        }

        _lastMirroredStatusLine = line;
        AppendTerminalOutput(line);
    }

    private void MoveToCcStage(int stageIndex)
    {
        var bounded = Math.Clamp(stageIndex, 0, Math.Max(0, CcTimelineStages.Count - 1));
        if (_currentCcTimelineStageIndex == bounded)
        {
            return;
        }

        _currentCcTimelineStageIndex = bounded;
        UpdateCcTimelineState();
        OnPropertyChanged(nameof(CurrentCcStationLabel));
    }

    private void ToggleAutopilotMode()
    {
        IsAutopilotEnabled = !IsAutopilotEnabled;
        IsPromptOpen = true;
        IsCcTimelineExpanded = true;
        MoveToCcStage(CcStageRequest);
        PhaseTitle = IsAutopilotEnabled ? "CC flow" : "Raw";
        PhaseDetail = IsAutopilotEnabled
            ? "Phase 1: write the request, run DIR, then send the project map so the model returns a minimal CC list."
            : "Raw chat: Send passes only your prompt text to the selected model.";
        AppendTerminalOutput(IsAutopilotEnabled
            ? "CC flow enabled: DIR + Request -> CC -> GO."
            : "Raw enabled: clean chat with no ContextControl capsule, attachments, or workflow instructions.");
    }

    private void UpdateCcTimelineFromStatus()
    {
        var stage = ResolveCcStageIndex(PhaseTitle, PhaseDetail);
        if (stage >= 0)
        {
            MoveToCcStage(stage);
        }
    }

    private void UpdateCcTimelineState()
    {
        for (var index = 0; index < CcTimelineStages.Count; index++)
        {
            CcTimelineStages[index].ApplyState(index == _currentCcTimelineStageIndex, index < _currentCcTimelineStageIndex);
        }
    }

    private static int ResolveCcStageIndex(string title, string detail)
    {
        var text = $"{title} {detail}";
        if (text.Contains("DIR", StringComparison.OrdinalIgnoreCase)
            || text.Contains("scope", StringComparison.OrdinalIgnoreCase)
            || text.Contains("tree", StringComparison.OrdinalIgnoreCase))
        {
            return CcStageRequest;
        }

        if (text.Contains("Raw", StringComparison.OrdinalIgnoreCase)
            || text.Contains("CC flow", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Ready", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Nothing to send", StringComparison.OrdinalIgnoreCase))
        {
            return CcStageRequest;
        }

        if (text.Contains("CC export", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Context ready", StringComparison.OrdinalIgnoreCase)
            || text.Contains("CC request", StringComparison.OrdinalIgnoreCase)
            || text.Contains("source context", StringComparison.OrdinalIgnoreCase)
            || text.Contains("resolver", StringComparison.OrdinalIgnoreCase)
            || text.Contains("file request", StringComparison.OrdinalIgnoreCase)
            || text.Contains("FIND", StringComparison.OrdinalIgnoreCase))
        {
            return text.Contains("DIR", StringComparison.OrdinalIgnoreCase)
                ? CcStageRequest
                : CcStageExport;
        }

        if (text.Contains("patch write", StringComparison.OrdinalIgnoreCase)
            || text.Contains("patch review", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Local CC chat", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Local answer", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Patch answer", StringComparison.OrdinalIgnoreCase))
        {
            return CcStagePatch;
        }

        if (text.Contains("GO preview", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Patch planned", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Patch preview", StringComparison.OrdinalIgnoreCase))
        {
            return CcStagePreview;
        }

        if (text.Contains("GO apply", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Applying patch", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Patch applied", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Patch failed", StringComparison.OrdinalIgnoreCase))
        {
            return CcStageApply;
        }

        return -1;
    }

    private void BeginTransferProgress(
        string title,
        CancellationTokenSource? cancellationSource = null,
        bool revealTerminal = true)
    {
        if (revealTerminal)
        {
            IsPromptOpen = true;
            PromptModeKey = "terminal";
        }

        TransferProgressTitle = title;
        _transferProgressHistory.Clear();
        _transferProgressHistoryIndex = -1;
        _isTransferProgressDismissible = false;
        _transferProgressCancellation = cancellationSource;
        _lastTransferProgressUiUpdateUtc = DateTime.MinValue;
        _lastTransferProgressOperation = "";
        _lastTransferProgressStatus = "";
        _lastTransferProgressPercent = null;
        AddTransferProgressStatus("Starting...");
        TransferProgressSizeLabel = "0 B / ?";
        TransferProgressSpeedLabel = "0 B/s";
        TransferProgressPercentLabel = "";
        TransferProgressValue = 0;
        IsTransferProgressIndeterminate = true;
        IsTransferProgressActive = true;
        RaiseTransferProgressCommandStates();
    }

    private void UpdateTransferProgress(LocalLlmTransferProgress progress)
    {
        var status = string.IsNullOrWhiteSpace(progress.Status) ? progress.Operation : progress.Status;
        if (ShouldSkipTransferProgressUpdate(progress.Operation, status, progress.Percent))
        {
            return;
        }

        TransferProgressTitle = progress.Operation;
        AddTransferProgressStatus(status);
        IsTransferProgressIndeterminate = progress.Percent is null;
        if (progress.Percent is { } percent)
        {
            TransferProgressValue = percent;
            TransferProgressPercentLabel = $"{percent:0.#}%";
        }
        else
        {
            TransferProgressPercentLabel = "";
        }

        TransferProgressSizeLabel = string.Equals(progress.Operation, "Refreshing models", StringComparison.OrdinalIgnoreCase)
            && progress.TotalBytes is > 0 and <= 10
            ? $"{progress.CurrentBytes ?? 0}/{progress.TotalBytes} stages"
            : FormatTransferSize(progress.CurrentBytes, progress.TotalBytes);
        TransferProgressSpeedLabel = progress.BytesPerSecond is { } speed && speed > 0
            ? $"{FormatBytes((long)speed)}/s"
            : "speed pending";
        RaiseTransferProgressCommandStates();
    }

    private bool ShouldSkipTransferProgressUpdate(string operation, string status, double? percent)
    {
        var now = DateTime.UtcNow;
        var sameText = string.Equals(operation, _lastTransferProgressOperation, StringComparison.Ordinal)
            && string.Equals(status, _lastTransferProgressStatus, StringComparison.Ordinal);
        var percentDelta = percent.HasValue && _lastTransferProgressPercent.HasValue
            ? Math.Abs(percent.Value - _lastTransferProgressPercent.Value)
            : percent.HasValue == _lastTransferProgressPercent.HasValue ? 0 : 100;

        if (sameText && percentDelta < 0.2 && now - _lastTransferProgressUiUpdateUtc < TimeSpan.FromMilliseconds(90))
        {
            return true;
        }

        _lastTransferProgressUiUpdateUtc = now;
        _lastTransferProgressOperation = operation;
        _lastTransferProgressStatus = status;
        _lastTransferProgressPercent = percent;
        return false;
    }

    private void CompleteTransferProgress(string status, bool succeeded)
    {
        _transferProgressCancellation = null;
        AddTransferProgressStatus(status);
        TransferProgressSpeedLabel = succeeded ? "complete" : "stopped";
        TransferProgressValue = succeeded ? 100 : TransferProgressValue;
        TransferProgressPercentLabel = succeeded ? "100%" : TransferProgressPercentLabel;
        IsTransferProgressIndeterminate = false;

        IsTransferProgressActive = false;
        RaiseTransferProgressCommandStates();
    }

    private void CompleteDependencyTransferProgress(string status, bool succeeded)
    {
        _transferProgressCancellation = null;
        AddTransferProgressStatus(status);
        TransferProgressSpeedLabel = succeeded ? "complete" : "stopped";
        TransferProgressValue = succeeded ? 100 : TransferProgressValue;
        TransferProgressPercentLabel = succeeded ? "100%" : "error";
        IsTransferProgressIndeterminate = false;
        IsTransferProgressActive = true;
        _isTransferProgressDismissible = true;
        RaiseTransferProgressCommandStates();
    }

    private void AddTransferProgressStatus(string status)
    {
        var clean = (status ?? "").Trim();
        if (string.IsNullOrWhiteSpace(clean))
        {
            return;
        }

        if (_transferProgressHistory.Count == 0
            || !string.Equals(_transferProgressHistory[^1], clean, StringComparison.Ordinal))
        {
            _transferProgressHistory.Add(clean);
            if (_transferProgressHistory.Count > 200)
            {
                _transferProgressHistory.RemoveAt(0);
            }
        }

        _transferProgressHistoryIndex = _transferProgressHistory.Count - 1;
        TransferProgressStatus = clean;
        OnPropertyChanged(nameof(TransferProgressHistoryPositionLabel));
        RaiseTransferProgressCommandStates();
    }

    private bool CanMoveTransferProgressHistory(int delta)
    {
        var next = _transferProgressHistoryIndex + delta;
        return _transferProgressHistory.Count > 0 && next >= 0 && next < _transferProgressHistory.Count;
    }

    private void MoveTransferProgressHistory(int delta)
    {
        if (!CanMoveTransferProgressHistory(delta))
        {
            return;
        }

        _transferProgressHistoryIndex += delta;
        TransferProgressStatus = _transferProgressHistory[_transferProgressHistoryIndex];
        RaiseTransferProgressCommandStates();
    }

    private void CloseTransferProgress()
    {
        if (_transferProgressCancellation is { IsCancellationRequested: false } cancellation)
        {
            cancellation.Cancel();
            AddTransferProgressStatus("Stopping...");
            TransferProgressSpeedLabel = "stopping";
            TransferProgressPercentLabel = "cancel";
            RaiseTransferProgressCommandStates();
            return;
        }

        if (!CanCloseTransferProgress)
        {
            return;
        }

        IsTransferProgressActive = false;
        _isTransferProgressDismissible = false;
        RaiseTransferProgressCommandStates();
    }

    private void RaiseTransferProgressCommandStates()
    {
        OnPropertyChanged(nameof(CanCloseTransferProgress));
        OnPropertyChanged(nameof(IsTransferProgressLoading));
        OnPropertyChanged(nameof(TransferProgressHistoryPositionLabel));
        (PreviousTransferStatusCommand as RelayCommand<object>)?.RaiseCanExecuteChanged();
        (NextTransferStatusCommand as RelayCommand<object>)?.RaiseCanExecuteChanged();
        (CloseTransferProgressCommand as RelayCommand<object>)?.RaiseCanExecuteChanged();
    }

    private static string FormatTransferSize(long? currentBytes, long? totalBytes)
    {
        var current = currentBytes is { } currentValue ? FormatBytes(currentValue) : "0 B";
        var total = totalBytes is { } totalValue && totalValue > 0 ? FormatBytes(totalValue) : "?";
        return $"{current} / {total}";
    }

    private static string FormatBytes(long bytes)
    {
        var value = Math.Max(0, bytes);
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var number = (double)value;
        var unitIndex = 0;
        while (number >= 1024 && unitIndex < units.Length - 1)
        {
            number /= 1024;
            unitIndex++;
        }

        return unitIndex == 0
            ? $"{number:0} {units[unitIndex]}"
            : $"{number:0.#} {units[unitIndex]}";
    }

}
