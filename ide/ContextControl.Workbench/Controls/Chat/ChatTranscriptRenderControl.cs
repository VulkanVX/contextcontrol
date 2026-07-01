// CC-DESC: Draws the shared chat/image-generation transcript as one cached virtualized surface.

using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ContextControl.Workbench.ViewModels;

namespace ContextControl.Workbench.Controls;

public enum ChatTranscriptHitKind
{
    None,
    OpenAttachment,
    OpenImagePreview,
    DownloadAttachment,
    CopyMessage,
    CreateProject,
    ToggleSnippet,
    CopySnippet,
    SaveSnippet,
    UseSnippet,
    PreviewSnippet,
    ApplyEffectivePatch,
    ApplyAllPatch,
    OpenPatchPlanFile,
    ToggleThinking,
    ToggleDiagnostic,
    ToggleMessageCollapse
}

public sealed partial class ChatTranscriptRenderControl : Control
{
    public static readonly StyledProperty<IReadOnlyList<LocalLlmChatMessageViewModel>?> ItemsProperty =
        AvaloniaProperty.Register<ChatTranscriptRenderControl, IReadOnlyList<LocalLlmChatMessageViewModel>?>(nameof(Items));

    public static readonly StyledProperty<ICommand?> OpenAttachmentCommandProperty =
        AvaloniaProperty.Register<ChatTranscriptRenderControl, ICommand?>(nameof(OpenAttachmentCommand));

    public static readonly StyledProperty<ICommand?> CopyChatTextCommandProperty =
        AvaloniaProperty.Register<ChatTranscriptRenderControl, ICommand?>(nameof(CopyChatTextCommand));

    public static readonly StyledProperty<ICommand?> CreateProjectFromMessageCommandProperty =
        AvaloniaProperty.Register<ChatTranscriptRenderControl, ICommand?>(nameof(CreateProjectFromMessageCommand));

    public static readonly StyledProperty<ICommand?> ToggleSnippetCommandProperty =
        AvaloniaProperty.Register<ChatTranscriptRenderControl, ICommand?>(nameof(ToggleSnippetCommand));

    public static readonly StyledProperty<ICommand?> CopySnippetCommandProperty =
        AvaloniaProperty.Register<ChatTranscriptRenderControl, ICommand?>(nameof(CopySnippetCommand));

    public static readonly StyledProperty<ICommand?> SaveSnippetAsCommandProperty =
        AvaloniaProperty.Register<ChatTranscriptRenderControl, ICommand?>(nameof(SaveSnippetAsCommand));

    public static readonly StyledProperty<ICommand?> UseSnippetForCcCommandProperty =
        AvaloniaProperty.Register<ChatTranscriptRenderControl, ICommand?>(nameof(UseSnippetForCcCommand));

    public static readonly StyledProperty<ICommand?> PreviewSnippetCommandProperty =
        AvaloniaProperty.Register<ChatTranscriptRenderControl, ICommand?>(nameof(PreviewSnippetCommand));

    public static readonly StyledProperty<ICommand?> ApplyPatchCommandProperty =
        AvaloniaProperty.Register<ChatTranscriptRenderControl, ICommand?>(nameof(ApplyPatchCommand));

    public static readonly StyledProperty<ICommand?> ApplyAllPatchCommandProperty =
        AvaloniaProperty.Register<ChatTranscriptRenderControl, ICommand?>(nameof(ApplyAllPatchCommand));

    public static readonly StyledProperty<ICommand?> OpenPatchPlanFileCommandProperty =
        AvaloniaProperty.Register<ChatTranscriptRenderControl, ICommand?>(nameof(OpenPatchPlanFileCommand));

    public static readonly StyledProperty<ICommand?> ToggleThinkingCommandProperty =
        AvaloniaProperty.Register<ChatTranscriptRenderControl, ICommand?>(nameof(ToggleThinkingCommand));

    public static readonly StyledProperty<ICommand?> ToggleDiagnosticCommandProperty =
        AvaloniaProperty.Register<ChatTranscriptRenderControl, ICommand?>(nameof(ToggleDiagnosticCommand));

    public static readonly StyledProperty<string> ThemeKeyProperty =
        AvaloniaProperty.Register<ChatTranscriptRenderControl, string>(nameof(ThemeKey), "empty");

    public static readonly StyledProperty<string> UiFontFamilyProperty =
        AvaloniaProperty.Register<ChatTranscriptRenderControl, string>(nameof(UiFontFamily), "fonts:Inter, Segoe UI");

    public static readonly StyledProperty<string> CodeFontFamilyProperty =
        AvaloniaProperty.Register<ChatTranscriptRenderControl, string>(nameof(CodeFontFamily), "avares://ContextControl.Workbench/Assets/Fonts#Cascadia Code, Consolas");

    public static readonly StyledProperty<double> ChatFontSizeProperty =
        AvaloniaProperty.Register<ChatTranscriptRenderControl, double>(nameof(ChatFontSize), 10.0);

    private const double ContentTop = 2.0;
    private const double ContentBottom = 8.0;
    private const double HorizontalInset = 3.0;
    private const double ConversationColumnMaxWidth = 860.0;
    private const double ToolCallCollapsedMinWidth = 220.0;
    private const double ToolCallCollapsedMaxWidth = 430.0;
    private const double MessageGap = 6.0;
    private const double CardPaddingX = 7.0;
    private const double CardPaddingY = 4.0;
    private const double HeaderHeight = 18.0;
    private const double ButtonHeight = 18.0;
    private const double AttachmentHeight = 18.0;
    private const double AttachmentGap = 5.0;
    private const double ImagePreviewMaxHeight = 320.0;
    private const double ImagePreviewFallbackHeight = 220.0;
    private const double ImagePreviewInset = 4.0;
    private const double SnippetHeaderHeight = 17.0;
    private const double SnippetButtonHeight = 15.0;
    private const double SnippetBodyGap = 2.0;
    private const double SnippetPadding = 4.0;
    private const double PatchPlanRowHeight = 18.0;
    private const double ViewportOverscan = 96.0;
    private const double EstimatedMessageHeight = 96.0;
    private const int MaxTextCacheEntries = 8192;
    private const int MaxWrapCacheEntries = 768;

    private static readonly FontFamily DefaultCodeFontFamily = new("Consolas");
    private static readonly FontFamily DefaultUiFontFamily = new("Segoe UI");
    private static readonly Cursor HandCursor = new(StandardCursorType.Hand);
    private static readonly Cursor ArrowCursor = new(StandardCursorType.Arrow);
    private static readonly IBrush TransparentBrush = Brushes.Transparent;
    private static readonly IBrush PanelBorderFallbackBrush = Brush.Parse("#D8D3C7");
    private static readonly IBrush CommandBackgroundFallbackBrush = Brush.Parse("#EEE9DE");
    private static readonly IBrush CommandBorderFallbackBrush = Brush.Parse("#C9D0D2");
    private static readonly IBrush CommandPrimaryBackgroundFallbackBrush = Brush.Parse("#DDF1E7");
    private static readonly IBrush HistoryHoverFallbackBrush = Brush.Parse("#E8ECE8");
    private static readonly IBrush HistoryActiveFallbackBrush = Brush.Parse("#DDE7E6");
    private static readonly IBrush AccentFallbackBrush = Brush.Parse("#227A58");
    private static readonly IBrush AccentBorderFallbackBrush = Brush.Parse("#79BDA0");
    private static readonly IBrush EditorSurfaceFallbackBrush = Brush.Parse("#F7F3EA");
    private static readonly IBrush TextPrimaryFallbackBrush = Brush.Parse("#26383D");
    private static readonly IBrush TextMutedFallbackBrush = Brush.Parse("#6F7F85");
    private static readonly IBrush TextSelectionFallbackBrush = new SolidColorBrush(Color.FromArgb(86, 34, 122, 88));
    private static readonly IBrush PatchAddFallbackBrush = Brush.Parse("#2E7D4F");
    private static readonly IBrush PatchRemoveFallbackBrush = Brush.Parse("#B33A3A");
    private static readonly IBrush PatchLocFallbackBrush = Brush.Parse("#B56B20");
    private static readonly IBrush PatchVersionFallbackBrush = Brush.Parse("#7B858B");

    private readonly Dictionary<INotifyPropertyChanged, int> _subscribedItems = [];
    private readonly Dictionary<TextCacheKey, FormattedText> _textCache = new();
    private readonly Dictionary<WrapCacheKey, IReadOnlyList<string>> _wrapCache = new();
    private readonly Dictionary<string, ImageCacheEntry> _imageCache = [];
    private readonly Dictionary<LocalLlmChatPartViewModel, string> _normalizedPartTextCache = [];
    private readonly Dictionary<LocalLlmChatMessageViewModel, string> _normalizedThinkingTextCache = [];
    private readonly Dictionary<LocalLlmChatMessageViewModel, string> _normalizedDiagnosticTextCache = [];
    private readonly Dictionary<LocalLlmChatMessageViewModel, double> _prewarmedMessageWidths = [];
    private readonly Queue<LocalLlmChatMessageViewModel> _prewarmQueue = [];
    private readonly HashSet<LocalLlmChatMessageViewModel> _queuedPrewarmMessages = [];
    private readonly Dictionary<SnippetLineCacheKey, IReadOnlyList<string>> _snippetLineCache = [];
    private readonly Dictionary<LocalLlmChatMessageViewModel, double> _thinkingScrollOffsets = [];
    private readonly Dictionary<LocalLlmChatMessageViewModel, double> _diagnosticScrollOffsets = [];
    private readonly HashSet<LocalLlmChatMessageViewModel> _collapsedMessages = [];
    private readonly HashSet<LocalLlmChatMessageViewModel> _collapseStateInitialized = [];
    private readonly Dictionary<LocalLlmChatMessageViewModel, CollapseAnimation> _collapseAnimations = [];
    private readonly Dictionary<ChatSnippetViewModel, CollapseAnimation> _snippetAnimations = [];
    private readonly Dictionary<LocalLlmChatMessageViewModel, CollapseAnimation> _thinkingAnimations = [];
    private readonly Dictionary<LocalLlmChatMessageViewModel, CollapseAnimation> _diagnosticAnimations = [];
    private readonly HashSet<int> _animationRows = [];
    private readonly List<LocalLlmChatMessageViewModel> _completedMessageAnimations = [];
    private readonly List<ChatSnippetViewModel> _completedSnippetAnimations = [];
    private readonly DispatcherTimer _collapseAnimationTimer;
    private readonly Stopwatch _collapseAnimationClock = new();
    private readonly HeightDeltaIndex _heightDeltas = new();
    private readonly Dictionary<int, MessageLayout> _layoutCache = [];
    private readonly Dictionary<int, double> _rowHeights = [];
    private int _itemCount;
    private double _layoutWidth = -1.0;
    private INotifyCollectionChanged? _itemsCollectionChanged;
    private ScrollViewer? _scrollViewer;
    private IDisposable? _offsetSubscription;
    private IDisposable? _viewportSubscription;
    private HitRegion? _hoveredHit;
    private TextPosition? _selectionAnchor;
    private TextPosition? _selectionActive;
    private bool _isSelectingText;
    private ExtensionScrollDrag? _extensionScrollDrag;
    private bool _measureInvalidationQueued;
    private bool _prewarmQueued;

    public IReadOnlyList<LocalLlmChatMessageViewModel>? Items
    {
        get => GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    public ICommand? OpenAttachmentCommand
    {
        get => GetValue(OpenAttachmentCommandProperty);
        set => SetValue(OpenAttachmentCommandProperty, value);
    }

    public ICommand? CopyChatTextCommand
    {
        get => GetValue(CopyChatTextCommandProperty);
        set => SetValue(CopyChatTextCommandProperty, value);
    }

    public ICommand? CreateProjectFromMessageCommand
    {
        get => GetValue(CreateProjectFromMessageCommandProperty);
        set => SetValue(CreateProjectFromMessageCommandProperty, value);
    }

    public ICommand? ToggleSnippetCommand
    {
        get => GetValue(ToggleSnippetCommandProperty);
        set => SetValue(ToggleSnippetCommandProperty, value);
    }

    public ICommand? CopySnippetCommand
    {
        get => GetValue(CopySnippetCommandProperty);
        set => SetValue(CopySnippetCommandProperty, value);
    }

    public ICommand? SaveSnippetAsCommand
    {
        get => GetValue(SaveSnippetAsCommandProperty);
        set => SetValue(SaveSnippetAsCommandProperty, value);
    }

    public ICommand? UseSnippetForCcCommand
    {
        get => GetValue(UseSnippetForCcCommandProperty);
        set => SetValue(UseSnippetForCcCommandProperty, value);
    }

    public ICommand? PreviewSnippetCommand
    {
        get => GetValue(PreviewSnippetCommandProperty);
        set => SetValue(PreviewSnippetCommandProperty, value);
    }

    public ICommand? ApplyPatchCommand
    {
        get => GetValue(ApplyPatchCommandProperty);
        set => SetValue(ApplyPatchCommandProperty, value);
    }

    public ICommand? ApplyAllPatchCommand
    {
        get => GetValue(ApplyAllPatchCommandProperty);
        set => SetValue(ApplyAllPatchCommandProperty, value);
    }

    public ICommand? OpenPatchPlanFileCommand
    {
        get => GetValue(OpenPatchPlanFileCommandProperty);
        set => SetValue(OpenPatchPlanFileCommandProperty, value);
    }

    public ICommand? ToggleThinkingCommand
    {
        get => GetValue(ToggleThinkingCommandProperty);
        set => SetValue(ToggleThinkingCommandProperty, value);
    }

    public ICommand? ToggleDiagnosticCommand
    {
        get => GetValue(ToggleDiagnosticCommandProperty);
        set => SetValue(ToggleDiagnosticCommandProperty, value);
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

    public double ChatFontSize
    {
        get => GetValue(ChatFontSizeProperty);
        set => SetValue(ChatFontSizeProperty, value);
    }

    private double ChatTextFontSize => Math.Clamp(ChatFontSize + 0.2, 8.0, 22.0);
    private double ChatTextLineHeight => Math.Max(12.0, ChatTextFontSize * 1.42);
    private double ChatCodeFontSize => Math.Max(8.0, ChatTextFontSize + 0.5);
    private double ChatCodeLineHeight => Math.Max(12.0, ChatCodeFontSize * 1.43);
    private double ChatMetaFontSize => Math.Max(7.0, ChatTextFontSize - 1.0);

    private static bool IsFlatTranscriptMessage(LocalLlmChatMessageViewModel message)
    {
        return !message.IsUser;
    }

    public ChatTranscriptRenderControl()
    {
        Focusable = true;
        _collapseAnimationClock.Start();
        _collapseAnimationTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _collapseAnimationTimer.Tick += (_, _) => TickChatAnimations();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        AttachItems();
        ResetLayoutCache(ResolveLayoutWidth(Bounds.Width), Items?.Count ?? 0);
        InvalidateTranscript();
        AttachToScrollViewer(this.GetVisualAncestors().OfType<ScrollViewer>().FirstOrDefault());
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        AttachToScrollViewer(null);
        DetachItems();
        ClearImageCache();
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ItemsProperty)
        {
            AttachItems();
            ResetLayoutCache(ResolveLayoutWidth(Bounds.Width), Items?.Count ?? 0);
            InvalidateTranscript();
        }
        else if (change.Property == ThemeKeyProperty
            || change.Property == UiFontFamilyProperty
            || change.Property == CodeFontFamilyProperty
            || change.Property == ChatFontSizeProperty)
        {
            _textCache.Clear();
            _wrapCache.Clear();
            _snippetLineCache.Clear();
            _prewarmedMessageWidths.Clear();
            _prewarmQueue.Clear();
            _queuedPrewarmMessages.Clear();
            _prewarmQueued = false;
            ResetLayoutCache(ResolveLayoutWidth(Bounds.Width), Items?.Count ?? 0);
            InvalidateTranscript();
        }
        else if (change.Property == OpenAttachmentCommandProperty
            || change.Property == CopyChatTextCommandProperty
            || change.Property == CreateProjectFromMessageCommandProperty
            || change.Property == ToggleSnippetCommandProperty
            || change.Property == CopySnippetCommandProperty
            || change.Property == SaveSnippetAsCommandProperty
            || change.Property == UseSnippetForCcCommandProperty
            || change.Property == PreviewSnippetCommandProperty
            || change.Property == OpenPatchPlanFileCommandProperty
            || change.Property == ToggleThinkingCommandProperty
            || change.Property == ToggleDiagnosticCommandProperty)
        {
            InvalidateVisual();
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled)
        {
            return;
        }

        if (e.Key == Key.Escape && HasTextSelection)
        {
            ClearTextSelection();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.C
            && e.KeyModifiers.HasFlag(KeyModifiers.Control)
            && HasTextSelection)
        {
            _ = CopySelectedTextAsync();
            e.Handled = true;
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = ResolveLayoutWidth(availableSize.Width);
        EnsureLayoutCache(width);
        return new Size(Math.Max(1.0, width), GetTotalHeight());
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
            DrawEmptyState(context, viewportTop, viewportHeight);
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
            if (rowBottom < startY)
            {
                rowTop = rowBottom;
                continue;
            }

            DrawMessage(context, layout, index, rowTop, viewportTop, viewportTop + viewportHeight);
            rowTop = rowBottom;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var point = e.GetPosition(this);
        if (_extensionScrollDrag is not null)
        {
            UpdateExtensionScrollbarDrag(point.Y);
            Cursor = HandCursor;
            e.Handled = true;
            return;
        }

        if (_isSelectingText)
        {
            if (TryGetTextPosition(point, out var position))
            {
                _selectionActive = position;
                InvalidateVisual();
            }

            e.Handled = true;
            return;
        }

        var hit = HitTest(point);
        SetHoveredHit(hit);
        if (hit is null && TryResolveExtensionScrollbar(point, out _))
        {
            Cursor = HandCursor;
            return;
        }

        if (hit is null && TryGetTextPosition(point, out _))
        {
            Cursor = new Cursor(StandardCursorType.Ibeam);
        }
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (_isSelectingText)
        {
            return;
        }

        SetHoveredHit(null);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        Focus();
        var point = e.GetPosition(this);
        if (TryResolveExtensionScrollbar(point, out var scrollbar))
        {
            ClearTextSelection();
            BeginExtensionScrollbarDrag(scrollbar, point.Y);
            e.Pointer.Capture(this);
            Cursor = HandCursor;
            e.Handled = true;
            return;
        }

        var hit = HitTest(point);
        if (hit is not null)
        {
            ExecuteHit(hit);
            e.Handled = true;
            return;
        }

        if (TryGetTextPosition(point, out var position))
        {
            _selectionAnchor = position;
            _selectionActive = position;
            _isSelectingText = true;
            e.Pointer.Capture(this);
            Cursor = new Cursor(StandardCursorType.Ibeam);
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        ClearTextSelection();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_extensionScrollDrag is not null)
        {
            _extensionScrollDrag = null;
            e.Pointer.Capture(null);
            e.Handled = true;
            return;
        }

        if (_isSelectingText)
        {
            _isSelectingText = false;
            e.Pointer.Capture(null);
            e.Handled = true;
        }
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

        foreach (var item in _subscribedItems.Keys)
        {
            item.PropertyChanged -= OnItemPropertyChanged;
        }

        _subscribedItems.Clear();
        _thinkingScrollOffsets.Clear();
        _diagnosticScrollOffsets.Clear();
        _normalizedPartTextCache.Clear();
        _normalizedThinkingTextCache.Clear();
        _normalizedDiagnosticTextCache.Clear();
        _prewarmedMessageWidths.Clear();
        _prewarmQueue.Clear();
        _queuedPrewarmMessages.Clear();
        _prewarmQueued = false;
        _snippetLineCache.Clear();
        _collapsedMessages.Clear();
        _collapseStateInitialized.Clear();
        _collapseAnimations.Clear();
        _snippetAnimations.Clear();
        _thinkingAnimations.Clear();
        _diagnosticAnimations.Clear();
        _animationRows.Clear();
        _completedMessageAnimations.Clear();
        _completedSnippetAnimations.Clear();
        _collapseAnimationTimer.Stop();
    }

    private void SubscribeMessage(LocalLlmChatMessageViewModel message, int rowIndex)
    {
        Subscribe(message, rowIndex);
        foreach (var snippet in message.Snippets)
        {
            Subscribe(snippet, rowIndex);
        }

        QueueMessagePrewarm(message);
    }

    private void Subscribe(INotifyPropertyChanged item, int rowIndex)
    {
        if (_subscribedItems.ContainsKey(item))
        {
            _subscribedItems[item] = rowIndex;
            return;
        }

        item.PropertyChanged += OnItemPropertyChanged;
        _subscribedItems[item] = rowIndex;
    }

    private void OnItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        var newCount = Items?.Count ?? 0;
        if (e.Action == NotifyCollectionChangedAction.Add
            && e.NewItems is not null
            && e.NewStartingIndex == _itemCount)
        {
            foreach (var item in e.NewItems.OfType<LocalLlmChatMessageViewModel>())
            {
                EnsureCollapseStateInitialized(item);
                QueueMessagePrewarm(item);
            }

            ResizeLayoutCache(newCount);
            InvalidateTranscript();
            return;
        }

        DetachItems();
        if (Items is INotifyCollectionChanged collectionChanged)
        {
            _itemsCollectionChanged = collectionChanged;
            collectionChanged.CollectionChanged += OnItemsCollectionChanged;
        }

        ResetLayoutCache(ResolveLayoutWidth(Bounds.Width), newCount);
        InvalidateTranscript();
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        var keepMeasuredHeight = false;
        if (sender is LocalLlmChatMessageViewModel message && _subscribedItems.TryGetValue(message, out var messageIndex))
        {
            EnsureCollapseStateInitialized(message);
            var isThinkingExpansionChange = e.PropertyName == nameof(LocalLlmChatMessageViewModel.IsThinkingExpanded);
            var isDiagnosticExpansionChange = e.PropertyName == nameof(LocalLlmChatMessageViewModel.IsDiagnosticExpanded);
            if (isThinkingExpansionChange)
            {
                keepMeasuredHeight = true;
                if (!_thinkingAnimations.ContainsKey(message))
                {
                    BeginThinkingAnimation(message, message.IsThinkingExpanded, message.IsThinkingExpanded ? 0.0 : 1.0);
                }
            }
            else if (isDiagnosticExpansionChange)
            {
                keepMeasuredHeight = true;
                if (!_diagnosticAnimations.ContainsKey(message))
                {
                    BeginDiagnosticAnimation(message, message.IsDiagnosticExpanded, message.IsDiagnosticExpanded ? 0.0 : 1.0);
                }
            }

            if (e.PropertyName is nameof(LocalLlmChatMessageViewModel.RawText)
                or nameof(LocalLlmChatMessageViewModel.Text)
                or nameof(LocalLlmChatMessageViewModel.VisibleText)
                or nameof(LocalLlmChatMessageViewModel.Parts)
                or nameof(LocalLlmChatMessageViewModel.Snippets))
            {
                foreach (var snippet in message.Snippets)
                {
                    Subscribe(snippet, messageIndex);
                }
            }

            if (e.PropertyName is nameof(LocalLlmChatMessageViewModel.RawText)
                or nameof(LocalLlmChatMessageViewModel.Text)
                or nameof(LocalLlmChatMessageViewModel.VisibleText)
                or nameof(LocalLlmChatMessageViewModel.Parts)
                or nameof(LocalLlmChatMessageViewModel.ThinkingText)
                or nameof(LocalLlmChatMessageViewModel.DiagnosticPrompt))
            {
                _prewarmedMessageWidths.Remove(message);
            }

            if (e.PropertyName is nameof(LocalLlmChatMessageViewModel.RawText)
                or nameof(LocalLlmChatMessageViewModel.Text)
                or nameof(LocalLlmChatMessageViewModel.VisibleText)
                or nameof(LocalLlmChatMessageViewModel.Parts))
            {
                _normalizedPartTextCache.Clear();
                _snippetLineCache.Clear();
            }

            if (e.PropertyName == nameof(LocalLlmChatMessageViewModel.ThinkingText))
            {
                _normalizedThinkingTextCache.Remove(message);
            }

            if (e.PropertyName == nameof(LocalLlmChatMessageViewModel.DiagnosticPrompt))
            {
                _normalizedDiagnosticTextCache.Remove(message);
            }

            if (!isThinkingExpansionChange && !isDiagnosticExpansionChange)
            {
                QueueMessagePrewarm(message);
            }
        }
        else if (sender is ChatSnippetViewModel snippet
            && e.PropertyName is nameof(ChatSnippetViewModel.IsExpanded)
                or nameof(ChatSnippetViewModel.IsCollapsed)
                or nameof(ChatSnippetViewModel.ToggleLabel)
                or nameof(ChatSnippetViewModel.DisplayHeight)
                or nameof(ChatSnippetViewModel.DisplayText))
        {
            keepMeasuredHeight = true;
            if (e.PropertyName == nameof(ChatSnippetViewModel.IsExpanded)
                && !_snippetAnimations.ContainsKey(snippet))
            {
                BeginSnippetAnimation(snippet, snippet.IsExpanded, snippet.IsExpanded ? 0.0 : 1.0);
            }
        }

        if (sender is INotifyPropertyChanged item && _subscribedItems.TryGetValue(item, out var rowIndex))
        {
            var anchor = CaptureViewportAnchor();
            if (keepMeasuredHeight)
            {
                InvalidateRows([rowIndex], anchor);
            }
            else
            {
                InvalidateRow(rowIndex, anchor);
            }

            return;
        }

        ResetLayoutCache(ResolveLayoutWidth(Bounds.Width), Items?.Count ?? 0);
        InvalidateTranscript();
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

        _offsetSubscription = scrollViewer
            .GetObservable(ScrollViewer.OffsetProperty)
            .Subscribe(new ValueObserver<Vector>(_ => InvalidateVisual()));
        _viewportSubscription = scrollViewer
            .GetObservable(ScrollViewer.ViewportProperty)
            .Subscribe(new ValueObserver<Size>(OnScrollViewerViewportChanged));
    }

    private void OnScrollViewerViewportChanged(Size viewport)
    {
        var width = double.IsFinite(viewport.Width) && viewport.Width > 0.0
            ? viewport.Width
            : ResolveLayoutWidth(Bounds.Width);
        if (Math.Abs(width - _layoutWidth) >= 0.5)
        {
            var anchor = CaptureViewportAnchorFromCurrentLayout();
            ResetLayoutCacheForViewportWidth(width, Items?.Count ?? 0);
            QueueMeasureInvalidation();
            RestoreViewportAnchorAfterWidthChange(anchor);
        }

        InvalidateVisual();
    }

    private void InvalidateTranscript()
    {
        _hoveredHit = null;
        QueueMeasureInvalidation();
        InvalidateVisual();
    }

    private void InvalidateRow(int index, ViewportAnchor? anchor = null)
    {
        if (index < 0 || index >= _itemCount)
        {
            ResetLayoutCache(ResolveLayoutWidth(Bounds.Width), Items?.Count ?? 0);
            InvalidateTranscript();
            return;
        }

        _hoveredHit = null;
        _layoutCache.Remove(index);
        if (_rowHeights.Remove(index))
        {
            _heightDeltas.SetDelta(index, 0.0);
            QueueMeasureInvalidation();
        }

        InvalidateVisual();
        RestoreViewportAnchor(anchor, [index]);
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
        _layoutCache.Clear();
        _rowHeights.Clear();
        _heightDeltas.Reset(_itemCount);
        _hoveredHit = null;
        ClearTextSelection();
    }

    private void ResetLayoutCacheForViewportWidth(double width, int count)
    {
        count = Math.Max(0, count);
        if (count != _itemCount)
        {
            ResetLayoutCache(width, count);
            return;
        }

        _layoutWidth = Math.Max(1.0, width);
        _layoutCache.Clear();
        _hoveredHit = null;
        ClearTextSelection();
    }

    private void ResizeLayoutCache(int count)
    {
        count = Math.Max(0, count);
        if (count == _itemCount)
        {
            return;
        }

        if (count < _itemCount)
        {
            ResetLayoutCache(ResolveLayoutWidth(Bounds.Width), count);
            return;
        }

        _heightDeltas.Resize(count);
        _itemCount = count;
        _hoveredHit = null;
    }

    private MessageLayout? GetOrBuildLayout(int index)
    {
        var items = Items;
        if (items is null || index < 0 || index >= _itemCount || index >= items.Count)
        {
            return null;
        }

        if (_layoutCache.TryGetValue(index, out var layout))
        {
            return layout;
        }

        SubscribeMessage(items[index], index);
        EnsureCollapseStateInitialized(items[index]);
        layout = BuildMessageLayout(items[index], _layoutWidth);
        _layoutCache[index] = layout;
        SetMeasuredHeight(index, layout.Height);
        return layout;
    }

    private void SetMeasuredHeight(int index, double height)
    {
        if (index < 0 || index >= _itemCount || !double.IsFinite(height) || height <= 0.0)
        {
            return;
        }

        var wasMeasured = _rowHeights.TryGetValue(index, out var measuredHeight);
        var previousHeight = wasMeasured ? measuredHeight : EstimatedMessageHeight;
        if (Math.Abs(previousHeight - height) < 0.5 && wasMeasured)
        {
            return;
        }

        _rowHeights[index] = height;
        _heightDeltas.SetDelta(index, height - EstimatedMessageHeight);
        if (Math.Abs(previousHeight - height) >= 0.5)
        {
            QueueMeasureInvalidation();
        }
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

    private void QueueMessagePrewarm(LocalLlmChatMessageViewModel message)
    {
        if (!ShouldPrewarmMessage(message))
        {
            return;
        }

        var contentWidth = ResolvePrewarmContentWidth(message);
        if (_prewarmedMessageWidths.TryGetValue(message, out var warmedWidth)
            && Math.Abs(warmedWidth - contentWidth) < 0.5)
        {
            return;
        }

        if (_queuedPrewarmMessages.Add(message))
        {
            _prewarmQueue.Enqueue(message);
        }

        if (_prewarmQueued)
        {
            return;
        }

        _prewarmQueued = true;
        Dispatcher.UIThread.Post(ProcessNextPrewarmMessage, DispatcherPriority.Background);
    }

    private bool ShouldPrewarmMessage(LocalLlmChatMessageViewModel message)
    {
        return message.HasThinking
            || message.HasDiagnosticPrompt
            || message.HasSnippets
            || message.IsContextControlGenerated;
    }

    private double ResolvePrewarmContentWidth(LocalLlmChatMessageViewModel message)
    {
        var width = ResolveLayoutWidth(Bounds.Width);
        var availableCardWidth = Math.Max(0.0, width - HorizontalInset * 2.0 - 2.0);
        var cardWidth = message.IsUser
            ? ResolveMessageCardWidth(message, availableCardWidth, 0.0)
            : ResolveConversationColumnWidth(availableCardWidth);
        return Math.Max(1.0, Math.Round(cardWidth - CardPaddingX * 2.0, 1));
    }

    private void ProcessNextPrewarmMessage()
    {
        _prewarmQueued = false;
        if (_prewarmQueue.Count == 0)
        {
            return;
        }

        var message = _prewarmQueue.Dequeue();
        _queuedPrewarmMessages.Remove(message);
        if (Items is null || !ShouldPrewarmMessage(message))
        {
            ScheduleRemainingPrewarm();
            return;
        }

        var contentWidth = ResolvePrewarmContentWidth(message);
        if (!_prewarmedMessageWidths.TryGetValue(message, out var warmedWidth)
            || Math.Abs(warmedWidth - contentWidth) >= 0.5)
        {
            PrewarmMessageText(message, contentWidth);
            _prewarmedMessageWidths[message] = contentWidth;
        }

        ScheduleRemainingPrewarm();
    }

    private void ScheduleRemainingPrewarm()
    {
        if (_prewarmQueue.Count == 0 || _prewarmQueued)
        {
            return;
        }

        _prewarmQueued = true;
        Dispatcher.UIThread.Post(ProcessNextPrewarmMessage, DispatcherPriority.Background);
    }

    private void PrewarmMessageText(LocalLlmChatMessageViewModel message, double contentWidth)
    {
        var uiFontFamily = ResolveFontFamily(UiFontFamily, Resource("UiFontFamily", DefaultUiFontFamily));
        var codeFontFamily = ResolveFontFamily(CodeFontFamily, Resource("CodeFontFamily", DefaultCodeFontFamily));

        foreach (var part in message.Parts)
        {
            if (!part.IsText)
            {
                if (part.Snippet is { } snippet && !snippet.IsPatchPlan)
                {
                    PrewarmLines(GetSnippetDisplayLines(snippet, expanded: false), codeFontFamily, FontWeight.Normal, FontStyle.Normal, ChatCodeFontSize);
                    PrewarmLines(GetSnippetDisplayLines(snippet, expanded: true), codeFontFamily, FontWeight.Normal, FontStyle.Normal, ChatCodeFontSize);
                }

                continue;
            }

            var text = GetNormalizedPartText(part);
            if (!string.IsNullOrWhiteSpace(text))
            {
                PrewarmLines(WrapLines(text, contentWidth, uiFontFamily, FontWeight.Normal, FontStyle.Normal, ChatTextFontSize), uiFontFamily, FontWeight.Normal, FontStyle.Normal, ChatTextFontSize);
            }
        }

        if (message.HasThinking)
        {
            PrewarmLines(
                WrapLines(GetNormalizedThinkingText(message), contentWidth - 26.0, codeFontFamily, FontWeight.Normal, FontStyle.Normal, ChatCodeFontSize),
                codeFontFamily,
                FontWeight.Normal,
                FontStyle.Normal,
                ChatCodeFontSize);
        }

        if (message.HasDiagnosticPrompt)
        {
            PrewarmLines(
                WrapLines(GetNormalizedDiagnosticText(message), contentWidth - 26.0, codeFontFamily, FontWeight.Normal, FontStyle.Normal, ChatCodeFontSize),
                codeFontFamily,
                FontWeight.Normal,
                FontStyle.Normal,
                ChatCodeFontSize);
        }
    }

    private void PrewarmLines(
        IReadOnlyList<string> lines,
        FontFamily fontFamily,
        FontWeight weight,
        FontStyle style,
        double fontSize)
    {
        var brush = Resource("TextPrimaryBrush", TextPrimaryFallbackBrush);
        var count = Math.Min(lines.Count, 160);
        for (var index = 0; index < count; index++)
        {
            _ = GetFormattedText(lines[index], brush, fontFamily, weight, style, fontSize);
        }
    }

    private void ToggleMessageCollapse(LocalLlmChatMessageViewModel message)
    {
        EnsureCollapseStateInitialized(message);
        var anchor = CaptureViewportAnchor();
        var current = GetCollapseProgress(message);
        var target = _collapsedMessages.Contains(message) ? 0.0 : 1.0;
        _collapseStateInitialized.Add(message);
        if (target >= 1.0)
        {
            _collapsedMessages.Add(message);
        }
        else
        {
            _collapsedMessages.Remove(message);
        }

        _collapseAnimations[message] = new CollapseAnimation(
            current,
            target,
            _collapseAnimationClock.Elapsed.TotalMilliseconds);
        StartAnimationTimer();
        InvalidateMessageRows([message], anchor);
    }

    private void BeginSnippetAnimation(ChatSnippetViewModel snippet, bool targetExpanded, double? startProgress = null)
    {
        var current = startProgress ?? GetSnippetExpansionProgress(snippet);
        var target = targetExpanded ? 1.0 : 0.0;
        _snippetAnimations[snippet] = new CollapseAnimation(
            current,
            target,
            _collapseAnimationClock.Elapsed.TotalMilliseconds);
        StartAnimationTimer();
    }

    private void BeginThinkingAnimation(LocalLlmChatMessageViewModel message, bool targetExpanded, double? startProgress = null)
    {
        var current = startProgress ?? GetThinkingExpansionProgress(message);
        var target = targetExpanded ? 1.0 : 0.0;
        _thinkingAnimations[message] = new CollapseAnimation(
            current,
            target,
            _collapseAnimationClock.Elapsed.TotalMilliseconds);
        StartAnimationTimer();
    }

    private void BeginDiagnosticAnimation(LocalLlmChatMessageViewModel message, bool targetExpanded, double? startProgress = null)
    {
        var current = startProgress ?? GetDiagnosticExpansionProgress(message);
        var target = targetExpanded ? 1.0 : 0.0;
        _diagnosticAnimations[message] = new CollapseAnimation(
            current,
            target,
            _collapseAnimationClock.Elapsed.TotalMilliseconds);
        StartAnimationTimer();
    }

    private void StartAnimationTimer()
    {
        _collapseAnimationTimer.Stop();
        _collapseAnimationTimer.Start();
    }

    private double GetCollapseProgress(LocalLlmChatMessageViewModel message)
    {
        if (_collapseAnimations.TryGetValue(message, out var animation))
        {
            return animation.GetProgress(_collapseAnimationClock.Elapsed.TotalMilliseconds);
        }

        return _collapsedMessages.Contains(message) ? 1.0 : 0.0;
    }

    private bool IsVisuallyCollapsed(LocalLlmChatMessageViewModel message)
    {
        return GetCollapseProgress(message) > 0.02;
    }

    private double GetSnippetExpansionProgress(ChatSnippetViewModel snippet)
    {
        if (_snippetAnimations.TryGetValue(snippet, out var animation))
        {
            return animation.GetProgress(_collapseAnimationClock.Elapsed.TotalMilliseconds);
        }

        return snippet.IsExpanded ? 1.0 : 0.0;
    }

    private double GetThinkingExpansionProgress(LocalLlmChatMessageViewModel message)
    {
        if (_thinkingAnimations.TryGetValue(message, out var animation))
        {
            return animation.GetProgress(_collapseAnimationClock.Elapsed.TotalMilliseconds);
        }

        return message.IsThinkingExpanded ? 1.0 : 0.0;
    }

    private double GetDiagnosticExpansionProgress(LocalLlmChatMessageViewModel message)
    {
        if (_diagnosticAnimations.TryGetValue(message, out var animation))
        {
            return animation.GetProgress(_collapseAnimationClock.Elapsed.TotalMilliseconds);
        }

        return message.IsDiagnosticExpanded ? 1.0 : 0.0;
    }

    private void TickChatAnimations()
    {
        if (!HasActiveAnimations)
        {
            _collapseAnimationTimer.Stop();
            return;
        }

        var anchor = CaptureViewportAnchor();
        var now = _collapseAnimationClock.Elapsed.TotalMilliseconds;
        _animationRows.Clear();

        _completedMessageAnimations.Clear();
        foreach (var (message, animation) in _collapseAnimations)
        {
            AddAnimationRow(message);
            if (animation.IsComplete(now))
            {
                _completedMessageAnimations.Add(message);
            }
        }

        foreach (var message in _completedMessageAnimations)
        {
            if (!_collapseAnimations.TryGetValue(message, out var animation))
            {
                continue;
            }

            if (animation.TargetProgress >= 1.0)
            {
                _collapsedMessages.Add(message);
            }
            else
            {
                _collapsedMessages.Remove(message);
            }

            _collapseAnimations.Remove(message);
        }

        PruneCompletedMessageAnimations(_thinkingAnimations, now);
        PruneCompletedMessageAnimations(_diagnosticAnimations, now);
        PruneCompletedSnippetAnimations(now);

        InvalidateRows(_animationRows, anchor);
        if (!HasActiveAnimations)
        {
            _collapseAnimationTimer.Stop();
        }
    }

    private bool HasActiveAnimations => _collapseAnimations.Count > 0
        || _snippetAnimations.Count > 0
        || _thinkingAnimations.Count > 0
        || _diagnosticAnimations.Count > 0;

    private void AddAnimationRow(LocalLlmChatMessageViewModel message)
    {
        if (_subscribedItems.TryGetValue(message, out var rowIndex))
        {
            _animationRows.Add(rowIndex);
        }
    }

    private void AddAnimationRow(ChatSnippetViewModel snippet)
    {
        if (_subscribedItems.TryGetValue(snippet, out var rowIndex))
        {
            _animationRows.Add(rowIndex);
        }
    }

    private void PruneCompletedMessageAnimations(
        Dictionary<LocalLlmChatMessageViewModel, CollapseAnimation> animations,
        double now)
    {
        _completedMessageAnimations.Clear();
        foreach (var (message, animation) in animations)
        {
            AddAnimationRow(message);
            if (animation.IsComplete(now))
            {
                _completedMessageAnimations.Add(message);
            }
        }

        foreach (var message in _completedMessageAnimations)
        {
            animations.Remove(message);
        }
    }

    private void PruneCompletedSnippetAnimations(double now)
    {
        _completedSnippetAnimations.Clear();
        foreach (var (snippet, animation) in _snippetAnimations)
        {
            AddAnimationRow(snippet);
            if (animation.IsComplete(now))
            {
                _completedSnippetAnimations.Add(snippet);
            }
        }

        foreach (var snippet in _completedSnippetAnimations)
        {
            _snippetAnimations.Remove(snippet);
        }
    }

    private void InvalidateMessageRows(IReadOnlyList<LocalLlmChatMessageViewModel> messages, ViewportAnchor? anchor)
    {
        InvalidateRows(RowsForMessages(messages), anchor);
    }

    private IEnumerable<int> RowsForMessages(IEnumerable<LocalLlmChatMessageViewModel> messages)
    {
        foreach (var message in messages)
        {
            if (_subscribedItems.TryGetValue(message, out var rowIndex))
            {
                yield return rowIndex;
            }
        }
    }

    private IEnumerable<int> RowsForSnippets(IEnumerable<ChatSnippetViewModel> snippets)
    {
        foreach (var snippet in snippets)
        {
            if (_subscribedItems.TryGetValue(snippet, out var rowIndex))
            {
                yield return rowIndex;
            }
        }
    }

    private void InvalidateRows(IEnumerable<int> rows, ViewportAnchor? anchor)
    {
        var changedRows = new List<int>();
        foreach (var rowIndex in rows)
        {
            if (rowIndex >= 0 && rowIndex < _itemCount)
            {
                InvalidateRowLayout(rowIndex, keepMeasuredHeight: true);
                changedRows.Add(rowIndex);
            }
        }

        if (changedRows.Count == 0)
        {
            ResetLayoutCache(ResolveLayoutWidth(Bounds.Width), Items?.Count ?? 0);
        }

        QueueMeasureInvalidation();
        InvalidateVisual();
        RestoreViewportAnchor(anchor, changedRows);
    }

    private void InvalidateRowLayout(int index, bool keepMeasuredHeight = false)
    {
        _layoutCache.Remove(index);
        if (keepMeasuredHeight)
        {
            return;
        }

        if (_rowHeights.Remove(index))
        {
            _heightDeltas.SetDelta(index, 0.0);
        }
    }

    private void EnsureCollapseStateInitialized(LocalLlmChatMessageViewModel message)
    {
        if (!_collapseStateInitialized.Add(message))
        {
            return;
        }

        if (ShouldDefaultCollapseMessage(message))
        {
            _collapsedMessages.Add(message);
        }
    }

    private static bool ShouldDefaultCollapseMessage(LocalLlmChatMessageViewModel message)
    {
        return message.IsContextControlGenerated;
    }

    private ViewportAnchor? CaptureViewportAnchor()
    {
        if (_scrollViewer is null || _itemCount == 0)
        {
            return null;
        }

        EnsureLayoutCache(Bounds.Width);
        var viewportTop = Math.Clamp(_scrollViewer.Offset.Y, 0.0, Math.Max(0.0, GetTotalHeight()));
        var rowIndex = FindRowIndexAtOrAfter(viewportTop);
        if (rowIndex < 0)
        {
            return null;
        }

        return new ViewportAnchor(rowIndex, Math.Max(0.0, viewportTop - GetRowTop(rowIndex)));
    }

    private ViewportAnchor? CaptureViewportAnchorFromCurrentLayout()
    {
        if (_scrollViewer is null || _itemCount == 0)
        {
            return null;
        }

        var viewportTop = Math.Clamp(_scrollViewer.Offset.Y, 0.0, Math.Max(0.0, GetTotalHeight()));
        var rowIndex = FindRowIndexAtOrAfter(viewportTop);
        if (rowIndex < 0)
        {
            return null;
        }

        return new ViewportAnchor(rowIndex, Math.Max(0.0, viewportTop - GetRowTop(rowIndex)));
    }

    private void RestoreViewportAnchor(ViewportAnchor? anchor, IReadOnlyList<int> changedRows)
    {
        if (anchor is not { } value || _scrollViewer is null || _itemCount == 0)
        {
            return;
        }

        var rowIndex = Math.Clamp(value.RowIndex, 0, Math.Max(0, _itemCount - 1));
        var hasChangedRowsAboveAnchor = false;
        foreach (var row in changedRows)
        {
            if (row >= 0 && row < rowIndex)
            {
                _ = GetOrBuildLayout(row);
                hasChangedRowsAboveAnchor = true;
            }
        }

        if (!hasChangedRowsAboveAnchor)
        {
            return;
        }

        var targetOffset = GetRowTop(rowIndex) + value.OffsetWithinRow;
        var maxOffset = Math.Max(0.0, GetTotalHeight() - _scrollViewer.Viewport.Height);
        if (!double.IsFinite(maxOffset))
        {
            maxOffset = 0.0;
        }

        var nextY = Math.Clamp(targetOffset, 0.0, maxOffset);
        if (Math.Abs(_scrollViewer.Offset.Y - nextY) < 0.5)
        {
            return;
        }

        _scrollViewer.Offset = new Vector(_scrollViewer.Offset.X, nextY);
    }

    private void RestoreViewportAnchorAfterWidthChange(ViewportAnchor? anchor)
    {
        if (anchor is not { } value || _scrollViewer is null || _itemCount == 0)
        {
            return;
        }

        var rowIndex = Math.Clamp(value.RowIndex, 0, Math.Max(0, _itemCount - 1));
        var targetOffset = GetRowTop(rowIndex) + value.OffsetWithinRow;
        var maxOffset = Math.Max(0.0, GetTotalHeight() - _scrollViewer.Viewport.Height);
        if (!double.IsFinite(maxOffset))
        {
            maxOffset = 0.0;
        }

        var nextY = Math.Clamp(targetOffset, 0.0, maxOffset);
        if (Math.Abs(_scrollViewer.Offset.Y - nextY) < 0.5)
        {
            return;
        }

        _scrollViewer.Offset = new Vector(_scrollViewer.Offset.X, nextY);
        InvalidateVisual();
    }

    private double GetRowTop(int index)
    {
        index = Math.Clamp(index, 0, _itemCount);
        return ContentTop + (index * EstimatedMessageHeight) + _heightDeltas.PrefixSum(index);
    }

    private double GetRowHeight(int index)
    {
        return _rowHeights.TryGetValue(index, out var height) && height > 0.0
            ? height
            : EstimatedMessageHeight;
    }

    private double GetTotalHeight()
    {
        return ContentTop + ContentBottom + (_itemCount * EstimatedMessageHeight) + _heightDeltas.PrefixSum(_itemCount);
    }

    private double ResolveLayoutWidth(double width)
    {
        var viewportWidth = _scrollViewer?.Viewport.Width ?? 0.0;
        if (double.IsFinite(viewportWidth) && viewportWidth > 0.0)
        {
            return viewportWidth;
        }

        if (double.IsFinite(width) && width > 0.0)
        {
            return width;
        }

        if (double.IsFinite(Bounds.Width) && Bounds.Width > 0.0)
        {
            return Bounds.Width;
        }

        return double.IsFinite(_layoutWidth) && _layoutWidth > 0.0
            ? _layoutWidth
            : 1.0;
    }
}
