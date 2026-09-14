// CC-DESC: Lightweight Markdown-ish chat text renderer for local LLM responses.

using System.Text;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace ContextControl.Workbench.Controls;

public sealed partial class MarkdownTextBlock : SelectableTextBlock
{
    public static readonly StyledProperty<string> MarkdownProperty =
        AvaloniaProperty.Register<MarkdownTextBlock, string>(nameof(Markdown), "");

    public static readonly StyledProperty<string> CodeFontFamilyKeyProperty =
        AvaloniaProperty.Register<MarkdownTextBlock, string>(nameof(CodeFontFamilyKey), "Consolas");

    public static readonly StyledProperty<string> ThemeKeyProperty =
        AvaloniaProperty.Register<MarkdownTextBlock, string>(nameof(ThemeKey), "empty");

    private static readonly char[] InlineStopCharacters = ['`', '*', '_', '~', '[', '!', '<', '=', '+', '\\'];

    public string Markdown
    {
        get => GetValue(MarkdownProperty);
        set => SetValue(MarkdownProperty, value);
    }

    public string CodeFontFamilyKey
    {
        get => GetValue(CodeFontFamilyKeyProperty);
        set => SetValue(CodeFontFamilyKeyProperty, value);
    }

    public string ThemeKey
    {
        get => GetValue(ThemeKeyProperty);
        set => SetValue(ThemeKeyProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == MarkdownProperty
            || change.Property == CodeFontFamilyKeyProperty
            || change.Property == ThemeKeyProperty
            || change.Property == FontSizeProperty)
        {
            RebuildInlines();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        RebuildInlines();
    }

    private void RebuildInlines()
    {
        Inlines = new InlineCollection();
        Text = "";

        var text = Normalize(Markdown);
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var blocks = BuildBlocks(text);
        var wroteBlock = false;
        foreach (var block in blocks)
        {
            if (block.Kind == MarkdownBlockKind.Blank)
            {
                AddBreak();
                wroteBlock = false;
                continue;
            }

            if (wroteBlock)
            {
                AddBreak();
            }

            AppendBlock(block);
            wroteBlock = true;
        }
    }

    private IReadOnlyList<MarkdownBlock> BuildBlocks(string text)
    {
        var lines = text.Split('\n');
        var blocks = new List<MarkdownBlock>();
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            if (string.IsNullOrWhiteSpace(line))
            {
                if (blocks.Count == 0 || blocks[^1].Kind != MarkdownBlockKind.Blank)
                {
                    blocks.Add(new MarkdownBlock(MarkdownBlockKind.Blank, ""));
                }

                continue;
            }

            if (TryReadTable(lines, ref index, out var table))
            {
                blocks.Add(new MarkdownBlock(MarkdownBlockKind.Table, table));
                continue;
            }

            if (TryReadSetextHeading(lines, index, out var setextHeading, out var setextLevel))
            {
                blocks.Add(new MarkdownBlock(MarkdownBlockKind.Heading, setextHeading, setextLevel));
                index++;
                continue;
            }

            var heading = HeadingRegex().Match(line);
            if (heading.Success)
            {
                var level = Math.Clamp(heading.Groups["level"].Value.Length, 1, 6);
                blocks.Add(new MarkdownBlock(
                    MarkdownBlockKind.Heading,
                    TrimClosingHeadingMarks(heading.Groups["text"].Value),
                    level));
                continue;
            }

            if (ThematicBreakRegex().IsMatch(line))
            {
                blocks.Add(new MarkdownBlock(MarkdownBlockKind.Rule, ""));
                continue;
            }

            if (QuoteRegex().IsMatch(line))
            {
                var quoteLines = new List<string>();
                for (; index < lines.Length; index++)
                {
                    var quote = QuoteRegex().Match(lines[index]);
                    if (!quote.Success && !string.IsNullOrWhiteSpace(lines[index]))
                    {
                        index--;
                        break;
                    }

                    if (!quote.Success)
                    {
                        quoteLines.Add("");
                        continue;
                    }

                    quoteLines.Add(quote.Groups["text"].Value);
                }

                blocks.Add(new MarkdownBlock(MarkdownBlockKind.Quote, string.Join('\n', quoteLines).Trim()));
                continue;
            }

            if (ListLineRegex().IsMatch(line))
            {
                var listLines = new List<string>();
                for (; index < lines.Length; index++)
                {
                    if (!ListLineRegex().IsMatch(lines[index]) && !string.IsNullOrWhiteSpace(lines[index]))
                    {
                        index--;
                        break;
                    }

                    if (string.IsNullOrWhiteSpace(lines[index]))
                    {
                        break;
                    }

                    listLines.Add(lines[index]);
                }

                blocks.Add(new MarkdownBlock(MarkdownBlockKind.List, FormatListLines(listLines)));
                continue;
            }

            var paragraph = new List<string> { line.Trim() };
            for (var next = index + 1; next < lines.Length; next++)
            {
                if (string.IsNullOrWhiteSpace(lines[next])
                    || HeadingRegex().IsMatch(lines[next])
                    || ThematicBreakRegex().IsMatch(lines[next])
                    || QuoteRegex().IsMatch(lines[next])
                    || ListLineRegex().IsMatch(lines[next])
                    || (next + 1 < lines.Length && TableSeparatorRegex().IsMatch(lines[next + 1]))
                    || (next + 1 < lines.Length && SetextHeadingRegex().IsMatch(lines[next + 1])))
                {
                    break;
                }

                paragraph.Add(lines[next].Trim());
                index = next;
            }

            blocks.Add(new MarkdownBlock(MarkdownBlockKind.Paragraph, string.Join(' ', paragraph)));
        }

        while (blocks.Count > 0 && blocks[^1].Kind == MarkdownBlockKind.Blank)
        {
            blocks.RemoveAt(blocks.Count - 1);
        }

        return blocks;
    }

    private void AppendBlock(MarkdownBlock block)
    {
        switch (block.Kind)
        {
            case MarkdownBlockKind.Heading:
                AppendInlineText(
                    block.Text,
                    new InlineStyle(
                        Bold: true,
                        Italic: false,
                        Code: false,
                        Strike: false,
                        Underline: false,
                        FontScale: block.Level switch
                        {
                            1 => 1.45,
                            2 => 1.30,
                            3 => 1.18,
                            4 => 1.10,
                            _ => 1.02
                        }));
                break;
            case MarkdownBlockKind.Quote:
                AppendMultiline(block.Text, new InlineStyle(false, true, false, false, false, 1.0), "  ");
                break;
            case MarkdownBlockKind.List:
                AppendMultiline(block.Text, InlineStyle.Default, "");
                break;
            case MarkdownBlockKind.Table:
                AppendRun(block.Text, new InlineStyle(false, false, true, false, false, 1.0));
                break;
            case MarkdownBlockKind.Rule:
                AddBreak();
                break;
            default:
                AppendInlineText(block.Text, InlineStyle.Default);
                break;
        }
    }

    private void AppendMultiline(string text, InlineStyle style, string prefix)
    {
        var lines = text.Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            if (index > 0)
            {
                AddBreak();
            }

            if (!string.IsNullOrEmpty(prefix))
            {
                AppendRun(prefix, style);
            }

            AppendInlineText(lines[index], style);
        }
    }

    private void AppendInlineText(string text, InlineStyle style)
    {
        var index = 0;
        while (index < text.Length)
        {
            if (text[index] == '\\' && index + 1 < text.Length)
            {
                AppendRun(text.Substring(index + 1, 1), style);
                index += 2;
                continue;
            }

            if (TryReadDelimited(text, index, "`", "`", out var inlineCode, out var consumed))
            {
                AppendRun(inlineCode, style with { Code = true });
                index += consumed;
                continue;
            }

            if (TryReadLink(text, index, out var linkText, out var url, out consumed, out var isImage))
            {
                var label = string.IsNullOrWhiteSpace(linkText) ? url : linkText;
                AppendInlineText(label, style with { Italic = isImage || style.Italic, Underline = !isImage || style.Underline });
                if (!string.IsNullOrWhiteSpace(url) && !string.Equals(label, url, StringComparison.OrdinalIgnoreCase))
                {
                    AppendRun($" ({url})", style with { Code = true });
                }

                index += consumed;
                continue;
            }

            if (TryReadAutolink(text, index, out var autoLink, out consumed))
            {
                AppendRun(autoLink, style with { Underline = true });
                index += consumed;
                continue;
            }

            if (TryReadDelimited(text, index, "***", "***", out var boldItalic, out consumed)
                || TryReadDelimited(text, index, "___", "___", out boldItalic, out consumed))
            {
                AppendInlineText(boldItalic, style with { Bold = true, Italic = true });
                index += consumed;
                continue;
            }

            if (TryReadDelimited(text, index, "**", "**", out var bold, out consumed)
                || TryReadDelimited(text, index, "__", "__", out bold, out consumed))
            {
                AppendInlineText(bold, style with { Bold = true });
                index += consumed;
                continue;
            }

            if (TryReadDelimited(text, index, "~~", "~~", out var strike, out consumed))
            {
                AppendInlineText(strike, style with { Strike = true });
                index += consumed;
                continue;
            }

            if (TryReadDelimited(text, index, "==", "==", out var highlight, out consumed))
            {
                AppendInlineText(highlight, style with { Bold = true });
                index += consumed;
                continue;
            }

            if (TryReadDelimited(text, index, "++", "++", out var underline, out consumed))
            {
                AppendInlineText(underline, style with { Underline = true });
                index += consumed;
                continue;
            }

            if (CanStartSingleEmphasis(text, index, '*')
                && TryReadDelimited(text, index, "*", "*", out var italic, out consumed))
            {
                AppendInlineText(italic, style with { Italic = true });
                index += consumed;
                continue;
            }

            if (CanStartSingleEmphasis(text, index, '_')
                && TryReadDelimited(text, index, "_", "_", out italic, out consumed))
            {
                AppendInlineText(italic, style with { Italic = true });
                index += consumed;
                continue;
            }

            var next = text.IndexOfAny(InlineStopCharacters, index + 1);
            if (next < 0)
            {
                next = text.Length;
            }

            AppendRun(text[index..next], style);
            index = next;
        }
    }

    private void AppendRun(string text, InlineStyle style)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var run = new Run(text)
        {
            FontWeight = style.Bold ? FontWeight.Bold : FontWeight.Normal,
            FontStyle = style.Italic ? FontStyle.Italic : FontStyle.Normal,
            FontSize = FontSize * style.FontScale
        };

        if (style.Code)
        {
            run.FontFamily = ResolveCodeFontFamily();
        }

        if (style.Strike)
        {
            run.TextDecorations = Avalonia.Media.TextDecorations.Strikethrough;
        }
        else if (style.Underline)
        {
            run.TextDecorations = Avalonia.Media.TextDecorations.Underline;
        }

        Inlines?.Add(run);
    }

    private void AddBreak()
    {
        Inlines?.Add(new LineBreak());
    }

    private FontFamily ResolveCodeFontFamily()
    {
        try
        {
            return new FontFamily(string.IsNullOrWhiteSpace(CodeFontFamilyKey) ? "Consolas" : CodeFontFamilyKey);
        }
        catch
        {
            return new FontFamily("Consolas");
        }
    }

    private static bool TryReadDelimited(
        string text,
        int start,
        string opener,
        string closer,
        out string content,
        out int consumed)
    {
        content = "";
        consumed = 0;
        if (!text.AsSpan(start).StartsWith(opener, StringComparison.Ordinal))
        {
            return false;
        }

        var contentStart = start + opener.Length;
        if (contentStart >= text.Length)
        {
            return false;
        }

        var end = text.IndexOf(closer, contentStart, StringComparison.Ordinal);
        if (end <= contentStart)
        {
            return false;
        }

        content = text[contentStart..end];
        consumed = end + closer.Length - start;
        return !string.IsNullOrWhiteSpace(content);
    }

    private static bool TryReadLink(
        string text,
        int start,
        out string label,
        out string url,
        out int consumed,
        out bool isImage)
    {
        label = "";
        url = "";
        consumed = 0;
        isImage = false;

        var linkStart = start;
        if (text.AsSpan(start).StartsWith("![", StringComparison.Ordinal))
        {
            isImage = true;
            linkStart++;
        }
        else if (text[start] != '[')
        {
            return false;
        }

        var closeLabel = text.IndexOf(']', linkStart + 1);
        if (closeLabel < 0 || closeLabel + 1 >= text.Length || text[closeLabel + 1] != '(')
        {
            return false;
        }

        var closeUrl = text.IndexOf(')', closeLabel + 2);
        if (closeUrl < 0)
        {
            return false;
        }

        label = text[(linkStart + 1)..closeLabel].Trim();
        url = text[(closeLabel + 2)..closeUrl].Trim();
        consumed = closeUrl + 1 - start;
        return !string.IsNullOrWhiteSpace(label) || !string.IsNullOrWhiteSpace(url);
    }

    private static bool TryReadAutolink(string text, int start, out string url, out int consumed)
    {
        url = "";
        consumed = 0;
        if (text[start] != '<')
        {
            return false;
        }

        var end = text.IndexOf('>', start + 1);
        if (end < 0)
        {
            return false;
        }

        var candidate = text[(start + 1)..end].Trim();
        if (!candidate.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            && !candidate.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            && !candidate.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        url = candidate;
        consumed = end + 1 - start;
        return true;
    }

    private static bool CanStartSingleEmphasis(string text, int index, char marker)
    {
        if (text[index] != marker)
        {
            return false;
        }

        if (index + 1 >= text.Length || char.IsWhiteSpace(text[index + 1]))
        {
            return false;
        }

        if (index > 0 && char.IsLetterOrDigit(text[index - 1]) && marker == '_')
        {
            return false;
        }

        return index == 0 || !char.IsLetterOrDigit(text[index - 1]);
    }

    private static bool TryReadTable(string[] lines, ref int index, out string table)
    {
        table = "";
        if (index + 1 >= lines.Length || !LooksLikeTableRow(lines[index]) || !TableSeparatorRegex().IsMatch(lines[index + 1]))
        {
            return false;
        }

        var rows = new List<string[]>
        {
            SplitTableRow(lines[index])
        };
        index += 2;

        for (; index < lines.Length; index++)
        {
            if (!LooksLikeTableRow(lines[index]))
            {
                index--;
                break;
            }

            rows.Add(SplitTableRow(lines[index]));
        }

        table = FormatTable(rows);
        return !string.IsNullOrWhiteSpace(table);
    }

    private static bool TryReadSetextHeading(string[] lines, int index, out string heading, out int level)
    {
        heading = "";
        level = 2;
        if (index + 1 >= lines.Length || string.IsNullOrWhiteSpace(lines[index]))
        {
            return false;
        }

        var match = SetextHeadingRegex().Match(lines[index + 1]);
        if (!match.Success)
        {
            return false;
        }

        heading = lines[index].Trim();
        level = lines[index + 1].TrimStart().StartsWith("=", StringComparison.Ordinal) ? 1 : 2;
        return !string.IsNullOrWhiteSpace(heading);
    }

    private static string FormatListLines(IEnumerable<string> lines)
    {
        var builder = new StringBuilder();
        foreach (var raw in lines)
        {
            var match = ListLineRegex().Match(raw);
            if (!match.Success)
            {
                continue;
            }

            var indent = Math.Clamp(match.Groups["indent"].Value.Length / 2, 0, 6);
            var marker = match.Groups["marker"].Value;
            var text = match.Groups["text"].Value.Trim();
            var check = TaskPrefixRegex().Match(text);
            var prefix = marker.EndsWith(".", StringComparison.Ordinal) || marker.EndsWith(")", StringComparison.Ordinal)
                ? marker
                : "-";

            if (check.Success)
            {
                prefix = check.Groups["done"].Value.Equals("x", StringComparison.OrdinalIgnoreCase) ? "done -" : "todo -";
                text = text[check.Length..].TrimStart();
            }

            if (builder.Length > 0)
            {
                builder.AppendLine();
            }

            builder.Append(new string(' ', indent * 2));
            builder.Append(prefix);
            builder.Append(' ');
            builder.Append(text);
        }

        return builder.ToString();
    }

    private static string FormatTable(IReadOnlyList<string[]> rows)
    {
        if (rows.Count == 0)
        {
            return "";
        }

        var columnCount = rows.Max(row => row.Length);
        if (columnCount == 0)
        {
            return "";
        }

        var widths = new int[columnCount];
        foreach (var row in rows)
        {
            for (var column = 0; column < row.Length; column++)
            {
                widths[column] = Math.Max(widths[column], CleanInlineMarkers(row[column]).Length);
            }
        }

        var builder = new StringBuilder();
        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            if (rowIndex > 0)
            {
                builder.AppendLine();
            }

            var row = rows[rowIndex];
            for (var column = 0; column < columnCount; column++)
            {
                if (column > 0)
                {
                    builder.Append("  ");
                }

                var cell = column < row.Length ? CleanInlineMarkers(row[column]) : "";
                builder.Append(cell.PadRight(widths[column]));
            }
        }

        return builder.ToString();
    }

    private static bool LooksLikeTableRow(string line)
    {
        return !string.IsNullOrWhiteSpace(line)
            && line.Contains('|', StringComparison.Ordinal)
            && !TableSeparatorRegex().IsMatch(line);
    }

    private static string[] SplitTableRow(string line)
    {
        var clean = line.Trim();
        if (clean.StartsWith('|'))
        {
            clean = clean[1..];
        }

        if (clean.EndsWith('|'))
        {
            clean = clean[..^1];
        }

        return clean.Split('|').Select(cell => cell.Trim()).ToArray();
    }

    private static string CleanInlineMarkers(string text)
    {
        return InlineCleanupRegex().Replace(text ?? "", "$1").Trim();
    }

    private static string Normalize(string text)
    {
        return (text ?? "")
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Replace("<br />", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("<br/>", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("<br>", "\n", StringComparison.OrdinalIgnoreCase)
            .Trim();
    }

    private static string TrimClosingHeadingMarks(string text)
    {
        return Regex.Replace(text.Trim(), @"\s+#+\s*$", "");
    }

    private enum MarkdownBlockKind
    {
        Paragraph,
        Heading,
        List,
        Quote,
        Table,
        Rule,
        Blank
    }

    private sealed record MarkdownBlock(MarkdownBlockKind Kind, string Text, int Level = 0);

    private readonly record struct InlineStyle(
        bool Bold,
        bool Italic,
        bool Code,
        bool Strike,
        bool Underline,
        double FontScale)
    {
        public static InlineStyle Default { get; } = new(false, false, false, false, false, 1.0);
    }

    [GeneratedRegex(@"^(?<level>#{1,6})[ \t]+(?<text>.+?)\s*$")]
    private static partial Regex HeadingRegex();

    [GeneratedRegex(@"^[ \t]*(?:[-*_][ \t]*){3,}$")]
    private static partial Regex ThematicBreakRegex();

    [GeneratedRegex(@"^[ \t]*(?:=+|-+)[ \t]*$")]
    private static partial Regex SetextHeadingRegex();

    [GeneratedRegex(@"^[ \t]*>[ \t]?(?<text>.*)$")]
    private static partial Regex QuoteRegex();

    [GeneratedRegex(@"^(?<indent>[ \t]*)(?<marker>(?:[-+*])|(?:\d+[.)]))[ \t]+(?<text>.+)$")]
    private static partial Regex ListLineRegex();

    [GeneratedRegex(@"^\[(?<done>[ xX])\][ \t]+")]
    private static partial Regex TaskPrefixRegex();

    [GeneratedRegex(@"^[ \t]*\|?[ \t]*:?-{3,}:?[ \t]*(?:\|[ \t]*:?-{3,}:?[ \t]*)+\|?[ \t]*$")]
    private static partial Regex TableSeparatorRegex();

    [GeneratedRegex(@"(?:\*\*\*|___|\*\*|__|\*|_|~~|==|\+\+)(.*?)(?:\*\*\*|___|\*\*|__|\*|_|~~|==|\+\+)")]
    private static partial Regex InlineCleanupRegex();
}
