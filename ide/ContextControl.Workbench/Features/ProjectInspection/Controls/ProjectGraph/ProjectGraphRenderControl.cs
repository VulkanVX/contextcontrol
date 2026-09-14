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

public sealed partial class ProjectGraphRenderControl : Control
{
    public static readonly StyledProperty<IReadOnlyList<ProjectNodeViewModel>?> ItemsProperty =
        AvaloniaProperty.Register<ProjectGraphRenderControl, IReadOnlyList<ProjectNodeViewModel>?>(nameof(Items));

    public static readonly StyledProperty<ProjectNodeViewModel?> SelectedNodeProperty =
        AvaloniaProperty.Register<ProjectGraphRenderControl, ProjectNodeViewModel?>(
            nameof(SelectedNode),
            defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<int> GraphVersionProperty =
        AvaloniaProperty.Register<ProjectGraphRenderControl, int>(nameof(GraphVersion));

    public static readonly StyledProperty<int> CenterOnSelectedNodeVersionProperty =
        AvaloniaProperty.Register<ProjectGraphRenderControl, int>(nameof(CenterOnSelectedNodeVersion));

    public static readonly StyledProperty<string> ThemeKeyProperty =
        AvaloniaProperty.Register<ProjectGraphRenderControl, string>(nameof(ThemeKey), "empty");

    public static readonly StyledProperty<string> UiFontFamilyProperty =
        AvaloniaProperty.Register<ProjectGraphRenderControl, string>(nameof(UiFontFamily), "Segoe UI");

    public static readonly StyledProperty<string> CodeFontFamilyProperty =
        AvaloniaProperty.Register<ProjectGraphRenderControl, string>(nameof(CodeFontFamily), "Consolas");

    public static readonly StyledProperty<string> LayoutModeProperty =
        AvaloniaProperty.Register<ProjectGraphRenderControl, string>(nameof(LayoutMode), "graph");

    public static readonly StyledProperty<string> GenerationPaletteProperty =
        AvaloniaProperty.Register<ProjectGraphRenderControl, string>(
            nameof(GenerationPalette),
            "#7A858B,#808B76,#887F90,#918473,#728987,#8C787A,#82866E,#778094");

    private const int MaxGraphNodes = 4200;
    private const int MaxChildrenPerParent = 120;
    private const int OmittedCountCap = 99999;
    private const double LayoutMargin = 18.0;
    private const double TreeChildGapY = 18.0;
    private const double TreePackGap = 8.0;
    private const double TreeModuleGap = 24.0;
    private const double TreeRegionPadding = 6.0;
    private const double TreeLabelSidePadding = 10.0;
    private const double GenerationSectionGap = 12.0;
    private const double RootFileBlockGap = 8.0;
    private const double CubePackGap = 9.0;
    private const double MinZoom = 0.05;
    private const double MaxZoom = 2.35;
    private const double WheelPanStep = 42.0;
    private const int MaxTextCacheEntries = 4096;
    private const int MaxPenCacheEntries = 768;

    private static readonly FontFamily DefaultUiFontFamily = new("Segoe UI");
    private static readonly FontFamily DefaultCodeFontFamily = new("Consolas");
    private static Cursor HandCursor => new(StandardCursorType.Hand);
    private static Cursor ArrowCursor => new(StandardCursorType.Arrow);
    private static Cursor PanCursor => new(StandardCursorType.SizeAll);

    private static readonly IBrush EditorSurfaceFallbackBrush = Brush.Parse("#F8F7F2");
    private static readonly IBrush PanelBorderFallbackBrush = Brush.Parse("#C9D0D2");
    private static readonly IBrush CommandBackgroundFallbackBrush = Brush.Parse("#EEF1EF");
    private static readonly IBrush DirectoryHighlightFallbackBrush = Brush.Parse("#EBF0EA");
    private static readonly IBrush HistoryActiveFallbackBrush = Brush.Parse("#E7EEF0");
    private static readonly IBrush DropdownSelectedFallbackBrush = Brush.Parse("#CFE8EC");
    private static readonly IBrush TextPrimaryFallbackBrush = Brush.Parse("#31464B");
    private static readonly IBrush TextMutedFallbackBrush = Brush.Parse("#6F7F85");
    private static readonly IBrush FolderTextFallbackBrush = Brush.Parse("#31464B");
    private static readonly IBrush FileTextFallbackBrush = Brush.Parse("#51656B");
    private static readonly IBrush ExternalTextFallbackBrush = Brush.Parse("#C9852B");
    private static readonly IBrush AccentFallbackBrush = Brush.Parse("#227A58");
    private static readonly IBrush AccentBorderFallbackBrush = Brush.Parse("#79BDA0");
    private static readonly IBrush MetricFileFallbackBrush = Brush.Parse("#4E7D88");
    private static readonly IBrush MetricLocFallbackBrush = Brush.Parse("#5E766D");
    private static readonly Color EmptyFolderDarkColor = Color.Parse("#252B2F");
    private static readonly Color EmptyFolderLightColor = Color.Parse("#DEE4E7");
    private static readonly Color EmptyFolderBorderDarkColor = Color.Parse("#58636A");
    private static readonly Color EmptyFolderBorderLightColor = Color.Parse("#88959B");
    private static readonly Color[] LightRegionPalette =
    [
        Color.Parse("#D8EBFA"),
        Color.Parse("#E3E7FB"),
        Color.Parse("#D6F1F3"),
        Color.Parse("#E8E1FA"),
        Color.Parse("#DDEFF8"),
        Color.Parse("#DCEAFD"),
        Color.Parse("#D3F0EA"),
        Color.Parse("#E7EDFA")
    ];
    private static readonly Color[] DarkRegionPalette =
    [
        Color.Parse("#102B3C"),
        Color.Parse("#1D2646"),
        Color.Parse("#10363C"),
        Color.Parse("#292346"),
        Color.Parse("#143245"),
        Color.Parse("#172E49"),
        Color.Parse("#12372F"),
        Color.Parse("#1A2D42")
    ];
    private static readonly Color[] DefaultGenerationBasePalette =
    [
        Color.Parse("#7A858B"),
        Color.Parse("#808B76"),
        Color.Parse("#887F90"),
        Color.Parse("#918473"),
        Color.Parse("#728987"),
        Color.Parse("#8C787A"),
        Color.Parse("#82866E"),
        Color.Parse("#778094")
    ];

    private readonly List<GraphNode> _roots = [];
    private readonly List<GraphNode> _nodes = [];
    private readonly List<GraphEdge> _edges = [];
    private readonly List<EdgeRoute> _edgeRoutes = [];
    private readonly List<EdgeSegment> _edgeRouteSegments = [];
    private readonly Dictionary<string, Point> _manualPositions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<uint, IBrush> _solidBrushCache = new();
    private readonly Dictionary<PenCacheKey, Pen> _penCache = new();
    private readonly Dictionary<TextCacheKey, FormattedText> _textCache = new();
    private static readonly Pen EdgeRouteRailPen = new(Brushes.Transparent);
    private static readonly Pen EdgeRouteBranchPen = new(Brushes.Transparent);

    private INotifyCollectionChanged? _itemsCollectionChanged;
    private bool _isBuildingEdgeRoutes;
    private bool _currentEdgeRouteHasSegments;
    private bool _layoutDirty = true;
    private bool _fitRequested = true;
    private bool _viewportInitialized;
    private bool _isExportRendering;
    private Rect _currentEdgeRouteBounds;
    private Rect _contentBounds = new(0, 0, 1, 1);
    private double _zoom = 1.0;
    private Vector _pan = new(22, 22);
    private bool _isPanning;
    private Point _panStartScreen;
    private Vector _panStart;
    private GraphNode? _hoveredNode;

    public IReadOnlyList<ProjectNodeViewModel>? Items
    {
        get => GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    public ProjectNodeViewModel? SelectedNode
    {
        get => GetValue(SelectedNodeProperty);
        set => SetValue(SelectedNodeProperty, value);
    }

    public int GraphVersion
    {
        get => GetValue(GraphVersionProperty);
        set => SetValue(GraphVersionProperty, value);
    }

    public int CenterOnSelectedNodeVersion
    {
        get => GetValue(CenterOnSelectedNodeVersionProperty);
        set => SetValue(CenterOnSelectedNodeVersionProperty, value);
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

    public string LayoutMode
    {
        get => GetValue(LayoutModeProperty);
        set => SetValue(LayoutModeProperty, value);
    }

    public string GenerationPalette
    {
        get => GetValue(GenerationPaletteProperty);
        set => SetValue(GenerationPaletteProperty, value);
    }

    public ProjectGraphRenderControl()
    {
        Focusable = true;
    }

    public void FitToView()
    {
        _fitRequested = true;
        InvalidateVisual();
    }

    public void ResetLayout()
    {
        _manualPositions.Clear();
        _layoutDirty = true;
        _fitRequested = true;
        InvalidateVisual();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ItemsProperty)
        {
            AttachItems();
            _fitRequested = true;
            MarkLayoutDirty();
        }
        else if (change.Property == GraphVersionProperty)
        {
            MarkLayoutDirty();
        }
        else if (change.Property == CenterOnSelectedNodeVersionProperty)
        {
            CenterSelectedNode();
        }
        else if (change.Property == ThemeKeyProperty)
        {
            _solidBrushCache.Clear();
            _penCache.Clear();
            _textCache.Clear();
            InvalidateVisual();
        }
        else if (change.Property == GenerationPaletteProperty)
        {
            _solidBrushCache.Clear();
            _penCache.Clear();
            InvalidateVisual();
        }
        else if (change.Property == LayoutModeProperty)
        {
            _manualPositions.Clear();
            _fitRequested = true;
            MarkLayoutDirty();
        }
        else if (change.Property == UiFontFamilyProperty
            || change.Property == CodeFontFamilyProperty
            || change.Property == SelectedNodeProperty)
        {
            _textCache.Clear();
            InvalidateVisual();
        }
    }

    public void CenterSelectedNode()
    {
        if (SelectedNode is null || Bounds.Width <= 1 || Bounds.Height <= 1)
        {
            return;
        }

        EnsureLayout();
        var target = FindGraphNode(SelectedNode);
        if (target is null)
        {
            return;
        }

        _fitRequested = false;
        _viewportInitialized = true;
        _pan = new Vector(
            Bounds.Width * 0.5 - target.Bounds.Center.X * _zoom,
            Bounds.Height * 0.5 - target.Bounds.Center.Y * _zoom);
        InvalidateVisual();
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        if (!_viewportInitialized)
        {
            _fitRequested = true;
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();

        var point = e.GetPosition(this);
        var properties = e.GetCurrentPoint(this).Properties;
        if (properties.IsLeftButtonPressed)
        {
            var hit = HitTestGraphNode(point);
            if (hit is not null)
            {
                SelectedNode = hit.Node;
                Cursor = HandCursor;
                e.Handled = true;
                InvalidateVisual();
                return;
            }

            _isPanning = true;
            _panStartScreen = point;
            _panStart = _pan;
            e.Pointer.Capture(this);
            Cursor = PanCursor;
            e.Handled = true;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var point = e.GetPosition(this);

        if (_isPanning)
        {
            var delta = point - _panStartScreen;
            _pan = _panStart + delta;
            _viewportInitialized = true;
            e.Handled = true;
            InvalidateVisual();
            return;
        }

        var hovered = HitTestGraphNode(point);
        if (!ReferenceEquals(_hoveredNode, hovered))
        {
            _hoveredNode = hovered;
            Cursor = hovered is null ? ArrowCursor : HandCursor;
            InvalidateVisual();
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _isPanning = false;
        e.Pointer.Capture(null);
        Cursor = _hoveredNode is null ? ArrowCursor : HandCursor;
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (!_isPanning)
        {
            _hoveredNode = null;
            Cursor = ArrowCursor;
            InvalidateVisual();
        }
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);

        if ((e.KeyModifiers & KeyModifiers.Control) != 0)
        {
            var factor = e.Delta.Y > 0 ? 1.12 : 0.89;
            ZoomAt(e.GetPosition(this), factor);
        }
        else if ((e.KeyModifiers & KeyModifiers.Shift) != 0)
        {
            _pan += new Vector(e.Delta.Y * WheelPanStep, 0);
        }
        else
        {
            _pan += new Vector(e.Delta.X * WheelPanStep, e.Delta.Y * WheelPanStep);
        }

        _viewportInitialized = true;
        e.Handled = true;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        EnsureLayout();
        FitViewportIfRequested();

        var viewport = new Rect(0, 0, Bounds.Width, Bounds.Height);
        DrawGraph(context, viewport, includeGrid: true);
    }

    private void AttachItems()
    {
        if (_itemsCollectionChanged is not null)
        {
            _itemsCollectionChanged.CollectionChanged -= OnItemsCollectionChanged;
            _itemsCollectionChanged = null;
        }

        if (Items is INotifyCollectionChanged collectionChanged)
        {
            _itemsCollectionChanged = collectionChanged;
            collectionChanged.CollectionChanged += OnItemsCollectionChanged;
        }
    }

    private void OnItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        _fitRequested = true;
        MarkLayoutDirty();
    }

    private void MarkLayoutDirty()
    {
        _layoutDirty = true;
        _textCache.Clear();
        InvalidateVisual();
    }

}
