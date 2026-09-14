// CC-DESC: Rendering and hit-testing for the project tree surface.

using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using ContextControl.Workbench.ViewModels;

namespace ContextControl.Workbench.Controls;

public sealed partial class ProjectTreeRenderControl
{
    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var viewportTop = Math.Max(0.0, _scrollViewer?.Offset.Y ?? 0.0);
        var viewportHeight = _scrollViewer?.Viewport.Height ?? Math.Min(Bounds.Height, 800.0);
        if (!double.IsFinite(viewportHeight) || viewportHeight <= 0.0)
        {
            viewportHeight = Math.Min(Bounds.Height, 800.0);
        }

        context.DrawRectangle(TransparentBrush, null, new Rect(0, viewportTop, Bounds.Width, viewportHeight));

        var items = Items;
        if (items is null || items.Count == 0 || _rowTops.Length <= 1)
        {
            return;
        }

        var startY = Math.Max(0.0, viewportTop - ViewportOverscan);
        var endY = Math.Min(_totalHeight, viewportTop + viewportHeight + ViewportOverscan);
        var startIndex = FindRowIndexAtOrAfter(startY);
        if (startIndex < 0)
        {
            return;
        }

        var uiFontFamily = ResolveFontFamily(UiFontFamily, Resource("UiFontFamily", DefaultUiFontFamily));
        var codeFontFamily = ResolveFontFamily(CodeFontFamily, Resource("CodeFontFamily", DefaultCodeFontFamily));
        var width = Bounds.Width;
        for (var index = startIndex; index < items.Count; index++)
        {
            var rowTop = _rowTops[index];
            if (rowTop > endY)
            {
                break;
            }

            var rowBottom = _rowTops[index + 1];
            if (rowBottom < startY)
            {
                continue;
            }

            DrawRow(context, items[index], index, rowTop, rowBottom - rowTop, width, uiFontFamily, codeFontFamily);
        }
    }

    public ProjectTreeHitTestResult HitTestRow(Point point)
    {
        var items = Items;
        if (items is null
            || items.Count == 0
            || point.X < 0
            || point.Y < 0
            || point.X > Bounds.Width
            || point.Y > _totalHeight)
        {
            return default;
        }

        var index = FindRowIndexAtOrAfter(point.Y);
        if (index < 0 || index >= items.Count)
        {
            return default;
        }

        if (point.Y < _rowTops[index] || point.Y >= _rowTops[index + 1])
        {
            return default;
        }

        var row = items[index];
        if (!row.HasNode)
        {
            return new ProjectTreeHitTestResult(row, ProjectTreeHitKind.None, index);
        }

        var rowTop = _rowTops[index];
        var rowHeight = _rowTops[index + 1] - rowTop;
        if (row.ShowDisclosure && ToggleRect(row, rowTop, rowHeight).Contains(point))
        {
            return new ProjectTreeHitTestResult(row, ProjectTreeHitKind.Toggle, index);
        }

        if (row.CanIncludeExternal && IncludeRect(rowTop, rowHeight, Bounds.Width).Contains(point))
        {
            return new ProjectTreeHitTestResult(row, ProjectTreeHitKind.Include, index);
        }

        return new ProjectTreeHitTestResult(row, ProjectTreeHitKind.Row, index);
    }

    private void DrawRow(
        DrawingContext context,
        TreeRowViewModel row,
        int index,
        double rowTop,
        double rowHeight,
        double width,
        FontFamily uiFontFamily,
        FontFamily codeFontFamily)
    {
        if (!row.HasNode)
        {
            return;
        }

        var rowRect = new Rect(0, rowTop, width, rowHeight);
        DrawRowHighlights(context, row, rowRect);
        if (!row.IsTopLocRow)
        {
            DrawTreeLines(context, row, rowTop, rowHeight);
            DrawToggle(context, row, index, rowTop, rowHeight);
        }

        var detailLeft = DrawRightDetails(context, row, index, rowTop, rowHeight, width, codeFontFamily);
        DrawName(context, row, rowTop, rowHeight, detailLeft, uiFontFamily);
    }

    private void DrawRowHighlights(DrawingContext context, TreeRowViewModel row, Rect rowRect)
    {
        var borderBrush = Resource("CurrentRowBorderBrush", CurrentRowBorderFallbackBrush);
        if (row.IsSelected)
        {
            using (context.PushOpacity(0.94))
            {
                context.DrawRectangle(
                    Resource("DropdownSelectedBrush", SelectedRowFallbackBrush),
                    new Pen(borderBrush, 1),
                    rowRect,
                    4,
                    4);
            }
        }
        else if (row.ShowCurrentHighlight)
        {
            context.DrawRectangle(
                Resource("CurrentRowBrush", CurrentRowFallbackBrush),
                new Pen(borderBrush, 1),
                rowRect,
                4,
                4);
        }

        if (row.IsRegularFolder)
        {
            var x = ContentLeft + row.RailWidth;
            context.DrawRectangle(
                Resource("DirectoryHighlightBrush", DirectoryHighlightFallbackBrush),
                null,
                new Rect(x, rowRect.Y + 1, Math.Max(0.0, rowRect.Width - x), Math.Max(0.0, rowRect.Height - 2)),
                4,
                4);
        }
    }

    private void DrawTreeLines(DrawingContext context, TreeRowViewModel row, double rowTop, double rowHeight)
    {
        var centerY = Math.Floor(rowTop + rowHeight * 0.5) + 0.5;
        var ancestors = row.AncestorContinues;
        for (var level = 0; level < row.Depth - 1; level++)
        {
            if (level < ancestors.Count && ancestors[level])
            {
                DrawRail(context, level, rowTop, rowTop + rowHeight);
            }
        }

        if (row.IsSpacer)
        {
            DrawRail(context, Math.Max(0, row.Depth - 1), rowTop, rowTop + rowHeight);
            return;
        }

        if (row.Depth <= 0)
        {
            DrawLineToContent(context, 0, centerY, row.RailWidth);
            if (row.HasExpandedChildren)
            {
                DrawRail(context, 0, centerY, rowTop + rowHeight);
            }

            return;
        }

        var parentLevel = row.Depth - 1;
        DrawRail(context, parentLevel, rowTop, centerY);
        DrawLineToContent(context, parentLevel, centerY, row.RailWidth);

        if (!row.IsLast)
        {
            DrawRail(context, parentLevel, centerY, rowTop + rowHeight);
        }

        if (row.HasExpandedChildren)
        {
            DrawRail(context, row.Depth, centerY, rowTop + rowHeight);
        }
    }

    private void DrawToggle(DrawingContext context, TreeRowViewModel row, int index, double rowTop, double rowHeight)
    {
        if (!row.ShowDisclosure)
        {
            return;
        }

        var isHovered = _hoveredIndex == index && _hoveredKind == ProjectTreeHitKind.Toggle;
        var rect = ToggleRect(row, rowTop, rowHeight);
        context.DrawRectangle(
            isHovered ? Resource("HistoryActiveBrush", HistoryActiveFallbackBrush) : Resource("PanelBackgroundBrush", PanelFallbackBrush),
            new Pen(Resource("CommandBorderBrush", CommandBorderFallbackBrush), 1),
            rect,
            ToggleCornerRadius,
            ToggleCornerRadius);

        var arrowX = rect.X + (rect.Width - ArrowSize) * 0.5;
        var arrowY = rect.Y + (rect.Height - ArrowSize) * 0.5;
        using (context.PushTransform(Matrix.CreateTranslation(arrowX, arrowY)))
        {
            context.DrawGeometry(ArrowBrush, null, GetArrowGeometry(row.ArrowAngle));
        }
    }

    private double DrawRightDetails(
        DrawingContext context,
        TreeRowViewModel row,
        int index,
        double rowTop,
        double rowHeight,
        double width,
        FontFamily codeFontFamily)
    {
        var right = Math.Max(ContentLeft, width - RightInset);
        if (row.CanIncludeExternal)
        {
            DrawIncludeButton(context, index, rowTop, rowHeight, width, codeFontFamily);
            return Math.Max(ContentLeft, right - IncludeWidth - DetailSpacing);
        }

        if (row.ShowVersionLabel)
        {
            right = DrawRightText(
                context,
                row.VersionLabel,
                ThemeAdaptVersionColor
                    ? Resource("TextMutedBrush", TextMutedFallbackBrush)
                    : Resource("TreeVersionFixedBrush", VersionFixedFallbackBrush),
                codeFontFamily,
                FontWeight.Black,
                FontStyle.Normal,
                10.0,
                28.0,
                right,
                rowTop,
                rowHeight);
        }

        if (row.ShowNodeTypeLabel)
        {
            right = DrawRightText(
                context,
                row.NodeTypeLabel,
                row.IsExternal ? Resource("SkipTextBrush", SkipTextFallbackBrush) : Resource("NodeTextBrush", NodeTextFallbackBrush),
                codeFontFamily,
                FontWeight.Black,
                FontStyle.Normal,
                10.0,
                52.0,
                right,
                rowTop,
                rowHeight);
        }

        if (ShowFileDetails || row.IsTopLocRow)
        {
            if (row.ShowLocMetricLabel)
            {
                right = DrawRightText(
                    context,
                    row.LocMetricLabel,
                    ThemeAdaptLocColor
                        ? Resource("MetricLocBrush", MetricLocFallbackBrush)
                        : Resource("TreeLocFixedBrush", MetricLocFixedFallbackBrush),
                    codeFontFamily,
                    FontWeight.Black,
                    FontStyle.Normal,
                    10.0,
                    62.0,
                    right,
                    rowTop,
                    rowHeight);
            }

            if (row.ShowFileCountLabel)
            {
                right = DrawRightText(
                    context,
                    row.FileCountLabel,
                    ThemeAdaptFileCountColor
                        ? Resource("MetricFileBrush", MetricFileFallbackBrush)
                        : Resource("TreeFileFixedBrush", MetricFileFixedFallbackBrush),
                    codeFontFamily,
                    FontWeight.Black,
                    FontStyle.Normal,
                    10.0,
                    34.0,
                    right,
                    rowTop,
                    rowHeight);
            }
        }

        return right;
    }

    private double DrawRightText(
        DrawingContext context,
        string text,
        IBrush brush,
        FontFamily fontFamily,
        FontWeight weight,
        FontStyle style,
        double fontSize,
        double minWidth,
        double right,
        double rowTop,
        double rowHeight)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return right;
        }

        var formatted = GetFormattedText(text, brush, fontFamily, weight, style, fontSize);
        var slotWidth = Math.Max(minWidth, formatted.Width);
        var slotLeft = Math.Max(ContentLeft, right - slotWidth);
        var textX = Math.Max(slotLeft, right - formatted.Width);
        DrawClippedText(context, formatted, new Rect(slotLeft, rowTop, slotWidth, rowHeight), new Point(textX, CenteredTextY(rowTop, rowHeight, formatted.Height)));
        return slotLeft - DetailSpacing;
    }

    private void DrawName(
        DrawingContext context,
        TreeRowViewModel row,
        double rowTop,
        double rowHeight,
        double detailLeft,
        FontFamily uiFontFamily)
    {
        if (string.IsNullOrWhiteSpace(row.DisplayName))
        {
            return;
        }

        var nameX = ContentLeft + row.RailWidth + NameLeftMargin;
        var nameWidth = Math.Max(0.0, detailLeft - nameX - DetailSpacing);
        if (nameWidth <= 1.0)
        {
            return;
        }

        var brush = row.IsExternal
            ? Resource("ExternalTextBrush", ExternalTextFallbackBrush)
            : row.IsFolder
                ? Resource("FolderTextBrush", FolderTextFallbackBrush)
                : Resource("FileTextBrush", FileTextFallbackBrush);
        var weight = row.IsFolder ? FontWeight.ExtraBold : FontWeight.SemiBold;
        var style = row.IsExternal ? FontStyle.Italic : FontStyle.Normal;
        var formatted = GetFormattedText(row.DisplayName, brush, uiFontFamily, weight, style, 9.5);
        DrawClippedText(context, formatted, new Rect(nameX, rowTop, nameWidth, rowHeight), new Point(nameX, CenteredTextY(rowTop, rowHeight, formatted.Height)));
    }

    private void DrawIncludeButton(
        DrawingContext context,
        int index,
        double rowTop,
        double rowHeight,
        double width,
        FontFamily codeFontFamily)
    {
        var rect = IncludeRect(rowTop, rowHeight, width);
        var isHovered = _hoveredIndex == index && _hoveredKind == ProjectTreeHitKind.Include;
        var background = isHovered
            ? Resource("CommandPrimaryBackgroundBrush", CommandPrimaryBackgroundFallbackBrush)
            : Resource("IncludeBackgroundBrush", IncludeBackgroundFallbackBrush);
        var border = isHovered
            ? Resource("AccentBorderBrush", AccentBorderFallbackBrush)
            : Resource("IncludeBorderBrush", IncludeBorderFallbackBrush);
        var foreground = isHovered
            ? Resource("AccentBrush", AccentFallbackBrush)
            : Resource("IncludeTextBrush", IncludeTextFallbackBrush);

        context.DrawRectangle(background, new Pen(border, 1), rect, IncludeCornerRadius, IncludeCornerRadius);

        var label = isHovered ? "include" : "skip";
        var formatted = GetFormattedText(label, foreground, codeFontFamily, FontWeight.Bold, FontStyle.Normal, 7.5);
        var point = new Point(
            rect.X + Math.Max(0.0, (rect.Width - formatted.Width) * 0.5),
            rect.Y + Math.Max(0.0, (rect.Height - formatted.Height) * 0.5));
        DrawClippedText(context, formatted, rect, point);
    }

    private void DrawLineToContent(DrawingContext context, int level, double y, double railWidth)
    {
        var x = RailX(level);
        context.DrawLine(BranchPen, new Point(x, y), new Point(Math.Max(x, ContentLeft + railWidth - 2), y));
    }

    private void DrawRail(DrawingContext context, int level, double startY, double endY)
    {
        var x = RailX(level);
        context.DrawLine(RailPen, new Point(x, startY), new Point(x, endY));
    }

    private void DrawClippedText(DrawingContext context, FormattedText formatted, Rect clip, Point point)
    {
        if (clip.Width <= 0 || clip.Height <= 0)
        {
            return;
        }

        using (context.PushClip(clip))
        {
            context.DrawText(formatted, point);
        }
    }

}
