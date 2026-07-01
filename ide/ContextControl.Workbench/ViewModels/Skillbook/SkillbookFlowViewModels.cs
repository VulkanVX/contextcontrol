// CC-DESC: Presents editable Skillbook flows, sections, skills, and CC Flow prompt steps.

using ContextControl.Workbench.Services;

namespace ContextControl.Workbench.ViewModels;

public sealed class SkillbookFlowViewModel(SkillbookFlow flow) : ObservableObject
{
    public SkillbookFlow Model { get; } = flow;
    public string Id => Model.Id;
    public string Title => Model.Title;
    public string Summary => Model.Summary;
    public string Source => SkillbookService.NormalizeSource(Model.Source);
    public string SourceLabel => SkillbookService.SourceLabel(Source);
    public bool IsBuiltIn => Model.IsBuiltIn;
    public bool IsEditable => Model.IsEditable;
    public int SectionCount => Model.Sections.Count;
    public int SkillCount => Model.Sections.Sum(section => section.Skills.Count);
    public string CountLabel => $"{SectionCount:N0} section(s), {SkillCount:N0} skill(s)";
    public string EditLabel => IsBuiltIn ? "Locked" : IsEditable ? "Editable" : "Read-only";
}

public sealed class SkillbookSectionViewModel(SkillbookFlowViewModel flow, SkillbookSection section) : ObservableObject
{
    public SkillbookFlowViewModel Flow { get; } = flow;
    public SkillbookSection Model { get; } = section;
    public string Id => Model.Id;
    public string Title => Model.Title;
    public string Summary => Model.Summary;
    public string Source => SkillbookService.NormalizeSource(Model.Source);
    public string SourceLabel => SkillbookService.SourceLabel(Source);
    public bool IsBuiltIn => Model.IsBuiltIn;
    public bool IsEditable => Model.IsEditable;
    public int SkillCount => Model.Skills.Count;
    public string CountLabel => $"{SkillCount:N0} skill(s)";
    public string EditLabel => IsBuiltIn ? "Locked" : IsEditable ? "Editable" : "Read-only";
}

public sealed class SkillbookSkillViewModel(
    SkillbookFlowViewModel flow,
    SkillbookSectionViewModel section,
    SkillbookSkill skill) : ObservableObject
{
    public SkillbookFlowViewModel Flow { get; } = flow;
    public SkillbookSectionViewModel Section { get; } = section;
    public SkillbookSkill Model { get; } = skill;
    public string Id => Model.Id;
    public string Title => Model.Title;
    public string Text => Model.Text;
    public string Source => SkillbookService.NormalizeSource(Model.Source);
    public string SourceLabel => SkillbookService.SourceLabel(Source);
    public bool Enabled => Model.Enabled;
    public bool IsBuiltIn => Model.IsBuiltIn;
    public bool IsEditable => Model.IsEditable;
    public string Summary => $"{SourceLabel} - {Text.Length:N0} chars - {(Enabled ? "enabled" : "disabled")}";
    public string EditLabel => IsBuiltIn ? "Locked" : IsEditable ? "Editable" : "Read-only";
}

public sealed class PromptFlowStepViewModel(PromptFlowStep step) : ObservableObject
{
    private bool _isCurrent;

    public PromptFlowStep Model { get; } = step;
    public string Key => Model.Key;
    public string Title => Model.Title;
    public string Trigger => Model.Trigger;
    public string PromptSource => Model.PromptSource;
    public string Recipient => Model.Recipient;
    public string Attachments => Model.Attachments;
    public string SkillbookInjection => Model.SkillbookInjection;
    public bool SendsModelPrompt => Model.SendsModelPrompt;
    public string SendLabel => SendsModelPrompt ? "Model send" : "Local step";

    public bool IsCurrent
    {
        get => _isCurrent;
        private set => SetProperty(ref _isCurrent, value);
    }

    public string CurrentLabel => IsCurrent ? "Now" : "";

    public void ApplyCurrent(string key)
    {
        if (SetProperty(ref _isCurrent, Key.Equals(key, StringComparison.OrdinalIgnoreCase), nameof(IsCurrent)))
        {
            OnPropertyChanged(nameof(CurrentLabel));
        }
    }
}
