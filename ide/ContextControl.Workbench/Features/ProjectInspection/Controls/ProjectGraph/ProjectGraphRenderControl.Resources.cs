using System.Collections.Specialized;
using System.Globalization;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using ContextControl.Workbench.ViewModels;

namespace ContextControl.Workbench.Controls;
public sealed partial class ProjectGraphRenderControl
{
    private void DrawClippedText(DrawingContext context, FormattedText formatted, Rect clip, Point point)
    {
        if (clip.Width <= 0 || clip.Height <= 0)
        {
            return;
        }

        using (context.PushClip(clip))
        {
            context.DrawText(formatted, point);
        }
    }

    private FormattedText GetFormattedText(
        string text,
        IBrush brush,
        FontFamily fontFamily,
        string fontKey,
        FontWeight weight,
        FontStyle style,
        double fontSize)
    {
        fontSize = Math.Round(fontSize * 16.0) / 16.0;
        var key = new TextCacheKey(
            text,
            RuntimeHelpers.GetHashCode(brush),
            fontKey,
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

    private RenderResources ResolveRenderResources()
    {
        var uiFont = ResolveFontFamily(UiFontFamily, Resource("UiFontFamily", DefaultUiFontFamily));
        var codeFont = ResolveFontFamily(CodeFontFamily, Resource("CodeFontFamily", DefaultCodeFontFamily));
        return new RenderResources(
            Resource("EditorSurfaceBrush", EditorSurfaceFallbackBrush),
            Resource("PanelBorderBrush", PanelBorderFallbackBrush),
            Resource("CommandBackgroundBrush", CommandBackgroundFallbackBrush),
            Resource("HistoryActiveBrush", HistoryActiveFallbackBrush),
            Resource("DropdownSelectedBrush", DropdownSelectedFallbackBrush),
            Resource("TextPrimaryBrush", TextPrimaryFallbackBrush),
            Resource("TextMutedBrush", TextMutedFallbackBrush),
            Resource("FolderTextBrush", FolderTextFallbackBrush),
            Resource("FileTextBrush", FileTextFallbackBrush),
            Resource("ExternalTextBrush", ExternalTextFallbackBrush),
            Resource("AccentBrush", AccentFallbackBrush),
            Resource("AccentBorderBrush", AccentBorderFallbackBrush),
            Resource("MetricFileBrush", MetricFileFallbackBrush),
            Resource("MetricLocBrush", MetricLocFallbackBrush),
            uiFont,
            uiFont.ToString(),
            codeFont,
            codeFont.ToString(),
            IsDarkTheme(ThemeKey));
    }

    private T Resource<T>(string key, T fallback)
    {
        for (var control = this as Control; control is not null; control = control.Parent as Control)
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
            || string.Equals(themeKey, "contrast", StringComparison.OrdinalIgnoreCase)
            || string.Equals(themeKey, "matrix", StringComparison.OrdinalIgnoreCase);
    }
}
