// CC-DESC: Safe Python dependency environment discovery and managed venv paths.

using System.Text;

namespace ContextControl.Workbench.Services;

internal sealed record PythonDependencySpec(
    string Id,
    string DisplayName,
    IReadOnlyList<string> Packages,
    IReadOnlyDictionary<string, string> PackagesToModules,
    IReadOnlyList<string>? PipInstallArguments = null)
{
    public IReadOnlyList<string> InstallArguments => PipInstallArguments ?? Packages;
}

internal sealed record PythonEnvironmentCandidate(string Executable, bool IsManaged, string SourceLabel)
{
    public bool IsRememberedExternal { get; init; }
}

internal static class PythonDependencyEnvironment
{
    private const string ReadyStampHeader = "ContextControlPythonDependencyReady=2";

    private static readonly PythonDependencySpec[] Specs =
    [
        new(
            "diffusers",
            "Hugging Face Diffusers",
            ["torch", "diffusers", "huggingface-hub", "transformers", "accelerate", "safetensors", "pillow"],
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["torch"] = "torch",
                ["diffusers"] = "diffusers",
                ["huggingface-hub"] = "huggingface_hub",
                ["transformers"] = "transformers",
                ["accelerate"] = "accelerate",
                ["safetensors"] = "safetensors",
                ["Pillow"] = "PIL"
            },
            ["torch", "diffusers>=0.38.0", "huggingface-hub", "transformers", "accelerate", "safetensors", "pillow"]),
        new(
            "transformers",
            "Hugging Face Transformers",
            ["torch", "transformers", "accelerate", "safetensors", "sentencepiece", "protobuf"],
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["torch"] = "torch",
                ["transformers"] = "transformers",
                ["accelerate"] = "accelerate",
                ["safetensors"] = "safetensors",
                ["sentencepiece"] = "sentencepiece",
                ["protobuf"] = "google.protobuf"
            }),
        new(
            "mlx_lm",
            "MLX LM",
            ["mlx-lm"],
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["mlx-lm"] = "mlx_lm"
            }),
        new(
            "mlc_llm",
            "MLC LLM",
            ["mlc-llm-nightly-cpu", "mlc-ai-nightly-cpu"],
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["mlc-llm"] = "mlc_llm"
            },
            ["--pre", "-f", "https://mlc.ai/wheels", "mlc-llm-nightly-cpu", "mlc-ai-nightly-cpu"]),
        new(
            "vllm",
            "vLLM",
            ["vllm"],
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["vLLM"] = "vllm"
            }),
        new(
            "sglang",
            "SGLang",
            ["sglang"],
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["SGLang"] = "sglang"
            }),
        new(
            "openvino_genai",
            "OpenVINO GenAI",
            ["openvino-genai"],
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["openvino-genai"] = "openvino_genai"
            }),
        new(
            "onnxruntime_genai",
            "ONNX Runtime GenAI",
            ["onnxruntime-genai"],
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["onnxruntime-genai"] = "onnxruntime_genai"
            }),
        new(
            "tensorrt_llm",
            "TensorRT-LLM",
            ["tensorrt_llm"],
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["TensorRT-LLM"] = "tensorrt_llm"
            }),
        new(
            "exllamav2_tabbyapi",
            "ExLlamaV2 / TabbyAPI",
            ["tabbyAPI", "exllamav2", "fastapi"],
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["ExLlamaV2"] = "exllamav2",
                ["FastAPI"] = "fastapi"
            },
            ["tabbyAPI[cu12] @ git+https://github.com/theroyallab/tabbyAPI.git"])
    ];

    private static readonly Dictionary<string, PythonDependencySpec> SpecsById =
        Specs.ToDictionary(spec => spec.Id, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<PythonDependencySpec> AllSpecs => Specs;
    public static string? PlatformLimitation(string id) => id switch
    {
        "mlx_lm" when !OperatingSystem.IsMacOS() => "MLX LM needs Apple Silicon macOS. Connect to an MLX server in Local model servers instead.",
        "vllm" or "sglang" or "tensorrt_llm" when OperatingSystem.IsWindows() => "Use this runtime on Linux / WSL with supported hardware, then connect its API in Local model servers. Native Windows installation is unavailable.",
        _ => null
    };

    public static string ManagedRoot
    {
        get
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(localAppData))
            {
                localAppData = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".contextcontrol");
            }

            return Path.Combine(localAppData, "ContextControl", "dependencies", "python");
        }
    }

    public static IReadOnlyDictionary<string, string> ManagedProcessEnvironment =>
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["PYTHONNOUSERSITE"] = "1",
            ["PIP_REQUIRE_VIRTUALENV"] = "true",
            ["PIP_DISABLE_PIP_VERSION_CHECK"] = "1"
        };

    public static bool TryGetSpec(string dependencyId, out PythonDependencySpec spec)
    {
        if (SpecsById.TryGetValue(dependencyId, out var found))
        {
            spec = found;
            return true;
        }

        spec = null!;
        return false;
    }

    public static bool HasManagedInstaller(string dependencyId)
    {
        return SpecsById.ContainsKey(dependencyId);
    }

    public static bool HasManagedDependency(string dependencyId)
    {
        try
        {
            return Directory.Exists(ManagedDependencyDirectory(dependencyId));
        }
        catch
        {
            return false;
        }
    }

    public static string EnvironmentVariableName(string dependencyId)
    {
        var builder = new StringBuilder("CC_PYTHON_");
        foreach (var ch in dependencyId)
        {
            builder.Append(char.IsLetterOrDigit(ch) ? char.ToUpperInvariant(ch) : '_');
        }

        return builder.ToString();
    }

    public static string ManagedDependencyDirectory(string dependencyId)
    {
        return Path.Combine(ManagedRoot, SanitizeDependencyId(dependencyId));
    }

    public static string ManagedEnvironmentDirectory(string dependencyId)
    {
        return Path.Combine(ManagedDependencyDirectory(dependencyId), ".venv");
    }

    public static string ManagedPythonExecutable(string dependencyId)
    {
        var environmentDirectory = ManagedEnvironmentDirectory(dependencyId);
        return OperatingSystem.IsWindows()
            ? Path.Combine(environmentDirectory, "Scripts", "python.exe")
            : Path.Combine(environmentDirectory, "bin", "python");
    }

    public static string ManagedReadyStampPath(string dependencyId)
    {
        return Path.Combine(ManagedEnvironmentDirectory(dependencyId), ".contextcontrol-ready");
    }

    public static string ExternalReadyStampPath(string dependencyId)
    {
        return Path.Combine(ManagedRoot, SanitizeDependencyId(dependencyId), ".contextcontrol-external-python");
    }

    public static bool HasManagedReadyStamp(PythonDependencySpec spec)
    {
        try
        {
            var managedPython = ManagedPythonExecutable(spec.Id);
            var stampPath = ManagedReadyStampPath(spec.Id);
            if (!File.Exists(managedPython) || !File.Exists(stampPath))
            {
                return false;
            }

            var stamp = File.ReadAllText(stampPath);
            return stamp.Contains(ReadyStampHeader, StringComparison.OrdinalIgnoreCase)
                && spec.Packages.All(package => stamp.Contains(package, StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }

    public static void MarkManagedReady(PythonDependencySpec spec)
    {
        try
        {
            var stampPath = ManagedReadyStampPath(spec.Id);
            Directory.CreateDirectory(Path.GetDirectoryName(stampPath) ?? ManagedEnvironmentDirectory(spec.Id));
            File.WriteAllText(
                stampPath,
                string.Join(Environment.NewLine, spec.Packages.Prepend(DateTimeOffset.UtcNow.ToString("O")).Prepend(ReadyStampHeader)));
        }
        catch
        {
            // The stamp is an optimization; runtime validation remains the source of truth.
        }
    }

    public static void ClearManagedReady(string dependencyId)
    {
        try
        {
            var stampPath = ManagedReadyStampPath(dependencyId);
            if (File.Exists(stampPath))
            {
                File.Delete(stampPath);
            }
        }
        catch
        {
            // Missing or locked ready stamps should not block repair.
        }
    }

    public static string RemoveManagedDependency(string dependencyId)
    {
        var directory = ManagedDependencyDirectory(dependencyId);
        DeleteManagedChildDirectory(ManagedRoot, directory);
        return directory;
    }

    public static void ClearManagedEnvironmentVariables(string dependencyId)
    {
        var managedPython = ManagedPythonExecutable(dependencyId);
        ClearEnvironmentVariableIfMatches(EnvironmentVariableName(dependencyId), managedPython);
        if (dependencyId.Equals("diffusers", StringComparison.OrdinalIgnoreCase))
        {
            ClearEnvironmentVariableIfMatches("CC_PYTHON", managedPython);
        }
    }

    public static void ClearExternalEnvironmentVariables(string dependencyId, string executable)
    {
        ClearEnvironmentVariableIfMatches(EnvironmentVariableName(dependencyId), executable);
        if (dependencyId.Equals("diffusers", StringComparison.OrdinalIgnoreCase))
        {
            ClearEnvironmentVariableIfMatches("CC_PYTHON", executable);
        }
    }

    public static string? ReadRememberedExternalPython(PythonDependencySpec spec)
    {
        try
        {
            var stampPath = ExternalReadyStampPath(spec.Id);
            if (!File.Exists(stampPath))
            {
                return null;
            }

            var lines = File.ReadAllLines(stampPath);
            if (!spec.Packages.All(package => lines.Any(line => line.Contains(package, StringComparison.OrdinalIgnoreCase))))
            {
                return null;
            }

            var executable = lines
                .Select(line => line.Trim())
                .FirstOrDefault(line => line.StartsWith("python=", StringComparison.OrdinalIgnoreCase))
                ?.Substring("python=".Length);
            return TryNormalizeExecutable(executable, out var normalized)
                ? normalized
                : null;
        }
        catch
        {
            return null;
        }
    }

    public static void MarkExternalReady(PythonDependencySpec spec, string executable)
    {
        try
        {
            if (!TryNormalizeExecutable(executable, out var normalized))
            {
                return;
            }

            var managedPython = ManagedPythonExecutable(spec.Id);
            if (string.Equals(normalized, managedPython, StringComparison.OrdinalIgnoreCase))
            {
                MarkManagedReady(spec);
                return;
            }

            var stampPath = ExternalReadyStampPath(spec.Id);
            Directory.CreateDirectory(Path.GetDirectoryName(stampPath) ?? ManagedRoot);
            var lines = new List<string>
            {
                DateTimeOffset.UtcNow.ToString("O"),
                $"python={normalized}"
            };
            lines.AddRange(spec.Packages.Select(package => $"package={package}"));
            File.WriteAllLines(stampPath, lines);
        }
        catch
        {
            // Remembering an external interpreter is best-effort; validation remains the source of truth.
        }
    }

    public static void ClearExternalReady(PythonDependencySpec spec)
    {
        try
        {
            var stampPath = ExternalReadyStampPath(spec.Id);
            if (File.Exists(stampPath))
            {
                File.Delete(stampPath);
            }
        }
        catch
        {
            // External-ready stamps are advisory; failed cleanup must not block dependency state refresh.
        }
    }

    public static IReadOnlyList<PythonEnvironmentCandidate> FindPythonCandidatesForDetection(
        string dependencyId,
        bool includePathCandidates = true)
    {
        var candidates = new List<PythonEnvironmentCandidate>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddCandidate(ManagedPythonExecutable(dependencyId), isManaged: true, "ContextControl managed");
        if (TryGetSpec(dependencyId, out var spec))
        {
            AddCandidate(ReadRememberedExternalPython(spec), isManaged: false, "ContextControl remembered", isRememberedExternal: true);
        }

        AddCandidate(Environment.GetEnvironmentVariable(EnvironmentVariableName(dependencyId)), isManaged: false, EnvironmentVariableName(dependencyId));
        AddCandidate(Environment.GetEnvironmentVariable("CC_PYTHON"), isManaged: false, "CC_PYTHON");

        if (includePathCandidates)
        {
            foreach (var candidate in FindExecutableCandidatesOnPath(["python.exe", "python", "py.exe", "py"]))
            {
                AddCandidate(candidate, isManaged: false, "PATH");
            }

            foreach (var candidate in FindKnownPythonInstallCandidates())
            {
                AddCandidate(candidate, isManaged: false, "known install");
            }
        }

        return candidates;

        void AddCandidate(string? candidate, bool isManaged, string sourceLabel, bool isRememberedExternal = false)
        {
            if (TryNormalizeExecutable(candidate, out var normalized) && seen.Add(normalized))
            {
                candidates.Add(new PythonEnvironmentCandidate(normalized, isManaged, sourceLabel)
                {
                    IsRememberedExternal = isRememberedExternal
                });
            }
        }
    }

    public static IReadOnlyList<string> FindPythonSeedCandidates()
    {
        var candidates = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddCandidate(Environment.GetEnvironmentVariable("CC_PYTHON"));
        foreach (var candidate in FindExecutableCandidatesOnPath(["python.exe", "python", "py.exe", "py"]))
        {
            AddCandidate(candidate);
        }

        foreach (var candidate in FindKnownPythonInstallCandidates())
        {
            AddCandidate(candidate);
        }

        return candidates;

        void AddCandidate(string? candidate)
        {
            if (TryNormalizeExecutable(candidate, out var normalized) && seen.Add(normalized))
            {
                candidates.Add(normalized);
            }
        }
    }

    public static string BuildPythonModuleImportScript(IReadOnlyDictionary<string, string> packagesToModules)
    {
        var packageLines = string.Join(
            "\n",
            packagesToModules.Select(pair => $"    ({EscapePythonString(pair.Key)}, {EscapePythonString(pair.Value)}),"));

        return $$"""
import importlib
import sys

packages = [
{{packageLines}}
]
errors = []
for name, module in packages:
    try:
        importlib.import_module(module)
    except ModuleNotFoundError as exc:
        if exc.name == module or module.startswith(exc.name + "."):
            errors.append(f"Missing {name}")
        else:
            errors.append(f"{name}: {type(exc).__name__}: {exc}")
    except Exception as exc:
        errors.append(f"{name}: {type(exc).__name__}: {exc}")
if errors:
    print("; ".join(errors))
    sys.exit(1)
print(sys.executable)
""";
    }

    public static string BuildPythonDependencyHealthScript(
        string dependencyId,
        IReadOnlyDictionary<string, string> packagesToModules)
    {
        return dependencyId.Equals("diffusers", StringComparison.OrdinalIgnoreCase)
            ? BuildDiffusersHealthScript(packagesToModules)
            : BuildPythonModuleImportScript(packagesToModules);
    }

    public static string BuildFlux2KleinHealthScript(IReadOnlyDictionary<string, string> packagesToModules)
    {
        return BuildDiffusersHealthScript(packagesToModules, requireFlux2KleinPipeline: true);
    }

    public static string BuildPythonModuleProbeScript(IReadOnlyDictionary<string, string> packagesToModules)
    {
        var packageLines = string.Join(
            "\n",
            packagesToModules.Select(pair => $"    ({EscapePythonString(pair.Key)}, {EscapePythonString(pair.Value)}),"));

        return $$"""
import importlib.util
import sys

packages = [
{{packageLines}}
]
errors = []
for name, module in packages:
    try:
        found = importlib.util.find_spec(module)
    except (ImportError, AttributeError, ValueError) as exc:
        found = None
    if found is None:
        errors.append(f"Missing {name}")
if errors:
    print("; ".join(errors))
    sys.exit(1)
print(sys.executable)
""";
    }

    private static string BuildDiffusersHealthScript(
        IReadOnlyDictionary<string, string> packagesToModules,
        bool requireFlux2KleinPipeline = false)
    {
        var packageLines = string.Join(
            "\n",
            packagesToModules.Select(pair => $"    ({EscapePythonString(pair.Key)}, {EscapePythonString(pair.Value)}),"));
        var flux2KleinCheck = requireFlux2KleinPipeline
            ? """

diffusers = loaded.get("diffusers")
if diffusers is not None and not hasattr(diffusers, "Flux2KleinPipeline"):
    errors.append("Missing Flux2KleinPipeline in diffusers; reinstall Hugging Face Diffusers from ContextControl Dependencies.")
"""
            : "";

        return $$"""
import importlib
import sys

packages = [
{{packageLines}}
]
errors = []
loaded = {}
for name, module in packages:
    try:
        loaded[module] = importlib.import_module(module)
    except ModuleNotFoundError as exc:
        if exc.name == module or module.startswith(exc.name + "."):
            errors.append(f"Missing {name}")
        else:
            errors.append(f"{name}: {type(exc).__name__}: {exc}")
    except Exception as exc:
        errors.append(f"{name}: {type(exc).__name__}: {exc}")

torch = loaded.get("torch")
if torch is not None:
    try:
        _ = torch.__version__
        _ = torch.cuda.is_available()
    except Exception as exc:
        errors.append(f"PyTorch runtime check failed: {type(exc).__name__}: {exc}")
{{flux2KleinCheck}}

if errors:
    print("; ".join(errors))
    sys.exit(1)
print(sys.executable)
""";
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
                if (TryNormalizeExecutable(Path.Combine(directory, fileName), out var candidate) && seen.Add(candidate))
                {
                    candidates.Add(candidate);
                }
            }
        }

        return candidates;
    }

    private static IReadOnlyList<string> FindKnownPythonInstallCandidates()
    {
        var candidates = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddCandidate(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs",
            "Python",
            "Launcher",
            OperatingSystem.IsWindows() ? "py.exe" : "py"));

        AddPythonRoots(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs",
            "Python"));
        AddPythonRoots(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
        AddPythonRoots(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));

        return candidates;

        void AddPythonRoots(string root)
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            {
                return;
            }

            try
            {
                foreach (var directory in Directory.EnumerateDirectories(root, "Python3*", SearchOption.TopDirectoryOnly))
                {
                    AddCandidate(Path.Combine(directory, OperatingSystem.IsWindows() ? "python.exe" : "python"));
                }
            }
            catch
            {
                // Ignore inaccessible install roots.
            }
        }

        void AddCandidate(string? candidate)
        {
            if (TryNormalizeExecutable(candidate, out var normalized) && seen.Add(normalized))
            {
                candidates.Add(normalized);
            }
        }
    }

    private static bool TryNormalizeExecutable(string? candidate, out string normalized)
    {
        normalized = "";
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        try
        {
            var fullPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(candidate.Trim('"')));
            if (!File.Exists(fullPath))
            {
                return false;
            }

            if (LooksLikeWindowsStorePythonAlias(fullPath))
            {
                return false;
            }

            normalized = fullPath;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool LooksLikeWindowsStorePythonAlias(string fullPath)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        var fileName = Path.GetFileName(fullPath);
        if (!fileName.StartsWith("python", StringComparison.OrdinalIgnoreCase)
            && !fileName.Equals("py.exe", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var normalized = fullPath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        return normalized.Contains(
            $"{Path.DirectorySeparatorChar}Microsoft{Path.DirectorySeparatorChar}WindowsApps{Path.DirectorySeparatorChar}",
            StringComparison.OrdinalIgnoreCase);
    }

    private static string SanitizeDependencyId(string dependencyId)
    {
        var builder = new StringBuilder();
        foreach (var ch in dependencyId)
        {
            builder.Append(char.IsLetterOrDigit(ch) || ch is '_' or '-' ? ch : '_');
        }

        return builder.Length == 0 ? "dependency" : builder.ToString();
    }

    private static void DeleteManagedChildDirectory(string root, string directory)
    {
        var rootFullPath = EnsureTrailingSeparator(Path.GetFullPath(root));
        var directoryFullPath = Path.GetFullPath(directory);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!directoryFullPath.StartsWith(rootFullPath, comparison))
        {
            throw new InvalidOperationException($"Refusing to remove dependency outside managed root: {directoryFullPath}");
        }

        if (Directory.Exists(directoryFullPath))
        {
            Directory.Delete(directoryFullPath, recursive: true);
        }
    }

    private static string EnsureTrailingSeparator(string path)
    {
        return path.EndsWith(Path.DirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;
    }

    private static void ClearEnvironmentVariableIfMatches(string name, string expectedPath)
    {
        var current = Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Process);
        if (!PathsEqual(current, expectedPath))
        {
            return;
        }

        Environment.SetEnvironmentVariable(name, null, EnvironmentVariableTarget.Process);
    }

    private static bool PathsEqual(string? left, string right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        try
        {
            var leftFullPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(left.Trim('"')));
            var rightFullPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(right.Trim('"')));
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            return string.Equals(leftFullPath, rightFullPath, comparison);
        }
        catch
        {
            return false;
        }
    }

    private static string EscapePythonString(string value)
    {
        return "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
    }
}
