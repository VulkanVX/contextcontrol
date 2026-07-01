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
    private enum LocalModelRefreshDepth
    {
        Fast,
        Full
    }

    private Task RefreshLocalModelsAsync()
    {
        return RefreshLocalModelsAsync(LocalModelRefreshDepth.Full);
    }

    private async Task RefreshLocalModelsAsync(LocalModelRefreshDepth refreshDepth)
    {
        if (IsRefreshingLocalModels)
        {
            return;
        }

        var scanDependencies = refreshDepth == LocalModelRefreshDepth.Full;
        var refreshCancellation = new CancellationTokenSource();
        var progress = CreateTransferProgress("Refreshing Models", refreshCancellation, revealTerminal: false);
        var cancellationToken = refreshCancellation.Token;
        IsRefreshingLocalModels = true;
        LocalLlmStatus = "Detecting GPU and local Ollama models...";
        PhaseTitle = "Refreshing Models";
        PhaseDetail = scanDependencies
            ? "Detecting GPU, Ollama, installed tags, dependency runtimes, and local role delegation."
            : "Detecting GPU, Ollama, and installed tags.";
        ReportRefreshProgress(progress, PhaseDetail, 0);
        try
        {
            await Task.Yield();
            var refreshTask = _localLlmService.RefreshAsync(progress, cancellationToken);
            var dependencyStatusTask = scanDependencies
                ? DetectBackendDependencyStatusesAsync(cancellationToken, progress)
                : null;
            var result = await refreshTask;
            var dependencyStatuses = scanDependencies
                ? await AwaitDependencyStatusScanAsync(dependencyStatusTask!, progress)
                : DetectCachedBackendDependencyStatuses();
            cancellationToken.ThrowIfCancellationRequested();
            ReportRefreshProgress(progress, "Applying local role delegation.", 88);
            HardwareSummary = result.Hardware.Summary;
            LocalLlmStatus = result.Status;
            IsOllamaInstalled = result.OllamaInstalled;
            ApplyDetectedBackendDependencyStatuses(dependencyStatuses);
            ApplyLocalModelRefresh(result, preserveBackendModelStates: !scanDependencies);
            if (scanDependencies)
            {
                await ApplyBackendModelCacheStatesAsync(cancellationToken, progress);
            }

            ReportRefreshProgress(progress, "Refresh complete.", 100);
            CompleteTransferProgress($"Refresh complete: {result.Status}", succeeded: true);
            AppendTerminalOutput($"Hardware: {HardwareSummary}");
            AppendTerminalOutput($"Installed model IDs: {string.Join(", ", result.InstalledModelIds.OrderBy(id => id, StringComparer.OrdinalIgnoreCase))}");
            Log(result.OllamaReachable ? "info" : "warn", result.Status);
        }
        catch (OperationCanceledException)
        {
            LocalLlmStatus = "Local model refresh canceled.";
            PhaseTitle = "Refresh canceled";
            PhaseDetail = "Model refresh was stopped.";
            CompleteDependencyTransferProgress("Model refresh canceled.", succeeded: false);
            Log("warn", "Local model refresh canceled.");
        }
        catch (Exception ex)
        {
            LocalLlmStatus = ex.Message;
            CompleteDependencyTransferProgress(ex.Message, succeeded: false);
            Log("warn", $"Local model scan failed: {ex.Message}");
        }
        finally
        {
            IsRefreshingLocalModels = false;
            refreshCancellation.Dispose();
        }
    }

    private static async Task<IReadOnlyDictionary<string, DependencyRuntimeStatus>> AwaitDependencyStatusScanAsync(
        Task<IReadOnlyDictionary<string, DependencyRuntimeStatus>> dependencyStatusTask,
        IProgress<LocalLlmTransferProgress> progress)
    {
        ReportRefreshProgress(progress, "Validating dependency runtimes.", 62);
        return await dependencyStatusTask.ConfigureAwait(false);
    }

    private async Task RefreshLocalModelInstallStateAsync()
    {
        using var refreshTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        try
        {
            var result = await _localLlmService.RefreshAsync(refreshTimeout.Token);
            HardwareSummary = result.Hardware.Summary;
            LocalLlmStatus = result.Status;
            IsOllamaInstalled = result.OllamaInstalled;
            ApplyLocalModelRefresh(result, preserveBackendModelStates: true);
            AppendTerminalOutput($"Installed model IDs: {string.Join(", ", result.InstalledModelIds.OrderBy(id => id, StringComparer.OrdinalIgnoreCase))}");
        }
        catch (OperationCanceledException)
        {
            AppendTerminalOutput("Model install-state refresh timed out after uninstall. Press Refresh to scan again.");
        }
        catch (Exception ex)
        {
            AppendTerminalOutput($"Model install-state refresh failed after uninstall: {ex.Message}");
        }
    }

    private static void ReportRefreshProgress(
        IProgress<LocalLlmTransferProgress> progress,
        string status,
        double percent)
    {
        progress.Report(new LocalLlmTransferProgress(
            "Refreshing Models",
            status,
            (long)Math.Clamp((int)Math.Ceiling(percent / 25d), 0, 4),
            4,
            null,
            Math.Clamp(percent, 0, 100)));
    }

    public void SelectOllamaModelsDirectory(string directory)
    {
        OllamaModelsDirectory = directory;
        ApplyOllamaModelsDirectory();
    }

    private void ApplyOllamaModelsDirectory()
    {
        var result = LocalLlmService.ConfigureOllamaModelsDirectory(OllamaModelsDirectory);
        OllamaModelsDirectory = result.Directory;
        OllamaModelsDirectoryStatus = result.Status;
        _settings.OllamaModelsDirectory = result.Directory;
        SaveSettingsQuietly();
        AppendTerminalOutput($"{LocalLlmService.OllamaModelsEnvironmentVariable}={result.Directory}");
        Log(result.Succeeded ? "ok" : "warn", result.Status);
    }

    private static IEnumerable<LlmBackendDependencyViewModel> CreateLlmBackendDependencies()
    {
        yield return new LlmBackendDependencyViewModel(
            "ollama",
            "Ollama",
            "local model manager",
            "ollama + OpenAI-compatible",
            "Windows, macOS, Linux",
            "Primary CC path for local chat, model pulls, cloud-tag detection, and simple OpenAI-compatible use.",
            "Install Ollama, then set OLLAMA_MODELS to the model storage drive.",
            isRequired: true,
            isRecommended: true);
        yield return new LlmBackendDependencyViewModel(
            "llama_cpp_server",
            "llama.cpp server",
            "local server",
            "OpenAI-compatible",
            "Windows, macOS, Linux",
            "Direct GGUF runtime for models that are not packaged as Ollama tags.",
            "Install llama.cpp and run its server against a GGUF file.",
            isRequired: false,
            isRecommended: true);
        yield return new LlmBackendDependencyViewModel(
            "lm_studio",
            "LM Studio",
            "local GUI/server",
            "OpenAI-compatible",
            "Windows, macOS, Linux",
            "Friendly local model browser and server for GGUF models.",
            "Install LM Studio and enable its local server.",
            isRequired: false,
            isRecommended: true);
        yield return new LlmBackendDependencyViewModel(
            "koboldcpp",
            "KoboldCpp",
            "local server",
            "OpenAI-compatible",
            "Windows, macOS, Linux",
            "Single-binary GGUF runner, useful on Windows and CPU/GPU mixed setups.",
            "Download KoboldCpp and launch its API server.",
            isRequired: false,
            isRecommended: false);
        yield return new LlmBackendDependencyViewModel(
            "mlx_lm",
            "MLX LM",
            "Apple Silicon runtime",
            "Python library",
            "macOS",
            "Native Apple Silicon route for MLX-converted local models.",
            "Install mlx-lm in a Python environment on Apple Silicon.",
            isRequired: false,
            isRecommended: false);
        yield return new LlmBackendDependencyViewModel(
            "mlc_llm",
            "MLC LLM",
            "compiler runtime",
            "native/server",
            "Windows, macOS, Linux, Android, iOS, browser",
            "Compiler/deployment route for WebGPU, Vulkan, mobile, and other edge targets.",
            "Install MLC LLM when targeting mobile, browser, or compiled runtime experiments.",
            isRequired: false,
            isRecommended: false);
        yield return new LlmBackendDependencyViewModel(
            "transformers",
            "Hugging Face Transformers",
            "library fallback",
            "Python library",
            "Windows, macOS, Linux",
            "Universal fallback for new, niche, embedding, reranker, and research models before GGUF support.",
            "Install Python plus transformers/accelerate for manual model hosting.",
            isRequired: false,
            isRecommended: true);
        yield return new LlmBackendDependencyViewModel(
            "diffusers",
            "Hugging Face Diffusers",
            "image generation library",
            "Python library",
            "Windows, macOS, Linux",
            "Runs SD 1.5, SD 2.1, Tiny SD, BK-SDM, LCM, SD Turbo, and other low-hardware image checkpoints.",
            "Install Python plus torch, diffusers, huggingface-hub, transformers, accelerate, safetensors, and Pillow.",
            isRequired: false,
            isRecommended: true);
        yield return new LlmBackendDependencyViewModel(
            "stable_diffusion_cpp",
            "stable-diffusion.cpp",
            "GGUF image runner",
            "native CLI",
            "Windows, macOS, Linux",
            "Runs quantized Stable Diffusion and FLUX GGUF image models with CPU/GPU offload on low-VRAM machines.",
            "Install stable-diffusion.cpp and download compatible GGUF model files.",
            isRequired: false,
            isRecommended: true);
        yield return new LlmBackendDependencyViewModel(
            "vllm",
            "vLLM",
            "GPU server",
            "OpenAI-compatible",
            "Linux, Windows via WSL",
            "High-throughput GPU serving for larger dense and MoE models.",
            "Install vLLM on a CUDA server or WSL environment.",
            isRequired: false,
            isRecommended: false);
        yield return new LlmBackendDependencyViewModel(
            "sglang",
            "SGLang",
            "agent server",
            "OpenAI-compatible",
            "Linux, Windows via WSL",
            "Structured generation, JSON/tool flows, and multi-call model programs.",
            "Install SGLang where CUDA server workflows are available.",
            isRequired: false,
            isRecommended: false);
        yield return new LlmBackendDependencyViewModel(
            "onnxruntime_genai",
            "ONNX Runtime GenAI",
            "edge runtime",
            "native library",
            "Windows, Linux",
            "Windows and edge deployment route for ONNX-converted models.",
            "Install ONNX Runtime GenAI and use ONNX model artifacts.",
            isRequired: false,
            isRecommended: false);
        yield return new LlmBackendDependencyViewModel(
            "openvino_genai",
            "OpenVINO GenAI",
            "Intel edge runtime",
            "native library",
            "Windows, Linux",
            "Intel CPU/GPU/NPU path for local and edge inference.",
            "Install OpenVINO GenAI and use OpenVINO model artifacts.",
            isRequired: false,
            isRecommended: false);
        yield return new LlmBackendDependencyViewModel(
            "tensorrt_llm",
            "TensorRT-LLM",
            "NVIDIA server runtime",
            "native server",
            "Linux",
            "Maximum-performance NVIDIA path for heavier production serving.",
            "Install TensorRT-LLM on a compatible NVIDIA server.",
            isRequired: false,
            isRecommended: false);
        yield return new LlmBackendDependencyViewModel(
            "exllamav2_tabbyapi",
            "ExLlamaV2 / TabbyAPI",
            "consumer GPU server",
            "OpenAI-compatible",
            "Windows, Linux",
            "Fast NVIDIA consumer GPU route for EXL2 quantized models.",
            "Install ExLlamaV2 with TabbyAPI for an OpenAI-compatible endpoint.",
            isRequired: false,
            isRecommended: false);
        yield return new LlmBackendDependencyViewModel(
            "bitnet_cpp",
            "bitnet.cpp",
            "special runtime",
            "native CLI",
            "Windows, Linux",
            "Runtime for native BitNet ultra-low-bit models; not a normal GGUF path.",
            "Install bitnet.cpp for BitNet-native checkpoints.",
            isRequired: false,
            isRecommended: false);
        yield return new LlmBackendDependencyViewModel(
            "rwkv_runner",
            "RWKV runner",
            "special runtime",
            "custom",
            "Windows, macOS, Linux",
            "Runtime family for RWKV recurrent models and non-transformer experiments.",
            "Install an RWKV runner when using RWKV-specific checkpoints.",
            isRequired: false,
            isRecommended: false);
    }

    private void UpdateOllamaBackendDependency()
    {
        if (LlmBackendDependencies is null)
        {
            return;
        }

        var dependency = LlmBackendDependencies.FirstOrDefault(item => item.Id.Equals("ollama", StringComparison.OrdinalIgnoreCase));
        if (dependency is null)
        {
            return;
        }

        var isReady = IsOllamaInstalled || LocalLlmStatus.Contains("ready", StringComparison.OrdinalIgnoreCase);
        dependency.ApplyStatus(
            isReady,
            isReady ? "Ready" : "Not ready",
            $"{LocalLlmStatus} Storage: {OllamaModelsDirectory}");
        OnPropertyChanged(nameof(DependencySummary));
        OnPropertyChanged(nameof(LlmCompactInfoLabel));
    }

    private async Task<IReadOnlyDictionary<string, DependencyRuntimeStatus>> DetectBackendDependencyStatusesAsync(
        CancellationToken cancellationToken,
        IProgress<LocalLlmTransferProgress>? progress = null)
    {
        progress?.Report(new LocalLlmTransferProgress(
            "Refreshing Models",
            "Detecting dependency runtimes.",
            2,
            4,
            null,
            62));
        cancellationToken.ThrowIfCancellationRequested();
        var checks = PythonDependencyEnvironment.AllSpecs.ToDictionary(
            spec => spec.Id,
            spec => DetectPythonDependencyAsync(spec, cancellationToken),
            StringComparer.OrdinalIgnoreCase);

        checks["llama_cpp_server"] = Task.FromResult(DetectExecutableDependency(
            "llama_cpp_server",
            "llama.cpp server",
            ["llama-server.exe", "llama-server", "llama.cpp-server.exe", "llama.cpp-server"]));
        checks["lm_studio"] = Task.FromResult(DetectExecutableDependency(
            "lm_studio",
            "LM Studio",
            ["lms.exe", "lms"]));
        checks["koboldcpp"] = Task.FromResult(DetectExecutableDependency(
            "koboldcpp",
            "KoboldCpp",
            ["koboldcpp.exe", "koboldcpp"]));
        checks["mlc_llm"] = DetectPythonOrExecutableDependencyAsync(
            "mlc_llm",
            "MLC LLM",
            ["mlc_llm.exe", "mlc_llm", "mlc.exe", "mlc"],
            cancellationToken);
        checks["stable_diffusion_cpp"] = Task.FromResult(DetectExecutableDependency(
            "stable_diffusion_cpp",
            "stable-diffusion.cpp",
            ["sd.exe", "sd", "stable-diffusion-cli.exe", "stable-diffusion-cli"]));
        checks["tensorrt_llm"] = DetectPythonOrExecutableDependencyAsync(
            "tensorrt_llm",
            "TensorRT-LLM",
            ["trtllm-serve.exe", "trtllm-serve", "tensorrt_llm.exe", "tensorrt_llm"],
            cancellationToken);
        checks["exllamav2_tabbyapi"] = DetectPythonOrExecutableDependencyAsync(
            "exllamav2_tabbyapi",
            "ExLlamaV2 / TabbyAPI",
            ["tabbyapi.exe", "tabbyapi", "python-tabby.exe", "python-tabby"],
            cancellationToken);
        checks["bitnet_cpp"] = Task.FromResult(DetectSourceOrExecutableDependency(
            "bitnet_cpp",
            "bitnet.cpp",
            ["bitnet-run.exe", "bitnet-run", "run_inference.exe", "run_inference"]));
        checks["rwkv_runner"] = Task.FromResult(DetectExecutableDependency(
            "rwkv_runner",
            "RWKV runner",
            ["rwkv-runner.exe", "rwkv-runner"]));

        await Task.WhenAll(checks.Values);
        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new LocalLlmTransferProgress(
            "Refreshing Models",
            "Dependency runtime scan complete.",
            3,
            4,
            null,
            78));
        return checks.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.Result,
            StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyDictionary<string, DependencyRuntimeStatus> DetectCachedBackendDependencyStatuses()
    {
        var statuses = new Dictionary<string, DependencyRuntimeStatus>(StringComparer.OrdinalIgnoreCase);

        foreach (var spec in PythonDependencyEnvironment.AllSpecs)
        {
            if (PythonDependencyEnvironment.HasManagedReadyStamp(spec))
            {
                var managedPython = PythonDependencyEnvironment.ManagedPythonExecutable(spec.Id);
                statuses[spec.Id] = new DependencyRuntimeStatus(
                    true,
                    $"{spec.DisplayName} ready in {managedPython} (cached managed ContextControl venv).",
                    managedPython,
                    IsManaged: true);
                continue;
            }

            var externalPython = PythonDependencyEnvironment.ReadRememberedExternalPython(spec);
            if (!string.IsNullOrWhiteSpace(externalPython))
            {
                statuses[spec.Id] = new DependencyRuntimeStatus(
                    true,
                    $"{spec.DisplayName} ready in {externalPython} (cached external Python).",
                    externalPython);
            }
        }

        foreach (var spec in NativeDependencyEnvironment.AllSpecs)
        {
            var executable = NativeDependencyEnvironment.FindManagedExecutable(spec.Id, spec.ExecutableNames);
            if (!string.IsNullOrWhiteSpace(executable))
            {
                statuses[spec.Id] = new DependencyRuntimeStatus(
                    true,
                    $"{spec.DisplayName} ready in ContextControl's managed native store: {executable}.",
                    executable,
                    IsManaged: true);
            }
        }

        foreach (var spec in SourceDependencyEnvironment.AllSpecs)
        {
            var sourcePath = SourceDependencyEnvironment.FindManagedSource(spec.Id, spec.RequiredFiles);
            if (!string.IsNullOrWhiteSpace(sourcePath))
            {
                statuses[spec.Id] = new DependencyRuntimeStatus(
                    true,
                    $"{spec.DisplayName} source ready in ContextControl's managed source store: {sourcePath}.",
                    sourcePath,
                    IsManaged: true);
            }
        }

        return statuses;
    }

    private static async Task<DependencyRuntimeStatus> DetectPythonDependencyAsync(
        PythonDependencySpec spec,
        CancellationToken cancellationToken)
    {
        var requiresRuntimeHealthProbe = spec.Id.Equals("diffusers", StringComparison.OrdinalIgnoreCase);
        if (!requiresRuntimeHealthProbe && PythonDependencyEnvironment.HasManagedReadyStamp(spec))
        {
            var managedPython = PythonDependencyEnvironment.ManagedPythonExecutable(spec.Id);
            return new DependencyRuntimeStatus(
                true,
                $"{spec.DisplayName} ready in {managedPython} (managed ContextControl venv).",
                managedPython,
                IsManaged: true);
        }

        var status = await DetectPythonModulesAsync(
            spec.Id,
            spec.DisplayName,
            spec.PackagesToModules,
            includeExternalCandidates: false,
            importModules: requiresRuntimeHealthProbe,
            cancellationToken).ConfigureAwait(false);

        RememberPythonDependencyRuntime(spec, status);
        return status;
    }

    private static async Task<DependencyRuntimeStatus> DetectPythonOrExecutableDependencyAsync(
        string dependencyId,
        string displayName,
        IReadOnlyList<string> executableNames,
        CancellationToken cancellationToken)
    {
        var executableStatus = DetectExecutableDependency(dependencyId, displayName, executableNames);
        if (executableStatus.IsReady)
        {
            return executableStatus;
        }

        if (!PythonDependencyEnvironment.TryGetSpec(dependencyId, out var spec))
        {
            return executableStatus;
        }

        var pythonStatus = await DetectPythonDependencyAsync(spec, cancellationToken).ConfigureAwait(false);
        if (pythonStatus.IsReady)
        {
            return pythonStatus;
        }

        return new DependencyRuntimeStatus(false, $"{pythonStatus.Detail} | {executableStatus.Detail}");
    }

    private static void RememberPythonDependencyRuntime(PythonDependencySpec spec, DependencyRuntimeStatus status)
    {
        if (!status.IsReady || string.IsNullOrWhiteSpace(status.Executable))
        {
            return;
        }

        if (status.IsManaged)
        {
            PythonDependencyEnvironment.MarkManagedReady(spec);
        }
        else
        {
            PythonDependencyEnvironment.MarkExternalReady(spec, status.Executable);
        }

        var envVar = PythonDependencyEnvironment.EnvironmentVariableName(spec.Id);
        Environment.SetEnvironmentVariable(envVar, status.Executable, EnvironmentVariableTarget.Process);
        if (spec.Id.Equals("diffusers", StringComparison.OrdinalIgnoreCase))
        {
            Environment.SetEnvironmentVariable("CC_PYTHON", status.Executable, EnvironmentVariableTarget.Process);
        }
    }

    private void ApplyDetectedBackendDependencyStatuses(IReadOnlyDictionary<string, DependencyRuntimeStatus> statuses)
    {
        foreach (var dependency in LlmBackendDependencies)
        {
            if (dependency.Id.Equals("ollama", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (statuses.TryGetValue(dependency.Id, out var status))
            {
                dependency.ApplyStatus(status.IsReady, status.IsReady ? "Ready" : "Not detected", status.Detail, status.IsManaged);
            }
        }

        OnPropertyChanged(nameof(DependencySummary));
        OnPropertyChanged(nameof(LlmCompactInfoLabel));
        ApplyDependencyFilters();
        ApplyBackendDependencyStatesToModels();
    }

    private static async Task<DependencyRuntimeStatus> DetectPythonModulesAsync(
        string dependencyId,
        string displayName,
        IReadOnlyDictionary<string, string> packagesToModules,
        bool includeExternalCandidates,
        bool importModules,
        CancellationToken cancellationToken)
    {
        var pythonCandidates = PythonDependencyEnvironment
            .FindPythonCandidatesForDetection(dependencyId, includePathCandidates: includeExternalCandidates)
            .Where(candidate => includeExternalCandidates || candidate.IsManaged)
            .ToArray();
        if (pythonCandidates.Length == 0)
        {
            return includeExternalCandidates
                ? new DependencyRuntimeStatus(false, $"Python was not found on PATH. {displayName} needs Python packages.")
                : new DependencyRuntimeStatus(false, $"{displayName} managed venv was not found.");
        }

        var failures = new List<string>();
        foreach (var python in pythonCandidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var status = await ProbePythonModulesAsync(dependencyId, displayName, python, packagesToModules, importModules, cancellationToken).ConfigureAwait(false);
            if (status.IsReady)
            {
                return status;
            }

            failures.Add($"{python}: {status.Detail}");
        }

        var firstFailure = failures.FirstOrDefault();
        return new DependencyRuntimeStatus(
            false,
            firstFailure is null
                ? $"{displayName} packages were not detected."
                : $"{displayName} is not ready. First failure: {firstFailure}");
    }

    private static async Task<DependencyRuntimeStatus> ProbePythonModulesAsync(
        string dependencyId,
        string displayName,
        PythonEnvironmentCandidate python,
        IReadOnlyDictionary<string, string> packagesToModules,
        bool importModules,
        CancellationToken cancellationToken)
    {
        var timeout = importModules
            ? dependencyId.Equals("diffusers", StringComparison.OrdinalIgnoreCase)
                ? TimeSpan.FromSeconds(120)
                : TimeSpan.FromSeconds(45)
            : TimeSpan.FromSeconds(6);
        var result = await RunProcessForDependencyDetectionAsync(
            python.Executable,
            ["-c", importModules
                ? PythonDependencyEnvironment.BuildPythonDependencyHealthScript(dependencyId, packagesToModules)
                : PythonDependencyEnvironment.BuildPythonModuleProbeScript(packagesToModules)],
            timeout,
            cancellationToken,
            python.IsManaged ? PythonDependencyEnvironment.ManagedProcessEnvironment : null).ConfigureAwait(false);
        if (!result.Started)
        {
            return new DependencyRuntimeStatus(false, "could not be started");
        }

        if (result.ExitCode == 0)
        {
            var executable = FirstInterestingLine(result.StandardOutput) ?? python.Executable;
            var source = python.IsManaged ? "managed ContextControl venv" : python.SourceLabel;
            return new DependencyRuntimeStatus(true, $"{displayName} ready in {executable} ({source}).", executable, python.IsManaged);
        }

        if (result.TimedOut)
        {
            return new DependencyRuntimeStatus(false, $"runtime import check timed out after {timeout.TotalSeconds:0}s");
        }

        var reason = FirstDependencyInstallLine(result) ?? $"exited {result.ExitCode}";
        return new DependencyRuntimeStatus(false, reason);
    }

    private static async Task<DependencyProcessResult> RunProcessForDependencyDetectionAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
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
                return new DependencyProcessResult(false, -1, "", "");
            }
        }
        catch (Exception ex)
        {
            return new DependencyProcessResult(false, -1, "", ex.Message);
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            var timedOut = !cancellationToken.IsCancellationRequested;
            TryKillDependencyProcess(process);
            return new DependencyProcessResult(
                true,
                -1,
                await stdoutTask.ConfigureAwait(false),
                await stderrTask.ConfigureAwait(false),
                timedOut);
        }

        return new DependencyProcessResult(
            true,
            process.ExitCode,
            await stdoutTask.ConfigureAwait(false),
            await stderrTask.ConfigureAwait(false));
    }

    private static DependencyRuntimeStatus DetectExecutableDependency(
        string dependencyId,
        string displayName,
        IReadOnlyList<string> executableNames)
    {
        var names = ExpandExecutableNames(dependencyId, executableNames);
        var managedExecutable = NativeDependencyEnvironment.FindManagedExecutable(dependencyId, names);
        if (!string.IsNullOrWhiteSpace(managedExecutable))
        {
            return new DependencyRuntimeStatus(
                true,
                $"{displayName} ready in ContextControl's managed native store: {managedExecutable}.",
                managedExecutable,
                IsManaged: true);
        }

        foreach (var executableName in names)
        {
            var executable = FindExecutableOnPath(executableName);
            if (!string.IsNullOrWhiteSpace(executable))
            {
                return new DependencyRuntimeStatus(true, $"{displayName} ready at {executable}.");
            }
        }

        return NativeDependencyEnvironment.HasManagedInstaller(dependencyId)
            ? new DependencyRuntimeStatus(false, $"{displayName} executable was not found in ContextControl's managed store or on PATH.")
            : new DependencyRuntimeStatus(false, $"{displayName} executable was not found on PATH.");
    }

    private static DependencyRuntimeStatus DetectSourceOrExecutableDependency(
        string dependencyId,
        string displayName,
        IReadOnlyList<string> executableNames)
    {
        if (SourceDependencyEnvironment.TryGetSpec(dependencyId, out var sourceSpec))
        {
            var sourcePath = SourceDependencyEnvironment.FindManagedSource(sourceSpec.Id, sourceSpec.RequiredFiles);
            if (!string.IsNullOrWhiteSpace(sourcePath))
            {
                return new DependencyRuntimeStatus(
                    true,
                    $"{displayName} source ready in ContextControl's managed source store: {sourcePath}.",
                    sourcePath,
                    IsManaged: true);
            }
        }

        return DetectExecutableDependency(dependencyId, displayName, executableNames);
    }

    private static IReadOnlyList<string> ExpandExecutableNames(string dependencyId, IReadOnlyList<string> fallbackNames)
    {
        if (!NativeDependencyEnvironment.TryGetSpec(dependencyId, out var spec))
        {
            return fallbackNames;
        }

        return spec.ExecutableNames
            .Concat(fallbackNames)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string? FirstInterestingLine(string text)
    {
        return InterestingLines(text).FirstOrDefault();
    }

    private sealed record DependencyRuntimeStatus(bool IsReady, string Detail, string? Executable = null, bool IsManaged = false);

}
