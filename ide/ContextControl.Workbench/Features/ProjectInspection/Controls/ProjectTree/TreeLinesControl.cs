using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace ContextControl.Workbench.Controls;

public sealed class TreeLinesControl : Control
{
    public static readonly StyledProperty<int> DepthProperty =
        AvaloniaProperty.Register<TreeLinesControl, int>(nameof(Depth));

    public static readonly StyledProperty<bool> IsLastProperty =
        AvaloniaProperty.Register<TreeLinesControl, bool>(nameof(IsLast));

    public static readonly StyledProperty<IReadOnlyList<bool>?> AncestorContinuesProperty =
        AvaloniaProperty.Register<TreeLinesControl, IReadOnlyList<bool>?>(nameof(AncestorContinues));

    public static readonly StyledProperty<bool> HasExpandedChildrenProperty =
        AvaloniaProperty.Register<TreeLinesControl, bool>(nameof(HasExpandedChildren));

    public static readonly StyledProperty<bool> IsSpacerProperty =
        AvaloniaProperty.Register<TreeLinesControl, bool>(nameof(IsSpacer));

    public static readonly StyledProperty<string> ThemeKeyProperty =
        AvaloniaProperty.Register<TreeLinesControl, string>(nameof(ThemeKey), "empty");

    private const double RailStep = 9.0;
    private const double RailInset = 6.0;
    private static readonly Pen EmptyRailPen = new(new SolidColorBrush(Color.FromRgb(178, 195, 197)), 1);
    private static readonly Pen EmptyBranchPen = new(new SolidColorBrush(Color.FromRgb(139, 188, 192)), 1);
    private static readonly Pen DarkRailPen = new(new SolidColorBrush(Color.FromRgb(48, 67, 72)), 1);
    private static readonly Pen DarkBranchPen = new(new SolidColorBrush(Color.FromRgb(79, 139, 143)), 1);
    private static readonly Pen MatrixRailPen = new(new SolidColorBrush(Color.FromRgb(42, 118, 91)), 1);
    private static readonly Pen MatrixBranchPen = new(new SolidColorBrush(Color.FromRgb(101, 240, 178)), 1);

    public int Depth
    {
        get => GetValue(DepthProperty);
        set => SetValue(DepthProperty, value);
    }

    public bool IsLast
    {
        get => GetValue(IsLastProperty);
        set => SetValue(IsLastProperty, value);
    }

    public IReadOnlyList<bool>? AncestorContinues
    {
        get => GetValue(AncestorContinuesProperty);
        set => SetValue(AncestorContinuesProperty, value);
    }

    public bool HasExpandedChildren
    {
        get => GetValue(HasExpandedChildrenProperty);
        set => SetValue(HasExpandedChildrenProperty, value);
    }

    public bool IsSpacer
    {
        get => GetValue(IsSpacerProperty);
        set => SetValue(IsSpacerProperty, value);
    }

    public string ThemeKey
    {
        get => GetValue(ThemeKeyProperty);
        set => SetValue(ThemeKeyProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == DepthProperty
            || change.Property == IsLastProperty
            || change.Property == AncestorContinuesProperty
            || change.Property == HasExpandedChildrenProperty
            || change.Property == IsSpacerProperty
            || change.Property == ThemeKeyProperty)
        {
            InvalidateVisual();
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var centerY = Math.Floor(Bounds.Height * 0.5) + 0.5;
        var ancestors = AncestorContinues ?? [];

        DrawAncestorRails(context, ancestors, Depth, 0, Bounds.Height);

        if (IsSpacer)
        {
            DrawRail(context, Math.Max(0, Depth - 1), 0, Bounds.Height);
            return;
        }

        if (Depth <= 0)
        {
            DrawLineToContent(context, 0, centerY);
            if (HasExpandedChildren)
            {
                DrawRail(context, 0, centerY, Bounds.Height);
            }

            return;
        }

        var parentLevel = Depth - 1;
        DrawRail(context, parentLevel, 0, centerY);
        DrawLineToContent(context, parentLevel, centerY);

        if (!IsLast)
        {
            DrawRail(context, parentLevel, centerY, Bounds.Height);
        }

        if (HasExpandedChildren)
        {
            DrawRail(context, Depth, centerY, Bounds.Height);
        }
    }

    private void DrawAncestorRails(
        DrawingContext context,
        IReadOnlyList<bool> ancestors,
        int depth,
        double startY,
        double endY)
    {
        for (var level = 0; level < depth - 1; level++)
        {
            if (level < ancestors.Count && ancestors[level])
            {
                DrawRail(context, level, startY, endY);
            }
        }
    }

    private void DrawLineToContent(DrawingContext context, int level, double y)
    {
        var x = RailX(level);
        context.DrawLine(BranchPen, new Point(x, y), new Point(Math.Max(x, Bounds.Width - 2), y));
    }

    private void DrawRail(DrawingContext context, int level, double startY, double endY)
    {
        var x = RailX(level);
        context.DrawLine(RailPen, new Point(x, startY), new Point(x, endY));
    }

    private Pen RailPen => IsMatrixTheme(ThemeKey)
        ? MatrixRailPen
        : IsDarkRailTheme(ThemeKey)
            ? DarkRailPen
            : EmptyRailPen;

    private Pen BranchPen => IsMatrixTheme(ThemeKey)
        ? MatrixBranchPen
        : IsDarkRailTheme(ThemeKey)
            ? DarkBranchPen
            : EmptyBranchPen;

    private static bool IsMatrixTheme(string? themeKey)
    {
        return string.Equals(themeKey, "matrix", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDarkRailTheme(string? themeKey)
    {
        return string.Equals(themeKey, "dark", StringComparison.OrdinalIgnoreCase)
            || string.Equals(themeKey, "nocturne", StringComparison.OrdinalIgnoreCase)
            || string.Equals(themeKey, "onyx", StringComparison.OrdinalIgnoreCase)
            || string.Equals(themeKey, "smoke", StringComparison.OrdinalIgnoreCase)
            || string.Equals(themeKey, "carbon", StringComparison.OrdinalIgnoreCase)
            || string.Equals(themeKey, "obsidian", StringComparison.OrdinalIgnoreCase)
            || string.Equals(themeKey, "ash", StringComparison.OrdinalIgnoreCase)
            || string.Equals(themeKey, "graphene", StringComparison.OrdinalIgnoreCase)
            || string.Equals(themeKey, "ruby", StringComparison.OrdinalIgnoreCase)
            || string.Equals(themeKey, "amethyst", StringComparison.OrdinalIgnoreCase)
            || string.Equals(themeKey, "ember", StringComparison.OrdinalIgnoreCase)
            || string.Equals(themeKey, "cobalt", StringComparison.OrdinalIgnoreCase)
            || string.Equals(themeKey, "contrast", StringComparison.OrdinalIgnoreCase);
    }

    private static double RailX(int level)
    {
        return Math.Floor(RailInset + Math.Max(0, level) * RailStep) + 0.5;
    }
}
