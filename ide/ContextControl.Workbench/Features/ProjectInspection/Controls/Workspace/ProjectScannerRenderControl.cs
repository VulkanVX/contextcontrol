// CC-DESC: Draws the project scanner summary as one cached bounded surface.

using System.Collections.Specialized;
using System.Globalization;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using ContextControl.Workbench.Services;

namespace ContextControl.Workbench.Controls;

public sealed class ProjectScannerRenderControl : Control
{
    public static readonly StyledProperty<IReadOnlyList<ProjectStackMetric>?> MetricsProperty =
        AvaloniaProperty.Register<ProjectScannerRenderControl, IReadOnlyList<ProjectStackMetric>?>(nameof(Metrics));

    public static readonly StyledProperty<IReadOnlyList<ProjectStackSection>?> IdentitySectionsProperty =
        AvaloniaProperty.Register<ProjectScannerRenderControl, IReadOnlyList<ProjectStackSection>?>(nameof(IdentitySections));

    public static readonly StyledProperty<IReadOnlyList<ProjectStackSection>?> FileSectionsProperty =
        AvaloniaProperty.Register<ProjectScannerRenderControl, IReadOnlyList<ProjectStackSection>?>(nameof(FileSections));

    public static readonly StyledProperty<IReadOnlyList<ProjectStackSection>?> RuleSectionsProperty =
        AvaloniaProperty.Register<ProjectScannerRenderControl, IReadOnlyList<ProjectStackSection>?>(nameof(RuleSections));

    public static readonly StyledProperty<IReadOnlyList<ProjectStackSection>?> DiagnosticSectionsProperty =
        AvaloniaProperty.Register<ProjectScannerRenderControl, IReadOnlyList<ProjectStackSection>?>(nameof(DiagnosticSections));

    public static readonly StyledProperty<bool> ShowMetricsProperty =
        AvaloniaProperty.Register<ProjectScannerRenderControl, bool>(nameof(ShowMetrics), true);

    public static readonly StyledProperty<string> ThemeKeyProperty =
        AvaloniaProperty.Register<ProjectScannerRenderControl, string>(nameof(ThemeKey), "empty");

    public static readonly StyledProperty<string> UiFontFamilyProperty =
        AvaloniaProperty.Register<ProjectScannerRenderControl, string>(nameof(UiFontFamily), "fonts:Inter, Segoe UI");

    public static readonly StyledProperty<string> CodeFontFamilyProperty =
        AvaloniaProperty.Register<ProjectScannerRenderControl, string>(nameof(CodeFontFamily), "avares://ContextControl.Workbench/Assets/Fonts#Cascadia Code, Consolas");

    private const double ContentTop = 2.0;
    private const double ContentBottom = 8.0;
    private const double ColumnGap = 6.0;
    private const double PanelGap = 6.0;
    private const double PanelPadding = 7.0;
    private const double SectionGap = 5.0;
    private const double ItemLineHeight = 12.2;
    private const double ItemFontSize = 8.6;
    private const int MaxTextCacheEntries = 8192;

    private static readonly FontFamily DefaultCodeFontFamily = new("Consolas");
    private static readonly FontFamily DefaultUiFontFamily = new("Segoe UI");
    private static readonly IBrush TransparentBrush = Brushes.Transparent;
    private static readonly IBrush EditorSurfaceFallbackBrush = Brush.Parse("#F7F3EA");
    private static readonly IBrush PanelBorderFallbackBrush = Brush.Parse("#D8D3C7");
    private static readonly IBrush CommandBackgroundFallbackBrush = Brush.Parse("#EEE9DE");
    private static readonly IBrush CommandBorderFallbackBrush = Brush.Parse("#C9D0D2");
    private static readonly IBrush AccentFallbackBrush = Brush.Parse("#227A58");
    private static readonly IBrush TextPrimaryFallbackBrush = Brush.Parse("#26383D");
    private static readonly IBrush TextMutedFallbackBrush = Brush.Parse("#6F7F85");

    private readonly Dictionary<TextCacheKey, FormattedText> _textCache = [];
    private readonly List<IDisposableCollectionSubscription> _collectionSubscriptions = [];
    private ScannerLayout _layout = ScannerLayout.Empty;
    private ScrollViewer? _scrollViewer;
    private IDisposable? _offsetSubscription;
    private IDisposable? _viewportSubscription;
    private double _layoutWidth = -1.0;
    private bool _layoutValid;

    public IReadOnlyList<ProjectStackMetric>? Metrics
    {
        get => GetValue(MetricsProperty);
        set => SetValue(MetricsProperty, value);
    }

    public IReadOnlyList<ProjectStackSection>? IdentitySections
    {
        get => GetValue(IdentitySectionsProperty);
        set => SetValue(IdentitySectionsProperty, value);
    }

    public IReadOnlyList<ProjectStackSection>? FileSections
    {
        get => GetValue(FileSectionsProperty);
        set => SetValue(FileSectionsProperty, value);
    }

    public IReadOnlyList<ProjectStackSection>? RuleSections
    {
        get => GetValue(RuleSectionsProperty);
        set => SetValue(RuleSectionsProperty, value);
    }

    public IReadOnlyList<ProjectStackSection>? DiagnosticSections
    {
        get => GetValue(DiagnosticSectionsProperty);
        set => SetValue(DiagnosticSectionsProperty, value);
    }

    public bool ShowMetrics
    {
        get => GetValue(ShowMetricsProperty);
        set => SetValue(ShowMetricsProperty, value);
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
        AttachCollections();
        AttachToScrollViewer(this.GetVisualAncestors().OfType<ScrollViewer>().FirstOrDefault());
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        AttachToScrollViewer(null);
        DetachCollections();
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == MetricsProperty
            || change.Property == IdentitySectionsProperty
            || change.Property == FileSectionsProperty
            || change.Property == RuleSectionsProperty
            || change.Property == DiagnosticSectionsProperty
            || change.Property == ShowMetricsProperty)
        {
            AttachCollections();
            InvalidateLayoutCache();
        }
        else if (change.Property == ThemeKeyProperty
            || change.Property == UiFontFamilyProperty
            || change.Property == CodeFontFamilyProperty)
        {
            _textCache.Clear();
            InvalidateLayoutCache();
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        EnsureLayout(ResolveWidth(availableSize.Width));
        return new Size(Math.Max(1.0, ResolveWidth(availableSize.Width)), _layout.Height);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        EnsureLayout(Bounds.Width);

        var viewportTop = Math.Max(0.0, _scrollViewer?.Offset.Y ?? 0.0);
        var viewportHeight = _scrollViewer?.Viewport.Height ?? Math.Min(Bounds.Height, 900.0);
        if (!double.IsFinite(viewportHeight) || viewportHeight <= 0.0)
        {
            viewportHeight = Math.Min(Bounds.Height, 900.0);
        }

        context.DrawRectangle(TransparentBrush, null, new Rect(0, viewportTop, Bounds.Width, viewportHeight));
        var viewport = new Rect(0, viewportTop, Bounds.Width, viewportHeight);
        foreach (var box in _layout.Boxes)
        {
            if (!box.Rect.Intersects(viewport))
            {
                continue;
            }

            context.DrawRectangle(box.Background, box.Border is null ? null : new Pen(box.Border, box.BorderThickness), box.Rect, box.Radius, box.Radius);
        }

        foreach (var text in _layout.Texts)
        {
            if (!text.Rect.Intersects(viewport))
            {
                continue;
            }

            DrawClippedText(context, text.Text, text.Rect, new Point(text.Rect.X, text.Rect.Y));
        }
    }

    private void EnsureLayout(double width)
    {
        width = ResolveWidth(width);
        if (_layoutValid && Math.Abs(width - _layoutWidth) < 0.5)
        {
            return;
        }

        _layoutWidth = width;
        _layout = BuildLayout(width);
        _layoutValid = true;
    }

    private ScannerLayout BuildLayout(double width)
    {
        var boxes = new List<BoxLayout>();
        var texts = new List<TextLayout>();
        var y = ContentTop;
        var codeFont = ResolveFontFamily(CodeFontFamily, Resource("CodeFontFamily", DefaultCodeFontFamily));

        if (ShowMetrics && Metrics is { Count: > 0 } metrics)
        {
            var strip = BuildMetricStrip(metrics, y, width, boxes, texts, codeFont);
            y = strip.Bottom + 6.0;
        }

        if (NoSections())
        {
            var empty = GetFormattedText("Run Scan to inspect this project.", Resource("TextMutedBrush", TextMutedFallbackBrush), codeFont, FontWeight.SemiBold, FontStyle.Normal, 11.0);
            texts.Add(new TextLayout(empty, new Rect(8.0, y + 18.0, Math.Max(0.0, width - 16.0), 18.0)));
            y += 64.0;
            return new ScannerLayout(boxes, texts, Math.Max(1.0, y + ContentBottom));
        }

        var columnWidth = Math.Max(1.0, (width - ColumnGap) * 0.5);
        var leftX = 0.0;
        var rightX = columnWidth + ColumnGap;
        var leftY = y;
        var rightY = y;

        if (SectionCount(IdentitySections) > 0)
        {
            leftY = BuildPanel("Project Signature", IdentitySections, new Rect(leftX, leftY, columnWidth, 1.0), boxes, texts, codeFont, primary: true).Bottom + PanelGap;
        }

        if (SectionCount(FileSections) > 0)
        {
            leftY = BuildPanel("Files", FileSections, new Rect(leftX, leftY, columnWidth, 1.0), boxes, texts, codeFont, primary: false).Bottom + PanelGap;
        }

        if (SectionCount(RuleSections) > 0)
        {
            rightY = BuildPanel("Rule Fit", RuleSections, new Rect(rightX, rightY, columnWidth, 1.0), boxes, texts, codeFont, primary: false).Bottom + PanelGap;
        }

        if (SectionCount(DiagnosticSections) > 0)
        {
            rightY = BuildPanel("Scan Notes", DiagnosticSections, new Rect(rightX, rightY, columnWidth, 1.0), boxes, texts, codeFont, primary: false).Bottom + PanelGap;
        }

        y = Math.Max(leftY, rightY);
        return new ScannerLayout(boxes, texts, Math.Max(1.0, y + ContentBottom));
    }

    private Rect BuildMetricStrip(IReadOnlyList<ProjectStackMetric> metrics, double y, double width, List<BoxLayout> boxes, List<TextLayout> texts, FontFamily codeFont)
    {
        var strip = new Rect(0, y, width, 1.0);
        var metricWidth = 104.0;
        var metricHeight = 34.0;
        var gap = 4.0;
        var sidePadding = 6.0;
        var innerWidth = Math.Max(1.0, width - sidePadding * 2.0);
        var cardWidth = Math.Min(metricWidth, innerWidth);
        var metricsPerRow = Math.Max(1, (int)Math.Floor((innerWidth + gap) / (cardWidth + gap)));
        var rowY = y + 5.0;
        for (var rowStart = 0; rowStart < metrics.Count; rowStart += metricsPerRow)
        {
            var rowMetrics = metrics.Skip(rowStart).Take(metricsPerRow).ToArray();
            var rowWidth = rowMetrics.Length * cardWidth + Math.Max(0, rowMetrics.Length - 1) * gap;
            var x = Math.Max(sidePadding, (width - rowWidth) * 0.5);
            foreach (var metric in rowMetrics)
            {
                var card = new Rect(x, rowY, cardWidth, metricHeight);
                boxes.Add(new BoxLayout(card, Resource("EditorSurfaceBrush", EditorSurfaceFallbackBrush), Resource("PanelBorderBrush", PanelBorderFallbackBrush), 1, 4));
                AddText(texts, metric.Key, new Rect(card.X + 5.0, card.Y + 3.0, card.Width - 10.0, 10.0), codeFont, 8.0, FontWeight.Black, Resource("TextMutedBrush", TextMutedFallbackBrush), 24);
                AddText(texts, metric.Value, new Rect(card.X + 5.0, card.Y + 16.0, card.Width - 10.0, 14.0), codeFont, 11.0, FontWeight.Black, Resource("TextPrimaryBrush", TextPrimaryFallbackBrush), 28);
                x += cardWidth + gap;
            }

            if (rowStart + metricsPerRow < metrics.Count)
            {
                rowY += metricHeight + 4.0;
            }
        }

        strip = new Rect(0, y, width, Math.Max(40.0, rowY + metricHeight + 1.0 - y));
        boxes.Insert(0, new BoxLayout(strip, Resource("CommandBackgroundBrush", CommandBackgroundFallbackBrush), Resource("PanelBorderBrush", PanelBorderFallbackBrush), 1, 5));
        return strip;
    }

    private Rect BuildPanel(string title, IReadOnlyList<ProjectStackSection>? sections, Rect seed, List<BoxLayout> boxes, List<TextLayout> texts, FontFamily codeFont, bool primary)
    {
        var panelBoxIndex = boxes.Count;
        var y = seed.Y + PanelPadding;
        AddText(texts, title, new Rect(seed.X + PanelPadding, y, Math.Max(0.0, seed.Width - PanelPadding * 2.0), 12.0), codeFont, 8.5, FontWeight.Black, Resource("AccentBrush", AccentFallbackBrush), 40);
        y += 17.0;
        if (sections is { Count: > 0 })
        {
            foreach (var section in sections.Where(section => section.Items.Count > 0))
            {
                y = BuildSection(section, seed.X + PanelPadding, y, Math.Max(1.0, seed.Width - PanelPadding * 2.0), boxes, texts, codeFont) + SectionGap;
            }
        }

        var panel = new Rect(seed.X, seed.Y, seed.Width, Math.Max(42.0, y + PanelPadding - seed.Y));
        boxes.Insert(panelBoxIndex, new BoxLayout(panel, Resource("EditorSurfaceBrush", EditorSurfaceFallbackBrush), Resource(primary ? "CommandBorderBrush" : "PanelBorderBrush", primary ? CommandBorderFallbackBrush : PanelBorderFallbackBrush), 1, 5));
        return panel;
    }

    private double BuildSection(ProjectStackSection section, double x, double y, double width, List<BoxLayout> boxes, List<TextLayout> texts, FontFamily codeFont)
    {
        var sectionBoxIndex = boxes.Count;
        var startY = y;
        var titleRect = new Rect(x, y, width, 18.0);
        boxes.Add(new BoxLayout(titleRect, Resource("EditorSurfaceBrush", EditorSurfaceFallbackBrush), Resource("PanelBorderBrush", PanelBorderFallbackBrush), 1, 4));
        AddText(texts, section.Title, new Rect(titleRect.X + 7.0, titleRect.Y + 3.0, titleRect.Width - 14.0, 11.0), codeFont, 9.0, FontWeight.ExtraBold, Resource("TextPrimaryBrush", TextPrimaryFallbackBrush), 44);
        y += 23.0;

        foreach (var item in section.Items)
        {
            var lines = WrapLines(item, Math.Max(1.0, width - 12.0), codeFont, FontWeight.Normal, FontStyle.Normal, ItemFontSize);
            foreach (var line in lines)
            {
                AddText(texts, line, new Rect(x + 7.0, y, Math.Max(0.0, width - 12.0), ItemLineHeight), codeFont, ItemFontSize, FontWeight.Normal, Resource("TextMutedBrush", TextMutedFallbackBrush), 160);
                y += ItemLineHeight;
            }

            y += 2.0;
        }

        var sectionRect = new Rect(x, startY, width, Math.Max(30.0, y - startY + 3.0));
        boxes.Insert(sectionBoxIndex, new BoxLayout(sectionRect, Resource("CommandBackgroundBrush", CommandBackgroundFallbackBrush), Resource("PanelBorderBrush", PanelBorderFallbackBrush), 1, 0));
        return sectionRect.Bottom;
    }

    private bool NoSections()
    {
        return SectionCount(IdentitySections) + SectionCount(FileSections) + SectionCount(RuleSections) + SectionCount(DiagnosticSections) == 0;
    }

    private static int SectionCount(IReadOnlyList<ProjectStackSection>? sections)
    {
        return sections?.Count(section => section.Items.Count > 0) ?? 0;
    }

    private void AddText(List<TextLayout> texts, string value, Rect rect, FontFamily font, double size, FontWeight weight, IBrush brush, int maxLength)
    {
        texts.Add(new TextLayout(GetFormattedText(Clean(value, maxLength), brush, font, weight, FontStyle.Normal, size), rect));
    }

    private void AttachCollections()
    {
        DetachCollections();
        AddCollection(Metrics);
        AddCollection(IdentitySections);
        AddCollection(FileSections);
        AddCollection(RuleSections);
        AddCollection(DiagnosticSections);
    }

    private void AddCollection<T>(IReadOnlyList<T>? values)
    {
        if (values is INotifyCollectionChanged collection)
        {
            collection.CollectionChanged += OnCollectionChanged;
            _collectionSubscriptions.Add(new CollectionSubscription(collection, OnCollectionChanged));
        }
    }

    private void DetachCollections()
    {
        foreach (var subscription in _collectionSubscriptions)
        {
            subscription.Dispose();
        }

        _collectionSubscriptions.Clear();
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        InvalidateLayoutCache();
    }

    private void InvalidateLayoutCache()
    {
        _layoutValid = false;
        _textCache.Clear();
        InvalidateMeasure();
        InvalidateVisual();
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

    private IReadOnlyList<string> WrapLines(string text, double width, FontFamily font, FontWeight weight, FontStyle style, double size)
    {
        var clean = Clean(text, 260);
        if (clean.Length == 0)
        {
            return [""];
        }

        var lines = new List<string>();
        var current = "";
        foreach (var word in clean.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
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

            current = word;
        }

        if (current.Length > 0)
        {
            lines.Add(current);
        }

        return lines.Count == 0 ? [""] : lines;
    }

    private double MeasureTextWidth(string text, FontFamily font, FontWeight weight, FontStyle style, double size)
    {
        return GetFormattedText(text, Resource("TextPrimaryBrush", TextPrimaryFallbackBrush), font, weight, style, size).Width;
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

    private double ResolveWidth(double width)
    {
        if (double.IsFinite(width) && width > 0.0)
        {
            return width;
        }

        return double.IsFinite(_layoutWidth) && _layoutWidth > 0.0 ? _layoutWidth : Math.Max(1.0, Bounds.Width);
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

    private static string Clean(string? value, int maxLength)
    {
        var clean = (value ?? "").Replace('\r', ' ').Replace('\n', ' ').Trim();
        return clean.Length <= maxLength ? clean : clean[..Math.Max(0, maxLength - 1)] + "...";
    }

    private sealed record ScannerLayout(IReadOnlyList<BoxLayout> Boxes, IReadOnlyList<TextLayout> Texts, double Height)
    {
        public static ScannerLayout Empty { get; } = new([], [], 1.0);
    }

    private sealed record BoxLayout(Rect Rect, IBrush Background, IBrush? Border, double BorderThickness, double Radius);

    private sealed record TextLayout(FormattedText Text, Rect Rect);

    private readonly record struct TextCacheKey(string Text, int BrushId, string FontFamily, FontWeight Weight, FontStyle Style, double FontSize);

    private interface IDisposableCollectionSubscription : IDisposable;

    private sealed class CollectionSubscription(INotifyCollectionChanged collection, NotifyCollectionChangedEventHandler handler) : IDisposableCollectionSubscription
    {
        public void Dispose()
        {
            collection.CollectionChanged -= handler;
        }
    }

    private sealed class ValueObserver<T>(Action<T> onNext) : IObserver<T>
    {
        public void OnCompleted() { }
        public void OnError(Exception error) { }
        public void OnNext(T value) => onNext(value);
    }
}
