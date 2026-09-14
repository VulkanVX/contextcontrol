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
        PhotoPreviews(root, output);
        MarkdownFormatting(root, output);
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

    private static void PhotoPreviews(string root, string output)
    {
        var photoPath = Path.Combine(root, "source-photo.png");
        File.WriteAllBytes(photoPath, GooglePhotoPreviewTests.Photo());
        var sources = new[]
        {
            new ContextControlAttachmentViewModel("[1] Photo source · open page", "https://example.com/article", "web", photoPath),
            new ContextControlAttachmentViewModel("[2] Another source", "https://example.org/article", "web", photoPath),
            new ContextControlAttachmentViewModel("[3] Text-only source", "https://example.net/article", "web"),
            new ContextControlAttachmentViewModel("[4] Unavailable photo", "https://example.edu/article", "web", Path.Combine(root, "missing.jpg"))
        };
        foreach (var (width, size) in new[] { (780, 11d), (380, 22d) })
        {
            var message = new LocalLlmChatMessageViewModel("assistant", "Source photos now appear below the answer. Click a photo to enlarge it, or its title to open the source. [1] [2]", attachments: sources);
            var transcript = new ChatTranscriptRenderControl { ChatFontSize = size, Items = [message] };
            var layout = Call(transcript, "BuildMessageLayout", message, (double)width - 40)!;
            var attachments = Read<System.Collections.IEnumerable>(layout, "Attachments").Cast<object>().ToArray();
            Check(attachments.Count(item => Read<bool>(item, "IsImagePreview")) == 2, "Available web photos must render; missing previews must retain a plain source chip.");
            var card = Read<Rect>(layout, "CardRect");
            foreach (var item in attachments)
            {
                var rect = Read<Rect>(item, "Rect");
                Check(rect.Right <= card.Right && rect.Bottom <= Read<double>(layout, "Height"), "Photo cards must fit narrow chat widths and measured message height.");
            }
            var hits = Read<System.Collections.IEnumerable>(layout, "Hits").Cast<object>().ToArray();
            var photoHit = hits.First(hit => Read<ChatTranscriptHitKind>(hit, "Kind") == ChatTranscriptHitKind.OpenImagePreview);
            Check(Read<object>(photoHit, "Parameter").Equals(photoPath), "Photo click targets must open the cached image.");
            Check(hits.Where(hit => Read<ChatTranscriptHitKind>(hit, "Kind") == ChatTranscriptHitKind.OpenAttachment).Select(hit => Read<object>(hit, "Parameter")).Distinct().Count() == 4,
                "Every photo and plain source must retain a separate page link.");
            Check(hits.Any(hit => Read<ChatTranscriptHitKind>(hit, "Kind") == ChatTranscriptHitKind.OpenAttachment && Read<object>(hit, "Parameter").Equals(sources[0].Path)),
                "The source URL must remain separate from the local photo cache path.");
            var window = new Window { Content = new Border { Background = new SolidColorBrush(Color.Parse("#151922")), Child = new ScrollViewer { Content = transcript } } };
            WorkbenchThemeResources.Apply(window, "studio", chatAppearanceKey: "adaptive");
            Snapshot(window, width, 820, Path.Combine(output, $"web-photos-{width}.png"));
            if (width == 780)
            {
                Call(transcript, "ExecuteHit", photoHit);
                var viewer = window.OwnedWindows.Single();
                Check(viewer.IsVisible && viewer.GetVisualDescendants().OfType<Image>().Any(image => image.Source is Bitmap), "Clicking a source photo must open the actual full image viewer.");
                Snapshot(viewer, 900, 600, Path.Combine(output, "web-photo-expanded.png"));
                viewer.Close();
            }
            window.Close();
        }
    }

    private static void MarkdownFormatting(string root, string output)
    {
        const string answer = """
            ## Places and reviews

            A **bold** name, *italic* note, ~~old detail~~ and `literal **code**` all keep their intended style. Encoded&#x20;space &amp; ampersand.

            3. **Maurizio's Italian Food**
               - **Cuisine:** Italian · pizza and pasta
               - **Reviews:** Fixture review summary for layout testing. [1]
               - **Location:** Example address; verify with the [official source](https://example.com/maurizio).
            4. **Example Garden Café**
               - **Reviews:** A second fixture entry. Rating unavailable. [2]

            > Source ratings and current opening hours need verification.

            | Place | Cuisine | Review information |
            | --- | --- | --- |
            | **Maurizio's** | Italian | Check the source [1] |
            | Garden Café | Café | Rating unavailable [2] |

            - [x] Read the source
            - [ ] Verify opening hours
            """;
        var blocks = ChatMarkdown.Parse(answer);
        var all = Flatten(blocks).ToArray();
        Check(all.Count(block => block.Kind == "card") == 2 && all.First(block => block.Kind == "card").Marker == "3.", "Numbered place entries must become information cards without losing their original rank.");
        Check(all.SelectMany(block => block.Runs).Any(run => run.Bold && run.Text.Contains("Maurizio's Italian Food")), "The reported restaurant name must be bold, not visible Markdown delimiters.");
        Check(all.SelectMany(block => block.Runs).Any(run => run.Code && run.Text == "literal **code**"), "Inline code must retain literal Markdown syntax.");
        Check(all.SelectMany(block => block.Runs).Any(run => run.Italic) && all.SelectMany(block => block.Runs).Any(run => run.Strike), "Italic and strikethrough formatting must remain distinct.");
        Check(string.Concat(all.Select(block => block.PlainText)).Contains("Encoded space & ampersand."), "HTML entities must decode into visible characters, including the reported hex space.");
        Check(ChatMarkdown.Parse(@"\*\*literal\*\*").Single().PlainText == "**literal**", "Intentionally escaped Markdown stays literal.");
        Check(ChatMarkdown.Parse("[unsafe](javascript:alert(1))").SelectMany(block => block.Runs).All(run => run.Url is null), "Formatting must never create executable script links.");
        Check(ChatMarkdown.Parse("**streaming").Single().PlainText.Contains("streaming"), "Incomplete streamed Markdown must retain its text.");
        var photo = Path.Combine(root, "place-fixture.png");
        File.WriteAllBytes(photo, GooglePhotoPreviewTests.Photo());
        var message = new LocalLlmChatMessageViewModel("assistant", answer, attachments:
        [
            new("[1] Maurizio's Italian Food · official source", "https://example.com/maurizio", "web", photo),
            new("[2] General review roundup", "https://example.org/reviews", "web", photo)
        ]);
        foreach (var (width, size) in new[] { (960, 15d), (420, 22d) })
        {
            var transcript = new ChatTranscriptRenderControl { ChatFontSize = size, Items = [message], OpenAttachmentCommand = new RelayCommand<string>(_ => { }) };
            var layout = Call(transcript, "BuildMessageLayout", message, (double)width - 40)!;
            var embedded = Read<HashSet<string>>(layout, "EmbeddedWebPhotos");
            Check(embedded.SetEquals(["https://example.com/maurizio"]), "Only a cited source whose title identifies the place may supply its card photo; a generic roundup must not be misattributed.");
            var textBlocks = Read<System.Collections.IEnumerable>(layout, "TextBlocks").Cast<object>().Select(value => Read<object>(value, "TextBlock")).ToArray();
            Check(textBlocks.All(value => Read<object>(value, "Rich") is not null), "Assistant prose must use the active rich-text renderer.");
            var rich = textBlocks.Select(value => Read<object>(value, "Rich")).ToArray();
            var visible = string.Join("\n", rich.Select(value => Read<string>(value, "PlainText")));
            Check(visible.Contains("Maurizio's Italian Food") && !visible.Contains("**Maurizio") && !visible.Contains("&#x20;"), "Rendered text must not leak Markdown or entity syntax.");
            var decorations = Read<System.Collections.IEnumerable>(layout, "MarkdownDecorations").Cast<object>().ToArray();
            Check(decorations.Count(value => Read<string>(value, "Kind") == "card") == 2, "The active transcript must render two information cards.");
            Check(decorations.Any(value => Read<string>(value, "Kind") == "tableHeader") == (width == 960),
                "Comparison tables must switch to labelled rows when chat font size makes columns too narrow.");
            Check(decorations.All(value => Read<Rect>(value, "Rect").Right <= width), "Cards and tables must fit the viewport at large fonts.");
            var window = new Window { Content = new Border { Background = new SolidColorBrush(Color.Parse("#151922")), Child = new ScrollViewer { Content = transcript } } };
            WorkbenchThemeResources.Apply(window, "studio", chatAppearanceKey: "adaptive");
            Snapshot(window, width, width == 960 ? 1600 : 2200, Path.Combine(output, $"structured-reviews-{width}.png"));
            // Hit-testing and copying use the shaped text, so bold and links do not shift the selection.
            var actualLayout = Call(transcript, "GetOrBuildLayout", 0)!;
            var selectable = Read<System.Collections.IEnumerable>(actualLayout, "TextBlocks").Cast<object>().ElementAt(1);
            var firstBlock = Read<object>(selectable, "TextBlock");
            var rect = Read<Rect>(firstBlock, "Rect");
            var hitMethod = transcript.GetType().GetMethod("TryGetTextPosition", Private)!;
            object?[] args = [new Point(rect.X + 8, rect.Y + 5), null];
            Check((bool)hitMethod.Invoke(transcript, args)!, "Formatted body text must remain selectable.");
            var shaped = Read<object>(firstBlock, "Rich");
            var copyText = Read<string>(shaped, "PlainText");
            var boldStart = copyText.IndexOf("bold", StringComparison.Ordinal);
            var positionType = transcript.GetType().GetNestedType("TextPosition", BindingFlags.NonPublic)!;
            var blockIndex = Read<int>(selectable, "BlockIndex");
            typeof(ChatTranscriptRenderControl).GetField("_selectionAnchor", Private)!.SetValue(transcript, Activator.CreateInstance(positionType, [0, blockIndex, 0, boldStart]));
            typeof(ChatTranscriptRenderControl).GetField("_selectionActive", Private)!.SetValue(transcript, Activator.CreateInstance(positionType, [0, blockIndex, 0, boldStart + 4]));
            Check((string)Call(transcript, "BuildSelectedText")! == "bold", "Copying selected bold text must use the rendered text, without delimiters.");
            var actualHits = Read<System.Collections.IEnumerable>(actualLayout, "Hits").Cast<object>().ToArray();
            Check(actualHits.Count(hit => Read<ChatTranscriptHitKind>(hit, "Kind") == ChatTranscriptHitKind.OpenAttachment && Read<object>(hit, "Parameter").Equals("https://example.com/maurizio")) >= 3,
                "Inline citations as well as explicit Markdown links must open the cited source.");
            window.Close();
        }
        static IEnumerable<ChatMarkdownBlock> Flatten(IEnumerable<ChatMarkdownBlock> source)
        {
            foreach (var block in source)
            {
                yield return block;
                foreach (var child in Flatten(block.Children ?? [])) yield return child;
            }
        }
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
