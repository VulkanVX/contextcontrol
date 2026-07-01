// CC-DESC: Presents a global or project Skillbook instruction entry.

using ContextControl.Workbench.Services;

namespace ContextControl.Workbench.ViewModels;

public sealed class SkillbookEntryViewModel(SkillbookEntry entry) : ObservableObject
{
    public string Key { get; } = entry.Key;
    public string Title { get; } = entry.Title;
    public string Text { get; } = entry.Text;
    public string Source { get; } = SkillbookService.NormalizeSource(entry.Source);
    public bool Enabled { get; } = entry.Enabled;
    public string FlowTitle { get; } = entry.FlowTitle;
    public string SectionName { get; } = entry.SectionTitle;
    public bool IsEditable { get; } = entry.IsEditable;
    public bool IsBuiltIn { get; } = entry.IsBuiltIn;
    public string SourceLabel => SkillbookService.SourceLabel(Source);
    public int SourceRank => SkillbookService.SourceRank(Source);
    public string SectionTitle => SkillbookService.NormalizeSource(Source).ToLowerInvariant() switch
    {
        "codex" => "Codex Instructions",
        "cc-main" => "CC Main",
        "cc-flow" => "CC Flow",
        "project" => "Project Skillbook",
        "global" => "Global Skillbook",
        _ => "Skillbook"
    };
    public string Summary => $"{SourceLabel} - {Text.Length:N0} chars";
}
