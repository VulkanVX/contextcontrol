
namespace ContextControl.Setup;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        SetupOptions options;
        try
        {
            options = SetupOptions.Parse(args);
        }
        catch (Exception ex)
        {
            InstallerEngine.WriteEmergencyLog(ex);
            MessageBox.Show(ex.Message, "ContextControl Setup", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }

        if (options.Mode == SetupMode.Uninstall &&
            !options.UninstallRelaunched &&
            InstallerEngine.ShouldRelaunchUninstallFromTemp(options.InstallDir))
        {
            try
            {
                InstallerEngine.RelaunchUninstallerFromTemp(options);
                return 0;
            }
            catch (Exception ex)
            {
                InstallerEngine.WriteEmergencyLog(ex);
                MessageBox.Show(ex.Message, "ContextControl Setup", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }

        if (options.Quiet)
        {
            try
            {
                if (options.Mode == SetupMode.Uninstall)
                {
                    InstallerEngine.Uninstall(options, ProgressSink.Null);
                }
                else
                {
                    InstallerEngine.Install(options, ProgressSink.Null);
                }

                return 0;
            }
            catch (Exception ex)
            {
                InstallerEngine.WriteEmergencyLog(ex);
                return 1;
            }
        }

        ApplicationConfiguration.Initialize();
        using Form form = options.Mode == SetupMode.Uninstall
            ? new UninstallForm(options)
            : new SetupForm(options);
        Application.Run(form);
        return form is ISetupWindow setupWindow ? setupWindow.ExitCode : 1;
    }
}
