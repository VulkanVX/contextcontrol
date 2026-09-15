using ContextControl.Workbench.Services;

internal static class ChatActionTests
{
    public static async Task Run()
    {
        var checks = 0;
        void Check(bool condition, string why) { checks++; if (!condition) throw new InvalidOperationException(why); }
        Check(ChatActionCatalog.All.Count == 7 && ChatActionCatalog.All.All(a => a.Description.Length > 30), "Every action has a description.");
        Check(ChatActionCatalog.Parse(" /GaMe\nSnake ") is { Action.Command: "/game", Argument: "Snake" }, "Parse the command separately from the user's request.");
        Check(ChatActionCatalog.Parse("/gamex snake").Action is null && ChatActionCatalog.Parse("please /game snake").Action is null, "Only exact leading commands invoke actions.");
        Check(ChatActionCatalog.Match("/g").Single().Command == "/game", "Slash suggestions filter the shared catalog.");
        var browser = new ActionBrowser();
        var search = await GoogleResearchService.ResearchAsync("capital of Estonia", (_, _) => Task.FromResult("{\"search\":false}"), browser, _ => { }, default);
        Check(!search.DidSearch && browser.Searches == 0, "Automatic mode retains its no-search choice for a static question.");
        search = await GoogleResearchService.ResearchAsync("capital of Estonia", (_, _) => Task.FromResult("{\"search\":false}"), browser, _ => { }, default, forceSearch: true);
        Check(search.DidSearch && browser.Searches == 1, "An explicit search action runs even if the planner mistakenly chooses no search.");
        var hardware = LocalResourceTests.Hardware();
        var memory = new LocalModelMemory(16, 64, 262144, 131072);
        var adaptive = new LocalResourceSettings(Enabled: true, ContextMode: "Adaptive", MaxContextTokens: 65536);
        LocalContextDecision Choose(string prompt, bool think = false, bool game = false, LocalResourceSettings? settings = null, int? server = null)
            => LocalContextBudget.Choose(settings ?? adaptive, memory, hardware, 8192, prompt, think, game, true, server);
        Check(Choose("hello").Tokens == 4096, "Adaptive uses a small allocation for a short request.");
        Check(Choose(new string('x', 28000)).Tokens == 16384, "Adaptive grows with prepared input.");
        Check(Choose("snake", true, true).Tokens == 16384, "Game and thinking get extra output room.");
        Check(Choose("x", settings: adaptive with { ContextMode = "Long · 32K" }).Tokens == 32768, "Long context is selectable independently of the 8K smoke test.");
        Check(Choose("x", settings: adaptive with { ContextMode = "Fast · 8K" }).Tokens == 8192, "Fast is an upper target of 8K.");
        Check(Choose("x", settings: adaptive with { ContextMode = "Custom", MaxContextTokens = 65536 }).Tokens > 32768, "Custom mode supports more than the old 32K ceiling when memory fits.");
        Check(Choose(new string('x', 1000000)).EnoughRoom == false, "Oversized input reports insufficient room.");
        Check(Choose("x", settings: adaptive with { Enabled = false }).Tokens == 8192, "Disabling adaptation preserves the manual budget.");
        Check(Choose(new string('x', 28000), server: 4096) is { Tokens: 4096, EnoughRoom: false }, "A request cannot enlarge a server's loaded allocation.");
        var low = LocalContextBudget.Choose(adaptive, memory, LocalResourceTests.Hardware(freeRam: 26, gpu: 0), 8192, new string('x', 90000), true, true, true);
        Check(low.Tokens < 32768, "Available RAM and reserve constrain adaptive growth.");
        var max = LocalResourcePlanner.Plan(memory, hardware, adaptive with { ContextMode = "Maximum fit" });
        Check(max.Fits && max.ContextTokens > 32768 && max.ContextTokens <= 131072, "Maximum fit respects actual memory and the model ceiling.");
        var root = Path.Combine(Path.GetTempPath(), "ContextControlActionTests", Guid.NewGuid().ToString("N"));
        var original = new LocalLlmChatResult(true, "done", "```html\n" + GameLabTests.Fixture + "\n```");
        var validations = 0; var repairs = 0;
        var review = await GameCreationReview.RunAsync(original, "Snake", Path.Combine(root, "fixed"), (_, _) =>
            Task.FromResult(++validations == 1 ? new GameValidationResult(true, ["Cannot read properties of undefined (reading length)"], "Startup error") : new(true, [], "Passed")),
            (prompt, _) => { repairs++; Check(prompt.Contains("Cannot read properties") && prompt.Contains(GameLabTests.Fixture), "Repair receives exact final source and observed errors."); return Task.FromResult(original); }, _ => { }, default);
        Check(review.Validation.Passed && repairs == 1 && validations == 2, "Each repair is executed again before passing.");
        Check(File.Exists(Path.Combine(root, "fixed", "attempt-0.html")) && File.Exists(Path.Combine(root, "fixed", "attempt-1.html")), "Preserve original and repaired code.");
        Check(GameArtifact.Parse(review.Response.Message!) is not null, "Validation footer does not hide the Run action.");
        repairs = 0;
        review = await GameCreationReview.RunAsync(original, "Snake", Path.Combine(root, "bounded"), (_, _) => Task.FromResult(new GameValidationResult(true, ["broken"], "Error")),
            (_, _) => { repairs++; return Task.FromResult(original); }, _ => { }, default);
        Check(repairs == 2 && !review.Validation.Passed && review.Response.Message!.Contains("Game needs review"), "Unresolved errors stop after two repairs and never claim success.");
        review = await GameCreationReview.RunAsync(original, "Snake", Path.Combine(root, "unavailable"), (_, _) => Task.FromResult(new GameValidationResult(false, [], "Browser unavailable")),
            (_, _) => throw new Exception("Don't spend tokens fixing an unavailable browser"), _ => { }, default);
        Check(!review.Validation.Passed, "Unavailable checks do not pass or trigger model repairs.");
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        review = await GameCreationReview.RunAsync(original, "Snake", Path.Combine(root, "cancelled"), (_, _) => throw new Exception("No check on cancellation"),
            (_, _) => throw new Exception("No repair on cancellation"), _ => { }, cancel.Token);
        Check(!review.Validation.Passed && review.Response.Message!.Contains(GameLabTests.Fixture), "Cancellation preserves final generated code.");
        Console.WriteLine($"CHAT_ACTIONS_PASS {checks} checks");
    }
    private sealed class ActionBrowser : IGoogleResearchBrowser
    {
        public int Searches;
        public Task<GoogleSearchResult> SearchAsync(string query, CancellationToken token)
        { Searches++; return Task.FromResult(new GoogleSearchResult(query, GoogleSearchContext.SearchUrl(query), [new("Estonia", "https://example.org/estonia", "Tallinn is the capital of Estonia.")])); }
        public Task<GooglePageContent> ReadPageAsync(GoogleSearchSource source, CancellationToken token)
            => Task.FromResult(new GooglePageContent(source.Url, "Tallinn is the capital of Estonia."));
    }
}
