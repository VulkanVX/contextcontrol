using System.Collections.ObjectModel;
using ContextControl.Workbench.Services;

namespace ContextControl.Workbench.ViewModels;

public sealed partial class ContextControlViewModel
{
    public ObservableCollection<LocalRuntimeProfileViewModel> LocalRuntimeProfiles { get; } = [];
    public RelayCommand<object> ApplyRuntimeConnectionsCommand { get; private set; } = null!;
    private readonly ManagedLocalRuntimeService _managedRuntimes = new();
    private readonly Dictionary<string, CancellationTokenSource> _runtimeOperations = new();

    private void InitializeRuntimeProfiles()
    {
        foreach (var profile in _settings.LocalRuntimeProfiles.Where(profile => profile is not null))
        {
            var vm = new LocalRuntimeProfileViewModel(profile, (vm, action) => _ = ManageRuntimeAsync(vm, action));
            vm.SetResourceMode(_settings.LocalResources);
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(LocalRuntimeProfileViewModel.AdaptToHardware) or nameof(LocalRuntimeProfileViewModel.CpuThreads)
                    or nameof(LocalRuntimeProfileViewModel.GpuLayers) or nameof(LocalRuntimeProfileViewModel.ContextTokens))
                {
                    _settings.LocalRuntimeProfiles = LocalRuntimeProfiles.Select(p => p.ToProfile()).ToArray();
                    SaveSettingsQuietly();
                }
            };
            LocalRuntimeProfiles.Add(vm);
        }
        _localLlmService.ConfigureResources(_settings.LocalResources, _resourceHardware);
        _localLlmService.ConfigureRuntimes(_settings.LocalRuntimeProfiles);
        ApplyRuntimeConnectionsCommand = new RelayCommand<object>(_ => _ = ApplyRuntimeConnectionsAsync());
    }

    private async Task ManageRuntimeAsync(LocalRuntimeProfileViewModel vm, string action)
    {
        if (action == "stop")
        {
            if (_runtimeOperations.TryGetValue(vm.Id, out var operation)) operation.Cancel();
            _managedRuntimes.Stop(vm.Id);
            vm.Status = "Stopped";
            await RefreshLocalModelsAsync(LocalModelRefreshDepth.Fast);
            return;
        }
        if (vm.IsBusy) return;
        vm.IsBusy = true;
        using var cancellation = new CancellationTokenSource();
        _runtimeOperations[vm.Id] = cancellation;
        try
        {
            var status = new Progress<string>(value => vm.Status = value);
            if (action == "preview")
            {
                var profile = vm.ToProfile();
                vm.Status = "Estimating allocation…";
                var hardware = await LocalLlmService.DetectHardwareAsync(cancellation.Token);
                var adapted = await Task.Run(() => ManagedLocalRuntimeService.AdaptProfile(profile, _settings.LocalResources, hardware), cancellation.Token);
                vm.AdaptationSummary = adapted.Plan is { } plan ? $"{plan.Label} · {plan.GpuLayers} GPU layers · " + plan.Detail
                    : "Manual settings: " + profile.ContextTokens + " context, " + profile.GpuLayers + " GPU layers, " + profile.CpuThreads + " threads (0 = runtime default).";
                vm.Status = "Preview ready";
            }
            else if (action == "install")
            {
                if (_managedRuntimes.Owns(vm.Id)) { vm.Status = "Stop this runtime before updating it."; return; }
                var transfer = new Progress<LocalLlmTransferProgress>(value => vm.Status = value.Status);
                if (vm.Id == "transformers" && PythonDependencyEnvironment.TryGetSpec("transformers", out var python))
                    vm.Status = (await InstallPythonPackagesAsync(python, CreateTerminalProgress(), transfer, cancellation.Token)).Status;
                else if (NativeDependencyEnvironment.TryGetSpec(vm.Id == "llama-cpp" ? "llama_cpp_server" : vm.Id, out var native))
                    vm.Status = (await NativeDependencyEnvironment.InstallLatestReleaseAsync(native, CreateTerminalProgress(), transfer, cancellation.Token)).Status;
            }
            else
            {
                vm.Enabled = true;
                vm.Status = "Starting runtime…";
                var result = await _managedRuntimes.StartAsync(vm.ToProfile(), status, cancellation.Token, _settings.LocalResources);
                await ApplyRuntimeConnectionsAsync();
                vm.Status = result;
                if (_managedRuntimes.LastPlan is { } plan) vm.AdaptationSummary = plan.Detail;
            }
        }
        catch (OperationCanceledException) { vm.Status = "Stopped"; }
        catch (Exception ex) { vm.Status = ex.Message; }
        finally { _runtimeOperations.Remove(vm.Id); vm.IsBusy = false; }
    }

    private async Task ApplyRuntimeConnectionsAsync()
    {
        if (IsRefreshingLocalModels) return;
        try
        {
            var profiles = LocalRuntimeProfiles.Select(profile => profile.ToProfile()).ToArray();
            foreach (var profile in profiles.Where(profile => profile.Enabled)) _ = profile.ApiUri("models");
            _settings.LocalRuntimeProfiles = profiles;
            // Keep the actual running context even if the user edits settings for the next start.
            _localLlmService.ConfigureRuntimes(profiles.Select(profile => _managedRuntimes.Owns(profile.Id) ? ManagedLocalRuntimeService.ConnectionProfile(profile, _managedRuntimes.ActiveProfile) : profile).ToArray());
            SaveSettingsQuietly();
            await RefreshLocalModelsAsync(LocalModelRefreshDepth.Fast);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            LocalLlmStatus = ex.Message;
        }
    }
}
