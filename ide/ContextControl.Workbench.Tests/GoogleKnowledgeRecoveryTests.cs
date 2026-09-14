using ContextControl.Workbench.Services;

internal static class GoogleKnowledgeRecoveryTests
{
    public static async Task<int> Run()
    {
        var checks = 0;
        void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); checks++; }
        const string question = "What is NVIDIA RTX 5090?";
        var unknown = new LocalLlmChatResult(true, "done", "I don't have information about that product in my training data.");
        Check(GoogleKnowledgeRecovery.NeedsLookup(question, unknown.Message), "Missing information should trigger public research.");
        foreach (var answer in new[] { "I do not know what that is.", "I'm not familiar with this product.", "My knowledge cutoff is June 2024.", "I can't provide current information about it." })
            Check(GoogleKnowledgeRecovery.NeedsLookup(question, answer), "Common knowledge-gap wording should be recognized: " + answer);
        foreach (var answer in new[] { "It is a graphics card [1].", "<think>I don't know; verify it.</think>It is a graphics card [1].", "> I don't know.\nThat quote is an example.", "The phrase \"I don't know\" conveys uncertainty.", "```text\nI don't know\n```\nExample output." })
            Check(!GoogleKnowledgeRecovery.NeedsLookup(question, answer), "Only the assistant's visible admission should trigger research.");
        foreach (var request in new[] { "Can you use Google?", "Rewrite this text: I don't know", "Translate this to French", "What is my password?", @"Read C:\Users\person\private.txt", "What is my email?", "What is the api_key=secret?" })
            Check(!GoogleKnowledgeRecovery.NeedsLookup(request, unknown.Message), "Non-public and text transformation requests must not become fallback queries.");

        var preparations = 0;
        var generations = 0;
        Task<string?> Prepare(CancellationToken token) { token.ThrowIfCancellationRequested(); preparations++; return Task.FromResult<string?>("REAL WEB EVIDENCE"); }
        Task<LocalLlmChatResult> Generate(string prompt, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); generations++;
            Check(prompt == "REAL WEB EVIDENCE", "The second generation must receive researched context.");
            return Task.FromResult(new LocalLlmChatResult(true, "researched", "A graphics card, according to its manufacturer [1]."));
        }
        var recovered = await GoogleKnowledgeRecovery.RecoverAsync(question, unknown, true, false, Prepare, Generate, default);
        Check(recovered.Status == "researched" && preparations == 1 && generations == 1, "An unknown answer should be replaced by one researched answer.");
        preparations = generations = 0;
        foreach (var (enabled, searched, result) in new[] { (false, false, unknown), (true, true, unknown), (true, false, unknown with { Succeeded = false }), (true, false, unknown with { Message = "A graphics card." }) })
            Check(ReferenceEquals(await GoogleKnowledgeRecovery.RecoverAsync(question, result, enabled, searched, Prepare, Generate, default), result), "Disabled, already researched, failed and ordinary answers must remain unchanged.");
        Check(preparations == 0 && generations == 0, "Skipped recovery must make no planner, browser, or generation calls.");
        var stillUnknown = await GoogleKnowledgeRecovery.RecoverAsync(question, unknown, true, false, Prepare,
            (_, _) => { generations++; return Task.FromResult(unknown); }, default);
        Check(ReferenceEquals(stillUnknown, unknown) && preparations == 1 && generations == 1, "A second uncertain answer must not start another lookup loop.");
        var failed = await GoogleKnowledgeRecovery.RecoverAsync(question, unknown, true, false,
            _ => throw new InvalidOperationException("Google is unavailable."), Generate, default);
        Check(failed.Message!.StartsWith(unknown.Message!) && failed.Message.Contains("Google lookup could not be completed"), "Lookup failures preserve the original answer with an honest limitation.");
        var noPlan = await GoogleKnowledgeRecovery.RecoverAsync(question, unknown, true, false, _ => Task.FromResult<string?>(null), Generate, default);
        Check(ReferenceEquals(noPlan, unknown), "No web evidence must not trigger a fabricated sourced answer.");
        var failedAnswer = await GoogleKnowledgeRecovery.RecoverAsync(question, unknown, true, false, Prepare,
            (_, _) => Task.FromResult(new LocalLlmChatResult(false, "failed")), default);
        Check(failedAnswer.Message!.Contains("could not be completed"), "Failed second generation must be visible without discarding the first draft.");
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        try { await GoogleKnowledgeRecovery.RecoverAsync(question, unknown, true, false, Prepare, Generate, cancellation.Token); throw new Exception("Cancellation was swallowed."); }
        catch (OperationCanceledException) { checks++; }
        Console.WriteLine($"Google knowledge recovery passed: {checks} checks.");
        return checks;
    }
}
