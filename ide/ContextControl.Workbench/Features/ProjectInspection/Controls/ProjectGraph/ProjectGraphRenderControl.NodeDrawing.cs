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
    private void DrawNodes(DrawingContext context, Rect worldViewport, RenderResources resources)
    {
        foreach (var node in _nodes)
        {
            if (!RectMayIntersectWorld(node.Bounds, worldViewport))
            {
                continue;
            }

            var rect = ToScreen(node.Bounds);
            DrawNode(context, node, rect, resources);
        }
    }

    private void DrawNode(
        DrawingContext context,
        GraphNode node,
        Rect rect,
        RenderResources resources)
    {
        var isSelected = IsSelectedNode(node);
        var isHovered = ReferenceEquals(_hoveredNode, node);
        var isExternal = node.Node?.IsExternal == true;
        var background = NodeBackground(node, isSelected, isHovered, resources);
        var borderBrush = isSelected
            ? resources.AccentBorder
            : isExternal
                ? resources.ExternalText
                : node.Node?.IsFolder == true
                    ? FolderNodeBorderBrush(node, resources)
                    : resources.PanelBorder;
        var borderWidth = isSelected || isExternal
            ? 1.35
            : node.Node?.IsFolder == true
                ? 1.22
                : 1.0;

        context.DrawRectangle(
            background,
            CachedPen(borderBrush, Math.Clamp(borderWidth * _zoom, 0.75, 1.6)),
            rect,
            Math.Clamp(3 * _zoom, 0.75, 3),
            Math.Clamp(3 * _zoom, 0.75, 3));

        if (node.IsPinned && _zoom > 0.35)
        {
            var pinRadius = Math.Clamp(2.4 * _zoom, 1.2, 3.4);
            context.DrawEllipse(
                resources.Accent,
                null,
                new Point(rect.Right - 8 * _zoom, rect.Top + 8 * _zoom),
                pinRadius,
                pinRadius);
        }

        if (_zoom < 0.18 || rect.Width < 16 || rect.Height < 10)
        {
            return;
        }

        var padX = _isExportRendering ? Math.Max(3, 8 * _zoom) : Math.Clamp(8 * _zoom, 3, 8);
        var padTop = _isExportRendering ? Math.Max(2, 4 * _zoom) : Math.Clamp(4 * _zoom, 2, 4);
        var padBottom = _isExportRendering ? Math.Max(1.5, 3 * _zoom) : Math.Clamp(3 * _zoom, 1.5, 3);
        var textWidth = Math.Max(0, rect.Width - 2 * padX);
        var titleY = rect.Y + padTop;

        FormattedText? metaText = null;
        var metaY = 0.0;
        if (_zoom >= 0.46 && !node.IsAggregate && node.Node is not null)
        {
            var meta = BuildNodeMeta(node.Node);
            if (!string.IsNullOrWhiteSpace(meta))
            {
                var metaBrush = node.Node.IsExternal
                    ? resources.ExternalText
                    : node.Node.IsFolder
                        ? FolderNodeMetaBrush(node, resources)
                        : resources.MetricLoc;
                var metaSize = _isExportRendering ? Math.Max(4.0, 7.7 * _zoom) : Math.Clamp(7.7 * _zoom, 4.0, 8.2);
                metaText = GetFormattedText(meta, metaBrush, resources.CodeFont, resources.CodeFontKey, FontWeight.Black, FontStyle.Normal, metaSize);
                metaY = rect.Bottom - padBottom - metaText.Height;
            }
        }

        var titleBrush = NodeTextBrush(node, resources);
        var titleWeight = node.Node?.IsFolder == true || node.IsAggregate ? FontWeight.ExtraBold : FontWeight.SemiBold;
        var titleStyle = isExternal ? FontStyle.Italic : FontStyle.Normal;
        var titleFont = node.IsAggregate ? resources.CodeFont : resources.UiFont;
        var titleFontKey = node.IsAggregate ? resources.CodeFontKey : resources.UiFontKey;
        var titleBaseSize = node.Depth == 0 ? 11.5 : 10.0;
        var titleSize = _isExportRendering ? Math.Max(4.5, titleBaseSize * _zoom) : Math.Clamp(titleBaseSize * _zoom, 4.5, 12.0);
        var title = GetFormattedText(node.Title, titleBrush, titleFont, titleFontKey, titleWeight, titleStyle, titleSize);
        var titleBottom = metaText is null
            ? rect.Bottom - padBottom
            : Math.Max(titleY + 1, metaY - Math.Clamp(2 * _zoom, 0.8, 2));
        var titleClip = new Rect(rect.X + padX, titleY, textWidth, Math.Max(0, titleBottom - titleY));
        DrawClippedText(context, title, titleClip, new Point(titleClip.X, titleClip.Y));

        if (metaText is null || metaY <= titleY)
        {
            return;
        }

        var metaClip = new Rect(rect.X + padX, metaY, textWidth, Math.Max(0, rect.Bottom - padBottom - metaY));
        DrawClippedText(context, metaText, metaClip, new Point(metaClip.X, metaClip.Y));
    }

    private IBrush NodeBackground(GraphNode node, bool isSelected, bool isHovered, RenderResources resources)
    {
        if (!node.IsAggregate && node.Node?.IsFolder == true && node.Children.Count > 0)
        {
            return FolderNodeBackgroundBrush(node, resources);
        }

        if (isSelected)
        {
            return resources.DropdownSelected;
        }

        if (isHovered)
        {
            return resources.HistoryActive;
        }

        if (node.IsAggregate)
        {
            return resources.CommandBackground;
        }

        if (node.Node?.IsFolder == true)
        {
            return node.Children.Count == 0
                ? EmptyFolderBackgroundBrush(resources)
                : FolderNodeBackgroundBrush(node, resources);
        }

        return resources.CommandBackground;
    }

    private IBrush EmptyFolderBackgroundBrush(RenderResources resources)
    {
        return SolidBrush(resources.IsDark
            ? EmptyFolderDarkColor
            : EmptyFolderLightColor);
    }

    private IBrush FolderNodeBackgroundBrush(GraphNode node, RenderResources resources)
    {
        return SolidBrush(GenerationColor(node));
    }

    private IBrush FolderNodeBorderBrush(GraphNode node, RenderResources resources)
    {
        if (node.Children.Count == 0)
        {
            return SolidBrush(resources.IsDark
                ? EmptyFolderBorderDarkColor
                : EmptyFolderBorderLightColor);
        }

        var color = GenerationColor(node);
        return SolidBrush(resources.IsDark
            ? ScaleColor(color, 1.12)
            : ScaleColor(color, 0.70));
    }

    private IBrush RegionFillBrush(GraphNode node, RenderResources resources)
    {
        return SolidBrush(GenerationColor(node));
    }

    private IBrush RegionBorderBrush(GraphNode node, RenderResources resources)
    {
        var color = GenerationColor(node);
        return SolidBrush(resources.IsDark
            ? ScaleColor(color, 1.08)
            : ScaleColor(color, 0.68));
    }

    private IBrush SolidBrush(Color color)
    {
        var key = ((uint)color.A << 24)
            | ((uint)color.R << 16)
            | ((uint)color.G << 8)
            | color.B;
        if (_solidBrushCache.TryGetValue(key, out var brush))
        {
            return brush;
        }

        brush = new SolidColorBrush(color);
        _solidBrushCache[key] = brush;
        return brush;
    }

    private Pen CachedPen(IBrush brush, double thickness)
    {
        var key = new PenCacheKey(RuntimeHelpers.GetHashCode(brush), Math.Round(thickness * 64.0) / 64.0);
        if (_penCache.TryGetValue(key, out var pen))
        {
            return pen;
        }

        if (_penCache.Count > MaxPenCacheEntries)
        {
            _penCache.Clear();
        }

        pen = new Pen(brush, key.Thickness);
        _penCache[key] = pen;
        return pen;
    }

    private static int RegionPaletteIndex(GraphNode node, int paletteLength)
    {
        return Math.Max(0, node.Depth) % paletteLength;
    }

    private Color GenerationColor(GraphNode node)
    {
        var palette = ParseGenerationPalette(GenerationPalette);
        return palette[RegionPaletteIndex(node, palette.Length)];
    }

    private static Color[] ParseGenerationPalette(string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            var parsed = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(TryParseGraphColor)
                .Where(color => color.HasValue)
                .Select(color => color!.Value)
                .ToArray();
            if (parsed.Length > 0)
            {
                return parsed;
            }
        }

        return DefaultGenerationBasePalette;
    }

    private static Color? TryParseGraphColor(string value)
    {
        try
        {
            return Color.Parse(value);
        }
        catch
        {
            return null;
        }
    }

    private static Color ScaleColor(Color color, double scale)
    {
        return Color.FromArgb(
            color.A,
            (byte)Math.Clamp((int)Math.Round(color.R * scale), 0, 255),
            (byte)Math.Clamp((int)Math.Round(color.G * scale), 0, 255),
            (byte)Math.Clamp((int)Math.Round(color.B * scale), 0, 255));
    }

    private static Color WithAlpha(Color color, byte alpha)
    {
        return Color.FromArgb(alpha, color.R, color.G, color.B);
    }

    private static Color BlendColor(Color source, Color target, double targetAmount)
    {
        var sourceAmount = 1.0 - targetAmount;
        return Color.FromArgb(
            255,
            (byte)Math.Clamp((int)Math.Round(source.R * sourceAmount + target.R * targetAmount), 0, 255),
            (byte)Math.Clamp((int)Math.Round(source.G * sourceAmount + target.G * targetAmount), 0, 255),
            (byte)Math.Clamp((int)Math.Round(source.B * sourceAmount + target.B * targetAmount), 0, 255));
    }

    private IBrush NodeTextBrush(GraphNode node, RenderResources resources)
    {
        if (node.IsAggregate)
        {
            return resources.TextMuted;
        }

        if (node.Node?.IsExternal == true)
        {
            return resources.ExternalText;
        }

        return node.Node?.IsFolder == true
            ? FolderNodeTextBrush(node, resources)
            : resources.FileText;
    }

    private IBrush FolderNodeTextBrush(GraphNode node, RenderResources resources)
    {
        return node.Children.Count == 0
            ? resources.FolderText
            : SolidBrush(ContrastTextColor(GenerationColor(node)));
    }

    private IBrush FolderNodeMetaBrush(GraphNode node, RenderResources resources)
    {
        return node.Children.Count == 0
            ? resources.MetricFile
            : SolidBrush(ContrastTextColor(GenerationColor(node)));
    }

    private static Color ContrastTextColor(Color color)
    {
        var luminance = (0.2126 * SrgbToLinear(color.R)
            + 0.7152 * SrgbToLinear(color.G)
            + 0.0722 * SrgbToLinear(color.B));
        return luminance > 0.48
            ? Color.Parse("#172126")
            : Color.Parse("#FFFFFF");
    }

    private static double SrgbToLinear(byte value)
    {
        var channel = value / 255.0;
        return channel <= 0.03928
            ? channel / 12.92
            : Math.Pow((channel + 0.055) / 1.055, 2.4);
    }

    private static string BuildNodeMeta(ProjectNodeViewModel node)
    {
        if (node.IsExternal)
        {
            return "skip";
        }

        if (node.IsFolder)
        {
            return string.IsNullOrWhiteSpace(node.DirectoryStatsLabel)
                ? node.FileCountLabel
                : node.DirectoryStatsLabel;
        }

        return string.IsNullOrWhiteSpace(node.LocMetricLabel)
            ? node.VersionLabel
            : $"{node.VersionLabel}  {node.LocMetricLabel}";
    }

}
