using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ContextControl.Workbench.Services;

public sealed record GoogleEntryPhoto(string EntryTitle, string SourceTitle, string SourceUrl, string PreviewPath,
    string Caption = "", string Section = "", string Kind = "Article image", bool IsSubjectPhoto = false);

/// <summary>Optional photos tied to named entries; never use a roundup's hero image for every place.</summary>
public static class GoogleEntryPhotoService
{
    public const int MaxEntries = 6;

    public static IReadOnlyList<string> EntryNames(string markdown) => ChatMarkdown.Parse(markdown)
        .SelectMany(block => block.Kind == "card" ? new[] { block } : block.Kind == "table" ? TableCards(block.Children ?? []) : [])
        .Select(block => CleanTitle(block.PlainText)).Where(title => title.Length is >= 4 and <= 120 && !GoogleEvidenceText.IsInterfaceLabel(title))
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
        foreach (var c in CleanTitle(GoogleResearchService.CanonicalPhotoName(text)).Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            normalized.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ');
        }
        return Regex.Replace(normalized.ToString(), @"\s+", " ").Trim();
    }

    public static bool IsEntrySource(string entry, GoogleSearchSource source, IReadOnlyList<string> allEntries, bool productPhoto = false)
    {
        if (GoogleEvidenceText.IsInterfaceLabel(entry) || GoogleEvidenceText.IsInterfaceLabel(source.Title)) return false;
        var name = NormalizeName(entry);
        var title = " " + NormalizeName(source.Title) + " ";
        if (Regex.IsMatch(source.Title, @"\b(?:free images|stock photos|image search)\b", RegexOptions.IgnoreCase)
            || (Uri.TryCreate(source.Url, UriKind.Absolute, out var sourceUri) && Regex.IsMatch(sourceUri.AbsolutePath, @"/(?:images/)?search(?:/|$)", RegexOptions.IgnoreCase))) return false;
        var directMatch = title.Contains(" " + name + " ", StringComparison.Ordinal);
        if (!directMatch && productPhoto)
        {
            static string ProductName(string value) => NormalizeName(Regex.Replace(value, @"(?<=[A-Za-z])(?=\d)|(?<=\d)(?=[A-Za-z])", " "));
            var tokens = ProductName(entry).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var host = Uri.TryCreate(source.Url, UriKind.Absolute, out var productUri) ? productUri.Host : "";
            var productTitle = " " + ProductName(source.Title + " " + host) + " ";
            directMatch = tokens.Length >= 2 && tokens.Any(token => token.Any(char.IsDigit))
                && tokens.All(token => productTitle.Contains(" " + token + " ", StringComparison.Ordinal));
        }
        if (name.Length < 4 || !directMatch) return false;
        // A list page can mention a restaurant in its title while showing another place.
        if (Regex.IsMatch(title, @"\b(?:best|top|roundup|round up|places to|restaurants in)\b", RegexOptions.IgnoreCase)) return false;
        if (allEntries.Any(other => NormalizeName(other) != name && NormalizeName(other).Length >= 4
            && title.Contains(" " + NormalizeName(other) + " ", StringComparison.Ordinal))) return false;
        return GoogleSearchContext.IsPublicWebUrl(source.Url);
    }

    public static bool MentionsName(string name, string text)
    {
        var key = NormalizeName(name);
        var haystack = " " + NormalizeName(text) + " ";
        if (haystack.Contains(" " + key + " ", StringComparison.Ordinal)) return key.Length >= 4;
        // Full and abbreviated Warcraft names should identify the same subject.
        if (key.StartsWith("world of warcraft ", StringComparison.Ordinal))
            return haystack.Contains(" warcraft " + key[18..] + " ", StringComparison.Ordinal);
        return false;
    }

    public static bool MatchesPhotoTopic(string entry, string topic, GoogleSearchSource source, IReadOnlyList<string>? entries = null)
    {
        var context = NormalizeName(topic);
        var evidence = " " + NormalizeName(source.Title + " " + source.Snippet + " " + Uri.UnescapeDataString(source.Url)) + " ";
        // A race name such as Human, Gnome or Dwarf is not enough to identify a game.
        // Keep the franchise anchor even when Google happens to return an exact name match.
        if (Regex.IsMatch(context, @"\b(?:warcraft|wow)\b"))
            return Regex.IsMatch(evidence, @"\b(?:warcraft|wow|wowhead|warcraftwiki)\b");
        var ignored = ("a an the is are what which who when where how why of in on at to for from and or with by "
            + "me my you your it its this that these those about find show search google latest newest current new best top "
            + "photo photos picture pictures image images screenshot screenshots logo logos trailer trailers review reviews "
            + "information explain compare please game games person people place places restaurant restaurants bar bars hotel hotels "
            + NormalizeName(entry) + " " + string.Join(' ', (entries ?? []).Select(NormalizeName))).Split(' ').ToHashSet();
        var anchors = context.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(word => word.Length >= 4 && word.Any(char.IsLetter) && !ignored.Contains(word)).Distinct().ToArray();
        return anchors.Length == 0 || anchors.Any(word => evidence.Contains(" " + word + " ", StringComparison.Ordinal));
    }

    public static bool IsRelevantSavedPhoto(ContextControl.Workbench.ViewModels.ContextControlAttachmentViewModel photo,
        IEnumerable<ContextControl.Workbench.ViewModels.ContextControlAttachmentViewModel> attachments)
    {
        // Old histories did not store the photo query. A clear franchise in the answer's
        // numbered sources can still reject the unrelated pictures from the reported chat.
        var sources = string.Join(' ', attachments.Where(a => a.Kind == "web" && Regex.IsMatch(a.Label, @"^\[\d+\]" )).Select(a => a.Label + " " + a.Path));
        return !Regex.IsMatch(NormalizeName(sources), @"\b(?:warcraft|wow)\b")
            || MatchesPhotoTopic(photo.EntryTitle, "World of Warcraft", new GoogleSearchSource(photo.Label, photo.Path, photo.PhotoCaption + " " + photo.PhotoSection));
    }

    public static bool IsUsablePhoto(GooglePageImage image, string entry, bool subject)
    {
        if (!GooglePhotoPreviewService.IsImageLocation(image.Url)) return false;
        if (Regex.IsMatch(image.Caption + " " + image.Section, @"\b(?:drop the ads|subscribe|newsletters?|advertisement|sponsored|sign up|join our|cookie consent|premium membership)\b", RegexOptions.IgnoreCase)) return false;
        if (image.Url.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return image.Kind != "Logo" || subject && MentionsName(entry, image.Caption);
        var path = new Uri(image.Url).AbsolutePath;
        if (Regex.IsMatch(path, @"(?:^|[/_.-])(?:favicon|sprite|icon)(?:[/_.-]|$)", RegexOptions.IgnoreCase)) return false;
        var logo = image.Kind == "Logo" || Regex.IsMatch(path, @"(?:^|[/_.-])logo(?:[/_.-]|$)", RegexOptions.IgnoreCase);
        return !logo || subject && MentionsName(entry, image.Caption + " " + Uri.UnescapeDataString(path));
    }

    public static async Task LoadAsync(string markdown, GoogleResearchResult research, IGoogleResearchBrowser browser,
        Action<GoogleEntryPhoto> found, CancellationToken cancellationToken,
        GooglePhotoPreviewService? images = null, TimeSpan? timeBudget = null, Action<string>? status = null,
        IReadOnlyList<string>? onlyEntries = null)
    {
        if (research.Search is not { } search) return;
        var entries = onlyEntries ?? (research.PhotoSubject is { Length: > 0 } subject ? new[] { subject } : EntryNames(markdown));
        var contextEntries = EntryNames(markdown).Concat(entries).DistinctBy(NormalizeName).ToArray();
        if (entries.Count == 0) return;
        images ??= GooglePhotoPreviewService.Shared;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeBudget ?? TimeSpan.FromSeconds(research.PhotoSubject is not null ? 45 : Math.Min(80, 12 * entries.Count + 10)));
        var usedImages = new HashSet<string>(StringComparer.Ordinal);
        var topic = research.Topic + " " + search.Query;
        bool InTopic(string entry, GoogleSearchSource source) => MatchesPhotoTopic(entry, topic, source, contextEntries);
        bool Matches(string entry, GoogleSearchSource source) => InTopic(entry, source) && IsEntrySource(entry, source, contextEntries, research.PhotoSubject is not null);
        var subjectPhotos = research.PhotoSubject is not null;
        foreach (var (entry, entryIndex) in entries.Select((entry, index) => (entry, index)))
        {
            if (deadline.IsCancellationRequested) break;
            using var entryDeadline = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
            entryDeadline.CancelAfter(TimeSpan.FromSeconds(subjectPhotos ? 43 : 12));
            var token = entryDeadline.Token;
            var count = 0;
            var wanted = subjectPhotos ? 4 : 1;
            status?.Invoke($"Finding photos · {entryIndex + 1}/{entries.Count} · {entry}");
            async Task TryImages(GoogleSearchSource source, IReadOnlyList<GooglePageImage>? candidates, string? fallback = null)
            {
                if (!GoogleSearchContext.IsPublicWebUrl(source.Url) || !InTopic(entry, source)) return;
                var pageMatches = Matches(entry, source);
                var available = (candidates ?? []).ToList();
                if (pageMatches && fallback is not null) available.Add(new(fallback));
                foreach (var candidate in available.DistinctBy(image => image.Url).Take(24))
                {
                    token.ThrowIfCancellationRequested();
                    if (count >= wanted) return;
                    // A roundup can contribute a photo only when that image's own caption/section identifies the venue.
                    if ((!pageMatches && !MentionsName(entry, candidate.Caption + " " + candidate.Section)) || !IsUsablePhoto(candidate, entry, subjectPhotos)) continue;
                    var identity = candidate.Url.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ? candidate.Url : new Uri(candidate.Url).GetLeftPart(UriPartial.Path);
                    if (usedImages.Contains(identity)) continue;
                    var paths = await images.LoadManyAsync([source with { ImageUrl = candidate.Url }], token);
                    if (paths.Count == 0 || !File.Exists(paths[0])) continue;
                    usedImages.Add(identity); count++;
                    var caption = string.IsNullOrWhiteSpace(candidate.Caption) || Regex.IsMatch(candidate.Caption, @"^(?:image|alternate.image.name|photo)$", RegexOptions.IgnoreCase)
                        ? candidate.Section : candidate.Caption;
                    found(new(entry, source.Title, source.Url, paths[0], caption, candidate.Section, candidate.Kind, subjectPhotos));
                }
            }
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            async Task Visit(IEnumerable<GoogleSearchSource> sources)
            {
                foreach (var source in sources.Where(source => Matches(entry, source)).Take(3))
                {
                    if (count >= wanted) return;
                    if (!visited.Add(source.Url)) continue;
                    try
                    {
                        var page = await browser.ReadPageAsync(source, token);
                        var resolved = source with { Url = page.Url, Title = string.IsNullOrWhiteSpace(page.Title) ? source.Title : page.Title };
                        await TryImages(resolved, page.Images, page.ImageUrl ?? source.ImageUrl);
                    }
                    catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or IOException) { }
                }
            }
            try
            {
                // Reuse images already read for the answer before requesting another page or query.
                foreach (var page in research.Pages.Where(page => page.FullPageRead && page.SourceNumber > 0 && page.SourceNumber <= search.Sources.Count))
                {
                    var source = search.Sources[page.SourceNumber - 1];
                    await TryImages(source, page.Images, source.ImageUrl);
                    if (page.Images is { Count: > 0 }) visited.Add(source.Url);
                    if (count >= wanted) break;
                }
                if (count < wanted) await Visit(search.Sources);
                foreach (var query in new[] { $"\"{entry.Replace('"', ' ')}\" {search.Query}", $"\"{entry.Replace('"', ' ')}\" {search.Query} photos screenshots" })
                {
                    if (count >= wanted) break;
                    var specific = await browser.SearchAsync(GoogleSearchContext.NormalizeQuery(query), token);
                    await Visit(specific.Sources);
                }
            }
            // Give the next venue its own budget; one slow or blocked site must not consume the whole list.
            catch (Exception ex) when (ex is OperationCanceledException or HttpRequestException or InvalidOperationException or IOException) { }
        }
    }

    private static string CleanTitle(string title) => Regex.Replace(title, @"\s*\[\d{1,2}\]", "").Trim();
}
