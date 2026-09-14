using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace ContextControl.Workbench.Controls;

public static class HoverScrollbarBehavior
{
    public const string ExpandedClass = "scrollbar-expanded";

    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("IsEnabled", typeof(HoverScrollbarBehavior));

    public static readonly AttachedProperty<double> ReserveRightProperty =
        AvaloniaProperty.RegisterAttached<Control, double>("ReserveRight", typeof(HoverScrollbarBehavior));

    public static readonly AttachedProperty<double> ReserveBottomProperty =
        AvaloniaProperty.RegisterAttached<Control, double>("ReserveBottom", typeof(HoverScrollbarBehavior));

    private static readonly ConditionalWeakTable<Control, HoverState> States = new();

    static HoverScrollbarBehavior()
    {
        IsEnabledProperty.Changed.AddClassHandler<Control>((control, args) =>
        {
            if (args.NewValue is true)
            {
                Attach(control);
            }
            else
            {
                Detach(control);
            }
        });
    }

    public static bool GetIsEnabled(Control control)
    {
        return control.GetValue(IsEnabledProperty);
    }

    public static void SetIsEnabled(Control control, bool value)
    {
        control.SetValue(IsEnabledProperty, value);
    }

    public static double GetReserveRight(Control control)
    {
        return control.GetValue(ReserveRightProperty);
    }

    public static void SetReserveRight(Control control, double value)
    {
        control.SetValue(ReserveRightProperty, value);
    }

    public static double GetReserveBottom(Control control)
    {
        return control.GetValue(ReserveBottomProperty);
    }

    public static void SetReserveBottom(Control control, double value)
    {
        control.SetValue(ReserveBottomProperty, value);
    }

    public static void Refresh(Control? control)
    {
        if (control is null || !States.TryGetValue(control, out var state))
        {
            return;
        }

        Update(control, state, latchExpanded: state.IsHovered);
    }

    private static void Attach(Control control)
    {
        var state = States.GetValue(control, _ => new HoverState());
        if (state.IsAttached)
        {
            return;
        }

        state.IsAttached = true;
        control.PointerEntered += OnPointerEntered;
        control.PointerExited += OnPointerExited;
        control.AddHandler(InputElement.PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        control.AddHandler(InputElement.PointerReleasedEvent, OnPointerReleased, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
        control.SizeChanged += OnSizeChanged;
        control.AttachedToVisualTree += OnAttachedToVisualTree;
        control.DetachedFromVisualTree += OnDetachedFromVisualTree;
        ScheduleRefresh(control, state);
    }

    private static void Detach(Control control)
    {
        if (!States.TryGetValue(control, out var state) || !state.IsAttached)
        {
            return;
        }

        SetExpanded(control, state, false, false);
        state.IsHovered = false;
        state.IsAttached = false;
        control.PointerEntered -= OnPointerEntered;
        control.PointerExited -= OnPointerExited;
        control.RemoveHandler(InputElement.PointerPressedEvent, OnPointerPressed);
        control.RemoveHandler(InputElement.PointerReleasedEvent, OnPointerReleased);
        control.SizeChanged -= OnSizeChanged;
        control.AttachedToVisualTree -= OnAttachedToVisualTree;
        control.DetachedFromVisualTree -= OnDetachedFromVisualTree;
    }

    private static void OnPointerEntered(object? sender, PointerEventArgs e)
    {
        if (sender is not Control control)
        {
            return;
        }

        var state = States.GetValue(control, _ => new HoverState());
        state.IsHovered = true;
        Update(control, state, latchExpanded: true);
        ScheduleRefresh(control, state);
    }

    private static void OnPointerExited(object? sender, PointerEventArgs e)
    {
        if (sender is not Control control || !States.TryGetValue(control, out var state))
        {
            return;
        }

        if (state.IsScrollbarPressed)
        {
            state.IsHovered = true;
            Update(control, state, latchExpanded: true);
            return;
        }

        if (ContainsPointer(control, e))
        {
            Refresh(control);
            return;
        }

        state.IsHovered = false;
        Update(control, state, latchExpanded: false);
    }

    private static void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control control || !IsScrollChromeSource(e.Source))
        {
            return;
        }

        var state = States.GetValue(control, _ => new HoverState());
        state.IsScrollbarPressed = true;
        state.IsHovered = true;
        Update(control, state, latchExpanded: true);
    }

    private static void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (sender is not Control control
            || !States.TryGetValue(control, out var state)
            || !state.IsScrollbarPressed)
        {
            return;
        }

        state.IsScrollbarPressed = false;
        state.IsHovered = control.IsPointerOver || ContainsPointer(control, e);
        Update(control, state, latchExpanded: state.IsHovered);
        ScheduleRefresh(control, state);
    }

    private static void OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (sender is Control control && States.TryGetValue(control, out var state))
        {
            ScheduleRefresh(control, state);
        }
    }

    private static void ScheduleRefresh(Control control, HoverState state)
    {
        if (state.IsRefreshQueued)
        {
            return;
        }

        state.IsRefreshQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            state.IsRefreshQueued = false;
            Refresh(control);
        });
    }

    private static void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is Control control && control.IsPointerOver)
        {
            var state = States.GetValue(control, _ => new HoverState());
            state.IsHovered = true;
        }

        if (sender is Control attachedControl)
        {
            Dispatcher.UIThread.Post(() => Refresh(attachedControl));
        }
    }

    private static void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is Control control && States.TryGetValue(control, out var state))
        {
            state.IsHovered = false;
            state.IsScrollbarPressed = false;
            SetExpanded(control, state, false, false);
        }
    }

    private static void Update(Control control, HoverState state, bool latchExpanded)
    {
        var isScrollable = HasScrollableContent(control);
        var wasExpanded = control.Classes.Contains(ExpandedClass);
        var shouldExpand = state.IsHovered && isScrollable && (latchExpanded || wasExpanded || control.IsPointerOver);
        SetExpanded(control, state, shouldExpand, isScrollable);
    }

    private static void SetExpanded(Control control, HoverState state, bool isExpanded, bool isScrollable)
    {
        ScrollViewer.SetAllowAutoHide(control, false);
        foreach (var scrollViewer in GetScrollViewers(control))
        {
            ScrollViewer.SetAllowAutoHide(scrollViewer, false);
        }

        if (isExpanded)
        {
            if (!control.Classes.Contains(ExpandedClass))
            {
                control.Classes.Add(ExpandedClass);
            }
        }
        else
        {
            control.Classes.Remove(ExpandedClass);
        }

        foreach (var scrollBar in GetScrollBars(control))
        {
            if (isExpanded)
            {
                if (!scrollBar.Classes.Contains(ExpandedClass))
                {
                    scrollBar.Classes.Add(ExpandedClass);
                }
            }
            else
            {
                scrollBar.Classes.Remove(ExpandedClass);
            }
        }

        ApplyPaddingReserve(control, state, isExpanded, isScrollable);
    }

    private static void ApplyPaddingReserve(Control control, HoverState state, bool isExpanded, bool isScrollable)
    {
        var reserveRight = Math.Max(0, GetReserveRight(control));
        var reserveBottom = Math.Max(0, GetReserveBottom(control));
        if (reserveRight <= 0 && reserveBottom <= 0)
        {
            return;
        }

        if (!state.HasBasePadding)
        {
            if (!TryGetPadding(control, out var padding))
            {
                return;
            }

            state.BasePadding = padding;
            state.HasBasePadding = true;
        }

        var basePadding = state.BasePadding;
        var nextPadding = isExpanded
            ? new Thickness(
                basePadding.Left,
                basePadding.Top,
                basePadding.Right + reserveRight,
                basePadding.Bottom + reserveBottom)
            : new Thickness(
                basePadding.Left,
                basePadding.Top,
                basePadding.Right + (isScrollable ? reserveRight : 0),
                basePadding.Bottom + (isScrollable ? reserveBottom : 0));

        if (TryGetPadding(control, out var currentPadding) && currentPadding == nextPadding)
        {
            return;
        }

        SetPadding(control, nextPadding);
    }

    private static bool HasScrollableContent(Control control)
    {
        var scrollViewers = GetScrollViewers(control);
        if (scrollViewers.Count == 0)
        {
            return control.Classes.Contains(ExpandedClass);
        }

        foreach (var scrollViewer in scrollViewers)
        {
            var extent = scrollViewer.Extent;
            var viewport = scrollViewer.Viewport;
            var extentReady = IsReady(extent.Width) && IsReady(extent.Height);
            var viewportReady = IsReady(viewport.Width) && IsReady(viewport.Height);
            if (!extentReady || !viewportReady)
            {
                return control.Classes.Contains(ExpandedClass);
            }

            if (extent.Height > viewport.Height + 0.1 || extent.Width > viewport.Width + 0.1)
            {
                return true;
            }
        }

        return false;
    }

    private static List<ScrollViewer> GetScrollViewers(Control control)
    {
        var scrollViewers = new List<ScrollViewer>();
        if (control is ScrollViewer scrollViewer)
        {
            scrollViewers.Add(scrollViewer);
        }

        scrollViewers.AddRange(control.GetVisualDescendants().OfType<ScrollViewer>());
        return scrollViewers;
    }

    private static List<ScrollBar> GetScrollBars(Control control)
    {
        var scrollBars = new List<ScrollBar>();
        if (control is ScrollBar scrollBar)
        {
            scrollBars.Add(scrollBar);
        }

        scrollBars.AddRange(control.GetVisualDescendants().OfType<ScrollBar>());
        return scrollBars;
    }

    private static bool TryGetPadding(Control control, out Thickness padding)
    {
        switch (control)
        {
            case ScrollViewer scrollViewer:
                padding = scrollViewer.Padding;
                return true;
            case ListBox listBox:
                padding = listBox.Padding;
                return true;
            case TextBox textBox:
                padding = textBox.Padding;
                return true;
            case Border border:
                padding = border.Padding;
                return true;
            default:
                padding = default;
                return false;
        }
    }

    private static void SetPadding(Control control, Thickness padding)
    {
        switch (control)
        {
            case ScrollViewer scrollViewer:
                scrollViewer.Padding = padding;
                break;
            case ListBox listBox:
                listBox.Padding = padding;
                break;
            case TextBox textBox:
                textBox.Padding = padding;
                break;
            case Border border:
                border.Padding = padding;
                break;
        }
    }

    private static bool ContainsPointer(Control control, PointerEventArgs e)
    {
        var point = e.GetPosition(control);
        return point.X >= 0
            && point.Y >= 0
            && point.X <= control.Bounds.Width
            && point.Y <= control.Bounds.Height;
    }

    private static bool IsScrollChromeSource(object? source)
    {
        var control = source as Control;
        while (control is not null)
        {
            if (control is ScrollBar or Thumb or Track or RepeatButton)
            {
                return true;
            }

            control = control.GetVisualParent() as Control;
        }

        return false;
    }

    private static bool IsReady(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value) && value > 0;
    }

    private sealed class HoverState
    {
        public bool IsAttached { get; set; }
        public bool IsHovered { get; set; }
        public bool IsScrollbarPressed { get; set; }
        public bool IsRefreshQueued { get; set; }
        public bool HasBasePadding { get; set; }
        public Thickness BasePadding { get; set; }
    }
}
