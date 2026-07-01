// CC-DESC: Runs Codex CLI inside a ContextControl-owned read-only capsule harness.

using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace ContextControl.Workbench.Services;

public sealed record CodexHarnessRequest(
    string UserMessage,
    ContextCapsulePhase Phase,
    string ContextControlRoot,
    IReadOnlyList<ContextCapsuleAttachment> Attachments,
    string CodexInstructions,
    string EnabledSkillbookInstructions,
    string Model = "",
    string ReasoningEffort = "");

public sealed record CodexHarnessResult(
    bool Succeeded,
    string Status,
    string Message,
    string Thinking,
    string EventTrace,
    int ExitCode,
    CodexUsageSnapshot? UsageSnapshot = null);

public sealed record CodexTokenUsage(
    long? InputTokens,
    long? CachedInputTokens,
    long? OutputTokens,
    long? ReasoningOutputTokens,
    long? TotalTokens)
{
    public long? EffectiveTotalTokens => TotalTokens
        ?? (InputTokens.HasValue || OutputTokens.HasValue
            ? Math.Max(0, InputTokens ?? 0) + Math.Max(0, OutputTokens ?? 0)
            : null);

    public string Summary
    {
        get
        {
            var parts = new List<string>();
            if (InputTokens is { } input)
            {
                parts.Add($"{input:N0} in");
            }

            if (CachedInputTokens is > 0)
            {
                parts.Add($"{CachedInputTokens.Value:N0} cached");
            }

            if (OutputTokens is { } output)
            {
                parts.Add($"{output:N0} out");
            }

            if (ReasoningOutputTokens is > 0)
            {
                parts.Add($"{ReasoningOutputTokens.Value:N0} reasoning");
            }

            if (EffectiveTotalTokens is { } total)
            {
                parts.Add($"{total:N0} total");
            }

            return parts.Count == 0 ? "token usage pending" : string.Join(", ", parts);
        }
    }

    public LocalLlmUsageStats ToLocalLlmUsageStats()
    {
        return new LocalLlmUsageStats(
            InputTokens,
            OutputTokens,
            null,
            null,
            null,
            null,
            CachedInputTokens,
            ReasoningOutputTokens);
    }
}

public sealed record CodexRateLimitWindow(double? UsedPercent, int? WindowMinutes, DateTimeOffset? ResetsAt)
{
    public string Format(string fallbackLabel)
    {
        var label = WindowMinutes switch
        {
            300 => "5h",
            10080 => "weekly",
            > 0 => $"{WindowMinutes.Value:N0}m",
            _ => fallbackLabel
        };
        var used = UsedPercent is { } usedPercent ? $"{usedPercent:0.#}% used" : "usage ?";
        var reset = ResetsAt is { } resetAt ? $"resets {FormatReset(resetAt)}" : "reset ?";
        return $"{label}: {used}, {reset}";
    }

    private static string FormatReset(DateTimeOffset resetAt)
    {
        var local = resetAt.ToLocalTime();
        return local.Date == DateTimeOffset.Now.Date
            ? local.ToString("HH:mm")
            : local.ToString("MMM d HH:mm");
    }
}

public sealed record CodexRateLimitSnapshot(CodexRateLimitWindow? Primary, CodexRateLimitWindow? Secondary)
{
    public string Summary
    {
        get
        {
            var parts = new List<string>();
            if (Primary is not null)
            {
                parts.Add(Primary.Format("primary"));
            }

            if (Secondary is not null)
            {
                parts.Add(Secondary.Format("secondary"));
            }

            return parts.Count == 0
                ? ""
                : "Codex limits: " + string.Join("; ", parts);
        }
    }
}

public sealed record CodexUsageSnapshot(
    CodexTokenUsage? LastTokenUsage,
    CodexTokenUsage? TotalTokenUsage,
    CodexRateLimitSnapshot? RateLimits)
{
    public string UsageSummary
    {
        get
        {
            if (LastTokenUsage is null)
            {
                return TotalTokenUsage is null
                    ? "Codex usage pending."
                    : $"Codex session total: {TotalTokenUsage.Summary}.";
            }

            var summary = $"Last prompt: {LastTokenUsage.Summary}.";
            return TotalTokenUsage?.EffectiveTotalTokens is { } total
                ? $"{summary} Session total: {total:N0} tokens."
                : summary;
        }
    }

    public string RateLimitSummary => RateLimits?.Summary ?? "";
}

public sealed record CodexHarnessExecutionPlan(
    string HarnessRoot,
    string PromptPath,
    string LastMessagePath,
    IReadOnlyList<string> Arguments);

public sealed record CodexAvailabilityResult(
    bool Available,
    string Status,
    string Version,
    bool IsAuthenticated = false,
    bool RequiresLogin = false,
    bool RequiresInstall = false);

public sealed record CodexLoginLaunchResult(
    bool Succeeded,
    string Status);

public sealed class CodexHarnessService
{
    public const string OfficialGuideUrl = "https://developers.openai.com/codex/cli";

    private const int MaxAttachmentCharacters = 512_000;
    private const int MaxEventTraceLines = 80;
    private const int MaxUsageLogTailBytes = 512 * 1024;
    private static readonly TimeSpan FinalTurnExitGrace = TimeSpan.FromSeconds(5);
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

    public async Task<CodexAvailabilityResult> CheckAvailabilityAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var versionResult = await RunCodexCommandAsync(["--version"], TimeSpan.FromSeconds(5), cancellationToken);
            var version = versionResult.StandardOutput.Trim();
            if (versionResult.ExitCode != 0 || string.IsNullOrWhiteSpace(version))
            {
                var detail = FirstInterestingLine(versionResult.StandardError)
                    ?? FirstInterestingLine(versionResult.StandardOutput)
                    ?? $"Codex --version exited {versionResult.ExitCode}";
                return new CodexAvailabilityResult(
                    false,
                    $"Codex CLI was not found. Click Install Codex for OS setup, or Guide for the official setup page. Detail: {detail}",
                    "",
                    RequiresInstall: true);
            }

            var loginResult = await RunCodexCommandAsync(["login", "status"], TimeSpan.FromSeconds(5), cancellationToken);
            var loginText = $"{loginResult.StandardOutput}{Environment.NewLine}{loginResult.StandardError}".Trim();
            if (loginResult.ExitCode == 0 && IsLoggedInStatus(loginText))
            {
                var loginLine = FirstInterestingLine(loginText) ?? "logged in";
                return new CodexAvailabilityResult(
                    true,
                    $"Codex CLI ready: {version}; {loginLine}",
                    version,
                    IsAuthenticated: true,
                    RequiresLogin: false);
            }

            var loginDetail = FirstInterestingLine(loginText) ?? "not logged in";
            if (IsLoginRequiredText(loginText) || loginResult.ExitCode != 0)
            {
                return new CodexAvailabilityResult(
                    true,
                    $"Codex CLI installed ({version}), but login is required. Click Login Codex, complete auth, then Refresh Codex. Detail: {loginDetail}",
                    version,
                    IsAuthenticated: false,
                    RequiresLogin: true);
            }

            return new CodexAvailabilityResult(
                true,
                $"Codex CLI installed ({version}), but authentication status is unclear. Click Refresh Codex or run Codex Doctor.",
                version,
                IsAuthenticated: false,
                RequiresLogin: true);
        }
        catch (OperationCanceledException)
        {
            return new CodexAvailabilityResult(false, "Codex CLI check timed out.", "");
        }
        catch (Exception ex)
        {
            return new CodexAvailabilityResult(false, $"Codex CLI unavailable: {ex.Message}", "");
        }
    }

    public CodexLoginLaunchResult LaunchInstall()
    {
        if (OperatingSystem.IsWindows())
        {
            var scriptLines = new[]
            {
                "$Host.UI.RawUI.WindowTitle='ContextControl Codex Setup'",
                "Write-Host 'Installing Codex CLI with winget package OpenAI.Codex...'",
                "winget install --id OpenAI.Codex --source winget --accept-package-agreements --accept-source-agreements",
                "Write-Host ''",
                "Write-Host 'If install completed, return to ContextControl and click Refresh Codex.'",
                "Write-Host 'If winget was blocked or unavailable, use the Guide button.'",
                "Read-Host 'Press Enter to close'"
            };

            if (TryStartWindowsPowerShellScript("ContextControl Codex Setup", scriptLines, out var usedWindowsTerminal, out _))
            {
                var terminal = usedWindowsTerminal ? "Windows Terminal" : "PowerShell";
                return new CodexLoginLaunchResult(true, $"Opened Codex CLI installer in {terminal}. It uses winget package OpenAI.Codex; click Refresh Codex when it finishes.");
            }

            return new CodexLoginLaunchResult(false, "Could not open a terminal for the Codex CLI installer. Use Guide, or run: winget install --id OpenAI.Codex --source winget");
        }

        if (OperatingSystem.IsMacOS())
        {
            var shellCommand = "curl -fsSL https://chatgpt.com/codex/install.sh | CODEX_NON_INTERACTIVE=1 sh; echo; echo 'Return to ContextControl and click Refresh Codex.'; read -r -p 'Press Enter to close' _";
            var script = $"tell application \"Terminal\" to do script \"{EscapeAppleScriptDoubleQuotedString(shellCommand)}\"";
            if (TryStartProcess("osascript", ["-e", script], out var macError))
            {
                return new CodexLoginLaunchResult(true, "Opened Codex CLI installer in Terminal using the official standalone installer. Click Refresh Codex when it finishes.");
            }

            return new CodexLoginLaunchResult(false, $"Could not open Terminal for Codex CLI setup: {macError}. Use Guide, or run the official installer from the Codex CLI page.");
        }

        var linuxCommand = "curl -fsSL https://chatgpt.com/codex/install.sh | CODEX_NON_INTERACTIVE=1 sh; echo; echo 'Return to ContextControl and click Refresh Codex.'; read -r -p 'Press Enter to close' _";
        string[][] linuxLaunchers =
        [
            ["x-terminal-emulator", "-e", "bash", "-lc", linuxCommand],
            ["gnome-terminal", "--", "bash", "-lc", linuxCommand],
            ["konsole", "-e", "bash", "-lc", linuxCommand],
            ["xterm", "-e", "bash", "-lc", linuxCommand]
        ];

        var lastError = "";
        foreach (var launcher in linuxLaunchers)
        {
            if (TryStartProcess(launcher[0], launcher.Skip(1).ToArray(), out lastError))
            {
                return new CodexLoginLaunchResult(true, "Opened Codex CLI installer in a terminal using the official standalone installer. Click Refresh Codex when it finishes.");
            }
        }

        return new CodexLoginLaunchResult(false, $"Could not open a terminal for Codex CLI setup: {lastError}. Use Guide, or run the official installer from the Codex CLI page.");
    }

    public CodexLoginLaunchResult OpenOfficialGuide()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = OfficialGuideUrl,
                UseShellExecute = true
            });
            return new CodexLoginLaunchResult(true, "Opened the official Codex CLI setup guide.");
        }
        catch (Exception ex)
        {
            return new CodexLoginLaunchResult(false, $"Could not open the Codex CLI setup guide: {ex.Message}");
        }
    }

    public CodexLoginLaunchResult LaunchInteractiveLogin()
    {
        var codexExecutable = ResolveCodexExecutable();
        if (string.IsNullOrWhiteSpace(codexExecutable))
        {
            return new CodexLoginLaunchResult(false, "Codex CLI was not found. Install Codex CLI, then click Refresh Codex.");
        }

        if (OperatingSystem.IsWindows())
        {
            var scriptLines = new[]
            {
                "$Host.UI.RawUI.WindowTitle='ContextControl Codex Login'",
                $"& '{EscapePowerShellSingleQuotedString(codexExecutable)}' login",
                "Write-Host ''",
                "Write-Host 'Return to ContextControl and click Refresh Codex.'",
                "Read-Host 'Press Enter to close'"
            };

            if (TryStartWindowsPowerShellScript("ContextControl Codex Login", scriptLines, out var usedWindowsTerminal, out _))
            {
                var terminal = usedWindowsTerminal ? "Windows Terminal" : "PowerShell";
                return new CodexLoginLaunchResult(true, $"Opened Codex login in {terminal}. Complete the browser/device auth, then click Refresh Codex.");
            }

            var cmdCommand = $"\"{codexExecutable}\" login & echo. & echo Return to ContextControl and click Refresh Codex. & pause";
            if (TryStartProcess(
                    "cmd.exe",
                    ["/c", cmdCommand],
                    out var cmdError))
            {
                return new CodexLoginLaunchResult(true, "Opened Codex login in Command Prompt. Complete auth, then click Refresh Codex.");
            }

            return new CodexLoginLaunchResult(false, $"Could not open a terminal for Codex login: {cmdError}. Open a terminal and run: codex login");
        }

        if (OperatingSystem.IsMacOS())
        {
            var script = $"tell application \"Terminal\" to do script \"'{EscapeShellSingleQuotedString(codexExecutable)}' login; echo ''; echo Return to ContextControl and click Refresh Codex.; read -r -p 'Press Enter to close' _\"";
            if (TryStartProcess("osascript", ["-e", script], out var macError))
            {
                return new CodexLoginLaunchResult(true, "Opened Codex login in Terminal. Complete auth, then click Refresh Codex.");
            }

            return new CodexLoginLaunchResult(false, $"Could not open Terminal for Codex login: {macError}. Open a terminal and run: codex login");
        }

        var shellCommand = $"'{EscapeShellSingleQuotedString(codexExecutable)}' login; echo; echo 'Return to ContextControl and click Refresh Codex.'; read -r -p 'Press Enter to close' _";
        string[][] linuxLaunchers =
        [
            ["x-terminal-emulator", "-e", "bash", "-lc", shellCommand],
            ["gnome-terminal", "--", "bash", "-lc", shellCommand],
            ["konsole", "-e", "bash", "-lc", shellCommand],
            ["xterm", "-e", "bash", "-lc", shellCommand]
        ];

        var lastError = "";
        foreach (var launcher in linuxLaunchers)
        {
            if (TryStartProcess(launcher[0], launcher.Skip(1).ToArray(), out lastError))
            {
                return new CodexLoginLaunchResult(true, "Opened Codex login in a terminal. Complete auth, then click Refresh Codex.");
            }
        }

        return new CodexLoginLaunchResult(false, $"Could not open a terminal for Codex login: {lastError}. Open a terminal and run: codex login");
    }

    public async Task<CodexLoginLaunchResult> LogoutAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await RunCodexCommandAsync(["logout"], TimeSpan.FromSeconds(15), cancellationToken);
            var detail = FirstInterestingLine(result.StandardOutput)
                ?? FirstInterestingLine(result.StandardError)
                ?? $"Codex logout exited {result.ExitCode}";
            return result.ExitCode == 0
                ? new CodexLoginLaunchResult(true, $"Codex logged out: {detail}")
                : new CodexLoginLaunchResult(false, $"Codex logout failed: {detail}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new CodexLoginLaunchResult(false, $"Codex logout failed: {ex.Message}");
        }
    }

    public async Task<CodexLoginLaunchResult> RunDoctorAsync(
        IProgress<string>? terminal = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await RunCodexCommandAsync(["doctor"], TimeSpan.FromSeconds(30), cancellationToken);
            foreach (var line in InterestingLines(result.StandardOutput).Concat(InterestingLines(result.StandardError)).Take(80))
            {
                terminal?.Report(line);
            }

            var detail = FirstInterestingLine(result.StandardError)
                ?? FirstInterestingLine(result.StandardOutput)
                ?? $"Codex doctor exited {result.ExitCode}";
            return result.ExitCode == 0
                ? new CodexLoginLaunchResult(true, $"Codex doctor passed: {detail}")
                : new CodexLoginLaunchResult(false, $"Codex doctor found an issue: {detail}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new CodexLoginLaunchResult(false, $"Codex doctor failed: {ex.Message}");
        }
    }

    public async Task<CodexHarnessResult> SendAsync(
        CodexHarnessRequest request,
        IProgress<LocalLlmGenerationProgress>? progress = null,
        IProgress<string>? terminal = null,
        CancellationToken cancellationToken = default)
    {
        var startedUtc = DateTime.UtcNow;
        var root = string.IsNullOrWhiteSpace(request.ContextControlRoot)
            ? AppContext.BaseDirectory
            : request.ContextControlRoot;
        var plan = BuildExecutionPlan(root, request.Model, request.ReasoningEffort);
        Directory.CreateDirectory(plan.HarnessRoot);

        var prompt = BuildPrompt(request);
        await File.WriteAllTextAsync(plan.PromptPath, prompt, Utf8NoBom, cancellationToken);
        TryDelete(plan.LastMessagePath);

        progress?.Report(new LocalLlmGenerationProgress("Starting Codex CLI...", null, null, null, null, null, null, null, false));
        terminal?.Report("Codex harness uses an empty working directory and read-only sandbox.");
        terminal?.Report($"Codex capsule written: {plan.PromptPath}");

        var startInfo = new ProcessStartInfo
        {
            FileName = ResolveCodexExecutable() ?? "codex",
            WorkingDirectory = plan.HarnessRoot,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = Utf8NoBom,
            StandardOutputEncoding = Utf8NoBom,
            StandardErrorEncoding = Utf8NoBom,
            CreateNoWindow = true
        };

        foreach (var argument in plan.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        var outputBuilder = new StringBuilder();
        var errorBuilder = new StringBuilder();
        var eventTrace = new List<string>();
        var thinking = new StringBuilder();
        var messageCandidates = new List<string>();
        var finalTurnCompleted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var finalTurnObserved = false;
        CodexUsageSnapshot? latestUsage = null;
        string lastReportedUsage = "";
        string lastReportedRateLimit = "";
        var reportedPrivateReasoningStep = false;

        Process? process = null;
        try
        {
            process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            process.Start();

            await process.StandardInput.WriteAsync(prompt);
            process.StandardInput.Close();

            var stdoutTask = Task.Run(async () =>
            {
                while (!process.StandardOutput.EndOfStream)
                {
                    var line = await process.StandardOutput.ReadLineAsync(cancellationToken);
                    if (line is null)
                    {
                        break;
                    }

                    outputBuilder.AppendLine(line);
                    var parsed = ParseJsonEvent(line);
                    if (!string.IsNullOrWhiteSpace(parsed.TraceLine))
                    {
                        if (eventTrace.Count < MaxEventTraceLines)
                        {
                            eventTrace.Add(parsed.TraceLine);
                        }

                        terminal?.Report(parsed.TraceLine);
                    }

                    if (!string.IsNullOrWhiteSpace(parsed.Thinking))
                    {
                        if (thinking.Length > 0)
                        {
                            thinking.AppendLine();
                        }

                        thinking.AppendLine(parsed.Thinking.Trim());
                    }

                    if (!string.IsNullOrWhiteSpace(parsed.Message))
                    {
                        messageCandidates.Add(parsed.Message.Trim());
                    }

                    if (parsed.Usage is not null)
                    {
                        latestUsage = MergeUsageSnapshots(latestUsage, parsed.Usage);
                        ReportUsageToTerminal(latestUsage, terminal, ref lastReportedUsage, ref lastReportedRateLimit);
                    }

                    if (parsed.IsTerminalTurn
                        && (messageCandidates.Count > 0 || File.Exists(plan.LastMessagePath)))
                    {
                        finalTurnObserved = true;
                        finalTurnCompleted.TrySetResult(true);
                    }

                    var thinkingDelta = parsed.Thinking;
                    if (string.Equals(thinkingDelta, "Codex reported a private reasoning step.", StringComparison.OrdinalIgnoreCase))
                    {
                        if (reportedPrivateReasoningStep)
                        {
                            thinkingDelta = "";
                        }
                        else
                        {
                            reportedPrivateReasoningStep = true;
                        }
                    }

                    var status = string.IsNullOrWhiteSpace(parsed.TraceLine) ? "Codex event received..." : parsed.TraceLine;
                    if (parsed.Usage is not null && latestUsage is not null)
                    {
                        status = latestUsage.UsageSummary;
                    }

                    progress?.Report(new LocalLlmGenerationProgress(
                        status,
                        parsed.Message,
                        latestUsage?.LastTokenUsage?.InputTokens,
                        latestUsage?.LastTokenUsage?.OutputTokens
                            ?? messageCandidates.Sum(candidate => Math.Max(1, candidate.Length / 4)),
                        null,
                        null,
                        null,
                        null,
                        false,
                        thinkingDelta,
                        latestUsage));
                }
            }, cancellationToken);

            var stderrTask = Task.Run(async () =>
            {
                while (!process.StandardError.EndOfStream)
                {
                    var line = await process.StandardError.ReadLineAsync(cancellationToken);
                    if (line is null)
                    {
                        break;
                    }

                    errorBuilder.AppendLine(line);
                    terminal?.Report(line);
                }
            }, cancellationToken);

            var exitTask = process.WaitForExitAsync(cancellationToken);
            var firstCompletion = await Task.WhenAny(exitTask, finalTurnCompleted.Task);
            if (firstCompletion == finalTurnCompleted.Task && !exitTask.IsCompleted)
            {
                var gracefulExitTask = await Task.WhenAny(exitTask, Task.Delay(FinalTurnExitGrace, cancellationToken));
                if (gracefulExitTask != exitTask)
                {
                    terminal?.Report("Codex final response received; stopping lingering CLI process.");
                    TryKill(process);
                }
            }

            await exitTask;
            await Task.WhenAll(stdoutTask, stderrTask);

            if (latestUsage?.RateLimits is null)
            {
                var sessionUsage = TryReadLatestUsageSnapshotFromSessionLogs(startedUtc.AddSeconds(-5));
                if (sessionUsage is not null)
                {
                    latestUsage = MergeUsageSnapshots(
                        latestUsage,
                        sessionUsage,
                        preferIncomingLastTokenUsage: latestUsage?.LastTokenUsage is null);
                    ReportUsageToTerminal(latestUsage, terminal, ref lastReportedUsage, ref lastReportedRateLimit);
                    progress?.Report(new LocalLlmGenerationProgress(
                        string.IsNullOrWhiteSpace(latestUsage.RateLimitSummary)
                            ? latestUsage.UsageSummary
                            : latestUsage.RateLimitSummary,
                        null,
                        latestUsage.LastTokenUsage?.InputTokens,
                        latestUsage.LastTokenUsage?.OutputTokens,
                        null,
                        null,
                        null,
                        null,
                        false,
                        null,
                        latestUsage));
                }
            }

            var finalMessage = await ReadLastMessageAsync(plan.LastMessagePath, cancellationToken);
            if (string.IsNullOrWhiteSpace(finalMessage))
            {
                finalMessage = messageCandidates.LastOrDefault() ?? "";
            }

            var succeeded = !string.IsNullOrWhiteSpace(finalMessage)
                && (process.ExitCode == 0 || finalTurnObserved);
            var status = succeeded
                ? "Codex response ready."
                : BuildFailureStatus(process.ExitCode, errorBuilder.ToString(), outputBuilder.ToString());

            progress?.Report(new LocalLlmGenerationProgress(
                status,
                null,
                latestUsage?.LastTokenUsage?.InputTokens,
                latestUsage?.LastTokenUsage?.OutputTokens,
                null,
                null,
                null,
                null,
                true,
                null,
                latestUsage));
            return new CodexHarnessResult(
                succeeded,
                status,
                finalMessage.Trim(),
                thinking.ToString().Trim(),
                string.Join(Environment.NewLine, eventTrace),
                process.ExitCode,
                latestUsage);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            var status = "Codex stopped by user.";
            progress?.Report(new LocalLlmGenerationProgress(status, null, null, null, null, null, null, null, true, null, latestUsage));
            return new CodexHarnessResult(false, status, "", "", string.Join(Environment.NewLine, eventTrace), -1, latestUsage);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var status = IsLoginRequiredText(ex.Message)
                ? "Codex login required. Click Login Codex, complete auth, then Refresh Codex."
                : $"Codex CLI failed: {ex.Message}";
            progress?.Report(new LocalLlmGenerationProgress(status, null, null, null, null, null, null, null, true, null, latestUsage));
            return new CodexHarnessResult(false, status, "", "", string.Join(Environment.NewLine, eventTrace), -1, latestUsage);
        }
        finally
        {
            process?.Dispose();
        }
    }

    public static string BuildPrompt(CodexHarnessRequest request)
    {
        var builder = new StringBuilder();
        builder.AppendLine("ContextControl Codex harness capsule");
        builder.AppendLine();
        builder.AppendLine("You are running under ContextControl.");
        builder.AppendLine("Your working directory is an empty harness folder by design.");
        builder.AppendLine("The repository context you may use is included below as attachment text.");
        builder.AppendLine("Do not run repository navigation commands or read files outside the capsule for normal CC phases.");
        builder.AppendLine();
        builder.AppendLine($"Phase: {FormatPhase(request.Phase)}");
        builder.AppendLine();

        if (!string.IsNullOrWhiteSpace(request.CodexInstructions))
        {
            builder.AppendLine(request.CodexInstructions.Trim());
            builder.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(request.EnabledSkillbookInstructions))
        {
            builder.AppendLine("Enabled project/global Skillbook instructions:");
            builder.AppendLine(request.EnabledSkillbookInstructions.Trim());
            builder.AppendLine();
        }

        builder.AppendLine("User request:");
        builder.AppendLine(string.IsNullOrWhiteSpace(request.UserMessage) ? "(empty)" : request.UserMessage.Trim());
        builder.AppendLine();

        var included = request.Attachments.Where(attachment => attachment.Included).ToArray();
        builder.AppendLine("Attachment inventory:");
        if (included.Length == 0)
        {
            builder.AppendLine("- none");
        }
        else
        {
            foreach (var attachment in included)
            {
                var text = attachment.Text ?? "";
                builder.AppendLine($"- {attachment.Label} ({attachment.Kind}) BODY_CHARS: {text.Length}; EST_TOKENS: {ContextCapsuleBuilder.EstimateTokens(text)}");
            }
        }

        builder.AppendLine();
        builder.AppendLine("Included ContextControl attachments:");
        var remaining = MaxAttachmentCharacters;
        foreach (var attachment in included)
        {
            var text = attachment.Text ?? "";
            var clipped = text;
            if (remaining <= 0)
            {
                clipped = "";
            }
            else if (clipped.Length > remaining)
            {
                clipped = clipped[..remaining] + Environment.NewLine + ContextCapsuleBuilder.AttachmentClipMarker;
            }

            remaining -= Math.Max(0, clipped.Length);
            builder.AppendLine($"--- ATTACHMENT {attachment.Kind}: {attachment.Label}");
            builder.AppendLine(clipped.TrimEnd());
            builder.AppendLine($"--- END ATTACHMENT {attachment.Label}");
            builder.AppendLine();
        }

        return builder.ToString().TrimEnd();
    }

    public static CodexHarnessExecutionPlan BuildExecutionPlan(
        string contextControlRoot,
        string model = "",
        string reasoningEffort = "")
    {
        var root = string.IsNullOrWhiteSpace(contextControlRoot)
            ? AppContext.BaseDirectory
            : contextControlRoot;
        var harnessRoot = Path.Combine(root, ".tmp", "codex-harness");
        var lastMessagePath = Path.Combine(harnessRoot, "last-codex-message.md");
        var arguments = new List<string>
        {
            "exec",
            "--json",
            "--ephemeral",
            "--sandbox",
            "read-only",
            "--skip-git-repo-check",
            "--ignore-rules",
            "-C",
            harnessRoot,
            "-o",
            lastMessagePath
        };

        if (!string.IsNullOrWhiteSpace(model))
        {
            arguments.Add("--model");
            arguments.Add(model.Trim());
        }

        if (!string.IsNullOrWhiteSpace(reasoningEffort))
        {
            arguments.Add("-c");
            arguments.Add($"model_reasoning_effort={ToTomlStringLiteral(reasoningEffort.Trim())}");
        }

        arguments.Add("-");
        return new CodexHarnessExecutionPlan(
            harnessRoot,
            Path.Combine(harnessRoot, "last-codex-capsule.md"),
            lastMessagePath,
            arguments);
    }

    private static string ToTomlStringLiteral(string value)
    {
        return "\"" + (value ?? "").Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
    }

    private static async Task<string> ReadLastMessageAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            return File.Exists(path)
                ? await File.ReadAllTextAsync(path, cancellationToken)
                : "";
        }
        catch
        {
            return "";
        }
    }

    private static string BuildFailureStatus(int exitCode, string error, string output)
    {
        var combined = $"{error}{Environment.NewLine}{output}";
        if (IsLoginRequiredText(combined))
        {
            return "Codex login required. Click Login Codex, complete auth, then Refresh Codex.";
        }

        var detail = FirstInterestingLine(error) ?? FirstInterestingLine(output);
        return string.IsNullOrWhiteSpace(detail)
            ? $"Codex exited with code {exitCode} and no final message."
            : $"Codex exited with code {exitCode}: {detail}";
    }

    private static string? FirstInterestingLine(string text)
    {
        return (text ?? "")
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(line => !line.StartsWith("{", StringComparison.Ordinal));
    }

    public static bool IsLoginRequiredText(string text)
    {
        var clean = text ?? "";
        return clean.Contains("not logged in", StringComparison.OrdinalIgnoreCase)
            || clean.Contains("login required", StringComparison.OrdinalIgnoreCase)
            || clean.Contains("please login", StringComparison.OrdinalIgnoreCase)
            || clean.Contains("please log in", StringComparison.OrdinalIgnoreCase)
            || clean.Contains("authenticate", StringComparison.OrdinalIgnoreCase)
            || clean.Contains("authentication", StringComparison.OrdinalIgnoreCase)
            || clean.Contains("access token", StringComparison.OrdinalIgnoreCase)
            || clean.Contains("refresh token", StringComparison.OrdinalIgnoreCase)
            || clean.Contains("auth token", StringComparison.OrdinalIgnoreCase)
            || clean.Contains("api key", StringComparison.OrdinalIgnoreCase)
            || clean.Contains("credential", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLoggedInStatus(string text)
    {
        var clean = text ?? "";
        return clean.Contains("logged in", StringComparison.OrdinalIgnoreCase)
            && !clean.Contains("not logged in", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<CodexCommandResult> RunCodexCommandAsync(
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var executable = ResolveCodexExecutable();
        if (string.IsNullOrWhiteSpace(executable))
        {
            return new CodexCommandResult(-1, "", "Codex CLI executable was not found by ContextControl.");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Utf8NoBom,
            StandardErrorEncoding = Utf8NoBom,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var linkedToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linkedToken.CancelAfter(timeout);
        using var process = new Process { StartInfo = startInfo };
        process.Start();
        var outputTask = process.StandardOutput.ReadToEndAsync(linkedToken.Token);
        var errorTask = process.StandardError.ReadToEndAsync(linkedToken.Token);
        await process.WaitForExitAsync(linkedToken.Token);
        return new CodexCommandResult(
            process.ExitCode,
            await outputTask,
            await errorTask);
    }

    private static string? ResolveCodexExecutable()
    {
        var fileNames = OperatingSystem.IsWindows()
            ? new[] { "codex.exe", "codex.cmd", "codex.bat", "codex" }
            : ["codex"];

        foreach (var candidate in EnumerateCodexExecutableCandidates(fileNames))
        {
            try
            {
                var fullPath = Path.GetFullPath(candidate);
                if (File.Exists(fullPath))
                {
                    return fullPath;
                }
            }
            catch
            {
                // Ignore broken install locations.
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumerateCodexExecutableCandidates(IReadOnlyList<string> fileNames)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (OperatingSystem.IsWindows())
        {
            foreach (var candidate in EnumerateWindowsCodexExecutableCandidates())
            {
                if (seen.Add(candidate))
                {
                    yield return candidate;
                }
            }
        }

        foreach (var directory in EnumeratePathDirectories())
        {
            foreach (var fileName in fileNames)
            {
                var candidate = Path.Combine(directory, fileName);
                if (seen.Add(candidate))
                {
                    yield return candidate;
                }
            }
        }
    }

    private static IEnumerable<string> EnumerateWindowsCodexExecutableCandidates()
    {
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetEnvironmentVariable("ProgramW6432") ?? "",
            Environment.GetEnvironmentVariable("ProgramFiles") ?? ""
        };

        foreach (var root in roots.Where(root => !string.IsNullOrWhiteSpace(root)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (var candidate in EnumerateWinGetCodexCandidates(root))
            {
                yield return candidate;
            }
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            yield return Path.Combine(localAppData, "Microsoft", "WinGet", "Links", "codex.exe");
            yield return Path.Combine(localAppData, "Microsoft", "WinGet", "Links", "codex.cmd");
            yield return Path.Combine(localAppData, "Microsoft", "WindowsApps", "codex.exe");
            yield return Path.Combine(localAppData, "Microsoft", "WindowsApps", "codex.cmd");
        }
    }

    private static IEnumerable<string> EnumerateWinGetCodexCandidates(string root)
    {
        var packageRoots = new[]
        {
            Path.Combine(root, "Microsoft", "WinGet", "Packages"),
            Path.Combine(root, "WinGet", "Packages")
        };

        foreach (var packagesRoot in packageRoots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (var packageDirectory in EnumerateDirectoriesSafe(packagesRoot, "OpenAI.Codex_*"))
            {
                yield return Path.Combine(packageDirectory, "codex-x86_64-pc-windows-msvc.exe");
                yield return Path.Combine(packageDirectory, "codex-aarch64-pc-windows-msvc.exe");
                yield return Path.Combine(packageDirectory, "codex.exe");
                yield return Path.Combine(packageDirectory, "codex.cmd");
            }
        }
    }

    private static IEnumerable<string> EnumeratePathDirectories()
    {
        var paths = new List<string?>();
        paths.Add(Environment.GetEnvironmentVariable("PATH"));
        paths.Add(Environment.GetEnvironmentVariable("Path"));

        if (OperatingSystem.IsWindows())
        {
            paths.Add(Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User));
            paths.Add(Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.User));
            paths.Add(Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine));
            paths.Add(Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.Machine));
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths.Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            foreach (var rawDirectory in path!.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                string? candidate = null;
                try
                {
                    var expanded = Environment.ExpandEnvironmentVariables(rawDirectory);
                    var directory = Path.GetFullPath(expanded);
                    if (Directory.Exists(directory) && seen.Add(directory))
                    {
                        candidate = directory;
                    }
                }
                catch
                {
                    // Ignore broken PATH entries.
                }

                if (!string.IsNullOrWhiteSpace(candidate))
                {
                    yield return candidate;
                }
            }
        }
    }

    private static IEnumerable<string> EnumerateDirectoriesSafe(string root, string pattern)
    {
        try
        {
            return Directory.Exists(root)
                ? Directory.EnumerateDirectories(root, pattern, SearchOption.TopDirectoryOnly).ToArray()
                : [];
        }
        catch
        {
            return [];
        }
    }

    private static string ResolveWindowsPowerShellExecutable()
    {
        var systemRoot = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var candidate = Path.Combine(systemRoot, "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
        return File.Exists(candidate) ? candidate : "powershell.exe";
    }

    private static string EscapePowerShellSingleQuotedString(string value)
    {
        return (value ?? "").Replace("'", "''", StringComparison.Ordinal);
    }

    private static bool TryStartWindowsPowerShellScript(
        string title,
        IReadOnlyList<string> scriptLines,
        out bool usedWindowsTerminal,
        out string error)
    {
        usedWindowsTerminal = false;
        error = "";

        string scriptPath;
        try
        {
            scriptPath = CreateTemporaryPowerShellScript(title, scriptLines);
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }

        var powershell = ResolveWindowsPowerShellExecutable();
        if (TryStartProcess(
                "wt.exe",
                ["new-tab", "--title", title, powershell, "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", scriptPath],
                out error))
        {
            usedWindowsTerminal = true;
            return true;
        }

        if (TryStartProcess(
                powershell,
                ["-NoProfile", "-ExecutionPolicy", "Bypass", "-File", scriptPath],
                out error))
        {
            return true;
        }

        return false;
    }

    private static string CreateTemporaryPowerShellScript(string title, IReadOnlyList<string> scriptLines)
    {
        var safeTitle = new string((title ?? "ContextControl Codex")
            .Where(character => char.IsLetterOrDigit(character) || character is '-' or '_')
            .ToArray());
        if (string.IsNullOrWhiteSpace(safeTitle))
        {
            safeTitle = "ContextControlCodex";
        }

        var directory = Path.Combine(Path.GetTempPath(), "ContextControl", "Codex");
        Directory.CreateDirectory(directory);
        var scriptPath = Path.Combine(directory, $"{safeTitle}-{Guid.NewGuid():N}.ps1");
        File.WriteAllText(scriptPath, string.Join(Environment.NewLine, scriptLines) + Environment.NewLine, Utf8NoBom);
        return scriptPath;
    }

    private static string EscapeShellSingleQuotedString(string value)
    {
        return (value ?? "").Replace("'", "'\"'\"'", StringComparison.Ordinal);
    }

    private static string EscapeAppleScriptDoubleQuotedString(string value)
    {
        return (value ?? "")
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
    }

    private static bool TryStartProcess(string fileName, IReadOnlyList<string> arguments, out string error)
    {
        error = "";
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                UseShellExecute = false,
                CreateNoWindow = false
            };
            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            Process.Start(startInfo);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static IEnumerable<string> InterestingLines(string text)
    {
        return (text ?? "")
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.Length <= 240);
    }

    public static CodexUsageSnapshot? TryReadLatestUsageSnapshotFromSessionLogs(DateTime? sinceUtc = null)
    {
        try
        {
            var root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".codex",
                "sessions");
            if (!Directory.Exists(root))
            {
                return null;
            }

            var files = Directory.EnumerateFiles(root, "*.jsonl", new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    IgnoreInaccessible = true
                })
                .Select(path => new FileInfo(path))
                .Where(file => !sinceUtc.HasValue || file.LastWriteTimeUtc >= sinceUtc.Value.ToUniversalTime())
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .Take(12);

            foreach (var file in files)
            {
                CodexUsageSnapshot? latest = null;
                try
                {
                    foreach (var line in ReadRecentUsageLogLines(file.FullName))
                    {
                        if (!line.Contains("\"token_count\"", StringComparison.Ordinal)
                            && !line.Contains("\"rate_limits\"", StringComparison.Ordinal))
                        {
                            continue;
                        }

                        var usage = ParseJsonEvent(line).Usage;
                        if (usage is not null)
                        {
                            latest = MergeUsageSnapshots(latest, usage);
                        }
                    }
                }
                catch
                {
                    continue;
                }

                if (latest is not null)
                {
                    return latest;
                }
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    private static IReadOnlyList<string> ReadRecentUsageLogLines(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length <= 0)
            {
                return [];
            }

            var bytesToRead = (int)Math.Min(info.Length, MaxUsageLogTailBytes);
            var buffer = new byte[bytesToRead];
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            stream.Seek(-bytesToRead, SeekOrigin.End);
            var read = stream.Read(buffer, 0, buffer.Length);
            if (read <= 0)
            {
                return [];
            }

            var text = Utf8NoBom.GetString(buffer, 0, read)
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n');
            var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (info.Length <= bytesToRead || lines.Length <= 1)
            {
                return lines;
            }

            return lines.Skip(1).ToArray();
        }
        catch
        {
            return [];
        }
    }

    private static ParsedCodexEvent ParseJsonEvent(string line)
    {
        if (string.IsNullOrWhiteSpace(line) || !line.TrimStart().StartsWith("{", StringComparison.Ordinal))
        {
            return new ParsedCodexEvent("", "", CleanTrace(line), null, false);
        }

        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            var type = ReadString(root, "type");
            var text = ExtractText(root);
            var trace = BuildTraceLine(type, root, text);
            var usage = ExtractUsage(root);
            var isTerminalTurn = IsTerminalCodexEvent(type, root);
            var hasReasoningType = ContainsTypeValue(root, "reason");
            var hasMessageType = ContainsTypeValue(root, "message");
            var isReasoning = type.Contains("reason", StringComparison.OrdinalIgnoreCase)
                || hasReasoningType;
            var isMessage = type.Contains("message", StringComparison.OrdinalIgnoreCase)
                || type.Contains("final", StringComparison.OrdinalIgnoreCase)
                || hasMessageType
                || (type.Contains("completed", StringComparison.OrdinalIgnoreCase) && !hasReasoningType);
            var thinking = isReasoning ? text : "";
            if (isReasoning && string.IsNullOrWhiteSpace(thinking))
            {
                thinking = "Codex reported a private reasoning step.";
            }

            return new ParsedCodexEvent(
                isMessage && !isReasoning ? text : "",
                thinking,
                trace,
                usage,
                isTerminalTurn);
        }
        catch
        {
            return new ParsedCodexEvent("", "", CleanTrace(line), null, false);
        }
    }

    private static void ReportUsageToTerminal(
        CodexUsageSnapshot usage,
        IProgress<string>? terminal,
        ref string lastReportedUsage,
        ref string lastReportedRateLimit)
    {
        if (terminal is null)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(usage.UsageSummary)
            && !string.Equals(usage.UsageSummary, lastReportedUsage, StringComparison.Ordinal))
        {
            lastReportedUsage = usage.UsageSummary;
            terminal.Report(usage.UsageSummary);
        }

        if (!string.IsNullOrWhiteSpace(usage.RateLimitSummary)
            && !string.Equals(usage.RateLimitSummary, lastReportedRateLimit, StringComparison.Ordinal))
        {
            lastReportedRateLimit = usage.RateLimitSummary;
            terminal.Report(usage.RateLimitSummary);
        }
    }

    private static bool IsTerminalCodexEvent(string type, JsonElement root)
    {
        if (IsTerminalCodexTypeName(type))
        {
            return true;
        }

        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("payload", out var payload)
            && IsTerminalCodexTypeName(ReadString(payload, "type")))
        {
            return true;
        }

        return false;
    }

    private static bool IsTerminalCodexTypeName(string type)
    {
        var clean = (type ?? "")
            .Trim()
            .Replace('_', '.')
            .Replace('-', '.');
        return clean.Equals("turn.completed", StringComparison.OrdinalIgnoreCase)
            || clean.Equals("response.completed", StringComparison.OrdinalIgnoreCase)
            || clean.Equals("task.complete", StringComparison.OrdinalIgnoreCase)
            || clean.Equals("task.completed", StringComparison.OrdinalIgnoreCase);
    }

    private static CodexUsageSnapshot MergeUsageSnapshots(
        CodexUsageSnapshot? current,
        CodexUsageSnapshot incoming,
        bool preferIncomingLastTokenUsage = true)
    {
        if (current is null)
        {
            return incoming;
        }

        return new CodexUsageSnapshot(
            preferIncomingLastTokenUsage
                ? incoming.LastTokenUsage ?? current.LastTokenUsage
                : current.LastTokenUsage ?? incoming.LastTokenUsage,
            incoming.TotalTokenUsage ?? current.TotalTokenUsage,
            incoming.RateLimits ?? current.RateLimits);
    }

    private static CodexUsageSnapshot? ExtractUsage(JsonElement root)
    {
        var directUsage = root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("usage", out var usageElement)
                ? ParseTokenUsage(usageElement)
                : null;
        var directRateLimits = ExtractRateLimits(root);
        var snapshot = directUsage is not null || directRateLimits is not null
            ? new CodexUsageSnapshot(directUsage, null, directRateLimits)
            : null;

        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("payload", out var payload)
            || payload.ValueKind != JsonValueKind.Object)
        {
            return snapshot;
        }

        CodexTokenUsage? lastUsage = null;
        CodexTokenUsage? totalUsage = null;
        if (payload.TryGetProperty("info", out var info) && info.ValueKind == JsonValueKind.Object)
        {
            if (info.TryGetProperty("last_token_usage", out var lastTokenUsage))
            {
                lastUsage = ParseTokenUsage(lastTokenUsage);
            }

            if (info.TryGetProperty("total_token_usage", out var totalTokenUsage))
            {
                totalUsage = ParseTokenUsage(totalTokenUsage);
            }
        }

        var payloadRateLimits = ExtractRateLimits(payload);
        if (lastUsage is null && totalUsage is null && payloadRateLimits is null)
        {
            return snapshot;
        }

        return MergeUsageSnapshots(snapshot, new CodexUsageSnapshot(lastUsage, totalUsage, payloadRateLimits));
    }

    private static CodexTokenUsage? ParseTokenUsage(JsonElement element)
    {
        var input = ReadLong(element, "input_tokens");
        var cached = ReadLong(element, "cached_input_tokens");
        var output = ReadLong(element, "output_tokens");
        var reasoning = ReadLong(element, "reasoning_output_tokens");
        var total = ReadLong(element, "total_tokens");
        return input.HasValue || cached.HasValue || output.HasValue || reasoning.HasValue || total.HasValue
            ? new CodexTokenUsage(input, cached, output, reasoning, total)
            : null;
    }

    private static CodexRateLimitSnapshot? ExtractRateLimits(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty("rate_limits", out var rateLimits)
            || rateLimits.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var primary = rateLimits.TryGetProperty("primary", out var primaryElement)
            ? ParseRateLimitWindow(primaryElement)
            : null;
        var secondary = rateLimits.TryGetProperty("secondary", out var secondaryElement)
            ? ParseRateLimitWindow(secondaryElement)
            : null;
        return primary is null && secondary is null
            ? null
            : new CodexRateLimitSnapshot(primary, secondary);
    }

    private static CodexRateLimitWindow? ParseRateLimitWindow(JsonElement element)
    {
        var usedPercent = ReadDouble(element, "used_percent");
        var windowMinutes = ReadInt(element, "window_minutes");
        var resetSeconds = ReadLong(element, "resets_at");
        DateTimeOffset? resetsAt = resetSeconds is { } seconds
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : null;
        return usedPercent.HasValue || windowMinutes.HasValue || resetsAt.HasValue
            ? new CodexRateLimitWindow(usedPercent, windowMinutes, resetsAt)
            : null;
    }

    private static string BuildTraceLine(string type, JsonElement root, string text)
    {
        var cleanType = string.IsNullOrWhiteSpace(type) ? "codex.event" : type;
        var status = ReadString(root, "status");
        var title = string.IsNullOrWhiteSpace(status) ? cleanType : $"{cleanType} {status}";
        if (string.IsNullOrWhiteSpace(text))
        {
            return title;
        }

        return $"{title}: {CleanTrace(text)}";
    }

    private static string ExtractText(JsonElement element)
    {
        var builder = new StringBuilder();
        ExtractText(element, builder, depth: 0);
        return builder.ToString().Trim();
    }

    private static bool ContainsTypeValue(JsonElement element, string pattern)
    {
        return ContainsTypeValue(element, pattern, depth: 0);
    }

    private static bool ContainsTypeValue(JsonElement element, string pattern, int depth)
    {
        if (depth > 8)
        {
            return false;
        }

        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (property.NameEquals("type")
                        && property.Value.ValueKind == JsonValueKind.String
                        && (property.Value.GetString() ?? "").Contains(pattern, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }

                    if (ContainsTypeValue(property.Value, pattern, depth + 1))
                    {
                        return true;
                    }
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    if (ContainsTypeValue(item, pattern, depth + 1))
                    {
                        return true;
                    }
                }

                break;
        }

        return false;
    }

    private static void ExtractText(JsonElement element, StringBuilder builder, int depth)
    {
        if (depth > 8)
        {
            return;
        }

        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (property.NameEquals("text")
                        || property.NameEquals("delta")
                        || property.NameEquals("message")
                        || property.NameEquals("summary"))
                    {
                        if (property.Value.ValueKind == JsonValueKind.String)
                        {
                            AppendExtractedText(builder, property.Value.GetString());
                            continue;
                        }
                    }

                    if (property.NameEquals("content")
                        || property.NameEquals("item")
                        || property.NameEquals("output")
                        || property.NameEquals("reasoning"))
                    {
                        ExtractText(property.Value, builder, depth + 1);
                    }
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    ExtractText(item, builder, depth + 1);
                }

                break;
        }
    }

    private static void AppendExtractedText(StringBuilder builder, string? text)
    {
        var clean = (text ?? "").Trim();
        if (string.IsNullOrWhiteSpace(clean))
        {
            return;
        }

        if (builder.Length > 0)
        {
            builder.AppendLine();
        }

        builder.Append(clean);
    }

    private static string ReadString(JsonElement element, string propertyName)
    {
        return element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? ""
                : "";
    }

    private static long? ReadLong(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt64(out var number) => number,
            JsonValueKind.String when long.TryParse(value.GetString(), out var number) => number,
            _ => null
        };
    }

    private static int? ReadInt(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out var number) => number,
            JsonValueKind.String when int.TryParse(value.GetString(), out var number) => number,
            _ => null
        };
    }

    private static double? ReadDouble(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetDouble(out var number) => number,
            JsonValueKind.String when double.TryParse(value.GetString(), out var number) => number,
            _ => null
        };
    }

    private static string CleanTrace(string? text)
    {
        var clean = (text ?? "")
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();
        return clean.Length <= 220 ? clean : clean[..220] + " ...";
    }

    private static string FormatPhase(ContextCapsulePhase phase)
    {
        return phase switch
        {
            ContextCapsulePhase.FileRequest => "DIR + Request",
            ContextCapsulePhase.SourceAudit => "CC",
            ContextCapsulePhase.PatchWrite => "CC",
            ContextCapsulePhase.PatchReview => "CC",
            _ => "chat"
        };
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Stale diagnostic output is non-fatal; the next run will still use stdout candidates.
        }
    }

    private static void TryKill(Process? process)
    {
        try
        {
            if (process is not null && !process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Cancellation cleanup is best-effort.
        }
    }

    private sealed record ParsedCodexEvent(
        string Message,
        string Thinking,
        string TraceLine,
        CodexUsageSnapshot? Usage,
        bool IsTerminalTurn);

    private sealed record CodexCommandResult(int ExitCode, string StandardOutput, string StandardError);
}
