// CC-DESC: Rendering and hit-testing for the local LLM catalog surface.

// CC-DESC: Draws the LLM catalogue as a fixed-row virtualized surface for fast scrolling.

using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using ContextControl.Workbench.ViewModels;

namespace ContextControl.Workbench.Controls;

public sealed partial class LocalLlmCatalogRenderControl
{
    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var viewportTop = Math.Max(0.0, _scrollViewer?.Offset.Y ?? 0.0);
        var viewportHeight = _scrollViewer?.Viewport.Height ?? Math.Min(Bounds.Height, 900.0);
        if (!double.IsFinite(viewportHeight) || viewportHeight <= 0.0)
        {
            viewportHeight = Math.Min(Bounds.Height, 900.0);
        }

        context.DrawRectangle(TransparentBrush, null, new Rect(0, viewportTop, Bounds.Width, viewportHeight));

        var items = Items;
        if (items is null || items.Count == 0)
        {
            DrawEmptyState(context, viewportTop, viewportHeight);
            return;
        }

        var uiFontFamily = ResolveFontFamily(UiFontFamily, Resource("UiFontFamily", DefaultUiFontFamily));
        var codeFontFamily = ResolveFontFamily(CodeFontFamily, Resource("CodeFontFamily", DefaultCodeFontFamily));
        var startIndex = Math.Max(0, (int)Math.Floor((viewportTop - ContentTop - ViewportOverscan) / RowPitch));
        var endIndex = Math.Min(
            items.Count - 1,
            (int)Math.Ceiling((viewportTop + viewportHeight + ViewportOverscan - ContentTop) / RowPitch));

        for (var index = startIndex; index <= endIndex; index++)
        {
            DrawModel(context, items[index], index, RowTop(index), Bounds.Width, uiFontFamily, codeFontFamily);
        }
    }

    public LocalLlmCatalogHitTestResult HitTestModel(Point point)
    {
        var items = Items;
        if (items is null
            || items.Count == 0
            || point.X < 0
            || point.Y < ContentTop
            || point.X > Bounds.Width
            || point.Y > TotalHeight)
        {
            return default;
        }

        var index = (int)Math.Floor((point.Y - ContentTop) / RowPitch);
        if (index < 0 || index >= items.Count)
        {
            return default;
        }

        var rowTop = RowTop(index);
        var card = CardRect(rowTop, Bounds.Width);
        if (!card.Contains(point))
        {
            return default;
        }

        var model = items[index];
        if (IconHitRect(rowTop, Bounds.Width).Contains(point))
        {
            return new LocalLlmCatalogHitTestResult(model, LocalLlmCatalogHitKind.Icon, index);
        }

        if (PullButtonRect(rowTop, Bounds.Width).Contains(point))
        {
            return new LocalLlmCatalogHitTestResult(model, LocalLlmCatalogHitKind.Pull, index);
        }

        if (MetricsRect(rowTop, Bounds.Width).Contains(point))
        {
            return new LocalLlmCatalogHitTestResult(model, LocalLlmCatalogHitKind.Metrics, index);
        }

        return new LocalLlmCatalogHitTestResult(model, LocalLlmCatalogHitKind.None, index);
    }

    private void DrawEmptyState(DrawingContext context, double viewportTop, double viewportHeight)
    {
        var uiFontFamily = ResolveFontFamily(UiFontFamily, Resource("UiFontFamily", DefaultUiFontFamily));
        var text = GetFormattedText("No models match the current filters.", Resource("TextMutedBrush", TextMutedFallbackBrush), uiFontFamily, FontWeight.SemiBold, FontStyle.Normal, 11.0);
        var x = Math.Max(8.0, (Bounds.Width - text.Width) * 0.5);
        var y = viewportTop + Math.Max(24.0, (viewportHeight - text.Height) * 0.35);
        DrawClippedText(context, text, new Rect(0, viewportTop, Bounds.Width, viewportHeight), new Point(x, y));
    }

    private void DrawModel(
        DrawingContext context,
        LocalLlmModelViewModel model,
        int index,
        double rowTop,
        double width,
        FontFamily uiFontFamily,
        FontFamily codeFontFamily)
    {
        var card = CardRect(rowTop, width);
        if (card.Width <= 24.0)
        {
            return;
        }

        var isHovered = _hoveredIndex == index;
        var background = model.IsInstalled
            ? Resource("CommandBackgroundBrush", CommandBackgroundFallbackBrush)
            : Resource("EditorSurfaceBrush", EditorSurfaceFallbackBrush);
        var border = model.IsRecommended
            ? Resource("AccentBorderBrush", AccentBorderFallbackBrush)
            : Resource("PanelBorderBrush", PanelBorderFallbackBrush);
        if (isHovered && !model.IsInstalled)
        {
            background = Resource("HistoryHoverBrush", HistoryHoverFallbackBrush);
        }

        context.DrawRectangle(background, new Pen(border, 1), card, 5, 5);

        var contentLeft = card.X + CardPaddingX;
        var contentRight = card.Right - CardPaddingX;
        var rightWidth = Math.Clamp(card.Width * 0.16, RightColumnMinWidth, RightColumnMaxWidth);
        if (card.Width < 560.0)
        {
            rightWidth = Math.Min(RightColumnMinWidth, Math.Max(76.0, card.Width * 0.23));
        }

        var rightLeft = Math.Max(contentLeft, contentRight - rightWidth);
        var middleWidth = card.Width < 560.0 ? 96.0 : Math.Clamp(card.Width * 0.24, 118.0, 178.0);
        var middleLeft = Math.Max(contentLeft, rightLeft - middleWidth - 8.0);
        var leftWidth = Math.Max(0.0, middleLeft - contentLeft - 8.0);

        var leftRect = new Rect(contentLeft, card.Y + CardPaddingY, Math.Max(0.0, leftWidth), card.Height - CardPaddingY * 2);
        DrawModelSummary(context, model, leftRect, uiFontFamily, codeFontFamily);

        DrawRequirementColumn(context, model, MetricsRect(rowTop, width), codeFontFamily);
        DrawRightColumn(context, model, index, rowTop, width, new Rect(rightLeft, card.Y + 4.0, contentRight - rightLeft, card.Height - 8.0), uiFontFamily, codeFontFamily);
    }

    private void DrawModelSummary(
        DrawingContext context,
        LocalLlmModelViewModel model,
        Rect rect,
        FontFamily uiFontFamily,
        FontFamily codeFontFamily)
    {
        if (rect.Width <= 0.0 || rect.Height <= 0.0)
        {
            return;
        }

        var titleX = rect.X;
        if (model.HasIcon)
        {
            var shell = new Rect(rect.X, rect.Y, IconSize, IconSize);
            var shellFill = model.IconHasTransparentBackground
                ? null
                : Resource("CommandBackgroundBrush", CommandBackgroundFallbackBrush);
            context.DrawRectangle(
                shellFill,
                new Pen(Resource("CommandBorderBrush", CommandBorderFallbackBrush), 1),
                shell,
                5,
                5);

            var icon = model.IconImage;
            if (icon is not null)
            {
                var iconRect = new Rect(
                    shell.X + (shell.Width - IconImageSize) * 0.5,
                    shell.Y + (shell.Height - IconImageSize) * 0.5,
                    IconImageSize,
                    IconImageSize);
                context.DrawImage(icon, iconRect);
            }
            else
            {
                var initials = GetFormattedText(
                    ResolveInitials(model.Provider),
                    Resource("AccentBrush", AccentFallbackBrush),
                    codeFontFamily,
                    FontWeight.Black,
                    FontStyle.Normal,
                    7.2);
                DrawClippedText(
                    context,
                    initials,
                    shell,
                    new Point(
                        shell.X + Math.Max(0.0, (shell.Width - initials.Width) * 0.5),
                        TextCenterY(shell, initials)));
            }

            titleX = shell.Right + 6.0;
        }

        var scale = WideScale(rect.Width);
        var factsWidth = rect.Width >= 330.0
            ? Math.Clamp(rect.Width * 0.24, 98.0, 142.0)
            : 0.0;
        var factsRect = factsWidth > 0.0
            ? new Rect(rect.Right - factsWidth, rect.Y, factsWidth, 32.0)
            : default;
        var titleRight = factsWidth > 0.0 ? factsRect.X - 8.0 : rect.Right;
        var titleWidth = Math.Max(0.0, titleRight - titleX);
        var title = GetFormattedText(Clean(model.DisplayNameWithThinking, 90), Resource("TextPrimaryBrush", TextPrimaryFallbackBrush), uiFontFamily, FontWeight.ExtraBold, FontStyle.Normal, 11.0 + scale * 1.4);
        DrawClippedText(context, title, new Rect(titleX, rect.Y - 1.0, titleWidth, 14.0), new Point(titleX, rect.Y - 1.0));

        var id = GetFormattedText(Clean(model.Id, 120), Resource("TextMutedBrush", TextMutedFallbackBrush), codeFontFamily, FontWeight.Bold, FontStyle.Normal, 7.3 + scale * 0.7);
        DrawClippedText(context, id, new Rect(titleX, rect.Y + 13.0, titleWidth, 10.0), new Point(titleX, rect.Y + 13.0));

        if (factsWidth > 0.0)
        {
            DrawInlineModelFacts(context, model, factsRect, codeFontFamily);
        }

        var shouldWarnForHfToken = ShouldWarnAboutMissingHuggingFaceToken(model);
        var useText = shouldWarnForHfToken
            ? "HF token recommended before download. Open View -> Settings -> LLMs for tutorial."
            : ShowLocation && !string.IsNullOrWhiteSpace(model.StorageLocationLabel)
                ? model.StorageLocationLabel
            : model.PracticalUse;
        var useBrush = shouldWarnForHfToken
            ? Resource("AccentBrush", AccentFallbackBrush)
            : Resource("TextPrimaryBrush", TextPrimaryFallbackBrush);
        var use = GetFormattedText(Clean(useText, 180), useBrush, uiFontFamily, shouldWarnForHfToken ? FontWeight.ExtraBold : FontWeight.Normal, FontStyle.Normal, 7.6 + scale * 0.9);
        DrawClippedText(context, use, new Rect(rect.X, rect.Y + 32.0, rect.Width, 10.0), new Point(rect.X, rect.Y + 31.0));

        DrawHardwareFitLine(
            context,
            model,
            new Rect(rect.X, rect.Y + 42.0, rect.Width, 10.0),
            codeFontFamily,
            7.4 + scale * 0.35);

        DrawPurposeTags(context, model.PurposeTags, new Rect(rect.X, rect.Y + SummaryTagsY, rect.Width, 27.0), uiFontFamily, scale);
    }

    private void DrawInlineModelFacts(
        DrawingContext context,
        LocalLlmModelViewModel model,
        Rect rect,
        FontFamily codeFontFamily)
    {
        if (rect.Width <= 0.0)
        {
            return;
        }

        var date = GetFormattedText(
            Clean(FormatModelDate(model), 18),
            Resource("TextMutedBrush", TextMutedFallbackBrush),
            codeFontFamily,
            FontWeight.Black,
            FontStyle.Normal,
            7.3);
        DrawClippedText(
            context,
            date,
            new Rect(rect.X, rect.Y - 1.0, rect.Width, 10.0),
            new Point(rect.Right - Math.Min(rect.Width, date.Width), rect.Y - 1.1));

        var size = GetFormattedText(
            Clean($"Size {model.EffectiveDownloadSizeLabel}", 24),
            Resource("AccentBrush", AccentFallbackBrush),
            codeFontFamily,
            FontWeight.Black,
            FontStyle.Normal,
            9.0);
        DrawClippedText(
            context,
            size,
            new Rect(rect.X, rect.Y + 10.0, rect.Width, 11.0),
            new Point(rect.Right - Math.Min(rect.Width, size.Width), rect.Y + 9.0));

        if (model.IsCloudModel)
        {
            var vram = GetFormattedText(
                "VRAM CLOUD",
                ResolveStatusBrush(HardwareTone.Good),
                codeFontFamily,
                FontWeight.Black,
                FontStyle.Normal,
                7.8);
            DrawClippedText(
                context,
                vram,
                new Rect(rect.X, rect.Y + 22.0, rect.Width, 10.0),
                new Point(rect.Right - Math.Min(rect.Width, vram.Width), rect.Y + 21.2));
            return;
        }

        DrawRightAlignedSegments(
            context,
            [
                ($"MIN {FormatGpuVram(model.MinimumVramGiB)}", ResolveStatusBrush(ResolveMinimumVramTone(model))),
                (" | ", Resource("TextMutedBrush", TextMutedFallbackBrush)),
                ($"OK {FormatGpuVram(model.RecommendedVramGiB)}", ResolveStatusBrush(ResolveRecommendedVramTone(model)))
            ],
            new Rect(rect.X, rect.Y + 22.0, rect.Width, 10.0),
            codeFontFamily,
            7.8);
    }

    private void DrawHardwareFitLine(
        DrawingContext context,
        LocalLlmModelViewModel model,
        Rect rect,
        FontFamily codeFontFamily,
        double fontSize)
    {
        if (model.IsCloudModel)
        {
            DrawSegmentLine(
                context,
                [("CLOUD", ResolveStatusBrush(HardwareTone.Good)), (" - local VRAM not used", Resource("TextMutedBrush", TextMutedFallbackBrush))],
                rect,
                codeFontFamily,
                fontSize);
            return;
        }

        var cpu = model.WorksOnCpu ? "CPU OK" : "CPU NO";
        var gpuTone = ResolveGpuTone(model);
        var gpu = gpuTone switch
        {
            HardwareTone.Good => "GPU OK",
            HardwareTone.Risk => "GPU MIN",
            _ => "GPU NO"
        };
        DrawSegmentLine(
            context,
            [
                (cpu, ResolveStatusBrush(model.WorksOnCpu ? HardwareTone.Good : HardwareTone.Bad)),
                (" | ", Resource("TextMutedBrush", TextMutedFallbackBrush)),
                (gpu, ResolveStatusBrush(gpuTone)),
                ($" - {model.FitLabel}", ResolveStatusBrush(ResolveOverallHardwareTone(model)))
            ],
            rect,
            codeFontFamily,
            fontSize);
    }

    private static string FormatGpuVram(double vram)
    {
        var normalized = Math.Max(0.0, vram);
        return Math.Abs(normalized - Math.Round(normalized)) < 0.05
            ? $"{normalized:0} GB"
            : $"{normalized:0.#} GB";
    }

    private static string FormatModelDate(LocalLlmModelViewModel model)
    {
        if (model.ReleaseDateValue is { } releaseDate)
        {
            return releaseDate.ToString("MMM yyyy", CultureInfo.InvariantCulture).ToLowerInvariant();
        }

        var clean = (model.ReleaseDate ?? "").Trim();
        return string.IsNullOrWhiteSpace(clean)
            ? "unknown"
            : clean.ToLowerInvariant();
    }

    private void DrawSegmentLine(
        DrawingContext context,
        IReadOnlyList<(string Text, IBrush Brush)> segments,
        Rect rect,
        FontFamily codeFontFamily,
        double fontSize)
    {
        var x = rect.X;
        foreach (var (text, brush) in segments)
        {
            if (string.IsNullOrWhiteSpace(text) || x >= rect.Right)
            {
                continue;
            }

            var formatted = GetFormattedText(
                Clean(text, 64),
                brush,
                codeFontFamily,
                FontWeight.Black,
                FontStyle.Normal,
                fontSize);
            DrawClippedText(
                context,
                formatted,
                new Rect(x, rect.Y, Math.Max(0.0, rect.Right - x), rect.Height),
                new Point(x, rect.Y - 0.5));
            x += formatted.Width;
        }
    }

    private void DrawRightAlignedSegments(
        DrawingContext context,
        IReadOnlyList<(string Text, IBrush Brush)> segments,
        Rect rect,
        FontFamily codeFontFamily,
        double fontSize)
    {
        var formattedSegments = segments
            .Where(segment => !string.IsNullOrWhiteSpace(segment.Text))
            .Select(segment => (
                Text: GetFormattedText(Clean(segment.Text, 64), segment.Brush, codeFontFamily, FontWeight.Black, FontStyle.Normal, fontSize),
                segment.Brush))
            .ToArray();
        var totalWidth = formattedSegments.Sum(segment => segment.Text.Width);
        var x = rect.Right - Math.Min(rect.Width, totalWidth);
        foreach (var segment in formattedSegments)
        {
            if (x >= rect.Right)
            {
                break;
            }

            DrawClippedText(
                context,
                segment.Text,
                new Rect(x, rect.Y, Math.Max(0.0, rect.Right - x), rect.Height),
                new Point(x, rect.Y - 0.5));
            x += segment.Text.Width;
        }
    }

    private void DrawRequirementColumn(
        DrawingContext context,
        LocalLlmModelViewModel model,
        Rect rect,
        FontFamily codeFontFamily)
    {
        var rows = new (string Label, string Value)[][]
        {
            [("BASE", model.ModelBaseLabel), ("DEP", model.BackendRequirementLabel), ("LIC", model.License), ("VRAM", model.MinimumRequirement)],
            [("CTX", model.AdvertisedContext), ("OK", model.ComfortableContext), ("THK", model.ThinkingLabel)],
            [("TPS", model.ExpectedSpeed)],
            [("USE", model.PracticalUse)]
        };
        var y = rect.Y;
        foreach (var row in rows)
        {
            DrawMetricPills(context, row, new Rect(rect.X, y, rect.Width, 14.0), codeFontFamily);
            y += 14.0;
        }
    }

    private void DrawMetricPills(DrawingContext context, IReadOnlyList<(string Label, string Value)> segments, Rect rect, FontFamily codeFontFamily)
    {
        var x = rect.X;
        foreach (var segment in segments)
        {
            if (string.IsNullOrWhiteSpace(segment.Value))
            {
                continue;
            }

            var label = GetFormattedText(segment.Label, Resource("AccentBrush", AccentFallbackBrush), codeFontFamily, FontWeight.Black, FontStyle.Normal, 6.2);
            var value = GetFormattedText(Clean(segment.Value, 34), Resource("TextMutedBrush", TextMutedFallbackBrush), codeFontFamily, FontWeight.Bold, FontStyle.Normal, 6.2);
            var width = Math.Clamp(label.Width + value.Width + 12.0, 32.0, rect.Width);
            if (x > rect.X && x + width > rect.Right)
            {
                break;
            }

            var pill = new Rect(x, rect.Y, Math.Min(width, rect.Right - x), 12.0);
            context.DrawRectangle(Resource("CommandBackgroundBrush", CommandBackgroundFallbackBrush), new Pen(Resource("CommandBorderBrush", CommandBorderFallbackBrush), 1), pill, 4, 4);
            DrawClippedText(context, label, new Rect(pill.X + 4.0, pill.Y, pill.Width - 8.0, pill.Height), new Point(pill.X + 4.0, TextCenterY(pill, label)));
            DrawClippedText(context, value, new Rect(pill.X + 4.0 + label.Width + 3.0, pill.Y, Math.Max(0.0, pill.Width - label.Width - 11.0), pill.Height), new Point(pill.X + 4.0 + label.Width + 3.0, TextCenterY(pill, value)));
            x += pill.Width + ChipGap;
        }
    }

    private void DrawPurposeTags(
        DrawingContext context,
        IReadOnlyList<string> tags,
        Rect rect,
        FontFamily uiFontFamily,
        double scale)
    {
        if (tags.Count == 0 || rect.Width <= 0.0)
        {
            return;
        }

        var x = rect.X;
        foreach (var tag in tags)
        {
            var text = GetFormattedText(Clean(tag, 24), Resource("AccentBrush", AccentFallbackBrush), uiFontFamily, FontWeight.ExtraBold, FontStyle.Normal, 6.8 + scale * 0.8);
            var width = Math.Clamp(Math.Ceiling(text.Width) + 10.0, 30.0, 96.0);
            if (x > rect.X && x + width > rect.Right)
            {
                break;
            }

            var chip = new Rect(x, rect.Y, Math.Min(width, Math.Max(0.0, rect.Right - x)), 13.0);
            if (chip.Width <= 18.0)
            {
                break;
            }

            context.DrawRectangle(
                Resource("CommandBackgroundBrush", CommandBackgroundFallbackBrush),
                new Pen(Resource("AccentBorderBrush", AccentBorderFallbackBrush), 1),
                chip,
                4,
                4);
            DrawClippedText(
                context,
                text,
                new Rect(chip.X + 4.0, chip.Y, Math.Max(0.0, chip.Width - 8.0), chip.Height),
                new Point(
                    chip.X + Math.Max(4.0, (chip.Width - text.Width) * 0.5),
                    TextCenterY(chip, text)));
            x += chip.Width + ChipGap;
        }
    }

    private void DrawMetricTextSegments(
        DrawingContext context,
        LocalLlmModelViewModel model,
        Rect rect,
        FontFamily codeFontFamily,
        double scale)
    {
        if (rect.Width <= 0.0 || rect.Height <= 0.0)
        {
            return;
        }

        var segments = new (string Label, string Value)[]
        {
            ("BASE", model.ModelBaseLabel),
            ("DEP", model.BackendRequirementLabel),
            ("SIZE", model.EffectiveDownloadSizeLabel),
            ("GPU", model.VramSummary),
            ("VRAM", model.MinimumRequirement),
            ("CTX", model.AdvertisedContext),
            ("OK", model.ComfortableContext),
            ("TPS", model.ExpectedSpeed),
            ("LIC", model.License),
            ("THK", model.ThinkingLabel)
        };

        var x = rect.X;
        var y = rect.Y - 0.5;
        var lineHeight = 6.8;
        var maxLines = rect.Height >= 13.0 ? 2 : 1;
        var line = 0;
        var fontSize = 5.9 + scale * 0.65;
        var separatorBrush = Resource("TextMutedBrush", TextMutedFallbackBrush);
        var labelBrush = Resource("AccentBrush", AccentFallbackBrush);
        var valueBrush = Resource("TextMutedBrush", TextMutedFallbackBrush);

        foreach (var segment in segments)
        {
            if (string.IsNullOrWhiteSpace(segment.Value))
            {
                continue;
            }

            var needsSeparator = x > rect.X + 0.5;
            var separator = needsSeparator
                ? GetFormattedText("| ", separatorBrush, codeFontFamily, FontWeight.Black, FontStyle.Normal, fontSize)
                : null;
            var labelText = GetFormattedText($"{segment.Label}:", labelBrush, codeFontFamily, FontWeight.Black, FontStyle.Normal, fontSize);
            var valueText = GetFormattedText(Clean(segment.Value, 38), valueBrush, codeFontFamily, FontWeight.Bold, FontStyle.Normal, fontSize);
            var segmentWidth = (separator?.Width ?? 0.0) + labelText.Width + 2.0 + valueText.Width + 5.0;

            if (needsSeparator && x + segmentWidth > rect.Right && line + 1 < maxLines)
            {
                x = rect.X;
                y += lineHeight;
                line++;
                needsSeparator = false;
                separator = null;
                segmentWidth = labelText.Width + 2.0 + valueText.Width + 5.0;
            }

            if (x + labelText.Width + 8.0 > rect.Right)
            {
                break;
            }

            if (separator is not null)
            {
                DrawClippedText(context, separator, new Rect(x, y, Math.Max(0.0, rect.Right - x), lineHeight), new Point(x, y));
                x += separator.Width;
            }

            DrawClippedText(context, labelText, new Rect(x, y, Math.Max(0.0, rect.Right - x), lineHeight), new Point(x, y));
            x += labelText.Width + 2.0;

            var valueWidth = Math.Max(0.0, rect.Right - x);
            var renderedValue = valueText;
            if (renderedValue.Width > valueWidth)
            {
                renderedValue = GetFormattedText(
                    FitLineToWidth(segment.Value, valueWidth, valueBrush, codeFontFamily, FontWeight.Bold, fontSize),
                    valueBrush,
                    codeFontFamily,
                    FontWeight.Bold,
                    FontStyle.Normal,
                    fontSize);
            }

            DrawClippedText(context, renderedValue, new Rect(x, y, valueWidth, lineHeight), new Point(x, y));
            x += Math.Min(renderedValue.Width, valueWidth) + 5.0;

            if (line == maxLines - 1 && x >= rect.Right - 6.0)
            {
                break;
            }
        }
    }

    private void DrawRightColumn(
        DrawingContext context,
        LocalLlmModelViewModel model,
        int index,
        double rowTop,
        double width,
        Rect rect,
        FontFamily uiFontFamily,
        FontFamily codeFontFamily)
    {
        if (rect.Width <= 0.0)
        {
            return;
        }

        DrawRightMeta(context, model, rect, codeFontFamily);

        var button = PullButtonRect(rowTop, width);
        var enabled = CanRunModelAction(model);
        var isHovered = _hoveredIndex == index && _hoveredKind == LocalLlmCatalogHitKind.Pull && enabled;
        var background = enabled
            ? isHovered ? Resource("HistoryActiveBrush", HistoryActiveFallbackBrush) : Resource("HistoryHoverBrush", HistoryHoverFallbackBrush)
            : Resource("CommandBackgroundBrush", CommandBackgroundFallbackBrush);
        var border = enabled && isHovered
            ? Resource("AccentBorderBrush", AccentBorderFallbackBrush)
            : Resource("CommandBorderBrush", CommandBorderFallbackBrush);
        var foreground = enabled
            ? Resource("TextPrimaryBrush", TextPrimaryFallbackBrush)
            : Resource("TextMutedBrush", TextMutedFallbackBrush);

        context.DrawRectangle(background, new Pen(border, 1), button, 4, 4);
        var label = GetFormattedText(Clean(model.PullButtonLabel, 30), foreground, uiFontFamily, FontWeight.ExtraBold, FontStyle.Normal, 8.2);
        DrawClippedText(
            context,
            label,
            new Rect(button.X + 4.0, button.Y, Math.Max(0.0, button.Width - 8.0), button.Height),
            new Point(
                button.X + Math.Max(4.0, (button.Width - label.Width) * 0.5),
                TextCenterY(button, label)));
    }

    private void DrawRightMeta(DrawingContext context, LocalLlmModelViewModel model, Rect rect, FontFamily codeFontFamily)
    {
        var tags = ShouldWarnAboutMissingHuggingFaceToken(model)
            ? new[] { "HF token", model.InstallLabel }
            : new[] { model.InstallLabel };
        var right = rect.Right;
        foreach (var tag in tags.Reverse())
        {
            right = DrawPill(context, tag, rect.Y, right, Math.Max(0.0, right - rect.X), tag == "HF token", codeFontFamily);
        }
    }

    private bool ShouldWarnAboutMissingHuggingFaceToken(LocalLlmModelViewModel model)
    {
        return model.UsesHuggingFaceHubDownload && !HasHuggingFaceToken;
    }

    private double DrawPill(
        DrawingContext context,
        string text,
        double y,
        double right,
        double maxWidth,
        bool isAccent,
        FontFamily codeFontFamily)
    {
        if (string.IsNullOrWhiteSpace(text) || maxWidth <= 24.0)
        {
            return right;
        }

        var foreground = isAccent ? Resource("AccentBrush", AccentFallbackBrush) : Resource("TextPrimaryBrush", TextPrimaryFallbackBrush);
        var formatted = GetFormattedText(Clean(text, 40), foreground, codeFontFamily, FontWeight.Black, FontStyle.Normal, 6.9);
        var width = Math.Min(maxWidth, Math.Max(34.0, formatted.Width + 10.0));
        var rect = new Rect(Math.Max(0.0, right - width), y, width, PillHeight);
        context.DrawRectangle(
            isAccent ? Resource("CommandPrimaryBackgroundBrush", CommandPrimaryBackgroundFallbackBrush) : Resource("CommandBackgroundBrush", CommandBackgroundFallbackBrush),
            new Pen(isAccent ? Resource("AccentBorderBrush", AccentBorderFallbackBrush) : Resource("CommandBorderBrush", CommandBorderFallbackBrush), 1),
            rect,
            4,
            4);
        DrawClippedText(
            context,
            formatted,
            new Rect(rect.X + 4.0, rect.Y, Math.Max(0.0, rect.Width - 8.0), rect.Height),
            new Point(rect.X + 5.0, TextCenterY(rect, formatted)));
        return rect.X - 3.0;
    }

    private static double TextCenterY(Rect rect, FormattedText formatted)
    {
        return rect.Y + Math.Max(0.0, (rect.Height - formatted.Height) * 0.5) + TextCenterNudgeY;
    }

    private static double WideScale(double width)
    {
        return Math.Clamp((width - 520.0) / 680.0, 0.0, 1.0);
    }

    private static HardwareTone ResolveGpuTone(LocalLlmModelViewModel model)
    {
        if (model.RecommendedVramFitsDetectedGpu)
        {
            return HardwareTone.Good;
        }

        return model.MinimumVramFitsDetectedGpu ? HardwareTone.Risk : HardwareTone.Bad;
    }

    private static HardwareTone ResolveMinimumVramTone(LocalLlmModelViewModel model)
    {
        if (model.RecommendedVramFitsDetectedGpu)
        {
            return HardwareTone.Good;
        }

        return model.MinimumVramFitsDetectedGpu ? HardwareTone.Risk : HardwareTone.Bad;
    }

    private static HardwareTone ResolveRecommendedVramTone(LocalLlmModelViewModel model)
    {
        if (model.RecommendedVramFitsDetectedGpu)
        {
            return HardwareTone.Good;
        }

        return model.MinimumVramFitsDetectedGpu ? HardwareTone.Risk : HardwareTone.Bad;
    }

    private static HardwareTone ResolveOverallHardwareTone(LocalLlmModelViewModel model)
    {
        if (model.IsCloudModel
            || model.RecommendedVramFitsDetectedGpu
            || (model.WorksOnCpu && model.MinimumVramGiB <= 0))
        {
            return HardwareTone.Good;
        }

        return model.CanRunOnDetectedHardware ? HardwareTone.Risk : HardwareTone.Bad;
    }

    private IBrush ResolveStatusBrush(HardwareTone tone)
    {
        return tone switch
        {
            HardwareTone.Good => FitGoodFallbackBrush,
            HardwareTone.Risk => FitRiskFallbackBrush,
            _ => FitBadFallbackBrush
        };
    }

    private enum HardwareTone
    {
        Good,
        Risk,
        Bad
    }

    private void DrawWrappedText(
        DrawingContext context,
        string? value,
        Rect clip,
        IBrush brush,
        FontFamily fontFamily,
        FontWeight weight,
        double fontSize,
        double lineHeight,
        int maxLines)
    {
        if (clip.Width <= 0.0 || clip.Height <= 0.0 || maxLines <= 0)
        {
            return;
        }

        var lines = WrapLines(Clean(value, 320), clip.Width, brush, fontFamily, weight, fontSize, maxLines);
        var y = clip.Y;
        foreach (var line in lines)
        {
            if (y + lineHeight > clip.Bottom + 0.5)
            {
                break;
            }

            var formatted = GetFormattedText(line, brush, fontFamily, weight, FontStyle.Normal, fontSize);
            DrawClippedText(context, formatted, new Rect(clip.X, y, clip.Width, lineHeight), new Point(clip.X, y));
            y += lineHeight;
        }
    }

    private IReadOnlyList<string> WrapLines(
        string text,
        double width,
        IBrush brush,
        FontFamily fontFamily,
        FontWeight weight,
        double fontSize,
        int maxLines)
    {
        if (string.IsNullOrWhiteSpace(text) || width <= 0.0)
        {
            return [];
        }

        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var lines = new List<string>();
        var current = "";
        foreach (var word in words)
        {
            var candidate = string.IsNullOrWhiteSpace(current) ? word : $"{current} {word}";
            if (GetFormattedText(candidate, brush, fontFamily, weight, FontStyle.Normal, fontSize).Width <= width)
            {
                current = candidate;
                continue;
            }

            if (!string.IsNullOrWhiteSpace(current))
            {
                lines.Add(current);
                current = word;
            }
            else
            {
                lines.Add(FitLineToWidth(word, width, brush, fontFamily, weight, fontSize));
                current = "";
            }

            if (lines.Count == maxLines)
            {
                break;
            }
        }

        if (lines.Count < maxLines && !string.IsNullOrWhiteSpace(current))
        {
            lines.Add(current);
        }

        if (lines.Count == maxLines)
        {
            var consumed = string.Join(" ", lines);
            if (text.Length > consumed.Length)
            {
                lines[^1] = FitLineToWidth(lines[^1] + "...", width, brush, fontFamily, weight, fontSize);
            }
        }

        return lines;
    }

    private string FitLineToWidth(
        string text,
        double width,
        IBrush brush,
        FontFamily fontFamily,
        FontWeight weight,
        double fontSize)
    {
        var clean = text ?? "";
        while (clean.Length > 4
            && GetFormattedText(clean, brush, fontFamily, weight, FontStyle.Normal, fontSize).Width > width)
        {
            clean = clean[..^4].TrimEnd() + "...";
        }

        return clean;
    }

    private void DrawClippedText(DrawingContext context, FormattedText formatted, Rect clip, Point point)
    {
        if (clip.Width <= 0.0 || clip.Height <= 0.0)
        {
            return;
        }

        using (context.PushClip(clip))
        {
            context.DrawText(formatted, point);
        }
    }

}
