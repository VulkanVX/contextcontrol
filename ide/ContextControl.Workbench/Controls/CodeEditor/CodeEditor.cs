using System.Globalization;
using System.Text;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TextMateSharp.Grammars;
using TextMateSharp.Registry;

namespace ContextControl.Workbench.Controls;

public sealed partial class CodeEditor : UserControl
{
    private const double EditorTopPadding = 10;
    private const double EditorBottomPadding = 14;
    private const double DefaultEditorFontSize = 12;
    private const double DefaultEditorLineHeight = 16;
    private const double MinimapWidth = 46;
    private const double ScrollbarReserve = 12;
    private const double MinimapIdleOpacity = 0.76;
    private const double MinimapHoverOpacity = 0.96;
    private const double BottomAnchorTolerance = 2;
    private const string AllSummaryFoldKinds = "namespace,class,struct,interface,enum,method,property,object,block,array,arguments";
    private const string MatrixConsoleSkinKey = "matrix-console";
    private static readonly string[] CommonGrammarExtensions = [".cs", ".xaml", ".xml", ".json", ".ps1", ".md", ".js", ".ts", ".css", ".html"];
    private static readonly object GrammarLock = new();
    private static readonly RegistryOptions SharedRegistryOptions = new(ThemeName.LightPlus);
    private static readonly Registry SharedRegistry = new(SharedRegistryOptions);
    private static readonly Dictionary<string, IGrammar?> SharedGrammarByExtension = new(StringComparer.OrdinalIgnoreCase);
    private static int _grammarPrewarmQueued;

    public static readonly StyledProperty<string> DocumentTextProperty =
        AvaloniaProperty.Register<CodeEditor, string>(nameof(DocumentText), "");

    public static readonly StyledProperty<string> DocumentPathProperty =
        AvaloniaProperty.Register<CodeEditor, string>(nameof(DocumentPath), "");

    public static readonly StyledProperty<IReadOnlyDictionary<int, string>?> LineChangesProperty =
        AvaloniaProperty.Register<CodeEditor, IReadOnlyDictionary<int, string>?>(nameof(LineChanges));

    public static readonly StyledProperty<string> ThemeKeyProperty =
        AvaloniaProperty.Register<CodeEditor, string>(nameof(ThemeKey), "empty");

    public static readonly StyledProperty<string> SyntaxThemeKeyProperty =
        AvaloniaProperty.Register<CodeEditor, string>(nameof(SyntaxThemeKey), "adaptive");

    public static readonly StyledProperty<string> CodeFontFamilyProperty =
        AvaloniaProperty.Register<CodeEditor, string>(nameof(CodeFontFamily), "avares://ContextControl.Workbench/Assets/Fonts#Cascadia Code, Consolas");

    public static readonly StyledProperty<double> EditorFontSizeProperty =
        AvaloniaProperty.Register<CodeEditor, double>(nameof(EditorFontSize), DefaultEditorFontSize);

    public static readonly StyledProperty<string> SkinKeyProperty =
        AvaloniaProperty.Register<CodeEditor, string>(nameof(SkinKey), "default");

    public static readonly StyledProperty<bool> ShowMinimapProperty =
        AvaloniaProperty.Register<CodeEditor, bool>(nameof(ShowMinimap), true);

    public static readonly StyledProperty<ScrollBarVisibility> VerticalScrollBarVisibilityProperty =
        AvaloniaProperty.Register<CodeEditor, ScrollBarVisibility>(nameof(VerticalScrollBarVisibility), ScrollBarVisibility.Auto);

    public static readonly StyledProperty<bool> ShowFoldArrowsProperty =
        AvaloniaProperty.Register<CodeEditor, bool>(nameof(ShowFoldArrows), true);

    public static readonly StyledProperty<bool> ShowSummaryArrowBordersProperty =
        AvaloniaProperty.Register<CodeEditor, bool>(nameof(ShowSummaryArrowBorders), true);

    public static readonly StyledProperty<bool> FoldArrowsInCodeEditorProperty =
        AvaloniaProperty.Register<CodeEditor, bool>(nameof(FoldArrowsInCodeEditor), true);

    public static readonly StyledProperty<bool> UseParentChildArrowIndentationProperty =
        AvaloniaProperty.Register<CodeEditor, bool>(nameof(UseParentChildArrowIndentation), true);

    public static readonly StyledProperty<bool> ShowVerticalScopeLinesProperty =
        AvaloniaProperty.Register<CodeEditor, bool>(nameof(ShowVerticalScopeLines), true);

    public static readonly StyledProperty<string> SummaryFoldKindsProperty =
        AvaloniaProperty.Register<CodeEditor, string>(nameof(SummaryFoldKinds), AllSummaryFoldKinds);

    public static readonly StyledProperty<bool> UseColorfulFamiliesProperty =
        AvaloniaProperty.Register<CodeEditor, bool>(nameof(UseColorfulFamilies), true);

    public static readonly StyledProperty<bool> ShowFoldSummaryPreviewProperty =
        AvaloniaProperty.Register<CodeEditor, bool>(nameof(ShowFoldSummaryPreview), true);

    private readonly CodeTextSurface _surface;
    private readonly CodeMinimap _minimap;
    private readonly ScrollViewer _scroller;
    private readonly ScrollBar _verticalScrollbar;
    private readonly Border _scrollCornerCover;
    private readonly Border _findPanel;
    private readonly TextBox _findBox;
    private readonly TextBlock _findCount;
    private readonly Grid _root;
    private readonly DispatcherTimer _skinAnimationTimer;
    private bool _isMinimapNavigating;
    private bool _hasMinimapDragMoved;
    private double _minimapDragStartY;
    private bool _isAnchoringBottomDuringResize;
    private bool _isAttachedToVisualTree;
    private bool _isSyncingVerticalScrollbar;
    private bool _isScrollbarRefreshQueued;
    private double _verticalOffset;

    public CodeEditor()
    {
        Palette.UseFont(CodeFontFamily);
        Palette.Use(ThemeKey, SyntaxThemeKey);
        _surface = new CodeTextSurface();
        _minimap = new CodeMinimap
        {
            Width = MinimapWidth,
            Opacity = MinimapIdleOpacity,
            IsHitTestVisible = true,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Stretch,
            Margin = new Thickness(0, 0, ScrollbarReserve, ScrollbarReserve)
        };
        _minimap.PointerEntered += (_, _) => _minimap.Opacity = MinimapHoverOpacity;
        _minimap.PointerExited += (_, _) => _minimap.Opacity = MinimapIdleOpacity;
        _minimap.PointerPressed += OnMinimapPointerPressed;
        AddHandler(PointerMovedEvent, OnMinimapPointerMoved, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, OnMinimapPointerReleased, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);

        _skinAnimationTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(92)
        };
        _skinAnimationTimer.Tick += OnSkinAnimationTick;

        _scroller = new ScrollViewer
        {
            Background = Palette.Background,
            Content = _surface,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        _surface.SetScrollHost(
            _scroller,
            ScrollToEditorOffset,
            () => _verticalOffset,
            () => MaxVerticalOffset);
        ScrollViewer.SetAllowAutoHide(_scroller, false);
        HoverScrollbarBehavior.SetReserveBottom(_scroller, ScrollbarReserve);
        _scroller.ScrollChanged += OnScrollerScrollChanged;
        _scroller.SizeChanged += OnScrollerSizeChanged;
        SizeChanged += OnEditorSizeChanged;

        _verticalScrollbar = new ScrollBar
        {
            Orientation = Orientation.Vertical,
            Width = ScrollbarReserve,
            MinWidth = ScrollbarReserve,
            Minimum = 0,
            SmallChange = EffectiveEditorLineHeight,
            LargeChange = EffectiveEditorLineHeight * 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Stretch,
            Margin = new Thickness(0, 0, 0, ScrollbarReserve),
            IsVisible = false,
            IsHitTestVisible = false
        };
        _verticalScrollbar.Classes.Add(HoverScrollbarBehavior.ExpandedClass);
        _scroller.GetObservable(ScrollViewer.ExtentProperty).Subscribe(new ValueObserver<Size>(_ => RefreshViewportState()));
        _scroller.GetObservable(ScrollViewer.ViewportProperty).Subscribe(new ValueObserver<Size>(_ => RefreshViewportState()));
        _verticalScrollbar.GetObservable(RangeBase.ValueProperty).Subscribe(new ValueObserver<double>(OnVerticalScrollbarValueChanged));

        _scrollCornerCover = new Border
        {
            Width = ScrollbarReserve,
            Height = ScrollbarReserve,
            Background = Palette.Background,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            IsHitTestVisible = false
        };

        _findBox = new TextBox
        {
            Width = 220,
            Watermark = "Find",
            Classes = { "graph-search-input" }
        };
        _findCount = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Palette.LineNumber,
            FontSize = 10,
            Margin = new Thickness(8, 0, 0, 0)
        };
        _findPanel = new Border
        {
            IsVisible = false,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 18),
            Padding = new Thickness(8, 6),
            CornerRadius = new CornerRadius(5),
            BorderThickness = new Thickness(1),
            Background = Palette.GutterBackground,
            BorderBrush = Palette.GutterRule,
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children = { _findBox, _findCount }
            }
        };
        _findBox.TextChanged += (_, _) => UpdateFind();
        _findBox.KeyDown += OnFindBoxKeyDown;
        AddHandler(PointerPressedEvent, OnEditorPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerWheelChangedEvent, OnEditorPointerWheelChanged, RoutingStrategies.Tunnel, handledEventsToo: true);

        _root = new Grid { Background = Palette.Background };
        _root.Children.Add(_scroller);
        _root.Children.Add(_scrollCornerCover);
        _root.Children.Add(_minimap);
        _root.Children.Add(_verticalScrollbar);
        _root.Children.Add(_findPanel);
        AddHandler(KeyDownEvent, OnEditorKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
        Content = _root;

        ApplyEditorFontSize();
        ApplyDocument();
        ApplyEditorVisualSettings();
        ApplyChrome();
        ApplyTheme();
        ApplySkin();
        QueueGrammarPrewarm();
    }

    private static void QueueGrammarPrewarm()
    {
        if (Interlocked.Exchange(ref _grammarPrewarmQueued, 1) == 1)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            foreach (var extension in CommonGrammarExtensions)
            {
                _ = GetGrammarForExtension(extension);
            }
        }, DispatcherPriority.Background);
    }

}
