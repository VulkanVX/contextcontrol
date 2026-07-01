// CC-DESC: Builds and validates the normalized DIR project profile used by ccDir manifests.

using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace ContextControl.Workbench.Services;

public sealed class ContextDirProjectProfile
{
    public int SchemaVersion { get; init; }
    public string GeneratedUtc { get; init; } = "";
    public string ProjectRoot { get; init; } = "";
    public string ProfileSource { get; init; } = "";
    public int VisibleFileCount { get; init; }
    public Dictionary<string, int> RootCounts { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> MajorManifestHashes { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> Languages { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<string> Stacks { get; init; } = [];
    public IReadOnlyList<ContextDirProfileRoot> Roots { get; init; } = [];
    public IReadOnlyList<ContextDirProfileFile> Files { get; init; } = [];
    public IReadOnlyList<ContextDirProfileFamily> Families { get; init; } = [];
}

public sealed record ContextDirProfileRoot(string Path, string Role, int Files);
public sealed record ContextDirProfileFile(string Path, string Kind, string Role, string Exports, int Score);
public sealed record ContextDirProfileFamily(string Path, string Role, string Exports);

public static class ContextDirProfileService
{
    private const string ProfileFileName = ".ccDirProfile.json";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = null,
        WriteIndented = true
    };

    public static string? GetProfilePath(
        string contextRoot,
        string? projectRootOverride,
        string? fileRulesPathOverride)
    {
        if (string.IsNullOrWhiteSpace(contextRoot))
        {
            return null;
        }

        var projectRoot = ResolveProjectRoot(contextRoot, projectRootOverride);
        var rulesPath = ResolveRulesPath(projectRoot, fileRulesPathOverride);
        return ResolveProfilePath(contextRoot, rulesPath);
    }

    public static async Task<string?> EnsureProfileAsync(
        string contextRoot,
        string? projectRootOverride,
        string? fileRulesPathOverride,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(contextRoot))
        {
            return null;
        }

        var scriptPath = Path.Combine(contextRoot, "ccDir.ps1");
        if (!File.Exists(scriptPath))
        {
            return null;
        }

        var projectRoot = ResolveProjectRoot(contextRoot, projectRootOverride);
        var rulesPath = ResolveRulesPath(projectRoot, fileRulesPathOverride);
        var profilePath = ResolveProfilePath(contextRoot, rulesPath);

        int? exitCode = null;
        var executables = OperatingSystem.IsWindows()
            ? new[] { "powershell", "pwsh" }
            : new[] { "pwsh", "powershell" };

        foreach (var executable in executables)
        {
            exitCode = await RunProfileExportAsync(executable, scriptPath, projectRoot, rulesPath, profilePath, cancellationToken);
            if (exitCode is not null)
            {
                break;
            }
        }

        if (exitCode != 0 || !File.Exists(profilePath))
        {
            return null;
        }

        await NormalizeProfileFileAsync(profilePath, cancellationToken);
        return profilePath;
    }

    private static string ResolveProjectRoot(string contextRoot, string? projectRootOverride)
    {
        return Path.GetFullPath(string.IsNullOrWhiteSpace(projectRootOverride)
            ? contextRoot
            : projectRootOverride);
    }

    private static string ResolveRulesPath(string projectRoot, string? fileRulesPathOverride)
    {
        if (!string.IsNullOrWhiteSpace(fileRulesPathOverride))
        {
            return Path.GetFullPath(fileRulesPathOverride);
        }

        return ProjectFileRules.Load(projectRoot).RulesPath;
    }

    private static string ResolveProfilePath(string contextRoot, string rulesPath)
    {
        var directory = Path.GetDirectoryName(rulesPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            directory = contextRoot;
        }

        Directory.CreateDirectory(directory);
        return Path.Combine(directory, ProfileFileName);
    }

    private static async Task<int?> RunProfileExportAsync(
        string executable,
        string scriptPath,
        string projectRoot,
        string rulesPath,
        string profilePath,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = Path.GetDirectoryName(scriptPath) ?? projectRoot,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = new UTF8Encoding(false, false),
            StandardErrorEncoding = new UTF8Encoding(false, false),
            CreateNoWindow = true
        };

        startInfo.Environment["CC_WORKBENCH_PROJECT_ROOT"] = projectRoot;
        startInfo.Environment["CC_WORKBENCH_FILE_RULES_PATH"] = rulesPath;
        startInfo.ArgumentList.Add("-NoLogo");
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(scriptPath);
        startInfo.ArgumentList.Add("-ProfileOnly");
        startInfo.ArgumentList.Add("-ProfileOutput");
        startInfo.ArgumentList.Add(profilePath);

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return null;
            }

            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            await outputTask;
            await errorTask;
            return process.ExitCode;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    private static async Task NormalizeProfileFileAsync(string profilePath, CancellationToken cancellationToken)
    {
        try
        {
            var json = await File.ReadAllTextAsync(profilePath, cancellationToken);
            var profile = JsonSerializer.Deserialize<ContextDirProjectProfile>(json, JsonOptions);
            if (profile is null || profile.SchemaVersion <= 0)
            {
                return;
            }

            await File.WriteAllTextAsync(
                profilePath,
                JsonSerializer.Serialize(profile, JsonOptions) + Environment.NewLine,
                new UTF8Encoding(false),
                cancellationToken);
        }
        catch
        {
            // The PowerShell fallback profile remains usable even if typed normalization fails.
        }
    }
}
