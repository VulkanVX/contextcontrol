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
            LocalRuntimeProfiles.Add(new(profile, (vm, action) => _ = ManageRuntimeAsync(vm, action)));
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
            if (action == "install")
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
                var result = await _managedRuntimes.StartAsync(vm.ToProfile(), status, cancellation.Token);
                await ApplyRuntimeConnectionsAsync();
                vm.Status = result;
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
            _localLlmService.ConfigureRuntimes(profiles);
            SaveSettingsQuietly();
            await RefreshLocalModelsAsync(LocalModelRefreshDepth.Fast);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            LocalLlmStatus = ex.Message;
        }
    }
}
