using Avalonia;
using ContextControl.Workbench.Services;
using ContextControl.Workbench.ViewModels;

namespace ContextControl.Workbench.Controls;

public sealed partial class ChatTranscriptRenderControl
{
    private double BuildResearchArticle(MessageLayout layout, IReadOnlyList<ChatMarkdownBlock> blocks,
        IReadOnlyList<ContextControlAttachmentViewModel> photos, double x, double y, double width)
    {
        var intro = blocks.ToList().FindIndex(block => block.Kind is "paragraph" or "card");
        if (intro < 0) intro = 0;
        var placements = new Dictionary<int, List<ContextControlAttachmentViewModel>>();
        foreach (var (photo, index) in photos.Select((photo, index) => (photo, index)))
        {
            var target = intro;
            if (index > 0)
            {
                var matches = blocks.Select((block, at) => (at, score: MediaSectionScore(photo.PhotoSection, photo.PhotoCaption, photo.EntryTitle, BlockText(block))))
                    .OrderByDescending(item => item.score).ToArray();
                if (matches.FirstOrDefault() is { score: > 0 } best) target = best.at;
            }
            if (!placements.TryGetValue(target, out var group)) placements[target] = group = [];
            group.Add(photo);
        }
        for (var index = 0; index < blocks.Count; index++)
        {
            var block = blocks[index];
            // Promoted named cards become ordinary article headings and paragraphs here.
            IReadOnlyList<ChatMarkdownBlock> content = block.Kind == "card"
                ? new[] { new ChatMarkdownBlock("heading", block.Runs, Level: 2) }.Concat(block.Children ?? []).ToArray() : [block];
            var hasPhotos = placements.TryGetValue(index, out var group);
            if (hasPhotos && index != intro && group!.Count == 1 && width >= 600)
            {
                var textWidth = (width - 24) * .48;
                var textBottom = BuildMarkdownBlocks(layout, content, x, y, textWidth);
                var photoBottom = BuildPhotoFigures(layout, group, x + textWidth + 24, y, width - textWidth - 24);
                y = Math.Max(textBottom, photoBottom) + 12;
            }
            else
            {
                y = BuildMarkdownBlocks(layout, content, x, y, width);
                if (hasPhotos) y = BuildPhotoFigures(layout, group!, x, y, width);
            }
        }
        if (blocks.Count == 0) y = BuildPhotoFigures(layout, photos, x, y, width);
        return y;
    }

    private static string BlockText(ChatMarkdownBlock block) => block.PlainText + " " + string.Join(' ', (block.Children ?? []).Select(BlockText));
    internal static int MediaSectionScore(string section, string caption, string subject, string blockText)
    {
        var body = GoogleEntryPhotoService.NormalizeName(blockText);
        var heading = GoogleEntryPhotoService.NormalizeName(section);
        if (heading.Length >= 6 && body.Contains(heading, StringComparison.Ordinal)) return 100;
        var ignored = ("the of and a an in to with for from image photo photos article new world ways " + GoogleEntryPhotoService.NormalizeName(subject)).Split(' ').ToHashSet();
        var tokens = GoogleEntryPhotoService.NormalizeName(section + " " + caption).Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(token => token.Length >= 4 && !ignored.Contains(token)).Distinct().ToArray();
        var shared = tokens.Where(token => (" " + body + " ").Contains(" " + token + " ", StringComparison.Ordinal)).ToArray();
        return shared.Length >= 2 || shared.Any(token => token.Length >= 7) ? shared.Length : 0;
    }

    private double BuildPhotoFigures(MessageLayout layout, IReadOnlyList<ContextControlAttachmentViewModel> photos, double x, double y, double width)
    {
        var index = 0;
        if (photos.Count % 2 == 1)
        {
            var heroWidth = Math.Min(width, 620);
            y = BuildPhotoFigure(layout, photos[0], x + (width - heroWidth) / 2, y + 6, heroWidth, true);
            index = 1;
        }
        for (; index < photos.Count; index += 2)
        {
            if (width < 380)
            {
                y = BuildPhotoFigure(layout, photos[index], x, y, width, false);
                if (index + 1 < photos.Count) y = BuildPhotoFigure(layout, photos[index + 1], x, y, width, false);
            }
            else
            {
                var columnWidth = (width - 12) / 2;
                var bottom = BuildPhotoFigure(layout, photos[index], x, y, columnWidth, false);
                var right = index + 1 < photos.Count ? BuildPhotoFigure(layout, photos[index + 1], x + columnWidth + 12, y, columnWidth, false) : y;
                y = Math.Max(bottom, right);
            }
        }
        return y + 4;
    }

    private double BuildPhotoFigure(MessageLayout layout, ContextControlAttachmentViewModel photo, double x, double y, double width, bool hero)
    {
        var bitmap = TryGetImageBitmap(photo.PreviewPath);
        if (bitmap is null) return y;
        var height = Math.Clamp(width * bitmap.PixelSize.Height / Math.Max(1, bitmap.PixelSize.Width), hero ? 140 : 110, hero ? 290 : 210);
        var rect = new Rect(x, y, width, height);
        layout.Attachments.Add(new(rect, photo.DisplayTitle, photo, true));
        layout.Hits.Add(new(rect, ChatTranscriptHitKind.OpenImagePreview, photo.PreviewPath));
        layout.EmbeddedWebPhotos.Add(photo.Path);
        var caption = string.IsNullOrWhiteSpace(photo.PhotoCaption) ? photo.EntryTitle : photo.PhotoCaption;
        y = AddRichParagraph(layout, [new(caption, Bold: true)], x + 4, rect.Bottom + 5, Math.Max(1, width - 8), ChatMetaFontSize);
        return AddRichParagraph(layout, [new((string.IsNullOrWhiteSpace(photo.PhotoKind) ? "Source image" : photo.PhotoKind) + " · "), new("Source ↗", Url: photo.Path)], x + 4, y, Math.Max(1, width - 8), ChatMetaFontSize) + 9;
    }
}
