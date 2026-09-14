using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ContextControl.Workbench.Controls;
using ContextControl.Workbench.Services;

namespace ContextControl.Workbench.Views;

public sealed class GraphGenerationColorWindow : Window
{
    private readonly ColorWheelPicker _picker;
    private readonly Slider _darknessSlider;
    private bool _syncing;

    public GraphGenerationColorWindow(int generation, Color initialColor)
    {
        Title = $"Generation {generation} color";
        Width = 300;
        Height = 260;
        MinWidth = 280;
        MinHeight = 246;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var (_, _, initialValue) = ToHsv(initialColor);
        _picker = new ColorWheelPicker
        {
            Width = 104,
            Height = 104,
            Brightness = Math.Clamp(initialValue, 0.08, 1.0),
            SelectedColor = initialColor,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        _darknessSlider = new Slider
        {
            Minimum = 0,
            Maximum = 0.92,
            Value = Math.Clamp(1.0 - initialValue, 0, 0.92),
            Width = 150,
            VerticalAlignment = VerticalAlignment.Center
        };
        _darknessSlider.PropertyChanged += (_, e) =>
        {
            if (e.Property == Slider.ValueProperty)
            {
                SyncPickerFromDarkness();
            }
        };

        var cancelButton = CommandButton("Cancel");
        cancelButton.Click += (_, _) => Close(null);

        var saveButton = CommandButton("Apply");
        saveButton.Classes.Add("primary");
        saveButton.Click += (_, _) => Close(_picker.SelectedColor);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Center,
            Children =
            {
                cancelButton,
                saveButton
            }
        };

        var content = new Border
        {
            Margin = new Thickness(8),
            Padding = new Thickness(10),
            Child = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock
                    {
                        Text = $"Generation {generation}",
                        FontSize = 12,
                        FontWeight = FontWeight.ExtraBold,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        TextAlignment = TextAlignment.Center
                    },
                    _picker,
                    new Grid
                    {
                        ColumnDefinitions = new ColumnDefinitions("Auto,Auto"),
                        ColumnSpacing = 8,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Children =
                        {
                            new TextBlock
                            {
                                Text = "Darkness",
                                VerticalAlignment = VerticalAlignment.Center
                            },
                            _darknessSlider
                        }
                    },
                    actions
                }
            }
        };
        Grid.SetColumn(_darknessSlider, 1);
        content.Classes.Add("settings-panel");
        Content = content;
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

    private void SyncPickerFromDarkness()
    {
        if (_syncing)
        {
            return;
        }

        _syncing = true;
        var (hue, saturation, _) = ToHsv(_picker.SelectedColor);
        var brightness = Math.Clamp(1.0 - _darknessSlider.Value, 0.08, 1.0);
        _picker.Brightness = brightness;
        _picker.SelectedColor = FromHsv(hue, saturation, brightness);
        _syncing = false;
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

    private static Color FromHsv(double hue, double saturation, double value)
    {
        hue = (hue % 1 + 1) % 1;
        saturation = Math.Clamp(saturation, 0, 1);
        value = Math.Clamp(value, 0, 1);
        var scaled = hue * 6;
        var sector = (int)Math.Floor(scaled);
        var fraction = scaled - sector;
        var p = value * (1 - saturation);
        var q = value * (1 - saturation * fraction);
        var t = value * (1 - saturation * (1 - fraction));
        var (r, g, b) = sector switch
        {
            0 => (value, t, p),
            1 => (q, value, p),
            2 => (p, value, t),
            3 => (p, q, value),
            4 => (t, p, value),
            _ => (value, p, q)
        };

        return Color.FromRgb(ToByte(r), ToByte(g), ToByte(b));
    }

    private static (double Hue, double Saturation, double Value) ToHsv(Color color)
    {
        var r = color.R / 255d;
        var g = color.G / 255d;
        var b = color.B / 255d;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var delta = max - min;
        var hue = 0d;
        if (delta > 0)
        {
            if (Math.Abs(max - r) < double.Epsilon)
            {
                hue = ((g - b) / delta) % 6;
            }
            else if (Math.Abs(max - g) < double.Epsilon)
            {
                hue = ((b - r) / delta) + 2;
            }
            else
            {
                hue = ((r - g) / delta) + 4;
            }

            hue /= 6;
            if (hue < 0)
            {
                hue += 1;
            }
        }

        var saturation = max <= 0 ? 0 : delta / max;
        return (hue, saturation, max);
    }

    private static byte ToByte(double value)
    {
        return (byte)Math.Round(Math.Clamp(value, 0, 1) * 255);
    }

}
