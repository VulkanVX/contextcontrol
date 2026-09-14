using System.Text;
using Avalonia;
using Avalonia.Media;
using ContextControl.Workbench.Services;

namespace ContextControl.Workbench.Controls;

public sealed record ProjectGraphExportDetails(
    string ProjectRoot,
    string Summary,
    string RuleSummary,
    IReadOnlyList<ProjectStackSection> Sections);

public sealed partial class ProjectGraphRenderControl
{
    private const int ExportVisualProjectDetailsItemLimit = 10;

    private static readonly string[] ExportProjectDetailsSectionOrder =
    [
        "Detected Stack",
        "Uses",
        "Unsupported Visible Types",
        "Autosetup Plan",
        "Already Allowed",
        "Languages",
        "Top File Types",
        "Manifests",
        "Already Counted LOC",
        "Skipped Samples"
    ];

    private static string BuildProjectDetailsPlainText(ProjectGraphExportDetails details)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Project Details");
        AppendProjectDetailsMetadata(builder, "Project", details.ProjectRoot);
        AppendProjectDetailsMetadata(builder, "Scan", details.Summary);
        AppendProjectDetailsMetadata(builder, "Rules", details.RuleSummary);

        foreach (var section in OrderedProjectDetailsSections(details, itemLimit: null))
        {
            builder.Append(section.Title)
                .Append(": ")
                .AppendLine(string.Join("; ", section.Items));
        }

        return builder.ToString().TrimEnd();
    }

    private static ProjectDetailsJson BuildProjectDetailsJson(ProjectGraphExportDetails details)
    {
        return new ProjectDetailsJson(
            details.ProjectRoot,
            details.Summary,
            details.RuleSummary,
            OrderedProjectDetailsSections(details, itemLimit: null)
                .Select(section => new ProjectDetailsSectionJson(section.Title, section.Items))
                .ToArray());
    }

    private static void AppendProjectDetailsMetadata(StringBuilder builder, string label, string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            builder.AppendLine($"{label}: {value.Trim()}");
        }
    }

    private static IReadOnlyList<ProjectStackSection> OrderedProjectDetailsSections(ProjectGraphExportDetails details, int? itemLimit)
    {
        var source = details.Sections
            .GroupBy(section => section.Title, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var ordered = new List<ProjectStackSection>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var title in ExportProjectDetailsSectionOrder)
        {
            seen.Add(title);
            ordered.Add(ProjectDetailsSectionOrEmpty(source, title, itemLimit));
        }

        foreach (var section in details.Sections.Where(section => seen.Add(section.Title)))
        {
            ordered.Add(new ProjectStackSection(section.Title, LimitProjectDetailsItems(section.Items, itemLimit)));
        }

        return ordered;
    }

    private static ProjectStackSection ProjectDetailsSectionOrEmpty(
        IReadOnlyDictionary<string, ProjectStackSection> source,
        string title,
        int? itemLimit)
    {
        return source.TryGetValue(title, out var section)
            ? new ProjectStackSection(title, LimitProjectDetailsItems(section.Items, itemLimit))
            : new ProjectStackSection(title, ["(none)"]);
    }

    private static IReadOnlyList<string> LimitProjectDetailsItems(IReadOnlyList<string> items, int? itemLimit)
    {
        var clean = items
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item.Trim())
            .ToArray();
        if (clean.Length == 0)
        {
            return ["(none)"];
        }

        if (itemLimit is not { } limit || clean.Length <= limit)
        {
            return clean;
        }

        return clean
            .Take(limit)
            .Concat([$"+{clean.Length - limit:N0} more"])
            .ToArray();
    }

    private ExportDetailsLayout BuildProjectDetailsLayout(
        ProjectGraphExportDetails details,
        double width,
        RenderResources resources,
        int itemLimit)
    {
        width = Math.Max(260.0, width);
        var scale = Math.Clamp(width / 1400.0, 0.85, 1.45);
        var padding = 14.0 * scale;
        var gap = 8.0 * scale;
        var contentWidth = Math.Max(1.0, width - padding * 2.0);
        var detailBlocks = BuildProjectDetailsBlocks(details, itemLimit);
        var columnCount = Math.Min(
            detailBlocks.Count,
            ResolveProjectDetailsColumnCount(contentWidth, gap, scale));
        var blockWidth = Math.Max(1.0, (contentWidth - gap * (columnCount - 1)) / columnCount);
        var runs = new List<ExportDetailsRun>();
        var boxes = new List<ExportDetailsBox>();
        var y = padding;

        var titleHeight = Math.Ceiling(15.0 * scale * 1.35);
        AddRun(runs, "Project Details", new Rect(padding, y, contentWidth, titleHeight), resources.UiFont, resources.UiFontKey, 15.0 * scale, FontWeight.ExtraBold, resources.TextPrimary);
        y += titleHeight + 5.0 * scale;

        var columnYs = Enumerable.Repeat(y, columnCount).ToArray();
        foreach (var block in detailBlocks)
        {
            var column = ShortestColumnIndex(columnYs);
            var x = padding + column * (blockWidth + gap);
            var rect = AddDetailsBlock(
                block.Title,
                block.Items,
                new Rect(x, columnYs[column], blockWidth, 1.0),
                boxes,
                runs,
                resources,
                scale,
                block.TextLimit);
            columnYs[column] = rect.Bottom + gap;
        }

        return new ExportDetailsLayout(boxes, runs, columnYs.Max() - gap + padding);
    }

    private static IReadOnlyList<ProjectDetailsBlock> BuildProjectDetailsBlocks(ProjectGraphExportDetails details, int itemLimit)
    {
        var blocks = new List<ProjectDetailsBlock>();
        AddMetadataBlock(blocks, "Project", details.ProjectRoot, 280);
        AddMetadataBlock(blocks, "Scan", details.Summary, 240);
        AddMetadataBlock(blocks, "Rules", details.RuleSummary, 240);

        foreach (var section in OrderedProjectDetailsSections(details, itemLimit))
        {
            blocks.Add(new ProjectDetailsBlock(section.Title, section.Items, 170));
        }

        return blocks.Count == 0
            ? [new ProjectDetailsBlock("Summary", ["No project details available."], 180)]
            : blocks;
    }

    private static void AddMetadataBlock(List<ProjectDetailsBlock> blocks, string title, string value, int textLimit)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            blocks.Add(new ProjectDetailsBlock(title, [value.Trim()], textLimit));
        }
    }

    private static int ResolveProjectDetailsColumnCount(double contentWidth, double gap, double scale)
    {
        var targetBlockWidth = 260.0 * scale;
        var count = (int)Math.Floor((contentWidth + gap) / (targetBlockWidth + gap));
        return Math.Clamp(count, 1, 10);
    }

    private void DrawProjectDetailsFooter(
        DrawingContext context,
        Rect rect,
        ExportDetailsLayout layout,
        RenderResources resources)
    {
        context.DrawRectangle(
            resources.CommandBackground,
            CachedPen(resources.PanelBorder, 1.0),
            rect,
            5.0,
            5.0);

        using (context.PushClip(rect))
        {
            foreach (var box in layout.Boxes)
            {
                var target = new Rect(rect.X + box.Rect.X, rect.Y + box.Rect.Y, box.Rect.Width, box.Rect.Height);
                context.DrawRectangle(box.Fill, CachedPen(box.Border, 1.0), target, box.Radius, box.Radius);
            }

            foreach (var run in layout.Runs)
            {
                var formatted = GetFormattedText(run.Text, run.Brush, run.Font, run.FontKey, run.Weight, FontStyle.Normal, run.FontSize);
                var target = new Rect(rect.X + run.Rect.X, rect.Y + run.Rect.Y, run.Rect.Width, run.Rect.Height);
                DrawClippedText(context, formatted, target, new Point(target.X, target.Y));
            }
        }
    }

    private void AppendProjectDetailsSvg(
        StringBuilder builder,
        Rect rect,
        ExportDetailsLayout layout,
        RenderResources resources)
    {
        builder.Append("<g id=\"project-details\">");
        AppendSvgRect(
            builder,
            rect,
            BrushColor(resources.CommandBackground, Colors.Transparent),
            BrushColor(resources.PanelBorder, Colors.Gray),
            1.0,
            5.0);

        foreach (var box in layout.Boxes)
        {
            AppendSvgRect(
                builder,
                new Rect(rect.X + box.Rect.X, rect.Y + box.Rect.Y, box.Rect.Width, box.Rect.Height),
                BrushColor(box.Fill, Colors.Transparent),
                BrushColor(box.Border, Colors.Gray),
                1.0,
                box.Radius);
        }

        foreach (var run in layout.Runs)
        {
            builder.Append("<text x=\"");
            AppendInvariant(builder, rect.X + run.Rect.X);
            builder.Append("\" y=\"");
            AppendInvariant(builder, rect.Y + run.Rect.Y + run.FontSize);
            builder.Append("\" font-family=\"");
            builder.Append(XmlEscape(run.FontKey));
            builder.Append("\" font-size=\"");
            AppendInvariant(builder, run.FontSize);
            builder.Append("\" font-weight=\"");
            builder.Append(SvgFontWeight(run.Weight));
            builder.Append("\" fill=\"");
            builder.Append(ColorHex(BrushColor(run.Brush, Colors.Black)));
            builder.Append("\">");
            builder.Append(XmlEscape(run.Text));
            builder.Append("</text>");
        }

        builder.Append("</g>");
    }

    private Rect AddDetailsBlock(
        string title,
        IReadOnlyList<string> items,
        Rect seed,
        List<ExportDetailsBox> boxes,
        List<ExportDetailsRun> runs,
        RenderResources resources,
        double scale,
        int textLimit)
    {
        var padding = 8.0 * scale;
        var titleSize = 9.8 * scale;
        var itemSize = 8.45 * scale;
        var titleHeight = Math.Ceiling(titleSize * 1.36);
        var itemLineHeight = Math.Ceiling(itemSize * 1.28);
        var y = seed.Y + padding;
        var contentX = seed.X + padding;
        var contentWidth = Math.Max(1.0, seed.Width - padding * 2.0);

        AddRun(runs, title, new Rect(contentX, y, contentWidth, titleHeight), resources.CodeFont, resources.CodeFontKey, titleSize, FontWeight.ExtraBold, resources.Accent);
        y += titleHeight + 3.0 * scale;

        foreach (var item in items)
        {
            foreach (var line in WrapProjectDetailsText(CleanProjectDetailText(item, textLimit), contentWidth, resources.TextPrimary, resources.CodeFont, resources.CodeFontKey, FontWeight.Normal, itemSize))
            {
                AddRun(runs, line, new Rect(contentX, y, contentWidth, itemLineHeight), resources.CodeFont, resources.CodeFontKey, itemSize, FontWeight.Normal, resources.TextPrimary);
                y += itemLineHeight;
            }

            y += 1.0 * scale;
        }

        var rect = new Rect(seed.X, seed.Y, seed.Width, Math.Max(34.0 * scale, y + padding - seed.Y));
        boxes.Add(new ExportDetailsBox(rect, resources.EditorSurface, resources.PanelBorder, 4.0 * scale));
        return rect;
    }

    private static void AddRun(
        List<ExportDetailsRun> runs,
        string text,
        Rect rect,
        FontFamily font,
        string fontKey,
        double fontSize,
        FontWeight weight,
        IBrush brush)
    {
        runs.Add(new ExportDetailsRun(text, rect, font, fontKey, fontSize, weight, brush));
    }

    private static int ShortestColumnIndex(IReadOnlyList<double> values)
    {
        var bestIndex = 0;
        var bestValue = values[0];
        for (var index = 1; index < values.Count; index++)
        {
            if (values[index] < bestValue)
            {
                bestValue = values[index];
                bestIndex = index;
            }
        }

        return bestIndex;
    }

    private IReadOnlyList<string> WrapProjectDetailsText(
        string text,
        double width,
        IBrush brush,
        FontFamily font,
        string fontKey,
        FontWeight weight,
        double fontSize)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [""];
        }

        var lines = new List<string>();
        var current = "";
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var candidate = current.Length == 0 ? word : $"{current} {word}";
            if (MeasureProjectDetailsText(candidate, brush, font, fontKey, weight, fontSize) <= width)
            {
                current = candidate;
                continue;
            }

            if (current.Length > 0)
            {
                lines.Add(current);
                current = "";
            }

            if (MeasureProjectDetailsText(word, brush, font, fontKey, weight, fontSize) <= width)
            {
                current = word;
                continue;
            }

            BreakProjectDetailsWord(word, width, brush, font, fontKey, weight, fontSize, lines);
        }

        if (current.Length > 0)
        {
            lines.Add(current);
        }

        return lines.Count == 0 ? [""] : lines;
    }

    private void BreakProjectDetailsWord(
        string word,
        double width,
        IBrush brush,
        FontFamily font,
        string fontKey,
        FontWeight weight,
        double fontSize,
        List<string> lines)
    {
        var remaining = word;
        while (remaining.Length > 0)
        {
            var lo = 1;
            var hi = remaining.Length;
            var best = 1;
            while (lo <= hi)
            {
                var mid = lo + (hi - lo) / 2;
                if (MeasureProjectDetailsText(remaining[..mid], brush, font, fontKey, weight, fontSize) <= width)
                {
                    best = mid;
                    lo = mid + 1;
                }
                else
                {
                    hi = mid - 1;
                }
            }

            lines.Add(remaining[..best]);
            remaining = remaining[best..];
        }
    }

    private double MeasureProjectDetailsText(
        string text,
        IBrush brush,
        FontFamily font,
        string fontKey,
        FontWeight weight,
        double fontSize)
    {
        return GetFormattedText(text, brush, font, fontKey, weight, FontStyle.Normal, fontSize).Width;
    }

    private static string CleanProjectDetailText(string? value, int maxLength)
    {
        var clean = (value ?? "")
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();
        return clean.Length <= maxLength ? clean : clean[..Math.Max(0, maxLength - 1)] + "...";
    }

    private static string SvgFontWeight(FontWeight weight)
    {
        var name = weight.ToString();
        return name is "Black" or "ExtraBlack" or "ExtraBold" or "Heavy"
            ? "800"
            : name is "Bold" or "DemiBold" or "SemiBold"
                ? "700"
                : "400";
    }

    private sealed record ProjectDetailsJson(
        string projectRoot,
        string summary,
        string ruleSummary,
        IReadOnlyList<ProjectDetailsSectionJson> sections);

    private sealed record ProjectDetailsSectionJson(string title, IReadOnlyList<string> items);

    private sealed record ProjectDetailsBlock(string Title, IReadOnlyList<string> Items, int TextLimit);

    private sealed record ExportDetailsLayout(IReadOnlyList<ExportDetailsBox> Boxes, IReadOnlyList<ExportDetailsRun> Runs, double Height);

    private sealed record ExportDetailsRun(
        string Text,
        Rect Rect,
        FontFamily Font,
        string FontKey,
        double FontSize,
        FontWeight Weight,
        IBrush Brush);

    private sealed record ExportDetailsBox(Rect Rect, IBrush Fill, IBrush Border, double Radius);
}
