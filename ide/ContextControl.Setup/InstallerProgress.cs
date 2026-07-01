internal interface IInstallProgress
{
    void Report(string message);
}

internal sealed class ProgressSink : IInstallProgress
{
    public static readonly ProgressSink Null = new();

    private ProgressSink()
    {
    }

    public void Report(string message)
    {
    }
}

internal sealed record InstallResult(string InstallDir, string ExePath, string LogPath, string? LaunchError);

internal sealed record UninstallResult(string LogPath);

internal sealed record ProcessResult(int ExitCode, string Output, string Error);
