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
    private static readonly IReadOnlyDictionary<string, long> EmptyModelSizes =
        new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

    private static async Task<InstalledModelsResult> DetectInstalledModelsAsync(CancellationToken cancellationToken)
    {
        var httpResult = await DetectInstalledModelsFromHttpAsync(cancellationToken).ConfigureAwait(false);
        if (httpResult.Reachable || httpResult.ModelIds.Count > 0)
        {
            return httpResult;
        }

        var commandResult = await DetectInstalledModelsFromCommandAsync(cancellationToken).ConfigureAwait(false);
        if (commandResult.Reachable || commandResult.ModelIds.Count > 0)
        {
            return commandResult;
        }

        var diskResult = DetectInstalledModelsFromDisk(
            commandResult.ExecutablePath ?? httpResult.ExecutablePath,
            commandResult.Installed ? commandResult.Status : httpResult.Status);
        if (diskResult.ModelIds.Count > 0)
        {
            return diskResult;
        }

        return commandResult.Installed ? commandResult : httpResult;
    }

    private static async Task<InstalledModelsResult> DetectInstalledModelsFromHttpAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var http = CreateHttpClient(TimeSpan.FromMilliseconds(900));
            var text = await http.GetStringAsync(new Uri(OllamaBaseUri, "/api/tags"), cancellationToken).ConfigureAwait(false);
            var tags = JsonSerializer.Deserialize<OllamaTagsResponse>(text, JsonOptions);
            var models = tags?.Models ?? [];
            var ids = models
                .Select(ResolveOllamaModelTagId)
                .Where(model => !string.IsNullOrWhiteSpace(model))
                .Select(model => model!.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var sizes = models
                .Select(model => (Id: ResolveOllamaModelTagId(model), Size: model.Size))
                .Where(model => !string.IsNullOrWhiteSpace(model.Id) && model.Size is > 0)
                .GroupBy(model => model.Id!.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First().Size!.Value, StringComparer.OrdinalIgnoreCase);

            return new InstalledModelsResult(true, true, ids, sizes, ResolveOllamaExecutable(), $"Ollama ready. {ids.Count} local model(s) installed.");
        }
        catch
        {
            var executablePath = ResolveOllamaExecutable();
            return executablePath is not null
                ? new InstalledModelsResult(true, false, new HashSet<string>(StringComparer.OrdinalIgnoreCase), EmptyModelSizes, executablePath, $"Ollama installed at {executablePath}. Start Ollama to detect installed models.")
                : new InstalledModelsResult(false, false, new HashSet<string>(StringComparer.OrdinalIgnoreCase), EmptyModelSizes, null, "Ollama is not running. Start Ollama to detect installed models.");
        }
    }

    private static async Task<InstalledModelsResult> DetectInstalledModelsFromCommandAsync(CancellationToken cancellationToken)
    {
        var ollamaPath = ResolveOllamaExecutable();
        if (ollamaPath is null)
        {
            return new InstalledModelsResult(false, false, new HashSet<string>(StringComparer.OrdinalIgnoreCase), EmptyModelSizes, null, "Ollama command was not found. Install Ollama to download and chat locally.");
        }

        var result = await RunProcessAsync(
            ollamaPath,
            ["list"],
            TimeSpan.FromSeconds(3),
            cancellationToken).ConfigureAwait(false);

        if (!result.Started)
        {
            return new InstalledModelsResult(true, false, new HashSet<string>(StringComparer.OrdinalIgnoreCase), EmptyModelSizes, ollamaPath, $"Ollama found at {ollamaPath}, but it could not be started.");
        }

        if (result.ExitCode != 0)
        {
            return new InstalledModelsResult(true, false, new HashSet<string>(StringComparer.OrdinalIgnoreCase), EmptyModelSizes, ollamaPath, FirstLine(result.StandardError) ?? "Ollama is installed but not responding.");
        }

        var lines = result.StandardOutput
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var modelLines = lines.Skip(1).ToArray();
        var ids = modelLines
            .Select(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault())
            .Where(model => !string.IsNullOrWhiteSpace(model))
            .Select(model => model!.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var sizes = modelLines
            .Select(ParseOllamaListSize)
            .Where(item => item.HasValue && item.Value.Size > 0)
            .Select(item => item!.Value)
            .GroupBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Size, StringComparer.OrdinalIgnoreCase);

        return new InstalledModelsResult(true, true, ids, sizes, ollamaPath, $"Ollama ready. {ids.Count} local model(s) installed.");
    }

    private static string? ResolveOllamaModelTagId(OllamaModelTag model)
    {
        return string.IsNullOrWhiteSpace(model.Name) ? model.Model : model.Name;
    }

    private static (string Id, long Size)? ParseOllamaListSize(string line)
    {
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 4 || string.IsNullOrWhiteSpace(parts[0]))
        {
            return null;
        }

        if (!double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || value <= 0)
        {
            return null;
        }

        var multiplier = parts[3].Trim().ToUpperInvariant() switch
        {
            "TB" or "TIB" => 1024d * 1024d * 1024d * 1024d,
            "GB" or "GIB" => 1024d * 1024d * 1024d,
            "MB" or "MIB" => 1024d * 1024d,
            "KB" or "KIB" => 1024d,
            "B" => 1d,
            _ => 0d
        };
        if (multiplier <= 0)
        {
            return null;
        }

        return (parts[0], (long)Math.Round(value * multiplier));
    }

    private static InstalledModelsResult DetectInstalledModelsFromDisk(string? executablePath, string fallbackStatus)
    {
        var root = ResolveOllamaModelsDirectory(null);
        var manifestsRoot = Path.Combine(root, "manifests");
        if (!Directory.Exists(manifestsRoot))
        {
            return new InstalledModelsResult(
                executablePath is not null,
                false,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                EmptyModelSizes,
                executablePath,
                fallbackStatus);
        }

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sizes = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var manifestPath in Directory.EnumerateFiles(manifestsRoot, "*", SearchOption.AllDirectories))
            {
                cancellationSafeNoop();
                var modelId = TryResolveManifestModelId(manifestsRoot, manifestPath);
                if (string.IsNullOrWhiteSpace(modelId))
                {
                    continue;
                }

                ids.Add(modelId);
                var size = TryReadManifestModelSize(root, manifestPath);
                if (size > 0)
                {
                    sizes[modelId] = size;
                }
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return new InstalledModelsResult(
            executablePath is not null || ids.Count > 0,
            false,
            ids,
            sizes,
            executablePath,
            ids.Count > 0
                ? $"Ollama is installed but not running. {ids.Count} model(s) found on disk. Start Ollama to chat."
                : fallbackStatus);

        static void cancellationSafeNoop()
        {
        }
    }

    private static string? TryResolveManifestModelId(string manifestsRoot, string manifestPath)
    {
        var relative = Path.GetRelativePath(manifestsRoot, manifestPath)
            .Replace('\\', '/');
        var parts = relative.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 3)
        {
            return null;
        }

        var tag = parts[^1];
        if (string.IsNullOrWhiteSpace(tag))
        {
            return null;
        }

        var nameParts = parts.Skip(1).Take(parts.Length - 2).ToArray();
        if (nameParts.Length == 0)
        {
            return null;
        }

        if (nameParts.Length >= 2 && nameParts[0].Equals("library", StringComparison.OrdinalIgnoreCase))
        {
            nameParts = nameParts.Skip(1).ToArray();
        }

        var name = string.Join("/", nameParts);
        return string.IsNullOrWhiteSpace(name) ? null : $"{name}:{tag}";
    }

    private static long TryReadManifestModelSize(string modelsRoot, string manifestPath)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
            if (!document.RootElement.TryGetProperty("layers", out var layers)
                || layers.ValueKind != JsonValueKind.Array)
            {
                return 0;
            }

            long total = 0;
            foreach (var layer in layers.EnumerateArray())
            {
                if (!layer.TryGetProperty("digest", out var digestProperty))
                {
                    continue;
                }

                var digest = digestProperty.GetString();
                if (string.IsNullOrWhiteSpace(digest))
                {
                    continue;
                }

                var blobName = digest.Replace(":", "-", StringComparison.Ordinal);
                var blobPath = Path.Combine(modelsRoot, "blobs", blobName);
                if (File.Exists(blobPath))
                {
                    total += new FileInfo(blobPath).Length;
                }
            }

            return total;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return 0;
        }
    }

    public static async Task<LocalLlmHardwareProfile> DetectHardwareAsync(CancellationToken cancellationToken)
    {
        var cpuTask = DetectCpuAsync(cancellationToken);
        var gpus = await DetectNvidiaGpusAsync(cancellationToken).ConfigureAwait(false);
        if (gpus.Count == 0) gpus = await DetectWindowsGpusAsync(cancellationToken).ConfigureAwait(false);
        var cpu = await cpuTask.ConfigureAwait(false);
        var memory = LocalSystemMemory.Read();
        cancellationToken.ThrowIfCancellationRequested();
        return new(gpus, memory.Total, memory.Available, cpu.Name, cpu.Cores, Environment.ProcessorCount);
    }

    private static async Task<(string Name, int? Cores)> DetectCpuAsync(CancellationToken token)
    {
        if (!OperatingSystem.IsWindows()) return (Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? "CPU", null);
        var result = await RunProcessAsync("powershell",
            ["-NoLogo", "-NoProfile", "-NonInteractive", "-Command", "Get-CimInstance Win32_Processor | ForEach-Object { $_.Name + '|' + $_.NumberOfCores }"],
            TimeSpan.FromSeconds(3), token).ConfigureAwait(false);
        var parts = result.StandardOutput.Trim().Split('|', 2);
        return (parts[0].Trim(), parts.Length > 1 && int.TryParse(parts[1].Trim(), out var count) ? count : null);
    }

    private static async Task<IReadOnlyList<LocalLlmGpuInfo>> DetectNvidiaGpusAsync(CancellationToken cancellationToken)
    {
        var result = await RunProcessAsync(
            "nvidia-smi",
            ["--query-gpu=name,memory.total,memory.free", "--format=csv,noheader,nounits"],
            TimeSpan.FromSeconds(2),
            cancellationToken).ConfigureAwait(false);

        if (!result.Started || result.ExitCode != 0)
        {
            return [];
        }

        return result.StandardOutput
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(ParseNvidiaGpuLine)
            .Where(gpu => gpu is not null)
            .Select(gpu => gpu!)
            .ToArray();
    }

    private static LocalLlmGpuInfo? ParseNvidiaGpuLine(string line)
    {
        var parts = line.Split(',', 3, StringSplitOptions.TrimEntries);
        if (parts.Length == 0 || string.IsNullOrWhiteSpace(parts[0]))
        {
            return null;
        }

        long? bytes = null;
        if (parts.Length > 1 && long.TryParse(parts[1], out var mib))
        {
            bytes = mib * 1024L * 1024L;
        }

        return new LocalLlmGpuInfo(parts[0], bytes, parts.Length > 2 && long.TryParse(parts[2], out var free) && free >= 0 ? free * 1024L * 1024L : null);
    }

    private static async Task<IReadOnlyList<LocalLlmGpuInfo>> DetectWindowsGpusAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            return [];
        }

        const string command = "Get-CimInstance Win32_VideoController | ForEach-Object { (($_.Name -replace '\\|',' ') + '|' + $_.AdapterRAM) }";
        var result = await RunProcessAsync(
            "powershell",
            ["-NoLogo", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", command],
            TimeSpan.FromMilliseconds(1200),
            cancellationToken).ConfigureAwait(false);

        if (!result.Started || result.ExitCode != 0)
        {
            return [];
        }

        return result.StandardOutput
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(ParseWindowsGpuLine)
            .Where(gpu => gpu is not null)
            .Select(gpu => gpu!)
            .ToArray();
    }

    private static LocalLlmGpuInfo? ParseWindowsGpuLine(string line)
    {
        var parts = line.Split('|', 2, StringSplitOptions.TrimEntries);
        if (parts.Length == 0 || string.IsNullOrWhiteSpace(parts[0]))
        {
            return null;
        }

        long? bytes = null;
        if (parts.Length > 1 && long.TryParse(parts[1], out var parsed) && parsed > 0)
        {
            bytes = parsed;
        }

        return new LocalLlmGpuInfo(parts[0], bytes);
    }

}
