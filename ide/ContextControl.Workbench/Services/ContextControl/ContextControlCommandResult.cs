// CC-DESC: Represents a completed Context Control child-process command.

namespace ContextControl.Workbench.Services;

public sealed record ContextControlCommandResult(
    string Command,
    int ExitCode,
    string StandardOutput,
    string StandardError,
    string? OutputFile = null,
    string Runner = "PowerShell",
    TimeSpan? Elapsed = null,
    string? FallbackReason = null)
{
    public bool Succeeded => ExitCode == 0;
}
