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
    private void DrawGraph(DrawingContext context, Rect viewport, bool includeGrid)
    {
        var worldViewport = ScreenToWorldViewport(viewport, 100);
        var resources = ResolveRenderResources();
        context.DrawRectangle(resources.EditorSurface, null, viewport);

        if (_nodes.Count == 0)
        {
            DrawEmptyState(context, resources, viewport);
            return;
        }

        if (includeGrid)
        {
            DrawGrid(context, viewport, resources);
        }

        DrawRegions(context, worldViewport, resources);
        DrawEdges(context, worldViewport, resources);
        DrawNodes(context, worldViewport, resources);
    }

    private void DrawEmptyState(DrawingContext context, RenderResources resources, Rect viewport)
    {
        var text = GetFormattedText(
            "Open a project to draw its architecture graph.",
            resources.TextMuted,
            resources.UiFont,
            resources.UiFontKey,
            FontWeight.SemiBold,
            FontStyle.Normal,
            12);
        var point = new Point(
            viewport.X + Math.Max(12, (viewport.Width - text.Width) * 0.5),
            viewport.Y + Math.Max(12, (viewport.Height - text.Height) * 0.5));
        context.DrawText(text, point);
    }

    private void DrawGrid(DrawingContext context, Rect viewport, RenderResources resources)
    {
        var pen = CachedPen(resources.PanelBorder, 0.55);
        var step = Math.Max(28, 56 * _zoom);
        var startX = _pan.X % step;
        var startY = _pan.Y % step;

        using (context.PushOpacity(resources.IsDark ? 0.20 : 0.35))
        {
            for (var x = startX; x < viewport.Width; x += step)
            {
                context.DrawLine(pen, new Point(x, 0), new Point(x, viewport.Height));
            }

            for (var y = startY; y < viewport.Height; y += step)
            {
                context.DrawLine(pen, new Point(0, y), new Point(viewport.Width, y));
            }
        }
    }

    private void DrawRegions(DrawingContext context, Rect worldViewport, RenderResources resources)
    {
        var activePen = CachedPen(resources.AccentBorder, Math.Clamp(1.2 * _zoom, 0.7, 1.45));

        foreach (var node in _nodes)
        {
            if (node.Parent is null
                || node.Children.Count == 0
                || node.RegionBounds.Width <= 0
                || node.RegionBounds.Height <= 0)
            {
                continue;
            }

            if (!RectMayIntersectWorld(node.RegionBounds, worldViewport))
            {
                continue;
            }

            var rect = ToScreen(node.RegionBounds);
            var selected = IsSelectedNode(node);
            var fillBrush = RegionFillBrush(node, resources);
            var borderPen = CachedPen(RegionBorderBrush(node, resources), Math.Clamp(0.95 * _zoom, 0.55, 1.2));
            var borderOpacity = selected ? 0.74 : 0.46;
            var radius = Math.Clamp(1.5 * _zoom, 0.5, 1.5);

            context.DrawRectangle(
                fillBrush,
                null,
                rect,
                radius,
                radius);

            using (context.PushOpacity(borderOpacity))
            {
                context.DrawRectangle(
                    null,
                    selected ? activePen : borderPen,
                    rect,
                    radius,
                    radius);
            }
        }
    }

    private void DrawEdges(DrawingContext context, Rect worldViewport, RenderResources resources)
    {
        if (_edgeRoutes.Count == 0)
        {
            return;
        }

        var edgePen = CachedPen(resources.MetricFile, Math.Clamp(1.02 * _zoom, 0.55, 1.25));
        var railPen = CachedPen(resources.MetricFile, Math.Clamp(1.18 * _zoom, 0.65, 1.45));
        var haloPen = CachedPen(resources.EditorSurface, Math.Clamp(2.8 * _zoom, 1.4, 3.2));

        using (context.PushOpacity(resources.IsDark ? 0.55 : 0.68))
        {
            foreach (var route in _edgeRoutes)
            {
                DrawEdgeRoute(context, route, haloPen, haloPen, worldViewport);
            }
        }

        using (context.PushOpacity(0.78))
        {
            foreach (var route in _edgeRoutes)
            {
                DrawEdgeRoute(context, route, railPen, edgePen, worldViewport);
            }
        }
    }

    private void DrawEdgeRoute(
        DrawingContext context,
        EdgeRoute route,
        Pen railPen,
        Pen branchPen,
        Rect worldViewport)
    {
        if (!RectMayIntersectWorld(route.Bounds, worldViewport))
        {
            return;
        }

        if (route.ClipBounds.Width > 0 && route.ClipBounds.Height > 0)
        {
            using (context.PushClip(ToScreen(route.ClipBounds)))
            {
                DrawEdgeRouteSegments(context, route, railPen, branchPen, worldViewport);
            }
            return;
        }

        DrawEdgeRouteSegments(context, route, railPen, branchPen, worldViewport);
    }

    private void DrawEdgeRouteSegments(
        DrawingContext context,
        EdgeRoute route,
        Pen railPen,
        Pen branchPen,
        Rect worldViewport)
    {
        var end = route.SegmentStart + route.SegmentCount;
        for (var index = route.SegmentStart; index < end; index++)
        {
            var segment = _edgeRouteSegments[index];
            if (!SegmentMayIntersectWorld(segment.Start, segment.End, worldViewport))
            {
                continue;
            }

            DrawWorldLine(context, segment.IsRail ? railPen : branchPen, segment.Start, segment.End);
        }
    }

}
