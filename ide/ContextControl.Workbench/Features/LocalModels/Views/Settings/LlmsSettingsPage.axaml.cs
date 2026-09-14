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

    private async void OnRuntimeModelBrowseClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: ContextControl.Workbench.ViewModels.LocalRuntimeProfileViewModel profile }
            || TopLevel.GetTopLevel(this) is not { } top) return;
        var files = await top.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            Title = "Choose a GGUF model (existing Ollama GGUF blobs also work)", AllowMultiple = false,
            FileTypeFilter = [new("GGUF model") { Patterns = ["*.gguf"] }, new("All files") { Patterns = ["*"] }]
        });
        if (files.FirstOrDefault()?.Path is { IsFile: true } path) profile.ModelPath = path.LocalPath;
    }
}
