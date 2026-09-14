using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace ContextControl.Workbench.Views.MainWindowParts;

public sealed partial class HistoryPane : UserControl
{
    public HistoryPane()
    {
        InitializeComponent();
    }

    private MainWindow? OwnerWindow => this.FindAncestorOfType<MainWindow>();

    private void OnHistoryVersionPointerPressed(object? sender, PointerPressedEventArgs e) => OwnerWindow?.OnHistoryVersionPointerPressed(sender, e);
}
