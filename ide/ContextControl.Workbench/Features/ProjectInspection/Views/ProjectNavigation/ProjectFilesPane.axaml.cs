using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using ContextControl.Workbench.Controls;

namespace ContextControl.Workbench.Views.MainWindowParts;

public sealed partial class ProjectFilesPane : UserControl
{
    public ProjectFilesPane()
    {
        InitializeComponent();
    }

    internal ScrollViewer TreeList => ProjectTreeList;
    internal Border TreeSearchPanel => ProjectTreeSearchPanel;
    internal TextBox TreeSearchBox => ProjectTreeSearchBox;
    internal ProjectTreeRenderControl TreeView => ProjectTreeView;

    private MainWindow? OwnerWindow => this.FindAncestorOfType<MainWindow>();

    private void OnProjectTreeSearchKeyDown(object? sender, KeyEventArgs e) => OwnerWindow?.OnProjectTreeSearchKeyDown(sender, e);

    private void OnProjectTreeViewPointerPressed(object? sender, PointerPressedEventArgs e) => OwnerWindow?.OnProjectTreeViewPointerPressed(sender, e);

    private void OnProjectTreeViewDoubleTapped(object? sender, TappedEventArgs e) => OwnerWindow?.OnProjectTreeViewDoubleTapped(sender, e);
}
