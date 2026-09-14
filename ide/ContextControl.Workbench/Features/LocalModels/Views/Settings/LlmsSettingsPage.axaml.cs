using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using ContextControl.Workbench.Services;

namespace ContextControl.Workbench.Views.ThemeSettingsWindowParts;

public sealed partial class LlmsSettingsPage : UserControl
{
    private const string HuggingFaceTokenPageUrl = "https://huggingface.co/settings/tokens";
    private const string HuggingFaceTokenDocsUrl = "https://huggingface.co/docs/hub/security-tokens";

    public LlmsSettingsPage()
    {
        InitializeComponent();
    }

    private ThemeSettingsWindow? OwnerWindow => this.FindAncestorOfType<ThemeSettingsWindow>();

    private void OnOllamaModelsBrowseClick(object? sender, RoutedEventArgs e) => OwnerWindow?.OnOllamaModelsBrowseClick(sender, e);

    private void OnHuggingFaceTokenTutorialClick(object? sender, RoutedEventArgs e) => OwnerWindow?.OnHuggingFaceTokenTutorialClick(sender, e);

    private void OnHuggingFaceTokenPageClick(object? sender, RoutedEventArgs e) => OpenExternal(HuggingFaceTokenPageUrl);

    private void OnHuggingFaceTokenDocsClick(object? sender, RoutedEventArgs e) => OpenExternal(HuggingFaceTokenDocsUrl);

    private static void OpenExternal(string url)
    {
        try
        {
            ExternalBrowserService.Open(ExternalBrowserService.DefaultTarget, url);
        }
        catch
        {
            // Settings remain usable even if the system browser cannot be opened.
        }
    }
}
