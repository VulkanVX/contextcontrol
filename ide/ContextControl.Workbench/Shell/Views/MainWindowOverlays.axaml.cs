using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace ContextControl.Workbench.Views.MainWindowParts;

public sealed partial class MainWindowOverlays : UserControl
{
    public MainWindowOverlays()
    {
        InitializeComponent();
    }

    internal Border FileDropOverlay => PromptFileDropOverlay;

    private MainWindow? OwnerWindow => this.FindAncestorOfType<MainWindow>();

    private void OnResizeLeftPointerPressed(object? sender, PointerPressedEventArgs e) => OwnerWindow?.OnResizeLeftPointerPressed(sender, e);

    private void OnResizeRightPointerPressed(object? sender, PointerPressedEventArgs e) => OwnerWindow?.OnResizeRightPointerPressed(sender, e);

    private void OnResizeTopPointerPressed(object? sender, PointerPressedEventArgs e) => OwnerWindow?.OnResizeTopPointerPressed(sender, e);

    private void OnResizeBottomPointerPressed(object? sender, PointerPressedEventArgs e) => OwnerWindow?.OnResizeBottomPointerPressed(sender, e);

    private void OnResizeTopLeftPointerPressed(object? sender, PointerPressedEventArgs e) => OwnerWindow?.OnResizeTopLeftPointerPressed(sender, e);

    private void OnResizeTopRightPointerPressed(object? sender, PointerPressedEventArgs e) => OwnerWindow?.OnResizeTopRightPointerPressed(sender, e);

    private void OnResizeBottomLeftPointerPressed(object? sender, PointerPressedEventArgs e) => OwnerWindow?.OnResizeBottomLeftPointerPressed(sender, e);

    private void OnResizeBottomRightPointerPressed(object? sender, PointerPressedEventArgs e) => OwnerWindow?.OnResizeBottomRightPointerPressed(sender, e);
}
