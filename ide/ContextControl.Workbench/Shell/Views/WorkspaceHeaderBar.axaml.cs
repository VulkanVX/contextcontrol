using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using ContextControl.Workbench.ViewModels;

namespace ContextControl.Workbench.Views.MainWindowParts;

public sealed partial class WorkspaceHeaderBar : UserControl
{
    public WorkspaceHeaderBar()
    {
        InitializeComponent();
    }

    private MainWindow? OwnerWindow => this.FindAncestorOfType<MainWindow>();

    private void OnBrowserTabPointerPressed(object? sender, PointerPressedEventArgs e) => OwnerWindow?.OnBrowserTabPointerPressed(sender, e);

    private void OnBrowserTabCloseClick(object? sender, RoutedEventArgs e) => OwnerWindow?.OnBrowserTabCloseClick(sender, e);

    private void OnBrowserBackClick(object? sender, RoutedEventArgs e) => OwnerWindow?.OnBrowserBackClick(sender, e);

    private void OnBrowserForwardClick(object? sender, RoutedEventArgs e) => OwnerWindow?.OnBrowserForwardClick(sender, e);

    private void OnBrowserRefreshClick(object? sender, RoutedEventArgs e) => OwnerWindow?.OnBrowserRefreshClick(sender, e);

    private void OnBrowserUrlKeyDown(object? sender, KeyEventArgs e) => OwnerWindow?.OnBrowserUrlKeyDown(sender, e);

    private void OnBrowserNewTabClick(object? sender, RoutedEventArgs e) => OwnerWindow?.OnBrowserNewTabClick(sender, e);

    private void OnBrowserOpenClick(object? sender, RoutedEventArgs e) => OwnerWindow?.OnBrowserOpenClick(sender, e);

    private void OnProjectGraphFitLayoutClick(object? sender, RoutedEventArgs e) => OwnerWindow?.OnProjectGraphFitLayoutClick(sender, e);

    private void OnProjectGraphExportClick(object? sender, RoutedEventArgs e) => OwnerWindow?.OnProjectGraphExportClick();

    private void OnProjectGraphColorSwatchClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: ProjectGraphGenerationColorViewModel color })
        {
            OwnerWindow?.OnProjectGraphGenerationColorClick(color);
        }
    }

    private void OnProjectScannerCopyClick(object? sender, RoutedEventArgs e) => OwnerWindow?.OnProjectScannerCopyClick(sender, e);
}
