using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ContextControl.Workbench.ViewModels;
using ContextControl.Workbench.Views;
using ContextControl.Workbench.Services;

namespace ContextControl.Workbench;

public sealed partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = WorkbenchViewModel.Create()
            };
            desktop.Exit += (_, _) => (desktop.MainWindow.DataContext as WorkbenchViewModel)?.Dispose();
            if (desktop.Args is ["--play-game", var gamePath])
            {
                var file = new FileInfo(Path.GetFullPath(gamePath));
                if (!file.Exists || file.Length > GameArtifact.MaxCharacters || !file.Extension.Equals(".html", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Choose an existing HTML game below 400 KB.");
                var html = File.ReadAllText(file.FullName);
                desktop.MainWindow.Opened += (_, _) =>
                {
                    var owner = desktop.MainWindow;
                    var lab = new GameLabWindow();
                    lab.Show(owner);
                    lab.LoadGame(new(file.Name, html), prompt =>
                    {
                        if (owner.DataContext is not WorkbenchViewModel vm) return;
                        vm.SwitchWorkspaceModeCommand.Execute(vm.WorkspaceModes.First(mode => mode.Key == "chat"));
                        vm.ContextControl.PrepareGameRepair(prompt);
                        owner.Activate();
                    });
                };
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
