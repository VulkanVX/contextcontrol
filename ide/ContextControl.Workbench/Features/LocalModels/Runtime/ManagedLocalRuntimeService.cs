using System.Diagnostics;
using System.Net.Http;
using System.Net.Sockets;
using System.Reflection;

namespace ContextControl.Workbench.Services;

/// <summary>Owns only the subprocess it starts. External servers and Ollama are never stopped here.</summary>
internal sealed class ManagedLocalRuntimeService : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1);
    private Process? _process;
    private string? _profileId;
    private string _lastOutput = "";

    public ManagedLocalRuntimeService() => AppDomain.CurrentDomain.ProcessExit += OnExit;
    private void OnExit(object? sender, EventArgs args) => StopOwned();
    public bool Owns(string id) => _profileId == id && _process is { HasExited: false };

    internal static ProcessStartInfo BuildStartInfo(LocalRuntimeProfile profile)
    {
        var endpoint = profile.ApiUri("models");
        if (endpoint.Scheme != "http" || endpoint.Host != "127.0.0.1")
            throw new InvalidOperationException("Managed servers need an http://127.0.0.1 endpoint. Other servers can be connected using Apply and connect.");
        if (profile.ContextTokens is < 1024 or > 32768) throw new InvalidOperationException("Use 1,024–32,768 context tokens for a managed server.");
        if (string.IsNullOrWhiteSpace(profile.ModelPath)) throw new InvalidOperationException("Choose a GGUF file, or enter a Transformers model folder / Hugging Face repository ID.");
        var info = new ProcessStartInfo { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        void Add(params string[] arguments) { foreach (var argument in arguments) info.ArgumentList.Add(argument); }
        var context = profile.ContextTokens.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var port = endpoint.Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var layers = Math.Clamp(profile.GpuLayers, 0, 999).ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (profile.Id is "llama-cpp" or "koboldcpp")
        {
            var path = Path.GetFullPath(profile.ModelPath.Trim());
            using (var file = File.OpenRead(path))
            {
                var magic = new byte[4];
                if (file.Read(magic, 0, 4) != 4 || !magic.SequenceEqual("GGUF"u8.ToArray())) throw new InvalidOperationException("Select a GGUF model file. Existing Ollama GGUF blobs can also be used without copying them.");
            }
            var dependency = profile.Id == "llama-cpp" ? "llama_cpp_server" : "koboldcpp";
            if (!NativeDependencyEnvironment.TryGetSpec(dependency, out var spec)) throw new InvalidOperationException("Unknown runtime.");
            info.FileName = NativeDependencyEnvironment.FindManagedExecutable(dependency, spec.ExecutableNames)
                ?? throw new InvalidOperationException("Install this runtime first.");
            if (profile.Id == "llama-cpp") Add("--model", path, "--host", "127.0.0.1", "--port", port, "--ctx-size", context, "--n-gpu-layers", layers, "--parallel", "1");
            else
            {
                Add("--model", path, "--host", "127.0.0.1", "--port", port, "--contextsize", context, "--gpulayers", layers, "--skiplauncher", "--quiet");
                if (profile.GpuLayers > 0) Add("--usevulkan");
            }
        }
        else if (profile.Id == "transformers")
        {
            info.FileName = PythonDependencyEnvironment.ManagedPythonExecutable("transformers");
            if (!File.Exists(info.FileName)) throw new InvalidOperationException("Install Transformers first.");
            var worker = Path.Combine(PythonDependencyEnvironment.ManagedDependencyDirectory("transformers"), "contextcontrol_server.py");
            using var embedded = Assembly.GetExecutingAssembly().GetManifestResourceStream("ContextControl.TransformersServer.py")
                ?? throw new InvalidOperationException("The Transformers worker is missing from this build.");
            using (var output = File.Create(worker)) embedded.CopyTo(output);
            Add("-u", worker, "--model", profile.ModelPath.Trim(), "--port", port, "--context", context);
            info.Environment["PYTHONNOUSERSITE"] = "1";
            info.Environment["HF_HUB_DISABLE_PROGRESS_BARS"] = "1";
        }
        else throw new InvalidOperationException("Start this server in its own runtime, then use Apply and connect.");
        return info;
    }

    public async Task<string> StartAsync(LocalRuntimeProfile profile, IProgress<string> progress, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_process is { HasExited: false }) return Owns(profile.Id) ? "Already running" : "Stop the other managed model first to release its memory.";
            var info = BuildStartInfo(profile);
            var endpoint = profile.ApiUri("models");
            using (var probe = new TcpClient())
            {
                try { await probe.ConnectAsync("127.0.0.1", endpoint.Port, cancellationToken).ConfigureAwait(false); }
                catch (SocketException) { }
                if (probe.Connected) return "This port is already in use. Connect to that server or choose another port.";
            }
            _process?.Dispose();
            var process = new Process { StartInfo = info };
            process.OutputDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) _lastOutput = e.Data; };
            process.ErrorDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) _lastOutput = e.Data; };
            _lastOutput = "";
            _process = process;
            _profileId = profile.Id;
            if (!process.Start()) throw new InvalidOperationException("Could not start the runtime.");
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            progress.Report("Loading model…");
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromMinutes(10));
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            while (!process.HasExited)
            {
                deadline.Token.ThrowIfCancellationRequested();
                try
                {
                    using var response = await http.GetAsync(endpoint, deadline.Token).ConfigureAwait(false);
                    if (response.IsSuccessStatusCode) return "Running · model ready";
                }
                catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException) { }
                await Task.Delay(500, deadline.Token).ConfigureAwait(false);
            }
            return $"Runtime exited ({process.ExitCode}): {_lastOutput}";
        }
        catch { StopOwned(); throw; }
        finally { _gate.Release(); }
    }

    public void Stop(string profileId) { if (_profileId == profileId) StopOwned(); }
    private void StopOwned()
    {
        try { if (_process is { HasExited: false }) _process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
    }
    public void Dispose() { StopOwned(); _process?.Dispose(); AppDomain.CurrentDomain.ProcessExit -= OnExit; }
}
