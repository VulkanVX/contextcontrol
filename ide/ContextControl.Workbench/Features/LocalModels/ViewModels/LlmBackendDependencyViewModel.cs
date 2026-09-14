// CC-DESC: Describes a local/edge LLM backend dependency and its runtime status.

using Avalonia.Media.Imaging;
using Avalonia.Platform;
using ContextControl.Workbench.Services;

namespace ContextControl.Workbench.ViewModels;

public sealed class LlmBackendDependencyViewModel(
    string id,
    string displayName,
    string category,
    string apiStyle,
    string platforms,
    string purpose,
    string installHint,
    bool isRequired,
    bool isRecommended) : ObservableObject
{
    private const string DepIconBase = "avares://ContextControl.Workbench/Assets/DepIcons/";
    private static readonly Dictionary<string, Bitmap> IconCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object IconCacheGate = new();
    private static readonly Dictionary<string, (DateTime LastWriteUtc, long Bytes)> SizeCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object SizeCacheGate = new();
    private bool _isReady;
    private bool _isManaged;
    private string _statusLabel = isRequired ? "Required" : "Optional";
    private string _detailLabel = installHint;
    private string _sizeLabel = "checking";
    private Bitmap? _iconImage;
    private bool? _iconHasTransparentBackground;

    public string Id { get; } = id;
    public string DisplayName { get; } = displayName;
    public string Category { get; } = category;
    public string ApiStyle { get; } = apiStyle;
    public string Platforms { get; } = platforms;
    public string Purpose { get; } = purpose;
    public string InstallHint { get; } = installHint;
    public string SizeLabel
    {
        get => _sizeLabel;
        private set => SetProperty(ref _sizeLabel, value);
    }
    public bool IsRequired { get; } = isRequired;
    public bool IsRecommended { get; } = isRecommended;
    public string InstallUrl => ResolveInstallUrl(Id);
    public string IconSource => ResolveIconSource(Id);
    public bool IconHasTransparentBackground
    {
        get
        {
            if (_iconHasTransparentBackground is { } cached)
            {
                return cached;
            }

            var value = IconTransparency.HasTransparentBackground(IconSource);
            _iconHasTransparentBackground = value;
            return value;
        }
    }

    public Bitmap? IconImage => _iconImage ??= LoadIcon(IconSource);
    public string? PlatformLimitation => PythonDependencyEnvironment.PlatformLimitation(Id);
    public bool HasSafeAutomaticInstaller => PlatformLimitation is null && (
        PythonDependencyEnvironment.HasManagedInstaller(Id)
        || NativeDependencyEnvironment.HasManagedInstaller(Id)
        || PackageManagerDependencyEnvironment.HasManagedInstaller(Id)
        || SourceDependencyEnvironment.HasManagedInstaller(Id));
    public bool CanRepairManaged => !IsReady && PythonDependencyEnvironment.HasManagedDependency(Id);
    public string InstallActionLabel => PlatformLimitation is not null ? "Other platform" : IsReady
        ? CanUninstall ? "Uninstall" : CanForceInstall ? "Force install" : "External"
        : CanRepairManaged ? "Repair"
        : HasSafeAutomaticInstaller ? "Install" : "Manual";
    public bool CanInstall => !IsReady;
    public bool CanUninstall => IsReady && IsManaged && !Id.Equals("ollama", StringComparison.OrdinalIgnoreCase);
    public bool CanForceInstall => false;

    public bool IsReady
    {
        get => _isReady;
        private set => SetProperty(ref _isReady, value);
    }

    public bool IsManaged
    {
        get => _isManaged;
        private set => SetProperty(ref _isManaged, value);
    }

    public string StatusLabel
    {
        get => _statusLabel;
        private set => SetProperty(ref _statusLabel, value);
    }

    public string DetailLabel
    {
        get => _detailLabel;
        private set => SetProperty(ref _detailLabel, value);
    }

    public string PriorityLabel => IsRequired ? "Required" : IsRecommended ? "Recommended" : "Optional";

    public void ApplyStatus(bool isReady, string statusLabel, string detailLabel, bool isManaged = false)
    {
        IsReady = isReady;
        IsManaged = isReady && isManaged;
        StatusLabel = string.IsNullOrWhiteSpace(statusLabel) ? (isReady ? "Ready" : "Not detected") : statusLabel.Trim();
        DetailLabel = string.IsNullOrWhiteSpace(detailLabel) ? InstallHint : detailLabel.Trim();
        SizeLabel = ResolveSizeLabel(Id, IsReady, IsManaged);
        OnPropertyChanged(nameof(InstallActionLabel));
        OnPropertyChanged(nameof(CanInstall));
        OnPropertyChanged(nameof(CanUninstall));
        OnPropertyChanged(nameof(CanForceInstall));
        OnPropertyChanged(nameof(CanRepairManaged));
    }

    private static string ResolveInstallUrl(string id)
    {
        return (id ?? "").Trim() switch
        {
            "ollama" => "https://ollama.com/download",
            "llama_cpp_server" => "https://github.com/ggml-org/llama.cpp",
            "lm_studio" => "https://lmstudio.ai/download",
            "koboldcpp" => "https://github.com/LostRuins/koboldcpp/releases",
            "mlx_lm" => "https://github.com/ml-explore/mlx-lm",
            "mlc_llm" => "https://llm.mlc.ai/docs/install/mlc_llm.html",
            "transformers" => "https://huggingface.co/docs/transformers/installation",
            "diffusers" => "https://huggingface.co/docs/diffusers/installation",
            "stable_diffusion_cpp" => "https://github.com/leejet/stable-diffusion.cpp",
            "vllm" => "https://docs.vllm.ai/en/latest/getting_started/installation.html",
            "sglang" => "https://docs.sglang.ai/start/install.html",
            "onnxruntime_genai" => "https://onnxruntime.ai/docs/genai/",
            "openvino_genai" => "https://docs.openvino.ai/latest/openvino-workflow-generative.html",
            "tensorrt_llm" => "https://nvidia.github.io/TensorRT-LLM/installation/",
            "exllamav2_tabbyapi" => "https://github.com/theroyallab/tabbyAPI",
            "bitnet_cpp" => "https://github.com/microsoft/BitNet",
            "rwkv_runner" => "https://github.com/josStorer/RWKV-Runner",
            _ => ""
        };
    }

    private static string ResolveSizeLabel(string id, bool isReady, bool isManaged)
    {
        if ((isManaged || !isReady) && TryResolveExactManagedSizeLabel(id, out var exactLabel))
        {
            return exactLabel;
        }

        if (isReady)
        {
            return isManaged ? "installed" : "external app";
        }

        return "not installed";
    }

    private static bool TryResolveExactManagedSizeLabel(string id, out string label)
    {
        label = "";
        foreach (var directory in ResolveManagedDependencyDirectories(id))
        {
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                continue;
            }

            var bytes = CalculateDirectorySize(directory);
            if (bytes <= 0)
            {
                continue;
            }

            label = FormatBytes(bytes);
            return true;
        }

        return false;
    }

    private static IEnumerable<string> ResolveManagedDependencyDirectories(string id)
    {
        if (PythonDependencyEnvironment.HasManagedInstaller(id))
        {
            yield return PythonDependencyEnvironment.ManagedDependencyDirectory(id);
        }

        if (NativeDependencyEnvironment.HasManagedInstaller(id))
        {
            yield return NativeDependencyEnvironment.ManagedDependencyDirectory(id);
        }

        if (SourceDependencyEnvironment.HasManagedInstaller(id))
        {
            yield return SourceDependencyEnvironment.ManagedDependencyDirectory(id);
        }
    }

    private static long CalculateDirectorySize(string directory)
    {
        DateTime lastWriteUtc;
        try
        {
            lastWriteUtc = Directory.GetLastWriteTimeUtc(directory);
        }
        catch
        {
            lastWriteUtc = DateTime.MinValue;
        }

        lock (SizeCacheGate)
        {
            if (SizeCache.TryGetValue(directory, out var cached)
                && cached.LastWriteUtc == lastWriteUtc)
            {
                return cached.Bytes;
            }
        }

        long total = 0;
        var pending = new Stack<string>();
        pending.Push(directory);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            try
            {
                foreach (var file in Directory.EnumerateFiles(current))
                {
                    try
                    {
                        total += new FileInfo(file).Length;
                    }
                    catch
                    {
                        // Skip files that disappear or are inaccessible during refresh.
                    }
                }
            }
            catch
            {
                // Skip directories that disappear or are inaccessible during refresh.
            }

            try
            {
                foreach (var child in Directory.EnumerateDirectories(current))
                {
                    try
                    {
                        if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0)
                        {
                            pending.Push(child);
                        }
                    }
                    catch
                    {
                        // Ignore broken child directories.
                    }
                }
            }
            catch
            {
                // Skip directories that disappear or are inaccessible during refresh.
            }
        }

        lock (SizeCacheGate)
        {
            SizeCache[directory] = (lastWriteUtc, total);
        }

        return total;
    }

    private static string FormatBytes(long bytes)
    {
        const double kib = 1024.0;
        const double mib = kib * 1024.0;
        const double gib = mib * 1024.0;
        const double tib = gib * 1024.0;

        return bytes switch
        {
            >= (long)tib => $"{bytes / tib:0.#} TB",
            >= (long)gib => $"{bytes / gib:0.#} GB",
            >= (long)mib => $"{bytes / mib:0.#} MB",
            >= (long)kib => $"{bytes / kib:0.#} KB",
            _ => $"{bytes} B"
        };
    }

    private static string ResolveIconSource(string id)
    {
        return (id ?? "").Trim() switch
        {
            "ollama" => DepIconBase + "ollama.png",
            "llama_cpp_server" => DepIconBase + "llamacpp.png",
            "lm_studio" => DepIconBase + "lmstudio.png",
            "koboldcpp" => DepIconBase + "koboldai.png",
            "mlx_lm" => DepIconBase + "mlx.png",
            "mlc_llm" => DepIconBase + "mlc.png",
            "transformers" => DepIconBase + "huggingface.png",
            "diffusers" => DepIconBase + "huggingface.png",
            "stable_diffusion_cpp" => DepIconBase + "llamacpp.png",
            "vllm" => DepIconBase + "vllm.png",
            "sglang" => DepIconBase + "sglang.png",
            "onnxruntime_genai" => DepIconBase + "onnx.png",
            "openvino_genai" => DepIconBase + "openvino.png",
            "tensorrt_llm" => DepIconBase + "nvidia.png",
            "exllamav2_tabbyapi" => DepIconBase + "nvidia.png",
            "bitnet_cpp" => DepIconBase + "microsoft.png",
            "rwkv_runner" => DepIconBase + "rwkv.png",
            _ => DepIconBase + "ollama.png"
        };
    }

    private static Bitmap? LoadIcon(string iconSource)
    {
        if (string.IsNullOrWhiteSpace(iconSource))
        {
            return null;
        }

        lock (IconCacheGate)
        {
            if (IconCache.TryGetValue(iconSource, out var cached))
            {
                return cached;
            }

            try
            {
                using var stream = AssetLoader.Open(new Uri(iconSource));
                var bitmap = new Bitmap(stream);
                IconCache[iconSource] = bitmap;
                return bitmap;
            }
            catch
            {
                return null;
            }
        }
    }
}
