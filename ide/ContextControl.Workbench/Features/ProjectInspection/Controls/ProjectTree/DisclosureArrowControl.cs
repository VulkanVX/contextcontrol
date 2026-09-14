using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace ContextControl.Workbench.Controls;

public sealed class DisclosureArrowControl : Control
{
    public static readonly StyledProperty<double> AngleProperty =
        AvaloniaProperty.Register<DisclosureArrowControl, double>(nameof(Angle));

    public static readonly StyledProperty<string> ThemeKeyProperty =
        AvaloniaProperty.Register<DisclosureArrowControl, string>(nameof(ThemeKey), "empty");

    private static readonly IBrush EmptyArrowBrush = new SolidColorBrush(Color.FromRgb(83, 97, 102));
    private static readonly IBrush DarkArrowBrush = new SolidColorBrush(Color.FromRgb(183, 199, 203));
    private static readonly IBrush MatrixArrowBrush = new SolidColorBrush(Color.FromRgb(150, 255, 195));
    private StreamGeometry? _arrowGeometry;
    private Size _arrowGeometrySize;
    private double _arrowGeometryAngle = double.NaN;

    public double Angle
    {
        get => GetValue(AngleProperty);
        set => SetValue(AngleProperty, value);
    }

    public string ThemeKey
    {
        get => GetValue(ThemeKeyProperty);
        set => SetValue(ThemeKeyProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == AngleProperty
            || change.Property == ThemeKeyProperty)
        {
            InvalidateVisual();
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        context.DrawGeometry(ArrowBrush, null, GetArrowGeometry());
    }

    private StreamGeometry GetArrowGeometry()
    {
        var boundsSize = Bounds.Size;
        if (_arrowGeometry is not null
            && _arrowGeometrySize == boundsSize
            && Math.Abs(_arrowGeometryAngle - Angle) < 0.001)
        {
            return _arrowGeometry;
        }

        var center = new Point(boundsSize.Width * 0.5, boundsSize.Height * 0.5);
        var size = Math.Min(boundsSize.Width, boundsSize.Height);
        var backX = size * 0.22;
        var halfHeight = size * 0.30;
        var tipX = size * 0.24;
        var left = Rotate(new Point(center.X - backX, center.Y - halfHeight), center, Angle);
        var tip = Rotate(new Point(center.X + tipX, center.Y), center, Angle);
        var right = Rotate(new Point(center.X - backX, center.Y + halfHeight), center, Angle);

        var geometry = new StreamGeometry();
        using (var stream = geometry.Open())
        {
            stream.BeginFigure(left, true);
            stream.LineTo(tip);
            stream.LineTo(right);
            stream.EndFigure(true);
        }

        _arrowGeometry = geometry;
        _arrowGeometrySize = boundsSize;
        _arrowGeometryAngle = Angle;
        return geometry;
    }

    private IBrush ArrowBrush => IsMatrixTheme(ThemeKey)
        ? MatrixArrowBrush
        : IsDarkArrowTheme(ThemeKey)
            ? DarkArrowBrush
            : EmptyArrowBrush;

    private static bool IsMatrixTheme(string? themeKey)
    {
        return string.Equals(themeKey, "matrix", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDarkArrowTheme(string? themeKey)
    {
        return string.Equals(themeKey, "dark", StringComparison.OrdinalIgnoreCase)
            || string.Equals(themeKey, "nocturne", StringComparison.OrdinalIgnoreCase)
            || string.Equals(themeKey, "onyx", StringComparison.OrdinalIgnoreCase)
            || string.Equals(themeKey, "smoke", StringComparison.OrdinalIgnoreCase)
            || string.Equals(themeKey, "carbon", StringComparison.OrdinalIgnoreCase)
            || string.Equals(themeKey, "obsidian", StringComparison.OrdinalIgnoreCase)
            || string.Equals(themeKey, "ash", StringComparison.OrdinalIgnoreCase)
            || string.Equals(themeKey, "graphene", StringComparison.OrdinalIgnoreCase)
            || string.Equals(themeKey, "ruby", StringComparison.OrdinalIgnoreCase)
            || string.Equals(themeKey, "amethyst", StringComparison.OrdinalIgnoreCase)
            || string.Equals(themeKey, "ember", StringComparison.OrdinalIgnoreCase)
            || string.Equals(themeKey, "cobalt", StringComparison.OrdinalIgnoreCase)
            || string.Equals(themeKey, "contrast", StringComparison.OrdinalIgnoreCase);
    }

    private static Point Rotate(Point point, Point center, double degrees)
    {
        var radians = degrees * Math.PI / 180.0;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);
        var x = point.X - center.X;
        var y = point.Y - center.Y;

        return new Point(
            center.X + x * cos - y * sin,
            center.Y + x * sin + y * cos);
    }
}
