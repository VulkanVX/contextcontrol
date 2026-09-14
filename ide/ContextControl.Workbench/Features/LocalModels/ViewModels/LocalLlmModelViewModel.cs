// CC-DESC: Presents a local Ollama model candidate with fit, install, and pull state.

using System.Globalization;
using System.Text.RegularExpressions;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using ContextControl.Workbench.Services;

namespace ContextControl.Workbench.ViewModels;

public sealed partial class LocalLlmModelViewModel(LocalLlmCatalogModel model) : ObservableObject
{
    private const string ModelIconBase = "avares://ContextControl.Workbench/Assets/ModelIcons/";
    private static readonly object IconCacheGate = new();
    private static readonly Dictionary<string, Bitmap> IconCache = new(StringComparer.OrdinalIgnoreCase);
    private bool _isInstalled;
    private bool _isAvailable;
    private bool _isRecommended;
    private bool _isPulling;
    private bool _isBackendDependencyReady;
    private bool _isBackendModelReady;
    private bool _hasDetectedThinking;
    private bool _hasNativeThinkingCapability;
    private bool _thinkingUnsupported;
    private string _storageLocationLabel = "";
    private string _detectedDownloadSizeLabel = "";
    private double? _detectedGpuVramGiB;
    private readonly string _provider = ResolveProvider(model.Id, model.DisplayName);
    private readonly DateTime? _releaseDateValue = ParseReleaseDate(model.ReleaseDate);
    private readonly string _iconSource = !string.IsNullOrWhiteSpace(model.IconSource)
        ? model.IconSource
        : ResolveProviderIcon(model.Id, model.DisplayName);
    private readonly int _advertisedContextTokens = ContextCapsuleBuilder.EstimateContextTokens(model.AdvertisedContext, 0);
    private readonly bool _isCloudModel = IsCloudModelId(model.Id);
    private readonly bool _supportsNativeThinkingById = SupportsNativeThinkingById(model.Id);
    private readonly bool _supportsThinkingOutputById = SupportsThinkingOutputById(model.Id);
    private readonly string _modelBaseLabel = ResolveModelBaseLabel(model);
    private readonly string _modelBaseDetail = ResolveModelBaseDetail(model);
    private readonly string _backendRequirementLabel = ResolveBackendRequirementLabel(model);
    private readonly string _backendRequirementDetail = ResolveBackendRequirementDetail(model);
    private readonly bool _isImageModel = DetectImageModel(model);
    private readonly bool _isImageGenerationModel = DetectImageGenerationModel(model);
    private readonly bool _isOllamaChatBlocked = DetectOllamaChatBlocked(model);
    private readonly IReadOnlyList<string> _purposeTags = ResolvePurposeTags(model);
    private Bitmap? _iconImage;
    private bool _iconImageLoaded;
    private bool? _iconHasTransparentBackground;
    private string _fitLabel = "Detecting fit";
    private string _fitDetail = "";

    public LocalLlmCatalogModel Model { get; } = model;
    public string Id => Model.Id;
    public string DisplayName => Model.DisplayName;
    public string DisplayNameWithThinking => SupportsThinking ? $"{DisplayName} (Thinking)" : DisplayName;
    public string Provider => _provider;
    public string ReleaseDate => Model.ReleaseDate;
    public DateTime? ReleaseDateValue => _releaseDateValue;
    public string IconSource => _iconSource;
    public bool HasIcon => !string.IsNullOrWhiteSpace(_iconSource);
    public bool IconHasTransparentBackground
    {
        get
        {
            if (_iconHasTransparentBackground is { } cached)
            {
                return cached;
            }

            var value = IconTransparency.HasTransparentBackground(_iconSource);
            _iconHasTransparentBackground = value;
            return value;
        }
    }

    public Bitmap? IconImage
    {
        get
        {
            if (_iconImageLoaded)
            {
                return _iconImage;
            }

            _iconImage = LoadIcon(_iconSource);
            _iconImageLoaded = true;
            return _iconImage;
        }
    }

    public string DownloadSize => Model.DownloadSize;
    public string BigDownloadSizeLabel => NormalizeSizeLabel(Model.DownloadSize);
    public string EffectiveDownloadSizeLabel => string.IsNullOrWhiteSpace(_detectedDownloadSizeLabel)
        ? BigDownloadSizeLabel
        : _detectedDownloadSizeLabel;
    public string License => Model.License;
    public string MinimumRequirement => Model.MinimumRequirement;
    public string AdvertisedContext => Model.AdvertisedContext;
    public string ComfortableContext => Model.ComfortableContext;
    public int AdvertisedContextTokens => _advertisedContextTokens;
    public string SourceBudget => Model.SourceBudget;
    public string ExpectedSpeed => Model.ExpectedSpeed;
    public string PracticalUse => Model.PracticalUse;
    public string PullCommand => IsCloudModel ? $"ollama run {Id}" : Model.PullCommand;
    public double MinimumVramGiB => Model.MinimumVramGiB;
    public double RecommendedVramGiB => Model.RecommendedVramGiB;
    public double? DetectedGpuVramGiB => _detectedGpuVramGiB;
    public bool HasDetectedGpu => _detectedGpuVramGiB is > 0;
    public bool CanRunOnDetectedCpu => IsCloudModel || WorksOnCpu;
    public bool CanRunOnDetectedGpu => IsCloudModel
        || (_detectedGpuVramGiB is > 0 && _detectedGpuVramGiB.Value >= MinimumVramGiB);
    public bool MinimumVramFitsDetectedGpu => IsCloudModel
        || MinimumVramGiB <= 0
        || (_detectedGpuVramGiB is > 0 && _detectedGpuVramGiB.Value >= MinimumVramGiB);
    public bool RecommendedVramFitsDetectedGpu => IsCloudModel
        || RecommendedVramGiB <= 0
        || (_detectedGpuVramGiB is > 0 && _detectedGpuVramGiB.Value >= RecommendedVramGiB);
    public bool CanRunOnDetectedHardware => IsCloudModel || CanRunOnDetectedCpu || CanRunOnDetectedGpu;
    public string VramSummary => IsCloudModel
        ? "cloud VRAM"
        : $"{FormatVram(MinimumVramGiB)} min, {FormatVram(RecommendedVramGiB)} advised";
    public string VramRiskLabel => IsCloudModel ? "CLOUD" : FormatVram(MinimumVramGiB);
    public string VramStableLabel => IsCloudModel ? "REMOTE" : FormatVram(RecommendedVramGiB);
    public string VramBestLabel => IsCloudModel ? "REMOTE" : FormatVram(CalculateBestVramGiB(MinimumVramGiB, RecommendedVramGiB));
    public string BigVramSummaryLabel => IsCloudModel
        ? "VRAM: CLOUD"
        : $"VRAM MIN (RISK): {VramRiskLabel} STABLE: {VramStableLabel} BEST: {VramBestLabel}";
    public string ReleaseVramSummary => string.IsNullOrWhiteSpace(ReleaseDate)
        || ReleaseDate.Equals("Unknown", StringComparison.OrdinalIgnoreCase)
            ? VramSummary
            : $"{ReleaseDate} | {VramSummary}";
    public bool WorksOnCpu => Model.WorksOnCpu;
    public bool IsCloudModel => _isCloudModel;
    public bool IsImageModel => _isImageModel;
    public bool IsImageGenerationModel => _isImageGenerationModel;
    public bool IsOllamaChatBlocked => _isOllamaChatBlocked;
    public bool CanUseInLocalChat => !IsImageGenerationModel && !IsOllamaChatBlocked;
    public bool SupportsNativeThinkingRequest => !_thinkingUnsupported && (_supportsNativeThinkingById || _hasNativeThinkingCapability);
    public bool SupportsThinking => SupportsNativeThinkingRequest || _supportsThinkingOutputById || _hasDetectedThinking;
    public string ThinkingLabel => SupportsNativeThinkingRequest ? "API thinking" : SupportsThinking ? "Thinking output" : "No think tag";
    public string ModelBaseLabel => _modelBaseLabel;
    public string ModelBaseDetail => _modelBaseDetail;
    public string BackendRequirementLabel => _backendRequirementLabel;
    public string BackendRequirementDetail => _backendRequirementDetail;
    public bool IsOllamaImageRoute => BackendRequirementLabel.Equals("Ollama image", StringComparison.OrdinalIgnoreCase);
    public bool IsBackendPlatformSupported => !IsOllamaImageRoute || OperatingSystem.IsMacOS();
    public bool UsesOllamaPull => BackendRequirementLabel.StartsWith("Ollama", StringComparison.OrdinalIgnoreCase);
    public bool RequiresManualBackend => !IsCloudModel && !UsesOllamaPull;
    public string DependencyId => ResolveDependencyId(BackendRequirementLabel);
    public bool IsBackendDependencyReady
    {
        get => _isBackendDependencyReady;
        private set
        {
            if (SetProperty(ref _isBackendDependencyReady, value))
            {
                OnPropertyChanged(nameof(InstallLabel));
                OnPropertyChanged(nameof(CanInstallDependency));
                OnPropertyChanged(nameof(CanUseManualBackend));
                OnPropertyChanged(nameof(CanDownloadBackendModel));
                OnPropertyChanged(nameof(HasBackendOnlyReadyState));
                OnPropertyChanged(nameof(PullButtonLabel));
            }
        }
    }
    public bool IsBackendModelReady
    {
        get => _isBackendModelReady;
        private set
        {
            if (SetProperty(ref _isBackendModelReady, value))
            {
                OnPropertyChanged(nameof(InstallLabel));
                OnPropertyChanged(nameof(CanDownloadBackendModel));
                OnPropertyChanged(nameof(CanUseManualBackend));
                OnPropertyChanged(nameof(PullButtonLabel));
                OnPropertyChanged(nameof(HasBackendOnlyReadyState));
            }
        }
    }
    public bool CanUseManualBackend => RequiresManualBackend
        && IsBackendDependencyReady
        && IsImageGenerationModel
        && (!UsesDownloadableBackendModel || IsBackendModelReady);
    public bool CanInstallDependency => !IsInstalled && RequiresManualBackend && !IsBackendDependencyReady && !string.IsNullOrWhiteSpace(DependencyId);
    public bool UsesDownloadableBackendModel => RequiresManualBackend
        && IsImageGenerationModel
        && DependencyId.Equals("diffusers", StringComparison.OrdinalIgnoreCase);
    public bool UsesHuggingFaceHubDownload => UsesDownloadableBackendModel;
    public string HuggingFaceTokenWarning =>
        "Please add a HF access token to not get rate-limited while downloading this model. In View -> Settings -> LLMs you will find both: a tutorial and a way to add your token.";
    public string StorageLocationLabel
    {
        get => _storageLocationLabel;
        private set => SetProperty(ref _storageLocationLabel, value ?? "");
    }

    public bool CanDownloadBackendModel => !IsInstalled
        && UsesDownloadableBackendModel
        && IsBackendDependencyReady
        && !IsBackendModelReady
        && !IsPulling;
    public bool HasBackendOnlyReadyState => !IsInstalled && RequiresManualBackend && IsBackendDependencyReady && !CanUseManualBackend;
    public IReadOnlyList<string> PurposeTags => _purposeTags;
    public string PurposeTagsLabel => string.Join(", ", _purposeTags);

    public bool IsInstalled
    {
        get => _isInstalled;
        private set
        {
            if (SetProperty(ref _isInstalled, value))
            {
                OnPropertyChanged(nameof(InstallLabel));
                OnPropertyChanged(nameof(CanPull));
                OnPropertyChanged(nameof(CanUninstall));
                OnPropertyChanged(nameof(CanInstallDependency));
                OnPropertyChanged(nameof(CanDownloadBackendModel));
                OnPropertyChanged(nameof(HasBackendOnlyReadyState));
                OnPropertyChanged(nameof(PullButtonLabel));
            }
        }
    }

    public bool IsAvailable
    {
        get => _isAvailable;
        private set
        {
            if (SetProperty(ref _isAvailable, value))
            {
                OnPropertyChanged(nameof(InstallLabel));
                OnPropertyChanged(nameof(PullButtonLabel));
            }
        }
    }

    public bool IsRecommended
    {
        get => _isRecommended;
        private set => SetProperty(ref _isRecommended, value);
    }

    public bool IsPulling
    {
        get => _isPulling;
        set
        {
            if (SetProperty(ref _isPulling, value))
            {
                OnPropertyChanged(nameof(CanPull));
                OnPropertyChanged(nameof(CanUninstall));
                OnPropertyChanged(nameof(CanDownloadBackendModel));
                OnPropertyChanged(nameof(PullButtonLabel));
            }
        }
    }

    public string FitLabel
    {
        get => _fitLabel;
        private set => SetProperty(ref _fitLabel, value);
    }

    public string FitDetail
    {
        get => _fitDetail;
        private set => SetProperty(ref _fitDetail, value);
    }

    public string InstallLabel => IsCloudModel
        ? IsAvailable ? "Cloud ready" : "Cloud"
        : !IsBackendPlatformSupported && !IsInstalled ? "Mac only"
        : IsBackendModelReady ? "Model ready"
        : CanUseManualBackend ? "Backend ready"
        : HasBackendOnlyReadyState ? "Backend installed"
        : IsInstalled ? "Installed" : "Not installed";

    public bool CanPull => !IsCloudModel && !IsInstalled && !IsPulling && UsesOllamaPull && IsBackendPlatformSupported;
    public bool CanUninstall => !IsCloudModel && IsInstalled && !IsPulling;

    public string PullButtonLabel => IsCloudModel
        ? IsAvailable ? "Cloud ready" : "Cloud"
        : IsPulling ? "Working" : IsInstalled ? "Uninstall" : !IsBackendPlatformSupported ? "Mac only" : IsBackendModelReady ? "Ready" : CanDownloadBackendModel ? "Download" : CanUseManualBackend ? "Backend ready" : HasBackendOnlyReadyState ? "Need model" : CanInstallDependency ? "Install dep" : RequiresManualBackend ? "Manual" : "Download";

    public void MarkThinkingDetected()
    {
        if (_hasDetectedThinking)
        {
            return;
        }

        _hasDetectedThinking = true;
        NotifyThinkingCapabilityChanged();
    }

    public void ApplyOllamaCapabilities(IReadOnlySet<string>? capabilities)
    {
        var hasNativeThinking = capabilities?.Any(capability =>
            capability.Equals("thinking", StringComparison.OrdinalIgnoreCase)
            || capability.Equals("think", StringComparison.OrdinalIgnoreCase)) == true;
        if (_hasNativeThinkingCapability == hasNativeThinking)
        {
            return;
        }

        _hasNativeThinkingCapability = hasNativeThinking;
        if (hasNativeThinking)
        {
            _thinkingUnsupported = false;
        }

        NotifyThinkingCapabilityChanged();
    }

    public void MarkThinkingUnsupported()
    {
        if (_thinkingUnsupported)
        {
            return;
        }

        _thinkingUnsupported = true;
        _hasNativeThinkingCapability = false;
        NotifyThinkingCapabilityChanged();
    }

    private void NotifyThinkingCapabilityChanged()
    {
        OnPropertyChanged(nameof(SupportsNativeThinkingRequest));
        OnPropertyChanged(nameof(SupportsThinking));
        OnPropertyChanged(nameof(ThinkingLabel));
        OnPropertyChanged(nameof(DisplayNameWithThinking));
    }

    public void ApplyState(
        bool isInstalled,
        bool isAvailable,
        LocalLlmHardwareProfile hardware,
        bool isBackendDependencyReady = false,
        bool isBackendModelReady = false)
    {
        _detectedGpuVramGiB = hardware.MaxGpuMemoryGiB;
        IsInstalled = isInstalled;
        IsBackendDependencyReady = isBackendDependencyReady;
        IsBackendModelReady = isBackendModelReady;
        IsAvailable = RequiresManualBackend
            ? IsInstalled || CanUseManualBackend
            : isAvailable;
        var fit = IsCloudModel
            ? new ModelFit(
                isAvailable,
                isAvailable ? "Cloud ready" : "Cloud",
                isAvailable
                    ? "Uses Ollama Cloud through the local Ollama API; local VRAM is not used."
                    : "Requires Ollama to be reachable and signed in for cloud access.")
            : CalculateFit(Model, hardware);
        IsRecommended = fit.IsRecommended;
        FitLabel = fit.Label;
        FitDetail = fit.Detail;
        NotifyHardwareFitChanged();
    }

    public void ApplyStorageLocation(string? location)
    {
        StorageLocationLabel = location ?? "";
    }

    public void ApplyDetectedDownloadSize(long? bytes)
    {
        var label = bytes is > 0 ? FormatDetectedBytes(bytes.Value) : "";
        if (SetProperty(ref _detectedDownloadSizeLabel, label, nameof(EffectiveDownloadSizeLabel)))
        {
            OnPropertyChanged(nameof(BigDownloadSizeLabel));
        }
    }

    public void MarkUninstalled()
    {
        IsInstalled = false;
        IsAvailable = RequiresManualBackend ? CanUseManualBackend : false;
        ApplyDetectedDownloadSize(null);
    }

    private void NotifyHardwareFitChanged()
    {
        OnPropertyChanged(nameof(DetectedGpuVramGiB));
        OnPropertyChanged(nameof(HasDetectedGpu));
        OnPropertyChanged(nameof(CanRunOnDetectedCpu));
        OnPropertyChanged(nameof(CanRunOnDetectedGpu));
        OnPropertyChanged(nameof(MinimumVramFitsDetectedGpu));
        OnPropertyChanged(nameof(RecommendedVramFitsDetectedGpu));
        OnPropertyChanged(nameof(CanRunOnDetectedHardware));
    }

    private static double CalculateBestVramGiB(double minimum, double recommended)
    {
        if (recommended <= 0)
        {
            return 0;
        }

        var headroom = minimum > 0
            ? Math.Max(2.0, recommended - minimum)
            : 2.0;
        return Math.Ceiling((recommended + headroom) * 2.0) / 2.0;
    }

    private static string NormalizeSizeLabel(string value)
    {
        var clean = (value ?? "").Trim().TrimStart('~').Trim();
        if (string.IsNullOrWhiteSpace(clean))
        {
            return "?";
        }

        var match = Regex.Match(
            clean,
            @"(?<value>\d+(?:\.\d+)?(?:\s*-\s*\d+(?:\.\d+)?)?)\s*(?<unit>TB|GB|MB|KB)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success)
        {
            return clean;
        }

        return $"{match.Groups["value"].Value.Replace(" ", "", StringComparison.Ordinal)} {match.Groups["unit"].Value.ToUpperInvariant()}";
    }

    private static string FormatDetectedBytes(long bytes)
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

}
