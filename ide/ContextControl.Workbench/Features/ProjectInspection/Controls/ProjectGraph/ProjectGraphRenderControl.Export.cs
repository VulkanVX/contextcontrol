using System.Globalization;
using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using SkiaSharp;

namespace ContextControl.Workbench.Controls;

public sealed partial class ProjectGraphRenderControl
{
    public async Task ExportRasterAsync(Stream output, string format, int targetWidth, ProjectGraphExportDetails? projectDetails = null)
    {
        EnsureLayout();

        var width = Math.Clamp(targetWidth, 1024, 8192);
        var exportBounds = ExportBounds();
        var aspect = exportBounds.Height / Math.Max(1.0, exportBounds.Width);
        var graphHeight = Math.Clamp((int)Math.Round(width * aspect), 512, 8192);
        var resources = ResolveRenderResources();
        var detailsLayout = projectDetails is null
            ? null
            : BuildProjectDetailsLayout(projectDetails, width, resources, ExportVisualProjectDetailsItemLimit);
        var detailsGap = detailsLayout is null ? 0 : (int)Math.Clamp(width * 0.012, 10, 24);
        var detailsHeight = detailsLayout is null ? 0 : Math.Min(4096, (int)Math.Ceiling(detailsLayout.Height));
        var totalHeight = Math.Min(8192, graphHeight + detailsGap + detailsHeight);
        if (detailsLayout is not null && totalHeight < graphHeight + detailsGap + detailsHeight)
        {
            graphHeight = Math.Max(512, totalHeight - detailsGap - detailsHeight);
            totalHeight = graphHeight + detailsGap + detailsHeight;
        }

        var bitmap = new RenderTargetBitmap(new PixelSize(width, totalHeight), new Vector(96, 96));
        var previousZoom = _zoom;
        var previousPan = _pan;
        var previousFitRequested = _fitRequested;
        var previousViewportInitialized = _viewportInitialized;
        var previousIsExportRendering = _isExportRendering;
        try
        {
            var scale = Math.Min(width / exportBounds.Width, graphHeight / exportBounds.Height);
            _zoom = Math.Clamp(scale, MinZoom, MaxZoom * 24.0);
            _pan = new Vector(
                (width - exportBounds.Width * _zoom) * 0.5 - exportBounds.Left * _zoom,
                (graphHeight - exportBounds.Height * _zoom) * 0.5 - exportBounds.Top * _zoom);
            _fitRequested = false;
            _viewportInitialized = true;
            _isExportRendering = true;

            using var drawingContext = bitmap.CreateDrawingContext();
            drawingContext.DrawRectangle(resources.EditorSurface, null, new Rect(0, 0, width, totalHeight));
            DrawGraph(drawingContext, new Rect(0, 0, width, graphHeight), includeGrid: true);
            if (detailsLayout is not null)
            {
                DrawProjectDetailsFooter(
                    drawingContext,
                    new Rect(0, graphHeight + detailsGap, width, detailsHeight),
                    detailsLayout,
                    resources);
            }
        }
        finally
        {
            _zoom = previousZoom;
            _pan = previousPan;
            _fitRequested = previousFitRequested;
            _viewportInitialized = previousViewportInitialized;
            _isExportRendering = previousIsExportRendering;
        }

        var normalized = NormalizeExportFormat(format);
        if (normalized == "png")
        {
            bitmap.Save(output);
            await output.FlushAsync();
            return;
        }

        await using var pngStream = new MemoryStream();
        bitmap.Save(pngStream);
        pngStream.Position = 0;
        using var skBitmap = SKBitmap.Decode(pngStream);
        using var image = SKImage.FromBitmap(skBitmap);
        var skFormat = normalized == "webp"
            ? SKEncodedImageFormat.Webp
            : SKEncodedImageFormat.Jpeg;
        using var data = image.Encode(skFormat, normalized == "jpg" ? 92 : 90);
        data.SaveTo(output);
        await output.FlushAsync();
    }

    public string ExportGraphText(string format, ProjectGraphExportDetails? projectDetails = null)
    {
        EnsureLayout();
        var normalized = NormalizeExportFormat(format);
        return normalized switch
        {
            "svg" => ExportSvg(projectDetails),
            "dot" => ExportDot(projectDetails),
            "graphml" => ExportGraphMl(projectDetails),
            "mmd" => ExportMermaid(projectDetails),
            "json" => ExportJson(projectDetails),
            _ => ExportSvg(projectDetails)
        };
    }

    public static string NormalizeExportFormat(string? format)
    {
        return (format ?? "png").Trim().TrimStart('.').ToLowerInvariant() switch
        {
            "jpeg" => "jpg",
            "mermaid" => "mmd",
            "graphviz" => "dot",
            "graph-ml" => "graphml",
            "webp" => "webp",
            "jpg" => "jpg",
            "svg" => "svg",
            "dot" => "dot",
            "graphml" => "graphml",
            "mmd" => "mmd",
            "json" => "json",
            _ => "png"
        };
    }

    private string ExportSvg(ProjectGraphExportDetails? projectDetails)
    {
        var resources = ResolveRenderResources();
        var bounds = ExportBounds();
        var detailsLayout = projectDetails is null
            ? null
            : BuildProjectDetailsLayout(projectDetails, Math.Max(bounds.Width, 640.0), resources, ExportVisualProjectDetailsItemLimit);
        var detailsRect = detailsLayout is null
            ? (Rect?)null
            : new Rect(bounds.X, bounds.Bottom + 18.0, Math.Max(bounds.Width, 640.0), detailsLayout.Height);
        var svgBounds = detailsRect is { } footer
            ? UnionRects(bounds, footer)
            : bounds;
        var palette = ParseGenerationPalette(GenerationPalette);
        var builder = new StringBuilder(16 * 1024);
        builder.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"");
        AppendInvariant(builder, svgBounds.X);
        builder.Append(' ');
        AppendInvariant(builder, svgBounds.Y);
        builder.Append(' ');
        AppendInvariant(builder, svgBounds.Width);
        builder.Append(' ');
        AppendInvariant(builder, svgBounds.Height);
        builder.Append("\" width=\"");
        AppendInvariant(builder, svgBounds.Width);
        builder.Append("\" height=\"");
        AppendInvariant(builder, svgBounds.Height);
        builder.Append("\"><rect x=\"");
        AppendInvariant(builder, svgBounds.X);
        builder.Append("\" y=\"");
        AppendInvariant(builder, svgBounds.Y);
        builder.Append("\" width=\"");
        AppendInvariant(builder, svgBounds.Width);
        builder.Append("\" height=\"");
        AppendInvariant(builder, svgBounds.Height);
        builder.Append("\" fill=\"");
        builder.Append(ColorHex(BrushColor(resources.EditorSurface, Colors.Transparent)));
        builder.Append("\"/>");

        foreach (var node in _nodes.Where(node => node.RegionBounds.Width > 0 && node.RegionBounds.Height > 0))
        {
            var baseColor = palette[RegionPaletteIndex(node, palette.Length)];
            AppendSvgRect(builder, node.RegionBounds, baseColor, resources.IsDark ? ScaleColor(baseColor, 1.08) : ScaleColor(baseColor, 0.68), 1.0, 2.0);
        }

        foreach (var edge in _edges)
        {
            builder.Append("<path d=\"M");
            AppendInvariant(builder, edge.Parent.Bounds.Center.X);
            builder.Append(' ');
            AppendInvariant(builder, edge.Parent.Bounds.Bottom);
            builder.Append("L");
            AppendInvariant(builder, edge.Child.Bounds.Center.X);
            builder.Append(' ');
            AppendInvariant(builder, edge.Child.Bounds.Top);
            builder.Append("\" fill=\"none\" stroke=\"");
            builder.Append(ColorHex(BrushColor(resources.MetricFile, Colors.Gray)));
            builder.Append("\" stroke-width=\"1\" opacity=\".65\"/>");
        }

        foreach (var node in _nodes)
        {
            var hasGenerationFill = node.Node?.IsFolder == true && node.Children.Count > 0;
            var baseColor = hasGenerationFill
                ? palette[RegionPaletteIndex(node, palette.Length)]
                : BrushColor(resources.CommandBackground, Colors.Black);
            var fill = hasGenerationFill
                ? baseColor
                : node.Node?.IsFolder == true
                    ? resources.IsDark ? EmptyFolderDarkColor : EmptyFolderLightColor
                : BrushColor(resources.CommandBackground, Colors.Black);
            AppendSvgRect(builder, node.Bounds, fill, baseColor, 1.0, 3.0);
            var meta = node.Node is null || node.IsAggregate ? "" : BuildNodeMeta(node.Node);
            builder.Append("<text x=\"");
            AppendInvariant(builder, node.Bounds.X + 7);
            builder.Append("\" y=\"");
            AppendInvariant(builder, node.Bounds.Y + (string.IsNullOrWhiteSpace(meta) ? 16 : 13));
            builder.Append("\" font-family=\"");
            builder.Append(XmlEscape(resources.UiFontKey));
            builder.Append("\" font-size=\"9\" fill=\"");
            builder.Append(ColorHex(hasGenerationFill
                ? ContrastTextColor(baseColor)
                : BrushColor(node.Node?.IsFolder == true ? resources.TextPrimary : resources.FileText, Colors.White)));
            builder.Append("\">");
            builder.Append(XmlEscape(node.Title));
            builder.Append("</text>");
            if (!string.IsNullOrWhiteSpace(meta))
            {
                builder.Append("<text x=\"");
                AppendInvariant(builder, node.Bounds.X + 7);
                builder.Append("\" y=\"");
                AppendInvariant(builder, node.Bounds.Bottom - 6);
                builder.Append("\" font-family=\"");
                builder.Append(XmlEscape(resources.CodeFontKey));
                builder.Append("\" font-size=\"7\" fill=\"");
                builder.Append(ColorHex(hasGenerationFill
                    ? ContrastTextColor(baseColor)
                    : BrushColor(node.Node?.IsExternal == true ? resources.ExternalText : resources.MetricLoc, Colors.White)));
                builder.Append("\">");
                builder.Append(XmlEscape(meta));
                builder.Append("</text>");
            }
        }

        AppendSvgLegend(builder, bounds, palette);
        if (detailsLayout is not null && detailsRect is { } footerRect)
        {
            AppendProjectDetailsSvg(builder, footerRect, detailsLayout, resources);
        }

        builder.Append("</svg>");
        return builder.ToString();
    }

    private string ExportDot(ProjectGraphExportDetails? projectDetails)
    {
        var builder = new StringBuilder(8 * 1024);
        if (projectDetails is not null)
        {
            AppendPrefixedLines(builder, BuildProjectDetailsPlainText(projectDetails), "// ");
        }

        var nodeIndex = BuildNodeIndex();
        builder.AppendLine("digraph ProjectGraph {");
        builder.AppendLine("  graph [rankdir=TB, splines=ortho, nodesep=0.28, ranksep=0.36];");
        builder.AppendLine("  node [shape=box, style=\"rounded,filled\", fillcolor=\"#F4F6F4\", color=\"#9AA6AA\", fontname=\"Segoe UI\", fontsize=10];");
        builder.AppendLine("  edge [color=\"#78939B\", arrowsize=0.65];");
        for (var index = 0; index < _nodes.Count; index++)
        {
            var node = _nodes[index];
            builder.Append("  ")
                .Append(NodeId(index))
                .Append(" [label=\"")
                .Append(DotEscape(BuildExportNodeLabel(node)))
                .Append("\", kind=\"")
                .Append(DotEscape(ExportNodeKind(node)))
                .Append("\", path=\"")
                .Append(DotEscape(node.Node?.Path ?? ""))
                .Append("\", depth=\"")
                .Append(node.Depth.ToString(CultureInfo.InvariantCulture))
                .Append("\", tooltip=\"")
                .Append(DotEscape(BuildExportNodeTooltip(node)))
                .Append("\"");
            if (node.Node?.IsFolder == true && !node.IsAggregate)
            {
                builder.Append(", fillcolor=\"#E4F0EA\"");
            }
            else if (node.Node?.IsExternal == true)
            {
                builder.Append(", fillcolor=\"#F8ECE0\", color=\"#C9852B\"");
            }
            else if (node.IsAggregate)
            {
                builder.Append(", fillcolor=\"#ECEFF1\", color=\"#819096\", fontname=\"Consolas\"");
            }

            builder.AppendLine("];");
        }

        foreach (var edge in _edges)
        {
            builder.Append("  ")
                .Append(NodeId(nodeIndex[edge.Parent]))
                .Append(" -> ")
                .Append(NodeId(nodeIndex[edge.Child]))
                .AppendLine(";");
        }

        builder.AppendLine("}");

        return builder.ToString();
    }

    private string ExportGraphMl(ProjectGraphExportDetails? projectDetails)
    {
        var builder = new StringBuilder(12 * 1024);
        var nodeIndex = BuildNodeIndex();
        builder.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?><graphml xmlns=\"http://graphml.graphdrawing.org/xmlns\">");
        builder.Append("<key id=\"label\" for=\"node\" attr.name=\"label\" attr.type=\"string\"/>");
        builder.Append("<key id=\"name\" for=\"node\" attr.name=\"name\" attr.type=\"string\"/>");
        builder.Append("<key id=\"path\" for=\"node\" attr.name=\"path\" attr.type=\"string\"/>");
        builder.Append("<key id=\"kind\" for=\"node\" attr.name=\"kind\" attr.type=\"string\"/>");
        builder.Append("<key id=\"depth\" for=\"node\" attr.name=\"depth\" attr.type=\"int\"/>");
        builder.Append("<key id=\"parent\" for=\"node\" attr.name=\"parent\" attr.type=\"string\"/>");
        builder.Append("<key id=\"meta\" for=\"node\" attr.name=\"meta\" attr.type=\"string\"/>");
        builder.Append("<key id=\"isExternal\" for=\"node\" attr.name=\"isExternal\" attr.type=\"boolean\"/>");
        builder.Append("<key id=\"isAggregate\" for=\"node\" attr.name=\"isAggregate\" attr.type=\"boolean\"/>");
        builder.Append("<key id=\"x\" for=\"node\" attr.name=\"x\" attr.type=\"double\"/>");
        builder.Append("<key id=\"y\" for=\"node\" attr.name=\"y\" attr.type=\"double\"/>");
        builder.Append("<key id=\"width\" for=\"node\" attr.name=\"width\" attr.type=\"double\"/>");
        builder.Append("<key id=\"height\" for=\"node\" attr.name=\"height\" attr.type=\"double\"/>");
        if (projectDetails is not null)
        {
            builder.Append("<key id=\"projectDetails\" for=\"graph\" attr.name=\"projectDetails\" attr.type=\"string\"/>");
        }

        builder.Append("<graph id=\"ProjectGraph\" edgedefault=\"directed\">");
        if (projectDetails is not null)
        {
            builder.Append("<data key=\"projectDetails\">")
                .Append(XmlEscape(BuildProjectDetailsPlainText(projectDetails)))
                .Append("</data>");
        }

        for (var index = 0; index < _nodes.Count; index++)
        {
            var node = _nodes[index];
            builder.Append("<node id=\"").Append(NodeId(index)).Append("\">");
            AppendGraphMlData(builder, "label", BuildExportNodeLabel(node));
            AppendGraphMlData(builder, "name", node.Node?.Name ?? node.Title);
            AppendGraphMlData(builder, "path", node.Node?.Path ?? "");
            AppendGraphMlData(builder, "kind", ExportNodeKind(node));
            AppendGraphMlData(builder, "depth", node.Depth.ToString(CultureInfo.InvariantCulture));
            AppendGraphMlData(builder, "parent", node.Parent is null ? "" : NodeId(nodeIndex[node.Parent]));
            AppendGraphMlData(builder, "meta", node.Node is null || node.IsAggregate ? "" : BuildNodeMeta(node.Node));
            AppendGraphMlData(builder, "isExternal", (node.Node?.IsExternal == true).ToString().ToLowerInvariant());
            AppendGraphMlData(builder, "isAggregate", node.IsAggregate.ToString().ToLowerInvariant());
            AppendGraphMlData(builder, "x", Math.Round(node.Bounds.X, 2).ToString(CultureInfo.InvariantCulture));
            AppendGraphMlData(builder, "y", Math.Round(node.Bounds.Y, 2).ToString(CultureInfo.InvariantCulture));
            AppendGraphMlData(builder, "width", Math.Round(node.Bounds.Width, 2).ToString(CultureInfo.InvariantCulture));
            AppendGraphMlData(builder, "height", Math.Round(node.Bounds.Height, 2).ToString(CultureInfo.InvariantCulture));
            builder.Append("</node>");
        }

        var edgeIndex = 0;
        foreach (var edge in _edges)
        {
            builder.Append("<edge id=\"e").Append(edgeIndex++).Append("\" source=\"")
                .Append(NodeId(nodeIndex[edge.Parent]))
                .Append("\" target=\"")
                .Append(NodeId(nodeIndex[edge.Child]))
                .Append("\"/>");
        }

        builder.Append("</graph></graphml>");
        return builder.ToString();
    }

    private string ExportMermaid(ProjectGraphExportDetails? projectDetails)
    {
        var builder = new StringBuilder(8 * 1024);
        if (projectDetails is not null)
        {
            AppendPrefixedLines(builder, BuildProjectDetailsPlainText(projectDetails), "%% ");
        }

        var nodeIndex = BuildNodeIndex();
        builder.AppendLine("graph TD");
        for (var index = 0; index < _nodes.Count; index++)
        {
            builder.Append("  ")
                .Append(NodeId(index))
                .Append("[\"")
                .Append(MermaidEscape(BuildMermaidNodeLabel(_nodes[index])))
                .AppendLine("\"]");
        }

        foreach (var edge in _edges)
        {
            builder.Append("  ")
                .Append(NodeId(nodeIndex[edge.Parent]))
                .Append(" --> ")
                .Append(NodeId(nodeIndex[edge.Child]))
                .AppendLine();
        }

        if (_nodes.Any(node => node.Node?.IsFolder == true))
        {
            builder.AppendLine("  classDef folder fill:#E4F0EA,stroke:#7CA190,color:#1D3230;");
            foreach (var index in _nodes.Select((node, index) => (node, index)).Where(item => item.node.Node?.IsFolder == true).Select(item => item.index))
            {
                builder.Append("  class ").Append(NodeId(index)).AppendLine(" folder;");
            }
        }

        if (_nodes.Any(node => node.Node?.IsExternal == true))
        {
            builder.AppendLine("  classDef external fill:#F8ECE0,stroke:#C9852B,color:#7A4710;");
            foreach (var index in _nodes.Select((node, index) => (node, index)).Where(item => item.node.Node?.IsExternal == true).Select(item => item.index))
            {
                builder.Append("  class ").Append(NodeId(index)).AppendLine(" external;");
            }
        }

        return builder.ToString();
    }

    private string ExportJson(ProjectGraphExportDetails? projectDetails)
    {
        var nodeIds = _nodes.Select((node, index) => new
        {
            id = NodeId(index),
            label = BuildExportNodeLabel(node),
            name = node.Node?.Name ?? node.Title,
            depth = node.Depth,
            path = node.Node?.Path ?? "",
            kind = ExportNodeKind(node),
            parent = node.Parent is null ? null : NodeId(_nodes.IndexOf(node.Parent)),
            children = node.Children.Select(child => NodeId(_nodes.IndexOf(child))).ToArray(),
            meta = node.Node is null || node.IsAggregate ? "" : BuildNodeMeta(node.Node),
            isExternal = node.Node?.IsExternal == true,
            isAggregate = node.IsAggregate,
            x = Math.Round(node.Bounds.X, 2),
            y = Math.Round(node.Bounds.Y, 2),
            width = Math.Round(node.Bounds.Width, 2),
            height = Math.Round(node.Bounds.Height, 2)
        }).ToArray();
        var edgeIds = _edges.Select(edge => new
        {
            source = NodeId(_nodes.IndexOf(edge.Parent)),
            target = NodeId(_nodes.IndexOf(edge.Child))
        }).ToArray();
        var roots = _roots.Select(root => NodeId(_nodes.IndexOf(root))).ToArray();
        object export = projectDetails is null
            ? new { graph = new { directed = true, roots }, nodes = nodeIds, edges = edgeIds }
            : new { graph = new { directed = true, roots }, nodes = nodeIds, edges = edgeIds, projectDetails = BuildProjectDetailsJson(projectDetails) };
        return JsonSerializer.Serialize(export, new JsonSerializerOptions { WriteIndented = false });
    }

    private Rect ExportBounds()
    {
        var bounds = _contentBounds.Inflate(18);
        return new Rect(Math.Floor(bounds.X), Math.Floor(bounds.Y), Math.Ceiling(bounds.Width), Math.Ceiling(bounds.Height));
    }

    private static Rect UnionRects(Rect first, Rect second)
    {
        var left = Math.Min(first.Left, second.Left);
        var top = Math.Min(first.Top, second.Top);
        var right = Math.Max(first.Right, second.Right);
        var bottom = Math.Max(first.Bottom, second.Bottom);
        return new Rect(left, top, Math.Max(1.0, right - left), Math.Max(1.0, bottom - top));
    }

    private static void AppendPrefixedLines(StringBuilder builder, string text, string prefix)
    {
        foreach (var line in text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n'))
        {
            builder.Append(prefix).AppendLine(line);
        }
    }

    private Dictionary<GraphNode, int> BuildNodeIndex()
    {
        var index = new Dictionary<GraphNode, int>(_nodes.Count);
        for (var nodeIndex = 0; nodeIndex < _nodes.Count; nodeIndex++)
        {
            index[_nodes[nodeIndex]] = nodeIndex;
        }

        return index;
    }

    private static string NodeId(int index)
    {
        return "n" + index.ToString(CultureInfo.InvariantCulture);
    }

    private static string ExportNodeKind(GraphNode node)
    {
        if (node.IsAggregate)
        {
            return "aggregate";
        }

        return node.Node?.IsFolder == true ? "folder" : "file";
    }

    private static string BuildExportNodeLabel(GraphNode node)
    {
        if (node.Node is null || node.IsAggregate)
        {
            return node.Title;
        }

        var meta = BuildNodeMeta(node.Node);
        return string.IsNullOrWhiteSpace(meta)
            ? node.Title
            : $"{node.Title}\\n{meta}";
    }

    private static string BuildMermaidNodeLabel(GraphNode node)
    {
        if (node.Node is null || node.IsAggregate)
        {
            return node.Title;
        }

        var meta = BuildNodeMeta(node.Node);
        return string.IsNullOrWhiteSpace(meta)
            ? node.Title
            : $"{node.Title}<br/>{meta}";
    }

    private static string BuildExportNodeTooltip(GraphNode node)
    {
        if (node.Node is null)
        {
            return node.Title;
        }

        var path = string.IsNullOrWhiteSpace(node.Node.Path) ? node.Node.Name : node.Node.Path;
        var meta = BuildNodeMeta(node.Node);
        return string.IsNullOrWhiteSpace(meta)
            ? path
            : $"{path} | {meta}";
    }

    private static void AppendGraphMlData(StringBuilder builder, string key, string value)
    {
        builder.Append("<data key=\"")
            .Append(XmlEscape(key))
            .Append("\">")
            .Append(XmlEscape(value))
            .Append("</data>");
    }

    private static void AppendSvgRect(StringBuilder builder, Rect rect, Color fill, Color stroke, double strokeWidth, double radius)
    {
        builder.Append("<rect x=\"");
        AppendInvariant(builder, rect.X);
        builder.Append("\" y=\"");
        AppendInvariant(builder, rect.Y);
        builder.Append("\" width=\"");
        AppendInvariant(builder, rect.Width);
        builder.Append("\" height=\"");
        AppendInvariant(builder, rect.Height);
        builder.Append("\" rx=\"");
        AppendInvariant(builder, radius);
        builder.Append("\" fill=\"");
        builder.Append(ColorHex(fill));
        builder.Append("\"");
        if (fill.A < 255)
        {
            builder.Append(" fill-opacity=\"");
            AppendInvariant(builder, fill.A / 255.0);
            builder.Append("\"");
        }

        builder.Append(" stroke=\"");
        builder.Append(ColorHex(stroke));
        builder.Append("\" stroke-width=\"");
        AppendInvariant(builder, strokeWidth);
        builder.Append("\"/>");
    }

    private static void AppendSvgLegend(StringBuilder builder, Rect bounds, IReadOnlyList<Color> palette)
    {
        var x = bounds.X + 12;
        var y = bounds.Y + 12;
        for (var index = 0; index < palette.Count; index++)
        {
            builder.Append("<rect x=\"");
            AppendInvariant(builder, x + index * 38);
            builder.Append("\" y=\"");
            AppendInvariant(builder, y);
            builder.Append("\" width=\"10\" height=\"10\" rx=\"2\" fill=\"");
            builder.Append(ColorHex(palette[index]));
            builder.Append("\"/><text x=\"");
            AppendInvariant(builder, x + 14 + index * 38);
            builder.Append("\" y=\"");
            AppendInvariant(builder, y + 9);
            builder.Append("\" font-family=\"Consolas\" font-size=\"8\" fill=\"");
            builder.Append(ColorHex(palette[index]));
            builder.Append("\">").Append(index + 1).Append("</text>");
        }
    }

    private static Color BrushColor(IBrush brush, Color fallback)
    {
        return brush is ISolidColorBrush solid ? solid.Color : fallback;
    }

    private static string ColorHex(Color color)
    {
        return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    }

    private static void AppendInvariant(StringBuilder builder, double value)
    {
        builder.Append(value.ToString("0.##", CultureInfo.InvariantCulture));
    }

    private static string XmlEscape(string value)
    {
        return value.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);
    }

    private static string DotEscape(string value)
    {
        return value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
    }

    private static string MermaidEscape(string value)
    {
        return value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\r\n", "<br/>", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Replace("\n", "<br/>", StringComparison.Ordinal)
            .Replace("[", "(", StringComparison.Ordinal)
            .Replace("]", ")", StringComparison.Ordinal);
    }
}
