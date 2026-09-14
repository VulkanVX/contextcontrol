using System.Collections.Specialized;
using System.Globalization;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using ContextControl.Workbench.ViewModels;

namespace ContextControl.Workbench.Controls;
public sealed partial class ProjectGraphRenderControl
{
    private readonly record struct GraphPackItem(GraphNode Node, Point Offset);

    private readonly record struct SizedGraphNode(GraphNode Node, Size Size);

    private readonly record struct EdgeRoute(
        GraphNode Parent,
        Rect Bounds,
        Rect ClipBounds,
        int SegmentStart,
        int SegmentCount);

    private readonly record struct EdgeSegment(Point Start, Point End, bool IsRail);

    private readonly record struct RenderResources(
        IBrush EditorSurface,
        IBrush PanelBorder,
        IBrush CommandBackground,
        IBrush HistoryActive,
        IBrush DropdownSelected,
        IBrush TextPrimary,
        IBrush TextMuted,
        IBrush FolderText,
        IBrush FileText,
        IBrush ExternalText,
        IBrush Accent,
        IBrush AccentBorder,
        IBrush MetricFile,
        IBrush MetricLoc,
        FontFamily UiFont,
        string UiFontKey,
        FontFamily CodeFont,
        string CodeFontKey,
        bool IsDark);

    private sealed class GraphPackPlan(IReadOnlyList<GraphPackItem> items, double width, double height)
    {
        public IReadOnlyList<GraphPackItem> Items { get; } = items;
        public double Width { get; } = width;
        public double Height { get; } = height;
    }

    private sealed class ChildRouteRow(double top)
    {
        public double Top { get; } = top;
        public double BusY { get; private set; }
        public double Left { get; private set; } = double.MaxValue;
        public double Right { get; private set; } = double.MinValue;
        public List<GraphNode> Children { get; } = [];

        public void Add(GraphNode child)
        {
            Children.Add(child);
            Left = Math.Min(Left, child.Bounds.Center.X);
            Right = Math.Max(Right, child.Bounds.Center.X);
            BusY = Top - 5.0;
        }
    }

    private readonly record struct GraphEdge(GraphNode Parent, GraphNode Child);

    private readonly record struct CountResult(int Count, bool Capped);

    private readonly record struct TextCacheKey(
        string Text,
        int BrushId,
        string FontFamily,
        FontWeight Weight,
        FontStyle Style,
        double FontSize);

    private readonly record struct PenCacheKey(int BrushId, double Thickness);

    private sealed class GraphNode
    {
        public GraphNode(ProjectNodeViewModel node, GraphNode? parent, int depth)
        {
            Node = node;
            Parent = parent;
            Depth = depth;
            Key = BuildNodeKey(node, parent);
            Title = node.Depth == 0 && node.IsFolder ? node.Name : node.DisplayName;
        }

        private GraphNode(GraphNode parent, int depth, int omittedCount, bool omittedCapped)
        {
            Parent = parent;
            Depth = depth;
            Key = $"{parent.Key}|more|{parent.Children.Count}";
            IsAggregate = true;
            Title = omittedCapped
                ? $"+{omittedCount:N0}+ more"
                : $"+{omittedCount:N0} more";
        }

        public ProjectNodeViewModel? Node { get; }
        public GraphNode? Parent { get; }
        public List<GraphNode> Children { get; } = [];
        public int Depth { get; }
        public string Key { get; }
        public string Title { get; }
        public bool IsAggregate { get; }
        public bool IsPinned { get; set; }
        public double Weight { get; set; }
        public Rect Bounds { get; set; }
        public Rect RegionBounds { get; set; }
        public Size TreeSize { get; set; }
        public GraphPackPlan? TreePlan { get; set; }
        public double RootLabelOffsetY { get; set; }

        public static GraphNode CreateAggregate(GraphNode parent, int depth, int omittedCount, bool omittedCapped)
        {
            return new GraphNode(parent, depth, omittedCount, omittedCapped);
        }

        private static string BuildNodeKey(ProjectNodeViewModel node, GraphNode? parent)
        {
            if (!string.IsNullOrWhiteSpace(node.Path))
            {
                return $"{RootKey(parent)}|{node.Path}";
            }

            return $"{RootKey(parent)}|{node.Name}";
        }

        private static string RootKey(GraphNode? parent)
        {
            while (parent?.Parent is not null)
            {
                parent = parent.Parent;
            }

            return parent?.Node?.Name ?? "root";
        }
    }
}
