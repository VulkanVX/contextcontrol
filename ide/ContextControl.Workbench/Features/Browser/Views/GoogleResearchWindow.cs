using System.Diagnostics;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ContextControl.Workbench.Controls;
using ContextControl.Workbench.Services;

namespace ContextControl.Workbench.Views;

public sealed class GoogleResearchWindow : Window
{
    private readonly WebView2Host _browser;
    private readonly TextBlock _status;
    private readonly TextBlock _address;
    private readonly GoogleResearchReader _reader;
    public bool IsClosed { get; private set; }

    public GoogleResearchWindow(Action<string>? diagnostic = null)
    {
        Title = "Google research · ContextControl";
        Width = 980;
        Height = 720;
        MinWidth = 540;
        MinHeight = 360;
        ShowActivated = false;
        Background = new SolidColorBrush(Color.Parse("#181E2B"));
        _status = new TextBlock { Text = "Searching Google…", FontSize = 14, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap };
        _address = new TextBlock { FontSize = 11, Foreground = Brushes.LightSteelBlue, TextTrimming = TextTrimming.CharacterEllipsis };
        var cancel = new Button { Content = "Stop research", HorizontalAlignment = HorizontalAlignment.Right };
        cancel.Click += (_, _) => _reader?.Cancel();
        var instructions = new TextBlock
        {
            Text = "If Google asks for consent or verification, complete it here. Research continues automatically. Closing this window stops the current request.",
            FontSize = 12, Foreground = Brushes.LightGray, TextWrapping = TextWrapping.Wrap
        };
        var header = new StackPanel { Margin = new Thickness(14, 10), Spacing = 7 };
        header.Children.Add(_status);
        header.Children.Add(_address);
        header.Children.Add(instructions);
        header.Children.Add(cancel);
        _browser = new WebView2Host
        {
            UserDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ContextControl", "GoogleResearch"),
            AllowDownloads = false,
            NavigationFilter = GoogleSearchContext.IsPublicWebUrl
        };
        _browser.Navigate("https://www.google.com/");
        _browser.NavigationCompleted += (_, e) => diagnostic?.Invoke($"Browser navigation: {(e.Succeeded ? "ready" : "failed")} {e.Url}");
        _browser.InitializationFailed += (_, e) => diagnostic?.Invoke(e.Message);
        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        layout.Children.Add(header);
        Grid.SetRow(_browser, 1);
        layout.Children.Add(_browser);
        Content = layout;
        _reader = new GoogleResearchReader(_browser, status => _status.Text = status, () => { if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal; Activate(); }, () => IsClosed);
        Closed += (_, _) => { IsClosed = true; _reader.Cancel(); };
    }

    public Task<GoogleSearchResult> SearchAsync(string query, CancellationToken token) => _reader.SearchAsync(query, token);
    public Task<GooglePageContent> ReadPageAsync(GoogleSearchSource source, CancellationToken token) => _reader.ReadPageAsync(source, token);
    public static bool MatchesSourcePage(string requested, string? actual) => GoogleResearchReader.MatchesSourcePage(requested, actual);
}
