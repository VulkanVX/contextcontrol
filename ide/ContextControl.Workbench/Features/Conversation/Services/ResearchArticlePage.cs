using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using ContextControl.Workbench.ViewModels;
using SkiaSharp;

namespace ContextControl.Workbench.Services;

/// <summary>A local, script-free reading view of the answer and its retrieved source photos.</summary>
public static class ResearchArticlePage
{
    public static bool CanOpen(LocalLlmChatMessageViewModel? message) => message is { IsUser: false }
        && !string.IsNullOrWhiteSpace(message.VisibleText) && message.AttachedFiles.Any(a => a.Kind == "web");

    public static (string Title, string Html) Build(LocalLlmChatMessageViewModel message)
    {
        var blocks = ChatMarkdown.Parse(message.VisibleText[..Math.Min(message.VisibleText.Length, 120_000)]);
        var attachments = message.AttachedFiles.Where(a => string.IsNullOrWhiteSpace(a.PreviewPath) || GoogleEntryPhotoService.IsRelevantSavedPhoto(a, message.AttachedFiles)).ToArray();
        var title = attachments.FirstOrDefault(a => a.IsSubjectPhoto)?.EntryTitle
            ?? blocks.FirstOrDefault(b => b.Kind == "heading")?.PlainText
            ?? blocks.FirstOrDefault()?.PlainText ?? "Research notes";
        if (string.IsNullOrWhiteSpace(title)) title = "Research notes";
        if (title.Length > 90) title = title[..87].TrimEnd() + "…";
        var body = new StringBuilder();
        var toc = new StringBuilder();
        var imageBudget = 1_000_000;
        var used = new HashSet<string>(StringComparer.Ordinal);
        var section = 0;
        string Inline(IReadOnlyList<ChatMarkdownRun> runs) => string.Concat(runs.Select(run =>
        {
            var text = H(run.Text);
            if (run.Code) text = "<code>" + text + "</code>";
            if (run.Bold) text = "<strong>" + text + "</strong>";
            if (run.Italic) text = "<em>" + text + "</em>";
            if (run.Strike) text = "<del>" + text + "</del>";
            if (GoogleSearchContext.IsPublicWebUrl(run.Url)) return "<a href=\"" + H(run.Url!) + "\">" + text + "</a>";
            if (!run.Code && run.Url is null) text = Regex.Replace(text, @"\[(\d{1,2})\]", m =>
                attachments.Any(a => a.Label.StartsWith(m.Value + " ", StringComparison.Ordinal))
                    ? "<a class=\"citation\" href=\"#ref-" + m.Groups[1].Value + "\">" + m.Value + "</a>" : m.Value);
            return text;
        }));
        string Figure(ContextControlAttachmentViewModel photo)
        {
            if (!used.Add(photo.PreviewPath) || imageBudget < 10_000) return "";
            var data = ImageData(photo.PreviewPath, imageBudget);
            if (data is null) return "";
            imageBudget -= data.Length;
            var caption = string.IsNullOrWhiteSpace(photo.PhotoCaption) ? photo.EntryTitle : photo.PhotoCaption;
            var credit = GoogleSearchContext.IsPublicWebUrl(photo.Path)
                ? "<a href=\"" + H(photo.Path) + "\">Source ↗</a>" : "Source photo";
            return "<figure><img src=\"" + data + "\" alt=\"" + H(caption) + "\"><figcaption>" + H(caption) + " <span>" + credit + "</span></figcaption></figure>";
        }
        void Render(IEnumerable<ChatMarkdownBlock> content)
        {
            foreach (var block in content)
            {
                switch (block.Kind)
                {
                    case "heading":
                    case "card":
                        var id = "section-" + ++section;
                        toc.Append("<a href=\"#").Append(id).Append("\">").Append(H(block.PlainText)).Append("</a>");
                        body.Append("<section id=\"").Append(id).Append("\"><h2>").Append(Inline(block.Runs)).Append("</h2>");
                        var photos = attachments.Where(a => !a.IsSubjectPhoto && !string.IsNullOrWhiteSpace(a.EntryTitle)
                            && GoogleEntryPhotoService.NormalizeName(a.EntryTitle) == GoogleEntryPhotoService.NormalizeName(block.PlainText)).Take(2).ToArray();
                        if (photos.Length > 0) body.Append("<aside class=\"entry-photo\">").Append(string.Concat(photos.Select(Figure))).Append("</aside>");
                        Render(block.Children ?? []); body.Append("</section>"); break;
                    case "table":
                        body.Append("<div class=\"table-wrap\"><table>"); Render(block.Children ?? []); body.Append("</table></div>"); break;
                    case "tableHeader":
                    case "tableRow":
                        var tag = block.Kind == "tableHeader" ? "th" : "td";
                        body.Append("<tr>");
                        foreach (var cell in block.Children ?? []) body.Append('<').Append(tag).Append('>').Append(Inline(cell.Runs)).Append("</").Append(tag).Append('>');
                        body.Append("</tr>"); break;
                    case "listItem":
                        body.Append("<div class=\"list-item\"><span>").Append(H(block.Marker)).Append("</span><div>"); Render(block.Children ?? []); body.Append("</div></div>"); break;
                    case "quote": body.Append("<blockquote>"); Render(block.Children ?? []); body.Append("</blockquote>"); break;
                    case "rule": body.Append("<hr>"); break;
                    case "code": body.Append("<pre>").Append(Inline(block.Runs)).Append("</pre>"); break;
                    default: body.Append("<p>").Append(Inline(block.Runs)).Append("</p>"); break;
                }
            }
        }
        // The collage is built only from host-retrieved subject photos. Entry photos stay with their named section.
        var collage = string.Concat(attachments.Where(a => a.IsSubjectPhoto).Take(4).Select(Figure));
        if (blocks.FirstOrDefault() is { Kind: "heading" or "card" } first && first.PlainText == title)
        { Render(first.Children ?? []); Render(blocks.Skip(1)); }
        else Render(blocks);
        var sources = new StringBuilder();
        foreach (var source in attachments.Where(a => a.Kind == "web" && GoogleSearchContext.IsPublicWebUrl(a.Path)).DistinctBy(a => a.Path).Take(24))
        {
            var number = Regex.Match(source.Label, @"^\[(\d{1,2})\]").Groups[1].Value;
            sources.Append("<li id=\"ref-").Append(number.Length > 0 ? number : "photo-" + sources.Length).Append("\"><a href=\"")
                .Append(H(source.Path)).Append("\">").Append(H(source.Label)).Append("</a><small>").Append(H(new Uri(source.Path).Host)).Append("</small></li>");
        }
        var html = "<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">"
            + "<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; img-src data:; style-src 'unsafe-inline'; base-uri 'none'; form-action 'none'\">"
            + "<title>" + H(title) + "</title><style>" + Css + "</style></head><body><header><b>ContextControl</b><span>Research library</span></header>"
            + "<div class=\"page\"><nav aria-label=\"Contents\"><small>ON THIS PAGE</small><a href=\"#overview\">Overview</a>" + toc + "<a href=\"#references\">References</a></nav><main id=\"overview\">"
            + "<div class=\"eyebrow\">RESEARCH NOTES · " + H(message.ModelId) + "</div><h1>" + H(title) + "</h1>"
            + "<p class=\"byline\">AI-generated synthesis · check the linked sources for accuracy and current details.</p>"
            + (collage.Length > 0 ? "<div class=\"collage\">" + collage + "</div>" : "") + body
            + "<section id=\"references\"><h2>References &amp; photo credits</h2><ol class=\"references\">" + sources + "</ol></section></main></div></body></html>";
        return (title, html);
    }

    private static string H(string text) => WebUtility.HtmlEncode(text);
    private static string? ImageData(string path, int budget)
    {
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length > GooglePhotoPreviewService.MaxDownloadBytes) return null;
            using var codec = SKCodec.Create(path);
            if (codec is null || codec.Info.Width > 1280 || codec.Info.Height > 1280) return null;
            using var bitmap = SKBitmap.Decode(codec);
            if (bitmap is null) return null;
            var scale = Math.Min(1d, 720d / Math.Max(bitmap.Width, bitmap.Height));
            using var reduced = bitmap.Resize(new SKImageInfo(Math.Max(1, (int)(bitmap.Width * scale)), Math.Max(1, (int)(bitmap.Height * scale))), SKFilterQuality.Medium);
            if (reduced is null) return null;
            using var image = SKImage.FromBitmap(reduced);
            using var encoded = image.Encode(SKEncodedImageFormat.Jpeg, 75);
            var data = "data:image/jpeg;base64," + Convert.ToBase64String(encoded.ToArray());
            // The same URI appears only once in the document, keeping WebView2 below its 2 MB limit.
            return data.Length < budget ? data : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return null; }
    }

    private const string Css = """
        :root{color-scheme:light;--ink:#202c34;--muted:#69747a;--accent:#256b70}*{box-sizing:border-box}html{scroll-behavior:smooth;scroll-padding-top:30px}body{margin:0;background:#f7f7f3;color:var(--ink);font:17px/1.75 'Segoe UI',sans-serif}header{background:#192b30;color:#edf6f5;padding:18px 5%;display:flex;justify-content:space-between;font-size:14px}header span{color:#a7c1c1}.page{max-width:1320px;margin:auto;display:grid;grid-template-columns:205px minmax(0,1fr);gap:58px;padding:52px 36px}nav{position:sticky;top:25px;align-self:start;font-size:13px;max-height:90vh;overflow:auto}nav small,.eyebrow{letter-spacing:1.6px;font-size:11px;font-weight:650;color:var(--muted)}nav a{display:block;padding:7px 0;text-decoration:none;line-height:1.5}nav small{display:block;margin-bottom:12px}main{min-width:0;max-width:900px}h1{font:600 clamp(30px,4vw,46px)/1.14 Georgia,serif;letter-spacing:-1px;margin:13px 0 20px}h2{font:600 28px/1.3 Georgia,serif;margin:36px 0 17px;padding-top:10px;border-top:1px solid #d9dfda}p{margin:0 0 18px}a{color:var(--accent);text-underline-offset:3px}.byline{font-size:12px;color:var(--muted);margin-bottom:28px}.collage{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:12px;margin:20px 0 32px}.collage figure:only-child{grid-column:1/-1}.collage img{width:100%;height:220px;object-fit:contain;background:#e9ede8;border-radius:12px}.collage figure:only-child img{height:360px}figure{margin:0}figcaption{font-size:12px;line-height:1.55;padding:8px 2px;color:var(--muted)}figcaption span{display:block;font-size:11px}.entry-photo{float:right;width:42%;margin:0 0 20px 24px}.entry-photo img{width:100%;max-height:250px;object-fit:contain;border-radius:12px}section{display:flow-root}.list-item{display:grid;grid-template-columns:26px 1fr;gap:4px}.list-item>span{color:var(--accent)}.table-wrap{overflow:auto}table{border-collapse:collapse;width:100%;font-size:14px;margin:18px 0}td,th{text-align:left;padding:13px 15px;border-bottom:1px solid #d9dfda}th{background:#e9eee8}blockquote{border-left:3px solid #6e9490;margin:22px 0;padding:6px 22px;color:#53645f}pre{white-space:pre-wrap;background:#e9ede8;padding:20px;border-radius:12px;overflow-wrap:anywhere}code{font-size:.9em}hr{border:0;border-top:1px solid #d9dfda;margin:30px 0}.references{font-size:14px;padding-left:22px}.references li{margin:12px 0}.references small{display:block;color:var(--muted);font-size:11px}.citation{font-size:.85em}img{max-width:100%}p,li,td{overflow-wrap:anywhere}@media(max-width:820px){.page{display:block;padding:28px 22px}nav{position:static;max-height:none;border-bottom:1px solid #d9dfda;margin-bottom:30px;padding-bottom:18px;columns:2}nav small{column-span:all}.collage img{height:160px}.entry-photo{float:none;width:100%;margin:10px 0}}@media(max-width:450px){.collage{grid-template-columns:1fr}.collage img{height:200px}h2{font-size:24px}}
        """;
}
