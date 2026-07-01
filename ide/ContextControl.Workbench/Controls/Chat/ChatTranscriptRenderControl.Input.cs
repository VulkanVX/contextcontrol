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
    private bool TryGetTextPosition(Point point, out TextPosition position)
    {
        position = default;
        EnsureLayoutCache(Bounds.Width);
        if (_itemCount == 0 || point.X < 0 || point.Y < 0 || point.X > Bounds.Width || point.Y > GetTotalHeight())
        {
            return false;
        }

        var rowIndex = FindRowIndexAtOrAfter(point.Y);
        if (rowIndex < 0 || rowIndex >= _itemCount)
        {
            return false;
        }

        var rowTop = GetRowTop(rowIndex);
        var layout = GetOrBuildLayout(rowIndex);
        if (layout is null)
        {
            return false;
        }

        foreach (var selectable in layout.SelectableTextBlocks)
        {
            var block = selectable.TextBlock;
            var rect = OffsetY(block.Rect, rowTop);
            if (point.Y < rect.Y || point.Y > rect.Bottom || point.X < rect.X - 6.0 || point.X > rect.Right + 6.0)
            {
                continue;
            }

            var lineIndex = Math.Clamp((int)Math.Floor((point.Y - rect.Y) / block.LineHeight), 0, block.Lines.Count - 1);
            var line = block.Lines[lineIndex];
            var column = CalculateColumnAtX(block, line, Math.Max(0.0, point.X - rect.X));
            position = new TextPosition(rowIndex, selectable.BlockIndex, lineIndex, column);
            return true;
        }

        return false;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        if (TryScrollExtensionPanel(e.GetPosition(this), e.Delta.Y))
        {
            e.Handled = true;
            return;
        }

        base.OnPointerWheelChanged(e);
    }

    private bool TryScrollExtensionPanel(Point point, double deltaY)
    {
        EnsureLayoutCache(Bounds.Width);
        if (_itemCount == 0 || point.X < 0 || point.Y < 0 || point.X > Bounds.Width || point.Y > GetTotalHeight())
        {
            return false;
        }

        var rowIndex = FindRowIndexAtOrAfter(point.Y);
        if (rowIndex < 0 || rowIndex >= _itemCount)
        {
            return false;
        }

        var rowTop = GetRowTop(rowIndex);
        var layout = GetOrBuildLayout(rowIndex);
        if (layout is null)
        {
            return false;
        }

        if (layout.Thinking is { TextBlock: { } thinkingBlock } thinking
            && OffsetY(thinking.BodyRect, rowTop).Contains(point)
            && ScrollExtensionPanel(thinking.Hit, thinkingBlock, _thinkingScrollOffsets, deltaY))
        {
            InvalidateVisual();
            return true;
        }

        if (layout.Diagnostic is { TextBlock: { } diagnosticBlock } diagnostic
            && OffsetY(diagnostic.BodyRect, rowTop).Contains(point)
            && ScrollExtensionPanel(diagnostic.Hit, diagnosticBlock, _diagnosticScrollOffsets, deltaY))
        {
            InvalidateVisual();
            return true;
        }

        return false;
    }

    private bool ScrollExtensionPanel(
        HitRegion hit,
        TextBlockLayout block,
        Dictionary<LocalLlmChatMessageViewModel, double> offsets,
        double deltaY)
    {
        if (hit.Parameter is not LocalLlmChatMessageViewModel message)
        {
            return false;
        }

        var max = GetMaxExtensionScrollOffset(block);
        if (max <= 0.5)
        {
            offsets.Remove(message);
            return false;
        }

        offsets.TryGetValue(message, out var current);
        var next = Math.Clamp(current - deltaY * block.LineHeight * 3.0, 0.0, max);
        if (Math.Abs(next - current) < 0.5)
        {
            return false;
        }

        offsets[message] = next;
        return true;
    }

    private bool TryResolveExtensionScrollbar(Point point, out ExtensionScrollbarHit scrollbar)
    {
        scrollbar = null!;
        EnsureLayoutCache(Bounds.Width);
        if (_itemCount == 0 || point.X < 0 || point.Y < 0 || point.X > Bounds.Width || point.Y > GetTotalHeight())
        {
            return false;
        }

        var rowIndex = FindRowIndexAtOrAfter(point.Y);
        if (rowIndex < 0 || rowIndex >= _itemCount)
        {
            return false;
        }

        var rowTop = GetRowTop(rowIndex);
        var layout = GetOrBuildLayout(rowIndex);
        if (layout is null)
        {
            return false;
        }

        if (layout.Thinking is { TextBlock: { } thinkingBlock } thinking
            && TryResolveExtensionScrollbar(point, rowTop, thinking.Hit, thinking.BodyRect, thinkingBlock, _thinkingScrollOffsets, out scrollbar))
        {
            return true;
        }

        if (layout.Diagnostic is { TextBlock: { } diagnosticBlock } diagnostic
            && TryResolveExtensionScrollbar(point, rowTop, diagnostic.Hit, diagnostic.BodyRect, diagnosticBlock, _diagnosticScrollOffsets, out scrollbar))
        {
            return true;
        }

        return false;
    }

    private bool TryResolveExtensionScrollbar(
        Point point,
        double rowTop,
        HitRegion hit,
        Rect bodyRect,
        TextBlockLayout block,
        Dictionary<LocalLlmChatMessageViewModel, double> offsets,
        out ExtensionScrollbarHit scrollbar)
    {
        scrollbar = null!;
        if (hit.Parameter is not LocalLlmChatMessageViewModel message)
        {
            return false;
        }

        var body = OffsetY(bodyRect, rowTop);
        var scrollOffset = GetExtensionScrollOffset(hit, block, offsets);
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
            return false;
        }

        var track = new Rect(body.Right - 8.0, body.Y + 7.0, 3.0, trackHeight);
        var hitTrack = new Rect(track.X - 5.0, track.Y, track.Width + 10.0, track.Height);
        if (!hitTrack.Contains(point))
        {
            return false;
        }

        var max = GetMaxExtensionScrollOffset(block);
        if (max <= 0.5)
        {
            return false;
        }

        scrollbar = new ExtensionScrollbarHit(message, offsets, track.Y, track.Height, thumbHeight, max, track.Y + thumbOffset);
        return true;
    }

    private void BeginExtensionScrollbarDrag(ExtensionScrollbarHit scrollbar, double pointerY)
    {
        var pointerOffset = pointerY >= scrollbar.ThumbY && pointerY <= scrollbar.ThumbY + scrollbar.ThumbHeight
            ? pointerY - scrollbar.ThumbY
            : scrollbar.ThumbHeight * 0.5;
        _extensionScrollDrag = new ExtensionScrollDrag(
            scrollbar.Message,
            scrollbar.Offsets,
            scrollbar.TrackY,
            scrollbar.TrackHeight,
            scrollbar.ThumbHeight,
            scrollbar.MaxOffset,
            pointerOffset);
        UpdateExtensionScrollbarDrag(pointerY);
    }

    private bool UpdateExtensionScrollbarDrag(double pointerY)
    {
        if (_extensionScrollDrag is not { } drag)
        {
            return false;
        }

        var travel = Math.Max(0.0, drag.TrackHeight - drag.ThumbHeight);
        var ratio = travel <= 0.0
            ? 0.0
            : Math.Clamp((pointerY - drag.TrackY - drag.PointerOffsetWithinThumb) / travel, 0.0, 1.0);
        drag.Offsets[drag.Message] = ratio * drag.MaxOffset;
        InvalidateVisual();
        return true;
    }

    private int CalculateColumnAtX(TextBlockLayout block, string line, double x)
    {
        if (string.IsNullOrEmpty(line) || x <= 0.0)
        {
            return 0;
        }

        var font = block.UseCodeFont
            ? ResolveFontFamily(CodeFontFamily, Resource("CodeFontFamily", DefaultCodeFontFamily))
            : ResolveFontFamily(UiFontFamily, Resource("UiFontFamily", DefaultUiFontFamily));
        var lo = 0;
        var hi = line.Length;
        while (lo < hi)
        {
            var mid = lo + ((hi - lo + 1) / 2);
            if (MeasureTextWidth(line[..mid], font, block.Weight, block.Style, block.FontSize) <= x)
            {
                lo = mid;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return lo;
    }

    private bool HasTextSelection => TryGetSelectionRange(out _, out _);

    private bool TryGetSelectionRange(out TextPosition start, out TextPosition end)
    {
        start = default;
        end = default;
        if (_selectionAnchor is not { } anchor || _selectionActive is not { } active)
        {
            return false;
        }

        if (CompareTextPositions(anchor, active) == 0)
        {
            return false;
        }

        if (CompareTextPositions(anchor, active) <= 0)
        {
            start = anchor;
            end = active;
        }
        else
        {
            start = active;
            end = anchor;
        }

        return true;
    }

    private void ClearTextSelection()
    {
        if (_selectionAnchor is null && _selectionActive is null && !_isSelectingText)
        {
            return;
        }

        _selectionAnchor = null;
        _selectionActive = null;
        _isSelectingText = false;
        InvalidateVisual();
    }

    private async Task CopySelectedTextAsync()
    {
        var selected = BuildSelectedText();
        if (string.IsNullOrWhiteSpace(selected))
        {
            return;
        }

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is not null)
        {
            await clipboard.SetTextAsync(selected);
        }
    }

    private string BuildSelectedText()
    {
        if (!TryGetSelectionRange(out var start, out var end))
        {
            return "";
        }

        EnsureLayoutCache(Bounds.Width);
        var builder = new List<string>();
        for (var rowIndex = start.RowIndex; rowIndex <= end.RowIndex && rowIndex < _itemCount; rowIndex++)
        {
            var layout = GetOrBuildLayout(rowIndex);
            if (layout is null)
            {
                continue;
            }

            foreach (var selectable in layout.SelectableTextBlocks)
            {
                var block = selectable.TextBlock;
                for (var lineIndex = 0; lineIndex < block.Lines.Count; lineIndex++)
                {
                    var line = block.Lines[lineIndex];
                    var lineStart = new TextPosition(rowIndex, selectable.BlockIndex, lineIndex, 0);
                    var lineEnd = new TextPosition(rowIndex, selectable.BlockIndex, lineIndex, line.Length);
                    if (CompareTextPositions(lineEnd, start) <= 0 || CompareTextPositions(lineStart, end) >= 0)
                    {
                        continue;
                    }

                    var startColumn = start.RowIndex == rowIndex && start.BlockIndex == selectable.BlockIndex && start.LineIndex == lineIndex
                        ? Math.Clamp(start.Column, 0, line.Length)
                        : 0;
                    var endColumn = end.RowIndex == rowIndex && end.BlockIndex == selectable.BlockIndex && end.LineIndex == lineIndex
                        ? Math.Clamp(end.Column, 0, line.Length)
                        : line.Length;
                    if (endColumn > startColumn)
                    {
                        builder.Add(line[startColumn..endColumn]);
                    }
                }
            }
        }

        return string.Join(Environment.NewLine, builder);
    }

    private static int CompareTextPositions(TextPosition left, TextPosition right)
    {
        var row = left.RowIndex.CompareTo(right.RowIndex);
        if (row != 0)
        {
            return row;
        }

        var block = left.BlockIndex.CompareTo(right.BlockIndex);
        if (block != 0)
        {
            return block;
        }

        var line = left.LineIndex.CompareTo(right.LineIndex);
        return line != 0 ? line : left.Column.CompareTo(right.Column);
    }

    private void DrawButton(DrawingContext context, Rect rect, string label, bool isEnabled, bool isHovered)
    {
        var background = isHovered && isEnabled
            ? Resource("ChatActionHoverBrush", HistoryActiveFallbackBrush)
            : TransparentBrush;
        var border = isHovered && isEnabled
            ? Resource("ChatActionHoverBorderBrush", AccentBorderFallbackBrush)
            : Resource("ChatActionBorderBrush", CommandBorderFallbackBrush);
        var foreground = isEnabled
            ? Resource("ChatActionForegroundBrush", TextPrimaryFallbackBrush)
            : Resource("ChatActionDisabledForegroundBrush", TextMutedFallbackBrush);
        context.DrawRectangle(background, new Pen(border, 1), rect, 4, 4);

        var font = ResolveFontFamily(UiFontFamily, Resource("UiFontFamily", DefaultUiFontFamily));
        var text = GetFormattedText(CleanOneLine(label, 24), foreground, font, FontWeight.ExtraBold, FontStyle.Normal, 8.2);
        DrawClippedText(
            context,
            text,
            new Rect(rect.X + 3.0, rect.Y, Math.Max(0.0, rect.Width - 6.0), rect.Height),
            new Point(rect.X + Math.Max(3.0, (rect.Width - text.Width) * 0.5), CenterTextY(rect, text)));
    }

    private void DrawCopyIconButton(DrawingContext context, Rect rect, bool isEnabled, bool isHovered)
    {
        var background = isHovered && isEnabled
            ? Resource("ChatActionHoverBrush", HistoryActiveFallbackBrush)
            : TransparentBrush;
        var border = isHovered && isEnabled
            ? Resource("ChatActionHoverBorderBrush", AccentBorderFallbackBrush)
            : Resource("ChatActionBorderBrush", CommandBorderFallbackBrush);
        var foreground = isEnabled && isHovered
            ? Resource("ChatActionForegroundBrush", TextPrimaryFallbackBrush)
            : isEnabled
            ? Resource("ChatMetaBrush", TextMutedFallbackBrush)
            : Resource("ChatActionDisabledForegroundBrush", TextMutedFallbackBrush);

        if (isHovered && isEnabled)
        {
            context.DrawRectangle(background, new Pen(border, 1), rect, 3, 3);
        }

        var x = rect.X + Math.Max(0.0, (rect.Width - 8.0) * 0.5);
        var y = rect.Y + Math.Max(0.0, (rect.Height - 8.5) * 0.5);
        var pen = new Pen(foreground, 0.9);
        context.DrawRectangle(null, pen, new Rect(x, y, 5.5, 6.4), 1.0, 1.0);
        context.DrawRectangle(null, pen, new Rect(x + 2.3, y + 2.3, 5.5, 6.4), 1.0, 1.0);
    }

    private void DrawToolPhaseIcon(DrawingContext context, Rect rect)
    {
        var foreground = Resource("ChatMetaBrush", TextMutedFallbackBrush);
        var pen = new Pen(foreground, 0.95);
        var x = rect.X + Math.Max(0.0, (rect.Width - 9.5) * 0.5);
        var y = rect.Y + Math.Max(0.0, (rect.Height - 8.5) * 0.5);
        var shell = new Rect(x, y, 9.5, 8.5);
        context.DrawRectangle(null, pen, shell, 1.2, 1.2);

        var promptX = shell.X + 2.2;
        var promptY = shell.Y + 2.1;
        context.DrawLine(pen, new Point(promptX, promptY), new Point(promptX + 2.0, promptY + 1.9));
        context.DrawLine(pen, new Point(promptX, promptY + 3.8), new Point(promptX + 2.0, promptY + 1.9));
        context.DrawLine(pen, new Point(promptX + 4.1, promptY + 4.1), new Point(promptX + 7.0, promptY + 4.1));
    }

    private void DrawDownloadIconButton(DrawingContext context, Rect rect, bool isEnabled, bool isHovered)
    {
        var background = isHovered && isEnabled
            ? Resource("ChatActionHoverBrush", HistoryActiveFallbackBrush)
            : TransparentBrush;
        var border = isHovered && isEnabled
            ? Resource("ChatActionHoverBorderBrush", AccentBorderFallbackBrush)
            : Resource("ChatActionBorderBrush", CommandBorderFallbackBrush);
        var foreground = isEnabled && isHovered
            ? Resource("ChatActionForegroundBrush", TextPrimaryFallbackBrush)
            : isEnabled
            ? Resource("ChatMetaBrush", TextMutedFallbackBrush)
            : Resource("ChatActionDisabledForegroundBrush", TextMutedFallbackBrush);

        if (isHovered && isEnabled)
        {
            context.DrawRectangle(background, new Pen(border, 1), rect, 4, 4);
        }

        var centerX = rect.X + rect.Width * 0.5;
        var top = rect.Y + 4.2;
        var pen = new Pen(foreground, 1.25);
        context.DrawLine(pen, new Point(centerX, top), new Point(centerX, top + 7.0));
        context.DrawLine(pen, new Point(centerX - 3.2, top + 4.2), new Point(centerX, top + 7.4));
        context.DrawLine(pen, new Point(centerX + 3.2, top + 4.2), new Point(centerX, top + 7.4));
        context.DrawLine(pen, new Point(centerX - 4.5, rect.Bottom - 5.0), new Point(centerX + 4.5, rect.Bottom - 5.0));
    }

    private void DrawEmptyState(DrawingContext context, double viewportTop, double viewportHeight)
    {
        var uiFontFamily = ResolveFontFamily(UiFontFamily, Resource("UiFontFamily", DefaultUiFontFamily));
        var text = GetFormattedText("No messages in this chat.", Resource("TextMutedBrush", TextMutedFallbackBrush), uiFontFamily, FontWeight.SemiBold, FontStyle.Normal, 11.0);
        var x = Math.Max(8.0, (Bounds.Width - text.Width) * 0.5);
        var y = viewportTop + Math.Max(24.0, (viewportHeight - text.Height) * 0.35);
        DrawClippedText(context, text, new Rect(0, viewportTop, Bounds.Width, viewportHeight), new Point(x, y));
    }

    private HitRegion? HitTest(Point point)
    {
        EnsureLayoutCache(Bounds.Width);
        if (_itemCount == 0 || point.X < 0 || point.Y < 0 || point.X > Bounds.Width || point.Y > GetTotalHeight())
        {
            return null;
        }

        var index = FindRowIndexAtOrAfter(point.Y);
        if (index < 0 || index >= _itemCount)
        {
            return null;
        }

        var rowTop = GetRowTop(index);
        var layout = GetOrBuildLayout(index);
        if (layout is null)
        {
            return null;
        }

        var cardRect = OffsetY(layout.CardRect, rowTop);
        if (point.Y >= rowTop + GetRowHeight(index) && index + 1 < _itemCount)
        {
            index = FindRowIndexAtOrAfter(point.Y);
            rowTop = GetRowTop(index);
            layout = GetOrBuildLayout(index);
            if (layout is null)
            {
                return null;
            }

            cardRect = OffsetY(layout.CardRect, rowTop);
        }

        foreach (var hit in layout.Hits)
        {
            if (!cardRect.Contains(point))
            {
                continue;
            }

            if (OffsetY(hit.Rect, rowTop).Contains(point) && CanExecuteHit(hit))
            {
                return hit;
            }
        }

        return null;
    }

    private bool CanExecuteHit(HitRegion hit)
    {
        if (hit.Kind == ChatTranscriptHitKind.ToggleMessageCollapse)
        {
            return hit.Parameter is LocalLlmChatMessageViewModel;
        }

        if (hit.Kind is ChatTranscriptHitKind.OpenImagePreview or ChatTranscriptHitKind.DownloadAttachment)
        {
            return hit.Parameter is string path && File.Exists(path);
        }

        return ResolveCommand(hit.Kind) is { } command && command.CanExecute(hit.Parameter);
    }

    private void ExecuteHit(HitRegion hit)
    {
        if (hit.Kind == ChatTranscriptHitKind.ToggleMessageCollapse && hit.Parameter is LocalLlmChatMessageViewModel message)
        {
            ToggleMessageCollapse(message);
            return;
        }

        if (hit.Kind == ChatTranscriptHitKind.ToggleSnippet && hit.Parameter is ChatSnippetViewModel snippet)
        {
            ExecuteSnippetToggle(snippet);
            return;
        }

        if (hit.Kind == ChatTranscriptHitKind.ToggleThinking && hit.Parameter is LocalLlmChatMessageViewModel thinkingMessage)
        {
            ExecuteThinkingToggle(thinkingMessage);
            return;
        }

        if (hit.Kind == ChatTranscriptHitKind.ToggleDiagnostic && hit.Parameter is LocalLlmChatMessageViewModel diagnosticMessage)
        {
            ExecuteDiagnosticToggle(diagnosticMessage);
            return;
        }

        if (hit.Kind == ChatTranscriptHitKind.OpenImagePreview && hit.Parameter is string imagePath)
        {
            OpenImagePreviewWindow(imagePath);
            return;
        }

        if (hit.Kind == ChatTranscriptHitKind.DownloadAttachment && hit.Parameter is string downloadPath)
        {
            _ = DownloadImageAsync(downloadPath);
            return;
        }

        var command = ResolveCommand(hit.Kind);
        if (command?.CanExecute(hit.Parameter) == true)
        {
            command.Execute(hit.Parameter);
        }
    }

    private void ExecuteSnippetToggle(ChatSnippetViewModel snippet)
    {
        var command = ToggleSnippetCommand;
        if (command?.CanExecute(snippet) != true)
        {
            return;
        }

        BeginSnippetAnimation(snippet, !snippet.IsExpanded);
        command.Execute(snippet);
    }

    private void ExecuteThinkingToggle(LocalLlmChatMessageViewModel message)
    {
        var command = ToggleThinkingCommand;
        if (command?.CanExecute(message) != true)
        {
            return;
        }

        BeginThinkingAnimation(message, !message.IsThinkingExpanded);
        command.Execute(message);
    }

    private void ExecuteDiagnosticToggle(LocalLlmChatMessageViewModel message)
    {
        var command = ToggleDiagnosticCommand;
        if (command?.CanExecute(message) != true)
        {
            return;
        }

        BeginDiagnosticAnimation(message, !message.IsDiagnosticExpanded);
        command.Execute(message);
    }

    private ICommand? ResolveCommand(ChatTranscriptHitKind kind)
    {
        return kind switch
        {
            ChatTranscriptHitKind.OpenAttachment => OpenAttachmentCommand,
            ChatTranscriptHitKind.CopyMessage => CopyChatTextCommand,
            ChatTranscriptHitKind.CreateProject => CreateProjectFromMessageCommand,
            ChatTranscriptHitKind.ToggleSnippet => ToggleSnippetCommand,
            ChatTranscriptHitKind.CopySnippet => CopySnippetCommand,
            ChatTranscriptHitKind.SaveSnippet => SaveSnippetAsCommand,
            ChatTranscriptHitKind.UseSnippet => UseSnippetForCcCommand,
            ChatTranscriptHitKind.PreviewSnippet => PreviewSnippetCommand,
            ChatTranscriptHitKind.ApplyEffectivePatch => ApplyPatchCommand,
            ChatTranscriptHitKind.ApplyAllPatch => ApplyAllPatchCommand,
            ChatTranscriptHitKind.OpenPatchPlanFile => OpenPatchPlanFileCommand,
            ChatTranscriptHitKind.ToggleThinking => ToggleThinkingCommand,
            ChatTranscriptHitKind.ToggleDiagnostic => ToggleDiagnosticCommand,
            _ => null
        };
    }

    private void SetHoveredHit(HitRegion? hit)
    {
        if (ReferenceEquals(_hoveredHit, hit))
        {
            return;
        }

        _hoveredHit = hit;
        Cursor = hit is null ? ArrowCursor : HandCursor;
        InvalidateVisual();
    }
}
