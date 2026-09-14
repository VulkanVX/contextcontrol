using ContextControl.Workbench.Services;
using ContextControl.Workbench.ViewModels;

internal static class GoogleEntryPhotoTests
{
    private static int _checks;
    private static void Check(bool condition, string message) { _checks++; if (!condition) throw new InvalidOperationException(message); }
    internal static async Task<int> Run()
    {
        const string numbered = "1. **Garden Café**\n   - In Vilnius [1]\n2. **Monstro**\n   - Pizza [1]";
        const string table = "| # | Restaurant | Rating |\n| --- | --- | --- |\n| 1 | Garden Café | Unavailable [1] |\n| 2 | Monstro | Unavailable [1] |";
        const string bare = "**Garden Café**\n\nIn Vilnius [1].\n\n**Monstro**\n\nPizza [1].\n\n**Additional Notes**\n\nVerify opening hours.";
        foreach (var text in new[] { numbered, table, bare })
            Check(GoogleEntryPhotoService.EntryNames(text).SequenceEqual(["Garden Café", "Monstro"]), "Recognize numbered, table and bare-bold place entries, without searching section labels.");
        Check(GoogleEntryPhotoService.EntryNames("| Metric | Value |\n| --- | --- |\n| Time | 20 |\n").Count == 0, "Do not treat arbitrary measurement tables as places.");
        var htmlDetails = ChatMarkdown.Parse("1. **Garden Café**\n   <div class=\"entry\"><ul><li><strong>Details:</strong> Pizza [1]</li></ul></div>");
        static IEnumerable<ChatMarkdownRun> Runs(IEnumerable<ChatMarkdownBlock> blocks) => blocks.SelectMany(block => block.Runs.Concat(Runs(block.Children ?? [])));
        var htmlRuns = Runs(htmlDetails).ToArray();
        Check(htmlRuns.Any(run => run.Bold && run.Text.Contains("Details")) && htmlRuns.All(run => !run.Text.Contains("<div") && !run.Text.Contains("<li")),
            "Render the HTML detail lists observed in Qwen's answer as formatted text, without leaking tags.");
        Check(Runs(ChatMarkdown.Parse("```html\n<div>literal code</div>\n```")).Any(run => run.Code && run.Text.Contains("<div>")), "Keep HTML examples literal inside code fences.");
        Check(Runs(ChatMarkdown.Parse("<!DOCTYPE html>\n<div")).Any(), "Incomplete HTML and declarations must terminate safely as text.");
        var entries = new[] { "Garden Café", "Monstro" };
        static GoogleSearchSource Source(string title, string? image = null) => new(title, "https://example.com/place", "", image);
        Check(GoogleEntryPhotoService.IsEntrySource("Garden Café", Source("Garden Cafe - Vilnius"), entries), "Match accented names to ordinary search spellings.");
        Check(!GoogleEntryPhotoService.IsEntrySource("Monstro", Source("Monstrosity pizza"), entries), "Match whole names, not arbitrary substrings.");
        Check(!GoogleEntryPhotoService.IsEntrySource("Monstro", Source("Best pizza places: Monstro"), entries), "Reject a roundup hero image.");
        Check(!GoogleEntryPhotoService.IsEntrySource("Monstro", Source("Monstro and Garden Cafe"), entries), "Reject an ambiguous multi-place source.");
        Check(!GoogleEntryPhotoService.IsEntrySource("Monstro", Source("Monstro") with { Url = "file:///C:/photo" }, entries), "Use public photo source pages only.");
        Check(GoogleResearchService.PhotoSubject("Show me a photo of Nvidia 5090") == "Nvidia 5090", "Recognize standalone photo requests.");
        Check(GoogleResearchService.ParseQuery("{\"search\":false}", "Show me a photo of Nvidia 5090") == "Nvidia 5090 photo", "Explicit photo requests still search when a small model declines web access.");
        Check(GoogleEntryPhotoService.IsEntrySource("Nvidia 5090", Source("NVIDIA GeForce RTX 5090 graphics card"), ["Nvidia 5090"], productPhoto: true), "Match product photos with intervening brand-family words.");
        Check(!GoogleEntryPhotoService.IsEntrySource("Nvidia 5090", Source("NVIDIA GeForce RTX 5080 graphics card"), ["Nvidia 5090"], productPhoto: true), "Never substitute a different product number.");
        Check(GoogleEntryPhotoService.IsEntrySource("Nvidia 5090", Source("GeForce RTX 5090 graphics cards") with { Url = "https://www.nvidia.com/en-us/geforce/graphics-cards/50-series/rtx-5090/" }, ["Nvidia 5090"], productPhoto: true), "The official manufacturer hostname can identify the product brand.");
        Check(!GoogleEntryPhotoService.IsEntrySource("Nvidia 5090", Source("32 Free images of Nvidia GeForce RTX 5090") with { Url = "https://pixabay.com/images/search/nvidia/" }, ["Nvidia 5090"], productPhoto: true), "An image-search page is not an individual product photo source.");
        var root = Path.Combine(Path.GetTempPath(), "ContextControlEntryPhotos", Guid.NewGuid().ToString("N"));
        var cache = new GooglePhotoPreviewService(Path.Combine(root, "photos"));
        var image = "data:image/png;base64," + Convert.ToBase64String(GooglePhotoPreviewTests.Photo());
        var search = new GoogleSearchResult("pizza Vilnius", GoogleSearchContext.SearchUrl("pizza Vilnius"), [Source("Top pizza places", image)]);
        var research = new GoogleResearchResult(search, []);
        var browser = new FixtureBrowser(query => new GoogleSearchResult(query, GoogleSearchContext.SearchUrl(query),
            query.Contains("Garden") ? [Source("Garden Cafe - official", image)] : [Source("Monstro - official", image)]));
        var found = new List<GoogleEntryPhoto>();
        var singlePhoto = new List<GoogleEntryPhoto>();
        await GoogleEntryPhotoService.LoadAsync("A prose answer without a list.", new GoogleResearchResult(new GoogleSearchResult("Nvidia 5090 photo", GoogleSearchContext.SearchUrl("Nvidia 5090 photo"),
            [Source("NVIDIA GeForce RTX 5090", image)]), [], "Nvidia 5090"), browser, singlePhoto.Add, default, cache);
        Check(singlePhoto.Count == 1 && singlePhoto[0].EntryTitle == "Nvidia 5090", "A standalone photo request does not require a generated list or table.");
        await GoogleEntryPhotoService.LoadAsync(numbered, research, browser, found.Add, default, cache);
        Check(found.Count == 1 && found[0].EntryTitle == "Garden Café", "Attach the correct named photo and avoid reusing an identical image for another entry.");
        Check(browser.Queries.Count == 2 && browser.Queries[0].Contains("\"Garden Café\"") && !browser.Queries[0].Contains("[1]"), "Search just the displayed name with the original search query.");
        Check(found.All(photo => File.Exists(photo.PreviewPath)), "Only attach decoded and cached photos.");

        using var stop = new CancellationTokenSource();
        stop.Cancel();
        await GoogleEntryPhotoService.LoadAsync(numbered, research, browser, _ => throw new Exception("Unexpected cancelled photo"), stop.Token, cache);
        Check(true, "Cancelling optional photos preserves the already completed answer.");
        var blocked = new FixtureBrowser(_ => throw new InvalidOperationException("page unavailable"));
        await GoogleEntryPhotoService.LoadAsync(numbered, research, blocked, _ => throw new Exception("Unexpected blocked photo"), default, cache);
        Check(true, "A blocked photo search must leave the answer intact.");

        var attachment = new ContextControlAttachmentViewModel("Garden Cafe - official", "https://example.com/place", "web", found[0].PreviewPath, "Garden Café");
        var session = ChatSessionViewModel.CreateNew();
        session.Append(new LocalLlmChatMessageViewModel("assistant", numbered, attachments: [attachment]));
        var history = new ChatHistoryService(root);
        history.Save(new ChatHistoryDocument { Sessions = [session.ToData()] }, root, "chat", mirrorDefaultScope: false);
        var persisted = history.Load(root).Sessions.Single().Messages.Single().Attachments.Single();
        Check(persisted.EntryTitle == "Garden Café" && persisted.PreviewPath == attachment.PreviewPath, "Persist the entry identity independently from the source caption and photo path.");
        Console.WriteLine($"Entry photo regression passed: {_checks} checks.");
        return _checks;
    }

    private sealed class FixtureBrowser(Func<string, GoogleSearchResult> search) : IGoogleResearchBrowser
    {
        public List<string> Queries { get; } = [];
        public Task<GoogleSearchResult> SearchAsync(string query, CancellationToken cancellationToken)
        { cancellationToken.ThrowIfCancellationRequested(); Queries.Add(query); return Task.FromResult(search(query)); }
        public Task<GooglePageContent> ReadPageAsync(GoogleSearchSource source, CancellationToken cancellationToken)
            => Task.FromResult(new GooglePageContent(source.Url, "No photo"));
    }
}
