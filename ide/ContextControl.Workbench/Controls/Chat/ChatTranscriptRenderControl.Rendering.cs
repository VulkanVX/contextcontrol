// CC-DESC: Draws the shared chat/image-generation transcript as one cached virtualized surface.

using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ContextControl.Workbench.ViewModels;

namespace ContextControl.Workbench.Controls;

public sealed partial class ChatTranscriptRenderControl
{
    private void DrawMessage(
        DrawingContext context,
        MessageLayout layout,
        int index,
        double rowTop,
        double viewportTop,
        double viewportBottom)
    {
        var message = layout.Message;
        var card = OffsetY(layout.CardRect, rowTop);
        if (!IsFlatTranscriptMessage(message))
        {
            var (background, border) = ResolveMessageChrome(message);
            context.DrawRectangle(background, new Pen(border, 1), card, 5, 5);
        }

        using var clip = context.PushClip(card);
        DrawHeader(context, layout, message, rowTop);

        foreach (var attachment in layout.Attachments)
        {
            DrawAttachment(context, attachment, rowTop);
        }

        foreach (var text in layout.TextBlocks)
        {
            DrawTextSelection(context, text.TextBlock, index, text.BlockIndex, rowTop, viewportTop, viewportBottom);
            DrawTextBlock(context, text.TextBlock, rowTop, viewportTop, viewportBottom);
        }

        foreach (var snippet in layout.Snippets)
        {
            DrawSnippet(context, snippet, index, rowTop, viewportTop, viewportBottom);
        }

        if (layout.Thinking is not null)
        {
            DrawThinking(context, layout.Thinking, rowTop, viewportTop, viewportBottom);
        }

        if (layout.Diagnostic is not null)
        {
            DrawDiagnostic(context, layout.Diagnostic, rowTop, viewportTop, viewportBottom);
        }
    }

    private (IBrush Background, IBrush Border) ResolveMessageChrome(LocalLlmChatMessageViewModel message)
    {
        if (message.IsUser)
        {
            return (
                Resource("ChatUserBubbleBrush", CommandPrimaryBackgroundFallbackBrush),
                Resource("ChatUserBorderBrush", AccentBorderFallbackBrush));
        }

        if (message.IsCodexGenerated)
        {
            return (
                Resource("ChatAssistantBubbleBrush", HistoryActiveFallbackBrush),
                Resource("ChatAssistantBorderBrush", AccentBorderFallbackBrush));
        }

        if (message.IsContextControlGenerated)
        {
            return (
                Resource("ChatToolBubbleBrush", EditorSurfaceFallbackBrush),
                Resource("ChatToolBorderBrush", CommandBorderFallbackBrush));
        }

        return (
            Resource("ChatLocalBubbleBrush", CommandBackgroundFallbackBrush),
            Resource("ChatLocalBorderBrush", PanelBorderFallbackBrush));
    }

    private void DrawHeader(DrawingContext context, MessageLayout layout, LocalLlmChatMessageViewModel message, double rowTop)
    {
        var uiFontFamily = ResolveFontFamily(UiFontFamily, Resource("UiFontFamily", DefaultUiFontFamily));
        var codeFontFamily = ResolveFontFamily(CodeFontFamily, Resource("CodeFontFamily", DefaultCodeFontFamily));
        var owner = ResolveOwnerBrush(message);
        var muted = Resource("ChatMetaBrush", TextMutedFallbackBrush);

        var role = GetFormattedText(CleanOneLine(message.HeaderTitle, 48), owner, uiFontFamily, FontWeight.Bold, FontStyle.Normal, 9.2);
        var meta = GetFormattedText(CleanOneLine(message.MetaLabel, 220), muted, uiFontFamily, FontWeight.SemiBold, FontStyle.Normal, ChatMetaFontSize);
        var clip = OffsetY(layout.HeaderMetaClip, rowTop);
        var headerRect = OffsetY(layout.HeaderRect, rowTop);
        var isHeaderHovered = _hoveredHit is { Kind: ChatTranscriptHitKind.ToggleMessageCollapse } hovered
            && ReferenceEquals(hovered.Parameter, message);
        if (isHeaderHovered)
        {
            context.DrawRectangle(
                Resource("ChatHeaderHoverBrush", TransparentBrush),
                null,
                headerRect,
                5,
                5);
        }

        var roleWidth = Math.Min(role.Width, Math.Max(0.0, clip.Width));
        var baselineY = CenterTextY(headerRect, role);
        if (message.IsContextControlGenerated && layout.ToolIconRect.Width > 0.0)
        {
            DrawToolPhaseIcon(context, OffsetY(layout.ToolIconRect, rowTop));
        }

        DrawClippedText(context, role, new Rect(clip.X, baselineY, roleWidth, 12.0), new Point(clip.X, baselineY));

        var metaX = clip.X + roleWidth + 9.0;
        if (metaX < clip.Right - 12.0)
        {
            var metaY = CenterTextY(headerRect, meta);
            DrawClippedText(
                context,
                meta,
                new Rect(metaX, metaY, Math.Max(0.0, clip.Right - metaX), 12.0),
                new Point(metaX, metaY));
        }

        var time = GetFormattedText(message.Time, muted, codeFontFamily, FontWeight.Bold, FontStyle.Normal, 8.8);
        var timeRect = OffsetY(layout.TimeRect, rowTop);
        DrawClippedText(
            context,
            time,
            timeRect,
            new Point(timeRect.X, CenterTextY(timeRect, time)));

        foreach (var hit in layout.Hits.Where(hit => hit.Kind is ChatTranscriptHitKind.CopyMessage or ChatTranscriptHitKind.CreateProject or ChatTranscriptHitKind.DownloadAttachment))
        {
            if (hit.Kind == ChatTranscriptHitKind.CopyMessage)
            {
                DrawCopyIconButton(context, OffsetY(hit.Rect, rowTop), CanExecuteHit(hit), ReferenceEquals(hit, _hoveredHit));
            }
            else if (hit.Kind == ChatTranscriptHitKind.DownloadAttachment)
            {
                DrawDownloadIconButton(context, OffsetY(hit.Rect, rowTop), CanExecuteHit(hit), ReferenceEquals(hit, _hoveredHit));
            }
            else
            {
                DrawButton(context, OffsetY(hit.Rect, rowTop), "Create Project", CanExecuteHit(hit), ReferenceEquals(hit, _hoveredHit));
            }
        }
    }

    private void DrawAttachment(DrawingContext context, AttachmentLayout attachment, double rowTop)
    {
        if (attachment.IsImagePreview)
        {
            DrawImageAttachment(context, attachment, rowTop);
            return;
        }

        var rect = OffsetY(attachment.Rect, rowTop);
        context.DrawRectangle(
            Resource("ChatSnippetBodyBrush", CommandBackgroundFallbackBrush),
            new Pen(Resource("ChatSnippetBorderBrush", CommandBorderFallbackBrush), 1),
            rect,
            5,
            5);

        var font = ResolveFontFamily(CodeFontFamily, Resource("CodeFontFamily", DefaultCodeFontFamily));
        var text = GetFormattedText(attachment.Label, Resource("ChatMetaBrush", TextMutedFallbackBrush), font, FontWeight.Bold, FontStyle.Normal, 9.0);
        DrawClippedText(context, text, new Rect(rect.X + 6.0, rect.Y, Math.Max(0.0, rect.Width - 12.0), rect.Height), new Point(rect.X + 6.0, CenterTextY(rect, text)));
    }

    private void DrawImageAttachment(DrawingContext context, AttachmentLayout attachment, double rowTop)
    {
        var rect = OffsetY(attachment.Rect, rowTop);
        var border = Resource("ChatSnippetBorderBrush", CommandBorderFallbackBrush);
        context.DrawRectangle(
            Resource("ChatSnippetShellBrush", EditorSurfaceFallbackBrush),
            new Pen(border, 1),
            rect,
            5,
            5);

        var imageBounds = new Rect(
            rect.X + ImagePreviewInset,
            rect.Y + ImagePreviewInset,
            Math.Max(0.0, rect.Width - ImagePreviewInset * 2.0),
            Math.Max(0.0, rect.Height - ImagePreviewInset * 2.0));
        var bitmap = TryGetImageBitmap(attachment.Attachment.Path);
        if (bitmap is null || imageBounds.Width <= 0.0 || imageBounds.Height <= 0.0)
        {
            var font = ResolveFontFamily(UiFontFamily, Resource("UiFontFamily", DefaultUiFontFamily));
            var text = GetFormattedText("Image preview unavailable", Resource("ChatMetaBrush", TextMutedFallbackBrush), font, FontWeight.SemiBold, FontStyle.Normal, 10.0);
            DrawClippedText(
                context,
                text,
                imageBounds,
                new Point(
                    imageBounds.X + Math.Max(0.0, (imageBounds.Width - text.Width) * 0.5),
                    imageBounds.Y + Math.Max(0.0, (imageBounds.Height - text.Height) * 0.5)));
            return;
        }

        context.DrawImage(bitmap, FitImageRect(bitmap.PixelSize, imageBounds));
    }

    private void DrawSnippet(DrawingContext context, SnippetLayout snippet, int rowIndex, double rowTop, double viewportTop, double viewportBottom)
    {
        var codeFontFamily = ResolveFontFamily(CodeFontFamily, Resource("CodeFontFamily", DefaultCodeFontFamily));
        var uiFontFamily = ResolveFontFamily(UiFontFamily, Resource("UiFontFamily", DefaultUiFontFamily));
        var header = OffsetY(snippet.HeaderRect, rowTop);
        var code = OffsetY(snippet.CodeRect, rowTop);

        var shell = new Rect(header.X, header.Y, header.Width, header.Height + SnippetBodyGap + code.Height);
        context.DrawRectangle(
            Resource("ChatSnippetShellBrush", EditorSurfaceFallbackBrush),
            new Pen(Resource("ChatSnippetBorderBrush", CommandBorderFallbackBrush), 1),
            shell,
            5,
            5);

        var typeRect = new Rect(header.X + SnippetPadding, header.Y + 1.0, 40.0, SnippetButtonHeight);
        var (badgeBackground, badgeBorder, badgeTextBrush) = ResolveSnippetBadgeChrome(snippet.Snippet);
        context.DrawRectangle(
            badgeBackground,
            new Pen(badgeBorder, 1),
            typeRect,
            4,
            4);
        var typeText = GetFormattedText(CleanOneLine(snippet.Snippet.TypeLabel, 14), badgeTextBrush, codeFontFamily, FontWeight.Black, FontStyle.Normal, 7.7);
        DrawClippedText(
            context,
            typeText,
            typeRect,
            new Point(typeRect.X + Math.Max(3.0, (typeRect.Width - typeText.Width) * 0.5), CenterTextY(typeRect, typeText)));

        var meta = GetFormattedText(CleanOneLine(snippet.Snippet.CompactMetaLabel, 120), Resource("ChatMetaBrush", TextMutedFallbackBrush), codeFontFamily, FontWeight.SemiBold, FontStyle.Normal, 8.1);
        var metaClip = OffsetY(snippet.MetaClip, rowTop);
        DrawClippedText(context, meta, metaClip, new Point(metaClip.X, CenterTextY(header, meta)));

        foreach (var button in snippet.Buttons)
        {
            DrawButton(context, OffsetY(button.Rect, rowTop), button.Label, button.IsEnabled, ReferenceEquals(button.Hit, _hoveredHit));
        }

        if (snippet.Snippet.IsPatchPlan)
        {
            DrawPatchPlanSnippet(context, snippet, rowTop, viewportTop, viewportBottom);
            return;
        }

        context.DrawRectangle(
            Resource("ChatSnippetBodyBrush", CommandBackgroundFallbackBrush),
            new Pen(Resource("ChatSnippetBorderBrush", PanelBorderFallbackBrush), 1),
            code,
            4,
            4);

        if (snippet.CodeTextBlock is not null)
        {
            DrawTextSelection(context, snippet.CodeTextBlock.TextBlock, rowIndex, snippet.CodeTextBlock.BlockIndex, rowTop, viewportTop, viewportBottom);
            DrawTextBlock(context, snippet.CodeTextBlock.TextBlock, rowTop, viewportTop, viewportBottom, wrap: false);
        }
    }

    private void DrawPatchPlanSnippet(DrawingContext context, SnippetLayout snippet, double rowTop, double viewportTop, double viewportBottom)
    {
        var rect = OffsetY(snippet.CodeRect, rowTop);
        context.DrawRectangle(
            Resource("ChatSnippetBodyBrush", CommandBackgroundFallbackBrush),
            new Pen(Resource("ChatSnippetBorderBrush", PanelBorderFallbackBrush), 1),
            rect,
            4,
            4);

        if (rect.Bottom < viewportTop || rect.Y > viewportBottom || rect.Width <= 0.0 || rect.Height <= 0.0)
        {
            return;
        }

        var rows = snippet.Snippet.PatchPlanRows;
        var codeFontFamily = ResolveFontFamily(CodeFontFamily, Resource("CodeFontFamily", DefaultCodeFontFamily));
        var primary = Resource("TextPrimaryBrush", TextPrimaryFallbackBrush);
        var muted = Resource("TextMutedBrush", TextMutedFallbackBrush);
        var versionBrush = Resource("PatchVersionBrush", PatchVersionFallbackBrush);
        var addBrush = Resource("PatchAddBrush", PatchAddFallbackBrush);
        var removeBrush = Resource("PatchRemoveBrush", PatchRemoveFallbackBrush);
        var locBrush = Resource("PatchLocBrush", PatchLocFallbackBrush);
        var divider = new Pen(Resource("PanelBorderBrush", PanelBorderFallbackBrush), 1);
        var rowHeight = PatchPlanRowHeight;

        using (context.PushClip(rect))
        {
            if (rows.Count == 0)
            {
                var empty = GetFormattedText("No patch file rows returned.", muted, codeFontFamily, FontWeight.SemiBold, FontStyle.Normal, 9.0);
                context.DrawText(empty, new Point(rect.X + 7.0, rect.Y + 7.0));
                return;
            }

            for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
            {
                var row = rows[rowIndex];
                var y = rect.Y + 4.0 + (rowIndex * rowHeight);
                if (y + rowHeight < viewportTop || y > viewportBottom)
                {
                    continue;
                }

                if (rowIndex > 0)
                {
                    context.DrawLine(divider, new Point(rect.X + 6.0, y - 3.0), new Point(rect.Right - 6.0, y - 3.0));
                }

                var rowRect = new Rect(rect.X + 3.0, y - 1.0, Math.Max(1.0, rect.Width - 6.0), rowHeight - 1.0);
                var isHovered = IsHoveredPatchPlanRow(row.Target, snippet.CodeRect.Y + 4.0 + (rowIndex * rowHeight));
                context.DrawRectangle(
                    isHovered ? Resource("HistoryActiveBrush", HistoryActiveFallbackBrush) : Resource("EditorSurfaceBrush", EditorSurfaceFallbackBrush),
                    null,
                    rowRect,
                    3,
                    3);

                var left = rowRect.X + 7.0;
                var tokens =
                    new[]
                    {
                        (Value: row.Added, Brush: addBrush, Weight: FontWeight.Black),
                        (Value: row.Removed, Brush: removeBrush, Weight: FontWeight.Black),
                        (Value: "|", Brush: muted, Weight: FontWeight.Bold),
                        (Value: row.Loc, Brush: locBrush, Weight: FontWeight.Bold),
                        (Value: "|", Brush: muted, Weight: FontWeight.Bold),
                        (Value: NormalizePatchPlanVersion(row.Version), Brush: versionBrush, Weight: FontWeight.Bold)
                    };
                var statsWidth = MeasurePatchPlanTokens(tokens, codeFontFamily);
                var statsX = Math.Max(left + 56.0, rowRect.Right - 7.0 - statsWidth);
                var nameWidth = Math.Max(56.0, statsX - left - 8.0);
                var fileText = GetFormattedText(CleanOneLine(row.FileName, 80), primary, codeFontFamily, FontWeight.Black, FontStyle.Normal, 9.0);
                DrawClippedText(context, fileText, new Rect(left, y + 1.0, nameWidth, 13.0), new Point(left, y + 1.0));

                DrawPatchPlanTokens(context, tokens, codeFontFamily, statsX, y + 1.0, rowRect.Right - 7.0);
            }
        }
    }

    private IBrush ResolveOwnerBrush(LocalLlmChatMessageViewModel message)
    {
        if (message.IsUser)
        {
            return Resource("ChatUserOwnerBrush", AccentFallbackBrush);
        }

        if (message.IsCodexGenerated)
        {
            return Resource("ChatAssistantOwnerBrush", AccentFallbackBrush);
        }

        if (message.IsContextControlGenerated)
        {
            return Resource("ChatToolOwnerBrush", TextPrimaryFallbackBrush);
        }

        return Resource("ChatLocalOwnerBrush", TextMutedFallbackBrush);
    }

    private (IBrush Background, IBrush Border, IBrush Text) ResolveSnippetBadgeChrome(ChatSnippetViewModel snippet)
    {
        if (snippet.IsRequestList)
        {
            return (
                Resource("ChatRequestBadgeBrush", CommandPrimaryBackgroundFallbackBrush),
                Resource("ChatRequestBadgeBorderBrush", AccentBorderFallbackBrush),
                Resource("ChatRequestBadgeTextBrush", AccentFallbackBrush));
        }

        return (
            Resource("ChatSnippetBadgeBrush", CommandBackgroundFallbackBrush),
            Resource("ChatSnippetBadgeBorderBrush", CommandBorderFallbackBrush),
            Resource("ChatSnippetBadgeTextBrush", TextMutedFallbackBrush));
    }

    private static double CenterTextY(Rect rect, FormattedText text)
    {
        return rect.Y + Math.Max(0.0, (rect.Height - text.Height) * 0.5);
    }

    private bool IsHoveredPatchPlanRow(string target, double layoutY)
    {
        return _hoveredHit is { Kind: ChatTranscriptHitKind.OpenPatchPlanFile, Parameter: PatchPlanFileViewModel file }
            && file.Target.Equals(target, StringComparison.OrdinalIgnoreCase)
            && Math.Abs(_hoveredHit.Rect.Y - layoutY) < 0.5;
    }

    private double MeasurePatchPlanTokens(
        IReadOnlyList<(string Value, IBrush Brush, FontWeight Weight)> tokens,
        FontFamily font)
    {
        var width = 0.0;
        foreach (var (value, _, weight) in tokens)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            if (width > 0.0)
            {
                width += value.Equals("|", StringComparison.Ordinal) ? 5.0 : 7.0;
            }

            width += GetFormattedText(CleanOneLine(value, 28), Brushes.Transparent, font, weight, FontStyle.Normal, 8.2).Width;
        }

        return width;
    }

    private void DrawPatchPlanTokens(
        DrawingContext context,
        IReadOnlyList<(string Value, IBrush Brush, FontWeight Weight)> tokens,
        FontFamily font,
        double x,
        double y,
        double right)
    {
        var hasDrawn = false;
        foreach (var (value, brush, weight) in tokens)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            if (x > right)
            {
                return;
            }

            if (hasDrawn && value.Equals("|", StringComparison.Ordinal))
            {
                x += 5.0;
            }
            else if (hasDrawn)
            {
                x += 7.0;
            }

            var text = GetFormattedText(CleanOneLine(value, 28), brush, font, weight, FontStyle.Normal, 8.2);
            var width = Math.Min(text.Width, Math.Max(0.0, right - x));
            DrawClippedText(context, text, new Rect(x, y, width, 12.0), new Point(x, y));
            x += text.Width;
            hasDrawn = true;
        }
    }

    private static string NormalizePatchPlanVersion(string value)
    {
        return (value ?? "")
            .Replace("\u2192", " > ", StringComparison.Ordinal)
            .Replace("->", " > ", StringComparison.Ordinal)
            .Replace("  ", " ", StringComparison.Ordinal)
            .Trim();
    }

    private void DrawThinking(DrawingContext context, ThinkingLayout thinking, double rowTop, double viewportTop, double viewportBottom)
    {
        DrawExtensionToggle(context, thinking.ButtonRect, thinking.Hit, "Thinking", thinking.IsExpanded, rowTop);
        if (thinking.TextBlock is null)
        {
            return;
        }

        var surface = OffsetY(thinking.BodyRect, rowTop);
        context.DrawRectangle(
            Resource("ChatDiagnosticPanelBrush", EditorSurfaceFallbackBrush),
            new Pen(Resource("ChatDiagnosticBorderBrush", CommandBorderFallbackBrush), 1),
            surface,
            3,
            3);
        var scrollOffset = GetExtensionScrollOffset(thinking.Hit, thinking.TextBlock, _thinkingScrollOffsets);
        DrawScrollableTextBlock(context, thinking.TextBlock, scrollOffset, rowTop, viewportTop, viewportBottom);
        DrawExtensionScrollbar(context, surface, thinking.TextBlock, scrollOffset);
    }

    private void DrawDiagnostic(DrawingContext context, DiagnosticLayout diagnostic, double rowTop, double viewportTop, double viewportBottom)
    {
        DrawExtensionToggle(context, diagnostic.ButtonRect, diagnostic.Hit, "Harness", diagnostic.IsExpanded, rowTop);
        if (diagnostic.TextBlock is null)
        {
            return;
        }

        var surface = OffsetY(diagnostic.BodyRect, rowTop);
        context.DrawRectangle(
            Resource("ChatDiagnosticPanelBrush", EditorSurfaceFallbackBrush),
            new Pen(Resource("ChatDiagnosticBorderBrush", CommandBorderFallbackBrush), 1),
            surface,
            3,
            3);
        var scrollOffset = GetExtensionScrollOffset(diagnostic.Hit, diagnostic.TextBlock, _diagnosticScrollOffsets);
        DrawScrollableTextBlock(context, diagnostic.TextBlock, scrollOffset, rowTop, viewportTop, viewportBottom);
        DrawExtensionScrollbar(context, surface, diagnostic.TextBlock, scrollOffset);
    }

    private double GetExtensionScrollOffset(
        HitRegion hit,
        TextBlockLayout block,
        Dictionary<LocalLlmChatMessageViewModel, double> offsets)
    {
        if (hit.Parameter is not LocalLlmChatMessageViewModel message)
        {
            return 0.0;
        }

        var max = GetMaxExtensionScrollOffset(block);
        if (max <= 0.5)
        {
            offsets.Remove(message);
            return 0.0;
        }

        offsets.TryGetValue(message, out var offset);
        offset = Math.Clamp(offset, 0.0, max);
        offsets[message] = offset;
        return offset;
    }

    private static double GetMaxExtensionScrollOffset(TextBlockLayout block)
    {
        return Math.Max(0.0, block.Lines.Count * block.LineHeight - block.Rect.Height);
    }

    private void DrawScrollableTextBlock(
        DrawingContext context,
        TextBlockLayout block,
        double scrollOffset,
        double rowTop,
        double viewportTop,
        double viewportBottom)
    {
        var rect = OffsetY(block.Rect, rowTop);
        if (rect.Bottom < viewportTop || rect.Y > viewportBottom || rect.Width <= 0.0 || rect.Height <= 0.0)
        {
            return;
        }

        var font = ResolveFontFamily(CodeFontFamily, Resource("CodeFontFamily", DefaultCodeFontFamily));
        var firstLine = Math.Max(0, (int)Math.Floor(scrollOffset / block.LineHeight) - 1);
        var lastLine = Math.Min(block.Lines.Count - 1, (int)Math.Ceiling((scrollOffset + rect.Height) / block.LineHeight) + 1);
        using (context.PushClip(rect))
        {
            for (var lineIndex = firstLine; lineIndex <= lastLine; lineIndex++)
            {
                var line = block.Lines[lineIndex];
                var y = rect.Y - scrollOffset + lineIndex * block.LineHeight;
                if (y + block.LineHeight < viewportTop || y > viewportBottom)
                {
                    continue;
                }

                var text = GetFormattedText(line, block.Brush, font, block.Weight, block.Style, block.FontSize);
                DrawClippedText(context, text, new Rect(rect.X, y, rect.Width, block.LineHeight), new Point(rect.X, y));
            }
        }
    }

    private void DrawExtensionScrollbar(DrawingContext context, Rect body, TextBlockLayout block, double scrollOffset)
    {
        var contentHeight = block.Lines.Count * block.LineHeight;
        if (!TryResolveExtensionScrollbarGeometry(
                body.Height,
                block.Rect.Height,
                contentHeight,
                scrollOffset,
                out var trackHeight,
                out var thumbHeight,
                out var thumbOffset))
        {
            return;
        }

        var track = new Rect(body.Right - 8.0, body.Y + 7.0, 3.0, trackHeight);
        context.DrawRectangle(Resource("ChatDiagnosticScrollbarTrackBrush", CommandBorderFallbackBrush), null, track, 1.5, 1.5);

        var thumbY = track.Y + thumbOffset;
        var thumb = new Rect(track.X - 0.5, thumbY, track.Width + 1.0, thumbHeight);
        context.DrawRectangle(Resource("ChatDiagnosticScrollbarThumbBrush", AccentFallbackBrush), null, thumb, 2.0, 2.0);
    }

    private static bool TryResolveExtensionScrollbarGeometry(
        double bodyHeight,
        double blockHeight,
        double contentHeight,
        double scrollOffset,
        out double trackHeight,
        out double thumbHeight,
        out double thumbOffset)
    {
        trackHeight = 0.0;
        thumbHeight = 0.0;
        thumbOffset = 0.0;

        if (contentHeight <= blockHeight + 1.0)
        {
            return false;
        }

        trackHeight = bodyHeight - 14.0;
        if (trackHeight < 4.0 || blockHeight < 4.0)
        {
            trackHeight = 0.0;
            return false;
        }

        var max = Math.Max(1.0, contentHeight - blockHeight);
        var minThumbHeight = Math.Min(18.0, trackHeight);
        thumbHeight = Math.Clamp(trackHeight * blockHeight / contentHeight, minThumbHeight, trackHeight);
        thumbOffset = (trackHeight - thumbHeight) * Math.Clamp(scrollOffset / max, 0.0, 1.0);
        return true;
    }

    private void DrawExtensionToggle(DrawingContext context, Rect rect, HitRegion hit, string label, bool isExpanded, double rowTop)
    {
        var header = OffsetY(rect, rowTop);
        var isEnabled = CanExecuteHit(hit);
        var isHovered = isEnabled && ReferenceEquals(hit, _hoveredHit);
        var buttonRect = new Rect(header.X, header.Y + 1.0, header.Width, Math.Max(1.0, header.Height - 2.0));
        var background = isHovered
            ? Resource("ChatDiagnosticButtonHoverBrush", HistoryActiveFallbackBrush)
            : Resource("ChatDiagnosticButtonBrush", CommandBackgroundFallbackBrush);
        var border = isHovered
            ? Resource("ChatActionHoverBorderBrush", AccentBorderFallbackBrush)
            : Resource("ChatDiagnosticBorderBrush", CommandBorderFallbackBrush);
        context.DrawRectangle(background, new Pen(border, 1), buttonRect, 5, 5);

        var codeFontFamily = ResolveFontFamily(CodeFontFamily, Resource("CodeFontFamily", DefaultCodeFontFamily));
        var titleBrush = isEnabled && isHovered
            ? Resource("ChatActionForegroundBrush", TextPrimaryFallbackBrush)
            : isEnabled
            ? Resource("ChatDiagnosticTitleBrush", TextMutedFallbackBrush)
            : Resource("TextMutedBrush", TextMutedFallbackBrush);
        var title = GetFormattedText(label, titleBrush, codeFontFamily, FontWeight.Black, FontStyle.Normal, 8.7);
        var titleY = CenterTextY(buttonRect, title);
        DrawClippedText(
            context,
            title,
            new Rect(buttonRect.X + 8.0, buttonRect.Y, Math.Max(0.0, buttonRect.Width - 34.0), buttonRect.Height),
            new Point(buttonRect.X + 8.0, titleY));

        var marker = GetFormattedText(isExpanded ? "-" : "+", Resource("ChatDiagnosticTitleBrush", TextMutedFallbackBrush), codeFontFamily, FontWeight.Black, FontStyle.Normal, 9.0);
        var markerRect = new Rect(buttonRect.Right - 18.0, buttonRect.Y + Math.Max(0.0, (buttonRect.Height - 13.0) * 0.5), 13.0, 13.0);
        context.DrawRectangle(
            Resource("ChatDiagnosticMarkerBrush", EditorSurfaceFallbackBrush),
            new Pen(Resource("ChatDiagnosticBorderBrush", CommandBorderFallbackBrush), 1),
            markerRect,
            4,
            4);
        var markerPoint = new Point(
            markerRect.X + Math.Max(0.0, (markerRect.Width - marker.Width) * 0.5),
            CenterTextY(markerRect, marker));
        DrawClippedText(
            context,
            marker,
            markerRect,
            markerPoint);
    }

    private void DrawTextBlock(
        DrawingContext context,
        TextBlockLayout block,
        double rowTop,
        double viewportTop,
        double viewportBottom,
        bool wrap = true)
    {
        var rect = OffsetY(block.Rect, rowTop);
        if (rect.Bottom < viewportTop || rect.Y > viewportBottom || rect.Width <= 0.0 || rect.Height <= 0.0)
        {
            return;
        }

        var font = block.UseCodeFont
            ? ResolveFontFamily(CodeFontFamily, Resource("CodeFontFamily", DefaultCodeFontFamily))
            : ResolveFontFamily(UiFontFamily, Resource("UiFontFamily", DefaultUiFontFamily));
        var firstLine = Math.Max(0, (int)Math.Floor((viewportTop - rect.Y) / block.LineHeight) - 1);
        var lastLine = Math.Min(block.Lines.Count - 1, (int)Math.Ceiling((viewportBottom - rect.Y) / block.LineHeight) + 1);
        if (lastLine < firstLine)
        {
            return;
        }

        using (context.PushClip(rect))
        {
            for (var lineIndex = firstLine; lineIndex <= lastLine; lineIndex++)
            {
                var line = block.Lines[lineIndex];
                if (string.IsNullOrEmpty(line))
                {
                    continue;
                }

                var renderedLine = wrap
                    ? line
                    : FitLineToWidth(line, rect.Width, block.Brush, font, block.Weight, block.Style, block.FontSize);
                var text = GetFormattedText(renderedLine, block.Brush, font, block.Weight, block.Style, block.FontSize);
                context.DrawText(text, new Point(rect.X, rect.Y + lineIndex * block.LineHeight));
            }
        }
    }

    private void DrawTextSelection(
        DrawingContext context,
        TextBlockLayout block,
        int rowIndex,
        int blockIndex,
        double rowTop,
        double viewportTop,
        double viewportBottom)
    {
        if (!TryGetSelectionRange(out var start, out var end)
            || rowIndex < start.RowIndex
            || rowIndex > end.RowIndex)
        {
            return;
        }

        var rect = OffsetY(block.Rect, rowTop);
        if (rect.Bottom < viewportTop || rect.Y > viewportBottom || rect.Width <= 0.0 || rect.Height <= 0.0)
        {
            return;
        }

        var font = block.UseCodeFont
            ? ResolveFontFamily(CodeFontFamily, Resource("CodeFontFamily", DefaultCodeFontFamily))
            : ResolveFontFamily(UiFontFamily, Resource("UiFontFamily", DefaultUiFontFamily));
        using (context.PushClip(rect))
        {
            for (var lineIndex = 0; lineIndex < block.Lines.Count; lineIndex++)
            {
                var line = block.Lines[lineIndex];
                var lineStart = new TextPosition(rowIndex, blockIndex, lineIndex, 0);
                var lineEnd = new TextPosition(rowIndex, blockIndex, lineIndex, line.Length);
                if (CompareTextPositions(lineEnd, start) <= 0 || CompareTextPositions(lineStart, end) >= 0)
                {
                    continue;
                }

                var startColumn = CompareTextPositions(start, lineStart) > 0 && start.RowIndex == rowIndex && start.BlockIndex == blockIndex && start.LineIndex == lineIndex
                    ? Math.Clamp(start.Column, 0, line.Length)
                    : 0;
                var endColumn = CompareTextPositions(end, lineEnd) < 0 && end.RowIndex == rowIndex && end.BlockIndex == blockIndex && end.LineIndex == lineIndex
                    ? Math.Clamp(end.Column, 0, line.Length)
                    : line.Length;
                if (endColumn <= startColumn)
                {
                    continue;
                }

                var prefixWidth = MeasureTextWidth(line[..startColumn], font, block.Weight, block.Style, block.FontSize);
                var selectedWidth = MeasureTextWidth(line[startColumn..endColumn], font, block.Weight, block.Style, block.FontSize);
                var highlight = new Rect(
                    rect.X + prefixWidth,
                    rect.Y + lineIndex * block.LineHeight,
                    Math.Max(2.0, selectedWidth),
                    block.LineHeight);
                context.DrawRectangle(TextSelectionFallbackBrush, null, highlight, 2, 2);
            }
        }
    }
}
