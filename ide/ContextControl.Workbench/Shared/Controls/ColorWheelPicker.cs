using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;

namespace ContextControl.Workbench.Controls;

public sealed class ColorWheelPicker : Control
{
    public static readonly StyledProperty<Color> SelectedColorProperty =
        AvaloniaProperty.Register<ColorWheelPicker, Color>(
            nameof(SelectedColor),
            Color.Parse("#DDE6E8"),
            defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<double> BrightnessProperty =
        AvaloniaProperty.Register<ColorWheelPicker, double>(
            nameof(Brightness),
            0.94,
            defaultBindingMode: BindingMode.TwoWay);

    private bool _isDragging;

    public Color SelectedColor
    {
        get => GetValue(SelectedColorProperty);
        set => SetValue(SelectedColorProperty, value);
    }

    public double Brightness
    {
        get => GetValue(BrightnessProperty);
        set => SetValue(BrightnessProperty, Math.Clamp(value, 0.08, 1.0));
    }

    public ColorWheelPicker()
    {
        Focusable = true;
        Cursor = new Cursor(StandardCursorType.Hand);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        return new Size(86, 86);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SelectedColorProperty
            || change.Property == BrightnessProperty)
        {
            InvalidateVisual();
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _isDragging = true;
        e.Pointer.Capture(this);
        UpdateColorFromPoint(e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_isDragging)
        {
            return;
        }

        UpdateColorFromPoint(e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_isDragging)
        {
            return;
        }

        _isDragging = false;
        e.Pointer.Capture(null);
        UpdateColorFromPoint(e.GetPosition(this));
        e.Handled = true;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var side = Math.Min(Bounds.Width, Bounds.Height);
        if (side <= 2)
        {
            return;
        }

        var center = new Point(Bounds.Width * 0.5, Bounds.Height * 0.5);
        var radius = Math.Max(1, side * 0.5 - 4);
        const int segments = 96;
        for (var index = 0; index < segments; index++)
        {
            var angleA = index * Math.Tau / segments;
            var angleB = (index + 1) * Math.Tau / segments;
            var color = FromHsv(index / (double)segments, 0.92, Brightness);
            var geometry = new StreamGeometry();
            using (var stream = geometry.Open())
            {
                stream.BeginFigure(center, isFilled: true);
                stream.LineTo(EdgePoint(center, radius, angleA));
                stream.LineTo(EdgePoint(center, radius, angleB));
                stream.EndFigure(isClosed: true);
            }

            context.DrawGeometry(new SolidColorBrush(color), null, geometry);
        }

        context.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromArgb(168, 255, 255, 255)), 1), center, radius, radius);
        context.DrawEllipse(new SolidColorBrush(Color.FromArgb(52, 0, 0, 0)), null, center, radius * 0.36, radius * 0.36);
        context.DrawEllipse(new SolidColorBrush(SelectedColor), new Pen(Brushes.White, 1), center, radius * 0.28, radius * 0.28);

        var (hue, saturation, _) = ToHsv(SelectedColor);
        var thumb = EdgePoint(center, radius * Math.Clamp(saturation, 0.08, 1), hue * Math.Tau);
        context.DrawEllipse(Brushes.White, new Pen(Brushes.Black, 1), thumb, 4.5, 4.5);
        context.DrawEllipse(new SolidColorBrush(SelectedColor), null, thumb, 2.7, 2.7);
    }

    private void UpdateColorFromPoint(Point point)
    {
        var side = Math.Min(Bounds.Width, Bounds.Height);
        var radius = Math.Max(1, side * 0.5 - 4);
        var center = new Point(Bounds.Width * 0.5, Bounds.Height * 0.5);
        var dx = point.X - center.X;
        var dy = point.Y - center.Y;
        var distance = Math.Sqrt(dx * dx + dy * dy);
        var hue = Math.Atan2(dy, dx) / Math.Tau;
        if (hue < 0)
        {
            hue += 1;
        }

        var saturation = Math.Clamp(distance / radius, 0, 1);
        SelectedColor = FromHsv(hue, saturation, Brightness);
    }

    private static Point EdgePoint(Point center, double radius, double angle)
    {
        return new Point(center.X + Math.Cos(angle) * radius, center.Y + Math.Sin(angle) * radius);
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
