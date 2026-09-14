using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace ContextControl.Workbench.Views.ThemeSettingsWindowParts;

public sealed partial class FileRulesSettingsPage : UserControl
{
    public FileRulesSettingsPage()
    {
        InitializeComponent();
    }

    private ThemeSettingsWindow? OwnerWindow => this.FindAncestorOfType<ThemeSettingsWindow>();

    private void OnEditRuleListClick(object? sender, RoutedEventArgs e) => OwnerWindow?.OnEditRuleListClick(sender, e);

    private void OnEditRuleEntryClick(object? sender, RoutedEventArgs e) => OwnerWindow?.OnEditRuleEntryClick(sender, e);
}
