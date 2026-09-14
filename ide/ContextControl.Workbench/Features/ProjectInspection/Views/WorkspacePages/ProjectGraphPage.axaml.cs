using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using ContextControl.Workbench.Controls;

namespace ContextControl.Workbench.Views.MainWindowParts;

public sealed partial class ProjectGraphPage : UserControl
{
    public ProjectGraphPage()
    {
        InitializeComponent();
    }

    internal ProjectGraphRenderControl GraphView => ProjectGraphView;
    internal Border SearchPanel => ProjectGraphSearchPanel;
    internal TextBox SearchBox => ProjectGraphSearchBox;

    private MainWindow? OwnerWindow => this.FindAncestorOfType<MainWindow>();

    private void OnProjectGraphSearchKeyDown(object? sender, KeyEventArgs e) => OwnerWindow?.OnProjectGraphSearchKeyDown(sender, e);

    private void OnProjectGraphCopyTreeClick(object? sender, RoutedEventArgs e) => OwnerWindow?.OnProjectGraphCopyTreeClick(sender, e);
}
