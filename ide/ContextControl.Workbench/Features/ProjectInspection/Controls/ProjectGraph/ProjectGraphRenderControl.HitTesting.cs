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
    private GraphNode? HitTestGraphNode(Point screenPoint)
    {
        if (_layoutDirty)
        {
            EnsureLayout();
        }

        var world = ScreenToWorld(screenPoint);
        for (var index = _nodes.Count - 1; index >= 0; index--)
        {
            var node = _nodes[index];
            if (node.Bounds.Contains(world))
            {
                return node;
            }
        }

        return null;
    }

    private GraphNode? FindGraphNode(ProjectNodeViewModel node)
    {
        foreach (var graphNode in _nodes)
        {
            if (ReferenceEquals(graphNode.Node, node))
            {
                return graphNode;
            }
        }

        return null;
    }

    private bool IsSelectedNode(GraphNode node)
    {
        return node.Node is not null && ReferenceEquals(node.Node, SelectedNode);
    }

    private double TreeAspect
    {
        get
        {
            var viewportAspect = Bounds.Width > 1 && Bounds.Height > 1
                ? Bounds.Width / Bounds.Height
                : 1.75;
            return Math.Clamp(viewportAspect * 0.95, 1.05, 2.35);
        }
    }

    private Point ScreenToWorld(Point screen)
    {
        return new Point((screen.X - _pan.X) / _zoom, (screen.Y - _pan.Y) / _zoom);
    }

    private Rect ScreenToWorldViewport(Rect screen, double margin)
    {
        var left = (screen.Left - margin - _pan.X) / _zoom;
        var top = (screen.Top - margin - _pan.Y) / _zoom;
        var right = (screen.Right + margin - _pan.X) / _zoom;
        var bottom = (screen.Bottom + margin - _pan.Y) / _zoom;
        return new Rect(left, top, Math.Max(1, right - left), Math.Max(1, bottom - top));
    }

    private Point ToScreen(Point world)
    {
        return new Point(world.X * _zoom + _pan.X, world.Y * _zoom + _pan.Y);
    }

    private Rect ToScreen(Rect world)
    {
        return new Rect(
            world.X * _zoom + _pan.X,
            world.Y * _zoom + _pan.Y,
            world.Width * _zoom,
            world.Height * _zoom);
    }

    private void ZoomAt(Point screenPoint, double factor)
    {
        var world = ScreenToWorld(screenPoint);
        var nextZoom = Math.Clamp(_zoom * factor, MinZoom, MaxZoom);
        _zoom = nextZoom;
        _pan = new Vector(screenPoint.X - world.X * _zoom, screenPoint.Y - world.Y * _zoom);
    }

    private static bool RectMayIntersectWorld(Rect rect, Rect worldViewport)
    {
        return rect.Right >= worldViewport.Left
            && rect.Left <= worldViewport.Right
            && rect.Bottom >= worldViewport.Top
            && rect.Top <= worldViewport.Bottom;
    }

    private static bool SegmentMayIntersectWorld(Point start, Point end, Rect worldViewport)
    {
        var left = Math.Min(start.X, end.X);
        var right = Math.Max(start.X, end.X);
        var top = Math.Min(start.Y, end.Y);
        var bottom = Math.Max(start.Y, end.Y);
        return right >= worldViewport.Left
            && left <= worldViewport.Right
            && bottom >= worldViewport.Top
            && top <= worldViewport.Bottom;
    }

}
