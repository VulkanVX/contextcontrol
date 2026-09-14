// CC-DESC: Local LLM service slice extracted from LocalLlmService.cs.

using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace ContextControl.Workbench.Services;

public sealed partial class LocalLlmService
{
    public Task<LocalLlmChatResult> PullModelAsync(string modelId, CancellationToken cancellationToken = default)
    {
        return PullModelAsync(modelId, null, null, cancellationToken);
    }

    public async Task<LocalLlmChatResult> PullModelAsync(
        string modelId,
        IProgress<LocalLlmTransferProgress>? progress,
        IProgress<string>? terminal,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(modelId))
        {
            return new LocalLlmChatResult(false, "No model selected.");
        }

        var ollamaPath = ResolveOllamaExecutable();
        if (ollamaPath is null)
        {
            return new LocalLlmChatResult(false, "Ollama command was not found. Install Ollama first.");
        }

        var parser = new OllamaPullProgressParser($"Downloading {modelId}", progress);
        terminal?.Report($"> {ollamaPath} pull {modelId}");
        var result = await RunProcessStreamingAsync(
            ollamaPath,
            ["pull", modelId],
            TimeSpan.FromMinutes(30),
            chunk =>
            {
                parser.Append(chunk);
                var clean = CleanProgressText(chunk);
                if (!string.IsNullOrWhiteSpace(clean))
                {
                    terminal?.Report(clean);
                }
            },
            cancellationToken).ConfigureAwait(false);

        if (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }

        return result.ExitCode == 0
            ? new LocalLlmChatResult(true, $"Downloaded {modelId}.")
            : new LocalLlmChatResult(false, FirstLine(result.StandardError) ?? FirstLine(result.StandardOutput) ?? $"ollama pull exited {result.ExitCode}.");
    }

    public async Task<LocalLlmChatResult> UninstallModelAsync(
        string modelId,
        IProgress<string>? terminal = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(modelId))
        {
            return new LocalLlmChatResult(false, "No model selected.");
        }

        var ollamaPath = ResolveOllamaExecutable();
        if (ollamaPath is null)
        {
            return new LocalLlmChatResult(false, "Ollama command was not found. Install Ollama first.");
        }

        terminal?.Report($"> {ollamaPath} rm {modelId}");
        var result = await RunProcessStreamingAsync(
            ollamaPath,
            ["rm", modelId],
            TimeSpan.FromMinutes(5),
            chunk =>
            {
                var clean = CleanProgressText(chunk);
                if (!string.IsNullOrWhiteSpace(clean))
                {
                    terminal?.Report(clean);
                }
            },
            cancellationToken).ConfigureAwait(false);

        if (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }

        if (result.ExitCode == 0)
        {
            return new LocalLlmChatResult(true, $"Uninstalled {modelId}.");
        }

        var failureText = $"{result.StandardError}\n{result.StandardOutput}";
        if (LooksLikeModelAlreadyRemoved(failureText))
        {
            return new LocalLlmChatResult(true, $"{modelId} was already removed.");
        }

        return new LocalLlmChatResult(false, FirstLine(result.StandardError) ?? FirstLine(result.StandardOutput) ?? $"ollama rm exited {result.ExitCode}.");
    }

    public Task<LocalLlmChatResult> InstallOllamaAsync(CancellationToken cancellationToken = default)
    {
        return InstallOllamaAsync(null, null, cancellationToken);
    }

    private static bool LooksLikeModelAlreadyRemoved(string text)
    {
        var clean = text.ToLowerInvariant();
        return clean.Contains("not found", StringComparison.Ordinal)
            || clean.Contains("no such model", StringComparison.Ordinal)
            || clean.Contains("does not exist", StringComparison.Ordinal)
            || clean.Contains("model not installed", StringComparison.Ordinal);
    }

    public async Task<LocalLlmChatResult> InstallOllamaAsync(
        IProgress<LocalLlmTransferProgress>? progress,
        IProgress<string>? terminal,
        CancellationToken cancellationToken = default)
    {
        var existingExecutable = ResolveOllamaExecutable();
        if (!string.IsNullOrWhiteSpace(existingExecutable))
        {
            progress?.Report(new LocalLlmTransferProgress(
                "Install Ollama",
                $"Ollama already exists at {existingExecutable}.",
                null,
                null,
                null,
                100));
            terminal?.Report($"Ollama already exists at {existingExecutable}.");
            return new LocalLlmChatResult(true, $"Ollama already exists at {existingExecutable}.");
        }

        if (!PackageManagerDependencyEnvironment.TryGetSpec("ollama", out var spec)
            || !PackageManagerDependencyEnvironment.TryResolveInstallCommand(spec, out var command))
        {
            progress?.Report(new LocalLlmTransferProgress(
                "Install Ollama",
                "Automatic Ollama installation is not configured for this OS.",
                null,
                null,
                null,
                null));
            terminal?.Report("Automatic Ollama installation is not configured for this OS.");
            await Task.CompletedTask.ConfigureAwait(false);
            return new LocalLlmChatResult(false, "Automatic Ollama installation is not configured for this OS.");
        }

        progress?.Report(new LocalLlmTransferProgress(
            "Install Ollama",
            $"Running {command.FileName} package install.",
            null,
            null,
            null,
            null));
        terminal?.Report($"> {command.FileName} {string.Join(' ', command.Arguments)}");
        var result = await RunProcessStreamingAsync(
            command.FileName,
            command.Arguments,
            command.Timeout,
            chunk =>
            {
                var clean = CleanProgressText(chunk);
                if (!string.IsNullOrWhiteSpace(clean))
                {
                    terminal?.Report(clean);
                    progress?.Report(new LocalLlmTransferProgress("Install Ollama", clean, null, null, null, null));
                }
            },
            cancellationToken).ConfigureAwait(false);

        return result.ExitCode == 0
            ? new LocalLlmChatResult(true, "Ollama package installer completed. Refresh if it is not visible until the next shell/session.")
            : new LocalLlmChatResult(false, FirstLine(result.StandardError) ?? FirstLine(result.StandardOutput) ?? $"Ollama package install exited {result.ExitCode}.");
    }

    public LocalLlmChatResult OpenOllamaDownloadPage()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = OllamaDownloadPageUrl,
                UseShellExecute = true
            });
            return new LocalLlmChatResult(true, "Opened the official Ollama download page.");
        }
        catch (Exception ex)
        {
            return new LocalLlmChatResult(false, $"Could not open Ollama download page: {ex.Message}");
        }
    }

    private static async Task<LocalLlmChatResult> WaitForOllamaInstallAsync(
        IProgress<string>? terminal,
        CancellationToken cancellationToken)
    {
        terminal?.Report("Checking for installed Ollama executable...");
        for (var attempt = 1; attempt <= 30; attempt++)
        {
            var executablePath = ResolveOllamaExecutable();
            if (executablePath is not null)
            {
                terminal?.Report($"Ollama installed: {executablePath}");
                return new LocalLlmChatResult(true, "Ollama installed. Refreshing local model state.");
            }

            terminal?.Report($"Ollama not visible yet; checking again ({attempt}/30)...");
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
        }

        return new LocalLlmChatResult(false, "Ollama installer closed, but the Ollama executable was not found yet. Try Refresh or reopen the app.");
    }

    private static string? ResolveOllamaExecutable()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Ollama", "ollama.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Ollama", "ollama.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Ollama", "ollama.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Ollama", "ollama.exe")
        };

        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
            {
                return candidate;
            }
        }

        return FindExecutableOnPath("ollama.exe") ?? FindExecutableOnPath("ollama");
    }

    private static string? FindExecutableOnPath(string fileName)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                var candidate = Path.Combine(directory, fileName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch
            {
                // Ignore broken PATH entries.
            }
        }

        return null;
    }

}
