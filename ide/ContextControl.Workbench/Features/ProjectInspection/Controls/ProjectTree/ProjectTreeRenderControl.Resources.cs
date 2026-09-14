// CC-DESC: Layout, formatting, resource, geometry, and small helper types for the project tree surface.

using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using ContextControl.Workbench.ViewModels;

namespace ContextControl.Workbench.Controls;

public sealed partial class ProjectTreeRenderControl
{
    private Rect ToggleRect(TreeRowViewModel row, double rowTop, double rowHeight)
    {
        var x = ContentLeft + Math.Max(0.0, RailInset + Math.Max(0, row.Depth) * RailStep - ToggleSize * 0.5);
        return new Rect(x, rowTop + Math.Max(0.0, (rowHeight - ToggleSize) * 0.5), ToggleSize, ToggleSize);
    }

    private static Rect IncludeRect(double rowTop, double rowHeight, double width)
    {
        var x = Math.Max(ContentLeft, width - RightInset - IncludeWidth);
        return new Rect(x, rowTop + Math.Max(0.0, (rowHeight - IncludeHeight) * 0.5), IncludeWidth, IncludeHeight);
    }

    private FormattedText GetFormattedText(
        string text,
        IBrush brush,
        FontFamily fontFamily,
        FontWeight weight,
        FontStyle style,
        double fontSize)
    {
        var key = new TextCacheKey(
            text,
            RuntimeHelpers.GetHashCode(brush),
            fontFamily.ToString(),
            weight,
            style,
            fontSize);
        if (_textCache.TryGetValue(key, out var formatted))
        {
            return formatted;
        }

        if (_textCache.Count > MaxTextCacheEntries)
        {
            _textCache.Clear();
        }

        formatted = new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface(fontFamily, style, weight),
            fontSize,
            brush);
        _textCache[key] = formatted;
        return formatted;
    }

    private StreamGeometry GetArrowGeometry(double angle)
    {
        var normalizedAngle = Math.Abs(angle - 90.0) < 0.001 ? 90.0 : 0.0;
        if (_arrowGeometryCache.TryGetValue(normalizedAngle, out var geometry))
        {
            return geometry;
        }

        var center = new Point(ArrowSize * 0.5, ArrowSize * 0.5);
        var backX = ArrowSize * 0.22;
        var halfHeight = ArrowSize * 0.30;
        var tipX = ArrowSize * 0.24;
        var left = Rotate(new Point(center.X - backX, center.Y - halfHeight), center, normalizedAngle);
        var tip = Rotate(new Point(center.X + tipX, center.Y), center, normalizedAngle);
        var right = Rotate(new Point(center.X - backX, center.Y + halfHeight), center, normalizedAngle);

        geometry = new StreamGeometry();
        using (var stream = geometry.Open())
        {
            stream.BeginFigure(left, true);
            stream.LineTo(tip);
            stream.LineTo(right);
            stream.EndFigure(true);
        }

        _arrowGeometryCache[normalizedAngle] = geometry;
        return geometry;
    }

    private T Resource<T>(string key, T fallback)
    {
        for (var control = this as Control; control is not null; control = control.GetVisualParent() as Control)
        {
            if (control.Resources.TryGetValue(key, out var value) && value is T typed)
            {
                return typed;
            }
        }

        if (Application.Current?.Resources.TryGetValue(key, out var appValue) == true && appValue is T appTyped)
        {
            return appTyped;
        }

        return fallback;
    }

    private static FontFamily ResolveFontFamily(string? value, FontFamily fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        try
        {
            return new FontFamily(value);
        }
        catch
        {
            return fallback;
        }
    }

    private IBrush ArrowBrush => IsMatrixTheme(ThemeKey)
        ? MatrixArrowBrush
        : IsDarkTheme(ThemeKey)
            ? DarkArrowBrush
            : EmptyArrowBrush;

    private Pen RailPen => IsMatrixTheme(ThemeKey)
        ? MatrixRailPen
        : IsDarkTheme(ThemeKey)
            ? DarkRailPen
            : EmptyRailPen;

    private Pen BranchPen => IsMatrixTheme(ThemeKey)
        ? MatrixBranchPen
        : IsDarkTheme(ThemeKey)
            ? DarkBranchPen
            : EmptyBranchPen;

    private static double RailX(int level)
    {
        return ContentLeft + Math.Floor(RailInset + Math.Max(0, level) * RailStep) + 0.5;
    }

    private static double CenteredTextY(double rowTop, double rowHeight, double textHeight)
    {
        return rowTop + Math.Max(0.0, (rowHeight - textHeight) * 0.5);
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

    private static bool IsMatrixTheme(string? themeKey)
    {
        return string.Equals(themeKey, "matrix", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDarkTheme(string? themeKey)
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

    private readonly record struct TextCacheKey(
        string Text,
        int BrushId,
        string FontFamily,
        FontWeight Weight,
        FontStyle Style,
        double FontSize);

    private sealed class ValueObserver<T>(Action<T> onNext) : IObserver<T>
    {
        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }

        public void OnNext(T value)
        {
            onNext(value);
        }
    }
}
