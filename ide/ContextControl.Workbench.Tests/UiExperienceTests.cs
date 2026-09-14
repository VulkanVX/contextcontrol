using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ContextControl.Workbench;
using ContextControl.Workbench.Controls;
using ContextControl.Workbench.Services;
using ContextControl.Workbench.ViewModels;
using ContextControl.Workbench.Views;

internal static class UiExperienceTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int _checks;
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        _checks++;
    }
    private static object? Call(object target, string name, params object?[] args) => target.GetType().GetMethods(Private)
        .Single(method => method.Name == name && method.GetParameters().Length == args.Length).Invoke(target, args);
    private static T Read<T>(object target, string name) => (T)target.GetType().GetProperty(name, Private | BindingFlags.Public)!.GetValue(target)!;

    public static void Run(string? output)
    {
        AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).UseSkia().WithInterFont().SetupWithoutStarting();
        var root = Path.Combine(Path.GetTempPath(), "ContextControlUiTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        output = Path.GetFullPath(output ?? Path.Combine(root, "snapshots"));
        Directory.CreateDirectory(output);
        var settings = WorkbenchSettings.Load(root);
        var ctor = typeof(WorkbenchViewModel).GetConstructors(Private).Single();
        using var workbench = (WorkbenchViewModel)ctor.Invoke([
            new ObservableCollection<ProjectTabViewModel>(), new ObservableCollection<ProjectNodeViewModel>(),
            new Dictionary<string, FileHistoryViewModel>(), null, true, settings, false]);
        workbench.SelectedTheme = workbench.Themes.Single(theme => theme.Key == "studio");
        var context = workbench.ContextControl;
        Typography(output);
        Headers(output);
        Monitor(context, workbench, root, output);
        var settingsWindow = new ThemeSettingsWindow { DataContext = workbench };
        settingsWindow.ApplyTheme("studio", uiFontSize: 11);
        Snapshot(settingsWindow, 1080, 700, Path.Combine(output, "settings-100.png"));
        workbench.UiFontSize = 22;
        settingsWindow.ApplyTheme("studio", uiFontSize: 22);
        Check(WorkbenchTypography.GetScale(settingsWindow) == 2, "Settings must apply the actual selected interface scale.");
        Snapshot(settingsWindow, 1080, 700, Path.Combine(output, "settings-200.png"));
        settingsWindow.Close();
        MainWindowFlow(workbench, output);
        workbench.UiFontSize = 17.5;
        workbench.IsChatMonitorEnabled = false;
        workbench.SaveChatMonitorPosition(new PixelPoint(-500, 210));
        workbench.FlushAppearanceSettings();
        var restored = WorkbenchSettings.Load(root);
        Check(restored.UiFontSize == 17.5 && !restored.ChatMonitorEnabled && restored.ChatMonitorX == -500,
            "Latest scale, monitor visibility and negative-screen position must survive restart.");
        Console.WriteLine($"UI experience regression passed: {_checks} checks. Render artifacts: {output}");
    }

    private static void Typography(string output)
    {
        var label = new TextBlock { Text = "Interface scale · Aa 0123", FontSize = 11 };
        var line = new ActivityLineControl { Text = "Custom renderer · same interface scale", FontSize = 11 };
        var panel = new StackPanel { Children = { label, line }, Margin = new Thickness(20) };
        var window = new Window { Content = panel };
        WorkbenchThemeResources.Apply(window, "studio", uiFontSize: 11);
        var host = window.Content;
        var accent = window.Resources["AccentBrush"];
        Snapshot(window, 600, 140, Path.Combine(output, "type-100.png"));
        var before = label.TranslatePoint(default, line);
        WorkbenchTypography.Apply(window, 22);
        Check(ReferenceEquals(host, window.Content), "Changing scale must reuse one layout host.");
        Check(ReferenceEquals(accent, window.Resources["AccentBrush"]), "Scale must not rebuild theme brushes.");
        Snapshot(window, 600, 230, Path.Combine(output, "type-200.png"));
        var transform = label.TransformToVisual((Control)window.Content!)!.Value;
        Check(Math.Abs(transform.M11 - 2) < .001 && Math.Abs(transform.M22 - 2) < .001,
            "Authored fixed-size text must scale on both axes.");
        Check(Math.Abs(line.TransformToVisual((Control)window.Content!)!.Value.M11 - 2) < .001,
            "Custom renderers must use the same scale as native text controls.");
        var watch = Stopwatch.StartNew();
        for (var i = 0; i < 1000; i++) WorkbenchTypography.Schedule(window, 8 + (i % 29) * .5);
        watch.Stop();
        Check(WorkbenchTypography.GetScale(window) == 2, "A slider burst must not synchronously relayout the engine.");
        Check(watch.ElapsedMilliseconds < 1000, "Scheduling scale changes should remain lightweight.");
        WorkbenchTypography.Apply(window, double.NaN);
        Check(WorkbenchTypography.GetScale(window) == 1, "Invalid saved sizes must fall back to a readable default.");
        Console.WriteLine($"Scale scheduling: 1,000 updates in {watch.Elapsed.TotalMilliseconds:0.0} ms; no synchronous layout/theme rebuild.");
        window.Close();
    }

    private static void Headers(string output)
    {
        double previousHeader = 0, previousTime = 0;
        foreach (var size in new[] { 8d, 11d, 22d })
        {
            var message = new LocalLlmChatMessageViewModel("assistant", "The header, timestamp and message now share a consistent type scale.\n\nLarge text stays readable without squeezing the header.", "gpt-5-codex", "Codex response", attachments: [new ContextControlAttachmentViewModel("[1] Official documentation", "https://docs.avaloniaui.net/docs/welcome", "web")]);
            var user = new LocalLlmChatMessageViewModel("user", "Make the whole interface easier to read.", "You", "");
            var transcript = new ChatTranscriptRenderControl { ChatFontSize = size, Items = [user, message] };
            var layout = Call(transcript, "BuildMessageLayout", message, 680d)!;
            var header = Read<Rect>(layout, "HeaderRect");
            var time = Read<Rect>(layout, "TimeRect");
            var clip = Read<Rect>(layout, "HeaderMetaClip");
            var bodySize = Read<double>(transcript, "ChatTextFontSize");
            var timestampSize = Read<double>(transcript, "ChatTimestampFontSize");
            Check(header.Height >= Read<double>(transcript, "ChatHeaderFontSize") + 6, "Header must reserve vertical breathing room.");
            Check(time.Height == header.Height && clip.Right < time.X, "Header metadata must not overlap its timestamp.");
            Check(timestampSize < bodySize && timestampSize > previousTime, "Timestamp must grow with body text while remaining smaller.");
            Check(header.Height >= previousHeader, "Header height must not shrink as chat text grows.");
            previousTime = timestampSize; previousHeader = header.Height;
            var window = new Window { Content = new Border { Background = new SolidColorBrush(Color.Parse("#151922")), Child = new ScrollViewer { Content = transcript } } };
            WorkbenchThemeResources.Apply(window, "studio", chatAppearanceKey: "adaptive");
            Snapshot(window, 720, 440, Path.Combine(output, $"chat-{size:0}.png"));
            window.Close();
        }
        Check(previousHeader > 30, "Largest chat text requires a genuinely taller header.");
    }

    private static void Monitor(ContextControlViewModel context, WorkbenchViewModel workbench, string root, string output)
    {
        Call(context, "CreateNewChatSession", true, true);
        var first = context.SelectedChatSession!;
        first.Rename("Typography review");
        context.PromptText = "Keep this draft for the first chat";
        var entry = context.ChatMonitor.Entries.Single();
        Check(context.IsChatMonitored(first), "Explicit new chats should appear automatically.");
        var request = ((ValueTuple<ChatRequestProgressViewModel, IProgress<LocalLlmGenerationProgress>>)Call(context, "CreateGenerationProgress", first, "Local model", "Chat", false)!).Item1;
        request.Status = "Writing"; request.SizeLabel = "128 output tok"; request.SpeedLabel = "42 tok/s";
        var secondRequest = ((ValueTuple<ChatRequestProgressViewModel, IProgress<LocalLlmGenerationProgress>>)Call(context, "CreateGenerationProgress", first, "Local model", "Chat", false)!).Item1;
        Check(context.ChatMonitor.Entries.Count == 1 && entry.IsActive, "Concurrent requests must share one session row.");
        Call(context, "CompleteGenerationProgress", secondRequest);
        Check(first.IsGenerating && entry.IsActive, "Completing one concurrent request must not clear another.");
        context.ToggleChatMonitorSession(first);
        Check(context.ChatMonitor.Entries.Count == 0 && context.ChatSessions.Contains(first), "Removing a row must keep chat history.");
        context.ToggleChatMonitorSession(first);
        entry = context.ChatMonitor.Entries.Single();
        Check(entry.IsActive, "Re-adding a running session must reconnect its progress.");
        Call(context, "CreateNewChatSession", true, true);
        var second = context.SelectedChatSession!;
        second.Rename("Release notes");
        context.PromptText = "A separate draft";
        Check(context.OpenMonitoredChat(entry) && context.PromptText == "Keep this draft for the first chat", "Opening a monitored chat must restore its own draft.");
        var window = new ChatMonitorWindow(workbench, (target, _) => Task.FromResult(context.OpenMonitoredChat(target)));
        window.Show(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
        Check(window.Bounds.Height < 180, "Two monitor rows must remain compact at their natural height.");
        Snapshot(window, 460, 150, Path.Combine(output, "monitor-compact.png"));
        var animationTimer = (DispatcherTimer)typeof(ActivityLineControl).GetField("Timer", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        Check(animationTimer.IsEnabled, "A visible active row must animate.");
        window.Hide();
        Check(!animationTimer.IsEnabled, "Hiding the floating window must suspend its animation timer.");
        window.Show();
        Call(window, "OnReply", new Button { DataContext = entry }, new Avalonia.Interactivity.RoutedEventArgs());
        Check(window.FindControl<Border>("ReplyPanel")!.IsVisible, "Reply action must expand the composer.");
        var file = Path.Combine(root, "notes.txt"); File.WriteAllText(file, "fixture attachment");
        context.AttachFiles([file]);
        Check(context.Attachments.Any(item => item.Path == file), "Quick reply must share real attachment state.");
        Snapshot(window, 460, 420, Path.Combine(output, "monitor-reply.png"));
        context.OpenMonitoredChat(context.ChatMonitor.Entries.Single(item => item.SessionId == second.Id));
        Check(!window.FindControl<Border>("ReplyPanel")!.IsVisible, "Changing target must close the reply composer before it can send to another chat.");
        Check(context.PromptText == "A separate draft", "Navigating between replies must preserve distinct drafts.");
        context.PromptText = "Draft written immediately before exit";
        context.FlushPendingChatDraft();
        var reloadedContext = new ContextControlViewModel(WorkbenchSettings.Load(root), refreshProviders: false);
        Check(reloadedContext.PromptText == "Draft written immediately before exit", "Exit must flush a draft before its debounce timer fires.");
        reloadedContext.ChatMonitor.Dispose();
        var otherProject = Path.Combine(root, "other-project"); Directory.CreateDirectory(otherProject);
        context.SetActiveProject(otherProject, null);
        Check(!context.IsMonitorTargetSelected(entry) && !context.OpenMonitoredChat(entry), "Wrong-project navigation must fail closed.");
        Call(context, "CompleteGenerationProgress", request);
        Check(!first.IsGenerating && !entry.IsActive, "Completion after a project switch must clear the originating session.");
        context.ChatMonitor.Flush();
        using var restored = new ChatMonitorService(root);
        Check(restored.Entries.Count == 2 && restored.Entries.All(item => !item.IsActive), "Restart must restore membership without phantom running requests.");
        var clamped = ChatMonitorWindow.ClampPosition(new PixelPoint(8000, -600), new PixelRect(-1920, 0, 1920, 1080), new PixelSize(460, 420));
        Check(clamped.X == -460 && clamped.Y == 0, "Monitor must remain reachable on negative-coordinate displays.");
        window.CloseWithWorkbench();
        var timer = (DispatcherTimer)typeof(ActivityLineControl).GetField("Timer", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        Check(!timer.IsEnabled, "Detached or idle monitor controls must stop animation scheduling.");
    }

    private static void MainWindowFlow(WorkbenchViewModel workbench, string output)
    {
        workbench.IsChatMonitorEnabled = false;
        workbench.UiFontSize = 11;
        Call(workbench, "SwitchWorkspaceMode", workbench.WorkspaceModes.Single(mode => mode.Key == "chat"));
        var context = workbench.ContextControl;
        var session = context.SelectedChatSession!;
        Call(context, "AppendChatMessageToSession", session, new LocalLlmChatMessageViewModel("user", "Review the interface and keep an eye on my other chats."));
        Call(context, "AppendChatMessageToSession", session, new LocalLlmChatMessageViewModel("assistant", "Your chats are available in Chat Monitor. Open a conversation or use quick reply while staying in your workspace.", "gpt-5-codex", "Codex chat"));
        Check(context.IsChatMonitored(session), "Continuing an existing unmonitored chat must add it automatically.");
        var main = new MainWindow { DataContext = workbench };
        Snapshot(main, 1360, 840, Path.Combine(output, "workbench-studio.png"));
        context.SwitchPromptToContextCommand.Execute(null);
        context.IsGoogleSearchEnabled = true;
        var sources = new[] { new ContextControlAttachmentViewModel("[1] Official Avalonia documentation", "https://docs.avaloniaui.net/docs/overview", "web") };
        Call(context, "AppendChatMessageToSession", session, new LocalLlmChatMessageViewModel("assistant", "Avalonia builds desktop applications with .NET [1].", "granite3.3:2b", "raw", attachments: sources));
        Snapshot(main, 1360, 840, Path.Combine(output, "google-research-chat.png"));
        var google = main.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Google auto") && button.IsEffectivelyVisible);
        Check(google.Command == context.ToggleGoogleSearchCommand, "Google control must use the live local chat setting.");
        google.Command!.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Check(Equals(google.Content, "Google off") && !context.IsGoogleSearchEnabled, "Clicking the composer research switch must update its real binding.");
        context.IsGoogleSearchEnabled = true;
        var accent = main.Resources["AccentBrush"];
        var watch = Stopwatch.StartNew();
        for (var i = 0; i < 300; i++) workbench.UiFontSize = 8 + (i % 29) * .5;
        workbench.UiFontSize = 16.5;
        watch.Stop();
        Check(ReferenceEquals(accent, main.Resources["AccentBrush"]), "Actual settings notifications must preserve palette resources during a slider sweep.");
        Check(watch.ElapsedMilliseconds < 1000, "Real workbench scale setting notifications must stay responsive.");
        Thread.Sleep(120); Dispatcher.UIThread.RunJobs();
        Check(Math.Abs(WorkbenchTypography.GetScale(main) - 1.5) < .001, "Debounced scale must settle to the final value.");
        Snapshot(main, 1360, 840, Path.Combine(output, "workbench-150.png"));
        Check(google.TranslatePoint(new Point(0, google.Bounds.Height), main)?.Y <= main.Bounds.Height, "Research control must remain inside the scaled composer.");
        var send = main.GetVisualDescendants().OfType<Button>().First(button => button.Classes.Contains("cc-prompt-send") && button.IsEffectivelyVisible);
        Check(send.TranslatePoint(new Point(0, send.Bounds.Height), main)?.Y <= main.Bounds.Height,
            "Wrapping navigation at large scale must leave the Send button inside the window.");
        Console.WriteLine($"Workbench scale sweep: 301 bound setting changes in {watch.Elapsed.TotalMilliseconds:0.0} ms.");
        workbench.IsChatMonitorEnabled = true;
        var floating = (ChatMonitorWindow?)typeof(MainWindow).GetField("_chatMonitorWindow", Private)!.GetValue(main);
        Check(floating?.IsVisible == true, "Settings toggle must show the actual floating window.");
        workbench.IsChatMonitorEnabled = false;
        Check(floating?.IsVisible == false, "Settings toggle must hide the actual floating window.");
        main.Close();
    }

    private static void Snapshot(Window window, int width, int height, string path)
    {
        window.SizeToContent = SizeToContent.Manual;
        window.Width = width; window.Height = height;
        window.Show();
        window.Width = width; window.Height = height;
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        var content = (Control)window.Content!;
        using var bitmap = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96, 96));
        bitmap.Render(content);
        bitmap.Save(path);
    }
}
