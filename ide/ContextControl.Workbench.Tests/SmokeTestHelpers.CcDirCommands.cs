using System.Diagnostics;
using System.Text.Json.Nodes;
using ContextControl.Workbench.Controls;
using ContextControl.Workbench.Services;
using ContextControl.Workbench.ViewModels;

internal static partial class SmokeTestHelpers
{
internal static void RunCcExportCommandSmoke()
{
    var repoRoot = FindRepositoryRoot(Directory.GetCurrentDirectory());
    if (repoRoot is null)
    {
        throw new InvalidOperationException("Could not locate repository root for cc export command smoke test.");
    }

    var projectRoot = Path.Combine(Path.GetTempPath(), "ContextControlCcExportSmoke", Guid.NewGuid().ToString("N"));
    try
    {
        WriteProjectFile(
            "Views/MainWindow.axaml",
            """
            <Window>
              <Button Content="Codex CLI" />
              <Button Classes="cc-prompt-send" Content="{Binding ContextControl.PromptSendButtonLabel}" Command="{Binding ContextControl.SendCommand}" />
            </Window>
            """);
        WriteProjectFile(
            "Styles/PromptComposer.axaml",
            """
            <Styles>
              <Style Selector="Button.cc-prompt-send">
                <Setter Property="Background" Value="#18222E" />
              </Style>
            </Styles>
            """);
        WriteProjectFile(
            "ViewModels/ContextControl/ShellPromptProperties.cs",
            """
            namespace Smoke;
            public sealed class ShellPromptProperties
            {
                public bool IsCodexPromptMode { get; set; }
                public string PromptSendButtonLabel => IsCodexPromptMode
                    ? "Send to Codex"
                    : "Send";
            }
            """);
        WriteProjectFile(
            "Services/ContextControl/ContextPromptBuilder.cs",
            """
            namespace Smoke;
            public sealed class ContextPromptBuilder
            {
                public string ParsePhase1RequestLines(string text)
                {
                    return text.Trim();
                }
            }
            """);
        WriteProjectFile("contextcontrol/noise.ps1", "Write-Host 'Send to Codex from generated tool folder'\n");
        WriteProjectFile(".ccWorkbench.chat-history.123.json", "{\"PromptText\":\"Send to Codex\"}\n");
        WriteProjectFile("patch.txt", "Send to Codex patch artifact\n");

        var findOutput = RunCc(repoRoot, projectRoot, ["FIND: Send to Codex"]);
        RequireTextContains(findOutput, "- ViewModels/ContextControl/ShellPromptProperties.cs");
        RequireTextContains(findOutput, "- Views/MainWindow.axaml");
        RequireTextContains(findOutput, "- Styles/PromptComposer.axaml");
        RequireTextNotContains(findOutput, "- contextcontrol/noise.ps1");
        RequireTextNotContains(findOutput, "- .ccWorkbench.chat-history.123.json");
        RequireTextNotContains(findOutput, "- patch.txt");

        var lowerFindOutput = RunCc(repoRoot, projectRoot, ["FIND: send to codex"]);
        RequireTextContains(lowerFindOutput, "- ViewModels/ContextControl/ShellPromptProperties.cs");

        var exactPathOutput = RunCc(repoRoot, projectRoot, ["Services/ContextControl/ContextPromptBuilder.cs"]);
        RequireTextContains(exactPathOutput, "public sealed class ContextPromptBuilder");
        RequireTextNotContains(exactPathOutput, "SKIPPED EXCLUDED");
        RequireTextNotContains(exactPathOutput, "MISSING: Services/ContextControl/ContextPromptBuilder.cs");

        var scopedFunctionOutput = RunCc(repoRoot, projectRoot, ["FUNCTION Services/ContextControl/ContextPromptBuilder.cs :: ParsePhase1RequestLines"]);
        RequireTextContains(scopedFunctionOutput, "Source: Services/ContextControl/ContextPromptBuilder.cs");
        RequireTextContains(scopedFunctionOutput, "ParsePhase1RequestLines");

        var propertyOutput = RunCc(repoRoot, projectRoot, ["FUNCTION ViewModels/ContextControl/ShellPromptProperties.cs :: PromptSendButtonLabel"]);
        RequireTextContains(propertyOutput, "Source: ViewModels/ContextControl/ShellPromptProperties.cs");
        RequireTextContains(propertyOutput, "Send to Codex");

        var wildcardFunctionOutput = RunCc(repoRoot, projectRoot, ["FUNCTION ViewModels/ContextControl/*.cs :: PromptSendButtonLabel"]);
        RequireTextContains(wildcardFunctionOutput, "Source: ViewModels/ContextControl/ShellPromptProperties.cs");

        var globalFunctionOutput = RunCc(repoRoot, projectRoot, ["FUNC: ParsePhase1RequestLines"]);
        RequireTextContains(globalFunctionOutput, "## FOUND FUNCTION: ParsePhase1RequestLines");
        RequireTextContains(globalFunctionOutput, "Source: Services/ContextControl/ContextPromptBuilder.cs");

        var functionColonOutput = RunCc(repoRoot, projectRoot, ["FUNCTION: ParsePhase1RequestLines"]);
        RequireTextContains(functionColonOutput, "Source: Services/ContextControl/ContextPromptBuilder.cs");

        var symbolFailure = RunCc(repoRoot, projectRoot, ["SYMBOL: SendCommand"], expectSuccess: false);
        RequireTextContains(symbolFailure, "SYMBOL: is disabled");
    }
    finally
    {
        try
        {
            if (Directory.Exists(projectRoot))
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup only; temp leftovers must not fail smoke checks.
        }
    }

    void WriteProjectFile(string relativePath, string text)
    {
        var fullPath = Path.Combine(projectRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, text);
    }
}

internal static async Task RunContextControlProcessServiceFallbackSmoke()
{
    var repoRoot = FindRepositoryRoot(Directory.GetCurrentDirectory());
    if (repoRoot is null)
    {
        throw new InvalidOperationException("Could not locate repository root for ContextControl process service smoke test.");
    }

    var smokeRoot = Path.Combine(Path.GetTempPath(), "ContextControlProcessServiceSmoke", Guid.NewGuid().ToString("N"));
    var contextRoot = Path.Combine(smokeRoot, "contextcontrol");
    var projectRoot = Path.Combine(smokeRoot, "project");
    var oldNativeExporter = Environment.GetEnvironmentVariable("CC_WORKBENCH_NATIVE_EXPORTER");
    var oldDisableNative = Environment.GetEnvironmentVariable("CC_WORKBENCH_DISABLE_NATIVE_EXPORTS");

    try
    {
        Directory.CreateDirectory(contextRoot);
        Directory.CreateDirectory(projectRoot);
        foreach (var fileName in new[] { "ccDir.ps1", "cc.ps1", "ccReplace.ps1", "ccStart.ps1" })
        {
            File.Copy(Path.Combine(repoRoot, fileName), Path.Combine(contextRoot, fileName), overwrite: true);
        }

        CopyDirectory(Path.Combine(repoRoot, "lib"), Path.Combine(contextRoot, "lib"));
        WriteProjectFile("src/Main.cs", "namespace Smoke; public sealed class Main { public void Run() { } }\n");

        var fakeNative = ResolveFastNoOutputExecutable();
        Environment.SetEnvironmentVariable("CC_WORKBENCH_NATIVE_EXPORTER", fakeNative);
        Environment.SetEnvironmentVariable("CC_WORKBENCH_DISABLE_NATIVE_EXPORTS", null);

        var service = new ContextControlProcessService(contextRoot);
        var result = await service.RunDirectoryExpandAsync("src", projectRoot);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException($"ContextControl process fallback smoke failed: {result.StandardError}{Environment.NewLine}{result.StandardOutput}");
        }

        if (!result.Runner.Equals("PowerShell fallback", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Expected PowerShell fallback runner, got '{result.Runner}'.");
        }

        if (string.IsNullOrWhiteSpace(result.FallbackReason)
            || !result.FallbackReason.Contains("native did not create", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Expected native missing-output fallback reason, got '{result.FallbackReason}'.");
        }

        if (result.Elapsed is null || result.Elapsed.Value <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("ContextControl process results should include elapsed runtime.");
        }

        var manifest = File.ReadAllText(service.DirectoryExportPath);
        RequireTextContains(manifest, "CC-DIR-MANIFEST-V2");
        RequireTextContains(manifest, "FILE path=\"src/Main.cs\"");
    }
    finally
    {
        Environment.SetEnvironmentVariable("CC_WORKBENCH_NATIVE_EXPORTER", oldNativeExporter);
        Environment.SetEnvironmentVariable("CC_WORKBENCH_DISABLE_NATIVE_EXPORTS", oldDisableNative);
        try
        {
            if (Directory.Exists(smokeRoot))
            {
                Directory.Delete(smokeRoot, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup only; temp leftovers must not fail smoke checks.
        }
    }

    void WriteProjectFile(string relativePath, string text)
    {
        var fullPath = Path.Combine(projectRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, text);
    }

    static string ResolveFastNoOutputExecutable()
    {
        if (OperatingSystem.IsWindows())
        {
            return Path.Combine(Environment.SystemDirectory, "cmd.exe");
        }

        if (File.Exists("/bin/true"))
        {
            return "/bin/true";
        }

        if (File.Exists("/usr/bin/true"))
        {
            return "/usr/bin/true";
        }

        throw new InvalidOperationException("Could not locate a fast no-output executable for native fallback smoke testing.");
    }

    static void CopyDirectory(string sourceRoot, string targetRoot)
    {
        Directory.CreateDirectory(targetRoot);
        foreach (var directory in Directory.EnumerateDirectories(sourceRoot, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(targetRoot, Path.GetRelativePath(sourceRoot, directory)));
        }

        foreach (var file in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(targetRoot, Path.GetRelativePath(sourceRoot, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }
}

internal static void RunCcDir(string repoRoot, string projectRoot, string outputPath, string[] arguments)
{
    var script = Path.Combine(repoRoot, "ccDir.ps1");
    var executable = OperatingSystem.IsWindows() ? "powershell" : "pwsh";
    var psi = new ProcessStartInfo(executable)
    {
        WorkingDirectory = repoRoot,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false
    };
    psi.Environment["CC_WORKBENCH_PROJECT_ROOT"] = projectRoot;
    psi.ArgumentList.Add("-NoProfile");
    if (OperatingSystem.IsWindows())
    {
        psi.ArgumentList.Add("-ExecutionPolicy");
        psi.ArgumentList.Add("Bypass");
    }

    psi.ArgumentList.Add("-File");
    psi.ArgumentList.Add(script);
    psi.ArgumentList.Add("-OutputFile");
    psi.ArgumentList.Add(outputPath);
    foreach (var argument in arguments)
    {
        psi.ArgumentList.Add(argument);
    }

    using var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start ccDir smoke test process.");
    var stdout = process.StandardOutput.ReadToEnd();
    var stderr = process.StandardError.ReadToEnd();
    process.WaitForExit();
    if (process.ExitCode != 0)
    {
        throw new InvalidOperationException($"ccDir smoke test failed with exit {process.ExitCode}.\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");
    }
}

internal static string RunCc(string repoRoot, string projectRoot, string[] requestLines, bool expectSuccess = true)
{
    var script = Path.Combine(repoRoot, "cc.ps1");
    var executable = OperatingSystem.IsWindows() ? "powershell" : "pwsh";
    var outputPath = Path.Combine(projectRoot, $"cc_code_export_{Guid.NewGuid():N}.md");
    var psi = new ProcessStartInfo(executable)
    {
        WorkingDirectory = repoRoot,
        RedirectStandardInput = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false
    };
    psi.Environment["CC_WORKBENCH_PROJECT_ROOT"] = projectRoot;
    psi.ArgumentList.Add("-NoProfile");
    if (OperatingSystem.IsWindows())
    {
        psi.ArgumentList.Add("-ExecutionPolicy");
        psi.ArgumentList.Add("Bypass");
    }

    psi.ArgumentList.Add("-File");
    psi.ArgumentList.Add(script);
    psi.ArgumentList.Add("-OutputFile");
    psi.ArgumentList.Add(outputPath);
    psi.ArgumentList.Add("-NoClipboard");

    using var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start cc export smoke test process.");
    foreach (var line in requestLines)
    {
        process.StandardInput.WriteLine(line);
    }

    process.StandardInput.WriteLine("END");
    process.StandardInput.Close();
    var stdout = process.StandardOutput.ReadToEnd();
    var stderr = process.StandardError.ReadToEnd();
    process.WaitForExit();

    if (expectSuccess && process.ExitCode != 0)
    {
        throw new InvalidOperationException($"cc export smoke test failed with exit {process.ExitCode}.\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");
    }

    if (!expectSuccess)
    {
        if (process.ExitCode == 0)
        {
            throw new InvalidOperationException($"cc export smoke test was expected to fail, but succeeded.\nSTDOUT:\n{stdout}");
        }

        return stdout + Environment.NewLine + stderr;
    }

    if (!File.Exists(outputPath))
    {
        throw new InvalidOperationException($"cc export smoke test did not create output file: {outputPath}\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");
    }

    return File.ReadAllText(outputPath);
}

internal static DirExportQualityReport AnalyzeUniversalDirExportQuality(string manifestText, DirExportQualityOptions options)
{
    var manifest = ContextDirManifestParser.Parse(manifestText);
    var failures = new List<string>();
    var warnings = new List<string>();
    var fileCount = manifest.Files.Count;
    var rootCount = manifest.Roots.Count;
    var familyCount = manifest.Families.Count;
    var lineCount = manifestText.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n').Length;

    if (!manifest.IsV2)
    {
        failures.Add("Manifest is not CC-DIR-MANIFEST-V2.");
    }

    if (fileCount == 0)
    {
        failures.Add("Manifest has no FILE anchors.");
    }

    var exampleFailures = ValidateManifestExamples(manifestText, manifest);
    failures.AddRange(exampleFailures);

    var categories = manifest.Files
        .Select(file => (File: file, Category: ClassifyDirExportFile(file)))
        .ToArray();
    var buildCount = categories.Count(item => item.Category.Equals("build-config", StringComparison.Ordinal));
    var representativeCount = categories.Count(item => IsRepresentativeDirExportCategory(item.Category));
    var nonDocFiles = categories
        .Where(item => !item.Category.Equals("docs", StringComparison.Ordinal))
        .ToArray();
    var coveredFiles = 0;
    var unrootedBuildConfigCount = 0;
    var repoRootFiles = nonDocFiles
        .Where(item => string.IsNullOrWhiteSpace(ParentDirectory(item.File.Path)))
        .ToArray();
    var repoRootCoveredCount = repoRootFiles.Count(item => IsRepoRootCoverageCategory(item.Category));

    foreach (var item in nonDocFiles)
    {
        if (IsDirExportFileRootCovered(item.File, item.Category, manifest.Roots))
        {
            coveredFiles++;
            continue;
        }

        var suggestedRoot = SuggestedManifestRootForFile(item.File);
        if (item.Category.Equals("build-config", StringComparison.Ordinal))
        {
            unrootedBuildConfigCount++;
            var message = $"Build/config FILE lacks close expandable ROOT: {item.File.Path}; drop candidate or add useful source-owned ROOT {suggestedRoot}.";
            if (options.BuildFocused)
            {
                warnings.Add(message);
                coveredFiles++;
            }
            else
            {
                failures.Add(message);
            }

            continue;
        }

        failures.Add($"FILE anchor lacks close expandable ROOT: {item.File.Path}; suggested ROOT {suggestedRoot}.");
    }

    var coveredFamilies = manifest.Families.Count(family => IsDirExportFamilyRootCovered(family, manifest.Roots));
    foreach (var family in manifest.Families)
    {
        if (!IsDirExportFamilyRootCovered(family, manifest.Roots))
        {
            failures.Add($"FAMILY lacks close parent ROOT: {family.Path}; suggested ROOT {SuggestedManifestRootForFamily(family)}.");
        }
    }

    var relevantCount = options.RelevantFileCount > 0 ? options.RelevantFileCount : Math.Max(fileCount, manifest.Roots.Count == 0 ? 0 : manifest.Roots.Max(root => root.Files));
    var repoClass = relevantCount <= 30
        ? "TinyRepo"
        : relevantCount > 500
            ? "LargeRepo"
            : "NormalRepo";
    var buildMaxRatio = repoClass.Equals("LargeRepo", StringComparison.Ordinal) ? 0.25 : 0.35;
    var buildMax = Math.Max(1, (int)Math.Floor(fileCount * buildMaxRatio));
    var buildPreferredMax = Math.Max(1, (int)Math.Floor(fileCount * 0.20));
    if (fileCount > 0 && buildCount > buildMax)
    {
        failures.Add($"Build/config share exceeds cap: {buildCount}/{fileCount}, cap {buildMax}.");
    }
    else if (fileCount > 0 && repoClass.Equals("LargeRepo", StringComparison.Ordinal) && buildCount > buildPreferredMax)
    {
        warnings.Add($"Build/config share is above preferred large-repo target: {buildCount}/{fileCount}, preferred {buildPreferredMax}, hard cap {buildMax}.");
    }

    var firstBuild = Array.FindIndex(categories, item => item.Category.Equals("build-config", StringComparison.Ordinal));
    var firstRepresentative = Array.FindIndex(categories, item => IsRepresentativeDirExportCategory(item.Category));
    if (firstBuild >= 0 && (firstRepresentative < 0 || firstBuild < firstRepresentative))
    {
        failures.Add("Build/config anchor appears before the first representative source/runtime/control-plane anchor.");
    }

    var sourceShareMin = (int)Math.Ceiling(fileCount * 0.60);
    if (!options.AllowDocsHeavy && !options.AllowToolsHeavy && !repoClass.Equals("TinyRepo", StringComparison.Ordinal) && representativeCount < sourceShareMin)
    {
        failures.Add($"Representative source/control-plane share is too low: {representativeCount}/{fileCount}, minimum {sourceShareMin}.");
    }

    var stableRootShares = manifest.Files
        .Select(file => StableManifestAnchorRoot(file.Path, file.Kind))
        .Where(root => !string.IsNullOrWhiteSpace(root))
        .GroupBy(root => root, StringComparer.OrdinalIgnoreCase)
        .ToArray();
    var representativeStableRoots = categories
        .Where(item => IsRepresentativeDirExportCategory(item.Category))
        .Select(item => StableManifestAnchorRoot(item.File.Path, item.File.Kind))
        .Where(root => !string.IsNullOrWhiteSpace(root))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Count();
    var largestRootShare = stableRootShares.Length == 0 ? 0 : stableRootShares.Max(group => group.Count());
    var largestRootName = stableRootShares.Length == 0
        ? ""
        : stableRootShares.OrderByDescending(group => group.Count()).First().Key;
    var singleRootRepo = relevantCount > 0 && largestRootShare / (double)Math.Max(1, fileCount) >= 0.80
        || representativeStableRoots <= 3;
    var maxRootShare = Math.Max(2, (int)Math.Floor(fileCount * 0.30));
    if (!repoClass.Equals("TinyRepo", StringComparison.Ordinal) && !singleRootRepo && largestRootShare > maxRootShare)
    {
        failures.Add($"Stable root FILE share exceeds cap: {largestRootName} has {largestRootShare}/{fileCount}, cap {maxRootShare}.");
    }

    var genericRoleCount = manifest.Files.Count(file => IsGenericRole(file.Role));
    var genericRoleMax = Math.Max(1, (int)Math.Floor(fileCount * 0.20));
    if (genericRoleCount > genericRoleMax)
    {
        var message = $"Generic roles exceed diagnostic cap: {genericRoleCount}/{fileCount}, cap {genericRoleMax}.";
        if (options.StrictRoleSpecificity)
        {
            failures.Add(message);
        }
        else
        {
            warnings.Add(message);
        }
    }

    if (lineCount > options.MaxL0Lines)
    {
        failures.Add($"L0 line budget exceeded: {lineCount}/{options.MaxL0Lines}.");
    }

    if (options.ExpectAsset && !manifest.Files.Any(file => file.Kind.Equals("shader", StringComparison.OrdinalIgnoreCase)))
    {
        failures.Add("Expected at least one shader/code-like asset anchor.");
    }

    return new DirExportQualityReport
    {
        Name = options.Name,
        Manifest = manifest,
        Failures = failures,
        Warnings = warnings,
        LineCount = lineCount,
        MaxL0Lines = options.MaxL0Lines,
        FileCount = fileCount,
        RootCount = rootCount,
        FamilyCount = familyCount,
        NonDocFileCount = nonDocFiles.Length,
        CoveredFileCount = coveredFiles,
        CoveredFamilyCount = coveredFamilies,
        BuildCount = buildCount,
        BuildMax = buildMax,
        BuildPreferredMax = buildPreferredMax,
        UnrootedBuildConfigCount = unrootedBuildConfigCount,
        RepoRootFileCount = repoRootFiles.Length,
        RepoRootCoveredCount = repoRootCoveredCount,
        RepresentativeCount = representativeCount,
        RepresentativeMin = sourceShareMin,
        LargestRootShare = largestRootShare,
        LargestRootName = largestRootName,
        MaxRootShare = maxRootShare,
        GenericRoleCount = genericRoleCount,
        GenericRoleMax = genericRoleMax,
        RepoClass = repoClass
    };
}

internal static void RequireUniversalDirExportQuality(DirExportQualityReport report)
{
    if (report.Failures.Count == 0)
    {
        return;
    }

    throw new InvalidOperationException(
        $"""
        DIR QUALITY {report.Name}
        Validity: {(report.Failures.Any(item => item.Contains("Manifest", StringComparison.OrdinalIgnoreCase) || item.Contains("example", StringComparison.OrdinalIgnoreCase)) ? "FAIL" : "PASS")}
        Anchor expandability: {report.CoveredFileCount}/{report.NonDocFileCount} {(report.CoveredFileCount == report.NonDocFileCount ? "PASS" : "FAIL")}
        Repo-root coverage: {report.RepoRootCoveredCount}/{report.RepoRootFileCount} {(report.RepoRootCoveredCount == report.RepoRootFileCount ? "PASS" : "FAIL")}
        Family parent roots: {report.CoveredFamilyCount}/{report.FamilyCount} {(report.CoveredFamilyCount == report.FamilyCount ? "PASS" : "FAIL")}
        Build/config share: {report.BuildCount}/{report.FileCount} preferred={report.BuildPreferredMax} cap={report.BuildMax} {(report.BuildCount <= report.BuildMax ? "PASS" : "FAIL")}
        Unrooted build/config FILES: {report.UnrootedBuildConfigCount}
        Source/control-plane share: {report.RepresentativeCount}/{report.FileCount} min={report.RepresentativeMin}
        Max stable-root FILE share: {report.LargestRootName} {report.LargestRootShare}/{report.FileCount} cap={report.MaxRootShare}
        Generic roles: {report.GenericRoleCount}/{report.FileCount} cap={report.GenericRoleMax}
        Line budget: {report.LineCount}/{report.MaxL0Lines} {(report.LineCount <= report.MaxL0Lines ? "PASS" : "FAIL")}
        Repo class: {report.RepoClass}
        Failures:
        {string.Join(Environment.NewLine, report.Failures.Select(item => "- " + item))}
        Warnings:
        {string.Join(Environment.NewLine, report.Warnings.Select(item => "- " + item))}
        """);
}

internal static IReadOnlyList<string> ValidateManifestExamples(string manifestText, ContextDirManifest manifest)
{
    var failures = new List<string>();
    var lines = manifestText.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
    var inExamples = false;
    foreach (var rawLine in lines)
    {
        var line = rawLine.Trim();
        if (line.Equals("VALID_OUTPUT_EXAMPLES:", StringComparison.OrdinalIgnoreCase))
        {
            inExamples = true;
            continue;
        }

        if (line.Equals("FINAL_CHECK:", StringComparison.OrdinalIgnoreCase))
        {
            break;
        }

        if (!inExamples || line.Length == 0 || line.Equals("END", StringComparison.OrdinalIgnoreCase))
        {
            continue;
        }

        if (line.StartsWith("EXPAND:", StringComparison.OrdinalIgnoreCase))
        {
            var path = NormalizeManifestPath(line["EXPAND:".Length..]);
            if (!ContextDirManifestParser.HasRootOrScope(manifest, path))
            {
                failures.Add($"EXPAND example does not match visible ROOT: {path}");
            }

            continue;
        }

        if (line.StartsWith("FIND:", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(line["FIND:".Length..]))
            {
                failures.Add("FIND example is empty.");
            }

            continue;
        }

        if (line.StartsWith("FUNCTION ", StringComparison.OrdinalIgnoreCase))
        {
            var body = line["FUNCTION ".Length..];
            var separator = body.IndexOf("::", StringComparison.Ordinal);
            var functionPath = separator >= 0
                ? NormalizeManifestPath(body[..separator])
                : "";
            if (functionPath.Contains('*', StringComparison.Ordinal))
            {
                if (!ContextDirManifestParser.HasFamily(manifest, functionPath))
                {
                    failures.Add($"FUNCTION example wildcard does not match visible FAMILY: {functionPath}");
                }
            }
            else if (!ContextDirManifestParser.HasFile(manifest, functionPath))
            {
                failures.Add($"FUNCTION example path does not match visible FILE: {functionPath}");
            }

            continue;
        }

        if (!line.StartsWith("FUNC:", StringComparison.OrdinalIgnoreCase) && !ContextDirManifestParser.HasFile(manifest, line))
        {
            failures.Add($"Source example path does not match visible FILE: {line}");
        }
    }

    return failures;
}
}
