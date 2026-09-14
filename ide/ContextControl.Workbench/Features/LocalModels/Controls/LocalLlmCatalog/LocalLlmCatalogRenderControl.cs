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

public enum LocalLlmCatalogHitKind
{
    None,
    Metrics,
    Icon,
    Pull
}

public readonly record struct LocalLlmCatalogHitTestResult(
    LocalLlmModelViewModel? Model,
    LocalLlmCatalogHitKind Kind,
    int Index);

public sealed partial class LocalLlmCatalogRenderControl : Control
{
    public static readonly StyledProperty<IReadOnlyList<LocalLlmModelViewModel>?> ItemsProperty =
        AvaloniaProperty.Register<LocalLlmCatalogRenderControl, IReadOnlyList<LocalLlmModelViewModel>?>(nameof(Items));

    public static readonly StyledProperty<ICommand?> PullModelCommandProperty =
        AvaloniaProperty.Register<LocalLlmCatalogRenderControl, ICommand?>(nameof(PullModelCommand));

    public static readonly StyledProperty<string> ThemeKeyProperty =
        AvaloniaProperty.Register<LocalLlmCatalogRenderControl, string>(nameof(ThemeKey), "empty");

    public static readonly StyledProperty<string> UiFontFamilyProperty =
        AvaloniaProperty.Register<LocalLlmCatalogRenderControl, string>(nameof(UiFontFamily), "fonts:Inter, Segoe UI");

    public static readonly StyledProperty<string> CodeFontFamilyProperty =
        AvaloniaProperty.Register<LocalLlmCatalogRenderControl, string>(nameof(CodeFontFamily), "avares://ContextControl.Workbench/Assets/Fonts#Cascadia Code, Consolas");

    public static readonly StyledProperty<bool> HasHuggingFaceTokenProperty =
        AvaloniaProperty.Register<LocalLlmCatalogRenderControl, bool>(nameof(HasHuggingFaceToken));

    public static readonly StyledProperty<bool> ShowLocationProperty =
        AvaloniaProperty.Register<LocalLlmCatalogRenderControl, bool>(nameof(ShowLocation));

    private const double ContentTop = 2.0;
    private const double ContentBottom = 6.0;
    private const double HorizontalInset = 3.0;
    private const double CardHeight = 78.0;
    private const double RowPitch = 82.0;
    private const double CardPaddingX = 6.0;
    private const double CardPaddingY = 4.0;
    private const double IconSize = 28.0;
    private const double IconImageSize = 20.0;
    private const double RightColumnMinWidth = 132.0;
    private const double RightColumnMaxWidth = 186.0;
    private const double ButtonHeight = 17.0;
    private const double PillHeight = 15.0;
    private const double ChipGap = 2.0;
    private const double SummaryTagsY = 51.0;
    private const double SummaryTagHeight = 13.0;
    private const double TextCenterNudgeY = 1.2;
    private const double ViewportOverscan = RowPitch * 2.0;
    private const int MaxTextCacheEntries = 8192;

    private static readonly FontFamily DefaultCodeFontFamily = new("Consolas");
    private static readonly FontFamily DefaultUiFontFamily = new("Segoe UI");
    private static readonly Cursor HandCursor = new(StandardCursorType.Hand);
    private static readonly Cursor ArrowCursor = new(StandardCursorType.Arrow);
    private static readonly IBrush TransparentBrush = Brushes.Transparent;
    private static readonly IBrush EditorSurfaceFallbackBrush = Brush.Parse("#F7F3EA");
    private static readonly IBrush PanelBorderFallbackBrush = Brush.Parse("#D8D3C7");
    private static readonly IBrush CommandBackgroundFallbackBrush = Brush.Parse("#EEE9DE");
    private static readonly IBrush CommandBorderFallbackBrush = Brush.Parse("#C9D0D2");
    private static readonly IBrush CommandPrimaryBackgroundFallbackBrush = Brush.Parse("#DDF1E7");
    private static readonly IBrush HistoryHoverFallbackBrush = Brush.Parse("#E8ECE8");
    private static readonly IBrush HistoryActiveFallbackBrush = Brush.Parse("#DDE7E6");
    private static readonly IBrush AccentFallbackBrush = Brush.Parse("#227A58");
    private static readonly IBrush AccentBorderFallbackBrush = Brush.Parse("#79BDA0");
    private static readonly IBrush FitGoodFallbackBrush = Brush.Parse("#1F8F5F");
    private static readonly IBrush FitRiskFallbackBrush = Brush.Parse("#C6861A");
    private static readonly IBrush FitBadFallbackBrush = Brush.Parse("#B84242");
    private static readonly IBrush TextPrimaryFallbackBrush = Brush.Parse("#26383D");
    private static readonly IBrush TextMutedFallbackBrush = Brush.Parse("#6F7F85");

    private readonly List<INotifyPropertyChanged> _subscribedModels = [];
    private readonly Dictionary<TextCacheKey, FormattedText> _textCache = new();
    private INotifyCollectionChanged? _itemsCollectionChanged;
    private ScrollViewer? _scrollViewer;
    private IDisposable? _offsetSubscription;
    private IDisposable? _viewportSubscription;
    private ICommand? _subscribedCommand;
    private int _hoveredIndex = -1;
    private LocalLlmCatalogHitKind _hoveredKind = LocalLlmCatalogHitKind.None;

    public IReadOnlyList<LocalLlmModelViewModel>? Items
    {
        get => GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    public ICommand? PullModelCommand
    {
        get => GetValue(PullModelCommandProperty);
        set => SetValue(PullModelCommandProperty, value);
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

    public bool HasHuggingFaceToken
    {
        get => GetValue(HasHuggingFaceTokenProperty);
        set => SetValue(HasHuggingFaceTokenProperty, value);
    }

    public bool ShowLocation
    {
        get => GetValue(ShowLocationProperty);
        set => SetValue(ShowLocationProperty, value);
    }

    public LocalLlmCatalogRenderControl()
    {
        Focusable = false;
        ToolTip.SetPlacement(this, PlacementMode.Pointer);
        ToolTip.SetShowDelay(this, 180);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        AttachToScrollViewer(this.GetVisualAncestors().OfType<ScrollViewer>().FirstOrDefault());
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        AttachToScrollViewer(null);
        AttachCommand(null);
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ItemsProperty)
        {
            AttachItems();
        }
        else if (change.Property == PullModelCommandProperty)
        {
            AttachCommand(PullModelCommand);
        }
        else if (change.Property == ThemeKeyProperty)
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
        else if (change.Property == HasHuggingFaceTokenProperty
            || change.Property == ShowLocationProperty)
        {
            InvalidateVisual();
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsFinite(availableSize.Width) ? availableSize.Width : 1.0;
        return new Size(Math.Max(1.0, width), TotalHeight);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var hit = HitTestModel(e.GetPosition(this));
        var kind = hit.Kind == LocalLlmCatalogHitKind.Pull && CanRunModelAction(hit.Model)
            ? LocalLlmCatalogHitKind.Pull
            : hit.Kind == LocalLlmCatalogHitKind.Icon
                ? LocalLlmCatalogHitKind.Icon
            : hit.Kind == LocalLlmCatalogHitKind.Metrics
                ? LocalLlmCatalogHitKind.Metrics
                : LocalLlmCatalogHitKind.None;
        SetHoveredHit(hit.Index, kind);
        ToolTip.SetTip(this, hit.Model is not null
            ? kind == LocalLlmCatalogHitKind.Icon
                ? BuildIconPreviewToolTip(hit.Model)
                : kind == LocalLlmCatalogHitKind.Metrics
                    ? BuildModelDetailToolTip(hit.Model)
                    : null
            : null);
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        SetHoveredHit(-1, LocalLlmCatalogHitKind.None);
        ToolTip.SetTip(this, null);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var hit = HitTestModel(e.GetPosition(this));
        if (hit.Kind != LocalLlmCatalogHitKind.Pull || !CanRunModelAction(hit.Model))
        {
            return;
        }

        PullModelCommand?.Execute(hit.Model);
        e.Handled = true;
    }

}
