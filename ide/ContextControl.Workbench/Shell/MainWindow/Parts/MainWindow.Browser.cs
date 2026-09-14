using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using ContextControl.Workbench.Controls;
using ContextControl.Workbench.ViewModels;

namespace ContextControl.Workbench.Views;

public sealed partial class MainWindow
{
    private void ShowResearchTab(BrowserTabViewModel tab)
    {
        if (ViewModel is not { } viewModel) return;
        viewModel.SwitchWorkspaceModeCommand.Execute(viewModel.WorkspaceModes.First(mode => mode.Key == "browser"));
        viewModel.BrowserPane.SelectTab(tab);
    }
    internal void OnResearchActivityClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var tab = ViewModel?.BrowserPane.Tabs.FirstOrDefault(tab => tab.IsAgentWorking);
        if (tab is not null) ShowResearchTab(tab);
        e.Handled = true;
    }
    internal void OnBrowserOpenClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        NavigateBrowserFromAddressBar();
        e.Handled = true;
    }

    internal void OnBrowserUrlKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        NavigateBrowserFromAddressBar();
        e.Handled = true;
    }

    internal void OnBrowserBackClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (ViewModel?.BrowserPane.SelectedTab?.IsAgentWorking == true) return;
        try
        {
            BrowserWebView.GoBack();
        }
        catch (Exception ex)
        {
            ViewModel?.BrowserPane.SetBrowserUnavailable($"Back failed: {ex.Message}");
        }

        e.Handled = true;
    }

    internal void OnBrowserForwardClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (ViewModel?.BrowserPane.SelectedTab?.IsAgentWorking == true) return;
        try
        {
            BrowserWebView.GoForward();
        }
        catch (Exception ex)
        {
            ViewModel?.BrowserPane.SetBrowserUnavailable($"Forward failed: {ex.Message}");
        }

        e.Handled = true;
    }

    internal void OnBrowserRefreshClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (ViewModel?.BrowserPane.SelectedTab?.IsAgentWorking == true) return;
        try
        {
            BrowserWebView.Reload();
        }
        catch (Exception ex)
        {
            ViewModel?.BrowserPane.SetBrowserUnavailable($"Refresh failed: {ex.Message}");
        }

        e.Handled = true;
    }

    internal void OnBrowserNewTabClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (ViewModel?.BrowserPane is not { } browserPane)
        {
            return;
        }

        browserPane.AddTab();
        e.Handled = true;
    }

    internal void OnBrowserTabPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (IsBrowserTabCloseSource(e.Source)
            || sender is not Control { DataContext: BrowserTabViewModel tab }
            || ViewModel?.BrowserPane is not { } browserPane)
        {
            return;
        }

        browserPane.SelectTab(tab);

        e.Handled = true;
    }

    internal void OnBrowserTabCloseClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: BrowserTabViewModel tab }
            || ViewModel?.BrowserPane is not { } browserPane)
        {
            return;
        }

        browserPane.CloseTab(tab);

        e.Handled = true;
    }

    internal void OnBrowserNavigationStarted(object? sender, WebView2NavigationStartingEventArgs e)
    {
        ViewModel?.BrowserPane.BeginNavigation(e.Url);
    }

    internal void OnBrowserNavigationCompleted(object? sender, WebView2NavigationCompletedEventArgs e)
    {
        ViewModel?.BrowserPane.CompleteNavigation(
            e.Url,
            e.Succeeded,
            e.CanGoBack,
            e.CanGoForward);
    }

    internal void OnBrowserInitializationFailed(object? sender, WebView2InitializationFailedEventArgs e)
    {
        ViewModel?.BrowserPane.SetBrowserUnavailable(e.Message);
    }

    private void NavigateBrowserFromAddressBar()
    {
        if (ViewModel?.BrowserPane is not { } browserPane)
        {
            return;
        }

        var url = browserPane.NormalizeUrl();
        if (browserPane.SelectedTab?.IsAgentWorking == true) browserPane.AddTab();
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            browserPane.SetBrowserUnavailable("Invalid URL.");
            return;
        }

        try
        {
            browserPane.BeginNavigation(uri.ToString());
            BrowserWebView.Navigate(uri.ToString());
        }
        catch (Exception ex)
        {
            browserPane.SetBrowserUnavailable($"Embedded browser unavailable: {ex.Message}");
        }
    }

    private void NavigateBrowserToUrl(string url)
    {
        if (ViewModel?.BrowserPane is not { } browserPane)
        {
            return;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            browserPane.SetBrowserUnavailable("Invalid URL.");
            return;
        }

        try
        {
            browserPane.BeginNavigation(uri.ToString());
            BrowserWebView.Navigate(uri.ToString());
        }
        catch (Exception ex)
        {
            browserPane.SetBrowserUnavailable($"Embedded browser unavailable: {ex.Message}");
        }
    }

    private static bool IsBrowserTabCloseSource(object? source)
    {
        var control = source as Control;
        while (control is not null)
        {
            if (control.Classes.Contains("browser-tab-close"))
            {
                return true;
            }

            control = control.GetVisualParent() as Control;
        }

        return false;
    }
}
