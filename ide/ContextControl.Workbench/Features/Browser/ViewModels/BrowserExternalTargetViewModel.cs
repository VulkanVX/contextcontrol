using ContextControl.Workbench.Services;

namespace ContextControl.Workbench.ViewModels;

public sealed class BrowserExternalTargetViewModel(ExternalBrowserTarget target)
{
    public ExternalBrowserTarget Target { get; } = target;

    public string Key => Target.Key;

    public string Name => Target.Name;

    public string ToolTip => string.IsNullOrWhiteSpace(Target.PathOrName)
        ? "Open with the operating system default browser"
        : Target.PathOrName;
}
