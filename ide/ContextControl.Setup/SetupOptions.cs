internal enum SetupMode
{
    Install,
    Uninstall
}

internal sealed class SetupOptions
{
    public static string DefaultInstallDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "ContextControl");

    public string InstallDir { get; init; } = DefaultInstallDir;
    public bool StartMenuShortcut { get; init; } = true;
    public bool DesktopShortcut { get; init; }
    public bool InstallWebView2Runtime { get; init; }
    public bool Launch { get; init; } = true;
    public bool Quiet { get; init; }
    public SetupMode Mode { get; init; }
    public bool RemoveUserData { get; init; }
    public bool UninstallRelaunched { get; init; }
    public int? WaitForProcessId { get; init; }

    public static SetupOptions Parse(string[] args)
    {
        var options = new MutableSetupOptions();
        for (var index = 0; index < args.Length; index++)
        {
            var raw = args[index].Trim();
            if (raw.Length == 0)
            {
                continue;
            }

            var separatorIndex = raw.IndexOf('=');
            var key = separatorIndex >= 0 ? raw[..separatorIndex] : raw;
            var value = separatorIndex >= 0 ? raw[(separatorIndex + 1)..] : null;
            key = key.TrimStart('-', '/').Trim();

            switch (key.ToLowerInvariant())
            {
                case "quiet":
                case "silent":
                    options.Quiet = true;
                    break;
                case "uninstall":
                case "remove":
                    options.Mode = SetupMode.Uninstall;
                    break;
                case "uninstallrelaunched":
                    options.UninstallRelaunched = true;
                    break;
                case "removeuserdata":
                    options.RemoveUserData = true;
                    break;
                case "waitforprocess":
                case "waitforpid":
                case "waitpid":
                    value ??= ReadNextValue(args, ref index, raw);
                    if (!int.TryParse(value, out var processId) || processId <= 0)
                    {
                        throw new ArgumentException($"Invalid process id for setup option: {raw}");
                    }

                    options.WaitForProcessId = processId;
                    break;
                case "installdir":
                case "installpath":
                case "dir":
                    value ??= ReadNextValue(args, ref index, raw);
                    options.InstallDirValue = value;
                    break;
                case "startmenushortcut":
                case "startmenu":
                    options.StartMenuShortcut = true;
                    break;
                case "nostartmenushortcut":
                case "nostartmenu":
                    options.StartMenuShortcut = false;
                    break;
                case "desktopshortcut":
                case "desktop":
                    options.DesktopShortcut = true;
                    break;
                case "nodesktopshortcut":
                case "nodesktop":
                    options.DesktopShortcut = false;
                    break;
                case "installwebview2runtime":
                case "installwebview2":
                case "webview2":
                    options.InstallWebView2Runtime = true;
                    break;
                case "launch":
                    options.Launch = true;
                    break;
                case "nolaunch":
                    options.Launch = false;
                    break;
                default:
                    throw new ArgumentException($"Unknown setup option: {raw}");
            }
        }

        if (options.Mode == SetupMode.Uninstall && !options.InstallDirSpecified)
        {
            options.InstallDir = InstallerEngine.ReadRegisteredInstallLocation() ?? DefaultInstallDir;
        }

        return options.ToImmutable();
    }

    private static string ReadNextValue(string[] args, ref int index, string optionName)
    {
        if (index + 1 >= args.Length)
        {
            throw new ArgumentException($"Missing value for setup option: {optionName}");
        }

        index++;
        return args[index];
    }

    private sealed class MutableSetupOptions
    {
        public string InstallDir { get; set; } = DefaultInstallDir;
        public bool StartMenuShortcut { get; set; } = true;
        public bool DesktopShortcut { get; set; }
        public bool InstallWebView2Runtime { get; set; }
        public bool Launch { get; set; } = true;
        public bool Quiet { get; set; }
        public SetupMode Mode { get; set; } = SetupMode.Install;
        public bool RemoveUserData { get; set; }
        public bool UninstallRelaunched { get; set; }
        public int? WaitForProcessId { get; set; }
        public bool InstallDirSpecified { get; set; }

        public string InstallDirValue
        {
            get => InstallDir;
            set
            {
                InstallDir = value;
                InstallDirSpecified = true;
            }
        }

        public SetupOptions ToImmutable() =>
            new()
            {
                InstallDir = InstallDir,
                StartMenuShortcut = StartMenuShortcut,
                DesktopShortcut = DesktopShortcut,
                InstallWebView2Runtime = InstallWebView2Runtime,
                Launch = Launch,
                Quiet = Quiet,
                Mode = Mode,
                RemoveUserData = RemoveUserData,
                UninstallRelaunched = UninstallRelaunched,
                WaitForProcessId = WaitForProcessId,
            };
    }
}
