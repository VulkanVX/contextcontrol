// CC-DESC: Runs native Context Control exports with PowerShell fallback for the workbench.

using System.Diagnostics;
using System.Text;

namespace ContextControl.Workbench.Services;

public sealed class ContextControlProcessService
{
    private const string DirectoryExportFileName = "cc_project_dir.md";
    private const string DirectoryTreeExportFileName = "cc_project_dir_tree.md";
    private const string SemanticMapFileName = "cc_semantic_map.md";
    private const string CodeExportFileName = "cc_code_export.md";
    private const string PatchFileName = "patch.txt";

    public ContextControlProcessService(string contextRoot)
    {
        ContextRoot = string.IsNullOrWhiteSpace(contextRoot)
            ? FindContextControlRoot(Directory.GetCurrentDirectory()) ?? AppContext.BaseDirectory
            : Path.GetFullPath(contextRoot);
    }

    public string ContextRoot { get; }
    public string DirectoryExportPath => Path.Combine(ContextRoot, DirectoryExportFileName);
    public string DirectoryTreeExportPath => Path.Combine(ContextRoot, DirectoryTreeExportFileName);
    public string SemanticMapPath => Path.Combine(ContextRoot, SemanticMapFileName);
    public string CodeExportPath => Path.Combine(ContextRoot, CodeExportFileName);
    public string PatchPath => Path.Combine(ContextRoot, PatchFileName);

    public static string? FindContextControlRoot(string startPath)
    {
        if (string.IsNullOrWhiteSpace(startPath))
        {
            return null;
        }

        var directory = File.Exists(startPath)
            ? new DirectoryInfo(Path.GetDirectoryName(startPath) ?? "")
            : new DirectoryInfo(startPath);

        while (directory is not null)
        {
            if (LooksLikeContextControl(directory.FullName))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }

    public async Task<ContextControlCommandResult> RunDirectoryExportAsync(
        string? projectRootOverride = null,
        string? fileRulesPathOverride = null,
        CancellationToken cancellationToken = default)
    {
        var profilePath = ContextDirProfileService.GetProfilePath(ContextRoot, projectRootOverride, fileRulesPathOverride);
        return await RunNativeOrPowerShellExportAsync(
            "DIR",
            "dir",
            ["-OutputFile", DirectoryExportPath, "-ProfileFile", profilePath ?? ""],
            "ccDir.ps1",
            ["-OutputFile", DirectoryExportPath, "-NoClipboard"],
            null,
            DirectoryExportPath,
            projectRootOverride,
            fileRulesPathOverride,
            cancellationToken,
            profilePath);
    }

    public async Task<ContextControlCommandResult> RunDirectoryTreeExportAsync(
        string? projectRootOverride = null,
        string? fileRulesPathOverride = null,
        CancellationToken cancellationToken = default)
    {
        var result = await RunDirectoryExportAsync(projectRootOverride, fileRulesPathOverride, cancellationToken);
        if (!result.Succeeded)
        {
            return new ContextControlCommandResult(
                "DIR tree",
                result.ExitCode,
                result.StandardOutput,
                result.StandardError,
                result.OutputFile,
                result.Runner,
                result.Elapsed,
                result.FallbackReason);
        }

        var directoryExportText = await ReadOutputFileAsync(DirectoryExportPath);
        if (string.IsNullOrWhiteSpace(directoryExportText))
        {
            return new ContextControlCommandResult(
                "DIR tree",
                1,
                "",
                "Directory export produced no manifest text.",
                DirectoryTreeExportPath,
                result.Runner,
                result.Elapsed,
                result.FallbackReason);
        }

        var treeText = BuildDirectoryTreeText(directoryExportText);
        await File.WriteAllTextAsync(DirectoryTreeExportPath, treeText, new UTF8Encoding(false, false), cancellationToken);

        return new ContextControlCommandResult(
            "DIR tree",
            0,
            $"Wrote {Path.GetFileName(DirectoryTreeExportPath)}",
            "",
            DirectoryTreeExportPath,
            result.Runner,
            result.Elapsed,
            result.FallbackReason);
    }

    public async Task<ContextControlCommandResult> RunDirectoryExpandAsync(
        string scope,
        string? projectRootOverride = null,
        string? fileRulesPathOverride = null,
        CancellationToken cancellationToken = default)
    {
        return await RunNativeOrPowerShellExportAsync(
            "DIR expand",
            "dir",
            ["-OutputFile", DirectoryExportPath, "-Lod", "1", "-Scope", scope ?? ""],
            "ccDir.ps1",
            ["-OutputFile", DirectoryExportPath, "-Lod", "1", "-Scope", scope ?? "", "-NoClipboard"],
            null,
            DirectoryExportPath,
            projectRootOverride,
            fileRulesPathOverride,
            cancellationToken);
    }

    public async Task<ContextControlCommandResult> RunCodeExportAsync(
        IEnumerable<string> requestLines,
        string? projectRootOverride = null,
        string? fileRulesPathOverride = null,
        CancellationToken cancellationToken = default)
    {
        var input = NormalizeRequestInput(requestLines);
        return await RunNativeOrPowerShellExportAsync(
            "CC",
            "cc",
            ["-OutputFile", CodeExportPath, "-NoClipboard"],
            "cc.ps1",
            ["-OutputFile", CodeExportPath, "-NoClipboard"],
            input,
            CodeExportPath,
            projectRootOverride,
            fileRulesPathOverride,
            cancellationToken);
    }

    private async Task<ContextControlCommandResult> RunNativeOrPowerShellExportAsync(
        string command,
        string nativeVerb,
        IReadOnlyList<string> nativeArguments,
        string scriptName,
        IReadOnlyList<string> powershellArguments,
        string? standardInput,
        string? outputFile,
        string? projectRootOverride,
        string? fileRulesPathOverride,
        CancellationToken cancellationToken,
        string? dirProfilePath = null)
    {
        if (!IsNativeExportDisabled() && TryResolveNativeExporterPath() is { } nativePath)
        {
            var nativeResult = await RunNativeProcessAsync(
                nativePath,
                command,
                nativeVerb,
                nativeArguments,
                standardInput,
                outputFile,
                projectRootOverride,
                fileRulesPathOverride,
                cancellationToken,
                dirProfilePath);

            if (!ShouldFallbackFromNative(nativeResult, outputFile))
            {
                return nativeResult;
            }

            return await RunPowerShellScriptAsync(
                command,
                scriptName,
                powershellArguments,
                standardInput,
                outputFile,
                projectRootOverride,
                fileRulesPathOverride,
                cancellationToken,
                dirProfilePath,
                BuildNativeFallbackReason(nativeResult, outputFile));
        }

        return await RunPowerShellScriptAsync(
            command,
            scriptName,
            powershellArguments,
            standardInput,
            outputFile,
            projectRootOverride,
            fileRulesPathOverride,
            cancellationToken,
            dirProfilePath);
    }

    private async Task<ContextControlCommandResult> RunNativeProcessAsync(
        string nativePath,
        string command,
        string nativeVerb,
        IReadOnlyList<string> arguments,
        string? standardInput,
        string? outputFile,
        string? projectRootOverride,
        string? fileRulesPathOverride,
        CancellationToken cancellationToken,
        string? dirProfilePath = null)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = nativePath,
            WorkingDirectory = ContextRoot,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false, false),
            StandardOutputEncoding = new UTF8Encoding(false, false),
            StandardErrorEncoding = new UTF8Encoding(false, false),
            CreateNoWindow = true
        };

        if (!string.IsNullOrWhiteSpace(projectRootOverride))
        {
            startInfo.Environment["CC_WORKBENCH_PROJECT_ROOT"] = Path.GetFullPath(projectRootOverride);
        }

        if (!string.IsNullOrWhiteSpace(fileRulesPathOverride))
        {
            startInfo.Environment["CC_WORKBENCH_FILE_RULES_PATH"] = Path.GetFullPath(fileRulesPathOverride);
        }

        if (!string.IsNullOrWhiteSpace(dirProfilePath))
        {
            startInfo.Environment["CC_WORKBENCH_DIR_PROFILE_PATH"] = Path.GetFullPath(dirProfilePath);
        }

        startInfo.ArgumentList.Add(nativeVerb);
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        var stopwatch = Stopwatch.StartNew();
        process.Start();

        if (!string.IsNullOrWhiteSpace(standardInput))
        {
            await process.StandardInput.WriteAsync(standardInput);
        }

        process.StandardInput.Close();

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }

        stopwatch.Stop();
        var output = await outputTask;
        var error = await errorTask;
        return new ContextControlCommandResult(command, process.ExitCode, output, error, outputFile, "Native", stopwatch.Elapsed);
    }

    private string? TryResolveNativeExporterPath()
    {
        foreach (var environmentName in new[] { "CC_WORKBENCH_NATIVE_EXPORTER", "CC_NATIVE_EXPORTER", "CCNATIVE_EXE" })
        {
            var configured = Environment.GetEnvironmentVariable(environmentName);
            if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
            {
                return Path.GetFullPath(configured);
            }
        }

        var executableName = OperatingSystem.IsWindows() ? "ccnative.exe" : "ccnative";
        var candidates = new[]
        {
            Path.Combine(ContextRoot, executableName),
            Path.Combine(ContextRoot, "build", "native-contextcontrol", "Release", executableName),
            Path.Combine(ContextRoot, "build", "native-contextcontrol", executableName),
            Path.Combine(ContextRoot, "native", "contextcontrol", "build", "Release", executableName),
            Path.Combine(ContextRoot, "native", "contextcontrol", "build", executableName),
            Path.Combine(ContextRoot, "native", "contextcontrol", "bin", executableName),
            Path.Combine(AppContext.BaseDirectory, executableName),
            Path.Combine(AppContext.BaseDirectory, "native", executableName)
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    private static bool IsNativeExportDisabled()
    {
        var value = Environment.GetEnvironmentVariable("CC_WORKBENCH_DISABLE_NATIVE_EXPORTS")
            ?? Environment.GetEnvironmentVariable("CC_DISABLE_NATIVE_EXPORTS");

        return !string.IsNullOrWhiteSpace(value)
            && (value.Equals("1", StringComparison.OrdinalIgnoreCase)
                || value.Equals("true", StringComparison.OrdinalIgnoreCase)
                || value.Equals("yes", StringComparison.OrdinalIgnoreCase));
    }

    private static bool ShouldFallbackFromNative(ContextControlCommandResult result, string? outputFile)
    {
        if (result.ExitCode != 0)
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(outputFile) && !File.Exists(outputFile);
    }

    private static string BuildNativeFallbackReason(ContextControlCommandResult nativeResult, string? outputFile)
    {
        if (nativeResult.ExitCode != 0)
        {
            return $"native exited {nativeResult.ExitCode}";
        }

        if (!string.IsNullOrWhiteSpace(outputFile) && !File.Exists(outputFile))
        {
            return $"native did not create {Path.GetFileName(outputFile)}";
        }

        return "native result was not usable";
    }

    public async Task WritePatchAsync(string patchText, CancellationToken cancellationToken = default)
    {
        var parent = Path.GetDirectoryName(PatchPath);
        if (!string.IsNullOrWhiteSpace(parent))
        {
            Directory.CreateDirectory(parent);
        }

        await File.WriteAllTextAsync(PatchPath, patchText ?? "", new UTF8Encoding(false), cancellationToken);
    }

    public async Task WriteSemanticMapAsync(string semanticMapText, CancellationToken cancellationToken = default)
    {
        var parent = Path.GetDirectoryName(SemanticMapPath);
        if (!string.IsNullOrWhiteSpace(parent))
        {
            Directory.CreateDirectory(parent);
        }

        await File.WriteAllTextAsync(SemanticMapPath, semanticMapText ?? "", new UTF8Encoding(false), cancellationToken);
    }

    private static string BuildDirectoryTreeText(string directoryExportText)
    {
        var manifest = ContextDirManifestParser.Parse(directoryExportText);
        var root = new DirectoryTreeNode();

        foreach (var file in manifest.Files)
        {
            var cleanPath = (file.Path ?? "").Replace('\\', '/').Trim().Trim('/');
            if (string.IsNullOrWhiteSpace(cleanPath))
            {
                continue;
            }

            var segments = cleanPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0)
            {
                continue;
            }

            var node = root;
            for (var index = 0; index < segments.Length - 1; index++)
            {
                var segment = segments[index].Trim();
                if (segment.Length == 0)
                {
                    continue;
                }

                if (!node.Directories.TryGetValue(segment, out var nextNode))
                {
                    nextNode = new DirectoryTreeNode();
                    node.Directories[segment] = nextNode;
                }

                node = nextNode;
            }

            var fileName = segments[^1].Trim();
            if (fileName.Length == 0)
            {
                continue;
            }

            var kind = !string.IsNullOrWhiteSpace(file.Kind)
                ? file.Kind.Trim()
                : ResolveFileKind(fileName);
            node.Files[fileName] = kind;
        }

        if (root.Directories.Count == 0 && root.Files.Count == 0)
        {
            return "(no files)" + Environment.NewLine;
        }

        var builder = new StringBuilder();
        AppendDirectoryTree(builder, root, string.Empty);
        return builder.ToString().TrimEnd() + Environment.NewLine;
    }

    private static string ResolveFileKind(string fileName)
    {
        var extension = Path.GetExtension(fileName).TrimStart('.').Trim();
        return extension.Length > 0 ? extension : "file";
    }

    private static void AppendDirectoryTree(StringBuilder builder, DirectoryTreeNode node, string prefix)
    {
        var entries = node.Directories
            .OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
            .Select(entry => new DirectoryTreeEntry(entry.Key, true, entry.Value, string.Empty))
            .Concat(node.Files
                .OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
                .Select(entry => new DirectoryTreeEntry(entry.Key, false, null, entry.Value)))
            .ToArray();

        for (var index = 0; index < entries.Length; index++)
        {
            var entry = entries[index];
            var isLast = index == entries.Length - 1;
            var connector = isLast ? "+-- " : "|-- ";
            var childPrefix = isLast ? "    " : "|   ";

            if (entry.IsDirectory)
            {
                builder.AppendLine($"{prefix}{connector}{entry.Name}/");
                AppendDirectoryTree(builder, entry.DirectoryNode!, prefix + childPrefix);
            }
            else
            {
                builder.AppendLine($"{prefix}{connector}{entry.Name}");
            }
        }
    }

    private sealed class DirectoryTreeNode
    {
        public Dictionary<string, DirectoryTreeNode> Directories { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> Files { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed record DirectoryTreeEntry(
        string Name,
        bool IsDirectory,
        DirectoryTreeNode? DirectoryNode,
        string Kind);

    public async Task<ContextControlCommandResult> PreviewPatchAsync(
        string? projectRootOverride = null,
        string? fileRulesPathOverride = null,
        CancellationToken cancellationToken = default)
    {
        return await RunPowerShellScriptAsync(
            "GO preview",
            "ccReplace.ps1",
            ["-InputFile", PatchPath, "-PlanOnly", "-Json"],
            null,
            PatchPath,
            projectRootOverride,
            fileRulesPathOverride,
            cancellationToken);
    }

    public async Task<ContextControlCommandResult> ApplyPatchAsync(
        string decision,
        string? projectRootOverride = null,
        string? fileRulesPathOverride = null,
        CancellationToken cancellationToken = default)
    {
        var cleanDecision = string.Equals(decision, "all", StringComparison.OrdinalIgnoreCase)
            ? "all"
            : "effective";

        return await RunPowerShellScriptAsync(
            "GO apply",
            "ccReplace.ps1",
            ["-InputFile", PatchPath, "-Apply", cleanDecision],
            null,
            PatchPath,
            projectRootOverride,
            fileRulesPathOverride,
            cancellationToken);
    }

    public async Task<string> ReadOutputFileAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path))
        {
            return "";
        }

        return await File.ReadAllTextAsync(path, cancellationToken);
    }

    private async Task<ContextControlCommandResult> RunPowerShellScriptAsync(
        string command,
        string scriptName,
        IReadOnlyList<string> arguments,
        string? standardInput,
        string? outputFile,
        string? projectRootOverride,
        string? fileRulesPathOverride,
        CancellationToken cancellationToken,
        string? dirProfilePath = null,
        string? fallbackReason = null)
    {
        var scriptPath = Path.Combine(ContextRoot, scriptName);
        if (!File.Exists(scriptPath))
        {
            return new ContextControlCommandResult(command, 1, "", $"Script not found: {scriptPath}", outputFile, "PowerShell", null, fallbackReason);
        }

        var executables = OperatingSystem.IsWindows()
            ? new[] { "powershell", "pwsh" }
            : new[] { "pwsh", "powershell" };

        foreach (var executable in executables)
        {
            try
            {
                return await RunProcessAsync(executable, command, scriptPath, arguments, standardInput, outputFile, projectRootOverride, fileRulesPathOverride, cancellationToken, dirProfilePath, fallbackReason);
            }
            catch (System.ComponentModel.Win32Exception) when (!string.Equals(executable, executables[^1], StringComparison.OrdinalIgnoreCase))
            {
            }
        }

        return await RunProcessAsync(executables[^1], command, scriptPath, arguments, standardInput, outputFile, projectRootOverride, fileRulesPathOverride, cancellationToken, dirProfilePath, fallbackReason);
    }

    private async Task<ContextControlCommandResult> RunProcessAsync(
        string executable,
        string command,
        string scriptPath,
        IReadOnlyList<string> arguments,
        string? standardInput,
        string? outputFile,
        string? projectRootOverride,
        string? fileRulesPathOverride,
        CancellationToken cancellationToken,
        string? dirProfilePath = null,
        string? fallbackReason = null)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = ContextRoot,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false, false),
            StandardOutputEncoding = new UTF8Encoding(false, false),
            StandardErrorEncoding = new UTF8Encoding(false, false),
            CreateNoWindow = true
        };

        if (!string.IsNullOrWhiteSpace(projectRootOverride))
        {
            startInfo.Environment["CC_WORKBENCH_PROJECT_ROOT"] = Path.GetFullPath(projectRootOverride);
        }

        if (!string.IsNullOrWhiteSpace(fileRulesPathOverride))
        {
            startInfo.Environment["CC_WORKBENCH_FILE_RULES_PATH"] = Path.GetFullPath(fileRulesPathOverride);
        }

        if (!string.IsNullOrWhiteSpace(dirProfilePath))
        {
            startInfo.Environment["CC_WORKBENCH_DIR_PROFILE_PATH"] = Path.GetFullPath(dirProfilePath);
        }

        startInfo.ArgumentList.Add("-NoLogo");
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(scriptPath);

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        var stopwatch = Stopwatch.StartNew();
        process.Start();

        if (!string.IsNullOrWhiteSpace(standardInput))
        {
            await process.StandardInput.WriteAsync(standardInput);
        }

        process.StandardInput.Close();

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }

        stopwatch.Stop();
        var output = await outputTask;
        var error = await errorTask;
        var runner = string.IsNullOrWhiteSpace(fallbackReason) ? "PowerShell" : "PowerShell fallback";
        return new ContextControlCommandResult(command, process.ExitCode, output, error, outputFile, runner, stopwatch.Elapsed, fallbackReason);
    }

    private static string NormalizeRequestInput(IEnumerable<string> requestLines)
    {
        var lines = requestLines
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Where(line => !string.Equals(line, "END", StringComparison.OrdinalIgnoreCase))
            .ToList();

        lines.Add("END");
        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    private static bool LooksLikeContextControl(string path)
    {
        return Directory.Exists(path)
            && File.Exists(Path.Combine(path, "ccStart.ps1"))
            && File.Exists(Path.Combine(path, "ccDir.ps1"))
            && File.Exists(Path.Combine(path, "cc.ps1"))
            && File.Exists(Path.Combine(path, "ccReplace.ps1"));
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
            // Cancellation cleanup is best-effort.
        }
    }
}
