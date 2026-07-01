// CC-DESC: Draws Skillbook entries as one lazy variable-height transcript-like surface.

using System.Collections.Specialized;
using System.Globalization;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ContextControl.Workbench.ViewModels;

namespace ContextControl.Workbench.Controls;

public sealed class SkillbookRenderControl : Control
{
    public static readonly StyledProperty<IReadOnlyList<SkillbookEntryViewModel>?> ItemsProperty =
        AvaloniaProperty.Register<SkillbookRenderControl, IReadOnlyList<SkillbookEntryViewModel>?>(nameof(Items));

    public static readonly StyledProperty<string> ThemeKeyProperty =
        AvaloniaProperty.Register<SkillbookRenderControl, string>(nameof(ThemeKey), "empty");

    public static readonly StyledProperty<string> UiFontFamilyProperty =
        AvaloniaProperty.Register<SkillbookRenderControl, string>(nameof(UiFontFamily), "fonts:Inter, Segoe UI");

    public static readonly StyledProperty<string> CodeFontFamilyProperty =
        AvaloniaProperty.Register<SkillbookRenderControl, string>(nameof(CodeFontFamily), "avares://ContextControl.Workbench/Assets/Fonts#Cascadia Code, Consolas");

    private const double ContentTop = 2.0;
    private const double ContentBottom = 8.0;
    private const double HorizontalInset = 2.0;
    private const double EstimatedEntryHeight = 190.0;
    private const double EntryGap = 7.0;
    private const double SectionHeaderHeight = 28.0;
    private const double SectionHeaderGap = 5.0;
    private const double CardPaddingX = 8.0;
    private const double CardPaddingY = 7.0;
    private const double HeaderHeight = 32.0;
    private const double BodyLineHeight = 14.4;
    private const double BodyFontSize = 10.2;
    private const double ViewportOverscan = 120.0;
    private const int MaxTextCacheEntries = 8192;

    private static readonly FontFamily DefaultCodeFontFamily = new("Consolas");
    private static readonly FontFamily DefaultUiFontFamily = new("Segoe UI");
    private static readonly IBrush TransparentBrush = Brushes.Transparent;
    private static readonly IBrush EditorSurfaceFallbackBrush = Brush.Parse("#F7F3EA");
    private static readonly IBrush PanelBorderFallbackBrush = Brush.Parse("#D8D3C7");
    private static readonly IBrush CommandBackgroundFallbackBrush = Brush.Parse("#EEE9DE");
    private static readonly IBrush CommandBorderFallbackBrush = Brush.Parse("#C9D0D2");
    private static readonly IBrush CommandPrimaryBackgroundFallbackBrush = Brush.Parse("#DDF1E7");
    private static readonly IBrush AccentFallbackBrush = Brush.Parse("#227A58");
    private static readonly IBrush AccentBorderFallbackBrush = Brush.Parse("#79BDA0");
    private static readonly IBrush TextPrimaryFallbackBrush = Brush.Parse("#26383D");
    private static readonly IBrush TextMutedFallbackBrush = Brush.Parse("#6F7F85");

    private readonly Dictionary<TextCacheKey, FormattedText> _textCache = [];
    private readonly Dictionary<int, EntryLayout> _layouts = [];
    private readonly Dictionary<int, double> _rowHeights = [];
    private readonly HeightDeltaIndex _heightDeltas = new();
    private INotifyCollectionChanged? _itemsCollectionChanged;
    private ScrollViewer? _scrollViewer;
    private IDisposable? _offsetSubscription;
    private IDisposable? _viewportSubscription;
    private int _itemCount;
    private double _layoutWidth = -1.0;
    private bool _measureInvalidationQueued;

    public IReadOnlyList<SkillbookEntryViewModel>? Items
    {
        get => GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    public string ThemeKey
    {
        get => GetValue(ThemeKeyProperty);
        set => SetValue(ThemeKeyProperty, value);
    }

    public string UiFontFamily
    {
        get => GetValue(UiFontFamilyProperty);
        set => SetValue(UiFontFamilyProperty, value);
    }

    public string CodeFontFamily
    {
        get => GetValue(CodeFontFamilyProperty);
        set => SetValue(CodeFontFamilyProperty, value);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        AttachItems();
        ResetLayoutCache(ResolveLayoutWidth(Bounds.Width), Items?.Count ?? 0);
        AttachToScrollViewer(this.GetVisualAncestors().OfType<ScrollViewer>().FirstOrDefault());
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        AttachToScrollViewer(null);
        DetachItems();
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ItemsProperty)
        {
            AttachItems();
            ResetLayoutCache(ResolveLayoutWidth(Bounds.Width), Items?.Count ?? 0);
            QueueMeasureInvalidation();
            InvalidateVisual();
        }
        else if (change.Property == ThemeKeyProperty
            || change.Property == UiFontFamilyProperty
            || change.Property == CodeFontFamilyProperty)
        {
            _textCache.Clear();
            ResetLayoutCache(ResolveLayoutWidth(Bounds.Width), Items?.Count ?? 0);
            QueueMeasureInvalidation();
            InvalidateVisual();
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        EnsureLayoutCache(ResolveLayoutWidth(availableSize.Width));
        return new Size(Math.Max(1.0, ResolveLayoutWidth(availableSize.Width)), GetTotalHeight());
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        EnsureLayoutCache(Bounds.Width);

        var viewportTop = Math.Max(0.0, _scrollViewer?.Offset.Y ?? 0.0);
        var viewportHeight = _scrollViewer?.Viewport.Height ?? Math.Min(Bounds.Height, 900.0);
        if (!double.IsFinite(viewportHeight) || viewportHeight <= 0.0)
        {
            viewportHeight = Math.Min(Bounds.Height, 900.0);
        }

        context.DrawRectangle(TransparentBrush, null, new Rect(0, viewportTop, Bounds.Width, viewportHeight));

        var items = Items;
        if (items is null || _itemCount == 0)
        {
            DrawEmpty(context, viewportTop, viewportHeight);
            return;
        }

        var startY = Math.Max(0.0, viewportTop - ViewportOverscan);
        var endY = Math.Min(GetTotalHeight(), viewportTop + viewportHeight + ViewportOverscan);
        var startIndex = FindRowIndexAtOrAfter(startY);
        if (startIndex < 0)
        {
            return;
        }

        var rowTop = GetRowTop(startIndex);
        for (var index = startIndex; index < _itemCount; index++)
        {
            if (rowTop > endY)
            {
                break;
            }

            var layout = GetOrBuildLayout(index);
            if (layout is null)
            {
                break;
            }

            var rowBottom = rowTop + GetRowHeight(index);
            if (rowBottom >= startY)
            {
                DrawEntry(context, layout, rowTop, viewportTop, viewportTop + viewportHeight);
            }

            rowTop = rowBottom;
        }
    }

    private EntryLayout? GetOrBuildLayout(int index)
    {
        var items = Items;
        if (items is null || index < 0 || index >= _itemCount || index >= items.Count)
        {
            return null;
        }

        if (_layouts.TryGetValue(index, out var layout))
        {
            return layout;
        }

        layout = BuildLayout(index, items[index]);
        _layouts[index] = layout;
        SetMeasuredHeight(index, layout.Height);
        return layout;
    }

    private EntryLayout BuildLayout(int index, SkillbookEntryViewModel entry)
    {
        var width = Math.Max(1.0, _layoutWidth);
        var cardWidth = Math.Max(0.0, width - HorizontalInset * 2.0 - 2.0);
        var contentWidth = Math.Max(1.0, cardWidth - CardPaddingX * 2.0);
        var codeFont = ResolveFontFamily(CodeFontFamily, Resource("CodeFontFamily", DefaultCodeFontFamily));
        var bodyLines = WrapLines(NormalizeText(entry.Text), contentWidth, codeFont, FontWeight.Normal, FontStyle.Normal, BodyFontSize);
        var bodyHeight = Math.Max(BodyLineHeight, bodyLines.Count * BodyLineHeight);
        var sectionTitle = ResolveSectionTitle(index, entry);
        var sectionHeight = string.IsNullOrWhiteSpace(sectionTitle) ? 0.0 : SectionHeaderHeight + SectionHeaderGap;
        var cardHeight = CardPaddingY + HeaderHeight + bodyHeight + CardPaddingY;
        return new EntryLayout(
            entry,
            sectionTitle,
            string.IsNullOrWhiteSpace(sectionTitle)
                ? new Rect()
                : new Rect(HorizontalInset, 0, cardWidth, SectionHeaderHeight),
            new Rect(HorizontalInset, sectionHeight, cardWidth, cardHeight),
            new Rect(HorizontalInset + CardPaddingX, sectionHeight + CardPaddingY + HeaderHeight, contentWidth, bodyHeight),
            bodyLines,
            sectionHeight + cardHeight + EntryGap);
    }

    private void DrawEntry(DrawingContext context, EntryLayout layout, double rowTop, double viewportTop, double viewportBottom)
    {
        var uiFont = ResolveFontFamily(UiFontFamily, Resource("UiFontFamily", DefaultUiFontFamily));
        var codeFont = ResolveFontFamily(CodeFontFamily, Resource("CodeFontFamily", DefaultCodeFontFamily));
        DrawSectionHeader(context, layout, rowTop, viewportTop, viewportBottom);
        var card = OffsetY(layout.CardRect, rowTop);
        context.DrawRectangle(Resource("CommandBackgroundBrush", CommandBackgroundFallbackBrush), new Pen(Resource("PanelBorderBrush", PanelBorderFallbackBrush), 1), card, 5, 5);

        var titleRect = new Rect(card.X + CardPaddingX, card.Y + CardPaddingY, Math.Max(0.0, card.Width - CardPaddingX * 2.0 - 74.0), 14.0);
        DrawText(context, layout.Entry.Title, titleRect, uiFont, 10.6, FontWeight.ExtraBold, Resource("TextPrimaryBrush", TextPrimaryFallbackBrush), 110);
        DrawText(context, layout.Entry.Summary, new Rect(titleRect.X, titleRect.Y + 15.0, titleRect.Width, 11.0), codeFont, 8.5, FontWeight.SemiBold, Resource("TextMutedBrush", TextMutedFallbackBrush), 120);

        var sourceRect = new Rect(card.Right - CardPaddingX - 64.0, card.Y + CardPaddingY + 1.0, 64.0, 17.0);
        context.DrawRectangle(Resource("CommandPrimaryBackgroundBrush", CommandPrimaryBackgroundFallbackBrush), new Pen(Resource("AccentBorderBrush", AccentBorderFallbackBrush), 1), sourceRect, 4, 4);
        DrawCenteredText(context, layout.Entry.SourceLabel, sourceRect, codeFont, 8.0, FontWeight.Black, Resource("AccentBrush", AccentFallbackBrush), 16);

        DrawBody(context, layout, rowTop, viewportTop, viewportBottom);
    }

    private void DrawSectionHeader(DrawingContext context, EntryLayout layout, double rowTop, double viewportTop, double viewportBottom)
    {
        if (string.IsNullOrWhiteSpace(layout.SectionTitle))
        {
            return;
        }

        var rect = OffsetY(layout.SectionRect, rowTop);
        if (rect.Bottom < viewportTop || rect.Y > viewportBottom)
        {
            return;
        }

        var uiFont = ResolveFontFamily(UiFontFamily, Resource("UiFontFamily", DefaultUiFontFamily));
        var codeFont = ResolveFontFamily(CodeFontFamily, Resource("CodeFontFamily", DefaultCodeFontFamily));
        var title = GetFormattedText(layout.SectionTitle, Resource("TextPrimaryBrush", TextPrimaryFallbackBrush), uiFont, FontWeight.ExtraBold, FontStyle.Normal, 13.0);
        var summary = GetFormattedText(ResolveSectionSummary(layout.Entry.Source), Resource("TextMutedBrush", TextMutedFallbackBrush), codeFont, FontWeight.SemiBold, FontStyle.Normal, 8.8);
        DrawClippedText(context, title, rect, new Point(rect.X + 2.0, rect.Y + 2.0));
        DrawClippedText(context, summary, new Rect(rect.X + 2.0, rect.Y + 17.0, rect.Width - 4.0, 10.0), new Point(rect.X + 2.0, rect.Y + 17.0));
    }

    private void DrawBody(DrawingContext context, EntryLayout layout, double rowTop, double viewportTop, double viewportBottom)
    {
        var rect = OffsetY(layout.BodyRect, rowTop);
        if (rect.Bottom < viewportTop || rect.Y > viewportBottom)
        {
            return;
        }

        var codeFont = ResolveFontFamily(CodeFontFamily, Resource("CodeFontFamily", DefaultCodeFontFamily));
        var brush = Resource("TextPrimaryBrush", TextPrimaryFallbackBrush);
        var firstLine = Math.Max(0, (int)Math.Floor((viewportTop - rect.Y) / BodyLineHeight) - 1);
        var lastLine = Math.Min(layout.BodyLines.Count - 1, (int)Math.Ceiling((viewportBottom - rect.Y) / BodyLineHeight) + 1);
        using (context.PushClip(rect))
        {
            for (var index = firstLine; index <= lastLine; index++)
            {
                var line = layout.BodyLines[index];
                if (line.Length == 0)
                {
                    continue;
                }

                var text = GetFormattedText(line, brush, codeFont, FontWeight.Normal, FontStyle.Normal, BodyFontSize);
                context.DrawText(text, new Point(rect.X, rect.Y + index * BodyLineHeight));
            }
        }
    }

    private void DrawEmpty(DrawingContext context, double viewportTop, double viewportHeight)
    {
        var uiFont = ResolveFontFamily(UiFontFamily, Resource("UiFontFamily", DefaultUiFontFamily));
        var text = GetFormattedText("No Skillbook entries found.", Resource("TextMutedBrush", TextMutedFallbackBrush), uiFont, FontWeight.SemiBold, FontStyle.Normal, 11.0);
        DrawClippedText(context, text, new Rect(0, viewportTop, Bounds.Width, viewportHeight), new Point(Math.Max(8.0, (Bounds.Width - text.Width) * 0.5), viewportTop + 36.0));
    }

    private void AttachItems()
    {
        DetachItems();
        if (Items is INotifyCollectionChanged collectionChanged)
        {
            _itemsCollectionChanged = collectionChanged;
            collectionChanged.CollectionChanged += OnItemsCollectionChanged;
        }
    }

    private void DetachItems()
    {
        if (_itemsCollectionChanged is not null)
        {
            _itemsCollectionChanged.CollectionChanged -= OnItemsCollectionChanged;
            _itemsCollectionChanged = null;
        }
    }

    private void OnItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        ResetLayoutCache(ResolveLayoutWidth(Bounds.Width), Items?.Count ?? 0);
        QueueMeasureInvalidation();
        InvalidateVisual();
    }

    private void EnsureLayoutCache(double width)
    {
        width = ResolveLayoutWidth(width);
        var count = Items?.Count ?? 0;
        if (Math.Abs(width - _layoutWidth) < 0.5 && count == _itemCount)
        {
            return;
        }

        ResetLayoutCache(width, count);
    }

    private void ResetLayoutCache(double width, int count)
    {
        _layoutWidth = Math.Max(1.0, width);
        _itemCount = Math.Max(0, count);
        _layouts.Clear();
        _rowHeights.Clear();
        _heightDeltas.Reset(_itemCount);
    }

    private void SetMeasuredHeight(int index, double height)
    {
        var wasMeasured = _rowHeights.TryGetValue(index, out var measuredHeight);
        var previous = wasMeasured ? measuredHeight : EstimatedEntryHeight;
        if (Math.Abs(previous - height) < 0.5 && wasMeasured)
        {
            return;
        }

        _rowHeights[index] = height;
        _heightDeltas.SetDelta(index, height - EstimatedEntryHeight);
        QueueMeasureInvalidation();
    }

    private double GetRowTop(int index)
    {
        index = Math.Clamp(index, 0, _itemCount);
        return ContentTop + (index * EstimatedEntryHeight) + _heightDeltas.PrefixSum(index);
    }

    private double GetRowHeight(int index)
    {
        return _rowHeights.TryGetValue(index, out var height) && height > 0.0 ? height : EstimatedEntryHeight;
    }

    private double GetTotalHeight()
    {
        return ContentTop + ContentBottom + (_itemCount * EstimatedEntryHeight) + _heightDeltas.PrefixSum(_itemCount);
    }

    private double ResolveLayoutWidth(double width)
    {
        if (double.IsFinite(width) && width > 0.0)
        {
            return width;
        }

        if (double.IsFinite(_layoutWidth) && _layoutWidth > 0.0)
        {
            return _layoutWidth;
        }

        return Math.Max(1.0, Bounds.Width);
    }

    private int FindRowIndexAtOrAfter(double y)
    {
        if (_itemCount == 0)
        {
            return -1;
        }

        var lo = 0;
        var hi = _itemCount - 1;
        while (lo < hi)
        {
            var mid = lo + ((hi - lo) / 2);
            if (GetRowTop(mid + 1) <= y)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid;
            }
        }

        return lo;
    }

    private void AttachToScrollViewer(ScrollViewer? scrollViewer)
    {
        if (ReferenceEquals(_scrollViewer, scrollViewer))
        {
            return;
        }

        _offsetSubscription?.Dispose();
        _viewportSubscription?.Dispose();
        _offsetSubscription = null;
        _viewportSubscription = null;
        _scrollViewer = scrollViewer;
        if (scrollViewer is null)
        {
            return;
        }

        _offsetSubscription = scrollViewer.GetObservable(ScrollViewer.OffsetProperty).Subscribe(new ValueObserver<Vector>(_ => InvalidateVisual()));
        _viewportSubscription = scrollViewer.GetObservable(ScrollViewer.ViewportProperty).Subscribe(new ValueObserver<Size>(_ => InvalidateVisual()));
    }

    private void QueueMeasureInvalidation()
    {
        if (_measureInvalidationQueued)
        {
            return;
        }

        _measureInvalidationQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _measureInvalidationQueued = false;
            InvalidateMeasure();
        }, DispatcherPriority.Background);
    }

    private IReadOnlyList<string> WrapLines(string text, double width, FontFamily font, FontWeight weight, FontStyle style, double size)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [""];
        }

        var lines = new List<string>();
        foreach (var rawLine in text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n'))
        {
            var line = rawLine.TrimEnd();
            if (line.Length == 0)
            {
                lines.Add("");
                continue;
            }

            WrapSingleLine(line, width, font, weight, style, size, lines);
        }

        return lines.Count == 0 ? [""] : lines;
    }

    private void WrapSingleLine(string line, double width, FontFamily font, FontWeight weight, FontStyle style, double size, List<string> lines)
    {
        var words = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var current = "";
        foreach (var word in words)
        {
            var candidate = current.Length == 0 ? word : $"{current} {word}";
            if (MeasureTextWidth(candidate, font, weight, style, size) <= width)
            {
                current = candidate;
                continue;
            }

            if (current.Length > 0)
            {
                lines.Add(current);
                current = "";
            }

            if (MeasureTextWidth(word, font, weight, style, size) <= width)
            {
                current = word;
            }
            else
            {
                BreakLongWord(word, width, font, weight, style, size, lines);
            }
        }

        if (current.Length > 0)
        {
            lines.Add(current);
        }
    }

    private void BreakLongWord(string word, double width, FontFamily font, FontWeight weight, FontStyle style, double size, List<string> lines)
    {
        var remaining = word;
        while (remaining.Length > 0)
        {
            var lo = 1;
            var hi = remaining.Length;
            var best = 1;
            while (lo <= hi)
            {
                var mid = lo + ((hi - lo) / 2);
                if (MeasureTextWidth(remaining[..mid], font, weight, style, size) <= width)
                {
                    best = mid;
                    lo = mid + 1;
                }
                else
                {
                    hi = mid - 1;
                }
            }

            lines.Add(remaining[..best]);
            remaining = remaining[best..];
        }
    }

    private double MeasureTextWidth(string text, FontFamily font, FontWeight weight, FontStyle style, double size)
    {
        return GetFormattedText(text, Resource("TextPrimaryBrush", TextPrimaryFallbackBrush), font, weight, style, size).Width;
    }

    private void DrawText(DrawingContext context, string text, Rect rect, FontFamily font, double size, FontWeight weight, IBrush brush, int maxLength)
    {
        var formatted = GetFormattedText(Clean(text, maxLength), brush, font, weight, FontStyle.Normal, size);
        DrawClippedText(context, formatted, rect, new Point(rect.X, rect.Y));
    }

    private void DrawCenteredText(DrawingContext context, string text, Rect rect, FontFamily font, double size, FontWeight weight, IBrush brush, int maxLength)
    {
        var formatted = GetFormattedText(Clean(text, maxLength), brush, font, weight, FontStyle.Normal, size);
        DrawClippedText(context, formatted, rect, new Point(rect.X + Math.Max(3.0, (rect.Width - formatted.Width) * 0.5), rect.Y + 2.0));
    }

    private FormattedText GetFormattedText(string text, IBrush brush, FontFamily font, FontWeight weight, FontStyle style, double size)
    {
        var key = new TextCacheKey(text, RuntimeHelpers.GetHashCode(brush), font.ToString(), weight, style, size);
        if (_textCache.TryGetValue(key, out var formatted))
        {
            return formatted;
        }

        if (_textCache.Count > MaxTextCacheEntries)
        {
            _textCache.Clear();
        }

        formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(font, style, weight), size, brush);
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

    private static Rect OffsetY(Rect rect, double y)
    {
        return new Rect(rect.X, rect.Y + y, rect.Width, rect.Height);
    }

    private static void DrawClippedText(DrawingContext context, FormattedText formatted, Rect clip, Point point)
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

    private static string NormalizeText(string? value)
    {
        return (value ?? "").Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Trim();
    }

    private static string Clean(string? value, int maxLength)
    {
        var clean = (value ?? "").Replace('\r', ' ').Replace('\n', ' ').Trim();
        return clean.Length <= maxLength ? clean : clean[..Math.Max(0, maxLength - 1)] + "...";
    }

    private string ResolveSectionTitle(int index, SkillbookEntryViewModel entry)
    {
        var items = Items;
        if (index <= 0 || items is null || index >= items.Count)
        {
            return entry.SectionTitle;
        }

        var previous = items[index - 1];
        return string.Equals(previous.SourceLabel, entry.SourceLabel, StringComparison.OrdinalIgnoreCase)
            ? ""
            : entry.SectionTitle;
    }

    private static string ResolveSectionSummary(string source)
    {
        return source.ToLowerInvariant() switch
        {
            "codex" => "Every built-in instruction injected into the Codex harness.",
            "cc-main" => "Phase-neutral operating law for the Context Control workflow.",
            "cc-flow" or "skillflow" => "The visible user action, expected output, and Codex duty for each Context Control phase.",
            "project" => "Project-local instructions that override matching global entries.",
            "global" => "Reusable instructions from the user profile Skillbook.",
            _ => "ContextControl instruction entries."
        };
    }

    private sealed record EntryLayout(
        SkillbookEntryViewModel Entry,
        string SectionTitle,
        Rect SectionRect,
        Rect CardRect,
        Rect BodyRect,
        IReadOnlyList<string> BodyLines,
        double Height);

    private readonly record struct TextCacheKey(string Text, int BrushId, string FontFamily, FontWeight Weight, FontStyle Style, double FontSize);

    private sealed class HeightDeltaIndex
    {
        private const int BlockSize = 256;
        private const int BlocksPerGroup = 256;
        private readonly Dictionary<int, double> _deltas = [];
        private readonly Dictionary<int, double> _blockSums = [];
        private readonly Dictionary<int, double> _groupSums = [];
        private int _count;

        public void Reset(int count)
        {
            _count = Math.Max(0, count);
            _deltas.Clear();
            _blockSums.Clear();
            _groupSums.Clear();
        }

        public void SetDelta(int index, double delta)
        {
            if (index < 0 || index >= _count)
            {
                return;
            }

            _deltas.TryGetValue(index, out var previous);
            var difference = delta - previous;
            if (Math.Abs(difference) < 0.01)
            {
                return;
            }

            if (Math.Abs(delta) < 0.01)
            {
                _deltas.Remove(index);
            }
            else
            {
                _deltas[index] = delta;
            }

            var block = index / BlockSize;
            AddSparse(_blockSums, block, difference);
            AddSparse(_groupSums, block / BlocksPerGroup, difference);
        }

        public double PrefixSum(int count)
        {
            count = Math.Clamp(count, 0, _count);
            if (count == 0 || _deltas.Count == 0)
            {
                return 0.0;
            }

            var fullBlocks = count / BlockSize;
            var remainingRows = count % BlockSize;
            var fullGroups = fullBlocks / BlocksPerGroup;
            var remainingBlocks = fullBlocks % BlocksPerGroup;
            var sum = 0.0;
            for (var group = 0; group < fullGroups; group++)
            {
                if (_groupSums.TryGetValue(group, out var groupSum))
                {
                    sum += groupSum;
                }
            }

            var blockStart = fullGroups * BlocksPerGroup;
            for (var offset = 0; offset < remainingBlocks; offset++)
            {
                if (_blockSums.TryGetValue(blockStart + offset, out var blockSum))
                {
                    sum += blockSum;
                }
            }

            var rowStart = fullBlocks * BlockSize;
            for (var offset = 0; offset < remainingRows; offset++)
            {
                if (_deltas.TryGetValue(rowStart + offset, out var delta))
                {
                    sum += delta;
                }
            }

            return sum;
        }

        private static void AddSparse(Dictionary<int, double> values, int key, double delta)
        {
            values.TryGetValue(key, out var previous);
            var next = previous + delta;
            if (Math.Abs(next) < 0.01)
            {
                values.Remove(key);
            }
            else
            {
                values[key] = next;
            }
        }
    }

    private sealed class ValueObserver<T>(Action<T> onNext) : IObserver<T>
    {
        public void OnCompleted() { }
        public void OnError(Exception error) { }
        public void OnNext(T value) => onNext(value);
    }
}
