using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ContextControl.Workbench.Services;

namespace ContextControl.Workbench.Views;

public sealed class SkillbookRenameWindow : Window
{
    private readonly TextBox _nameBox;

    public SkillbookRenameWindow(string title, string currentName)
    {
        Title = title;
        Width = 360;
        Height = 158;
        MinWidth = 320;
        MinHeight = 150;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _nameBox = new TextBox
        {
            Text = currentName,
            MinHeight = 28,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        _nameBox.Classes.Add("setting-search");
        _nameBox.KeyDown += OnNameBoxKeyDown;

        var applyButton = CommandButton("Apply");
        applyButton.Classes.Add("primary");
        applyButton.Click += (_, _) => Apply();

        var cancelButton = CommandButton("Cancel");
        cancelButton.Click += (_, _) => Close(null);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Center,
            Children =
            {
                applyButton,
                cancelButton
            }
        };

        var content = new Border
        {
            Margin = new Thickness(8),
            Padding = new Thickness(10),
            Child = new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    new TextBlock
                    {
                        Text = title,
                        FontSize = 12,
                        FontWeight = FontWeight.ExtraBold,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        TextAlignment = TextAlignment.Center
                    },
                    _nameBox,
                    actions
                }
            }
        };
        content.Classes.Add("settings-panel");
        Content = content;

        Opened += (_, _) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                _nameBox.Focus();
                _nameBox.SelectAll();
            });
        };
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

    private void OnNameBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Apply();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            Close(null);
            e.Handled = true;
        }
    }

    private void Apply()
    {
        var clean = (_nameBox.Text ?? "").Trim();
        if (string.IsNullOrWhiteSpace(clean))
        {
            return;
        }

        Close(clean);
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
}
