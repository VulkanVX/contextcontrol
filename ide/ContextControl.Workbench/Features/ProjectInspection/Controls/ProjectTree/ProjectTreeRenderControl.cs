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

public enum ProjectTreeHitKind
{
    None,
    Row,
    Toggle,
    Include
}

public readonly record struct ProjectTreeHitTestResult(TreeRowViewModel? Row, ProjectTreeHitKind Kind, int Index);

public sealed partial class ProjectTreeRenderControl : Control
{
    public static readonly StyledProperty<IReadOnlyList<TreeRowViewModel>?> ItemsProperty =
        AvaloniaProperty.Register<ProjectTreeRenderControl, IReadOnlyList<TreeRowViewModel>?>(nameof(Items));

    public static readonly StyledProperty<bool> ShowFileDetailsProperty =
        AvaloniaProperty.Register<ProjectTreeRenderControl, bool>(nameof(ShowFileDetails));

    public static readonly StyledProperty<string> ThemeKeyProperty =
        AvaloniaProperty.Register<ProjectTreeRenderControl, string>(nameof(ThemeKey), "empty");

    public static readonly StyledProperty<string> UiFontFamilyProperty =
        AvaloniaProperty.Register<ProjectTreeRenderControl, string>(nameof(UiFontFamily), "fonts:Inter, Segoe UI");

    public static readonly StyledProperty<string> CodeFontFamilyProperty =
        AvaloniaProperty.Register<ProjectTreeRenderControl, string>(nameof(CodeFontFamily), "avares://ContextControl.Workbench/Assets/Fonts#Cascadia Code, Consolas");

    public static readonly StyledProperty<bool> ThemeAdaptLocColorProperty =
        AvaloniaProperty.Register<ProjectTreeRenderControl, bool>(nameof(ThemeAdaptLocColor));

    public static readonly StyledProperty<bool> ThemeAdaptVersionColorProperty =
        AvaloniaProperty.Register<ProjectTreeRenderControl, bool>(nameof(ThemeAdaptVersionColor));

    public static readonly StyledProperty<bool> ThemeAdaptFileCountColorProperty =
        AvaloniaProperty.Register<ProjectTreeRenderControl, bool>(nameof(ThemeAdaptFileCountColor));

    private const double ContentLeft = 4.0;
    private const double ContentTop = 4.0;
    private const double ContentBottom = 9.0;
    private const double ViewportOverscan = 34.0;
    private const double RailStep = 9.0;
    private const double RailInset = 6.0;
    private const double ToggleSize = 11.0;
    private const double ToggleCornerRadius = 3.0;
    private const double ArrowSize = 8.0;
    private const double IncludeWidth = 38.0;
    private const double IncludeHeight = 15.0;
    private const double IncludeCornerRadius = 4.0;
    private const double NameLeftMargin = 3.0;
    private const double RightInset = 3.0;
    private const double DetailSpacing = 4.0;
    private const int MaxTextCacheEntries = 4096;

    private static readonly FontFamily DefaultCodeFontFamily = new("Consolas");
    private static readonly FontFamily DefaultUiFontFamily = new("Segoe UI");
    private static readonly Cursor HandCursor = new(StandardCursorType.Hand);
    private static readonly Cursor ArrowCursor = new(StandardCursorType.Arrow);

    private static readonly IBrush TransparentBrush = Brushes.Transparent;
    private static readonly IBrush EmptyArrowBrush = new SolidColorBrush(Color.FromRgb(83, 97, 102));
    private static readonly IBrush DarkArrowBrush = new SolidColorBrush(Color.FromRgb(183, 199, 203));
    private static readonly IBrush MatrixArrowBrush = new SolidColorBrush(Color.FromRgb(150, 255, 195));
    private static readonly Pen EmptyRailPen = new(new SolidColorBrush(Color.FromRgb(178, 195, 197)), 1);
    private static readonly Pen EmptyBranchPen = new(new SolidColorBrush(Color.FromRgb(139, 188, 192)), 1);
    private static readonly Pen DarkRailPen = new(new SolidColorBrush(Color.FromRgb(48, 67, 72)), 1);
    private static readonly Pen DarkBranchPen = new(new SolidColorBrush(Color.FromRgb(79, 139, 143)), 1);
    private static readonly Pen MatrixRailPen = new(new SolidColorBrush(Color.FromRgb(42, 118, 91)), 1);
    private static readonly Pen MatrixBranchPen = new(new SolidColorBrush(Color.FromRgb(101, 240, 178)), 1);

    private static readonly IBrush PanelFallbackBrush = Brush.Parse("#F4F2EC");
    private static readonly IBrush CommandBorderFallbackBrush = Brush.Parse("#C9D0D2");
    private static readonly IBrush HistoryActiveFallbackBrush = Brush.Parse("#E7EEF0");
    private static readonly IBrush DirectoryHighlightFallbackBrush = Brush.Parse("#EBF0EA");
    private static readonly IBrush CurrentRowFallbackBrush = Brush.Parse("#DCEBEE");
    private static readonly IBrush CurrentRowBorderFallbackBrush = Brush.Parse("#B7CBD1");
    private static readonly IBrush SelectedRowFallbackBrush = Brush.Parse("#CFE8EC");
    private static readonly IBrush FolderTextFallbackBrush = Brush.Parse("#31464B");
    private static readonly IBrush FileTextFallbackBrush = Brush.Parse("#51656B");
    private static readonly IBrush ExternalTextFallbackBrush = Brush.Parse("#8A6262");
    private static readonly IBrush TextMutedFallbackBrush = Brush.Parse("#6F7F85");
    private static readonly IBrush VersionFixedFallbackBrush = Brush.Parse("#858B91");
    private static readonly IBrush NodeTextFallbackBrush = Brush.Parse("#475B61");
    private static readonly IBrush SkipTextFallbackBrush = Brush.Parse("#9B6868");
    private static readonly IBrush MetricFileFallbackBrush = Brush.Parse("#4E7D88");
    private static readonly IBrush MetricFileFixedFallbackBrush = Brush.Parse("#355A86");
    private static readonly IBrush MetricLocFallbackBrush = Brush.Parse("#5E766D");
    private static readonly IBrush MetricLocFixedFallbackBrush = Brush.Parse("#D89042");
    private static readonly IBrush IncludeBackgroundFallbackBrush = Brush.Parse("#E9F4EE");
    private static readonly IBrush IncludeBorderFallbackBrush = Brush.Parse("#A5CAB8");
    private static readonly IBrush IncludeTextFallbackBrush = Brush.Parse("#2F6E53");
    private static readonly IBrush CommandPrimaryBackgroundFallbackBrush = Brush.Parse("#DDF1E7");
    private static readonly IBrush AccentBorderFallbackBrush = Brush.Parse("#79BDA0");
    private static readonly IBrush AccentFallbackBrush = Brush.Parse("#227A58");

    private readonly List<INotifyPropertyChanged> _subscribedRows = [];
    private readonly Dictionary<TextCacheKey, FormattedText> _textCache = new();
    private readonly Dictionary<double, StreamGeometry> _arrowGeometryCache = new();
    private double[] _rowTops = [ContentTop];
    private double _totalHeight = ContentTop + ContentBottom;
    private INotifyCollectionChanged? _itemsCollectionChanged;
    private ScrollViewer? _scrollViewer;
    private IDisposable? _offsetSubscription;
    private IDisposable? _viewportSubscription;
    private int _hoveredIndex = -1;
    private ProjectTreeHitKind _hoveredKind = ProjectTreeHitKind.None;

    public IReadOnlyList<TreeRowViewModel>? Items
    {
        get => GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    public bool ShowFileDetails
    {
        get => GetValue(ShowFileDetailsProperty);
        set => SetValue(ShowFileDetailsProperty, value);
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

    public bool ThemeAdaptLocColor
    {
        get => GetValue(ThemeAdaptLocColorProperty);
        set => SetValue(ThemeAdaptLocColorProperty, value);
    }

    public bool ThemeAdaptVersionColor
    {
        get => GetValue(ThemeAdaptVersionColorProperty);
        set => SetValue(ThemeAdaptVersionColorProperty, value);
    }

    public bool ThemeAdaptFileCountColor
    {
        get => GetValue(ThemeAdaptFileCountColorProperty);
        set => SetValue(ThemeAdaptFileCountColorProperty, value);
    }

    public ProjectTreeRenderControl()
    {
        Focusable = false;
    }

    public void BringRowIntoView(TreeRowViewModel? row)
    {
        var items = Items;
        if (row is null || items is null || items.Count == 0)
        {
            return;
        }

        var index = -1;
        for (var itemIndex = 0; itemIndex < items.Count; itemIndex++)
        {
            if (ReferenceEquals(items[itemIndex], row))
            {
                index = itemIndex;
                break;
            }
        }

        if (index < 0 || _rowTops.Length <= index + 1)
        {
            return;
        }

        var scrollViewer = _scrollViewer
            ?? this.GetVisualAncestors().OfType<ScrollViewer>().FirstOrDefault();
        if (scrollViewer is null)
        {
            return;
        }

        var rowTop = _rowTops[index];
        var rowBottom = _rowTops[index + 1];
        var rowCenter = (rowTop + rowBottom) * 0.5;
        var maxY = Math.Max(0, scrollViewer.Extent.Height - scrollViewer.Viewport.Height);
        var targetY = Math.Clamp(rowCenter - scrollViewer.Viewport.Height * 0.5, 0, maxY);
        scrollViewer.Offset = new Vector(scrollViewer.Offset.X, targetY);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        AttachToScrollViewer(this.GetVisualAncestors().OfType<ScrollViewer>().FirstOrDefault());
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        AttachToScrollViewer(null);
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ItemsProperty)
        {
            AttachItems();
        }
        else if (change.Property == ShowFileDetailsProperty)
        {
            InvalidateVisual();
        }
        else if (change.Property == ThemeKeyProperty)
        {
            _textCache.Clear();
            _arrowGeometryCache.Clear();
            InvalidateVisual();
        }
        else if (change.Property == ThemeAdaptLocColorProperty
            || change.Property == ThemeAdaptVersionColorProperty
            || change.Property == ThemeAdaptFileCountColorProperty)
        {
            _textCache.Clear();
            InvalidateVisual();
        }
        else if (change.Property == UiFontFamilyProperty
            || change.Property == CodeFontFamilyProperty)
        {
            _textCache.Clear();
            InvalidateVisual();
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsFinite(availableSize.Width) ? availableSize.Width : 1.0;
        return new Size(Math.Max(1.0, width), _totalHeight);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var hit = HitTestRow(e.GetPosition(this));
        SetHoveredHit(hit.Index, hit.Kind);
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        SetHoveredHit(-1, ProjectTreeHitKind.None);
    }

}
