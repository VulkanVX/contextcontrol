using Avalonia.Controls;
using Avalonia.VisualTree;
using ContextControl.Workbench.Controls;

namespace ContextControl.Workbench.Views.MainWindowParts;

public sealed partial class BrowserSurface : UserControl
{
    public BrowserSurface()
    {
        InitializeComponent();
    }

    internal WebView2Host BrowserWebViewControl => BrowserWebView;

    private MainWindow? OwnerWindow => this.FindAncestorOfType<MainWindow>();

    private void OnBrowserNavigationStarted(object? sender, WebView2NavigationStartingEventArgs e) => OwnerWindow?.OnBrowserNavigationStarted(sender, e);

    private void OnBrowserNavigationCompleted(object? sender, WebView2NavigationCompletedEventArgs e) => OwnerWindow?.OnBrowserNavigationCompleted(sender, e);

    private void OnBrowserInitializationFailed(object? sender, WebView2InitializationFailedEventArgs e) => OwnerWindow?.OnBrowserInitializationFailed(sender, e);
}
