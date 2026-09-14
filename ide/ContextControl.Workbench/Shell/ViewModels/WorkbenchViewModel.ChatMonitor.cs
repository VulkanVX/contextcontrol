using Avalonia;

namespace ContextControl.Workbench.ViewModels;

public sealed partial class WorkbenchViewModel
{
    public bool IsChatProgressPanelEnabled
    {
        get => _workbenchSettings.ChatProgressPanelEnabled;
        set
        {
            if (_workbenchSettings.ChatProgressPanelEnabled == value) return;
            _workbenchSettings.ChatProgressPanelEnabled = value;
            OnPropertyChanged();
            SaveAppearanceSettings();
        }
    }
    public bool IsChatMonitorEnabled
    {
        get => _workbenchSettings.ChatMonitorEnabled;
        set
        {
            if (_workbenchSettings.ChatMonitorEnabled == value) return;
            _workbenchSettings.ChatMonitorEnabled = value;
            OnPropertyChanged();
            SaveAppearanceSettings();
        }
    }

    public PixelPoint? ChatMonitorPosition => _workbenchSettings.ChatMonitorX is { } x && _workbenchSettings.ChatMonitorY is { } y ? new PixelPoint(x, y) : null;

    public void SaveChatMonitorPosition(PixelPoint position)
    {
        _workbenchSettings.ChatMonitorX = position.X;
        _workbenchSettings.ChatMonitorY = position.Y;
        SaveAppearanceSettings();
    }
}
