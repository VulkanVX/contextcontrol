using ContextControl.Workbench.Services;

namespace ContextControl.Workbench.ViewModels;

public sealed class LocalRuntimeProfileViewModel : ObservableObject
{
    private readonly LocalRuntimeProfile _profile;
    private bool _enabled;
    private string _endpoint;
    private string _keyVariable;
    private string _status = "Not checked";
    private int _contextTokens;
    private string _modelPath;
    private int _gpuLayers;
    private bool _busy;
    public LocalRuntimeProfileViewModel(LocalRuntimeProfile profile, Action<LocalRuntimeProfileViewModel, string>? action = null)
    {
        _profile = profile;
        _enabled = profile.Enabled;
        _endpoint = profile.Endpoint ?? "";
        _keyVariable = profile.ApiKeyEnvironmentVariable ?? "";
        _contextTokens = profile.ContextTokens;
        _modelPath = profile.ModelPath ?? "";
        _gpuLayers = profile.GpuLayers;
        InstallCommand = new(_ => action?.Invoke(this, "install"), _ => !IsBusy);
        StartCommand = new(_ => action?.Invoke(this, "start"), _ => !IsBusy);
        StopCommand = new(_ => action?.Invoke(this, "stop"));
    }
    public string Id => _profile.Id;
    public string Name => _profile.Name;
    public bool CanManage => Id is "llama-cpp" or "koboldcpp" or "transformers";
    public bool SupportsGpuLayers => Id is "llama-cpp" or "koboldcpp";
    public RelayCommand<object> InstallCommand { get; }
    public RelayCommand<object> StartCommand { get; }
    public RelayCommand<object> StopCommand { get; }
    public bool IsBusy { get => _busy; set { if (SetProperty(ref _busy, value)) { InstallCommand.RaiseCanExecuteChanged(); StartCommand.RaiseCanExecuteChanged(); } } }
    public string ModelPath { get => _modelPath; set => SetProperty(ref _modelPath, value ?? ""); }
    public int GpuLayers { get => _gpuLayers; set => SetProperty(ref _gpuLayers, value); }
    public bool Enabled { get => _enabled; set => SetProperty(ref _enabled, value); }
    public string Endpoint { get => _endpoint; set => SetProperty(ref _endpoint, value); }
    public string ApiKeyEnvironmentVariable { get => _keyVariable; set => SetProperty(ref _keyVariable, value); }
    public int ContextTokens { get => _contextTokens; set => SetProperty(ref _contextTokens, value); }
    public string Status { get => _status; set => SetProperty(ref _status, value); }
    public LocalRuntimeProfile ToProfile() => _profile with { Enabled = Enabled, Endpoint = Endpoint?.Trim() ?? "",
        ApiKeyEnvironmentVariable = ApiKeyEnvironmentVariable?.Trim() ?? "", ContextTokens = Math.Clamp(ContextTokens, 1024, 1048576),
        ModelPath = ModelPath.Trim(), GpuLayers = Math.Clamp(GpuLayers, 0, 999) };
}
