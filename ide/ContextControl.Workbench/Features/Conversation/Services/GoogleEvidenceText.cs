using System.Text.RegularExpressions;

namespace ContextControl.Workbench.Services;

/// <summary>Reject search-interface labels as evidence or named places.</summary>
public static class GoogleEvidenceText
{
    public static bool IsInterfaceLabel(string? text)
    {
        var clean = GoogleEntryPhotoService.NormalizeName(text ?? "");
        return Regex.IsMatch(clean, @"^(?:translate (?:this|the) page|translated by google|isversti\b|tulkot (?:so|šo) lapu|tolgi see leht|perevesti etu stranicu|перевести (?:эту )?страницу|traduire cette page|ubersetze diese seite|more results|view all results)\b", RegexOptions.IgnoreCase);
    }
    public static string CleanSnippet(string text) => string.Join("\n", text.Split('\n').Where(line => !IsInterfaceLabel(line)));
    // Image delivery is a host operation. Remove the model's capability boilerplate;
    // the caller reports the actual lookup outcome after decoded photos are attached.
    public static string WithoutPhotoCapabilityClaims(string markdown)
    {
        return string.Join("\n\n", Regex.Split(markdown, @"\r?\n\s*\r?\n").Where(paragraph =>
            !(paragraph.Length <= 1000 && Regex.IsMatch(paragraph, @"^\s*[(\*]*(?:Note:\s*)?(?:As\b|I\b|We\b|ContextControl\b|Unfortunately\b)", RegexOptions.IgnoreCase)
              && Regex.IsMatch(paragraph, @"\b(?:I|we|AI|model|ContextControl)\b", RegexOptions.IgnoreCase)
              && Regex.IsMatch(paragraph, @"\b(?:can['’]t|cannot|unable to|not able to)\s+(?:directly\s+)?(?:display|show|provide|retrieve|access)\b[^.!?\n]{0,100}\b(?:images?|photos?|pictures?)\b", RegexOptions.IgnoreCase)))).Trim();
    }
    public static bool HasInterfaceEntry(string markdown) => Regex.Replace(markdown, @"<think>.*?(?:</think>|$)|```.*?(?:```|$)", "", RegexOptions.Singleline | RegexOptions.IgnoreCase)
        .Split('\n').Any(line => IsInterfaceLabel(CleanEntryStart(line)));
    private static string CleanEntryStart(string line) => Regex.Replace(line.Trim(), @"^(?:#{1,6}\s*|\d+\s*[.)]\s*|[-•*]\s+)?(?:\*\*)?", "").Trim();

    public static string OmitInterfaceEntries(string markdown)
    {
        var output = new List<string>();
        var omitting = false;
        foreach (var line in markdown.Split('\n'))
        {
            if (IsInterfaceLabel(CleanEntryStart(line))) { omitting = true; continue; }
            if (omitting && Regex.IsMatch(line, @"^\s*(?:\d+[.)]\s+|#{1,6}\s+|\*\*[^*]+\*\*\s*$|(?:Sources|References)\s*:)", RegexOptions.IgnoreCase)) omitting = false;
            if (!omitting) output.Add(line);
        }
        return string.Join('\n', output).Trim() + "\n\n*An entry was omitted because a search-page label did not identify a real place.*";
    }
    public static async Task<LocalLlmChatResult> ReviewAsync(LocalLlmChatResult answer, string preparedPrompt,
        Func<string, CancellationToken, Task<LocalLlmChatResult>> regenerate, CancellationToken cancellationToken)
    {
        if (!answer.Succeeded || !HasInterfaceEntry(answer.Message ?? "")) return answer;
        var prompt = preparedPrompt + "\nANSWER CHECK: A previous draft confused a Google interface label (such as Translate this page or Išversti šį puslapį) with a place name. Write the complete answer again using only actual venue names present in the source text. Omit a venue if its name cannot be established. Do not discuss the invalid draft.";
        var repaired = await regenerate(prompt, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return repaired.Succeeded && !string.IsNullOrWhiteSpace(repaired.Message) && !HasInterfaceEntry(repaired.Message) ? repaired
            : answer with { Message = OmitInterfaceEntries(answer.Message ?? "") };
    }
}
