// CC-DESC: Layout, formatting, resource, and small helper types for the local LLM catalog surface.

// CC-DESC: Draws the LLM catalogue as a fixed-row virtualized surface for fast scrolling.

using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using ContextControl.Workbench.ViewModels;

namespace ContextControl.Workbench.Controls;

public sealed partial class LocalLlmCatalogRenderControl
{
    private double TotalHeight => ContentTop + ContentBottom + (Items?.Count ?? 0) * RowPitch;

    private static double RowTop(int index)
    {
        return ContentTop + Math.Max(0, index) * RowPitch;
    }

    private static Rect CardRect(double rowTop, double width)
    {
        return new Rect(HorizontalInset, rowTop, Math.Max(0.0, width - HorizontalInset * 2.0 - 2.0), CardHeight);
    }

    private static Rect PullButtonRect(double rowTop, double width)
    {
        var card = CardRect(rowTop, width);
        var buttonWidth = Math.Clamp(card.Width * 0.12, 62.0, 82.0);
        var buttonY = card.Y + CardPaddingY + SummaryTagsY + SummaryTagHeight - ButtonHeight;
        return new Rect(card.Right - CardPaddingX - buttonWidth, buttonY, buttonWidth, ButtonHeight);
    }

    private static Rect IconHitRect(double rowTop, double width)
    {
        var card = CardRect(rowTop, width);
        return new Rect(card.X + CardPaddingX, card.Y + CardPaddingY, IconSize, IconSize);
    }

    private static Rect MetricsRect(double rowTop, double width)
    {
        var card = CardRect(rowTop, width);
        var contentLeft = card.X + CardPaddingX;
        var contentRight = card.Right - CardPaddingX;
        var rightWidth = Math.Clamp(card.Width * 0.16, RightColumnMinWidth, RightColumnMaxWidth);
        if (card.Width < 560.0)
        {
            rightWidth = Math.Min(RightColumnMinWidth, Math.Max(76.0, card.Width * 0.23));
        }

        var rightLeft = Math.Max(contentLeft, contentRight - rightWidth);
        var middleWidth = card.Width < 560.0 ? 96.0 : Math.Clamp(card.Width * 0.24, 118.0, 178.0);
        var middleLeft = Math.Max(contentLeft, rightLeft - middleWidth - 8.0);
        return new Rect(middleLeft, card.Y + 5.0, Math.Max(0.0, rightLeft - middleLeft - 8.0), card.Height - 10.0);
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

    private static string Clean(string? value, int maxLength)
    {
        var clean = (value ?? "")
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();
        if (clean.Length <= maxLength)
        {
            return clean;
        }

        return clean[..Math.Max(0, maxLength - 1)] + "...";
    }

    private static string ResolveInitials(string? value)
    {
        var words = (value ?? "")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(word => char.IsLetterOrDigit(word[0]))
            .Take(2)
            .Select(word => char.ToUpperInvariant(word[0]).ToString())
            .ToArray();
        return words.Length == 0 ? "LLM" : string.Concat(words);
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
