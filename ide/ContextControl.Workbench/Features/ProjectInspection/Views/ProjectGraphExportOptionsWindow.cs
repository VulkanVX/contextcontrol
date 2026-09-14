using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ContextControl.Workbench.Services;

namespace ContextControl.Workbench.Views;

public sealed record ProjectGraphExportOptions(string Format, string Resolution, bool IncludeProjectDetails);

public sealed class ProjectGraphExportOptionsWindow : Window
{
    private readonly ComboBox _formatBox;
    private readonly ComboBox _resolutionBox;
    private readonly CheckBox _projectDetailsBox;

    public ProjectGraphExportOptionsWindow()
    {
        Title = "Export architecture graph";
        Width = 340;
        Height = 244;
        MinWidth = 320;
        MinHeight = 234;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _formatBox = new ComboBox
        {
            MinHeight = 26,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = new[] { "png", "jpg", "webp", "svg", "dot", "graphml", "mmd", "json" }
        };
        _formatBox.SelectedIndex = 0;
        _formatBox.Classes.Add("theme-picker");
        _formatBox.SelectionChanged += (_, _) => RefreshResolutionState();

        _resolutionBox = new ComboBox
        {
            MinHeight = 26,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = new[] { "1k", "2k", "4k", "8k" }
        };
        _resolutionBox.SelectedIndex = 1;
        _resolutionBox.Classes.Add("theme-picker");

        _projectDetailsBox = new CheckBox
        {
            Content = "Project Details",
            MinHeight = 24,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalContentAlignment = VerticalAlignment.Center,
            FontSize = 10,
            FontWeight = FontWeight.Bold
        };

        var fields = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("96,*"),
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto"),
            ColumnSpacing = 10,
            RowSpacing = 8,
            Children =
            {
                FieldLabel("File format"),
                _formatBox,
                FieldLabel("Photo size"),
                _resolutionBox,
                FieldLabel("Include"),
                _projectDetailsBox
            }
        };
        Grid.SetColumn(_formatBox, 1);
        Grid.SetRow(fields.Children[2], 1);
        Grid.SetRow(_resolutionBox, 1);
        Grid.SetColumn(_resolutionBox, 1);
        Grid.SetRow(fields.Children[4], 2);
        Grid.SetRow(_projectDetailsBox, 2);
        Grid.SetColumn(_projectDetailsBox, 1);

        var cancelButton = CommandButton("Cancel");
        cancelButton.Click += (_, _) => Close(null);

        var exportButton = CommandButton("Export");
        exportButton.Classes.Add("primary");
        exportButton.Click += (_, _) =>
        {
            Close(new ProjectGraphExportOptions(
                SelectedComboText(_formatBox, "png"),
                SelectedComboText(_resolutionBox, "2k"),
                _projectDetailsBox.IsChecked == true));
        };

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children =
            {
                cancelButton,
                exportButton
            }
        };

        var content = new Border
        {
            Margin = new Thickness(8),
            Padding = new Thickness(10),
            Child = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    new TextBlock
                    {
                        Text = "Export architecture graph",
                        FontSize = 12,
                        FontWeight = FontWeight.ExtraBold
                    },
                    fields,
                    actions
                }
            }
        };
        content.Classes.Add("settings-panel");
        Content = content;
        RefreshResolutionState();
    }

    public void ApplyTheme(
        string? themeKey,
        string? uiFontFamily = null,
        string? codeFontFamily = null,
        string? skinKey = null,
        string? uiFontColorModeKey = null,
        string? customUiFontColor = null)
    {
        WorkbenchThemeResources.Apply(this, themeKey, uiFontFamily, codeFontFamily, skinKey: skinKey, uiFontColorModeKey: uiFontColorModeKey, customUiFontColor: customUiFontColor);
        if (Resources.TryGetValue("AppBackgroundBrush", out var brush) && brush is IBrush background)
        {
            Background = background;
        }
    }

    private void RefreshResolutionState()
    {
        var format = SelectedComboText(_formatBox, "png");
        _resolutionBox.IsEnabled = format is "png" or "jpg" or "webp";
    }

    private static TextBlock FieldLabel(string text)
    {
        var label = new TextBlock
        {
            Text = text,
            VerticalAlignment = VerticalAlignment.Center
        };
        label.Classes.Add("field-label");
        return label;
    }

    private static Button CommandButton(string text)
    {
        var button = new Button
        {
            Content = text,
            MinHeight = 24,
            MinWidth = 72,
            Padding = new Thickness(9, 0),
            FontSize = 10,
            FontWeight = FontWeight.Bold,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        button.Classes.Add("dialog-command");
        return button;
    }

    private static string SelectedComboText(ComboBox comboBox, string fallback)
    {
        return comboBox.SelectedItem?.ToString() ?? fallback;
    }
}
