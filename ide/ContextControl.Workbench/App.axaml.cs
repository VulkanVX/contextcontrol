using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ContextControl.Workbench.ViewModels;
using ContextControl.Workbench.Views;

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
        }

        base.OnFrameworkInitializationCompleted();
    }
}
