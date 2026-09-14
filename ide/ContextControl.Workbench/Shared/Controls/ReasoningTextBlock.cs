using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Threading;
using ContextControl.Workbench.Services;

namespace ContextControl.Workbench.Controls;

/// <summary>Selectable CommonMark reasoning. Stream updates coalesce before layout; no browser or image loading.</summary>
public sealed class ReasoningTextBlock : SelectableTextBlock
{
    public static readonly StyledProperty<string> MarkdownProperty = AvaloniaProperty.Register<ReasoningTextBlock, string>(nameof(Markdown), "");
    public static readonly StyledProperty<FontFamily> CodeFontFamilyProperty = AvaloniaProperty.Register<ReasoningTextBlock, FontFamily>(nameof(CodeFontFamily), new FontFamily("Consolas"));
    public string Markdown { get => GetValue(MarkdownProperty); set => SetValue(MarkdownProperty, value); }
    public FontFamily CodeFontFamily { get => GetValue(CodeFontFamilyProperty); set => SetValue(CodeFontFamilyProperty, value); }
    public string RenderedPlainText { get; private set; } = "";
    private bool _queued;
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != MarkdownProperty && change.Property != FontSizeProperty && change.Property != CodeFontFamilyProperty) return;
        if (_queued) return;
        _queued = true;
        Dispatcher.UIThread.Post(() => { _queued = false; Rebuild(); }, DispatcherPriority.Background);
    }
    private void Rebuild()
    {
        var inlines = new InlineCollection();
        var plain = new StringBuilder();
        void Break() { if (plain.Length == 0 || plain[^1] == '\n') return; inlines.Add(new LineBreak()); plain.Append('\n'); }
        void Add(ChatMarkdownRun text, double scale = 1)
        {
            if (text.Text.Length == 0) return;
            var run = new Run(text.Text) { FontSize = FontSize * scale, FontWeight = text.Bold ? FontWeight.Bold : FontWeight.Normal,
                FontStyle = text.Italic ? FontStyle.Italic : FontStyle.Normal };
            if (text.Code) run.FontFamily = CodeFontFamily;
            if (text.Strike) run.TextDecorations = Avalonia.Media.TextDecorations.Strikethrough;
            else if (text.Url is not null) run.TextDecorations = Avalonia.Media.TextDecorations.Underline;
            inlines.Add(run); plain.Append(text.Text);
        }
        void Blocks(IReadOnlyList<ChatMarkdownBlock> blocks, int depth = 0)
        {
            foreach (var block in blocks)
            {
                Break();
                if (block.Kind == "table")
                {
                    foreach (var row in block.Children ?? [])
                    {
                        Break(); var first = true;
                        foreach (var cell in row.Children ?? [])
                        {
                            if (!first) Add(new("  |  ")); first = false;
                            foreach (var run in cell.Runs) Add(run with { Bold = row.Kind == "tableHeader" || run.Bold });
                        }
                    }
                }
                else if (block.Kind == "listItem")
                {
                    Add(new(new string(' ', depth * 2) + block.Marker + " "));
                    var first = true;
                    foreach (var child in block.Children ?? [])
                    {
                        if (!first || child.Kind != "paragraph") Blocks([child], depth + 1);
                        else foreach (var run in child.Runs) Add(run);
                        first = false;
                    }
                }
                else
                {
                    if (block.Kind == "quote") Add(new("│ "));
                    if (!string.IsNullOrEmpty(block.Marker)) Add(new(block.Marker + " "));
                    foreach (var run in block.Runs) Add(run, block.Kind is "heading" or "card" ? 1.12 : 1);
                    if (block.Children is { } children) Blocks(children, depth);
                }
                Break();
            }
        }
        Blocks(ChatMarkdown.Parse(Markdown ?? ""));
        RenderedPlainText = plain.ToString().TrimEnd('\n');
        Text = "";
        Inlines = inlines;
    }
}
