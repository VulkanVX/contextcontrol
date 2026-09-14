using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace ContextControl.Workbench.Services;

public enum ExternalBrowserLaunchKind
{
    SystemDefault,
    Executable,
    MacApplication,
    Command
}

public sealed record ExternalBrowserTarget(
    string Key,
    string Name,
    ExternalBrowserLaunchKind LaunchKind,
    string? PathOrName);

public static class ExternalBrowserService
{
    public static ExternalBrowserTarget DefaultTarget { get; } =
        new("default", "Default", ExternalBrowserLaunchKind.SystemDefault, null);

    public static IReadOnlyList<ExternalBrowserTarget> DetectTargets()
    {
        var targets = new List<ExternalBrowserTarget> { DefaultTarget };

        if (OperatingSystem.IsWindows())
        {
            AddWindowsTargets(targets);
        }
        else if (OperatingSystem.IsMacOS())
        {
            AddMacTargets(targets);
        }
        else
        {
            AddLinuxTargets(targets);
        }

        return targets
            .GroupBy(target => target.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(target => target.Key == "default" ? 0 : 1)
            .ThenBy(target => target.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static void Open(ExternalBrowserTarget? target, string url)
    {
        var selected = target ?? DefaultTarget;
        switch (selected.LaunchKind)
        {
            case ExternalBrowserLaunchKind.SystemDefault:
                OpenDefault(url);
                break;
            case ExternalBrowserLaunchKind.MacApplication:
                StartProcess("open", ["-a", selected.PathOrName ?? selected.Name, url], useShellExecute: false);
                break;
            case ExternalBrowserLaunchKind.Command:
            case ExternalBrowserLaunchKind.Executable:
                StartProcess(selected.PathOrName ?? selected.Name, [url], useShellExecute: false);
                break;
            default:
                OpenDefault(url);
                break;
        }
    }

    private static void OpenDefault(string url)
    {
        if (OperatingSystem.IsWindows())
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        else if (OperatingSystem.IsMacOS())
        {
            StartProcess("open", [url], useShellExecute: false);
        }
        else
        {
            StartProcess("xdg-open", [url], useShellExecute: false);
        }
    }

    private static void StartProcess(string fileName, IEnumerable<string> arguments, bool useShellExecute)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            UseShellExecute = useShellExecute
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        Process.Start(startInfo);
    }

    [SupportedOSPlatform("windows")]
    private static void AddWindowsTargets(List<ExternalBrowserTarget> targets)
    {
        AddKnownWindowsBrowser(targets, "edge", "Microsoft Edge",
            @"Microsoft\Edge\Application\msedge.exe");
        AddKnownWindowsBrowser(targets, "chrome", "Google Chrome",
            @"Google\Chrome\Application\chrome.exe");
        AddKnownWindowsBrowser(targets, "brave", "Brave",
            @"BraveSoftware\Brave-Browser\Application\brave.exe");
        AddKnownWindowsBrowser(targets, "firefox", "Firefox",
            @"Mozilla Firefox\firefox.exe",
            programFilesOnly: true);
        AddKnownWindowsBrowser(targets, "vivaldi", "Vivaldi",
            @"Vivaldi\Application\vivaldi.exe");
        AddKnownWindowsBrowser(targets, "opera", "Opera",
            @"Programs\Opera\opera.exe",
            localAppDataOnly: true);

        AddWindowsRegistryBrowsers(targets);
    }

    private static void AddKnownWindowsBrowser(
        List<ExternalBrowserTarget> targets,
        string key,
        string name,
        string relativePath,
        bool programFilesOnly = false,
        bool localAppDataOnly = false)
    {
        var roots = new List<string>();
        if (!localAppDataOnly)
        {
            AddIfNotBlank(roots, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
            AddIfNotBlank(roots, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));
        }

        if (!programFilesOnly)
        {
            AddIfNotBlank(roots, Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        }

        foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var path = Path.Combine(root, relativePath);
            if (File.Exists(path))
            {
                AddExecutableTarget(targets, key, name, path);
                return;
            }
        }
    }

    [SupportedOSPlatform("windows")]
    private static void AddWindowsRegistryBrowsers(List<ExternalBrowserTarget> targets)
    {
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        {
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try
                {
                    using var root = RegistryKey.OpenBaseKey(hive, view);
                    using var clients = root.OpenSubKey(@"Software\Clients\StartMenuInternet");
                    if (clients is null)
                    {
                        continue;
                    }

                    foreach (var subKeyName in clients.GetSubKeyNames())
                    {
                        using var browserKey = clients.OpenSubKey(subKeyName);
                        using var commandKey = clients.OpenSubKey($@"{subKeyName}\shell\open\command");
                        var displayName = browserKey?.GetValue(null)?.ToString();
                        var command = commandKey?.GetValue(null)?.ToString();
                        var executable = TryExtractWindowsExecutable(command);
                        if (string.IsNullOrWhiteSpace(displayName) || string.IsNullOrWhiteSpace(executable) || !File.Exists(executable))
                        {
                            continue;
                        }

                        AddExecutableTarget(targets, StableKey(displayName, executable), displayName.Trim(), executable);
                    }
                }
                catch
                {
                    // Browser discovery is best-effort and should never block startup.
                }
            }
        }
    }

    private static void AddMacTargets(List<ExternalBrowserTarget> targets)
    {
        AddMacApplication(targets, "safari", "Safari", "Safari");
        AddMacApplication(targets, "chrome", "Google Chrome", "Google Chrome");
        AddMacApplication(targets, "edge", "Microsoft Edge", "Microsoft Edge");
        AddMacApplication(targets, "brave", "Brave", "Brave Browser");
        AddMacApplication(targets, "firefox", "Firefox", "Firefox");
        AddMacApplication(targets, "vivaldi", "Vivaldi", "Vivaldi");
        AddMacApplication(targets, "opera", "Opera", "Opera");
    }

    private static void AddMacApplication(List<ExternalBrowserTarget> targets, string key, string name, string applicationName)
    {
        if (Directory.Exists($"/Applications/{applicationName}.app")
            || Directory.Exists(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Applications",
                $"{applicationName}.app")))
        {
            targets.Add(new ExternalBrowserTarget(key, name, ExternalBrowserLaunchKind.MacApplication, applicationName));
        }
    }

    private static void AddLinuxTargets(List<ExternalBrowserTarget> targets)
    {
        AddLinuxCommand(targets, "chrome", "Google Chrome", "google-chrome");
        AddLinuxCommand(targets, "chrome-stable", "Google Chrome", "google-chrome-stable");
        AddLinuxCommand(targets, "chromium", "Chromium", "chromium");
        AddLinuxCommand(targets, "edge", "Microsoft Edge", "microsoft-edge");
        AddLinuxCommand(targets, "brave", "Brave", "brave-browser");
        AddLinuxCommand(targets, "firefox", "Firefox", "firefox");
        AddLinuxCommand(targets, "vivaldi", "Vivaldi", "vivaldi");
        AddLinuxCommand(targets, "opera", "Opera", "opera");
    }

    private static void AddLinuxCommand(List<ExternalBrowserTarget> targets, string key, string name, string command)
    {
        if (FindOnPath(command) is not null)
        {
            targets.Add(new ExternalBrowserTarget(key, name, ExternalBrowserLaunchKind.Command, command));
        }
    }

    private static string? FindOnPath(string command)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var candidate = Path.Combine(directory, command);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static void AddExecutableTarget(List<ExternalBrowserTarget> targets, string key, string name, string executablePath)
    {
        var normalizedPath = Path.GetFullPath(executablePath);
        if (targets.Any(target => string.Equals(target.PathOrName, normalizedPath, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        targets.Add(new ExternalBrowserTarget(key, name, ExternalBrowserLaunchKind.Executable, normalizedPath));
    }

    private static string StableKey(string displayName, string executablePath)
    {
        var fileName = Path.GetFileNameWithoutExtension(executablePath).ToLowerInvariant();
        if (fileName.Contains("msedge", StringComparison.OrdinalIgnoreCase))
        {
            return "edge";
        }

        if (fileName.Contains("chrome", StringComparison.OrdinalIgnoreCase))
        {
            return "chrome";
        }

        if (fileName.Contains("firefox", StringComparison.OrdinalIgnoreCase))
        {
            return "firefox";
        }

        if (fileName.Contains("brave", StringComparison.OrdinalIgnoreCase))
        {
            return "brave";
        }

        return displayName.Trim().ToLowerInvariant().Replace(' ', '-');
    }

    private static string? TryExtractWindowsExecutable(string? command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return null;
        }

        var trimmed = command.Trim();
        if (trimmed.StartsWith('"'))
        {
            var endQuote = trimmed.IndexOf('"', 1);
            return endQuote > 1 ? trimmed[1..endQuote] : null;
        }

        var exeIndex = trimmed.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        return exeIndex > 0 ? trimmed[..(exeIndex + 4)].Trim() : null;
    }

    private static void AddIfNotBlank(List<string> values, string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            values.Add(value);
        }
    }
}
