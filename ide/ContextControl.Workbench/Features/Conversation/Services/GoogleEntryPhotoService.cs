using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ContextControl.Workbench.Services;

public sealed record GoogleEntryPhoto(string EntryTitle, string SourceTitle, string SourceUrl, string PreviewPath);

/// <summary>Optional photos tied to named entries; never use a roundup's hero image for every place.</summary>
public static class GoogleEntryPhotoService
{
    public const int MaxEntries = 6;

    public static IReadOnlyList<string> EntryNames(string markdown) => ChatMarkdown.Parse(markdown)
        .SelectMany(block => block.Kind == "card" ? new[] { block } : block.Kind == "table" ? TableCards(block.Children ?? []) : [])
        .Select(block => CleanTitle(block.PlainText)).Where(title => title.Length is >= 4 and <= 120)
        .DistinctBy(NormalizeName).Take(MaxEntries).ToArray();

    public static IReadOnlyList<ChatMarkdownBlock> TableCards(IReadOnlyList<ChatMarkdownBlock> rows)
    {
        var headers = rows.FirstOrDefault(row => row.Kind == "tableHeader")?.Children ?? [];
        var nameIndex = -1;
        for (var i = 0; i < headers.Count; i++)
            if (Regex.IsMatch(headers[i].PlainText.Trim(), @"^(?:name|place|restaurant|pizzeria|pizza place|product|hotel|venue|model)(?:\s+name)?$", RegexOptions.IgnoreCase)) { nameIndex = i; break; }
        if (nameIndex < 0) return [];
        var cards = new List<ChatMarkdownBlock>();
        foreach (var row in rows.Where(row => row.Kind == "tableRow"))
        {
            var cells = row.Children ?? [];
            if (nameIndex >= cells.Count) continue;
            var title = CleanTitle(cells[nameIndex].PlainText);
            if (title.Length < 4) continue;
            var details = cells.Select((cell, index) => (cell, index)).Where(item => item.index != nameIndex)
                .Select(item => new ChatMarkdownBlock("paragraph", new[] { new ChatMarkdownRun((item.index < headers.Count ? headers[item.index].PlainText : "") + ": ", Bold: true) }
                    .Concat(item.cell.Runs).ToArray())).ToArray();
            cards.Add(new ChatMarkdownBlock("card", [new(title, Bold: true)], Children: details));
        }
        return cards;
    }

    public static string NormalizeName(string text)
    {
        var normalized = new StringBuilder();
        foreach (var c in CleanTitle(text).Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            normalized.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ');
        }
        return Regex.Replace(normalized.ToString(), @"\s+", " ").Trim();
    }

    public static bool IsEntrySource(string entry, GoogleSearchSource source, IReadOnlyList<string> allEntries)
    {
        var name = NormalizeName(entry);
        var title = " " + NormalizeName(source.Title) + " ";
        if (name.Length < 4 || !title.Contains(" " + name + " ", StringComparison.Ordinal)) return false;
        // A list page can mention a restaurant in its title while showing another place.
        if (Regex.IsMatch(title, @"\b(?:best|top|roundup|round up|places to|restaurants in)\b", RegexOptions.IgnoreCase)) return false;
        if (allEntries.Any(other => NormalizeName(other) != name && NormalizeName(other).Length >= 4
            && title.Contains(" " + NormalizeName(other) + " ", StringComparison.Ordinal))) return false;
        return GoogleSearchContext.IsPublicWebUrl(source.Url);
    }

    private static bool IsUsablePhoto(string? image) => GooglePhotoPreviewService.IsImageLocation(image)
        && (image!.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
            || !Regex.IsMatch(new Uri(image).AbsolutePath, @"(?:^|[/_.-])(?:logo|favicon|sprite|icon)(?:[/_.-]|$)", RegexOptions.IgnoreCase));

    public static async Task LoadAsync(string markdown, GoogleResearchResult research, IGoogleResearchBrowser browser,
        Action<GoogleEntryPhoto> found, CancellationToken cancellationToken,
        GooglePhotoPreviewService? images = null, TimeSpan? timeBudget = null)
    {
        if (research.Search is not { } search) return;
        var entries = EntryNames(markdown);
        if (entries.Count == 0) return;
        images ??= GooglePhotoPreviewService.Shared;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeBudget ?? TimeSpan.FromSeconds(30));
        var usedImages = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            foreach (var entry in entries)
            {
                deadline.Token.ThrowIfCancellationRequested();
                var matches = search.Sources.Where(source => IsEntrySource(entry, source, entries)).ToArray();
                if (matches.Length == 0)
                {
                    // The original query already went to Google. Add only the displayed
                    // entry name, rather than sending the answer or private chat history.
                    var query = GoogleSearchContext.NormalizeQuery($"\"{entry.Replace('"', ' ')}\" {search.Query}");
                    var specific = await browser.SearchAsync(query, deadline.Token);
                    matches = specific.Sources.Where(source => IsEntrySource(entry, source, entries)).ToArray();
                }
                foreach (var source in matches.Take(2))
                {
                    var candidate = source;
                    if (!IsUsablePhoto(candidate.ImageUrl))
                    {
                        try
                        {
                            var page = await browser.ReadPageAsync(source, deadline.Token);
                            if (IsUsablePhoto(page.ImageUrl)) candidate = source with { ImageUrl = page.ImageUrl };
                        }
                        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or IOException) { continue; }
                    }
                    if (!IsUsablePhoto(candidate.ImageUrl) || usedImages.Contains(candidate.ImageUrl!)) continue;
                    var paths = await images.LoadManyAsync([candidate], deadline.Token);
                    if (paths.Count == 0 || !File.Exists(paths[0])) continue;
                    usedImages.Add(candidate.ImageUrl!);
                    found(new GoogleEntryPhoto(entry, candidate.Title, candidate.Url, paths[0]));
                    break;
                }
            }
        }
        // Photos are optional. A slow or blocked source never removes a completed answer.
        catch (Exception ex) when (ex is OperationCanceledException or HttpRequestException or InvalidOperationException or IOException) { }
    }

    private static string CleanTitle(string title) => Regex.Replace(title, @"\s*\[\d{1,2}\]", "").Trim();
}
