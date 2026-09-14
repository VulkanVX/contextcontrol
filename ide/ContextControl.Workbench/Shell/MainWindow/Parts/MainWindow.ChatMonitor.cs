using Avalonia.Controls;
using ContextControl.Workbench.ViewModels;

namespace ContextControl.Workbench.Views;

public sealed partial class MainWindow
{
    private ChatMonitorWindow? _chatMonitorWindow;
    private ContextControl.Workbench.Services.WorkspaceResearchBrowser? _googleResearchBrowser;

    private void UpdateChatMonitor()
    {
        if (ViewModel is not { } workbench) return;
        if (!workbench.IsChatMonitorEnabled) { _chatMonitorWindow?.Hide(); return; }
        _chatMonitorWindow ??= new ChatMonitorWindow(workbench, NavigateMonitoredChatAsync);
        if (!_chatMonitorWindow.IsVisible) _chatMonitorWindow.Show();
    }

    private async Task<bool> NavigateMonitoredChatAsync(ChatMonitorEntryViewModel entry, bool activate)
    {
        if (ViewModel is not { } workbench || !Directory.Exists(entry.ProjectRoot)) return false;
        if (!string.Equals(workbench.ContextControl.ActiveProjectRoot, entry.ProjectRoot, StringComparison.OrdinalIgnoreCase))
            await workbench.LoadProjectAsync(entry.ProjectRoot);
        if (!workbench.ContextControl.OpenMonitoredChat(entry)) return false;
        if (activate)
        {
            Show();
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Activate();
        }
        return true;
    }

    protected override void OnClosed(EventArgs e)
    {
        _googleResearchBrowser?.Dispose();
        _googleResearchBrowser = null;
        _chatMonitorWindow?.CloseWithWorkbench();
        _chatMonitorWindow = null;
        ViewModel?.FlushAppearanceSettings();
        base.OnClosed(e);
    }
}
