using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace ContextControl.Workbench.Controls;

/// <summary>A lightweight live status line. Animation reuses measured text and never rewrites chat content.</summary>
public sealed class ActivityLineControl : Control
{
    public static readonly StyledProperty<string> TextProperty = AvaloniaProperty.Register<ActivityLineControl, string>(nameof(Text), "Working");
    public static readonly StyledProperty<bool> IsActiveProperty = AvaloniaProperty.Register<ActivityLineControl, bool>(nameof(IsActive));
    public static readonly StyledProperty<IBrush> ForegroundProperty = AvaloniaProperty.Register<ActivityLineControl, IBrush>(nameof(Foreground), Brushes.Gray);
    public static readonly StyledProperty<IBrush> AccentProperty = AvaloniaProperty.Register<ActivityLineControl, IBrush>(nameof(Accent), new SolidColorBrush(Color.Parse("#69B8C8")));
    public static readonly StyledProperty<FontFamily> FontFamilyProperty = AvaloniaProperty.Register<ActivityLineControl, FontFamily>(nameof(FontFamily), FontFamily.Default);
    public static readonly StyledProperty<double> FontSizeProperty = AvaloniaProperty.Register<ActivityLineControl, double>(nameof(FontSize), 11);
    private static readonly HashSet<ActivityLineControl> Animated = [];
    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static readonly DispatcherTimer Timer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private FormattedText? _text;
    private FormattedText? _highlight;
    private double _measuredWidth = -1;
    private bool _attached;
    private Window? _window;

    static ActivityLineControl()
    {
        Timer.Tick += (_, _) =>
        {
            foreach (var line in Animated)
                if (line.IsEffectivelyVisible && TopLevel.GetTopLevel(line) is not Window { WindowState: WindowState.Minimized })
                    line.InvalidateVisual();
        };
    }

    public string Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public bool IsActive { get => GetValue(IsActiveProperty); set => SetValue(IsActiveProperty, value); }
    public IBrush Foreground { get => GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }
    public IBrush Accent { get => GetValue(AccentProperty); set => SetValue(AccentProperty, value); }
    public FontFamily FontFamily { get => GetValue(FontFamilyProperty); set => SetValue(FontFamilyProperty, value); }
    public double FontSize { get => GetValue(FontSizeProperty); set => SetValue(FontSizeProperty, value); }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        _window = TopLevel.GetTopLevel(this) as Window;
        if (_window is not null) _window.PropertyChanged += OnWindowChanged;
        UpdateAnimation();
    }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _attached = false;
        if (_window is not null) _window.PropertyChanged -= OnWindowChanged;
        _window = null;
        UpdateAnimation();
        base.OnDetachedFromVisualTree(e);
    }
    private void OnWindowChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == IsVisibleProperty || e.Property == Window.WindowStateProperty) UpdateAnimation();
    }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsActiveProperty || change.Property == IsVisibleProperty) UpdateAnimation();
        if (change.Property == TextProperty || change.Property == FontFamilyProperty || change.Property == FontSizeProperty || change.Property == ForegroundProperty || change.Property == AccentProperty)
        {
            _text = null;
            _highlight = null;
            InvalidateVisual();
            if (change.Property == FontSizeProperty || change.Property == FontFamilyProperty) InvalidateMeasure();
        }
    }

    private void UpdateAnimation()
    {
        if (_attached && IsActive && IsVisible && _window is { IsVisible: true, WindowState: not WindowState.Minimized }) Animated.Add(this); else Animated.Remove(this);
        if (Animated.Count > 0) Timer.Start(); else Timer.Stop();
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize) => new(double.IsFinite(availableSize.Width) ? availableSize.Width : 260, Math.Max(22, FontSize * 1.6));

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var width = Math.Max(1, Bounds.Width - 24);
        if (_text is null || Math.Abs(width - _measuredWidth) > 0.1)
        {
            _measuredWidth = width;
            _text = Format(Foreground, width);
            _highlight = Format(Accent, width);
        }
        var t = Clock.Elapsed.TotalSeconds;
        var center = Bounds.Height / 2;
        for (var i = 0; i < (IsActive ? 3 : 1); i++)
        {
            var pulse = IsActive ? 0.45 + 0.55 * (0.5 + 0.5 * Math.Sin(t * 5 - i * 0.9)) : 0.6;
            using (context.PushOpacity(pulse)) context.DrawEllipse(Accent, null, new Point(4 + i * 5, center), 1.7, 1.7);
        }
        var origin = new Point(23, Math.Max(0, center - _text.Height / 2));
        using (context.PushClip(new Rect(23, 0, width, Bounds.Height)))
        {
            context.DrawText(_text, origin);
            if (IsActive && _highlight is not null)
            {
                var sweep = (t % 2.6) / 2.6 * (width + 90) - 60;
                using (context.PushClip(new Rect(23 + sweep, 0, 54, Bounds.Height)))
                using (context.PushOpacity(0.7)) context.DrawText(_highlight, origin);
            }
        }
    }

    private FormattedText Format(IBrush brush, double width) => new(Text ?? "", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(FontFamily), FontSize, brush)
    {
        MaxTextWidth = width, MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis
    };
}
