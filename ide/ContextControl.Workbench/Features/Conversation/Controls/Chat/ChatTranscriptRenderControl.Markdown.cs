using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Utilities;
using ContextControl.Workbench.Services;
using ContextControl.Workbench.ViewModels;

namespace ContextControl.Workbench.Controls;

public sealed partial class ChatTranscriptRenderControl
{
    private readonly ConditionalWeakTable<LocalLlmChatPartViewModel, MarkdownBlocks> _markdownCache = new();
    private sealed record MarkdownBlocks(IReadOnlyList<ChatMarkdownBlock> Blocks);
    private sealed record RichTextLayout(TextLayout Layout, string PlainText, int[] LineStarts);
    private sealed record MarkdownDecoration(Rect Rect, string Kind);

    private double BuildMarkdown(MessageLayout layout, LocalLlmChatPartViewModel part, double x, double y, double width)
    {
        var blocks = _markdownCache.GetValue(part, static item => new(ChatMarkdown.Parse(item.Text))).Blocks;
        return BuildMarkdownBlocks(layout, blocks, x, y, width);
    }

    private double BuildMarkdownBlocks(MessageLayout layout, IReadOnlyList<ChatMarkdownBlock> blocks, double x, double y, double width)
    {
        foreach (var block in blocks)
        {
            switch (block.Kind)
            {
                case "card":
                    y = BuildInformationCard(layout, block, x, y, width);
                    break;
                case "table":
                    y = BuildMarkdownTable(layout, block.Children ?? [], x, y, width);
                    break;
                case "listItem":
                    var indent = Math.Min(width * .15, ChatTextFontSize * 2.1);
                    var top = y;
                    _ = AddRichParagraph(layout, [new(block.Marker)], x, y, indent, ChatTextFontSize);
                    y = BuildMarkdownBlocks(layout, block.Children ?? [], x + indent, y, Math.Max(1, width - indent));
                    y = Math.Max(y, top + ChatTextLineHeight + 4);
                    break;
                case "quote":
                    var quoteTop = y;
                    y = BuildMarkdownBlocks(layout, block.Children ?? [], x + 14, y + 6, Math.Max(1, width - 22)) + 4;
                    layout.MarkdownDecorations.Add(new(new Rect(x, quoteTop, width, y - quoteTop), "quote"));
                    y += 6;
                    break;
                case "rule":
                    layout.MarkdownDecorations.Add(new(new Rect(x, y + 6, width, 1), "rule"));
                    y += 16;
                    break;
                default:
                    var size = block.Kind == "heading" ? ChatTextFontSize * (block.Level switch { 1 => 1.5, 2 => 1.3, _ => 1.15 }) : ChatTextFontSize;
                    if (block.Kind == "heading") y += 6;
                    y = AddRichParagraph(layout, block.Runs, x, y, width, size);
                    break;
            }
        }
        return y;
    }

    private double AddRichParagraph(MessageLayout layout, IReadOnlyList<ChatMarkdownRun> input, double x, double y, double width, double size)
    {
        var runs = ResolveCitationRuns(input, layout.Message);
        var plain = string.Concat(runs.Select(run => run.Text));
        if (string.IsNullOrWhiteSpace(plain)) return y;
        var uiFont = ResolveFontFamily(UiFontFamily, Resource("UiFontFamily", DefaultUiFontFamily));
        var codeFont = ResolveFontFamily(CodeFontFamily, Resource("CodeFontFamily", DefaultCodeFontFamily));
        var bodyBrush = Resource("ChatBodyTextBrush", TextPrimaryFallbackBrush);
        var linkBrush = Resource("AccentBrush", AccentBorderFallbackBrush);
        var styles = new List<ValueSpan<TextRunProperties>>();
        var offset = 0;
        foreach (var run in runs)
        {
            var decorations = new TextDecorationCollection();
            if (run.Strike) decorations.AddRange(TextDecorations.Strikethrough);
            if (run.Url is not null) decorations.AddRange(TextDecorations.Underline);
            var properties = new GenericTextRunProperties(
                new Typeface(run.Code ? codeFont : uiFont, run.Italic ? FontStyle.Italic : FontStyle.Normal, run.Bold ? FontWeight.Bold : FontWeight.Normal),
                size, decorations, run.Url is not null ? linkBrush : bodyBrush,
                run.Code ? Resource("ChatSnippetBodyBrush", CommandBackgroundFallbackBrush) : null);
            styles.Add(new ValueSpan<TextRunProperties>(offset, run.Text.Length, properties));
            offset += run.Text.Length;
        }
        var lineHeight = Math.Max(ChatTextLineHeight, size * 1.5);
        var text = new TextLayout(plain, new Typeface(uiFont), size, bodyBrush, textWrapping: TextWrapping.Wrap,
            maxWidth: Math.Max(1, width), lineHeight: lineHeight, textStyleOverrides: styles);
        var starts = text.TextLines.Select(line => line.FirstTextSourceIndex).ToArray();
        var lines = text.TextLines.Select(line => plain.Substring(Math.Min(plain.Length, line.FirstTextSourceIndex),
            Math.Max(0, Math.Min(line.Length, plain.Length - line.FirstTextSourceIndex))).TrimEnd('\r', '\n')).ToArray();
        var height = Math.Max(lineHeight, text.Height);
        var rect = new Rect(x, y, width, height);
        AddSelectableTextBlock(layout, new TextBlockLayout(rect, lines, size, lineHeight, FontWeight.Normal, FontStyle.Normal, false, bodyBrush,
            new RichTextLayout(text, plain, starts)), drawInMessage: true);
        offset = 0;
        foreach (var run in runs)
        {
            if (run.Url is not null)
                foreach (var hit in text.HitTestTextRange(offset, run.Text.Length))
                    layout.Hits.Add(new HitRegion(new Rect(x + hit.X, y + hit.Y, hit.Width, hit.Height), ChatTranscriptHitKind.OpenAttachment, run.Url));
            offset += run.Text.Length;
        }
        return y + height + Math.Max(5, size * .35);
    }

    private static IReadOnlyList<ChatMarkdownRun> ResolveCitationRuns(IReadOnlyList<ChatMarkdownRun> runs, LocalLlmChatMessageViewModel message)
    {
        var result = new List<ChatMarkdownRun>();
        foreach (var run in runs)
        {
            if (run.Url is not null || run.Code) { result.Add(run); continue; }
            var cursor = 0;
            foreach (Match match in Regex.Matches(run.Text, @"\[(\d{1,2})\]"))
            {
                var source = message.AttachedFiles.FirstOrDefault(attachment => attachment.Kind == "web" && attachment.Label.StartsWith(match.Value + " ", StringComparison.Ordinal));
                if (source is null || !GoogleSearchContext.IsPublicWebUrl(source.Path)) continue;
                if (match.Index > cursor) result.Add(run with { Text = run.Text[cursor..match.Index] });
                result.Add(run with { Text = match.Value, Url = source.Path });
                cursor = match.Index + match.Length;
            }
            if (cursor < run.Text.Length) result.Add(run with { Text = run.Text[cursor..] });
        }
        return result;
    }

    private double BuildInformationCard(MessageLayout layout, ChatMarkdownBlock block, double x, double y, double width)
    {
        var top = y;
        const double padding = 12;
        var innerWidth = Math.Max(1, width - padding * 2);
        var photo = FindEntryPhoto(block, layout.Message);
        var sidePhoto = photo is not null && innerWidth >= 450;
        var textWidth = sidePhoto ? innerWidth - 180 : innerWidth;
        y = AddRichParagraph(layout, [new(block.Marker + " " + block.PlainText, Bold: true)], x + padding, y + padding, textWidth, ChatTextFontSize * 1.14);
        var photoBottom = y;
        if (photo is not null)
        {
            var rect = sidePhoto ? new Rect(x + width - padding - 164, top + padding, 164, 112)
                : new Rect(x + padding, y, Math.Min(232, innerWidth), 140);
            layout.Attachments.Add(new AttachmentLayout(rect, photo.DisplayTitle, photo, true));
            layout.Hits.Add(new HitRegion(rect, ChatTranscriptHitKind.OpenImagePreview, photo.PreviewPath));
            layout.EmbeddedWebPhotos.Add(photo.Path);
            photoBottom = AddRichParagraph(layout, [new("Photo source", Url: photo.Path)], rect.X, rect.Bottom + 3, rect.Width, ChatMetaFontSize);
            if (!sidePhoto) y = photoBottom;
        }
        y = BuildMarkdownBlocks(layout, block.Children ?? [], x + padding, y, textWidth);
        var bottom = Math.Max(y, photoBottom) + padding - 4;
        layout.MarkdownDecorations.Add(new(new Rect(x, top, width, bottom - top), "card"));
        return bottom + 10;
    }

    private ContextControlAttachmentViewModel? FindEntryPhoto(ChatMarkdownBlock block, LocalLlmChatMessageViewModel message, bool allowCitedSource = true)
    {
        var entry = GoogleEntryPhotoService.NormalizeName(block.PlainText);
        var matched = message.AttachedFiles.FirstOrDefault(source => source.Kind == "web" && !string.IsNullOrWhiteSpace(source.EntryTitle)
            && GoogleEntryPhotoService.NormalizeName(source.EntryTitle) == entry && TryGetImageBitmap(source.PreviewPath) is not null);
        if (matched is not null) return matched;
        if (!allowCitedSource) return null;
        static string Name(string value) => string.Concat(value.Where(char.IsLetterOrDigit)).ToLowerInvariant();
        static string Details(ChatMarkdownBlock value) => value.PlainText + string.Concat((value.Children ?? []).Select(Details));
        var name = Name(block.PlainText);
        if (name.Length < 8) return null;
        var body = Details(block);
        return message.AttachedFiles.FirstOrDefault(source => source.Kind == "web" && Name(source.Label).Contains(name, StringComparison.Ordinal)
            && source.Label.StartsWith('[') && source.Label.IndexOf(']') is var end && end > 0 && body.Contains(source.Label[..(end + 1)], StringComparison.Ordinal)
            && TryGetImageBitmap(source.PreviewPath) is not null);
    }

    private double BuildMarkdownTable(MessageLayout layout, IReadOnlyList<ChatMarkdownBlock> rows, double x, double y, double width)
    {
        // A list of named places may arrive as a table. Once a related photo is
        // available, keep each row's fields together in a photo-bearing entry card.
        var cards = GoogleEntryPhotoService.TableCards(rows);
        if (cards.Any(card => FindEntryPhoto(card, layout.Message, allowCitedSource: false) is not null))
            return BuildMarkdownBlocks(layout, cards, x, y, width);
        var columns = rows.Select(row => row.Children?.Count ?? 0).DefaultIfEmpty(0).Max();
        if (columns == 0) return y;
        var headers = rows.FirstOrDefault(row => row.Kind == "tableHeader")?.Children ?? [];
        var stacked = width / columns < Math.Max(110, ChatTextFontSize * 9);
        foreach (var row in rows)
        {
            if (stacked && row.Kind == "tableHeader") continue;
            var top = y;
            var bottom = y;
            var cells = row.Children ?? [];
            for (var i = 0; i < cells.Count; i++)
            {
                var runs = cells[i].Runs;
                if (row.Kind == "tableHeader") runs = runs.Select(run => run with { Bold = true }).ToArray();
                if (stacked)
                {
                    var labelled = i < headers.Count ? new[] { new ChatMarkdownRun(headers[i].PlainText + ": ", Bold: true) }.Concat(runs).ToArray() : runs;
                    bottom = AddRichParagraph(layout, labelled, x + 9, bottom + 4, Math.Max(1, width - 18), ChatTextFontSize);
                }
                else
                {
                    var cellWidth = width / columns;
                    bottom = Math.Max(bottom, AddRichParagraph(layout, runs, x + cellWidth * i + 8, top + 7, Math.Max(1, cellWidth - 16), ChatTextFontSize));
                }
            }
            y = bottom + 7;
            layout.MarkdownDecorations.Add(new(new Rect(x, top, width, y - top), row.Kind == "tableHeader" ? "tableHeader" : "tableRow"));
        }
        return y + 8;
    }

    private void ClearRichLayouts()
    {
        foreach (var layout in _layoutCache.Values) DisposeRichLayout(layout);
        _layoutCache.Clear();
    }

    private void RemoveRichLayout(int index)
    {
        if (_layoutCache.Remove(index, out var layout)) DisposeRichLayout(layout);
    }

    private static void DisposeRichLayout(MessageLayout layout)
    {
        foreach (var block in layout.TextBlocks) block.TextBlock.Rich?.Layout.Dispose();
    }
}
