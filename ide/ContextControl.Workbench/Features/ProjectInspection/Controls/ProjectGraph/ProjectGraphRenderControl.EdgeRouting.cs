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
    private void RebuildEdgeRoutes()
    {
        _edgeRoutes.Clear();
        _edgeRouteSegments.Clear();
        _isBuildingEdgeRoutes = true;

        try
        {
            foreach (var parent in _nodes)
            {
                if (parent.Children.Count == 0)
                {
                    continue;
                }

                var segmentStart = _edgeRouteSegments.Count;
                _currentEdgeRouteHasSegments = false;
                _currentEdgeRouteBounds = default;

                DrawFanout(null!, parent, parent.Children, EdgeRouteRailPen, EdgeRouteBranchPen);

                var segmentCount = _edgeRouteSegments.Count - segmentStart;
                if (segmentCount <= 0 || !_currentEdgeRouteHasSegments)
                {
                    continue;
                }

                var clipBounds = parent.Parent is null
                    ? default
                    : parent.RegionBounds.Width > 0 && parent.RegionBounds.Height > 0
                    ? parent.RegionBounds
                    : default;
                _edgeRoutes.Add(new EdgeRoute(parent, _currentEdgeRouteBounds, clipBounds, segmentStart, segmentCount));
            }
        }
        finally
        {
            _isBuildingEdgeRoutes = false;
            _currentEdgeRouteHasSegments = false;
            _currentEdgeRouteBounds = default;
        }
    }

    private void AddEdgeRouteSegment(Point start, Point end, bool isRail)
    {
        if (Math.Abs(start.X - end.X) <= 0.05 && Math.Abs(start.Y - end.Y) <= 0.05)
        {
            return;
        }

        _edgeRouteSegments.Add(new EdgeSegment(start, end, isRail));

        var left = Math.Min(start.X, end.X);
        var top = Math.Min(start.Y, end.Y);
        var right = Math.Max(start.X, end.X);
        var bottom = Math.Max(start.Y, end.Y);
        var segmentBounds = new Rect(left, top, Math.Max(0.5, right - left), Math.Max(0.5, bottom - top));

        if (!_currentEdgeRouteHasSegments)
        {
            _currentEdgeRouteBounds = segmentBounds;
            _currentEdgeRouteHasSegments = true;
            return;
        }

        var routeLeft = Math.Min(_currentEdgeRouteBounds.Left, segmentBounds.Left);
        var routeTop = Math.Min(_currentEdgeRouteBounds.Top, segmentBounds.Top);
        var routeRight = Math.Max(_currentEdgeRouteBounds.Right, segmentBounds.Right);
        var routeBottom = Math.Max(_currentEdgeRouteBounds.Bottom, segmentBounds.Bottom);
        _currentEdgeRouteBounds = new Rect(
            routeLeft,
            routeTop,
            Math.Max(0.5, routeRight - routeLeft),
            Math.Max(0.5, routeBottom - routeTop));
    }

    private void DrawClippedFanout(
        DrawingContext context,
        GraphNode parent,
        IReadOnlyList<GraphNode> children,
        Pen railPen,
        Pen branchPen)
    {
        if (parent.RegionBounds.Width <= 0 || parent.RegionBounds.Height <= 0)
        {
            DrawFanout(context, parent, children, railPen, branchPen);
            return;
        }

        using (context.PushClip(ToScreen(parent.RegionBounds)))
        {
            DrawFanout(context, parent, children, railPen, branchPen);
        }
    }

    private void DrawFanout(
        DrawingContext context,
        GraphNode parent,
        IReadOnlyList<GraphNode> children,
        Pen railPen,
        Pen branchPen)
    {
        if (children.Count == 0)
        {
            return;
        }

        if (parent.Parent is null)
        {
            DrawRootFanout(context, parent, children, railPen, branchPen);
            return;
        }

        DrawFanoutRows(context, parent, children, railPen, branchPen, new Point(parent.Bounds.Center.X, parent.Bounds.Bottom));
    }

    private void DrawRootFanout(
        DrawingContext context,
        GraphNode parent,
        IReadOnlyList<GraphNode> children,
        Pen railPen,
        Pen branchPen)
    {
        var upperChildren = children
            .Where(child => child.Bounds.Top < parent.Bounds.Top)
            .ToArray();
        var lowerChildren = children
            .Where(child => child.Bounds.Top >= parent.Bounds.Bottom)
            .ToArray();

        if (upperChildren.Length > 0)
        {
            DrawRootFileCapConnector(context, parent, upperChildren, railPen);
        }

        if (lowerChildren.Length > 0)
        {
            DrawFanoutRows(context, parent, lowerChildren, railPen, branchPen, new Point(parent.Bounds.Center.X, parent.Bounds.Bottom));
        }
    }

    private void DrawRootFileCapConnector(
        DrawingContext context,
        GraphNode parent,
        IReadOnlyList<GraphNode> upperChildren,
        Pen railPen)
    {
        var blockBottom = upperChildren[0].Bounds.Bottom;
        for (var index = 1; index < upperChildren.Count; index++)
        {
            blockBottom = Math.Max(blockBottom, upperChildren[index].Bounds.Bottom);
        }

        var x = parent.Bounds.Center.X;
        DrawWorldLine(context, railPen, new Point(x, parent.Bounds.Top), new Point(x, blockBottom));
    }

    private void DrawFanoutRows(
        DrawingContext context,
        GraphNode parent,
        IReadOnlyList<GraphNode> children,
        Pen railPen,
        Pen branchPen,
        Point parentAnchor)
    {
        var rows = BuildChildRouteRows(children);
        if (rows.Count == 0)
        {
            return;
        }

        if (rows.Count == 1)
        {
            DrawChildRouteRow(context, rows[0], railPen, branchPen, parentAnchor.X, parentAnchor, connectParent: true);
            return;
        }

        var trunkX = GetChildRouteTrunkX(parent);
        var firstBusY = rows[0].BusY;
        DrawWorldLine(context, railPen, parentAnchor, new Point(parentAnchor.X, firstBusY));
        DrawWorldLine(context, railPen, new Point(parentAnchor.X, firstBusY), new Point(trunkX, firstBusY));

        var previousBusY = firstBusY;
        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var row = rows[rowIndex];
            if (rowIndex > 0)
            {
                DrawWorldLine(context, railPen, new Point(trunkX, previousBusY), new Point(trunkX, row.BusY));
            }

            DrawChildRouteRow(context, row, railPen, branchPen, trunkX, parentAnchor, connectParent: false);
            previousBusY = row.BusY;
        }
    }

    private void DrawChildRouteRow(
        DrawingContext context,
        ChildRouteRow row,
        Pen railPen,
        Pen branchPen,
        double entryX,
        Point parentAnchor,
        bool connectParent)
    {
        if (connectParent)
        {
            DrawWorldLine(context, railPen, parentAnchor, new Point(parentAnchor.X, row.BusY));
        }

        var railStart = Math.Min(entryX, row.Left);
        var railEnd = Math.Max(entryX, row.Right);
        DrawWorldLine(context, railPen, new Point(railStart, row.BusY), new Point(railEnd, row.BusY));

        foreach (var child in row.Children)
        {
            var childAnchor = new Point(child.Bounds.Center.X, child.Bounds.Top);
            DrawWorldLine(context, branchPen, new Point(childAnchor.X, row.BusY), childAnchor);
        }
    }

    private static List<ChildRouteRow> BuildChildRouteRows(IReadOnlyList<GraphNode> children)
    {
        var ordered = new List<GraphNode>(children);
        ordered.Sort(static (left, right) =>
        {
            var topCompare = left.Bounds.Top.CompareTo(right.Bounds.Top);
            return topCompare != 0
                ? topCompare
                : left.Bounds.Center.X.CompareTo(right.Bounds.Center.X);
        });

        var rows = new List<ChildRouteRow>();
        ChildRouteRow? current = null;
        foreach (var child in ordered)
        {
            if (current is null || Math.Abs(child.Bounds.Top - current.Top) > 1.0)
            {
                current = new ChildRouteRow(child.Bounds.Top);
                rows.Add(current);
            }

            current.Add(child);
        }

        return rows;
    }

    private static double GetChildRouteTrunkX(GraphNode parent)
    {
        if (parent.RegionBounds.Width > 0)
        {
            return parent.RegionBounds.Left + Math.Min(4.0, Math.Max(2.0, TreeRegionPadding * 0.5));
        }

        return parent.Bounds.Left - 4.0;
    }

    private void DrawWorldLine(DrawingContext context, Pen pen, Point start, Point end)
    {
        if (_isBuildingEdgeRoutes)
        {
            AddEdgeRouteSegment(start, end, ReferenceEquals(pen, EdgeRouteRailPen));
            return;
        }

        context.DrawLine(pen, ToScreen(start), ToScreen(end));
    }

}
