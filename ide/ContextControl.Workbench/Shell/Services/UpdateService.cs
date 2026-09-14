// CC-DESC: Checks GitHub releases and downloads the Windows installer update.

using System.Diagnostics;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;

namespace ContextControl.Workbench.Services;

public sealed record AppUpdateInfo(
    bool IsAvailable,
    string CurrentVersion,
    string LatestVersion,
    string ReleaseUrl,
    string InstallerUrl,
    long? InstallerSizeBytes,
    string TargetCommit,
    DateTimeOffset? PublishedAt);

public sealed record AppUpdateDownloadResult(bool Succeeded, string Status, string? InstallerPath = null);

public sealed class UpdateService
{
    private const string LatestReleaseApi = "https://api.github.com/repos/VulkanVX/contextcontrol/releases/latest";
    private const string InstallerAssetName = "ContextControl-win-x64-Setup.exe";
    private static readonly TimeSpan HttpTimeout = TimeSpan.FromSeconds(20);

    public string CurrentVersion => NormalizeVersionLabel(
        Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
        ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
        ?? "0.0.0");

    public async Task<AppUpdateInfo> CheckLatestAsync(CancellationToken cancellationToken = default)
    {
        using var http = CreateHttpClient();
        using var response = await http.GetAsync(LatestReleaseApi, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;

        var latestVersion = NormalizeVersionLabel(GetString(root, "tag_name") ?? "0.0.0");
        var releaseUrl = GetString(root, "html_url") ?? "https://github.com/VulkanVX/contextcontrol/releases/latest";
        var targetCommit = GetString(root, "target_commitish") ?? "";
        var publishedAt = TryReadDate(root, "published_at");
        var installer = ResolveInstallerAsset(root);
        var currentVersion = CurrentVersion;

        return new AppUpdateInfo(
            IsVersionNewer(latestVersion, currentVersion),
            currentVersion,
            latestVersion,
            releaseUrl,
            installer.Url,
            installer.SizeBytes,
            targetCommit,
            publishedAt);
    }

    public async Task<AppUpdateDownloadResult> DownloadInstallerAsync(
        AppUpdateInfo update,
        IProgress<LocalLlmTransferProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(update.InstallerUrl))
        {
            return new AppUpdateDownloadResult(false, $"Release {update.LatestVersion} has no {InstallerAssetName} asset.");
        }

        if (!Uri.TryCreate(update.InstallerUrl, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return new AppUpdateDownloadResult(false, "Update download URL was not HTTPS.");
        }

        var updateDirectory = Path.Combine(Path.GetTempPath(), "ContextControl", "updates", update.LatestVersion);
        Directory.CreateDirectory(updateDirectory);
        CleanupUpdateCache(updateDirectory);
        var installerPath = Path.Combine(updateDirectory, InstallerAssetName);

        if (TryUseCachedInstaller(installerPath, update.InstallerSizeBytes, progress, update.LatestVersion, out var cached))
        {
            return cached;
        }

        if (File.Exists(installerPath))
        {
            try
            {
                File.Delete(installerPath);
            }
            catch (IOException)
            {
                return new AppUpdateDownloadResult(
                    false,
                    "A previous ContextControl setup window is still using the cached installer. Close that setup window, then press Check updates again.");
            }
            catch (UnauthorizedAccessException)
            {
                return new AppUpdateDownloadResult(
                    false,
                    "ContextControl could not replace the cached installer. Close any open setup window, then press Check updates again.");
            }
        }

        var partialPath = Path.Combine(updateDirectory, $"{InstallerAssetName}.{Environment.ProcessId}.{Guid.NewGuid():N}.download");
        using var http = CreateHttpClient(timeout: TimeSpan.FromMinutes(20));
        using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return new AppUpdateDownloadResult(false, $"Update download failed: HTTP {(int)response.StatusCode}.");
        }

        var totalBytes = response.Content.Headers.ContentLength;
        long readBytes = 0;
        try
        {
            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using var target = new FileStream(partialPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 1024 * 128, useAsync: true);
            var buffer = new byte[1024 * 128];
            var stopwatch = Stopwatch.StartNew();
            progress?.Report(new LocalLlmTransferProgress(
                "Downloading update",
                $"Downloading ContextControl {update.LatestVersion}...",
                0,
                totalBytes,
                null,
                totalBytes is > 0 ? 0 : null));
            while (true)
            {
                var read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                readBytes += read;
                var elapsedSeconds = Math.Max(0.001d, stopwatch.Elapsed.TotalSeconds);
                var speed = readBytes / elapsedSeconds;
                var percent = totalBytes is > 0
                    ? Math.Clamp(readBytes / (double)totalBytes.Value * 100d, 0d, 100d)
                    : (double?)null;
                var status = totalBytes is > 0
                    ? $"Downloading update {FormatBytes(readBytes)} / {FormatBytes(totalBytes.Value)}"
                    : $"Downloading update {FormatBytes(readBytes)}";
                progress?.Report(new LocalLlmTransferProgress(
                    "Downloading update",
                    status,
                    readBytes,
                    totalBytes,
                    speed,
                    percent));
            }
        }
        catch
        {
            DeleteFileBestEffort(partialPath);
            throw;
        }

        try
        {
            File.Move(partialPath, installerPath);
        }
        catch (IOException)
        {
            if (IsCachedInstallerUsable(installerPath, update.InstallerSizeBytes))
            {
                DeleteFileBestEffort(partialPath);
                return BuildCachedInstallerResult(installerPath, progress, update.LatestVersion, update.InstallerSizeBytes);
            }

            DeleteFileBestEffort(partialPath);
            return new AppUpdateDownloadResult(
                false,
                "The update downloaded, but ContextControl could not store it because another setup process is using the cached installer. Close the setup window, then press Check updates again.");
        }

        progress?.Report(new LocalLlmTransferProgress(
            "Downloading update",
            $"Downloaded ContextControl {update.LatestVersion}.",
            readBytes,
            totalBytes ?? readBytes,
            null,
            100));
        return new AppUpdateDownloadResult(true, $"Downloaded ContextControl {update.LatestVersion}.", installerPath);
    }

    public AppUpdateDownloadResult LaunchInstaller(string installerPath, string installDirectory, int waitForProcessId = 0)
    {
        if (string.IsNullOrWhiteSpace(installerPath) || !File.Exists(installerPath))
        {
            return new AppUpdateDownloadResult(false, "Downloaded installer was not found.");
        }

        if (IsProcessRunningFromPath(installerPath))
        {
            return new AppUpdateDownloadResult(true, "Update installer is already open. Closing ContextControl so setup can continue.", installerPath);
        }

        var installerArguments = BuildInstallerArguments(installDirectory, waitForProcessId);
        if (waitForProcessId > 0)
        {
            return LaunchInstallerAfterProcessExit(installerPath, installerArguments, waitForProcessId);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = installerPath,
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(installerPath) ?? Path.GetTempPath()
        };

        foreach (var argument in installerArguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        Process.Start(startInfo);
        return new AppUpdateDownloadResult(true, "Update installer started.", installerPath);
    }

    private static IReadOnlyList<string> BuildInstallerArguments(string installDirectory, int waitForProcessId)
    {
        var arguments = new List<string>();
        if (!string.IsNullOrWhiteSpace(installDirectory) && Directory.Exists(installDirectory))
        {
            arguments.Add($"/installDir={installDirectory}");
        }

        if (waitForProcessId > 0)
        {
            arguments.Add($"/waitForProcess={waitForProcessId}");
        }

        return arguments;
    }

    private static AppUpdateDownloadResult LaunchInstallerAfterProcessExit(
        string installerPath,
        IReadOnlyList<string> installerArguments,
        int waitForProcessId)
    {
        var powershell = ResolveWindowsPowerShell();
        if (powershell is null)
        {
            return new AppUpdateDownloadResult(false, "Windows PowerShell was not found, so ContextControl could not hand off the update safely.");
        }

        var updateDirectory = Path.GetDirectoryName(installerPath) ?? Path.GetTempPath();
        var handoffPath = Path.Combine(updateDirectory, $"ContextControlUpdateHandoff-{Environment.ProcessId}-{Guid.NewGuid():N}.ps1");
        var script = BuildUpdateHandoffScript(installerPath, installerArguments, waitForProcessId);
        File.WriteAllText(handoffPath, script);

        var startInfo = new ProcessStartInfo
        {
            FileName = powershell,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = updateDirectory
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-WindowStyle");
        startInfo.ArgumentList.Add("Hidden");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(handoffPath);

        Process.Start(startInfo);
        return new AppUpdateDownloadResult(true, "Update handoff started. ContextControl will close before setup replaces files.", installerPath);
    }

    private static bool IsProcessRunningFromPath(string executablePath)
    {
        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(executablePath);
        }
        catch
        {
            return false;
        }

        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    var path = process.MainModule?.FileName;
                    if (!string.IsNullOrWhiteSpace(path)
                        && string.Equals(Path.GetFullPath(path), fullPath, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
                catch
                {
                    // Access can fail for elevated or system processes.
                }
            }
        }

        return false;
    }

    private static HttpClient CreateHttpClient(TimeSpan? timeout = null)
    {
        var http = new HttpClient
        {
            Timeout = timeout ?? HttpTimeout
        };
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("ContextControl", "1.0"));
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return http;
    }

    private static InstallerAsset ResolveInstallerAsset(JsonElement releaseRoot)
    {
        if (!releaseRoot.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
        {
            return new InstallerAsset("", null);
        }

        foreach (var asset in assets.EnumerateArray())
        {
            var name = GetString(asset, "name");
            if (!string.Equals(name, InstallerAssetName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var sizeBytes = asset.TryGetProperty("size", out var size) && size.TryGetInt64(out var value)
                ? value
                : (long?)null;
            return new InstallerAsset(GetString(asset, "browser_download_url") ?? "", sizeBytes);
        }

        return new InstallerAsset("", null);
    }

    private static bool TryUseCachedInstaller(
        string installerPath,
        long? expectedSizeBytes,
        IProgress<LocalLlmTransferProgress>? progress,
        string latestVersion,
        out AppUpdateDownloadResult result)
    {
        if (!IsCachedInstallerUsable(installerPath, expectedSizeBytes))
        {
            result = new AppUpdateDownloadResult(false, "");
            return false;
        }

        result = BuildCachedInstallerResult(installerPath, progress, latestVersion, expectedSizeBytes);
        return true;
    }

    private static AppUpdateDownloadResult BuildCachedInstallerResult(
        string installerPath,
        IProgress<LocalLlmTransferProgress>? progress,
        string latestVersion,
        long? expectedSizeBytes)
    {
        var size = expectedSizeBytes ?? new FileInfo(installerPath).Length;
        progress?.Report(new LocalLlmTransferProgress(
            "Downloading update",
            $"Using already downloaded ContextControl {latestVersion} installer.",
            size,
            size,
            null,
            100));
        return new AppUpdateDownloadResult(true, $"Using already downloaded ContextControl {latestVersion} installer.", installerPath);
    }

    private static bool IsCachedInstallerUsable(string installerPath, long? expectedSizeBytes)
    {
        try
        {
            if (!File.Exists(installerPath))
            {
                return false;
            }

            var length = new FileInfo(installerPath).Length;
            if (length <= 0)
            {
                return false;
            }

            return expectedSizeBytes is null or <= 0 || length == expectedSizeBytes.Value;
        }
        catch
        {
            return false;
        }
    }

    private static void CleanupUpdateCache(string activeUpdateDirectory)
    {
        var updatesRoot = Path.GetDirectoryName(activeUpdateDirectory);
        if (string.IsNullOrWhiteSpace(updatesRoot) || !Directory.Exists(updatesRoot))
        {
            return;
        }

        foreach (var directory in Directory.EnumerateDirectories(updatesRoot))
        {
            try
            {
                if (!string.Equals(Path.GetFullPath(directory), Path.GetFullPath(activeUpdateDirectory), StringComparison.OrdinalIgnoreCase))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
            catch
            {
                // A setup process may still be using an older cached installer.
            }
        }

        foreach (var file in Directory.EnumerateFiles(activeUpdateDirectory, "*.download"))
        {
            DeleteFileBestEffort(file);
        }

        foreach (var file in Directory.EnumerateFiles(activeUpdateDirectory, "ContextControlUpdateHandoff*.ps1"))
        {
            DeleteFileBestEffort(file);
        }
    }

    private static void DeleteFileBestEffort(string path)
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
            // Temp cleanup must not block update installation.
        }
    }

    private static string? ResolveWindowsPowerShell()
    {
        var systemPowerShell = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "System32",
            "WindowsPowerShell",
            "v1.0",
            "powershell.exe");
        return File.Exists(systemPowerShell) ? systemPowerShell : "powershell.exe";
    }

    private static string BuildUpdateHandoffScript(
        string installerPath,
        IReadOnlyList<string> installerArguments,
        int waitForProcessId)
    {
        var arguments = string.Join(", ", installerArguments.Select(argument => $"'{EscapePowerShell(argument)}'"));
        return $$"""
$ErrorActionPreference = 'SilentlyContinue'
$processIdToWait = {{waitForProcessId}}
$installerPath = '{{EscapePowerShell(installerPath)}}'
$installerArguments = @({{arguments}})
try {
    Wait-Process -Id $processIdToWait -Timeout 90 -ErrorAction SilentlyContinue
} catch {
}
Start-Sleep -Milliseconds 500
Start-Process -FilePath $installerPath -ArgumentList $installerArguments -WorkingDirectory (Split-Path -Parent $installerPath)
Start-Sleep -Seconds 2
Remove-Item -LiteralPath $PSCommandPath -Force -ErrorAction SilentlyContinue
""";
    }

    private static string EscapePowerShell(string value)
    {
        return (value ?? "").Replace("'", "''", StringComparison.Ordinal);
    }

    private static bool IsVersionNewer(string latest, string current)
    {
        if (TryParseVersion(latest, out var latestVersion) && TryParseVersion(current, out var currentVersion))
        {
            return latestVersion.CompareTo(currentVersion) > 0;
        }

        return !string.Equals(latest, current, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseVersion(string value, out Version version)
    {
        version = new Version(0, 0, 0);
        var clean = NormalizeVersionLabel(value);
        var plusIndex = clean.IndexOf('+', StringComparison.Ordinal);
        if (plusIndex >= 0)
        {
            clean = clean[..plusIndex];
        }

        var dashIndex = clean.IndexOf('-', StringComparison.Ordinal);
        if (dashIndex >= 0)
        {
            clean = clean[..dashIndex];
        }

        return Version.TryParse(clean, out version!);
    }

    private static string NormalizeVersionLabel(string value)
    {
        var clean = (value ?? "").Trim();
        return clean.StartsWith("v", StringComparison.OrdinalIgnoreCase)
            ? clean[1..]
            : clean;
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static DateTimeOffset? TryReadDate(JsonElement element, string propertyName)
    {
        var value = GetString(element, propertyName);
        return DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;
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

    private sealed record InstallerAsset(string Url, long? SizeBytes);
}
