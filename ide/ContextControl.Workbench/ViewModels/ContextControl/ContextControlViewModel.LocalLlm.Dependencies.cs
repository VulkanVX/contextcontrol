// CC-DESC: Extracted ContextControlViewModel system slice.
// CC-DESC: Owns Context Control workflow state, prompt bar state, and DIR/CC/GO commands.

using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows.Input;
using Avalonia.Collections;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using ContextControl.Workbench.Services;

namespace ContextControl.Workbench.ViewModels;

public sealed partial class ContextControlViewModel
{
    private static readonly Encoding DependencyProcessUtf8Encoding = new UTF8Encoding(false, false);

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

    private static IReadOnlyList<string> FindExecutableCandidatesOnPath(IReadOnlyList<string> fileNames)
    {
        var candidates = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return candidates;
        }

        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            foreach (var fileName in fileNames)
            {
                try
                {
                    var candidate = Path.GetFullPath(Path.Combine(directory, fileName));
                    if (File.Exists(candidate) && seen.Add(candidate))
                    {
                        candidates.Add(candidate);
                    }
                }
                catch
                {
                    // Ignore broken PATH entries.
                }
            }
        }

        return candidates;
    }

    private static async Task<DependencyProcessResult> RunProcessForDependencyInstallAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        IProgress<string> terminal,
        IProgress<LocalLlmTransferProgress> progress,
        string operation,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? environmentVariables = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = DependencyProcessUtf8Encoding,
            StandardErrorEncoding = DependencyProcessUtf8Encoding,
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
                progress.Report(new LocalLlmTransferProgress(operation, "Process could not be started.", null, null, null, null));
                return new DependencyProcessResult(false, -1, "", "");
            }
        }
        catch (Exception ex)
        {
            progress.Report(new LocalLlmTransferProgress(operation, ex.Message, null, null, null, null));
            return new DependencyProcessResult(false, -1, "", ex.Message);
        }

        progress.Report(new LocalLlmTransferProgress(operation, $"Started {Path.GetFileName(fileName)}.", null, null, null, null));
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        var stdoutTask = ReadDependencyProcessStreamAsync(process.StandardOutput, stdout, terminal, progress, operation);
        var stderrTask = ReadDependencyProcessStreamAsync(process.StandardError, stderr, terminal, progress, operation);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            var timedOut = !cancellationToken.IsCancellationRequested;
            TryKillDependencyProcess(process);
            await Task.WhenAll(SafeDependencyWaitAsync(stdoutTask), SafeDependencyWaitAsync(stderrTask));
            progress.Report(new LocalLlmTransferProgress(
                operation,
                timedOut ? "Timed out; process was stopped." : "Stopped by user.",
                null,
                null,
                null,
                null));
            return new DependencyProcessResult(true, -1, stdout.ToString(), stderr.ToString(), timedOut);
        }

        await Task.WhenAll(SafeDependencyWaitAsync(stdoutTask), SafeDependencyWaitAsync(stderrTask));
        progress.Report(new LocalLlmTransferProgress(operation, $"Exited with code {process.ExitCode}.", null, null, null, process.ExitCode == 0 ? 100 : null));
        return new DependencyProcessResult(true, process.ExitCode, stdout.ToString(), stderr.ToString());
    }

    private static async Task ReadDependencyProcessStreamAsync(
        StreamReader reader,
        StringBuilder sink,
        IProgress<string> terminal,
        IProgress<LocalLlmTransferProgress> progress,
        string operation)
    {
        while (await reader.ReadLineAsync() is { } line)
        {
            sink.AppendLine(line);
            if (!string.IsNullOrWhiteSpace(line))
            {
                terminal.Report(line);
                progress.Report(new LocalLlmTransferProgress(operation, line, null, null, null, null));
            }
        }
    }

    private static async Task SafeDependencyWaitAsync(Task task)
    {
        try
        {
            await task;
        }
        catch
        {
            // Dependency installer logs are best-effort.
        }
    }

    private static void TryKillDependencyProcess(Process process)
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
            // Cleanup is best-effort.
        }
    }

    private static string? FirstDependencyInstallLine(DependencyProcessResult result)
    {
        if (result.TimedOut)
        {
            return "timed out; process was stopped";
        }

        return InterestingLines(result.StandardError).FirstOrDefault()
            ?? InterestingLines(result.StandardOutput).FirstOrDefault();
    }

    private async Task InstallOllamaAsync()
    {
        var dependency = LlmBackendDependencies.FirstOrDefault(item => item.Id.Equals("ollama", StringComparison.OrdinalIgnoreCase));
        if (dependency is null)
        {
            PhaseTitle = "Ollama install unavailable";
            PhaseDetail = "Ollama dependency metadata was not found.";
            Log("warn", PhaseDetail);
            return;
        }

        PhaseTitle = "Install Ollama";
        PhaseDetail = "Installing Ollama through the OS package manager.";
        IsInstallingOllama = true;
        var installCancellation = new CancellationTokenSource();
        var progress = CreateTransferProgress("Installing Ollama", installCancellation);
        progress.Report(new LocalLlmTransferProgress(
            "Installing Ollama",
            "Using the OS package manager; this may show an installer prompt outside ContextControl.",
            null,
            null,
            null,
            null));

        try
        {
            var result = await InstallBackendDependencyAutomaticallyAsync(dependency, progress, installCancellation.Token);
            PhaseTitle = result.Succeeded ? "Ollama installed" : "Ollama install needs attention";
            PhaseDetail = result.Status;
            CompleteDependencyTransferProgress(result.Status, result.Succeeded);
            Log(result.Succeeded ? "ok" : "warn", result.Status);
            if (result.Succeeded)
            {
                await RefreshLocalModelsAsync(LocalModelRefreshDepth.Fast);
            }
        }
        catch (OperationCanceledException)
        {
            PhaseTitle = "Ollama install canceled";
            PhaseDetail = "Ollama install was stopped.";
            CompleteDependencyTransferProgress("Ollama install canceled.", succeeded: false);
            Log("warn", "Ollama install canceled.");
        }
        catch (Exception ex)
        {
            PhaseTitle = "Ollama install failed";
            PhaseDetail = ex.Message;
            CompleteDependencyTransferProgress(ex.Message, succeeded: false);
            Log("error", ex.Message);
        }
        finally
        {
            IsInstallingOllama = false;
            installCancellation.Dispose();
        }
    }

    private static void ApplyReadableProcessEnvironment(ProcessStartInfo startInfo)
    {
        startInfo.Environment["PYTHONUTF8"] = "1";
        startInfo.Environment["PYTHONIOENCODING"] = "utf-8";
        startInfo.Environment["DOTNET_SYSTEM_CONSOLE_ALLOW_ANSI_COLOR_REDIRECTION"] = "1";
    }

    private void OpenOllamaDownloadPage()
    {
        var result = _localLlmService.OpenOllamaDownloadPage();
        LocalLlmStatus = result.Status;
        Log(result.Succeeded ? "info" : "warn", result.Status);
    }

    private async Task InstallBackendDependencyAsync(LlmBackendDependencyViewModel? dependency)
    {
        if (dependency is null)
        {
            return;
        }

        if (dependency.Id.Equals("ollama", StringComparison.OrdinalIgnoreCase))
        {
            await InstallOllamaAsync();
            return;
        }

        PhaseTitle = $"Install {dependency.DisplayName}";
        PhaseDetail = dependency.InstallHint;
        AppendTerminalOutput($"{dependency.DisplayName}: {dependency.InstallHint}");
        var installCancellation = new CancellationTokenSource();
        var progress = CreateTransferProgress($"Installing {dependency.DisplayName}", installCancellation);
        progress.Report(new LocalLlmTransferProgress(
            $"Installing {dependency.DisplayName}",
            dependency.InstallHint,
            null,
            null,
            null,
            null));

        try
        {
            var result = await InstallBackendDependencyAutomaticallyAsync(dependency, progress, installCancellation.Token);
            PhaseTitle = result.Succeeded ? $"{dependency.DisplayName} installed" : $"{dependency.DisplayName} install needs attention";
            PhaseDetail = result.Status;
            CompleteDependencyTransferProgress(result.Status, result.Succeeded);
            Log(result.Succeeded ? "ok" : "warn", result.Status);
            if (result.Succeeded)
            {
                dependency.ApplyStatus(true, "Ready", result.Status, result.IsManaged);
                OnPropertyChanged(nameof(DependencySummary));
                OnPropertyChanged(nameof(LlmCompactInfoLabel));
                ApplyDependencyFilters();
                ApplyBackendDependencyStatesToModels();
                return;
            }

            AppendTerminalOutput($"{dependency.DisplayName}: no browser fallback was opened. Only ContextControl-local installers run from this button.");
        }
        catch (OperationCanceledException)
        {
            PhaseTitle = "Dependency install canceled";
            PhaseDetail = $"{dependency.DisplayName} install was stopped.";
            CompleteDependencyTransferProgress($"{dependency.DisplayName} install canceled.", succeeded: false);
            Log("warn", $"{dependency.DisplayName} install canceled.");
        }
        catch (Exception ex)
        {
            PhaseTitle = "Dependency install failed";
            PhaseDetail = ex.Message;
            CompleteDependencyTransferProgress(ex.Message, succeeded: false);
            Log("error", ex.Message);
        }
        finally
        {
            installCancellation.Dispose();
        }
    }

    private async Task UninstallBackendDependencyAsync(LlmBackendDependencyViewModel? dependency)
    {
        if (dependency is null)
        {
            return;
        }

        if (dependency.CanForceInstall)
        {
            OpenExternalDependencyDeletePrompt(dependency);
            return;
        }

        if (!dependency.CanUninstall)
        {
            PhaseTitle = "Dependency uninstall unavailable";
            PhaseDetail = $"{dependency.DisplayName} was detected outside ContextControl's managed dependency folders.";
            Log("warn", PhaseDetail);
            return;
        }

        PhaseTitle = $"Uninstall {dependency.DisplayName}";
        PhaseDetail = "Removing ContextControl-managed dependency files.";
        var uninstallCancellation = new CancellationTokenSource();
        var progress = CreateTransferProgress($"Uninstalling {dependency.DisplayName}", uninstallCancellation);
        progress.Report(new LocalLlmTransferProgress(
            $"Uninstalling {dependency.DisplayName}",
            "Removing ContextControl-managed dependency files.",
            null,
            null,
            null,
            null));

        try
        {
            var result = await UninstallManagedBackendDependencyAsync(dependency, progress, uninstallCancellation.Token);
            PhaseTitle = result.Succeeded ? $"{dependency.DisplayName} uninstalled" : $"{dependency.DisplayName} uninstall failed";
            PhaseDetail = result.Status;
            CompleteDependencyTransferProgress(result.Status, result.Succeeded);
            Log(result.Succeeded ? "ok" : "warn", result.Status);
            if (result.Succeeded)
            {
                dependency.ApplyStatus(false, "Not detected", result.Status);
                OnPropertyChanged(nameof(DependencySummary));
                OnPropertyChanged(nameof(LlmCompactInfoLabel));
                ApplyDependencyFilters();
                ApplyBackendDependencyStatesToModels();
            }
        }
        catch (OperationCanceledException)
        {
            PhaseTitle = "Dependency uninstall canceled";
            PhaseDetail = $"{dependency.DisplayName} uninstall was stopped.";
            CompleteDependencyTransferProgress($"{dependency.DisplayName} uninstall canceled.", succeeded: false);
            Log("warn", $"{dependency.DisplayName} uninstall canceled.");
        }
        catch (Exception ex)
        {
            PhaseTitle = "Dependency uninstall failed";
            PhaseDetail = ex.Message;
            CompleteDependencyTransferProgress(ex.Message, succeeded: false);
            Log("error", ex.Message);
        }
        finally
        {
            uninstallCancellation.Dispose();
        }
    }

    private void OpenExternalDependencyDeletePrompt(LlmBackendDependencyViewModel dependency)
    {
        _pendingExternalDependencyDelete = dependency;
        OnPropertyChanged(nameof(ExternalDependencyDeletePromptTitle));
        OnPropertyChanged(nameof(ExternalDependencyDeletePromptMessage));
        IsExternalDependencyDeletePromptOpen = true;
        RaiseCommandStates();
    }

    private void CloseExternalDependencyDeletePrompt()
    {
        IsExternalDependencyDeletePromptOpen = false;
        _pendingExternalDependencyDelete = null;
        OnPropertyChanged(nameof(ExternalDependencyDeletePromptTitle));
        RaiseCommandStates();
    }

    private async Task ConfirmExternalDependencyDeleteAsync()
    {
        var dependency = _pendingExternalDependencyDelete;
        if (dependency is null)
        {
            CloseExternalDependencyDeletePrompt();
            return;
        }

        CloseExternalDependencyDeletePrompt();
        await ForceInstallExternalBackendDependencyAsync(dependency);
    }

    private async Task ForceInstallExternalBackendDependencyAsync(LlmBackendDependencyViewModel dependency)
    {
        PhaseTitle = $"Force install {dependency.DisplayName}";
        PhaseDetail = "Removing external packages before installing a managed ContextControl environment.";
        var deleteCancellation = new CancellationTokenSource();
        var progress = CreateTransferProgress($"Force installing {dependency.DisplayName}", deleteCancellation);
        progress.Report(new LocalLlmTransferProgress(
            $"Force installing {dependency.DisplayName}",
            "Removing external packages before installing a managed ContextControl environment.",
            null,
            null,
            null,
            null));

        try
        {
            var deleteResult = await DeleteExternalPythonDependencyAsync(dependency, progress, deleteCancellation.Token);
            if (!deleteResult.Succeeded)
            {
                PhaseTitle = $"{dependency.DisplayName} delete failed";
                PhaseDetail = deleteResult.Status;
                CompleteDependencyTransferProgress(deleteResult.Status, succeeded: false);
                Log("warn", deleteResult.Status);
                return;
            }

            dependency.ApplyStatus(false, "Not detected", deleteResult.Status);
            progress.Report(new LocalLlmTransferProgress(
                $"Force installing {dependency.DisplayName}",
                "External packages removed. Installing into ContextControl's managed dependency folder.",
                null,
                null,
                null,
                null));

            var installResult = await InstallBackendDependencyAutomaticallyAsync(
                dependency,
                progress,
                deleteCancellation.Token,
                forceManagedPython: true);
            PhaseTitle = installResult.Succeeded ? $"{dependency.DisplayName} installed" : $"{dependency.DisplayName} install needs attention";
            PhaseDetail = installResult.Succeeded
                ? installResult.Status
                : $"{deleteResult.Status} Managed install failed: {installResult.Status}";
            CompleteDependencyTransferProgress(PhaseDetail, installResult.Succeeded);
            Log(installResult.Succeeded ? "ok" : "warn", PhaseDetail);
            dependency.ApplyStatus(
                installResult.Succeeded,
                installResult.Succeeded ? "Ready" : "Not detected",
                PhaseDetail,
                installResult.IsManaged);
            OnPropertyChanged(nameof(DependencySummary));
            OnPropertyChanged(nameof(LlmCompactInfoLabel));
            ApplyDependencyFilters();
            ApplyBackendDependencyStatesToModels();
        }
        catch (OperationCanceledException)
        {
            PhaseTitle = "Dependency force install canceled";
            PhaseDetail = $"{dependency.DisplayName} force install was stopped.";
            CompleteDependencyTransferProgress($"{dependency.DisplayName} force install canceled.", succeeded: false);
            Log("warn", $"{dependency.DisplayName} force install canceled.");
        }
        catch (Exception ex)
        {
            PhaseTitle = "Dependency force install failed";
            PhaseDetail = ex.Message;
            CompleteDependencyTransferProgress(ex.Message, succeeded: false);
            Log("error", ex.Message);
        }
        finally
        {
            deleteCancellation.Dispose();
        }
    }

    private async Task<DependencyInstallResult> DeleteExternalPythonDependencyAsync(
        LlmBackendDependencyViewModel dependency,
        IProgress<LocalLlmTransferProgress> progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!PythonDependencyEnvironment.TryGetSpec(dependency.Id, out var spec))
        {
            return new DependencyInstallResult(false, $"{dependency.DisplayName} has no external Python delete path configured.");
        }

        var status = await ResolveExternalPythonDependencyAsync(spec, cancellationToken).ConfigureAwait(false);
        if (!status.IsReady || string.IsNullOrWhiteSpace(status.Executable))
        {
            PythonDependencyEnvironment.ClearExternalReady(spec);
            return new DependencyInstallResult(false, $"{spec.DisplayName} external environment could not be validated: {status.Detail}");
        }

        if (status.IsManaged)
        {
            return new DependencyInstallResult(false, $"{spec.DisplayName} now resolves to ContextControl's managed venv. Use Uninstall instead.");
        }

        var terminal = CreateTerminalProgress();
        var pipArgs = new List<string> { "-m", "pip", "uninstall", "-y" };
        pipArgs.AddRange(spec.Packages);
        terminal.Report($"> {status.Executable} {string.Join(' ', pipArgs)}");
        progress.Report(new LocalLlmTransferProgress(
            $"Deleting {spec.DisplayName}",
            $"Running pip uninstall in external Python: {status.Executable}",
            null,
            null,
            null,
            null));
        var deleteResult = await RunProcessForDependencyInstallAsync(
            status.Executable,
            pipArgs,
            TimeSpan.FromMinutes(10),
            terminal,
            progress,
            $"Deleting {spec.DisplayName}",
            cancellationToken).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();
        if (deleteResult.ExitCode != 0)
        {
            var failure = FirstDependencyInstallLine(deleteResult) ?? $"{spec.DisplayName} pip uninstall exited {deleteResult.ExitCode}.";
            return new DependencyInstallResult(false, $"{spec.DisplayName} external delete failed: {failure}");
        }

        PythonDependencyEnvironment.ClearExternalEnvironmentVariables(spec.Id, status.Executable);
        PythonDependencyEnvironment.ClearExternalReady(spec);
        return new DependencyInstallResult(true, $"{spec.DisplayName} packages removed from external Python: {status.Executable}.");
    }

    private static async Task<DependencyRuntimeStatus> ResolveExternalPythonDependencyAsync(
        PythonDependencySpec spec,
        CancellationToken cancellationToken)
    {
        var remembered = PythonDependencyEnvironment.ReadRememberedExternalPython(spec);
        if (!string.IsNullOrWhiteSpace(remembered))
        {
            var rememberedStatus = await ProbePythonModulesAsync(
                spec.Id,
                spec.DisplayName,
                new PythonEnvironmentCandidate(remembered, IsManaged: false, SourceLabel: "ContextControl remembered")
                {
                    IsRememberedExternal = true
                },
                spec.PackagesToModules,
                importModules: false,
                cancellationToken).ConfigureAwait(false);
            if (rememberedStatus.IsReady)
            {
                return rememberedStatus;
            }
        }

        var status = await DetectPythonModulesAsync(
            spec.Id,
            spec.DisplayName,
            spec.PackagesToModules,
            includeExternalCandidates: true,
            importModules: false,
            cancellationToken).ConfigureAwait(false);
        return status.IsReady
            ? status
            : new DependencyRuntimeStatus(false, status.Detail);
    }

    private async Task<DependencyInstallResult> UninstallManagedBackendDependencyAsync(
        LlmBackendDependencyViewModel dependency,
        IProgress<LocalLlmTransferProgress> progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (PythonDependencyEnvironment.TryGetSpec(dependency.Id, out var pythonSpec))
        {
            var directory = PythonDependencyEnvironment.ManagedDependencyDirectory(pythonSpec.Id);
            progress.Report(new LocalLlmTransferProgress(
                $"Uninstalling {pythonSpec.DisplayName}",
                $"Removing {directory}",
                null,
                null,
                null,
                null));
            var removedPath = await Task.Run(() =>
            {
                PythonDependencyEnvironment.ClearManagedEnvironmentVariables(pythonSpec.Id);
                return PythonDependencyEnvironment.RemoveManagedDependency(pythonSpec.Id);
            }, cancellationToken).ConfigureAwait(false);
            return new DependencyInstallResult(true, $"{pythonSpec.DisplayName} managed environment removed: {removedPath}.");
        }

        if (NativeDependencyEnvironment.TryGetSpec(dependency.Id, out var nativeSpec))
        {
            var directory = NativeDependencyEnvironment.ManagedDependencyDirectory(nativeSpec.Id);
            progress.Report(new LocalLlmTransferProgress(
                $"Uninstalling {nativeSpec.DisplayName}",
                $"Removing {directory}",
                null,
                null,
                null,
                null));
            var removedPath = await Task.Run(() => NativeDependencyEnvironment.RemoveManagedDependency(nativeSpec.Id), cancellationToken).ConfigureAwait(false);
            return new DependencyInstallResult(true, $"{nativeSpec.DisplayName} managed files removed: {removedPath}.");
        }

        if (SourceDependencyEnvironment.TryGetSpec(dependency.Id, out var sourceSpec))
        {
            var directory = SourceDependencyEnvironment.ManagedDependencyDirectory(sourceSpec.Id);
            progress.Report(new LocalLlmTransferProgress(
                $"Uninstalling {sourceSpec.DisplayName}",
                $"Removing {directory}",
                null,
                null,
                null,
                null));
            var removedPath = await Task.Run(() => SourceDependencyEnvironment.RemoveManagedDependency(sourceSpec.Id), cancellationToken).ConfigureAwait(false);
            return new DependencyInstallResult(true, $"{sourceSpec.DisplayName} managed source removed: {removedPath}.");
        }

        return new DependencyInstallResult(false, $"{dependency.DisplayName} has no ContextControl-managed files to remove.");
    }

    private async Task<DependencyInstallResult> InstallBackendDependencyAutomaticallyAsync(
        LlmBackendDependencyViewModel dependency,
        IProgress<LocalLlmTransferProgress> progress,
        CancellationToken cancellationToken,
        bool forceManagedPython = false)
    {
        var terminal = CreateTerminalProgress();
        if (PythonDependencyEnvironment.TryGetSpec(dependency.Id, out var pythonSpec))
        {
            return await InstallPythonPackagesAsync(pythonSpec, terminal, progress, cancellationToken, forceManagedPython);
        }

        if (NativeDependencyEnvironment.TryGetSpec(dependency.Id, out var nativeSpec))
        {
            return await InstallNativeDependencyAsync(nativeSpec, terminal, progress, cancellationToken);
        }

        if (SourceDependencyEnvironment.TryGetSpec(dependency.Id, out var sourceSpec))
        {
            return await InstallSourceDependencyAsync(sourceSpec, terminal, progress, cancellationToken);
        }

        if (PackageManagerDependencyEnvironment.TryGetSpec(dependency.Id, out var packageManagerSpec))
        {
            return await InstallPackageManagerDependencyAsync(packageManagerSpec, terminal, progress, cancellationToken);
        }

        return new DependencyInstallResult(false, $"{dependency.DisplayName} has no ContextControl-local installer configured yet.");
    }

    private static async Task<DependencyInstallResult> InstallSourceDependencyAsync(
        SourceDependencySpec spec,
        IProgress<string> terminal,
        IProgress<LocalLlmTransferProgress> progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = await SourceDependencyEnvironment
            .InstallLatestSourceArchiveAsync(spec, terminal, progress, cancellationToken)
            .ConfigureAwait(false);
        return result.Succeeded
            ? new DependencyInstallResult(true, $"{result.Status} No model weights were downloaded.", IsManaged: true)
            : new DependencyInstallResult(false, result.Status);
    }

    private async Task<DependencyInstallResult> InstallPackageManagerDependencyAsync(
        PackageManagerDependencySpec spec,
        IProgress<string> terminal,
        IProgress<LocalLlmTransferProgress> progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var existingExecutable = FindExecutableCandidatesOnPath(spec.ExecutableNames).FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(existingExecutable))
        {
            return new DependencyInstallResult(
                true,
                $"{spec.DisplayName} already exists at {existingExecutable}. ContextControl did not modify the user install.");
        }

        if (!PackageManagerDependencyEnvironment.TryResolveInstallCommand(spec, out var command))
        {
            return new DependencyInstallResult(false, $"{spec.DisplayName} has no package-manager installer for this OS yet.");
        }

        terminal.Report($"> {command.FileName} {string.Join(' ', command.Arguments)}");
        progress.Report(new LocalLlmTransferProgress(
            $"Installing {spec.DisplayName}",
            $"Running {command.FileName} package install.",
            null,
            null,
            null,
            null));
        var installResult = await RunProcessForDependencyInstallAsync(
            command.FileName,
            command.Arguments,
            command.Timeout,
            terminal,
            progress,
            $"Installing {spec.DisplayName}",
            cancellationToken).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();
        if (installResult.ExitCode != 0)
        {
            var failure = FirstDependencyInstallLine(installResult) ?? $"{spec.DisplayName} package install exited {installResult.ExitCode}.";
            return new DependencyInstallResult(false, $"{spec.DisplayName} package install failed: {failure}");
        }

        return new DependencyInstallResult(
            true,
            $"{spec.DisplayName} package installer completed. Refresh if the executable is not visible until the next shell/session.");
    }

    private async Task<DependencyInstallResult> InstallNativeDependencyAsync(
        NativeDependencySpec spec,
        IProgress<string> terminal,
        IProgress<LocalLlmTransferProgress> progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var managedExecutable = NativeDependencyEnvironment.FindManagedExecutable(spec.Id, spec.ExecutableNames);
        if (!string.IsNullOrWhiteSpace(managedExecutable))
        {
            return new DependencyInstallResult(
                true,
                $"{spec.DisplayName} already validates in ContextControl's managed native store: {managedExecutable}. No user PATH or packages were modified.",
                IsManaged: true);
        }

        var externalExecutable = FindExecutableCandidatesOnPath(spec.ExecutableNames).FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(externalExecutable))
        {
            return new DependencyInstallResult(
                true,
                $"{spec.DisplayName} already exists at {externalExecutable}. ContextControl did not modify the user install.");
        }

        terminal.Report($"> managed native root: {NativeDependencyEnvironment.ManagedDependencyDirectory(spec.Id)}");
        var result = await NativeDependencyEnvironment
            .InstallLatestReleaseAsync(spec, terminal, progress, cancellationToken)
            .ConfigureAwait(false);
        return result.Succeeded
            ? new DependencyInstallResult(
                true,
                $"{result.Status} No user PATH, global packages, or existing installs were modified.",
                IsManaged: true)
            : new DependencyInstallResult(false, result.Status);
    }

    private async Task<DependencyInstallResult> InstallPythonPackagesAsync(
        PythonDependencySpec spec,
        IProgress<string> terminal,
        IProgress<LocalLlmTransferProgress> progress,
        CancellationToken cancellationToken,
        bool forceManaged = false)
    {
        var operation = $"Installing {spec.DisplayName}";
        var managedPython = PythonDependencyEnvironment.ManagedPythonExecutable(spec.Id);
        var managedDependencyDirectory = PythonDependencyEnvironment.ManagedDependencyDirectory(spec.Id);
        var managedDirectory = PythonDependencyEnvironment.ManagedEnvironmentDirectory(spec.Id);

        if (!forceManaged && File.Exists(managedPython))
        {
            var existing = await DetectPythonModulesAsync(
                spec.Id,
                spec.DisplayName,
                spec.PackagesToModules,
                includeExternalCandidates: false,
                importModules: true,
                cancellationToken).ConfigureAwait(false);
            if (existing.IsReady)
            {
                RememberPythonDependencyRuntime(spec, existing);
                return new DependencyInstallResult(
                    true,
                    $"{spec.DisplayName} already validates: {existing.Detail}; no user environment was modified.",
                    existing.IsManaged);
            }

            terminal.Report($"{spec.DisplayName} managed venv is not healthy: {existing.Detail}");
            terminal.Report($"Removing only ContextControl-managed dependency files: {managedDependencyDirectory}");
            progress.Report(new LocalLlmTransferProgress(
                operation,
                $"Repairing managed ContextControl venv at {managedDirectory}.",
                null,
                null,
                null,
                null));
            PythonDependencyEnvironment.ClearManagedEnvironmentVariables(spec.Id);
            PythonDependencyEnvironment.RemoveManagedDependency(spec.Id);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(managedDirectory) ?? PythonDependencyEnvironment.ManagedRoot);

        if (!File.Exists(managedPython))
        {
            var seedResult = await CreateManagedPythonEnvironmentAsync(spec, terminal, progress, cancellationToken).ConfigureAwait(false);
            if (!seedResult.Succeeded || !File.Exists(managedPython))
            {
                return seedResult;
            }
        }

        var pipArgs = new List<string> { "-m", "pip", "install", "--upgrade", "--upgrade-strategy", "eager" };
        pipArgs.AddRange(spec.InstallArguments);
        terminal.Report($"> {managedPython} {string.Join(' ', pipArgs)}");
        progress.Report(new LocalLlmTransferProgress(
            operation,
            $"Installing packages into managed ContextControl environment: {managedDirectory}",
            null,
            null,
            null,
            null));
        var installResult = await RunProcessForDependencyInstallAsync(
            managedPython,
            pipArgs,
            TimeSpan.FromMinutes(30),
            terminal,
            progress,
            operation,
            cancellationToken,
            PythonDependencyEnvironment.ManagedProcessEnvironment).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();
        if (installResult.ExitCode != 0)
        {
            var failure = FirstDependencyInstallLine(installResult) ?? $"{spec.DisplayName} pip install exited {installResult.ExitCode}.";
            return new DependencyInstallResult(false, $"{spec.DisplayName} managed install failed: {failure}");
        }

        var validation = await ProbePythonModulesAsync(
            spec.Id,
            spec.DisplayName,
            new PythonEnvironmentCandidate(managedPython, IsManaged: true, SourceLabel: "ContextControl managed"),
            spec.PackagesToModules,
            importModules: true,
            cancellationToken).ConfigureAwait(false);
        if (!validation.IsReady)
        {
            PythonDependencyEnvironment.ClearManagedReady(spec.Id);
            return new DependencyInstallResult(false, $"{spec.DisplayName} installed into managed venv, but validation failed: {validation.Detail}");
        }

        PythonDependencyEnvironment.MarkManagedReady(spec);
        RememberPythonDependencyRuntime(
            spec,
            new DependencyRuntimeStatus(
                true,
                $"{spec.DisplayName} ready in {managedPython} (managed ContextControl venv).",
                managedPython,
                IsManaged: true));

        return new DependencyInstallResult(true, $"{spec.DisplayName} installed and validated in managed ContextControl venv: {managedDirectory}.", IsManaged: true);
    }

    private async Task<DependencyInstallResult> CreateManagedPythonEnvironmentAsync(
        PythonDependencySpec spec,
        IProgress<string> terminal,
        IProgress<LocalLlmTransferProgress> progress,
        CancellationToken cancellationToken)
    {
        var operation = $"Creating {spec.DisplayName} managed venv";
        var managedDirectory = PythonDependencyEnvironment.ManagedEnvironmentDirectory(spec.Id);
        var bootstrapAttempted = false;
        var failures = new List<string>();

        while (true)
        {
            failures.Clear();
            var seedCandidates = PythonDependencyEnvironment.FindPythonSeedCandidates();
            if (seedCandidates.Count == 0)
            {
                if (bootstrapAttempted)
                {
                    return new DependencyInstallResult(false, $"Python was installed, but no usable python.exe was found for {spec.DisplayName}. Restart ContextControl and try again.");
                }

                var bootstrap = await InstallPythonSeedAsync(spec, terminal, progress, cancellationToken).ConfigureAwait(false);
                if (!bootstrap.Succeeded)
                {
                    return bootstrap;
                }

                bootstrapAttempted = true;
                continue;
            }

            foreach (var seedPython in seedCandidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var args = new[] { "-m", "venv", managedDirectory };
                terminal.Report($"> {seedPython} {string.Join(' ', args)}");
                progress.Report(new LocalLlmTransferProgress(operation, $"Creating managed venv at {managedDirectory}", null, null, null, null));
                var result = await RunProcessForDependencyInstallAsync(
                    seedPython,
                    args,
                    TimeSpan.FromMinutes(10),
                    terminal,
                    progress,
                    operation,
                    cancellationToken).ConfigureAwait(false);

                if (result.ExitCode == 0 && File.Exists(PythonDependencyEnvironment.ManagedPythonExecutable(spec.Id)))
                {
                    return new DependencyInstallResult(true, $"{spec.DisplayName} managed venv created at {managedDirectory}.", IsManaged: true);
                }

                failures.Add($"{seedPython}: {FirstDependencyInstallLine(result) ?? $"venv exited {result.ExitCode}"}");
            }

            if (bootstrapAttempted)
            {
                return new DependencyInstallResult(false, $"Could not create managed venv for {spec.DisplayName}. {string.Join(" | ", failures.Take(3))}");
            }

            var installPython = await InstallPythonSeedAsync(spec, terminal, progress, cancellationToken).ConfigureAwait(false);
            if (!installPython.Succeeded)
            {
                failures.Add(installPython.Status);
                return new DependencyInstallResult(false, $"Could not create managed venv for {spec.DisplayName}. {string.Join(" | ", failures.Take(3))}");
            }

            bootstrapAttempted = true;
        }
    }

    private async Task<DependencyInstallResult> InstallPythonSeedAsync(
        PythonDependencySpec spec,
        IProgress<string> terminal,
        IProgress<LocalLlmTransferProgress> progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!PackageManagerDependencyEnvironment.TryResolvePythonInstallCommand(out var command))
        {
            return new DependencyInstallResult(false, $"Python was not found. Install Python 3.12 first; ContextControl will then create its own venv for {spec.DisplayName}.");
        }

        const string operation = "Installing Python";
        terminal.Report("Python was not found, or only the Microsoft Store python alias was present.");
        terminal.Report($"> {command.FileName} {string.Join(' ', command.Arguments)}");
        progress.Report(new LocalLlmTransferProgress(
            operation,
            $"Installing Python 3.12 with {command.FileName} so ContextControl can create a managed venv.",
            null,
            null,
            null,
            null));

        var result = await RunProcessForDependencyInstallAsync(
            command.FileName,
            command.Arguments,
            command.Timeout,
            terminal,
            progress,
            operation,
            cancellationToken).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();
        if (result.ExitCode != 0)
        {
            var failure = FirstDependencyInstallLine(result) ?? $"Python installer exited {result.ExitCode}.";
            return new DependencyInstallResult(false, $"Automatic Python install failed: {failure}");
        }

        return new DependencyInstallResult(true, "Python installer completed. Creating ContextControl managed venv next.");
    }

}
