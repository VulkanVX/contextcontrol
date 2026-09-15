using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using ContextControl.Workbench.Services;

namespace ContextControl.Workbench.Views;

public partial class GameLabWindow : Window
{
    private readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly List<string> _errors = [];
    private string _lastRun = "";
    private bool _polling, _running, _expanded;
    private int _revision;
    private Action<string>? _repair;
    public bool IsClosed { get; private set; }
    public GameLabWindow()
    {
        InitializeComponent();
        SourceEditor.SyntaxHighlighting = GameEditorSyntax.Create();
        GameBrowser.UserDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ContextControl", "GameLabBrowser");
        GameBrowser.AllowDownloads = false;
        GameBrowser.BlockExternalResources = true;
        GameBrowser.NavigationFilter = url => url == "about:blank";
        GameBrowser.NavigateHtml(GamePreviewDocument.Stopped);
        GameBrowser.InitializationFailed += (_, e) => AddError(e.Message);
        _poll.Tick += async (_, _) => await PollAsync();
        Closed += (_, _) => { IsClosed = true; _running = false; _poll.Stop(); };
    }
    public void LoadGame(GameArtifact game, Action<string> repair)
    {
        _repair = repair;
        GameTitle.Text = game.Title;
        SourceEditor.Text = game.Html;
        Run(game.Html);
    }
    private void OnRun(object? sender, RoutedEventArgs e) => Run(SourceEditor.Text ?? "");
    private void OnRestart(object? sender, RoutedEventArgs e) { if (_lastRun.Length > 0) Run(_lastRun); }
    private void Run(string html)
    {
        try
        {
            var document = GamePreviewDocument.Build(html);
            _errors.Clear(); ConsoleText.Text = "Starting game…"; ConsoleTitle.Text = "CONSOLE · no errors reported";
            _lastRun = html; _running = true; StateLabel.Text = "STARTING";
            RevisionLabel.Text = $"Run {++_revision} · offline preview";
            GameBrowser.NavigateHtml(document); _poll.Start();
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException) { AddError(ex.Message); }
    }
    private void OnStop(object? sender, RoutedEventArgs e)
    {
        _running = false; _poll.Stop(); GameBrowser.NavigateHtml(GamePreviewDocument.Stopped); StateLabel.Text = "STOPPED";
    }
    private void OnDesktop(object? sender, RoutedEventArgs e) { StageFrame.Width = double.NaN; StageFrame.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch; ViewportLabel.Text = "Responsive desktop"; }
    private void OnMobile(object? sender, RoutedEventArgs e) { StageFrame.Width = 360; StageFrame.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center; ViewportLabel.Text = "Mobile · 360 px"; }
    private void OnExpand(object? sender, RoutedEventArgs e)
    {
        _expanded = !_expanded; SourcePanel.IsVisible = !_expanded;
        WorkspaceGrid.ColumnDefinitions[1].Width = new GridLength(_expanded ? 0 : 10);
        WorkspaceGrid.ColumnDefinitions[2].Width = new GridLength(_expanded ? 0 : 360);
    }
    private void OnFix(object? sender, RoutedEventArgs e)
    {
        var request = "Improve this browser game. " + (_errors.Count > 0 ? "Fix these runtime errors:\n" + string.Join("\n", _errors.Take(12)) : "Check controls, collisions, restart and game-over behavior. Describe the changes.");
        _repair?.Invoke(GameArtifact.Prompt(request, new(GameTitle.Text ?? "Game", SourceEditor.Text ?? "")));
        OnStop(sender, e);
        Hide();
    }
    private async void OnScreenshot(object? sender, RoutedEventArgs e)
    {
        try
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "Save game screenshot", SuggestedFileName = "game.png", DefaultExtension = "png", FileTypeChoices = [new FilePickerFileType("PNG image") { Patterns = ["*.png"] }] });
            if (file is null) return;
            using var bitmap = await GameBrowser.CaptureActionPreviewAsync(0);
            if (bitmap is null) { AddError("The game preview is not ready for a screenshot."); return; }
            await using var stream = await file.OpenWriteAsync(); stream.SetLength(0); bitmap.Save(stream);
            ConsoleText.Text = "Screenshot saved: " + file.Name;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException) { AddError(ex.Message); }
    }
    private async void OnSave(object? sender, RoutedEventArgs e)
    {
        try
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "Save playable game", SuggestedFileName = "index.html", DefaultExtension = "html", FileTypeChoices = [new FilePickerFileType("HTML game") { Patterns = ["*.html"] }] });
            if (file is null) return;
            await using var stream = await file.OpenWriteAsync(); stream.SetLength(0);
            await stream.WriteAsync(Encoding.UTF8.GetBytes(SourceEditor.Text ?? ""));
            ConsoleText.Text = "Saved " + file.Name;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException) { AddError(ex.Message); }
    }
    private void AddError(string error)
    {
        if (_errors.Contains(error) || _errors.Count >= 50) return;
        _errors.Add(error); StateLabel.Text = "CHECK ERRORS"; ConsoleTitle.Text = $"CONSOLE · {_errors.Count} error(s)";
        ConsoleText.Text = string.Join("\n", _errors);
    }
    private async Task PollAsync()
    {
        if (_polling || !_running || !GameBrowser.IsReady || GameBrowser.IsNavigating) return;
        _polling = true;
        var revision = _revision;
        try
        {
            var response = await GameBrowser.ExecuteScriptAsync(GamePreviewDocument.PollScript);
            if (!_running || IsClosed || revision != _revision) return;
            var json = JsonSerializer.Deserialize<string>(response);
            if (json is null) return;
            using var events = JsonDocument.Parse(json);
            foreach (var item in events.RootElement.EnumerateArray())
            {
                if (item.GetProperty("kind").GetString() == "game-error") AddError(item.GetProperty("text").GetString() ?? "Game error");
                else if (item.GetProperty("kind").GetString() == "game-stats") RevisionLabel.Text = $"Run {_revision} · {item.GetProperty("text").GetString()} preview FPS";
                else if (_errors.Count == 0) { StateLabel.Text = "PLAYING"; ConsoleText.Text = "Game loaded. Click the stage to play. No runtime errors reported."; }
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or System.Runtime.InteropServices.COMException) { if (_running) AddError(ex.Message); }
        finally { _polling = false; }
    }
}
