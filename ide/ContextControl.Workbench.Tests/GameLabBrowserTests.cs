using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Themes.Fluent;
using ContextControl.Workbench.Controls;
using ContextControl.Workbench.Services;
using ContextControl.Workbench.Views;
using Microsoft.Web.WebView2.Core;

internal static class GameLabBrowserTests
{
    internal static string Output = "";
    internal static string? GamePath;
    internal static Exception? Failure;
    internal static void Run(string output, string? gamePath)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("The Game Lab browser check requires Windows.");
        Output = Path.GetFullPath(output); GamePath = gamePath; Directory.CreateDirectory(Output);
        var thread = new Thread(() =>
        {
            try { AppBuilder.Configure<GameLabTestApp>().UsePlatformDetect().StartWithClassicDesktopLifetime([]); }
            catch (Exception ex) { Failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (Failure is not null) throw new InvalidOperationException("Game Lab browser verification failed", Failure);
    }
}
internal sealed class GameLabTestApp : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        Styles.Add(new Avalonia.Markup.Xaml.Styling.StyleInclude(new Uri("avares://ContextControl.Workbench/")) { Source = new Uri("avares://AvaloniaEdit/Themes/Fluent/AvaloniaEdit.xaml") });
    }
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime lifetime) return;
        var window = new GameLabWindow { ShowActivated = false };
        lifetime.MainWindow = window;
        var started = false;
        window.Opened += async (_, _) =>
        {
            if (started) return;
            started = true;
            var checks = 0;
            void Check(bool condition, string message) { checks++; if (!condition) throw new InvalidOperationException(message); }
            async Task Until(Func<bool> ready)
            {
                using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(25));
                while (!ready()) await Task.Delay(75, limit.Token);
            }
            try
            {
                var browser = window.FindControl<WebView2Host>("GameBrowser")!;
                var state = window.FindControl<TextBlock>("StateLabel")!;
                await Until(() => browser.IsReady);
                var core = (CoreWebView2)typeof(WebView2Host).GetField("_webView", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(browser)!;
                CoreWebView2Frame? frame = null;
                core.FrameCreated += (_, args) => frame = args.Frame;
                string? repair = null;
                window.LoadGame(new("Snake lab", GameLabTests.Fixture), text => repair = text);
                await Until(() => state.Text is "PLAYING" or "CHECK ERRORS");
                Check(state.Text == "PLAYING", "The fixture game must load without errors: " + window.FindControl<TextBox>("ConsoleText")!.Text);
                Check(frame is not null, "A game must run in its own frame.");
                var sandbox = await browser.ExecuteScriptAsync("document.getElementById('game').getAttribute('sandbox')");
                Check(sandbox == "\"allow-scripts\"", "The running frame has only script permission.");
                var before = await frame!.ExecuteScriptAsync("x");
                await frame.ExecuteScriptAsync("dispatchEvent(new KeyboardEvent('keydown',{key:'ArrowRight'}))");
                Check(await frame.ExecuteScriptAsync("x") != before, "Keyboard controls reach the game.");
                Check(await frame.ExecuteScriptAsync("c.getImageData(x*20,y*20,1,1).data[1]") == "225", "The snake is actually drawn on canvas.");
                Check(await frame.ExecuteScriptAsync("(()=>{try{return parent.document.body.innerHTML}catch(e){return 'isolated'}})()") == "\"isolated\"", "The game cannot read the host document.");
                Check(await frame.ExecuteScriptAsync("(()=>{try{return !!localStorage}catch(e){return false}})()") == "false", "The game has no profile storage access.");
                using (var image = File.Create(Path.Combine(GameLabBrowserTests.Output, "stage.png")))
                    await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, image);
                window.FindControl<Button>("StopButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Until(() => !browser.IsNavigating);
                Check(state.Text == "STOPPED" && await browser.ExecuteScriptAsync("document.querySelector('iframe')===null") == "true", "Stop destroys the game frame and its timers.");
                frame = null;
                window.FindControl<Button>("RestartButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Until(() => state.Text is "PLAYING" or "CHECK ERRORS");
                Check(state.Text == "PLAYING" && await frame!.ExecuteScriptAsync("x") == "4", "Restart resets the last run.");
                window.LoadGame(new("Broken fixture", GameLabTests.Fixture.Replace("reset();</script>", "reset();throw new Error('fixture repair check');</script>")), text => repair = text);
                await Until(() => state.Text == "CHECK ERRORS");
                Check(window.FindControl<TextBox>("ConsoleText")!.Text!.Contains("fixture repair check"), "Report runtime errors in the console: " + window.FindControl<TextBox>("ConsoleText")!.Text);
                await frame!.ExecuteScriptAsync("window.blocked=false;fetch('https://example.invalid/blocked').catch(()=>window.blocked=true)");
                await Task.Delay(500);
                Check(await frame.ExecuteScriptAsync("window.blocked") == "true", "Network requests are blocked in the real browser.");
                window.FindControl<Button>("FixButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(repair?.Contains("fixture repair check") == true && repair.Contains("throw new Error('fixture repair check')"), "Repair draft includes the errors and exact source.");
                Check(!window.IsVisible, "Repair returns the user to chat and stops the preview.");
                window.Show();
                var validation = await window.ValidateAsync(new("Checked fixture", GameLabTests.Fixture), default);
                Check(validation.Passed, "The automatic startup/input check accepts a working game: " + string.Join(";", validation.Errors));
                var broken = GameLabTests.Fixture.Replace("reset();</script>", "reset();throw new Error('automatic initialization error');</script>");
                validation = await window.ValidateAsync(new("Broken game", broken), default);
                Check(!validation.Passed && validation.Completed && validation.Errors.Any(e => e.Contains("automatic initialization error")), "Automatic checking catches final-code initialization errors.");
                var attempts = 0;
                var reviewed = await GameCreationReview.RunAsync(new(true, "done", "```html\n" + broken + "\n```"), "Snake", Path.Combine(GameLabBrowserTests.Output, "automatic-review"),
                    (game, token) => window.ValidateAsync(game, token), (prompt, token) => { attempts++; return Task.FromResult(new LocalLlmChatResult(true, "fixture repair", "```html\n" + GameLabTests.Fixture + "\n```")); }, _ => { }, default);
                Check(reviewed.Validation.Passed && attempts == 1, "The repair coordinator reruns corrected code through the real browser.");
                var badInput = GameLabTests.Fixture.Replace("reset();</script>", "reset();addEventListener('keydown',()=>{throw new Error('input handler broken')});</script>");
                validation = await window.ValidateAsync(new("Broken input", badInput), default);
                Check(validation.Errors.Any(e => e.Contains("input handler broken")), "Synthetic input catches handler errors after successful startup.");
                if (GameLabBrowserTests.GamePath is { } path)
                {
                    frame = null;
                    var html = await File.ReadAllTextAsync(path);
                    var original = Path.Combine(Path.GetDirectoryName(path)!, "qwen-original.html");
                    if (File.Exists(original))
                    {
                        validation = await window.ValidateAsync(new("Original Qwen Snake", await File.ReadAllTextAsync(original)), default);
                        Check(validation.Completed && !validation.Passed, "The automatic checker catches the original Qwen Snake bug.");
                    }
                    validation = await window.ValidateAsync(new("Repaired Qwen Snake", html), default);
                    Check(validation.Passed, "The repaired Qwen Snake survives automatic checks: " + string.Join(";", validation.Errors));
                    window.LoadGame(new("Generated Snake", html), _ => { });
                    await Until(() => state.Text is "PLAYING" or "CHECK ERRORS");
                    await Task.Delay(1500);
                    Check(state.Text == "PLAYING", "The generated game must load without runtime errors: " + window.FindControl<TextBox>("ConsoleText")!.Text);
                    Check(await frame!.ExecuteScriptAsync("document.querySelectorAll('canvas').length") != "0", "Generated Snake contains a canvas.");
                    using var image = File.Create(Path.Combine(GameLabBrowserTests.Output, "generated-snake.png"));
                    await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, image);
                }
                Console.WriteLine($"GAME_LAB_BROWSER_PASS {checks} checks");
            }
            catch (Exception ex) { GameLabBrowserTests.Failure = ex; }
            finally { window.Close(); lifetime.Shutdown(); }
        };
        base.OnFrameworkInitializationCompleted();
    }
}
