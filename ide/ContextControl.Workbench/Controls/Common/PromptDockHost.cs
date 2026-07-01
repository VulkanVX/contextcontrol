using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace ContextControl.Workbench.Controls;

public sealed class PromptDockHost : Panel
{
    public static readonly StyledProperty<double> PromptHeightProperty =
        AvaloniaProperty.Register<PromptDockHost, double>(nameof(PromptHeight));

    public static readonly StyledProperty<bool> IsPromptOpenProperty =
        AvaloniaProperty.Register<PromptDockHost, bool>(nameof(IsPromptOpen));

    public double PromptHeight
    {
        get => GetValue(PromptHeightProperty);
        set => SetValue(PromptHeightProperty, value);
    }

    public bool IsPromptOpen
    {
        get => GetValue(IsPromptOpenProperty);
        set => SetValue(IsPromptOpenProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == PromptHeightProperty
            || change.Property == IsPromptOpenProperty)
        {
            InvalidateMeasure();
            InvalidateArrange();
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var promptHeight = ResolvePromptHeight(IsPromptOpen, PromptHeight, availableSize.Height);
        var contentHeight = double.IsFinite(availableSize.Height)
            ? Math.Max(0, availableSize.Height - promptHeight)
            : double.PositiveInfinity;

        var content = ContentChild;
        content?.Measure(new Size(availableSize.Width, contentHeight));

        var prompt = PromptChild;
        prompt?.Measure(new Size(availableSize.Width, promptHeight));

        var overlay = OverlayChild;
        overlay?.Measure(new Size(availableSize.Width, double.PositiveInfinity));

        var desiredWidth = Math.Max(
            Math.Max(content?.DesiredSize.Width ?? 0, prompt?.DesiredSize.Width ?? 0),
            overlay?.DesiredSize.Width ?? 0);
        var desiredHeight = (content?.DesiredSize.Height ?? 0) + promptHeight;
        return new Size(
            double.IsFinite(availableSize.Width) ? availableSize.Width : desiredWidth,
            double.IsFinite(availableSize.Height) ? availableSize.Height : desiredHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var promptHeight = ResolvePromptHeight(IsPromptOpen, PromptHeight, finalSize.Height);
        var contentHeight = Math.Max(0, finalSize.Height - promptHeight);
        var content = ContentChild;
        var prompt = PromptChild;
        var overlay = OverlayChild;

        content?.Arrange(new Rect(0, 0, finalSize.Width, contentHeight));
        prompt?.Arrange(new Rect(0, contentHeight, finalSize.Width, promptHeight));
        if (overlay is not null && overlay.IsVisible)
        {
            var overlayHeight = overlay.DesiredSize.Height;
            var overlayWidth = ResolveOverlayWidth(overlay, finalSize.Width);
            var overlayLeft = ResolveOverlayLeft(overlay, finalSize.Width, overlayWidth);
            var overlayTop = Math.Max(0, contentHeight - overlayHeight - 14);
            overlay.Arrange(new Rect(overlayLeft, overlayTop, overlayWidth, overlayHeight));
        }

        return finalSize;
    }

    private Control? ContentChild => Children.Count > 0 ? Children[0] : null;

    private Control? PromptChild => Children.Count > 1 ? Children[1] : null;

    private Control? OverlayChild => Children.Count > 2 ? Children[2] : null;

    private static double ResolveOverlayWidth(Control overlay, double availableWidth)
    {
        if (overlay.HorizontalAlignment == HorizontalAlignment.Stretch)
        {
            return availableWidth;
        }

        return Math.Min(availableWidth, overlay.DesiredSize.Width);
    }

    private static double ResolveOverlayLeft(Control overlay, double availableWidth, double overlayWidth)
    {
        return overlay.HorizontalAlignment switch
        {
            HorizontalAlignment.Center => Math.Max(0, (availableWidth - overlayWidth) / 2),
            HorizontalAlignment.Right => Math.Max(0, availableWidth - overlayWidth),
            _ => 0
        };
    }

    private static double ResolvePromptHeight(bool isPromptOpen, double requestedHeight, double availableHeight)
    {
        if (!isPromptOpen)
        {
            return 0;
        }

        var promptHeight = CleanPromptHeight(requestedHeight);
        return double.IsFinite(availableHeight)
            ? Math.Min(promptHeight, Math.Max(0, availableHeight))
            : promptHeight;
    }

    private static double CleanPromptHeight(double value)
    {
        return double.IsFinite(value) ? Math.Max(0, value) : 0;
    }
}
