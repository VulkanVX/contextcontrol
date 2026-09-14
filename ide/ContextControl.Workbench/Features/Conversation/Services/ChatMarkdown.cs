using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Extensions.TaskLists;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace ContextControl.Workbench.Services;

public sealed record ChatMarkdownRun(string Text, bool Bold = false, bool Italic = false, bool Code = false, bool Strike = false, string? Url = null);
public sealed record ChatMarkdownBlock(string Kind, IReadOnlyList<ChatMarkdownRun> Runs, int Level = 0,
    string Marker = "", IReadOnlyList<ChatMarkdownBlock>? Children = null)
{
    public string PlainText => string.Concat(Runs.Select(run => run.Text));
}

/// <summary>Semantic Markdown data shared by the virtualized transcript. No HTML execution or image loading.</summary>
public static class ChatMarkdown
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UsePipeTables().UseEmphasisExtras().UseAutoLinks().UseTaskLists().Build();

    public static IReadOnlyList<ChatMarkdownBlock> Parse(string text)
    {
        var document = Markdown.Parse(text, Pipeline);
        return PromoteNamedSections(Blocks(document, 0));
    }

    private static IReadOnlyList<ChatMarkdownBlock> PromoteNamedSections(IReadOnlyList<ChatMarkdownBlock> blocks)
    {
        static bool IsTitle(ChatMarkdownBlock block) => block.Kind == "heading"
            || block.Kind == "paragraph" && block.Runs.Count > 0 && block.Runs.All(run => run.Bold || string.IsNullOrWhiteSpace(run.Text));
        static bool IsEntryTitle(ChatMarkdownBlock block) => IsTitle(block) && block.PlainText.Length is >= 4 and <= 120
            && !System.Text.RegularExpressions.Regex.IsMatch(block.PlainText.Trim(), @"^(?:sources?|references?|additional notes|notes?|summary|conclusion|recommendations?|places and reviews)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        // Small models often emit bold names or headings instead of numbered entries.
        // Promote repeated titled sections so they can use the same per-entry layout.
        if (blocks.Count(IsEntryTitle) < 2) return blocks;
        var result = new List<ChatMarkdownBlock>();
        for (var i = 0; i < blocks.Count; i++)
        {
            var block = blocks[i];
            if (!IsEntryTitle(block) || i + 1 >= blocks.Count || blocks[i + 1].Kind is not ("paragraph" or "listItem")) { result.Add(block); continue; }
            var children = new List<ChatMarkdownBlock>();
            while (i + 1 < blocks.Count && !IsTitle(blocks[i + 1]) && blocks[i + 1].Kind is "paragraph" or "listItem") children.Add(blocks[++i]);
            result.Add(new ChatMarkdownBlock("card", block.Runs, Children: children));
        }
        return result;
    }

    private static IReadOnlyList<ChatMarkdownBlock> Blocks(ContainerBlock container, int depth)
    {
        var result = new List<ChatMarkdownBlock>();
        foreach (var block in container)
        {
            switch (block)
            {
                case Table table:
                    result.Add(new("table", [], Children: table.OfType<TableRow>().Select(row =>
                        new ChatMarkdownBlock(row.IsHeader ? "tableHeader" : "tableRow", [], Children: row.OfType<TableCell>()
                            .Select(cell => new ChatMarkdownBlock("cell", Join(Blocks(cell, depth)))).ToArray())).ToArray()));
                    break;
                case ListBlock list:
                    var number = int.TryParse(list.OrderedStart, out var start) ? start : 1;
                    foreach (var item in list.OfType<ListItemBlock>())
                    {
                        var children = Blocks(item, depth + 1).ToList();
                        var marker = list.IsOrdered ? $"{number++}." : "•";
                        var first = item.FirstOrDefault() as ParagraphBlock;
                        // A titled item with supporting details is an information card, irrespective of subject.
                        // Names, ratings, addresses and review text stay exactly as supplied by the answer.
                        if (depth == 0 && first?.Inline?.FirstChild is EmphasisInline { DelimiterCount: 2, DelimiterChar: '*' or '_' } title
                            && children.Count > 0 && IsTitledEntry(title, children))
                        {
                            var titleRuns = Inlines(title, new("", Bold: true));
                            var rest = children[0].Runs.ToList();
                            var remove = string.Concat(titleRuns.Select(run => run.Text)).Length;
                            while (rest.Count > 0 && remove >= rest[0].Text.Length) { remove -= rest[0].Text.Length; rest.RemoveAt(0); }
                            if (rest.Count > 0 && remove > 0) rest[0] = rest[0] with { Text = rest[0].Text[remove..] };
                            while (rest.Count > 0 && string.IsNullOrWhiteSpace(rest[0].Text)) rest.RemoveAt(0);
                            if (rest.Count > 0) rest[0] = rest[0] with { Text = rest[0].Text.TrimStart(' ', '\n', ':', '—', '-') };
                            children[0] = children[0] with { Runs = rest };
                            result.Add(new("card", titleRuns, Marker: marker, Children: children));
                        }
                        else
                            result.Add(new("listItem", [], Level: depth, Marker: marker, Children: children));
                    }
                    break;
                case HeadingBlock heading:
                    result.Add(new("heading", Inlines(heading.Inline, new("", Bold: true)), heading.Level));
                    break;
                case ParagraphBlock paragraph:
                    result.Add(new("paragraph", Inlines(paragraph.Inline, new(""))));
                    break;
                case QuoteBlock quote:
                    result.Add(new("quote", [], Children: Blocks(quote, depth)));
                    break;
                case ThematicBreakBlock:
                    result.Add(new("rule", []));
                    break;
                case CodeBlock code:
                    result.Add(new("code", [new(code.Lines.ToString(), Code: true)]));
                    break;
                case HtmlBlock html:
                    // Some small models mix HTML detail lists into Markdown entries.
                    // Convert their basic typography to semantic text, without a web
                    // renderer, external image loads, attributes or executable content.
                    result.AddRange(Blocks(Markdown.Parse(HtmlDetailsAsMarkdown(html.Lines.ToString()), Pipeline), depth));
                    break;
                case ContainerBlock nested:
                    result.AddRange(Blocks(nested, depth + 1));
                    break;
                case LeafBlock leaf when leaf.Inline is not null:
                    result.Add(new("paragraph", Inlines(leaf.Inline, new(""))));
                    break;
            }
        }
        return result;
    }

    private static string HtmlDetailsAsMarkdown(string html)
    {
        static string Replace(string value, string pattern, string replacement) => System.Text.RegularExpressions.Regex.Replace(
            value, pattern, replacement, System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline, TimeSpan.FromSeconds(1));
        var text = Replace(html, @"<(script|style)\b[^>]*>.*?</\1\s*>", "");
        text = Replace(text, @"<!--.*?-->", "");
        text = Replace(text, @"</?(?:strong|b)\b[^>]*>", "**");
        text = Replace(text, @"</?(?:em|i)\b[^>]*>", "*");
        text = Replace(text, @"<br\s*/?>", "\n");
        text = Replace(text, @"<li\b[^>]*>", "\n- ");
        text = Replace(text, @"</(?:li|p|div|section|ul|ol)\s*>", "\n");
        text = Replace(text, @"<(?:p|div|section|ul|ol)\b[^>]*>", "\n");
        text = Replace(text, @"</?[a-z][^>]*>", "");
        // Escape any incomplete tag or declaration that remains, so reparsing can
        // never recurse into another HTML block (including a bare DOCTYPE).
        return text.Replace("<", "&lt;", StringComparison.Ordinal).Replace(">", "&gt;", StringComparison.Ordinal).Trim();
    }

    private static bool IsTitledEntry(EmphasisInline title, IReadOnlyList<ChatMarkdownBlock> children)
    {
        var name = string.Concat(Inlines(title, new("")).Select(run => run.Text));
        if (name.Length is < 3 or > 160 || name.Contains('\n')) return false;
        return title.NextSibling is null or LineBreakInline
            || children.Count > 1
            || title.NextSibling is LiteralInline literal && literal.Content.ToString().TrimStart().StartsWith(':');
    }

    private static IReadOnlyList<ChatMarkdownRun> Join(IReadOnlyList<ChatMarkdownBlock> blocks)
    {
        var runs = new List<ChatMarkdownRun>();
        foreach (var block in blocks)
        {
            if (runs.Count > 0) runs.Add(new("\n"));
            runs.AddRange(block.Runs);
            if (block.Children is { } children) runs.AddRange(Join(children));
        }
        return runs;
    }

    private static IReadOnlyList<ChatMarkdownRun> Inlines(ContainerInline? container, ChatMarkdownRun style)
    {
        var result = new List<ChatMarkdownRun>();
        if (container is null) return result;
        foreach (var inline in container)
        {
            switch (inline)
            {
                case LiteralInline literal:
                    result.Add(style with { Text = literal.Content.ToString() });
                    break;
                case HtmlEntityInline entity:
                    result.Add(style with { Text = entity.Transcoded.ToString() });
                    break;
                case CodeInline code:
                    result.Add(style with { Text = code.Content, Code = true });
                    break;
                case LineBreakInline:
                    result.Add(style with { Text = "\n" });
                    break;
                case EmphasisInline emphasis:
                    result.AddRange(Inlines(emphasis, style with
                    {
                        Bold = style.Bold || emphasis.DelimiterChar is '*' or '_' && emphasis.DelimiterCount >= 2,
                        Italic = style.Italic || emphasis.DelimiterChar is '*' or '_' && emphasis.DelimiterCount % 2 == 1,
                        Strike = style.Strike || emphasis.DelimiterChar == '~'
                    }));
                    break;
                case LinkInline link:
                    var target = GoogleSearchContext.IsPublicWebUrl(link.Url) ? link.Url : null;
                    // Markdown image labels remain readable. Only research-supplied images are fetched.
                    result.AddRange(Inlines(link, style with { Url = target }));
                    break;
                case AutolinkInline link:
                    result.Add(style with { Text = link.Url, Url = GoogleSearchContext.IsPublicWebUrl(link.Url) ? link.Url : null });
                    break;
                case TaskList task:
                    result.Add(style with { Text = task.Checked ? "☑ " : "☐ " });
                    break;
                case HtmlInline html:
                    result.Add(style with { Text = html.Tag.Equals("<br>", StringComparison.OrdinalIgnoreCase)
                        || html.Tag.Equals("<br/>", StringComparison.OrdinalIgnoreCase) || html.Tag.Equals("<br />", StringComparison.OrdinalIgnoreCase) ? "\n" : html.Tag });
                    break;
                case ContainerInline nested:
                    result.AddRange(Inlines(nested, style));
                    break;
            }
        }
        var merged = new List<ChatMarkdownRun>();
        foreach (var run in result)
        {
            if (merged.Count > 0 && (merged[^1] with { Text = "" }) == (run with { Text = "" }))
                merged[^1] = merged[^1] with { Text = merged[^1].Text + run.Text };
            else if (run.Text.Length > 0) merged.Add(run);
        }
        return merged;
    }
}
