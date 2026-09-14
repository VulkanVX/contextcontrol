using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace ContextControl.Workbench.Controls;

public sealed class LoadingRingControl : Control
{
    public static readonly StyledProperty<IBrush?> TrackBrushProperty =
        AvaloniaProperty.Register<LoadingRingControl, IBrush?>(nameof(TrackBrush));

    public static readonly StyledProperty<IBrush?> RingBrushProperty =
        AvaloniaProperty.Register<LoadingRingControl, IBrush?>(nameof(RingBrush));

    private static readonly IBrush FallbackTrackBrush = new SolidColorBrush(Color.FromArgb(90, 120, 132, 138));
    private static readonly IBrush FallbackRingBrush = new SolidColorBrush(Color.FromRgb(69, 164, 176));

    private readonly DispatcherTimer _animationTimer;
    private double _angle;
    private bool _isAttached;

    public LoadingRingControl()
    {
        _animationTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _animationTimer.Tick += (_, _) =>
        {
            _angle = (_angle + 8.5) % 360.0;
            InvalidateVisual();
        };
    }

    public IBrush? TrackBrush
    {
        get => GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    public IBrush? RingBrush
    {
        get => GetValue(RingBrushProperty);
        set => SetValue(RingBrushProperty, value);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _isAttached = true;
        UpdateTimer();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _isAttached = false;
        _animationTimer.Stop();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsVisibleProperty)
        {
            UpdateTimer();
        }
        else if (change.Property == TrackBrushProperty || change.Property == RingBrushProperty)
        {
            InvalidateVisual();
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var size = Bounds.Size;
        var diameter = Math.Min(size.Width, size.Height);
        if (diameter <= 2)
        {
            return;
        }

        var stroke = Math.Max(1.4, diameter * 0.18);
        var radius = Math.Max(1.0, (diameter - stroke) * 0.5);
        var center = new Point(size.Width * 0.5, size.Height * 0.5);
        context.DrawEllipse(null, new Pen(TrackBrush ?? FallbackTrackBrush, stroke), center, radius, radius);
        context.DrawGeometry(null, new Pen(RingBrush ?? FallbackRingBrush, stroke), CreateArc(center, radius, _angle, 88.0));
    }

    private void UpdateTimer()
    {
        if (_isAttached && IsVisible)
        {
            if (!_animationTimer.IsEnabled)
            {
                _animationTimer.Start();
            }
        }
        else
        {
            _animationTimer.Stop();
        }
    }

    private static StreamGeometry CreateArc(Point center, double radius, double startDegrees, double sweepDegrees)
    {
        var start = PointOnCircle(center, radius, startDegrees);
        var end = PointOnCircle(center, radius, startDegrees + sweepDegrees);
        var geometry = new StreamGeometry();
        using (var stream = geometry.Open())
        {
            stream.BeginFigure(start, false);
            stream.ArcTo(end, new Size(radius, radius), 0, sweepDegrees > 180.0, SweepDirection.Clockwise);
        }

        return geometry;
    }

    private static Point PointOnCircle(Point center, double radius, double degrees)
    {
        var radians = degrees * Math.PI / 180.0;
        return new Point(
            center.X + Math.Cos(radians) * radius,
            center.Y + Math.Sin(radians) * radius);
    }
}
