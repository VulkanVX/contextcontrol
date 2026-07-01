// CC-DESC: Source-archive installers for backend projects without portable release binaries.

using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text;

namespace ContextControl.Workbench.Services;

internal sealed record SourceDependencySpec(
    string Id,
    string DisplayName,
    string Repository,
    string Branch,
    IReadOnlyList<string> RequiredFiles);

internal sealed record SourceDependencyInstallResult(bool Succeeded, string Status, string? SourcePath = null);

internal static class SourceDependencyEnvironment
{
    private static readonly SourceDependencySpec[] Specs =
    [
        new(
            "bitnet_cpp",
            "bitnet.cpp",
            "microsoft/BitNet",
            "main",
            ["run_inference.py", "setup_env.py", "requirements.txt"])
    ];

    private static readonly Dictionary<string, SourceDependencySpec> SpecsById =
        Specs.ToDictionary(spec => spec.Id, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<SourceDependencySpec> AllSpecs => Specs;

    public static bool HasManagedInstaller(string dependencyId)
    {
        return SpecsById.ContainsKey(dependencyId);
    }

    public static bool TryGetSpec(string dependencyId, out SourceDependencySpec spec)
    {
        if (SpecsById.TryGetValue(dependencyId, out var found))
        {
            spec = found;
            return true;
        }

        spec = null!;
        return false;
    }

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

            return Path.Combine(localAppData, "ContextControl", "dependencies", "source");
        }
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

    public static string? FindManagedSource(string dependencyId, IReadOnlyList<string> requiredFiles)
    {
        var directory = ManagedDependencyDirectory(dependencyId);
        if (!Directory.Exists(directory))
        {
            return null;
        }

        try
        {
            return Directory
                .EnumerateDirectories(directory, "*", SearchOption.AllDirectories)
                .Append(directory)
                .OrderByDescending(Directory.GetLastWriteTimeUtc)
                .FirstOrDefault(path => requiredFiles.All(file => File.Exists(Path.Combine(path, file))));
        }
        catch
        {
            return null;
        }
    }

    public static async Task<SourceDependencyInstallResult> InstallLatestSourceArchiveAsync(
        SourceDependencySpec spec,
        IProgress<string> terminal,
        IProgress<LocalLlmTransferProgress> progress,
        CancellationToken cancellationToken = default)
    {
        var operation = $"Installing {spec.DisplayName}";
        var existing = FindManagedSource(spec.Id, spec.RequiredFiles);
        if (!string.IsNullOrWhiteSpace(existing))
        {
            return new SourceDependencyInstallResult(true, $"{spec.DisplayName} source already validates at {existing}.", existing);
        }

        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("ContextControl", "1.0"));
        var installDirectory = Path.Combine(ManagedDependencyDirectory(spec.Id), SanitizeDependencyId(spec.Branch));
        Directory.CreateDirectory(installDirectory);
        var downloadUrl = $"https://github.com/{spec.Repository}/archive/refs/heads/{spec.Branch}.zip";
        var zipPath = Path.Combine(installDirectory, $"{SanitizeDependencyId(spec.Branch)}.zip");

        terminal.Report($"> download {downloadUrl}");
        progress.Report(new LocalLlmTransferProgress(operation, $"Downloading {spec.DisplayName} source archive", null, null, null, null));
        var downloadResult = await DownloadFileAsync(http, downloadUrl, zipPath, operation, progress, cancellationToken).ConfigureAwait(false);
        if (!downloadResult.Succeeded)
        {
            return downloadResult;
        }

        var extractDirectory = Path.Combine(installDirectory, "extracted");
        Directory.CreateDirectory(extractDirectory);
        progress.Report(new LocalLlmTransferProgress(operation, $"Extracting {Path.GetFileName(zipPath)}", null, null, null, null));
        await Task.Run(() => ExtractZipSafely(zipPath, extractDirectory), cancellationToken).ConfigureAwait(false);

        var sourcePath = FindManagedSource(spec.Id, spec.RequiredFiles);
        if (sourcePath is null)
        {
            return new SourceDependencyInstallResult(false, $"{spec.DisplayName} source downloaded, but expected files were not found.");
        }

        progress.Report(new LocalLlmTransferProgress(operation, $"{spec.DisplayName} source ready at {sourcePath}", null, null, null, 100));
        return new SourceDependencyInstallResult(true, $"{spec.DisplayName} source installed locally at {sourcePath}. Model weights are not downloaded automatically.", sourcePath);
    }

    private static async Task<SourceDependencyInstallResult> DownloadFileAsync(
        HttpClient http,
        string url,
        string path,
        string operation,
        IProgress<LocalLlmTransferProgress> progress,
        CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return new SourceDependencyInstallResult(false, $"Download failed: HTTP {(int)response.StatusCode}.");
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

        return new SourceDependencyInstallResult(true, $"Downloaded {path}.");
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
