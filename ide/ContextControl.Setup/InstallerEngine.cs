using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32;

internal static class InstallerEngine
{
    private const string ProductName = "ContextControl";
    private const string PayloadResourceName = "ContextControlPayload.zip";
    private const string UninstallerFileName = "ContextControl.Uninstall.exe";
    private const string UninstallRegistrySubKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\ContextControl";

    public static InstallResult Install(SetupOptions options, IInstallProgress progress)
    {
        var log = new List<string>();
        string? resolvedInstallDir = null;
        var appDataLogPath = GetAppDataLogPath();

        void Log(string message)
        {
            var line = $"{DateTimeOffset.Now:O} {message}";
            log.Add(line);
            progress.Report(message);
        }

        try
        {
            Log("Preparing install folder...");
            resolvedInstallDir = PrepareInstallDir(options.InstallDir);
            Directory.CreateDirectory(resolvedInstallDir);

            WaitForProcessExit(options.WaitForProcessId, Log);
            StopRunningWorkbench(resolvedInstallDir, Log);

            Log("Extracting ContextControl app files...");
            ExtractPayload(resolvedInstallDir, Log);

            var exePath = Path.Combine(resolvedInstallDir, "ContextControl.Workbench.exe");
            if (!File.Exists(exePath))
            {
                throw new FileNotFoundException("ContextControl.Workbench.exe was not found after extraction.", exePath);
            }

            Log("Preparing uninstaller...");
            var uninstallerPath = CopyUninstaller(resolvedInstallDir, Log);

            if (options.InstallWebView2Runtime)
            {
                InstallWebView2Runtime(Log);
            }
            else if (!TestWebView2Runtime())
            {
                Log("WebView2 Runtime was not detected. The app will still open, but the Browser workspace may need WebView2 later.");
            }

            if (options.StartMenuShortcut)
            {
                Log("Creating Start Menu shortcut...");
                var startMenuFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
                    "Programs",
                    "ContextControl");
                CreateShortcut(
                    Path.Combine(startMenuFolder, "ContextControl.lnk"),
                    exePath,
                    resolvedInstallDir);
                CreateShortcut(
                    Path.Combine(startMenuFolder, "Uninstall ContextControl.lnk"),
                    uninstallerPath,
                    resolvedInstallDir,
                    "/uninstall");
            }

            if (options.DesktopShortcut)
            {
                Log("Creating desktop shortcut...");
                CreateShortcut(
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "ContextControl.lnk"),
                    exePath,
                    resolvedInstallDir);
            }

            Log("Registering Windows uninstall entry...");
            RegisterUninstallEntry(resolvedInstallDir, exePath, uninstallerPath);

            string? launchError = null;
            if (options.Launch)
            {
                Log("Launching ContextControl...");
                launchError = LaunchApp(exePath, resolvedInstallDir);
                if (!string.IsNullOrWhiteSpace(launchError))
                {
                    Log($"Launch warning: {launchError}");
                }
            }

            Log("ContextControl install finished.");
            var installLogPath = WriteLogs(log, resolvedInstallDir, appDataLogPath);
            return new InstallResult(resolvedInstallDir, exePath, installLogPath, launchError);
        }
        catch (Exception ex)
        {
            log.Add($"{DateTimeOffset.Now:O} ERROR {ex}");
            WriteLogs(log, resolvedInstallDir, appDataLogPath);
            throw;
        }
    }

    public static UninstallResult Uninstall(SetupOptions options, IInstallProgress progress)
    {
        var log = new List<string>();
        var appDataLogPath = GetAppDataLogPath("uninstall.log");
        var resolvedInstallDir = PrepareInstallDir(options.InstallDir);

        void Log(string message)
        {
            var line = $"{DateTimeOffset.Now:O} {message}";
            log.Add(line);
            progress.Report(message);
        }

        try
        {
            Log($"Preparing to remove {resolvedInstallDir}...");
            EnsureSafeUninstallTarget(resolvedInstallDir);
            WaitForProcessExit(options.WaitForProcessId, Log);

            Log("Stopping running ContextControl windows...");
            StopRunningWorkbench(resolvedInstallDir, Log);

            Log("Removing shortcuts...");
            RemoveShortcuts();

            Log("Removing Windows uninstall entry...");
            RemoveUninstallEntry();

            Log("Removing installed app folder...");
            DeleteDirectoryWithRetry(resolvedInstallDir, recursive: true, Log);

            if (options.RemoveUserData)
            {
                Log("Removing ContextControl user data...");
                RemoveUserData(Log);
            }

            Log("ContextControl uninstall finished.");
            var logPath = WriteLogs(log, null, appDataLogPath);
            return new UninstallResult(logPath);
        }
        catch (Exception ex)
        {
            log.Add($"{DateTimeOffset.Now:O} ERROR {ex}");
            WriteLogs(log, null, appDataLogPath);
            throw;
        }
    }

    public static string? ReadRegisteredInstallLocation()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(UninstallRegistrySubKey);
            return key?.GetValue("InstallLocation") as string;
        }
        catch
        {
            return null;
        }
    }

    public static bool ShouldRelaunchUninstallFromTemp(string installDir)
    {
        var processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath) || string.IsNullOrWhiteSpace(installDir))
        {
            return false;
        }

        try
        {
            var processFull = Path.GetFullPath(processPath);
            var installFull = Path.GetFullPath(installDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return processFull.StartsWith(installFull, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public static void RelaunchUninstallerFromTemp(SetupOptions options)
    {
        var processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath) || !File.Exists(processPath))
        {
            throw new InvalidOperationException("The running setup executable could not be located for uninstall relaunch.");
        }

        var tempDir = Path.Combine(Path.GetTempPath(), "ContextControl", "Uninstall-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var tempExe = Path.Combine(tempDir, UninstallerFileName);
        File.Copy(processPath, tempExe, overwrite: true);

        var arguments = new List<string>
        {
            "/uninstall",
            "/uninstallRelaunched",
            $"/installDir={options.InstallDir}",
            $"/waitForProcess={Environment.ProcessId}"
        };
        if (options.Quiet)
        {
            arguments.Add("/quiet");
        }

        if (options.RemoveUserData)
        {
            arguments.Add("/removeUserData");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = tempExe,
            WorkingDirectory = tempDir,
            UseShellExecute = false,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        Process.Start(startInfo);
    }

    public static void WriteEmergencyLog(Exception exception)
    {
        try
        {
            var logPath = GetAppDataLogPath();
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
            File.AppendAllText(
                logPath,
                $"{DateTimeOffset.Now:O} ERROR {exception}{Environment.NewLine}",
                System.Text.Encoding.UTF8);
        }
        catch
        {
            // Last-resort logging must not hide the original setup failure.
        }
    }

    private static string CopyUninstaller(string installDir, Action<string> log)
    {
        var source = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(source) || !File.Exists(source))
        {
            throw new InvalidOperationException("The setup executable could not be located, so the uninstaller could not be registered.");
        }

        var destination = Path.Combine(installDir, UninstallerFileName);
        if (!string.Equals(Path.GetFullPath(source), Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))
        {
            File.Copy(source, destination, overwrite: true);
            log($"Uninstaller copied to {destination}.");
        }

        return destination;
    }

    private static void RegisterUninstallEntry(string installDir, string exePath, string uninstallerPath)
    {
        using var key = Registry.CurrentUser.CreateSubKey(UninstallRegistrySubKey, writable: true)
            ?? throw new InvalidOperationException("Could not create the Windows uninstall registry entry.");

        key.SetValue("DisplayName", ProductName, RegistryValueKind.String);
        key.SetValue("DisplayVersion", Application.ProductVersion, RegistryValueKind.String);
        key.SetValue("Publisher", "VulkanVX", RegistryValueKind.String);
        key.SetValue("InstallLocation", installDir, RegistryValueKind.String);
        key.SetValue("DisplayIcon", exePath, RegistryValueKind.String);
        key.SetValue("UninstallString", $"\"{uninstallerPath}\" /uninstall", RegistryValueKind.String);
        key.SetValue("QuietUninstallString", $"\"{uninstallerPath}\" /uninstall /quiet", RegistryValueKind.String);
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        key.SetValue("EstimatedSize", EstimateInstalledSizeKb(installDir), RegistryValueKind.DWord);
    }

    private static int EstimateInstalledSizeKb(string installDir)
    {
        try
        {
            var bytes = Directory.EnumerateFiles(installDir, "*", SearchOption.AllDirectories)
                .Sum(path => new FileInfo(path).Length);
            var kb = Math.Max(1, bytes / 1024);
            return kb > int.MaxValue ? int.MaxValue : (int)kb;
        }
        catch
        {
            return 1;
        }
    }

    private static void RemoveUninstallEntry()
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(UninstallRegistrySubKey, throwOnMissingSubKey: false);
        }
        catch
        {
            // A missing or locked registry entry should not leave app files behind.
        }
    }

    private static void EnsureSafeUninstallTarget(string installDir)
    {
        var full = Path.GetFullPath(installDir);
        var root = Path.GetPathRoot(full);
        if (string.IsNullOrWhiteSpace(full) ||
            string.Equals(full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), root?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Refusing to uninstall unsafe folder: {full}");
        }

        if (!Directory.Exists(full))
        {
            throw new DirectoryNotFoundException($"Install folder was not found: {full}");
        }

        var workbenchExe = Path.Combine(full, "ContextControl.Workbench.exe");
        var uninstallerExe = Path.Combine(full, UninstallerFileName);
        if (!File.Exists(workbenchExe) && !File.Exists(uninstallerExe))
        {
            throw new InvalidOperationException($"The selected folder does not look like a ContextControl install: {full}");
        }
    }

    private static void StopRunningWorkbench(string installDir, Action<string> log)
    {
        var installFull = Path.GetFullPath(installDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        foreach (var process in Process.GetProcessesByName("ContextControl.Workbench"))
        {
            using (process)
            {
                string? path = null;
                try
                {
                    path = process.MainModule?.FileName;
                }
                catch
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(path) ||
                    !Path.GetFullPath(path).StartsWith(installFull, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                log($"Stopping process {process.Id}...");
                try
                {
                    process.CloseMainWindow();
                    if (!process.WaitForExit(3000))
                    {
                        process.Kill(entireProcessTree: true);
                        process.WaitForExit(5000);
                    }
                }
                catch (Exception ex)
                {
                    log($"Could not stop process {process.Id}: {ex.Message}");
                }
            }
        }
    }

    private static void WaitForProcessExit(int? processId, Action<string> log)
    {
        if (processId is null or <= 0 || processId == Environment.ProcessId)
        {
            return;
        }

        try
        {
            using var process = Process.GetProcessById(processId.Value);
            if (process.HasExited)
            {
                return;
            }

            log($"Waiting for ContextControl process {processId.Value} to exit...");
            if (!process.WaitForExit(30000))
            {
                log($"Process {processId.Value} is still running; setup will try to close it before updating files.");
            }
        }
        catch (ArgumentException)
        {
            // The launching process already exited.
        }
        catch (Exception ex)
        {
            log($"Could not wait for process {processId.Value}: {ex.Message}");
        }
    }

    private static void RemoveShortcuts()
    {
        var startMenu = Environment.GetFolderPath(Environment.SpecialFolder.StartMenu);
        DeleteFileIfExists(Path.Combine(startMenu, "Programs", "ContextControl.lnk"));
        DeleteFileIfExists(Path.Combine(startMenu, "Programs", "ContextControl", "ContextControl.lnk"));
        DeleteFileIfExists(Path.Combine(startMenu, "Programs", "ContextControl", "Uninstall ContextControl.lnk"));
        DeleteDirectoryIfEmpty(Path.Combine(startMenu, "Programs", "ContextControl"));
        DeleteFileIfExists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "ContextControl.lnk"));
    }

    private static void RemoveUserData(Action<string> log)
    {
        DeleteDirectoryIfExists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ContextControl"), log);
        DeleteDirectoryIfExists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ContextControl"), log);
    }

    private static void DeleteDirectoryWithRetry(string path, bool recursive, Action<string> log)
    {
        Exception? lastException = null;
        for (var attempt = 1; attempt <= 40; attempt++)
        {
            try
            {
                if (!Directory.Exists(path))
                {
                    return;
                }

                Directory.Delete(path, recursive);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                lastException = ex;
                Thread.Sleep(250);
            }
        }

        log($"Could not remove {path}: {lastException?.Message}");
        if (lastException is not null)
        {
            throw lastException;
        }
    }

    private static void DeleteFileIfExists(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Shortcut cleanup is best-effort.
        }
    }

    private static void DeleteDirectoryIfEmpty(string path)
    {
        try
        {
            if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any())
            {
                Directory.Delete(path);
            }
        }
        catch
        {
            // Shortcut folder cleanup is best-effort.
        }
    }

    private static void DeleteDirectoryIfExists(string path, Action<string> log)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
                log($"Removed {path}.");
            }
        }
        catch (Exception ex)
        {
            log($"Could not remove {path}: {ex.Message}");
        }
    }

    private static string PrepareInstallDir(string installDir)
    {
        var expanded = Environment.ExpandEnvironmentVariables(installDir.Trim().Trim('"'));
        if (string.IsNullOrWhiteSpace(expanded))
        {
            expanded = SetupOptions.DefaultInstallDir;
        }

        return Path.GetFullPath(expanded);
    }

    private static void ExtractPayload(string installDir, Action<string> log)
    {
        using var payloadStream = Assembly.GetExecutingAssembly().GetManifestResourceStream(PayloadResourceName);
        if (payloadStream is null)
        {
            var resources = string.Join(", ", Assembly.GetExecutingAssembly().GetManifestResourceNames());
            throw new InvalidOperationException($"Installer payload is missing. Embedded resources: {resources}");
        }

        using var archive = new ZipArchive(payloadStream, ZipArchiveMode.Read);
        var root = Path.GetFullPath(installDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var rootWithSeparator = root + Path.DirectorySeparatorChar;
        var entries = archive.Entries;
        var updatedFiles = 0;
        var skippedUnchangedFiles = 0;
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            if (string.IsNullOrWhiteSpace(entry.FullName))
            {
                continue;
            }

            if (index % 25 == 0)
            {
                log($"Extracting files... {index + 1}/{entries.Count}");
            }

            var relativePath = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
            var destinationPath = Path.GetFullPath(Path.Combine(root, relativePath));
            if (!destinationPath.Equals(root, StringComparison.OrdinalIgnoreCase) &&
                !destinationPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Installer payload contains an unsafe path: {entry.FullName}");
            }

            if (relativePath.EndsWith(Path.DirectorySeparatorChar))
            {
                Directory.CreateDirectory(destinationPath);
                continue;
            }

            var fileName = Path.GetFileName(destinationPath);
            if (string.Equals(fileName, ".ccWorkbench.settings.json", StringComparison.OrdinalIgnoreCase) &&
                File.Exists(destinationPath))
            {
                log("Keeping existing .ccWorkbench.settings.json.");
                continue;
            }

            if (DestinationMatchesEntry(destinationPath, entry))
            {
                skippedUnchangedFiles++;
                continue;
            }

            var parent = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(parent))
            {
                Directory.CreateDirectory(parent);
            }

            ExtractChangedFile(entry, destinationPath);
            updatedFiles++;
        }

        log($"Extraction finished. Updated {updatedFiles} file(s), skipped {skippedUnchangedFiles} unchanged file(s).");
    }

    private static bool DestinationMatchesEntry(string destinationPath, ZipArchiveEntry entry)
    {
        try
        {
            if (!File.Exists(destinationPath))
            {
                return false;
            }

            var fileInfo = new FileInfo(destinationPath);
            if (fileInfo.Length != entry.Length)
            {
                return false;
            }

            using var source = entry.Open();
            using var target = new FileStream(destinationPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return StreamsEqual(source, target);
        }
        catch
        {
            return false;
        }
    }

    private static bool StreamsEqual(Stream left, Stream right)
    {
        var leftBuffer = new byte[1024 * 64];
        var rightBuffer = new byte[1024 * 64];
        while (true)
        {
            var leftRead = left.Read(leftBuffer, 0, leftBuffer.Length);
            var rightRead = right.Read(rightBuffer, 0, rightBuffer.Length);
            if (leftRead != rightRead)
            {
                return false;
            }

            if (leftRead == 0)
            {
                return true;
            }

            if (!leftBuffer.AsSpan(0, leftRead).SequenceEqual(rightBuffer.AsSpan(0, rightRead)))
            {
                return false;
            }
        }
    }

    private static void ExtractChangedFile(ZipArchiveEntry entry, string destinationPath)
    {
        var tempPath = destinationPath + ".ccupdate";
        try
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }

            entry.ExtractToFile(tempPath, overwrite: true);
            if (File.Exists(destinationPath))
            {
                File.Delete(destinationPath);
            }

            File.Move(tempPath, destinationPath);
        }
        catch
        {
            DeleteFileIfExists(tempPath);
            throw;
        }
    }

    private static string WriteLogs(IReadOnlyCollection<string> lines, string? installDir, string appDataLogPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(appDataLogPath)!);
        File.WriteAllLines(appDataLogPath, lines, System.Text.Encoding.UTF8);

        if (string.IsNullOrWhiteSpace(installDir))
        {
            return appDataLogPath;
        }

        try
        {
            var installLogPath = Path.Combine(installDir, "install.log");
            File.WriteAllLines(installLogPath, lines, System.Text.Encoding.UTF8);
            return installLogPath;
        }
        catch
        {
            return appDataLogPath;
        }
    }

    private static string GetAppDataLogPath(string fileName = "install.log")
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ContextControl");
        return Path.Combine(root, fileName);
    }

    private static void CreateShortcut(string shortcutPath, string targetPath, string workingDirectory)
    {
        CreateShortcut(shortcutPath, targetPath, workingDirectory, "");
    }

    private static void CreateShortcut(string shortcutPath, string targetPath, string workingDirectory, string arguments)
    {
        var parent = Path.GetDirectoryName(shortcutPath);
        if (!string.IsNullOrWhiteSpace(parent))
        {
            Directory.CreateDirectory(parent);
        }

        var shellType = Type.GetTypeFromProgID("WScript.Shell") ??
            throw new InvalidOperationException("WScript.Shell is unavailable, so setup could not create a shortcut.");
        object? shell = null;
        object? shortcut = null;

        try
        {
            shell = Activator.CreateInstance(shellType);
            shortcut = shellType.InvokeMember(
                "CreateShortcut",
                BindingFlags.InvokeMethod,
                binder: null,
                target: shell,
                args: [shortcutPath]);

            var shortcutType = shortcut!.GetType();
            shortcutType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, [targetPath]);
            shortcutType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, shortcut, [workingDirectory]);
            shortcutType.InvokeMember("Arguments", BindingFlags.SetProperty, null, shortcut, [arguments]);
            shortcutType.InvokeMember("IconLocation", BindingFlags.SetProperty, null, shortcut, [targetPath]);
            shortcutType.InvokeMember("Description", BindingFlags.SetProperty, null, shortcut, [string.IsNullOrWhiteSpace(arguments) ? "ContextControl Workbench" : "Uninstall ContextControl"]);
            shortcutType.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
        }
        finally
        {
            if (shortcut is not null && Marshal.IsComObject(shortcut))
            {
                Marshal.FinalReleaseComObject(shortcut);
            }

            if (shell is not null && Marshal.IsComObject(shell))
            {
                Marshal.FinalReleaseComObject(shell);
            }
        }
    }

    private static string? LaunchApp(string exePath, string workingDirectory)
    {
        try
        {
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = exePath,
                WorkingDirectory = workingDirectory,
                UseShellExecute = true,
            });

            if (process is not null && process.WaitForExit(2000))
            {
                return $"ContextControl exited immediately with code {process.ExitCode}. Try running {exePath} directly, or check install.log.";
            }

            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    private static bool TestWebView2Runtime()
    {
        return TestWebView2Runtime(RegistryHive.LocalMachine, RegistryView.Registry64) ||
            TestWebView2Runtime(RegistryHive.LocalMachine, RegistryView.Registry32) ||
            TestWebView2Runtime(RegistryHive.CurrentUser, RegistryView.Default);
    }

    private static bool TestWebView2Runtime(RegistryHive hive, RegistryView view)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var clients = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\EdgeUpdate\Clients");
            if (clients is null)
            {
                return false;
            }

            foreach (var subKeyName in clients.GetSubKeyNames())
            {
                using var client = clients.OpenSubKey(subKeyName);
                var name = client?.GetValue("name") as string;
                if (name?.Contains("WebView2", StringComparison.OrdinalIgnoreCase) == true)
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

    private static void InstallWebView2Runtime(Action<string> log)
    {
        if (TestWebView2Runtime())
        {
            log("WebView2 Runtime is already installed.");
            return;
        }

        log("Installing Microsoft Edge WebView2 Runtime with winget...");
        ProcessResult result;
        try
        {
            result = RunProcess(
                "winget",
                "install --id Microsoft.EdgeWebView2Runtime --exact --silent --accept-package-agreements --accept-source-agreements",
                TimeSpan.FromMinutes(10));
        }
        catch (Exception ex)
        {
            log($"WebView2 Runtime install was skipped because winget could not be started: {ex.Message}");
            return;
        }

        if (result.ExitCode == 0)
        {
            log("WebView2 Runtime installation finished.");
            return;
        }

        log($"WebView2 Runtime install was skipped or failed. winget exit code: {result.ExitCode}. {result.Output} {result.Error}");
    }

    private static ProcessResult RunProcess(string fileName, string arguments, TimeSpan timeout)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                StandardErrorEncoding = System.Text.Encoding.UTF8,
                CreateNoWindow = true,
            },
        };

        process.Start();
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();

        var timeoutMilliseconds = (int)Math.Min(int.MaxValue, timeout.TotalMilliseconds);
        if (!process.WaitForExit(timeoutMilliseconds))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Best effort after a timed-out optional dependency install.
            }

            return new ProcessResult(-1, outputTask.GetAwaiter().GetResult(), "Timed out while running winget. " + errorTask.GetAwaiter().GetResult());
        }

        return new ProcessResult(process.ExitCode, outputTask.GetAwaiter().GetResult(), errorTask.GetAwaiter().GetResult());
    }

}
