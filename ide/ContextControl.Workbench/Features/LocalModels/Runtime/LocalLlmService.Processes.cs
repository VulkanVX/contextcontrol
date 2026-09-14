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
    private static readonly Encoding ProcessUtf8Encoding = new UTF8Encoding(false, false);

    private static HttpClient CreateHttpClient(TimeSpan timeout)
    {
        return new HttpClient
        {
            BaseAddress = OllamaBaseUri,
            Timeout = timeout
        };
    }

    private static async Task<ProcessResult> RunProcessAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? environmentVariables = null)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = ProcessUtf8Encoding,
            StandardErrorEncoding = ProcessUtf8Encoding,
            CreateNoWindow = true
        };
        ApplyReadableProcessEnvironment(process.StartInfo);

        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        if (environmentVariables is not null)
        {
            foreach (var (key, value) in environmentVariables)
            {
                process.StartInfo.Environment[key] = value;
            }
        }

        try
        {
            if (!process.Start())
            {
                return ProcessResult.NotStarted();
            }
        }
        catch
        {
            return ProcessResult.NotStarted();
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            return new ProcessResult(
                true,
                -1,
                await SafeReadAsync(stdoutTask).ConfigureAwait(false),
                await SafeReadAsync(stderrTask).ConfigureAwait(false),
                TimedOut: !cancellationToken.IsCancellationRequested);
        }

        return new ProcessResult(
            true,
            process.ExitCode,
            await SafeReadAsync(stdoutTask).ConfigureAwait(false),
            await SafeReadAsync(stderrTask).ConfigureAwait(false));
    }

    private static async Task<ProcessResult> RunProcessStreamingAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        Action<string> onChunk,
        CancellationToken cancellationToken,
        string? workingDirectory = null,
        IReadOnlyDictionary<string, string>? environmentVariables = null)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = ProcessUtf8Encoding,
            StandardErrorEncoding = ProcessUtf8Encoding,
            CreateNoWindow = true
        };
        ApplyReadableProcessEnvironment(process.StartInfo);
        if (!string.IsNullOrWhiteSpace(workingDirectory))
        {
            process.StartInfo.WorkingDirectory = workingDirectory;
        }

        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        if (environmentVariables is not null)
        {
            foreach (var (key, value) in environmentVariables)
            {
                process.StartInfo.Environment[key] = value;
            }
        }

        try
        {
            if (!process.Start())
            {
                return ProcessResult.NotStarted();
            }
        }
        catch
        {
            return ProcessResult.NotStarted();
        }

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        var stdoutTask = ReadProcessStreamAsync(process.StandardOutput, stdout, onChunk, cancellationToken);
        var stderrTask = ReadProcessStreamAsync(process.StandardError, stderr, onChunk, cancellationToken);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            await Task.WhenAll(SafeWaitAsync(stdoutTask), SafeWaitAsync(stderrTask)).ConfigureAwait(false);
            return new ProcessResult(true, -1, stdout.ToString(), stderr.ToString(), TimedOut: !cancellationToken.IsCancellationRequested);
        }

        await Task.WhenAll(SafeWaitAsync(stdoutTask), SafeWaitAsync(stderrTask)).ConfigureAwait(false);
        return new ProcessResult(true, process.ExitCode, stdout.ToString(), stderr.ToString());
    }

    private static async Task ReadProcessStreamAsync(
        StreamReader reader,
        StringBuilder sink,
        Action<string> onChunk,
        CancellationToken cancellationToken)
    {
        var buffer = new char[512];
        while (true)
        {
            var read = await reader.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return;
            }

            var chunk = new string(buffer, 0, read);
            sink.Append(chunk);
            onChunk(chunk);
        }
    }

    private static async Task SafeWaitAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch
        {
            // Keep process cleanup best-effort.
        }
    }

    private static async Task<string> SafeReadAsync(Task<string> task)
    {
        try
        {
            return await task.ConfigureAwait(false);
        }
        catch
        {
            return "";
        }
    }

    private static void ApplyReadableProcessEnvironment(ProcessStartInfo startInfo)
    {
        startInfo.Environment["PYTHONUTF8"] = "1";
        startInfo.Environment["PYTHONIOENCODING"] = "utf-8";
        startInfo.Environment["DOTNET_SYSTEM_CONSOLE_ALLOW_ANSI_COLOR_REDIRECTION"] = "1";
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Detection should never crash the workbench.
        }
    }

    private static string? FirstLine(string? text)
    {
        return (text ?? "")
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();
    }

    private static string? FirstFailureLine(string? text)
    {
        var lines = (text ?? "")
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(CleanProgressText)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToArray();
        if (lines.Length == 0)
        {
            return null;
        }

        var explicitFailure = lines.FirstOrDefault(line => line.StartsWith("CC_ERROR ", StringComparison.Ordinal));
        if (!string.IsNullOrWhiteSpace(explicitFailure))
        {
            return explicitFailure["CC_ERROR ".Length..];
        }

        for (var i = lines.Length - 1; i >= 0; i--)
        {
            var line = lines[i];
            if (!LooksLikePythonExceptionLine(line))
            {
                continue;
            }

            if (!line.EndsWith(":", StringComparison.Ordinal))
            {
                return line;
            }

            var detail = lines
                .Skip(i + 1)
                .FirstOrDefault(candidate => !IsProcessNoiseLine(candidate));
            return string.IsNullOrWhiteSpace(detail) ? line : $"{line} {detail}";
        }

        return lines.FirstOrDefault(line => !IsProcessNoiseLine(line)) ?? lines[0];
    }

    private static bool LooksLikePythonExceptionLine(string line)
    {
        return Regex.IsMatch(line, @"^[A-Za-z_][A-Za-z0-9_.]*(Error|Exception):", RegexOptions.CultureInvariant)
            || line.StartsWith("ERROR:", StringComparison.OrdinalIgnoreCase)
            || line.StartsWith("Error:", StringComparison.Ordinal);
    }

    private static bool IsProcessNoiseLine(string line)
    {
        var value = line.Trim();
        return value.StartsWith("File \"", StringComparison.Ordinal)
            || value.StartsWith("Traceback ", StringComparison.Ordinal)
            || value.StartsWith("warnings.warn(", StringComparison.Ordinal)
            || value.Contains("UserWarning:", StringComparison.Ordinal)
            || value.Contains("FutureWarning:", StringComparison.Ordinal)
            || value.Contains("DeprecationWarning:", StringComparison.Ordinal)
            || value.StartsWith("[transformers] ", StringComparison.Ordinal);
    }

    private static string CleanProgressText(string text)
    {
        return AnsiRegex.Replace(text ?? "", "")
            .Replace('\r', '\n')
            .Replace('\b', ' ')
            .Trim();
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

    private static long? ParseByteCount(string value, string unit)
    {
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            return null;
        }

        var normalized = (unit ?? "").Trim().ToUpperInvariant();
        var multiplier = normalized switch
        {
            "KB" or "KIB" => 1024d,
            "MB" or "MIB" => 1024d * 1024d,
            "GB" or "GIB" => 1024d * 1024d * 1024d,
            "TB" or "TIB" => 1024d * 1024d * 1024d * 1024d,
            _ => 1d
        };

        return (long)Math.Max(0, number * multiplier);
    }

    private static double? ParseByteRate(string value, string unit)
    {
        var cleanUnit = (unit ?? "").Replace("/s", "", StringComparison.OrdinalIgnoreCase);
        return ParseByteCount(value, cleanUnit);
    }

}
