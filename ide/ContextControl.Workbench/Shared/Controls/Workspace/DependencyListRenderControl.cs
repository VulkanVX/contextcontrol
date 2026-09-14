// CC-DESC: Draws backend dependencies as one fixed-row virtualized surface.

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

public sealed class DependencyListRenderControl : Control
{
    public static readonly StyledProperty<IReadOnlyList<LlmBackendDependencyViewModel>?> ItemsProperty =
        AvaloniaProperty.Register<DependencyListRenderControl, IReadOnlyList<LlmBackendDependencyViewModel>?>(nameof(Items));

    public static readonly StyledProperty<ICommand?> InstallCommandProperty =
        AvaloniaProperty.Register<DependencyListRenderControl, ICommand?>(nameof(InstallCommand));

    public static readonly StyledProperty<ICommand?> UninstallCommandProperty =
        AvaloniaProperty.Register<DependencyListRenderControl, ICommand?>(nameof(UninstallCommand));

    public static readonly StyledProperty<string> ThemeKeyProperty =
        AvaloniaProperty.Register<DependencyListRenderControl, string>(nameof(ThemeKey), "empty");

    public static readonly StyledProperty<string> UiFontFamilyProperty =
        AvaloniaProperty.Register<DependencyListRenderControl, string>(nameof(UiFontFamily), "fonts:Inter, Segoe UI");

    public static readonly StyledProperty<string> CodeFontFamilyProperty =
        AvaloniaProperty.Register<DependencyListRenderControl, string>(nameof(CodeFontFamily), "avares://ContextControl.Workbench/Assets/Fonts#Cascadia Code, Consolas");

    private const double ContentTop = 2.0;
    private const double ContentBottom = 6.0;
    private const double HorizontalInset = 3.0;
    private const double RowPitch = 66.0;
    private const double CardHeight = 62.0;
    private const double CardPadding = 7.0;
    private const double IconSize = 34.0;
    private const double IconImageSize = 24.0;
    private const double ButtonHeight = 18.0;
    private const double ViewportOverscan = RowPitch * 2.0;
    private const int MaxTextCacheEntries = 4096;

    private static readonly FontFamily DefaultCodeFontFamily = new("Consolas");
    private static readonly FontFamily DefaultUiFontFamily = new("Segoe UI");
    private static readonly Cursor HandCursor = new(StandardCursorType.Hand);
    private static readonly Cursor ArrowCursor = new(StandardCursorType.Arrow);
    private static readonly IBrush TransparentBrush = Brushes.Transparent;
    private static readonly IBrush EditorSurfaceFallbackBrush = Brush.Parse("#F7F3EA");
    private static readonly IBrush PanelBorderFallbackBrush = Brush.Parse("#D8D3C7");
    private static readonly IBrush CommandBackgroundFallbackBrush = Brush.Parse("#EEE9DE");
    private static readonly IBrush CommandBorderFallbackBrush = Brush.Parse("#C9D0D2");
    private static readonly IBrush HistoryHoverFallbackBrush = Brush.Parse("#E8ECE8");
    private static readonly IBrush HistoryActiveFallbackBrush = Brush.Parse("#DDE7E6");
    private static readonly IBrush AccentFallbackBrush = Brush.Parse("#227A58");
    private static readonly IBrush AccentBorderFallbackBrush = Brush.Parse("#79BDA0");
    private static readonly IBrush TextPrimaryFallbackBrush = Brush.Parse("#26383D");
    private static readonly IBrush TextMutedFallbackBrush = Brush.Parse("#6F7F85");

    private readonly List<INotifyPropertyChanged> _subscribedItems = [];
    private readonly Dictionary<TextCacheKey, FormattedText> _textCache = [];
    private INotifyCollectionChanged? _itemsCollectionChanged;
    private ScrollViewer? _scrollViewer;
    private IDisposable? _offsetSubscription;
    private IDisposable? _viewportSubscription;
    private int _hoveredIndex = -1;
    private bool _hoverInstall;
    private bool _hoverIcon;

    public DependencyListRenderControl()
    {
        ToolTip.SetPlacement(this, PlacementMode.Pointer);
        ToolTip.SetShowDelay(this, 180);
    }

    public IReadOnlyList<LlmBackendDependencyViewModel>? Items
    {
        get => GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    public ICommand? InstallCommand
    {
        get => GetValue(InstallCommandProperty);
        set => SetValue(InstallCommandProperty, value);
    }

    public ICommand? UninstallCommand
    {
        get => GetValue(UninstallCommandProperty);
        set => SetValue(UninstallCommandProperty, value);
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
            InvalidateMeasure();
            InvalidateVisual();
        }
        else if (change.Property == ThemeKeyProperty
            || change.Property == UiFontFamilyProperty
            || change.Property == CodeFontFamilyProperty)
        {
            _textCache.Clear();
            InvalidateVisual();
        }
        else if (change.Property == InstallCommandProperty
            || change.Property == UninstallCommandProperty)
        {
            InvalidateVisual();
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsFinite(availableSize.Width) ? availableSize.Width : 1.0;
        return new Size(Math.Max(1.0, width), ContentTop + ContentBottom + (Items?.Count ?? 0) * RowPitch);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var viewportTop = Math.Max(0.0, _scrollViewer?.Offset.Y ?? 0.0);
        var viewportHeight = _scrollViewer?.Viewport.Height ?? Math.Min(Bounds.Height, 900.0);
        if (!double.IsFinite(viewportHeight) || viewportHeight <= 0.0)
        {
            viewportHeight = Math.Min(Bounds.Height, 900.0);
        }

        context.DrawRectangle(TransparentBrush, null, new Rect(0, viewportTop, Bounds.Width, viewportHeight));

        var items = Items;
        if (items is null || items.Count == 0)
        {
            DrawEmpty(context, viewportTop, viewportHeight);
            return;
        }

        var uiFont = ResolveFontFamily(UiFontFamily, Resource("UiFontFamily", DefaultUiFontFamily));
        var codeFont = ResolveFontFamily(CodeFontFamily, Resource("CodeFontFamily", DefaultCodeFontFamily));
        var start = Math.Max(0, (int)Math.Floor((viewportTop - ContentTop - ViewportOverscan) / RowPitch));
        var end = Math.Min(items.Count - 1, (int)Math.Ceiling((viewportTop + viewportHeight + ViewportOverscan - ContentTop) / RowPitch));
        for (var index = start; index <= end; index++)
        {
            DrawDependency(context, items[index], index, RowTop(index), uiFont, codeFont);
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var (index, install, icon) = HitTestDependency(e.GetPosition(this));
        if (_hoveredIndex == index && _hoverInstall == install && _hoverIcon == icon)
        {
            return;
        }

        _hoveredIndex = index;
        _hoverInstall = install;
        _hoverIcon = icon;
        Cursor = install ? HandCursor : ArrowCursor;
        ToolTip.SetTip(this, BuildHoverToolTip(index, icon));
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hoveredIndex = -1;
        _hoverInstall = false;
        _hoverIcon = false;
        Cursor = ArrowCursor;
        ToolTip.SetTip(this, null);
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var (index, install, _) = HitTestDependency(e.GetPosition(this));
        var items = Items;
        if (!install || items is null || index < 0 || index >= items.Count)
        {
            return;
        }

        var item = items[index];
        if (item.CanUninstall || item.CanForceInstall)
        {
            if (UninstallCommand?.CanExecute(item) == true)
            {
                UninstallCommand.Execute(item);
                e.Handled = true;
            }

            return;
        }

        if (InstallCommand?.CanExecute(item) == true)
        {
            InstallCommand.Execute(item);
            e.Handled = true;
        }
    }

    private void DrawDependency(DrawingContext context, LlmBackendDependencyViewModel item, int index, double rowTop, FontFamily uiFont, FontFamily codeFont)
    {
        var card = CardRect(rowTop);
        if (card.Width <= 24.0)
        {
            return;
        }

        var background = item.IsReady
            ? Resource("CommandBackgroundBrush", CommandBackgroundFallbackBrush)
            : _hoveredIndex == index
                ? Resource("HistoryHoverBrush", HistoryHoverFallbackBrush)
                : Resource("EditorSurfaceBrush", EditorSurfaceFallbackBrush);
        var border = item.IsRecommended || item.IsReady
            ? Resource("AccentBorderBrush", AccentBorderFallbackBrush)
            : Resource("PanelBorderBrush", PanelBorderFallbackBrush);
        context.DrawRectangle(background, new Pen(border, 1), card, 5, 5);

        var rightWidth = Math.Clamp(card.Width * 0.20, 118.0, 174.0);
        var metricsWidth = Math.Clamp(card.Width * 0.28, 150.0, 260.0);
        var right = new Rect(card.Right - CardPadding - rightWidth, card.Y + 5.0, rightWidth, card.Height - 10.0);
        var metrics = new Rect(right.X - metricsWidth - 8.0, card.Y + 7.0, metricsWidth, card.Height - 14.0);
        var iconShell = IconShellRect(rowTop);
        DrawDependencyIcon(context, item, iconShell, codeFont);

        var leftX = iconShell.Right + 7.0;
        var left = new Rect(leftX, card.Y + 5.0, Math.Max(0.0, metrics.X - leftX - 8.0), card.Height - 10.0);
        var sizeWidth = left.Width >= 260.0
            ? Math.Clamp(left.Width * 0.24, 88.0, 132.0)
            : 0.0;
        var textWidth = sizeWidth > 0.0
            ? Math.Max(0.0, left.Width - sizeWidth - 8.0)
            : left.Width;

        DrawText(context, item.DisplayName, left.X, left.Y - 1.0, textWidth, 13.0, uiFont, 10.8, FontWeight.ExtraBold, Resource("TextPrimaryBrush", TextPrimaryFallbackBrush), 80);
        DrawText(context, item.Id, left.X, left.Y + 12.0, textWidth, 10.0, codeFont, 7.8, FontWeight.Bold, Resource("TextMutedBrush", TextMutedFallbackBrush), 90);
        if (sizeWidth > 0.0)
        {
            DrawDependencyInlineFacts(
                context,
                item,
                new Rect(left.Right - sizeWidth, left.Y, sizeWidth, 24.0),
                codeFont);
        }

        DrawText(context, item.Purpose, left.X, left.Y + 24.0, left.Width, 11.0, uiFont, 8.6, FontWeight.Normal, Resource("TextPrimaryBrush", TextPrimaryFallbackBrush), 110);
        DrawText(context, item.DetailLabel, left.X, left.Y + 36.0, left.Width, 10.0, uiFont, 7.8, FontWeight.Normal, Resource("TextMutedBrush", TextMutedFallbackBrush), 130);

        DrawChip(context, "TYPE", item.Category, new Rect(metrics.X, metrics.Y, metrics.Width, 14.0), codeFont);
        DrawChip(context, "API", item.ApiStyle, new Rect(metrics.X, metrics.Y + 16.0, metrics.Width, 14.0), codeFont);
        DrawChip(context, "OS", item.Platforms, new Rect(metrics.X, metrics.Y + 32.0, metrics.Width, 14.0), codeFont);

        DrawPill(context, item.StatusLabel, new Rect(right.X, right.Y, right.Width, 16.0), codeFont, item.IsReady);
        DrawInstallButton(context, item, index, InstallButtonRect(rowTop));
    }

    private void DrawDependencyInlineFacts(DrawingContext context, LlmBackendDependencyViewModel item, Rect rect, FontFamily font)
    {
        if (rect.Width <= 0.0 || string.IsNullOrWhiteSpace(item.SizeLabel))
        {
            return;
        }

        var size = GetFormattedText(
            Clean($"Size {item.SizeLabel}", 30),
            Resource("AccentBrush", AccentFallbackBrush),
            font,
            FontWeight.Black,
            FontStyle.Normal,
            10.2);
        DrawClippedText(
            context,
            size,
            new Rect(rect.X, rect.Y + 4.0, rect.Width, 14.0),
            new Point(rect.Right - Math.Min(rect.Width, size.Width), rect.Y + 3.2));
    }

    private void DrawDependencyIcon(DrawingContext context, LlmBackendDependencyViewModel item, Rect shell, FontFamily font)
    {
        var shellFill = item.IconHasTransparentBackground
            ? null
            : Resource("CommandBackgroundBrush", CommandBackgroundFallbackBrush);
        context.DrawRectangle(
            shellFill,
            new Pen(Resource("CommandBorderBrush", CommandBorderFallbackBrush), 1),
            shell,
            6,
            6);

        if (item.IconImage is { } icon)
        {
            var iconRect = new Rect(
                shell.X + (shell.Width - IconImageSize) * 0.5,
                shell.Y + (shell.Height - IconImageSize) * 0.5,
                IconImageSize,
                IconImageSize);
            context.DrawImage(icon, iconRect);
            return;
        }

        var initials = GetFormattedText(
            ResolveInitials(item.DisplayName),
            Resource("AccentBrush", AccentFallbackBrush),
            font,
            FontWeight.Black,
            FontStyle.Normal,
            8.0);
        DrawClippedText(
            context,
            initials,
            shell,
            new Point(
                shell.X + Math.Max(0.0, (shell.Width - initials.Width) * 0.5),
                shell.Y + Math.Max(0.0, (shell.Height - initials.Height) * 0.5)));
    }

    private void DrawChip(DrawingContext context, string label, string value, Rect rect, FontFamily font)
    {
        context.DrawRectangle(Resource("CommandBackgroundBrush", CommandBackgroundFallbackBrush), new Pen(Resource("CommandBorderBrush", CommandBorderFallbackBrush), 1), rect, 4, 4);
        DrawText(context, label, rect.X + 5.0, rect.Y + 1.0, 28.0, rect.Height, font, 7.0, FontWeight.Black, Resource("AccentBrush", AccentFallbackBrush), 12);
        DrawText(context, value, rect.X + 34.0, rect.Y + 1.0, Math.Max(0.0, rect.Width - 38.0), rect.Height, font, 7.8, FontWeight.Bold, Resource("TextMutedBrush", TextMutedFallbackBrush), 42);
    }

    private void DrawPill(DrawingContext context, string label, Rect rect, FontFamily font, bool accent)
    {
        context.DrawRectangle(
            Resource(accent ? "CommandPrimaryBackgroundBrush" : "CommandBackgroundBrush", CommandBackgroundFallbackBrush),
            new Pen(Resource(accent ? "AccentBorderBrush" : "CommandBorderBrush", accent ? AccentBorderFallbackBrush : CommandBorderFallbackBrush), 1),
            rect,
            4,
            4);
        DrawText(context, label, rect.X + 4.0, rect.Y + 1.0, Math.Max(0.0, rect.Width - 8.0), rect.Height, font, 7.4, FontWeight.Black, Resource("TextPrimaryBrush", TextPrimaryFallbackBrush), 24);
    }

    private void DrawInstallButton(DrawingContext context, LlmBackendDependencyViewModel item, int index, Rect rect)
    {
        var enabled = item.CanUninstall || item.CanForceInstall
            ? UninstallCommand?.CanExecute(item) == true
            : InstallCommand?.CanExecute(item) == true;
        var hovered = enabled && _hoveredIndex == index && _hoverInstall;
        context.DrawRectangle(
            hovered ? Resource("HistoryActiveBrush", HistoryActiveFallbackBrush) : Resource("CommandBackgroundBrush", CommandBackgroundFallbackBrush),
            new Pen(hovered ? Resource("AccentBorderBrush", AccentBorderFallbackBrush) : Resource("CommandBorderBrush", CommandBorderFallbackBrush), 1),
            rect,
            4,
            4);
        var font = ResolveFontFamily(UiFontFamily, Resource("UiFontFamily", DefaultUiFontFamily));
        var brush = enabled ? Resource("TextPrimaryBrush", TextPrimaryFallbackBrush) : Resource("TextMutedBrush", TextMutedFallbackBrush);
        var text = GetFormattedText(Clean(item.InstallActionLabel, 16), brush, font, FontWeight.ExtraBold, FontStyle.Normal, 8.5);
        DrawClippedText(context, text, rect, new Point(rect.X + Math.Max(3.0, (rect.Width - text.Width) * 0.5), rect.Y + 2.0));
    }

    private void DrawEmpty(DrawingContext context, double viewportTop, double viewportHeight)
    {
        var font = ResolveFontFamily(UiFontFamily, Resource("UiFontFamily", DefaultUiFontFamily));
        var text = GetFormattedText("No dependencies match the current search.", Resource("TextMutedBrush", TextMutedFallbackBrush), font, FontWeight.SemiBold, FontStyle.Normal, 11.0);
        DrawClippedText(context, text, new Rect(0, viewportTop, Bounds.Width, viewportHeight), new Point(Math.Max(8.0, (Bounds.Width - text.Width) * 0.5), viewportTop + 36.0));
    }

    private (int Index, bool Install, bool Icon) HitTestDependency(Point point)
    {
        var items = Items;
        if (items is null || items.Count == 0 || point.X < 0 || point.Y < ContentTop || point.X > Bounds.Width)
        {
            return (-1, false, false);
        }

        var index = (int)Math.Floor((point.Y - ContentTop) / RowPitch);
        if (index < 0 || index >= items.Count || !CardRect(RowTop(index)).Contains(point))
        {
            return (-1, false, false);
        }

        var rowTop = RowTop(index);
        return (index, InstallButtonRect(rowTop).Contains(point), IconShellRect(rowTop).Contains(point));
    }

    private Control? BuildHoverToolTip(int index, bool icon)
    {
        var items = Items;
        if (items is null || index < 0 || index >= items.Count)
        {
            return null;
        }

        var item = items[index];
        return icon ? BuildIconPreviewToolTip(item) : BuildDependencyDetailToolTip(item);
    }

    private Control BuildIconPreviewToolTip(LlmBackendDependencyViewModel item)
    {
        Control content = item.IconImage is null
            ? new TextBlock
            {
                Text = ResolveInitials(item.DisplayName),
                FontSize = 28,
                FontWeight = FontWeight.Black,
                Foreground = Resource("AccentBrush", AccentFallbackBrush),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
            : new Image
            {
                Source = item.IconImage,
                Width = 180,
                Height = 180,
                Stretch = Stretch.Uniform
            };

        return new Border
        {
            Padding = new Thickness(8),
            Background = Resource("EditorSurfaceBrush", EditorSurfaceFallbackBrush),
            BorderBrush = Resource("PanelBorderBrush", PanelBorderFallbackBrush),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Child = content
        };
    }

    private Control BuildDependencyDetailToolTip(LlmBackendDependencyViewModel item)
    {
        var panel = new StackPanel { Spacing = 3, MaxWidth = 380 };
        panel.Children.Add(new TextBlock
        {
            Text = $"{item.DisplayName} ({item.Id})",
            FontSize = 10,
            FontWeight = FontWeight.ExtraBold,
            Foreground = Resource("TextPrimaryBrush", TextPrimaryFallbackBrush),
            TextWrapping = TextWrapping.Wrap
        });

        foreach (var (label, value) in new[]
        {
            ("STATUS", item.StatusLabel),
            ("SIZE", item.SizeLabel),
            ("TYPE", item.Category),
            ("API", item.ApiStyle),
            ("OS", item.Platforms),
            ("WHY", item.Purpose),
            ("DETAIL", item.DetailLabel),
            ("INSTALL", item.InstallHint)
        })
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            panel.Children.Add(new Border
            {
                Padding = new Thickness(0, 2),
                BorderBrush = Resource("CommandBorderBrush", CommandBorderFallbackBrush),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Child = new Grid
                {
                    ColumnDefinitions =
                    {
                        new ColumnDefinition(54, GridUnitType.Pixel),
                        new ColumnDefinition(1, GridUnitType.Star)
                    },
                    Children =
                    {
                        new TextBlock
                        {
                            Text = label,
                            FontSize = 9,
                            FontWeight = FontWeight.Black,
                            Foreground = Resource("AccentBrush", AccentFallbackBrush)
                        },
                        new TextBlock
                        {
                            [Grid.ColumnProperty] = 1,
                            Text = Clean(value, 140),
                            FontSize = 9,
                            FontWeight = FontWeight.Bold,
                            Foreground = Resource("TextMutedBrush", TextMutedFallbackBrush),
                            TextWrapping = TextWrapping.Wrap
                        }
                    }
                }
            });
        }

        return new Border
        {
            Padding = new Thickness(6),
            Background = Resource("EditorSurfaceBrush", EditorSurfaceFallbackBrush),
            BorderBrush = Resource("PanelBorderBrush", PanelBorderFallbackBrush),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5),
            Child = panel
        };
    }

    private void AttachItems()
    {
        DetachItems();
        if (Items is INotifyCollectionChanged collectionChanged)
        {
            _itemsCollectionChanged = collectionChanged;
            collectionChanged.CollectionChanged += OnItemsCollectionChanged;
        }

        if (Items is not null)
        {
            foreach (var item in Items.OfType<INotifyPropertyChanged>())
            {
                item.PropertyChanged += OnItemPropertyChanged;
                _subscribedItems.Add(item);
            }
        }
    }

    private void DetachItems()
    {
        if (_itemsCollectionChanged is not null)
        {
            _itemsCollectionChanged.CollectionChanged -= OnItemsCollectionChanged;
            _itemsCollectionChanged = null;
        }

        foreach (var item in _subscribedItems)
        {
            item.PropertyChanged -= OnItemPropertyChanged;
        }

        _subscribedItems.Clear();
    }

    private void OnItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        AttachItems();
        _textCache.Clear();
        InvalidateMeasure();
        InvalidateVisual();
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
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

    private Rect CardRect(double rowTop)
    {
        return new Rect(HorizontalInset, rowTop, Math.Max(0.0, Bounds.Width - HorizontalInset * 2.0 - 2.0), CardHeight);
    }

    private Rect InstallButtonRect(double rowTop)
    {
        var card = CardRect(rowTop);
        var width = Math.Clamp(card.Width * 0.11, 62.0, 82.0);
        return new Rect(card.Right - CardPadding - width, card.Bottom - CardPadding - ButtonHeight, width, ButtonHeight);
    }

    private Rect IconShellRect(double rowTop)
    {
        var card = CardRect(rowTop);
        return new Rect(card.X + CardPadding, card.Y + (card.Height - IconSize) * 0.5, IconSize, IconSize);
    }

    private static double RowTop(int index)
    {
        return ContentTop + Math.Max(0, index) * RowPitch;
    }

    private void DrawText(DrawingContext context, string text, double x, double y, double width, double height, FontFamily font, double size, FontWeight weight, IBrush brush, int maxLength)
    {
        if (width <= 0.0 || height <= 0.0)
        {
            return;
        }

        var formatted = GetFormattedText(Clean(text, maxLength), brush, font, weight, FontStyle.Normal, size);
        DrawClippedText(context, formatted, new Rect(x, y, width, height), new Point(x, y));
    }

    private FormattedText GetFormattedText(string text, IBrush brush, FontFamily fontFamily, FontWeight weight, FontStyle style, double fontSize)
    {
        var key = new TextCacheKey(text, RuntimeHelpers.GetHashCode(brush), fontFamily.ToString(), weight, style, fontSize);
        if (_textCache.TryGetValue(key, out var formatted))
        {
            return formatted;
        }

        if (_textCache.Count > MaxTextCacheEntries)
        {
            _textCache.Clear();
        }

        formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(fontFamily, style, weight), fontSize, brush);
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

    private static string ResolveInitials(string? value)
    {
        var words = (value ?? "")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 0)
        {
            return "?";
        }

        return string.Concat(words.Take(2).Select(word => char.ToUpperInvariant(word[0])));
    }

    private readonly record struct TextCacheKey(string Text, int BrushId, string FontFamily, FontWeight Weight, FontStyle Style, double FontSize);

    private sealed class ValueObserver<T>(Action<T> onNext) : IObserver<T>
    {
        public void OnCompleted() { }
        public void OnError(Exception error) { }
        public void OnNext(T value) => onNext(value);
    }
}
