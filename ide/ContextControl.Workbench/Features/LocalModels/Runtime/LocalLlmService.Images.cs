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
    private static readonly TimeSpan DiffusersPythonProbeValidationTimeout = TimeSpan.FromSeconds(120);
    private static readonly TimeSpan DiffusersPythonCacheProbeValidationTimeout = TimeSpan.FromSeconds(120);

    public async Task<LocalLlmChatResult> DownloadImageModelAsync(
        string modelId,
        IProgress<LocalLlmTransferProgress>? progress,
        IProgress<string>? terminal,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(modelId))
        {
            return new LocalLlmChatResult(false, "No image model selected.");
        }

        if (!IsDiffusersImageModel(modelId))
        {
            return new LocalLlmChatResult(false, $"{modelId} does not have a ContextControl-managed model downloader yet.");
        }

        var pythonResolution = await ResolvePythonForModulesAsync(
            DiffusersPythonRequirements,
            cancellationToken,
            requireFlux2KleinPipeline: IsFlux2KleinDiffusersModel(modelId)).ConfigureAwait(false);
        if (!pythonResolution.Succeeded || pythonResolution.Executable is null)
        {
            return new LocalLlmChatResult(false, BuildDiffusersRuntimeFailureMessage(pythonResolution.Status));
        }

        var python = pythonResolution.Executable;
        var pythonEnvironment = pythonResolution.IsManaged
            ? PythonDependencyEnvironment.ManagedProcessEnvironment
            : null;
        var script = """
import sys, time, threading, os

model_id = sys.argv[1]

stop_heartbeat = False
stage = "starting"
started = time.time()
stage_deadline = None

def status(message):
    print("CC_STATUS " + message, flush=True)

def set_stage(message, timeout_seconds=None):
    global stage, stage_deadline
    stage = message
    stage_deadline = time.time() + timeout_seconds if timeout_seconds else None
    status(message)

def heartbeat():
    while not stop_heartbeat:
        time.sleep(30)
        if not stop_heartbeat:
            elapsed = int((time.time() - started) // 60)
            status(f"Still {stage} after {elapsed} min. Large Hugging Face shards can keep the file counter on the same percentage.")

def watchdog():
    while not stop_heartbeat:
        time.sleep(5)
        if stage_deadline and time.time() > stage_deadline:
            print(f"CC_ERROR Timed out while {stage}. Reinstall Hugging Face Diffusers from Dependencies if this repeats.", flush=True)
            os._exit(124)

def flux2_klein_allow_patterns():
    return [
        "model_index.json",
        "scheduler/*",
        "text_encoder/*",
        "tokenizer/*",
        "transformer/*",
        "vae/*",
    ]

is_flux2_klein = "flux.2-klein" in model_id.lower() or "flux2-klein" in model_id.lower()
thread = threading.Thread(target=heartbeat, daemon=True)
watchdog_thread = threading.Thread(target=watchdog, daemon=True)
thread.start()
watchdog_thread.start()
try:
    set_stage("Importing Hugging Face Hub downloader.", 180)
    from huggingface_hub import snapshot_download

    if is_flux2_klein:
        set_stage("Downloading FLUX.2 Klein Diffusers pipeline files. First run is large and may sit on one shard for a long time.")
        path = snapshot_download(repo_id=model_id, allow_patterns=flux2_klein_allow_patterns())
    else:
        set_stage("Downloading Hugging Face Diffusers model files.")
        path = snapshot_download(repo_id=model_id)
finally:
    stop_heartbeat = True
    thread.join(timeout=1)
    watchdog_thread.join(timeout=1)

print(f"Cached model at {path}")
""";

        progress?.Report(new LocalLlmTransferProgress(
            $"Downloading {modelId}",
            "Downloading Hugging Face model files into the local cache.",
            null,
            null,
            null,
            null));
        terminal?.Report($"> {Path.GetFileName(python)} -c <huggingface snapshot_download> {modelId}");
        terminal?.Report(ResolveHuggingFaceTokenStatus(null));
        var result = await RunProcessStreamingAsync(
            python,
            ["-c", script, modelId],
            ResolveDiffusersDownloadTimeout(modelId),
            chunk =>
            {
                var clean = CleanProgressText(chunk);
                if (!string.IsNullOrWhiteSpace(clean))
                {
                    terminal?.Report(clean);
                    progress?.Report(new LocalLlmTransferProgress($"Downloading {modelId}", clean, null, null, null, null));
                }
            },
            cancellationToken,
            environmentVariables: pythonEnvironment).ConfigureAwait(false);

        if (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }

        if (!result.Started)
        {
            return new LocalLlmChatResult(false, "Python could not be started.");
        }

        if (result.ExitCode != 0)
        {
            var reason = FirstFailureLine(result.StandardError) ?? FirstFailureLine(result.StandardOutput) ?? $"Hugging Face model download exited {result.ExitCode}.";
            return new LocalLlmChatResult(false, reason);
        }

        return new LocalLlmChatResult(true, $"Downloaded and cached {modelId}.");
    }

    public async Task<IReadOnlySet<string>> DetectCachedImageModelIdsAsync(
        IReadOnlyList<string> modelIds,
        CancellationToken cancellationToken = default,
        bool allowPythonProbe = true)
    {
        var candidates = modelIds
            .Where(modelId => !string.IsNullOrWhiteSpace(modelId) && IsDiffusersImageModel(modelId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (candidates.Length == 0)
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        var cached = DetectCachedImageModelIdsFromFileSystem(candidates);
        var remaining = candidates
            .Where(modelId => !cached.Contains(modelId))
            .ToArray();
        if (!allowPythonProbe || remaining.Length == 0)
        {
            return cached;
        }

        var pythonResolution = await ResolvePythonForModulesAsync(
            DiffusersPythonRequirements,
            cancellationToken,
            DiffusersPythonCacheProbeValidationTimeout).ConfigureAwait(false);
        if (!pythonResolution.Succeeded || pythonResolution.Executable is null)
        {
            return cached;
        }

        var script = """
import sys
from huggingface_hub import snapshot_download

def is_flux2_klein(model_id):
    clean = model_id.lower()
    return "flux.2-klein" in clean or "flux2-klein" in clean

def flux2_klein_allow_patterns():
    return [
        "model_index.json",
        "scheduler/*",
        "text_encoder/*",
        "tokenizer/*",
        "transformer/*",
        "vae/*",
    ]

def allow_patterns_for(model_id):
    if is_flux2_klein(model_id):
        return flux2_klein_allow_patterns()
    return None

for model_id in sys.argv[1:]:
    try:
        if is_flux2_klein(model_id):
            from diffusers import Flux2KleinPipeline
        snapshot_download(repo_id=model_id, local_files_only=True, allow_patterns=allow_patterns_for(model_id))
    except Exception:
        continue
    print("CC_MODEL_CACHED=" + model_id)
""";
        var args = new List<string> { "-c", script };
        args.AddRange(remaining);
        var result = await RunProcessAsync(
            pythonResolution.Executable,
            args,
            TimeSpan.FromSeconds(45),
            cancellationToken,
            pythonResolution.IsManaged ? PythonDependencyEnvironment.ManagedProcessEnvironment : null).ConfigureAwait(false);
        if (!result.Started || result.ExitCode != 0)
        {
            return cached;
        }

        foreach (var modelId in result.StandardOutput
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => line.StartsWith("CC_MODEL_CACHED=", StringComparison.Ordinal))
            .Select(line => line["CC_MODEL_CACHED=".Length..])
            .Where(modelId => !string.IsNullOrWhiteSpace(modelId)))
        {
            cached.Add(modelId);
        }

        return cached;
    }

    public static string ResolveHuggingFaceHubCacheDirectory()
    {
        var explicitCache = Environment.GetEnvironmentVariable("HUGGINGFACE_HUB_CACHE");
        if (!string.IsNullOrWhiteSpace(explicitCache))
        {
            return Path.GetFullPath(Environment.ExpandEnvironmentVariables(explicitCache.Trim()));
        }

        var hfHome = Environment.GetEnvironmentVariable("HF_HOME");
        if (!string.IsNullOrWhiteSpace(hfHome))
        {
            return Path.Combine(Path.GetFullPath(Environment.ExpandEnvironmentVariables(hfHome.Trim())), "hub");
        }

        var xdgCache = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
        if (!OperatingSystem.IsWindows() && !string.IsNullOrWhiteSpace(xdgCache))
        {
            return Path.Combine(Path.GetFullPath(Environment.ExpandEnvironmentVariables(xdgCache.Trim())), "huggingface", "hub");
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".cache",
            "huggingface",
            "hub");
    }

    private static HashSet<string> DetectCachedImageModelIdsFromFileSystem(IReadOnlyList<string> modelIds)
    {
        var cached = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var cacheRoot = ResolveHuggingFaceHubCacheDirectory();
        if (string.IsNullOrWhiteSpace(cacheRoot) || !Directory.Exists(cacheRoot))
        {
            return cached;
        }

        foreach (var modelId in modelIds)
        {
            if (HasCachedDiffusersSnapshot(cacheRoot, modelId))
            {
                cached.Add(modelId);
            }
        }

        return cached;
    }

    private static bool HasCachedDiffusersSnapshot(string cacheRoot, string modelId)
    {
        try
        {
            var modelDirectory = Path.Combine(cacheRoot, "models--" + modelId.Replace("/", "--", StringComparison.Ordinal));
            var snapshotsDirectory = Path.Combine(modelDirectory, "snapshots");
            if (!Directory.Exists(snapshotsDirectory))
            {
                return false;
            }

            foreach (var snapshot in Directory.EnumerateDirectories(snapshotsDirectory))
            {
                if (File.Exists(Path.Combine(snapshot, "model_index.json")))
                {
                    return true;
                }
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    public async Task<LocalLlmImageGenerationResult> GenerateImageAsync(
        string modelId,
        string prompt,
        string outputDirectory,
        IProgress<LocalLlmGenerationProgress>? progress,
        IProgress<string>? terminal,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(modelId))
        {
            return new LocalLlmImageGenerationResult(false, "Choose an installed image generation model first.", [], "");
        }

        if (string.IsNullOrWhiteSpace(prompt))
        {
            return new LocalLlmImageGenerationResult(false, "Write an image prompt first.", [], "");
        }

        var resolvedOutputDirectory = string.IsNullOrWhiteSpace(outputDirectory)
            ? Path.Combine(Path.GetTempPath(), "ContextControl", "image-gen")
            : Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(resolvedOutputDirectory);

        if (IsOllamaExperimentalImageModel(modelId) && !IsOllamaExperimentalImageGenerationSupported())
        {
            return new LocalLlmImageGenerationResult(false, BuildUnsupportedOllamaImageMessage(modelId), [], resolvedOutputDirectory);
        }

        if (IsDiffusersImageModel(modelId))
        {
            return await GenerateImageWithDiffusersAsync(
                modelId,
                prompt,
                resolvedOutputDirectory,
                progress,
                terminal,
                cancellationToken).ConfigureAwait(false);
        }

        if (IsStableDiffusionCppImageModel(modelId))
        {
            return await GenerateImageWithStableDiffusionCppAsync(
                modelId,
                prompt,
                resolvedOutputDirectory,
                progress,
                terminal,
                cancellationToken).ConfigureAwait(false);
        }

        var executablePath = ResolveOllamaExecutable();
        if (executablePath is null)
        {
            return new LocalLlmImageGenerationResult(false, "Ollama command was not found. Install Ollama before generating images.", [], resolvedOutputDirectory);
        }

        var before = SnapshotImageFiles(resolvedOutputDirectory);
        var startedUtc = DateTime.UtcNow.AddSeconds(-2);
        progress?.Report(new LocalLlmGenerationProgress("Preparing image generation...", null, null, null, null, null, null, null, false));
        terminal?.Report($"> ollama run {modelId} \"{prompt.Trim()}\"");

        var result = await RunProcessStreamingAsync(
            executablePath,
            ["run", modelId, prompt.Trim()],
            TimeSpan.FromMinutes(30),
            chunk =>
            {
                var clean = CleanProgressText(chunk);
                if (!string.IsNullOrWhiteSpace(clean))
                {
                    terminal?.Report(clean);
                }

                progress?.Report(new LocalLlmGenerationProgress("Generating image...", null, null, null, null, null, null, null, false));
            },
            cancellationToken,
            resolvedOutputDirectory).ConfigureAwait(false);

        var images = FindGeneratedImageFiles(resolvedOutputDirectory, before, startedUtc);
        progress?.Report(new LocalLlmGenerationProgress("Image generation complete.", null, null, null, null, null, null, null, true));

        if (!result.Started)
        {
            return new LocalLlmImageGenerationResult(false, "Ollama could not be started.", [], resolvedOutputDirectory);
        }

        if (result.ExitCode != 0)
        {
            var reason = FirstFailureLine(result.StandardError) ?? FirstFailureLine(result.StandardOutput) ?? $"ollama run exited {result.ExitCode}.";
            if (LooksLikeHttpEofBackendFailure(result.StandardError, result.StandardOutput))
            {
                reason = BuildOllamaHttpEofImageFailureMessage(modelId);
            }

            return new LocalLlmImageGenerationResult(false, reason, images, resolvedOutputDirectory, result.StandardOutput);
        }

        if (images.Count == 0)
        {
            return new LocalLlmImageGenerationResult(
                false,
                "Ollama finished, but no generated image file was detected.",
                [],
                resolvedOutputDirectory,
                result.StandardOutput);
        }

        return new LocalLlmImageGenerationResult(
            true,
            $"Generated {images.Count:N0} image(s) with {modelId}.",
            images,
            resolvedOutputDirectory,
            result.StandardOutput);
    }

    private static async Task<LocalLlmImageGenerationResult> GenerateImageWithDiffusersAsync(
        string modelId,
        string prompt,
        string resolvedOutputDirectory,
        IProgress<LocalLlmGenerationProgress>? progress,
        IProgress<string>? terminal,
        CancellationToken cancellationToken)
    {
        var pythonResolution = await ResolvePythonForModulesAsync(
            DiffusersPythonRequirements,
            cancellationToken,
            requireFlux2KleinPipeline: IsFlux2KleinDiffusersModel(modelId)).ConfigureAwait(false);
        if (!pythonResolution.Succeeded || pythonResolution.Executable is null)
        {
            return new LocalLlmImageGenerationResult(false, BuildDiffusersRuntimeFailureMessage(pythonResolution.Status), [], resolvedOutputDirectory);
        }

        var python = pythonResolution.Executable;
        var pythonEnvironment = pythonResolution.IsManaged
            ? PythonDependencyEnvironment.ManagedProcessEnvironment
            : null;
        var before = SnapshotImageFiles(resolvedOutputDirectory);
        var startedUtc = DateTime.UtcNow.AddSeconds(-2);
        var outputPath = Path.Combine(resolvedOutputDirectory, $"cc-image-{DateTime.Now:yyyyMMdd-HHmmss}.png");
        var script = """
import os, sys, time, threading

model_id, prompt, output_path = sys.argv[1], sys.argv[2], sys.argv[3]
is_flux2_klein = "flux.2-klein" in model_id.lower() or "flux2-klein" in model_id.lower()

stop_heartbeat = False
stage = "starting"
started = time.time()
stage_deadline = None

def status(message):
    print("CC_STATUS " + message, flush=True)

def set_stage(message, timeout_seconds=None):
    global stage, stage_deadline
    stage = message
    stage_deadline = time.time() + timeout_seconds if timeout_seconds else None
    status(message)

def heartbeat():
    while not stop_heartbeat:
        time.sleep(30)
        if not stop_heartbeat:
            elapsed = int((time.time() - started) // 60)
            status(f"Still {stage} after {elapsed} min. First-run model downloads and CPU offload loads can be quiet for a while.")

def watchdog():
    while not stop_heartbeat:
        time.sleep(5)
        if stage_deadline and time.time() > stage_deadline:
            print(f"CC_ERROR Timed out while {stage}. Reinstall Hugging Face Diffusers from Dependencies if this repeats.", flush=True)
            os._exit(124)

def flux2_klein_allow_patterns():
    return [
        "model_index.json",
        "scheduler/*",
        "text_encoder/*",
        "tokenizer/*",
        "transformer/*",
        "vae/*",
    ]

thread = threading.Thread(target=heartbeat, daemon=True)
watchdog_thread = threading.Thread(target=watchdog, daemon=True)
thread.start()
watchdog_thread.start()
try:
    set_stage("Importing PyTorch.", 600)
    import torch

    set_stage("Importing Hugging Face Hub downloader.", 180)
    from huggingface_hub import snapshot_download

    model_source = model_id
    if is_flux2_klein:
        set_stage("Importing FLUX.2 Klein Diffusers pipeline.", 600)
        from diffusers import Flux2KleinPipeline

        if torch.cuda.is_available():
            dtype = torch.bfloat16 if getattr(torch.cuda, "is_bf16_supported", lambda: False)() else torch.float16
        else:
            dtype = torch.float32
        set_stage("Downloading FLUX.2 Klein Diffusers files. The percentage can pause while one multi-GB shard downloads.")
        model_source = snapshot_download(repo_id=model_id, allow_patterns=flux2_klein_allow_patterns())
        set_stage("Loading FLUX.2 Klein pipeline.")
        pipe = Flux2KleinPipeline.from_pretrained(model_source, torch_dtype=dtype)
        if torch.cuda.is_available():
            set_stage("Enabling CPU offload.")
            pipe.enable_model_cpu_offload()
        else:
            pipe = pipe.to("cpu")

        width = int(os.environ.get("CC_IMAGE_WIDTH", "768"))
        height = int(os.environ.get("CC_IMAGE_HEIGHT", "768"))
        generator = torch.Generator(device="cuda" if torch.cuda.is_available() else "cpu").manual_seed(0)
        set_stage(f"Generating FLUX.2 Klein image at {width}x{height} with CPU offload when CUDA is available.")
        image = pipe(
            prompt=prompt,
            height=height,
            width=width,
            guidance_scale=1.0,
            num_inference_steps=4,
            generator=generator,
        ).images[0]
    else:
        set_stage("Importing Diffusers text-to-image pipeline.", 600)
        from diffusers import AutoPipelineForText2Image

        dtype = torch.float16 if torch.cuda.is_available() else torch.float32
        set_stage("Loading Diffusers pipeline. First run may download model files into the Hugging Face cache.")
        pipe = AutoPipelineForText2Image.from_pretrained(model_id, torch_dtype=dtype, safety_checker=None)
        if torch.cuda.is_available():
            set_stage("Moving pipeline to CUDA.")
            pipe = pipe.to("cuda")
            pipe.enable_attention_slicing()
        else:
            pipe.enable_attention_slicing()
        try:
            pipe.enable_model_cpu_offload()
        except Exception:
            pass
        steps = 8 if "LCM" in model_id or "turbo" in model_id.lower() else 24
        set_stage("Generating image.")
        image = pipe(prompt, num_inference_steps=steps, guidance_scale=0.0 if "turbo" in model_id.lower() else 7.0).images[0]
    set_stage("Saving image.")
    image.save(output_path)
    print("Generated image.", flush=True)
finally:
    stop_heartbeat = True
    thread.join(timeout=1)
    watchdog_thread.join(timeout=1)
""";

        progress?.Report(new LocalLlmGenerationProgress("Preparing Diffusers image generation...", null, null, null, null, null, null, null, false));
        terminal?.Report($"> {Path.GetFileName(python)} -c <diffusers> {modelId}");
        terminal?.Report(ResolveHuggingFaceTokenStatus(null));
        terminal?.Report($"Prompt: {SummarizeImagePrompt(prompt)}");
        var result = await RunProcessStreamingAsync(
            python,
            ["-c", script, modelId, prompt.Trim(), outputPath],
            ResolveDiffusersGenerationTimeout(modelId),
            chunk =>
            {
                var clean = CleanProgressText(chunk);
                if (!string.IsNullOrWhiteSpace(clean))
                {
                    terminal?.Report(clean);
                }

                progress?.Report(new LocalLlmGenerationProgress("Generating image with Diffusers...", null, null, null, null, null, null, null, false));
            },
            cancellationToken,
            resolvedOutputDirectory,
            pythonEnvironment).ConfigureAwait(false);

        var images = FindGeneratedImageFiles(resolvedOutputDirectory, before, startedUtc);
        progress?.Report(new LocalLlmGenerationProgress("Image generation complete.", null, null, null, null, null, null, null, true));
        if (!result.Started)
        {
            return new LocalLlmImageGenerationResult(false, "Python could not be started.", [], resolvedOutputDirectory);
        }

        if (result.ExitCode != 0)
        {
            var reason = FirstFailureLine(result.StandardError) ?? FirstFailureLine(result.StandardOutput) ?? $"Diffusers exited {result.ExitCode}.";
            reason = BuildDiffusersFailureMessage(modelId, reason, result.StandardError, result.StandardOutput);
            return new LocalLlmImageGenerationResult(false, reason, images, resolvedOutputDirectory, result.StandardOutput);
        }

        return images.Count == 0
            ? new LocalLlmImageGenerationResult(false, "Diffusers finished, but no generated image file was detected.", [], resolvedOutputDirectory, result.StandardOutput)
            : new LocalLlmImageGenerationResult(true, $"Generated {images.Count:N0} image(s) with {modelId}.", images, resolvedOutputDirectory, result.StandardOutput);
    }

    private static readonly PythonModuleRequirement[] DiffusersPythonRequirements =
    [
        new("torch", "torch"),
        new("diffusers", "diffusers"),
        new("huggingface-hub", "huggingface_hub"),
        new("transformers", "transformers"),
        new("accelerate", "accelerate"),
        new("safetensors", "safetensors"),
        new("Pillow", "PIL")
    ];

    private static async Task<PythonExecutableResolution> ResolvePythonForModulesAsync(
        IReadOnlyList<PythonModuleRequirement> requirements,
        CancellationToken cancellationToken,
        TimeSpan? validationTimeout = null,
        bool requireFlux2KleinPipeline = false)
    {
        var candidates = PythonDependencyEnvironment
            .FindPythonCandidatesForDetection("diffusers", includePathCandidates: false)
            .Where(candidate => candidate.IsManaged)
            .ToArray();
        if (candidates.Length == 0)
        {
            return PythonExecutableResolution.Failed("Hugging Face Diffusers is not installed in ContextControl's managed venv. Install it from Dependencies before using Diffusers image models.");
        }

        var failures = new List<string>();
        var packagesToModules = requirements.ToDictionary(
            requirement => requirement.DisplayName,
            requirement => requirement.ModuleName,
            StringComparer.OrdinalIgnoreCase);
        var script = requireFlux2KleinPipeline
            ? PythonDependencyEnvironment.BuildFlux2KleinHealthScript(packagesToModules)
            : PythonDependencyEnvironment.BuildPythonDependencyHealthScript("diffusers", packagesToModules);
        var timeout = validationTimeout ?? DiffusersPythonProbeValidationTimeout;
        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await RunProcessAsync(
                candidate.Executable,
                ["-c", script],
                timeout,
                cancellationToken,
                candidate.IsManaged ? PythonDependencyEnvironment.ManagedProcessEnvironment : null).ConfigureAwait(false);

            if (!result.Started)
            {
                failures.Add($"{candidate.Executable}: could not be started");
                continue;
            }

            if (result.ExitCode == 0)
            {
                return PythonExecutableResolution.Ready(candidate.Executable, FirstLine(result.StandardOutput) ?? candidate.Executable, candidate.IsManaged);
            }

            var reason = result.TimedOut
                ? $"timed out after {timeout.TotalSeconds:0}s while validating the ContextControl-managed Diffusers runtime. Reinstall Hugging Face Diffusers from Dependencies to repair the managed venv."
                : FirstFailureLine(result.StandardError) ?? FirstFailureLine(result.StandardOutput) ?? $"exited {result.ExitCode}";
            failures.Add($"{candidate.Executable}: {reason}");
        }

        var firstFailure = failures.FirstOrDefault();
        var runtimeName = requireFlux2KleinPipeline
            ? "ContextControl's managed Diffusers runtime for FLUX.2 Klein"
            : "ContextControl's managed Diffusers runtime";
        var status = firstFailure is null
            ? $"{runtimeName} could not be validated."
            : $"{runtimeName} is not ready. First failure: {firstFailure}";
        if (candidates.Any(candidate => candidate.IsManaged))
        {
            PythonDependencyEnvironment.ClearManagedReady("diffusers");
        }

        return PythonExecutableResolution.Failed(status);
    }

    private static async Task<LocalLlmImageGenerationResult> GenerateImageWithStableDiffusionCppAsync(
        string modelId,
        string prompt,
        string resolvedOutputDirectory,
        IProgress<LocalLlmGenerationProgress>? progress,
        IProgress<string>? terminal,
        CancellationToken cancellationToken)
    {
        var executableNames = new[] { "sd.exe", "sd", "stable-diffusion-cli.exe", "stable-diffusion-cli" };
        var executable = NativeDependencyEnvironment.FindManagedExecutable("stable_diffusion_cpp", executableNames)
            ?? FindExecutableOnPath("sd.exe")
            ?? FindExecutableOnPath("sd")
            ?? FindExecutableOnPath("stable-diffusion-cli.exe")
            ?? FindExecutableOnPath("stable-diffusion-cli");
        if (executable is null)
        {
            return new LocalLlmImageGenerationResult(false, "stable-diffusion.cpp was not found in ContextControl's managed native store or on PATH. Install it and run with a compatible GGUF model file.", [], resolvedOutputDirectory);
        }

        var modelPath = Environment.GetEnvironmentVariable("CC_IMAGE_MODEL_PATH");
        if (string.IsNullOrWhiteSpace(modelPath) || !File.Exists(modelPath))
        {
            return new LocalLlmImageGenerationResult(false, "Set CC_IMAGE_MODEL_PATH to the local GGUF diffusion model file before using this quantized image route.", [], resolvedOutputDirectory);
        }

        var before = SnapshotImageFiles(resolvedOutputDirectory);
        var startedUtc = DateTime.UtcNow.AddSeconds(-2);
        var outputPath = Path.Combine(resolvedOutputDirectory, $"cc-image-{DateTime.Now:yyyyMMdd-HHmmss}.png");
        progress?.Report(new LocalLlmGenerationProgress("Preparing stable-diffusion.cpp generation...", null, null, null, null, null, null, null, false));
        terminal?.Report($"> {Path.GetFileName(executable)} -m {modelPath} -p \"{prompt.Trim()}\"");
        var result = await RunProcessStreamingAsync(
            executable,
            ["-m", modelPath, "-p", prompt.Trim(), "-o", outputPath],
            TimeSpan.FromMinutes(45),
            chunk =>
            {
                var clean = CleanProgressText(chunk);
                if (!string.IsNullOrWhiteSpace(clean))
                {
                    terminal?.Report(clean);
                }

                progress?.Report(new LocalLlmGenerationProgress("Generating image with stable-diffusion.cpp...", null, null, null, null, null, null, null, false));
            },
            cancellationToken,
            resolvedOutputDirectory).ConfigureAwait(false);

        var images = FindGeneratedImageFiles(resolvedOutputDirectory, before, startedUtc);
        progress?.Report(new LocalLlmGenerationProgress("Image generation complete.", null, null, null, null, null, null, null, true));
        if (!result.Started)
        {
            return new LocalLlmImageGenerationResult(false, "stable-diffusion.cpp could not be started.", [], resolvedOutputDirectory);
        }

        if (result.ExitCode != 0)
        {
            var reason = FirstFailureLine(result.StandardError) ?? FirstFailureLine(result.StandardOutput) ?? $"stable-diffusion.cpp exited {result.ExitCode}.";
            return new LocalLlmImageGenerationResult(false, reason, images, resolvedOutputDirectory, result.StandardOutput);
        }

        return images.Count == 0
            ? new LocalLlmImageGenerationResult(false, "stable-diffusion.cpp finished, but no generated image file was detected.", [], resolvedOutputDirectory, result.StandardOutput)
            : new LocalLlmImageGenerationResult(true, $"Generated {images.Count:N0} image(s) with {modelId}.", images, resolvedOutputDirectory, result.StandardOutput);
    }

    private static bool IsDiffusersImageModel(string modelId)
    {
        return modelId.Contains('/', StringComparison.Ordinal) && !modelId.StartsWith("x/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsOllamaExperimentalImageModel(string modelId)
    {
        return modelId.StartsWith("x/flux2-klein", StringComparison.OrdinalIgnoreCase)
            || modelId.StartsWith("x/z-image", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsFlux2KleinDiffusersModel(string modelId)
    {
        return modelId.StartsWith("black-forest-labs/FLUX.2-klein", StringComparison.OrdinalIgnoreCase);
    }

    private static TimeSpan ResolveDiffusersDownloadTimeout(string modelId)
    {
        return ResolveTimeoutFromEnvironment(
            "CC_DIFFUSERS_DOWNLOAD_TIMEOUT_MINUTES",
            IsFlux2KleinDiffusersModel(modelId) ? 360 : 180);
    }

    private static TimeSpan ResolveDiffusersGenerationTimeout(string modelId)
    {
        return ResolveTimeoutFromEnvironment(
            "CC_DIFFUSERS_TIMEOUT_MINUTES",
            IsFlux2KleinDiffusersModel(modelId) ? 240 : 120);
    }

    private static TimeSpan ResolveTimeoutFromEnvironment(string name, int defaultMinutes)
    {
        var raw = Environment.GetEnvironmentVariable(name);
        if (int.TryParse(raw, out var minutes) && minutes > 0)
        {
            return TimeSpan.FromMinutes(Math.Clamp(minutes, 5, 24 * 60));
        }

        return TimeSpan.FromMinutes(defaultMinutes);
    }

    private static string SummarizeImagePrompt(string prompt)
    {
        var clean = string.Join(
                " ",
                (prompt ?? "")
                    .Replace("\r\n", "\n", StringComparison.Ordinal)
                    .Replace('\r', '\n')
                    .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            .Trim();
        return clean.Length <= 160 ? clean : clean[..160] + "...";
    }

    private static string BuildDiffusersFailureMessage(
        string modelId,
        string reason,
        params string?[] outputs)
    {
        var text = string.Join("\n", outputs.Where(output => !string.IsNullOrWhiteSpace(output)));
        var lower = text.ToLowerInvariant();
        if (lower.Contains("cuda out of memory", StringComparison.Ordinal)
            || lower.Contains("outofmemoryerror", StringComparison.Ordinal)
            || lower.Contains("out of memory", StringComparison.Ordinal))
        {
            return IsFlux2KleinDiffusersModel(modelId)
                ? $"{modelId} ran out of GPU memory in Diffusers. FLUX.2 Klein 4B can run on Windows through Diffusers, but 8 GB VRAM usually needs CPU offload and a smaller output size. Close other GPU apps and try CC_IMAGE_WIDTH=512 and CC_IMAGE_HEIGHT=512, or use a smaller SD/LCM image model."
                : $"{modelId} ran out of GPU memory in Diffusers. Close other GPU apps, try a smaller image model, or lower CC_IMAGE_WIDTH and CC_IMAGE_HEIGHT.";
        }

        if (IsFlux2KleinDiffusersModel(modelId)
            && (lower.Contains("cannot import name 'flux2kleinpipeline'", StringComparison.Ordinal)
                || lower.Contains("importerror", StringComparison.Ordinal) && lower.Contains("flux2kleinpipeline", StringComparison.Ordinal)
                || lower.Contains("attributeerror", StringComparison.Ordinal) && lower.Contains("flux2kleinpipeline", StringComparison.Ordinal)))
        {
            return "The installed Diffusers package does not include Flux2KleinPipeline yet. Reinstall Hugging Face Diffusers from ContextControl Dependencies so the managed venv upgrades to a Klein-capable Diffusers version, then download the FLUX.2 Klein Diffusers model again.";
        }

        return reason;
    }

    private static string BuildDiffusersRuntimeFailureMessage(string reason)
    {
        var clean = string.IsNullOrWhiteSpace(reason) ? "Diffusers runtime validation failed." : reason.Trim();
        var lower = clean.ToLowerInvariant();
        var looksLikeTorchDllFailure =
            lower.Contains("winerror 1114", StringComparison.Ordinal)
            || lower.Contains("c10.dll", StringComparison.Ordinal)
            || lower.Contains("dynamic link library", StringComparison.Ordinal)
            || (lower.Contains("torch", StringComparison.Ordinal) && lower.Contains("dll", StringComparison.Ordinal));

        if (!looksLikeTorchDllFailure)
        {
            return clean;
        }

        var managedDirectory = PythonDependencyEnvironment.ManagedDependencyDirectory("diffusers");
        return $"{clean} Repair Hugging Face Diffusers from Dependencies. ContextControl will delete only its managed Diffusers venv at {managedDirectory} and reinstall it; if the repair still fails, update the Microsoft Visual C++ Redistributable and NVIDIA driver, then repair again.";
    }

    private static bool IsOllamaExperimentalImageGenerationSupported()
    {
        return OperatingSystem.IsMacOS();
    }

    private static string BuildUnsupportedOllamaImageMessage(string modelId)
    {
        return $"{modelId} uses Ollama's experimental image-generation route, which currently works only on macOS. On Windows/Linux, Ollama can pull the model but generation can fail with HTTP 500/EOF. Use a Diffusers model such as Tiny Stable Diffusion, BK-SDM Small, LCM DreamShaper, or SD Turbo on this PC.";
    }

    private static string BuildOllamaHttpEofImageFailureMessage(string modelId)
    {
        return IsOllamaExperimentalImageModel(modelId)
            ? $"{modelId} reached Ollama's image-generation backend, but Ollama closed the request with HTTP 500/EOF. This usually means the experimental Ollama image route is unsupported on this OS or this model size is not runnable on the current hardware. Try a Diffusers model on Windows/Linux, or update Ollama and use a smaller image model on macOS."
            : "Ollama closed the image-generation request with HTTP 500/EOF before an image was written. Try a supported image route, update Ollama, or use a smaller model so the backend does not terminate mid-request.";
    }

    private static bool LooksLikeHttpEofBackendFailure(params string?[] outputs)
    {
        var text = string.Join("\n", outputs.Where(output => !string.IsNullOrWhiteSpace(output))).ToLowerInvariant();
        return text.Contains("eof", StringComparison.Ordinal)
            && (text.Contains("500", StringComparison.Ordinal)
                || text.Contains("internal server error", StringComparison.Ordinal)
                || text.Contains("/api/generate", StringComparison.Ordinal)
                || text.Contains("/completion", StringComparison.Ordinal)
                || text.Contains("post http", StringComparison.Ordinal));
    }

    private static bool IsStableDiffusionCppImageModel(string modelId)
    {
        return modelId.Contains("flux1", StringComparison.OrdinalIgnoreCase)
            || modelId.Contains("gguf", StringComparison.OrdinalIgnoreCase);
    }

    private static Dictionary<string, DateTime> SnapshotImageFiles(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        }

        return Directory.EnumerateFiles(directory)
            .Where(path => ImageOutputExtensions.Contains(Path.GetExtension(path)))
            .ToDictionary(path => path, path => File.GetLastWriteTimeUtc(path), StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> FindGeneratedImageFiles(
        string directory,
        IReadOnlyDictionary<string, DateTime> before,
        DateTime startedUtc)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return [];
        }

        return Directory.EnumerateFiles(directory)
            .Where(path => ImageOutputExtensions.Contains(Path.GetExtension(path)))
            .Select(path => new FileInfo(path))
            .Where(file =>
                !before.TryGetValue(file.FullName, out var previousWriteUtc)
                    ? file.LastWriteTimeUtc >= startedUtc
                    : file.LastWriteTimeUtc > previousWriteUtc)
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .Select(file => file.FullName)
            .ToArray();
    }

    private sealed record PythonModuleRequirement(string DisplayName, string ModuleName);

    private sealed record PythonExecutableResolution(bool Succeeded, string? Executable, string Status, bool IsManaged = false)
    {
        public static PythonExecutableResolution Ready(string executable, string status, bool isManaged)
        {
            return new PythonExecutableResolution(true, executable, status, isManaged);
        }

        public static PythonExecutableResolution Failed(string status)
        {
            return new PythonExecutableResolution(false, null, status);
        }
    }

}
