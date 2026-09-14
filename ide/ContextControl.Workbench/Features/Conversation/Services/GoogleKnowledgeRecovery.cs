using System.Text.RegularExpressions;

namespace ContextControl.Workbench.Services;

/// <summary>One bounded lookup when a completed local answer acknowledges missing public knowledge.</summary>
public static partial class GoogleKnowledgeRecovery
{
    public static bool IsPublicLookupQuestion(string question) => question.Length is > 2 and <= 800
        && !NonResearchRequest().IsMatch(question.Trim()) && !PrivateData().IsMatch(question);

    public static bool NeedsLookup(string question, string? answer)
    {
        if (!IsPublicLookupQuestion(question) || string.IsNullOrWhiteSpace(answer)) return false;
        // A model may doubt something while reasoning and still produce a supported answer.
        // Quoted examples and code are not admissions by the assistant either.
        var visible = Regex.Replace(answer, @"<think>.*?(?:</think>|$)|```.*?(?:```|$)", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        visible = Regex.Replace(visible, @"(?m)^\s*>.*$|[""“][^""”]*[""”]", "");
        return KnowledgeGap().IsMatch(visible.Length <= 2200 ? visible : visible[..2200]);
    }

    public static async Task<LocalLlmChatResult> RecoverAsync(string question, LocalLlmChatResult original,
        bool enabled, bool alreadySearched, Func<CancellationToken, Task<string?>> prepare,
        Func<string, CancellationToken, Task<LocalLlmChatResult>> generate, CancellationToken cancellationToken)
    {
        if (!enabled || alreadySearched || !original.Succeeded || !NeedsLookup(question, original.Message)) return original;
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var prompt = await prepare(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (prompt is null) return original;
            var researched = await generate(prompt, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            // Never recursively retry a second uncertain answer.
            return researched.Succeeded && !string.IsNullOrWhiteSpace(researched.Message)
                ? researched : WithLimitation(original, "The answer from web sources could not be completed.");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or HttpRequestException or TimeoutException)
        {
            return WithLimitation(original, "Google lookup could not be completed. " + ex.Message);
        }
    }

    private static LocalLlmChatResult WithLimitation(LocalLlmChatResult original, string limitation) => original with
    {
        Status = "Local answer · web lookup incomplete",
        Message = original.Message + "\n\n" + limitation
    };

    [GeneratedRegex(@"^(?:can|could|do|are)\s+you\s+(?:use\s+google|(?:web\s+)?search|browse|access)\b|^(?:(?:please|can\s+you|could\s+you)\s+)*(?:rewrite|translate|summari[sz]e|proofread|edit|format|write\s+(?:a|an|some)|hello|hi|hey)\b", RegexOptions.IgnoreCase)]
    private static partial Regex NonResearchRequest();

    [GeneratedRegex(@"```|[A-Za-z]:\\|/(?:Users|home|etc)/|\b(?:password|passwd|secret|credentials?|api[_ -]?key|access[_ -]?token|private[_ -]?key)\b|\b(?:my|our)\s+(?:files?|documents?|emails?|account|address|phone|logs?|code)\b|\S+@\S+", RegexOptions.IgnoreCase)]
    private static partial Regex PrivateData();

    [GeneratedRegex(@"\bI\s+(?:(?:do\s+not|don['’]t)\s+(?:know|have\s+(?:(?:any|enough|reliable|current|specific|detailed|up.to.date)\s+){0,3}(?:information|knowledge|data))|(?:cannot|can['’]t|am\s+unable\s+to)\s+(?:find|provide|access)\s+(?:(?:any|current|reliable|up.to.date|real.time|specific)\s+){0,3}(?:information|details|data))\b|\bI(?:['’]m|\s+am)\s+(?:not\s+(?:familiar|aware)|unaware)\b|\b(?:my\s+(?:knowledge|training)(?:\s+data)?\s+(?:cutoff|cut.off|only\s+(?:goes|extends))|not\s+(?:in|part\s+of)\s+my\s+(?:knowledge|training))\b", RegexOptions.IgnoreCase)]
    private static partial Regex KnowledgeGap();
}
