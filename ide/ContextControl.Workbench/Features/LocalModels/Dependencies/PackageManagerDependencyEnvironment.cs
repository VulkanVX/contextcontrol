// CC-DESC: Package-manager install commands for dependencies that cannot be ContextControl-local.

namespace ContextControl.Workbench.Services;

internal sealed record PackageManagerDependencySpec(
    string Id,
    string DisplayName,
    IReadOnlyList<string> ExecutableNames,
    string? WindowsWingetId = null,
    string? MacBrewCask = null,
    string? LinuxInstallShell = null);

internal sealed record PackageManagerInstallCommand(
    string FileName,
    IReadOnlyList<string> Arguments,
    TimeSpan Timeout);

internal static class PackageManagerDependencyEnvironment
{
    private static readonly PackageManagerDependencySpec PythonSpec = new(
        "python",
        "Python 3.12",
        ["python.exe", "python", "py.exe", "py"],
        WindowsWingetId: "Python.Python.3.12",
        MacBrewCask: "python@3.12",
        LinuxInstallShell: "if command -v apt-get >/dev/null 2>&1; then sudo apt-get update && sudo apt-get install -y python3 python3-venv python3-pip; elif command -v dnf >/dev/null 2>&1; then sudo dnf install -y python3 python3-pip; elif command -v pacman >/dev/null 2>&1; then sudo pacman -S --needed python python-pip; else echo 'No supported package manager found for Python.' >&2; exit 1; fi");

    private static readonly PackageManagerDependencySpec[] Specs =
    [
        new(
            "ollama",
            "Ollama",
            ["ollama.exe", "ollama"],
            WindowsWingetId: "Ollama.Ollama",
            MacBrewCask: "ollama",
            LinuxInstallShell: "curl -fsSL https://ollama.com/install.sh | sh"),
        new(
            "lm_studio",
            "LM Studio",
            ["lms.exe", "lms", "LM Studio.exe"],
            WindowsWingetId: "ElementLabs.LMStudio",
            MacBrewCask: "lm-studio")
    ];

    private static readonly Dictionary<string, PackageManagerDependencySpec> SpecsById =
        Specs.ToDictionary(spec => spec.Id, StringComparer.OrdinalIgnoreCase);

    public static bool HasManagedInstaller(string dependencyId)
    {
        return TryGetSpec(dependencyId, out var spec) && TryResolveInstallCommand(spec, out _);
    }

    public static bool TryGetSpec(string dependencyId, out PackageManagerDependencySpec spec)
    {
        if (SpecsById.TryGetValue(dependencyId, out var found))
        {
            spec = found;
            return true;
        }

        spec = null!;
        return false;
    }

    public static bool TryResolveInstallCommand(
        PackageManagerDependencySpec spec,
        out PackageManagerInstallCommand command)
    {
        if (OperatingSystem.IsWindows() && !string.IsNullOrWhiteSpace(spec.WindowsWingetId))
        {
            var arguments = new List<string>
            {
                "install",
                "--id",
                spec.WindowsWingetId,
                "--exact",
                "--silent",
                "--accept-package-agreements",
                "--accept-source-agreements"
            };
            if (spec.Id.Equals("python", StringComparison.OrdinalIgnoreCase))
            {
                arguments.Add("--scope");
                arguments.Add("user");
            }

            command = new PackageManagerInstallCommand(
                "winget",
                arguments,
                TimeSpan.FromMinutes(45));
            return true;
        }

        if (OperatingSystem.IsMacOS() && !string.IsNullOrWhiteSpace(spec.MacBrewCask))
        {
            command = new PackageManagerInstallCommand(
                "brew",
                ["install", "--cask", spec.MacBrewCask],
                TimeSpan.FromMinutes(45));
            return true;
        }

        if (OperatingSystem.IsLinux() && !string.IsNullOrWhiteSpace(spec.LinuxInstallShell))
        {
            command = new PackageManagerInstallCommand(
                "sh",
                ["-c", spec.LinuxInstallShell],
                TimeSpan.FromMinutes(45));
            return true;
        }

        command = null!;
        return false;
    }

    public static bool TryResolvePythonInstallCommand(out PackageManagerInstallCommand command)
    {
        return TryResolveInstallCommand(PythonSpec, out command);
    }
}
