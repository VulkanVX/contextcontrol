using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace ContextControl.Workbench.Controls;

/// <summary>Small, font-independent line icons with a shared 24-unit drawing grid.</summary>
public sealed class WorkspaceIcon : Control
{
    public static readonly StyledProperty<string> IconKeyProperty = AvaloniaProperty.Register<WorkspaceIcon, string>(nameof(IconKey), "chat");
    public static readonly StyledProperty<IBrush?> ForegroundProperty = AvaloniaProperty.Register<WorkspaceIcon, IBrush?>(nameof(Foreground), Brushes.LightGray);
    public string IconKey { get => GetValue(IconKeyProperty); set => SetValue(IconKeyProperty, value); }
    public IBrush? Foreground { get => GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }
    static WorkspaceIcon() => AffectsRender<WorkspaceIcon>(IconKeyProperty, ForegroundProperty);
    private static readonly IReadOnlyDictionary<string, Geometry> Shapes = new Dictionary<string, string>
    {
        ["code"] = "M8 6 L2 12 8 18 M16 6 L22 12 16 18 M14 3 L10 21",
        ["chat"] = "M5 4 H19 Q21 4 21 6 V15 Q21 17 19 17 H10 L5 21 V17 Q3 17 3 15 V6 Q3 4 5 4 Z M7 9 H17 M7 13 H14",
        ["graph"] = "M5 5 H9 V9 H5 Z M16 3 H20 V7 H16 Z M15 16 H19 V20 H15 Z M4 16 H8 V20 H4 Z M9 7 L16 5 M8 9 L15 16 M6 9 V16",
        ["browser"] = "M21 12 A9 9 0 1 1 3 12 A9 9 0 1 1 21 12 Z M3 12 H21 M12 3 C6 8 6 16 12 21 C18 16 18 8 12 3 Z",
        ["llms"] = "M7 7 H17 V17 H7 Z M9 10 H15 V14 H9 Z M9 3 V7 M15 3 V7 M9 17 V21 M15 17 V21 M3 9 H7 M3 15 H7 M17 9 H21 M17 15 H21",
        ["dependencies"] = "M8 3 V9 M16 3 V9 M5 9 H19 V12 Q19 18 12 18 Q5 18 5 12 Z M12 18 V22",
        ["stack"] = "M3 7 L12 3 21 7 12 11 Z M3 12 L12 16 21 12 M3 17 L12 21 21 17",
        ["skillbook"] = "M12 5 Q7 2 3 4 V19 Q7 17 12 20 Q17 17 21 19 V4 Q17 2 12 5 Z M12 5 V20 M6 8 H9 M6 12 H9 M15 8 H18 M15 12 H18",
        ["scanner"] = "M8 3 H3 V8 M16 3 H21 V8 M3 16 V21 H8 M21 16 V21 H16 M6 12 H18 M9 8 H15 M9 16 H15",
        ["reasoning"] = "M9 3 Q5 2 5 7 Q2 8 3 12 Q2 16 6 17 Q5 21 10 21 L12 19 V5 Q11 3 9 3 Z M15 3 Q19 2 19 7 Q22 8 21 12 Q22 16 18 17 Q19 21 14 21 L12 19 M5 7 L9 9 M3 12 H7 M6 17 L9 14 M19 7 L15 9 M21 12 H17 M18 17 L15 14",
        ["reasoning-unknown"] = "M21 12 A9 9 0 1 1 3 12 A9 9 0 1 1 21 12 Z M9 8 Q9 5 12 5 Q16 5 16 8 Q16 10 12 12 V14 M12 17 V18",
        ["reasoning-off"] = "M21 12 A9 9 0 1 1 3 12 A9 9 0 1 1 21 12 Z M7 12 H17"
    }.ToDictionary(pair => pair.Key, pair => (Geometry)StreamGeometry.Parse(pair.Value));
    public static bool HasIcon(string key) => Shapes.ContainsKey(key);
    public static void Draw(DrawingContext context, string key, Rect rect, IBrush? brush)
    {
        if (rect.Width <= 0 || rect.Height <= 0 || !Shapes.TryGetValue(key, out var shape)) return;
        using (context.PushTransform(Matrix.CreateScale(rect.Width / 24, rect.Height / 24) * Matrix.CreateTranslation(rect.X, rect.Y)))
            context.DrawGeometry(null, new Pen(brush, 1.6, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), shape);
    }
    public override void Render(DrawingContext context) => Draw(context, IconKey, new Rect(Bounds.Size), Foreground);
}
