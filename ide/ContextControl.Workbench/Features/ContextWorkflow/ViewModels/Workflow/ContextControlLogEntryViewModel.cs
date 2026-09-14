// CC-DESC: Represents one Context Control dock log entry.

namespace ContextControl.Workbench.ViewModels;

public sealed class ContextControlLogEntryViewModel(string level, string message)
{
    public string Time { get; } = DateTime.Now.ToString("HH:mm:ss");
    public string Level { get; } = level;
    public string Message { get; } = message;
}
