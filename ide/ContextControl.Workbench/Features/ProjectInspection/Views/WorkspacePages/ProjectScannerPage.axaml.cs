using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using ContextControl.Workbench.ViewModels;

namespace ContextControl.Workbench.Views.MainWindowParts;

public sealed partial class ProjectScannerPage : UserControl
{
    private void OnOpenFileClick(object? sender, RoutedEventArgs e) => (DataContext as WorkbenchViewModel)?.OpenSelectedScannerFile();
    private void OnFileDoubleTapped(object? sender, TappedEventArgs e) => (DataContext as WorkbenchViewModel)?.OpenSelectedScannerFile();

    public ProjectScannerPage()
    {
        InitializeComponent();
    }
}
