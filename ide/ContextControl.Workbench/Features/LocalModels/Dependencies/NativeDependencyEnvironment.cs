// CC-DESC: Safe native dependency download/install helpers for ContextControl-owned folders.

using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace ContextControl.Workbench.Services;

internal sealed record NativeAssetPreference(IReadOnlyList<string> RequiredFragments, IReadOnlyList<string> RejectedFragments);

internal sealed record NativeDependencySpec(
    string Id,
    string DisplayName,
    string Repository,
    IReadOnlyList<string> ExecutableNames,
    IReadOnlyList<NativeAssetPreference> WindowsAssets,
    IReadOnlyList<NativeAssetPreference> LinuxAssets,
    IReadOnlyList<NativeAssetPreference> MacAssets);

internal sealed record NativeDependencyInstallResult(bool Succeeded, string Status, string? ExecutablePath = null);

internal static class NativeDependencyEnvironment
{
    private static readonly NativeDependencySpec[] Specs =
    [
        new(
            "llama_cpp_server",
            "llama.cpp server",
            "ggml-org/llama.cpp",
            ["llama-server.exe", "llama-server", "server.exe", "server"],
            [
                Preference("bin-win-cpu-x64", ".zip"),
                Preference("bin-win-vulkan-x64", ".zip")
            ],
            [],
            []),
        new(
            "koboldcpp",
            "KoboldCpp",
            "LostRuins/koboldcpp",
            ["koboldcpp-nocuda.exe", "koboldcpp.exe", "koboldcpp-oldpc.exe", "koboldcpp-linux-x64-nocuda", "koboldcpp-linux-x64", "koboldcpp-mac-arm64"],
            [
                Preference("koboldcpp-nocuda.exe"),
                Preference("koboldcpp.exe"),
                Preference("koboldcpp-oldpc.exe")
            ],
            [
                Preference("koboldcpp-linux-x64-nocuda"),
                Preference("koboldcpp-linux-x64")
            ],
            [
                Preference("koboldcpp-mac-arm64")
            ]),
        new(
            "stable_diffusion_cpp",
            "stable-diffusion.cpp",
            "leejet/stable-diffusion.cpp",
            ["sd.exe", "sd", "stable-diffusion.exe", "stable-diffusion", "stable-diffusion-cli.exe", "stable-diffusion-cli"],
            [
                Preference("bin-win-vulkan-x64", ".zip"),
                Preference("bin-win-noavx-x64", ".zip"),
                Preference("bin-win-avx2-x64", ".zip")
            ],
            [],
            []),
        new(
            "rwkv_runner",
            "RWKV runner",
            "josStorer/RWKV-Runner",
            ["RWKV-Runner_windows_x64.exe", "RWKV-Runner_linux_x64", "RWKV-Runner", "rwkv-runner.exe", "rwkv-runner"],
            [
                Preference("RWKV-Runner_windows_x64.exe")
            ],
            [
                Preference("RWKV-Runner_linux_x64")
            ],
            [
                Preference("RWKV-Runner_macos_universal", ".zip")
            ])
    ];

    private static readonly Dictionary<string, NativeDependencySpec> SpecsById =
        Specs.ToDictionary(spec => spec.Id, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<NativeDependencySpec> AllSpecs => Specs;

    public static string ManagedRoot
    {
        get
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(localAppData))
            {
                localAppData = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".contextcontrol");
            }

            return Path.Combine(localAppData, "ContextControl", "dependencies", "native");
        }
    }

    public static bool HasManagedInstaller(string dependencyId)
    {
        return SpecsById.ContainsKey(dependencyId);
    }

    public static bool TryGetSpec(string dependencyId, out NativeDependencySpec spec)
    {
        if (SpecsById.TryGetValue(dependencyId, out var found))
        {
            spec = found;
            return true;
        }

        spec = null!;
        return false;
    }

    public static string ManagedDependencyDirectory(string dependencyId)
    {
        return Path.Combine(ManagedRoot, SanitizeDependencyId(dependencyId));
    }

    public static string RemoveManagedDependency(string dependencyId)
    {
        var directory = ManagedDependencyDirectory(dependencyId);
        DeleteManagedChildDirectory(ManagedRoot, directory);
        return directory;
    }

    public static string? FindManagedExecutable(string dependencyId, IReadOnlyList<string> executableNames)
    {
        var directory = ManagedDependencyDirectory(dependencyId);
        if (!Directory.Exists(directory))
        {
            return null;
        }

        foreach (var executableName in executableNames)
        {
            try
            {
                var match = Directory
                    .EnumerateFiles(directory, executableName, SearchOption.AllDirectories)
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(match))
                {
                    return match;
                }
            }
            catch
            {
                // Ignore broken managed dependency trees.
            }
        }

        return null;
    }

    public static async Task<NativeDependencyInstallResult> InstallLatestReleaseAsync(
        NativeDependencySpec spec,
        IProgress<string> terminal,
        IProgress<LocalLlmTransferProgress> progress,
        CancellationToken cancellationToken = default)
    {
        var operation = $"Installing {spec.DisplayName}";
        var preferences = AssetPreferencesForCurrentPlatform(spec);
        if (preferences.Count == 0)
        {
            return new NativeDependencyInstallResult(false, $"{spec.DisplayName} has no portable ContextControl-local asset for this OS yet.");
        }

        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("ContextControl", "1.0"));

        var releaseUri = $"https://api.github.com/repos/{spec.Repository}/releases/latest";
        terminal.Report($"> GET {releaseUri}");
        using var response = await http.GetAsync(releaseUri, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return new NativeDependencyInstallResult(false, $"Could not resolve latest {spec.DisplayName} release: HTTP {(int)response.StatusCode}.");
        }

        await using var releaseStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var releaseDocument = await JsonDocument.ParseAsync(releaseStream, cancellationToken: cancellationToken).ConfigureAwait(false);
        var root = releaseDocument.RootElement;
        var tag = root.TryGetProperty("tag_name", out var tagElement)
            ? tagElement.GetString() ?? "latest"
            : "latest";
        var asset = SelectAsset(root, preferences);
        if (asset is null)
        {
            return new NativeDependencyInstallResult(false, $"{spec.DisplayName} latest release has no matching portable asset for this OS.");
        }

        var installDirectory = Path.Combine(ManagedDependencyDirectory(spec.Id), SanitizeDependencyId(tag));
        Directory.CreateDirectory(installDirectory);

        var assetName = asset.Name;
        var downloadUrl = asset.DownloadUrl;
        var downloadPath = Path.Combine(installDirectory, SanitizeFileName(assetName));
        terminal.Report($"> download {downloadUrl}");
        progress.Report(new LocalLlmTransferProgress(operation, $"Downloading {assetName}", null, null, null, null));
        var downloadResult = await DownloadFileAsync(http, downloadUrl, downloadPath, operation, progress, cancellationToken).ConfigureAwait(false);
        if (!downloadResult.Succeeded)
        {
            return downloadResult;
        }

        var installResult = await MaterializeAssetAsync(spec, downloadPath, installDirectory, operation, progress, cancellationToken).ConfigureAwait(false);
        if (!installResult.Succeeded)
        {
            return installResult;
        }

        return installResult.ExecutablePath is not null
            ? installResult
            : new NativeDependencyInstallResult(true, $"{spec.DisplayName} downloaded into {installDirectory}.", FindManagedExecutable(spec.Id, spec.ExecutableNames));
    }

    private static IReadOnlyList<NativeAssetPreference> AssetPreferencesForCurrentPlatform(NativeDependencySpec spec)
    {
        if (OperatingSystem.IsWindows())
        {
            return spec.WindowsAssets;
        }

        if (OperatingSystem.IsLinux())
        {
            return spec.LinuxAssets;
        }

        if (OperatingSystem.IsMacOS())
        {
            return spec.MacAssets;
        }

        return [];
    }

    private static NativeAsset? SelectAsset(JsonElement releaseRoot, IReadOnlyList<NativeAssetPreference> preferences)
    {
        if (!releaseRoot.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var candidates = new List<NativeAsset>();
        foreach (var asset in assets.EnumerateArray())
        {
            if (!asset.TryGetProperty("name", out var nameElement)
                || !asset.TryGetProperty("browser_download_url", out var urlElement))
            {
                continue;
            }

            var name = nameElement.GetString();
            var url = urlElement.GetString();
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(url))
            {
                continue;
            }

            candidates.Add(new NativeAsset(name, url));
        }

        foreach (var preference in preferences)
        {
            var match = candidates.FirstOrDefault(asset => MatchesPreference(asset.Name, preference));
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }

    private static bool MatchesPreference(string assetName, NativeAssetPreference preference)
    {
        return preference.RequiredFragments.All(fragment => assetName.Contains(fragment, StringComparison.OrdinalIgnoreCase))
            && !preference.RejectedFragments.Any(fragment => assetName.Contains(fragment, StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<NativeDependencyInstallResult> DownloadFileAsync(
        HttpClient http,
        string url,
        string path,
        string operation,
        IProgress<LocalLlmTransferProgress> progress,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return new NativeDependencyInstallResult(false, "Download was blocked because the release asset URL was not HTTPS.");
        }

        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return new NativeDependencyInstallResult(false, $"Download failed: HTTP {(int)response.StatusCode}.");
        }

        var totalBytes = response.Content.Headers.ContentLength;
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var target = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read, 1024 * 128, useAsync: true);
        var buffer = new byte[1024 * 128];
        long readBytes = 0;
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            readBytes += read;
            progress.Report(new LocalLlmTransferProgress(
                operation,
                $"Downloaded {FormatBytes(readBytes)} / {(totalBytes is > 0 ? FormatBytes(totalBytes.Value) : "?")}",
                readBytes,
                totalBytes,
                null,
                totalBytes is > 0 ? Math.Clamp(readBytes * 100d / totalBytes.Value, 0, 100) : null));
        }

        return new NativeDependencyInstallResult(true, $"Downloaded {path}.");
    }

    private static async Task<NativeDependencyInstallResult> MaterializeAssetAsync(
        NativeDependencySpec spec,
        string downloadPath,
        string installDirectory,
        string operation,
        IProgress<LocalLlmTransferProgress> progress,
        CancellationToken cancellationToken)
    {
        var extension = Path.GetExtension(downloadPath);
        if (extension.Equals(".zip", StringComparison.OrdinalIgnoreCase))
        {
            var extractDirectory = Path.Combine(installDirectory, "extracted");
            Directory.CreateDirectory(extractDirectory);
            progress.Report(new LocalLlmTransferProgress(operation, $"Extracting {Path.GetFileName(downloadPath)}", null, null, null, null));
            await Task.Run(() => ExtractZipSafely(downloadPath, extractDirectory), cancellationToken).ConfigureAwait(false);
        }

        var executable = FindManagedExecutable(spec.Id, spec.ExecutableNames);
        if (executable is null)
        {
            executable = spec.ExecutableNames.Any(name => string.Equals(Path.GetFileName(downloadPath), name, StringComparison.OrdinalIgnoreCase))
                ? downloadPath
                : null;
        }

        if (executable is null)
        {
            return new NativeDependencyInstallResult(false, $"{spec.DisplayName} downloaded, but no expected executable was found in {installDirectory}.");
        }

        TryMarkExecutable(executable);
        progress.Report(new LocalLlmTransferProgress(operation, $"{spec.DisplayName} ready at {executable}", null, null, null, 100));
        return new NativeDependencyInstallResult(true, $"{spec.DisplayName} installed locally at {executable}.", executable);
    }

    private static void ExtractZipSafely(string zipPath, string destinationDirectory)
    {
        var destinationRoot = Path.GetFullPath(destinationDirectory);
        if (!destinationRoot.EndsWith(Path.DirectorySeparatorChar))
        {
            destinationRoot += Path.DirectorySeparatorChar;
        }

        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        using var archive = ZipFile.OpenRead(zipPath);
        foreach (var entry in archive.Entries)
        {
            var targetPath = Path.GetFullPath(Path.Combine(destinationRoot, entry.FullName));
            if (!targetPath.StartsWith(destinationRoot, comparison))
            {
                throw new InvalidOperationException($"Blocked unsafe archive path: {entry.FullName}");
            }

            if (string.IsNullOrWhiteSpace(entry.Name))
            {
                Directory.CreateDirectory(targetPath);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(targetPath) ?? destinationRoot);
            entry.ExtractToFile(targetPath, overwrite: true);
        }
    }

    private static void TryMarkExecutable(string executable)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            File.SetUnixFileMode(
                executable,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                    | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
                    | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }
        catch
        {
            // Best effort on filesystems that do not support Unix modes.
        }
    }

    private static NativeAssetPreference Preference(params string[] requiredFragments)
    {
        return new NativeAssetPreference(requiredFragments, ["installer"]);
    }

    private static string SanitizeDependencyId(string value)
    {
        var builder = new StringBuilder();
        foreach (var ch in value)
        {
            builder.Append(char.IsLetterOrDigit(ch) || ch is '_' or '-' or '.' ? ch : '_');
        }

        return builder.Length == 0 ? "dependency" : builder.ToString();
    }

    private static void DeleteManagedChildDirectory(string root, string directory)
    {
        var rootFullPath = EnsureTrailingSeparator(Path.GetFullPath(root));
        var directoryFullPath = Path.GetFullPath(directory);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!directoryFullPath.StartsWith(rootFullPath, comparison))
        {
            throw new InvalidOperationException($"Refusing to remove dependency outside managed root: {directoryFullPath}");
        }

        if (Directory.Exists(directoryFullPath))
        {
            Directory.Delete(directoryFullPath, recursive: true);
        }
    }

    private static string EnsureTrailingSeparator(string path)
    {
        return path.EndsWith(Path.DirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var builder = new StringBuilder();
        foreach (var ch in value)
        {
            builder.Append(invalid.Contains(ch) ? '_' : ch);
        }

        return builder.Length == 0 ? "download.bin" : builder.ToString();
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

    private sealed record NativeAsset(string Name, string DownloadUrl);
}
