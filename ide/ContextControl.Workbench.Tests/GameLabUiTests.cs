using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ContextControl.Workbench;
using ContextControl.Workbench.Services;
using ContextControl.Workbench.Views;

internal static class GameLabUiTests
{
    public static void Run(string output)
    {
        Directory.CreateDirectory(output);
        AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).UseSkia().WithInterFont().SetupWithoutStarting();
        var window = new GameLabWindow();
        window.LoadGame(new("Snake · generated game", GameLabTests.Fixture), _ => { });
        foreach (var (width, height) in new[] { (1200, 820), (900, 620) })
        {
            window.Width = width; window.Height = height; window.Show();
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            var editor = window.FindControl<AvaloniaEdit.TextEditor>("SourceEditor")!;
            if (editor.Bounds.Width < 200 || editor.Bounds.Height < 100 || editor.Document.TextLength != GameLabTests.Fixture.Length) throw new InvalidOperationException("The editable source must remain usable at compact sizes.");
            if (window.FindControl<Button>("RunButton")!.Bounds.Width < 50) throw new InvalidOperationException("Run action has no usable hit target.");
            using var image = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96, 96));
            image.Render((Control)window.Content!); image.Save(Path.Combine(output, $"game-lab-{width}.png"));
        }
        window.Close();
        Console.WriteLine("GAME_LAB_UI_PASS: desktop and compact layouts, editable source and Run controls");
    }
}
