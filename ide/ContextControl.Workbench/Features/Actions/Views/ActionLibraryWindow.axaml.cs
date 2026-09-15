using Avalonia.Controls;
using Avalonia.Interactivity;
using ContextControl.Workbench.Services;

namespace ContextControl.Workbench.Views;

public partial class ActionLibraryWindow : Window
{
    public Action<ChatAction>? Selected { get; set; }
    public ActionLibraryWindow() { InitializeComponent(); ActionItems.ItemsSource = ChatActionCatalog.All; }
    private void OnUse(object? sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.DataContext is not ChatAction action) return;
        Selected?.Invoke(action); Close();
    }
}
