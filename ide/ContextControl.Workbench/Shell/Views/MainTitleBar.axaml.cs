using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace ContextControl.Workbench.Views.MainWindowParts;

public sealed partial class MainTitleBar : UserControl
{
    public MainTitleBar()
    {
        InitializeComponent();
    }

    internal Button EditMenu => EditMenuButton;

    private MainWindow? OwnerWindow => this.FindAncestorOfType<MainWindow>();

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e) => OwnerWindow?.OnTitleBarPointerPressed(sender, e);

    private void OnEditOpenProjectClick(object? sender, RoutedEventArgs e) => OwnerWindow?.OnEditOpenProjectClick(sender, e);

    private void OnEditNewProjectClick(object? sender, RoutedEventArgs e) => OwnerWindow?.OnEditNewProjectClick(sender, e);

    private void OnEditSettingsClick(object? sender, RoutedEventArgs e) => OwnerWindow?.OnEditSettingsClick(sender, e);

    private void OnMinimizeClick(object? sender, RoutedEventArgs e) => OwnerWindow?.OnMinimizeClick(sender, e);

    private void OnMaximizeRestoreClick(object? sender, RoutedEventArgs e) => OwnerWindow?.OnMaximizeRestoreClick(sender, e);

    private void OnCloseClick(object? sender, RoutedEventArgs e) => OwnerWindow?.OnCloseClick(sender, e);
}
