// CC-DESC: Owns Skillbook flow editing and CC Flow prompt-step visibility.

using System.Collections.ObjectModel;
using System.Windows.Input;
using ContextControl.Workbench.Services;

namespace ContextControl.Workbench.ViewModels;

public sealed partial class ContextControlViewModel
{
    private SkillbookFlowViewModel? _selectedSkillbookFlow;
    private SkillbookSectionViewModel? _selectedSkillbookSection;
    private SkillbookSkillViewModel? _selectedSkillbookSkill;
    private string _skillbookFlowTitleText = "";
    private string _skillbookSectionTitleText = "";
    private string _skillbookSkillTitleText = "";
    private string _skillbookEditorText = "";
    private string _skillbookEditorStatus = "Skillbook loaded.";
    private bool _skillbookEditorEnabled = true;
    private bool _isSkillbookInfoVisible = true;
    private bool _isSkillbookBuiltInEditMode;
    private bool _isLoadingSkillbookSelection;

    public ObservableCollection<SkillbookFlowViewModel> SkillbookFlows { get; } = [];
    public ObservableCollection<SkillbookSectionViewModel> SkillbookSections { get; } = [];
    public ObservableCollection<SkillbookSkillViewModel> SkillbookSkills { get; } = [];
    public ObservableCollection<PromptFlowStepViewModel> PromptFlowSteps { get; } = [];

    public SkillbookFlowViewModel? SelectedSkillbookFlow
    {
        get => _selectedSkillbookFlow;
        set
        {
            if (SetProperty(ref _selectedSkillbookFlow, value))
            {
                SkillbookFlowTitleText = value?.Title ?? "";
                RefreshSkillbookSections(value);
                RaiseSkillbookCommandStates();
            }
        }
    }

    public SkillbookSectionViewModel? SelectedSkillbookSection
    {
        get => _selectedSkillbookSection;
        set
        {
            if (SetProperty(ref _selectedSkillbookSection, value))
            {
                SkillbookSectionTitleText = value?.Title ?? "";
                RefreshSkillbookSkills(value);
                RaiseSkillbookCommandStates();
            }
        }
    }

    public SkillbookSkillViewModel? SelectedSkillbookSkill
    {
        get => _selectedSkillbookSkill;
        set
        {
            if (SetProperty(ref _selectedSkillbookSkill, value))
            {
                LoadSelectedSkill(value);
                RaiseSkillbookCommandStates();
            }
        }
    }

    public string SkillbookFlowTitleText
    {
        get => _skillbookFlowTitleText;
        set
        {
            if (SetProperty(ref _skillbookFlowTitleText, value ?? ""))
            {
                RaiseSkillbookCommandStates();
            }
        }
    }

    public string SkillbookSectionTitleText
    {
        get => _skillbookSectionTitleText;
        set
        {
            if (SetProperty(ref _skillbookSectionTitleText, value ?? ""))
            {
                RaiseSkillbookCommandStates();
            }
        }
    }

    public string SkillbookSkillTitleText
    {
        get => _skillbookSkillTitleText;
        set
        {
            if (SetProperty(ref _skillbookSkillTitleText, value ?? "") && !_isLoadingSkillbookSelection)
            {
                RaiseSkillbookCommandStates();
            }
        }
    }

    public string SkillbookEditorText
    {
        get => _skillbookEditorText;
        set
        {
            if (SetProperty(ref _skillbookEditorText, value ?? "") && !_isLoadingSkillbookSelection)
            {
                RaiseSkillbookCommandStates();
            }
        }
    }

    public bool SkillbookEditorEnabled
    {
        get => _skillbookEditorEnabled;
        set
        {
            if (SetProperty(ref _skillbookEditorEnabled, value) && !_isLoadingSkillbookSelection)
            {
                RaiseSkillbookCommandStates();
            }
        }
    }

    public string SkillbookEditorStatus
    {
        get => _skillbookEditorStatus;
        private set => SetProperty(ref _skillbookEditorStatus, value ?? "");
    }

    public bool IsSkillbookInfoVisible
    {
        get => _isSkillbookInfoVisible;
        private set => SetProperty(ref _isSkillbookInfoVisible, value);
    }

    public bool CanEditSelectedSkillbookFlow => SelectedSkillbookFlow?.IsEditable == true;
    public bool CanEditSelectedSkillbookSection => SelectedSkillbookSection?.IsEditable == true;
    public bool CanRenameSelectedSkillbookSection => SelectedSkillbookSection?.IsEditable == true
        && !string.Equals(SelectedSkillbookSection.Id, "legacy", StringComparison.OrdinalIgnoreCase);
    public bool CanEditSelectedSkillbookSkill => SelectedSkillbookSkill?.IsEditable == true
        || (SelectedSkillbookSkill?.IsBuiltIn == true && _isSkillbookBuiltInEditMode);
    public bool IsSkillbookFlowTitleReadOnly => !CanEditSelectedSkillbookFlow;
    public bool IsSkillbookSectionTitleReadOnly => !CanRenameSelectedSkillbookSection;
    public bool IsSkillbookEditorReadOnly => !CanEditSelectedSkillbookSkill;
    public bool ShowSkillbookBuiltInEditButton => SelectedSkillbookSkill?.IsBuiltIn == true;
    public string SkillbookBuiltInEditButtonLabel => _isSkillbookBuiltInEditMode ? "Lock" : "Edit";
    public string SkillbookEditorModeLabel => SelectedSkillbookSkill?.IsBuiltIn == true
        ? _isSkillbookBuiltInEditMode ? "Editing built-in override" : "Read-only built-in"
        : CanEditSelectedSkillbookSkill ? "Editable markdown" : "Read-only";
    public string SkillbookFlowCountSummary => $"{SkillbookFlows.Count:N0} flow(s); {SkillbookFlows.Sum(flow => flow.SkillCount):N0} skill(s)";
    public string SkillbookSelectedSummary => SelectedSkillbookSkill is null
        ? "Select a skill to view or edit it."
        : SelectedSkillbookSkill.Title;

    public ICommand AddSkillbookFlowCommand { get; private set; } = null!;
    public ICommand AddSkillbookSectionCommand { get; private set; } = null!;
    public ICommand AddSkillbookSkillCommand { get; private set; } = null!;
    public ICommand ToggleSkillbookInfoCommand { get; private set; } = null!;
    public ICommand ToggleSkillbookBuiltInEditCommand { get; private set; } = null!;
    public ICommand SaveSkillbookFlowCommand { get; private set; } = null!;
    public ICommand SaveSkillbookSectionCommand { get; private set; } = null!;
    public ICommand SaveSkillbookSkillCommand { get; private set; } = null!;
    public ICommand RevertSkillbookSkillCommand { get; private set; } = null!;
    public ICommand ReloadSkillbookCommand { get; private set; } = null!;

    private void InitializeSkillbookCommands()
    {
        AddSkillbookFlowCommand = new RelayCommand<object>(_ => AddSkillbookFlow());
        AddSkillbookSectionCommand = new RelayCommand<object>(_ => AddSkillbookSection(), _ => CanEditSelectedSkillbookFlow);
        AddSkillbookSkillCommand = new RelayCommand<object>(_ => AddSkillbookSkill(), _ => CanEditSelectedSkillbookSection);
        ToggleSkillbookInfoCommand = new RelayCommand<object>(_ => IsSkillbookInfoVisible = !IsSkillbookInfoVisible);
        ToggleSkillbookBuiltInEditCommand = new RelayCommand<object>(_ => ToggleSkillbookBuiltInEdit(), _ => SelectedSkillbookSkill?.IsBuiltIn == true);
        SaveSkillbookFlowCommand = new RelayCommand<object>(_ => SaveSkillbookFlow(), _ => CanEditSelectedSkillbookFlow && !string.IsNullOrWhiteSpace(SkillbookFlowTitleText));
        SaveSkillbookSectionCommand = new RelayCommand<object>(_ => SaveSkillbookSection(), _ => CanRenameSelectedSkillbookSection && !string.IsNullOrWhiteSpace(SkillbookSectionTitleText));
        SaveSkillbookSkillCommand = new RelayCommand<object>(_ => SaveSkillbookSkill(), _ => CanEditSelectedSkillbookSkill && !string.IsNullOrWhiteSpace(SkillbookSkillTitleText));
        RevertSkillbookSkillCommand = new RelayCommand<object>(_ => LoadSelectedSkill(SelectedSkillbookSkill), _ => SelectedSkillbookSkill is not null);
        ReloadSkillbookCommand = new RelayCommand<object>(_ => ReloadSkillbookDocument());
    }

    private void LoadSkillbookDocument(string flowId = "", string sectionId = "", string skillId = "")
    {
        var document = _skillbookService.LoadDocument();
        SkillbookEntries.Clear();
        foreach (var entry in _skillbookService.LoadEntries().Select(entry => new SkillbookEntryViewModel(entry)))
        {
            SkillbookEntries.Add(entry);
        }

        SkillbookFlows.Clear();
        foreach (var flow in document.Flows.Select(flow => new SkillbookFlowViewModel(flow)))
        {
            SkillbookFlows.Add(flow);
        }

        PromptFlowSteps.Clear();
        foreach (var step in document.PromptFlowSteps.Select(step => new PromptFlowStepViewModel(step)))
        {
            PromptFlowSteps.Add(step);
        }

        SelectedSkillbookFlow = SelectById(SkillbookFlows, flowId, flow => flow.Id) ?? SkillbookFlows.FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(sectionId))
        {
            SelectedSkillbookSection = SelectById(SkillbookSections, sectionId, section => section.Id) ?? SkillbookSections.FirstOrDefault();
        }

        if (!string.IsNullOrWhiteSpace(skillId))
        {
            SelectedSkillbookSkill = SelectById(SkillbookSkills, skillId, skill => skill.Id) ?? SkillbookSkills.FirstOrDefault();
        }

        RefreshSkillbookProperties();
        RefreshPromptFlowSteps();
    }

    private void ReloadSkillbookDocument()
    {
        var flowId = SelectedSkillbookFlow?.Id ?? "";
        var sectionId = SelectedSkillbookSection?.Id ?? "";
        var skillId = SelectedSkillbookSkill?.Id ?? "";
        LoadSkillbookDocument(flowId, sectionId, skillId);
        SkillbookEditorStatus = "Skillbook reloaded.";
    }

    private void AddSkillbookFlow()
    {
        var flow = _skillbookService.CreateProjectFlow("New Flow");
        LoadSkillbookDocument(flow.Id);
        SkillbookEditorStatus = "Created a project flow. Right-click it to rename.";
    }

    private void AddSkillbookSection()
    {
        if (SelectedSkillbookFlow is null)
        {
            return;
        }

        var section = _skillbookService.CreateProjectSection(SelectedSkillbookFlow.Model, "New Section");
        LoadSkillbookDocument(SelectedSkillbookFlow.Id, section.Id);
        SkillbookEditorStatus = "Created a section. Right-click it to rename.";
    }

    private void AddSkillbookSkill()
    {
        if (SelectedSkillbookFlow is null || SelectedSkillbookSection is null)
        {
            return;
        }

        var skill = _skillbookService.CreateProjectSkill(
            SelectedSkillbookFlow.Model,
            SelectedSkillbookSection.Model,
            "New Skill");
        LoadSkillbookDocument(SelectedSkillbookFlow.Id, SelectedSkillbookSection.Id, skill.Id);
        SkillbookEditorStatus = "Created a markdown skill. Edit its body here; right-click it to rename.";
    }

    public bool RenameSkillbookFlow(SkillbookFlowViewModel flow, string title)
    {
        if (!flow.IsEditable || string.IsNullOrWhiteSpace(title))
        {
            SkillbookEditorStatus = "This flow cannot be renamed.";
            return false;
        }

        var sectionId = SelectedSkillbookSection?.Id ?? "";
        var skillId = SelectedSkillbookSkill?.Id ?? "";
        _skillbookService.SaveFlow(flow.Model, title);
        LoadSkillbookDocument(flow.Id, sectionId, skillId);
        SkillbookEditorStatus = "Flow renamed.";
        return true;
    }

    public bool RenameSkillbookSection(SkillbookSectionViewModel section, string title)
    {
        if (!section.IsEditable
            || string.Equals(section.Id, "legacy", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(title))
        {
            SkillbookEditorStatus = "This section cannot be renamed.";
            return false;
        }

        var skillId = SelectedSkillbookSkill?.Id ?? "";
        _skillbookService.SaveSection(section.Model, title);
        LoadSkillbookDocument(section.Flow.Id, section.Id, skillId);
        SkillbookEditorStatus = "Section renamed.";
        return true;
    }

    public bool RenameSkillbookSkill(SkillbookSkillViewModel skill, string title)
    {
        if ((!skill.IsEditable && !skill.IsBuiltIn) || string.IsNullOrWhiteSpace(title))
        {
            SkillbookEditorStatus = "This skill cannot be renamed.";
            return false;
        }

        var text = ReferenceEquals(SelectedSkillbookSkill, skill) ? SkillbookEditorText : skill.Text;
        var enabled = ReferenceEquals(SelectedSkillbookSkill, skill) ? SkillbookEditorEnabled : skill.Enabled;
        SaveSkillbookSkillModel(skill.Model, title, text, enabled);
        LoadSkillbookDocument(skill.Flow.Id, skill.Section.Id, skill.Id);
        SkillbookEditorStatus = "Skill renamed.";
        OnPropertyChanged(nameof(PromptTokenomicsLabel));
        OnPropertyChanged(nameof(PromptContextPressureLabel));
        return true;
    }

    private void SaveSkillbookFlow()
    {
        if (SelectedSkillbookFlow is null)
        {
            return;
        }

        _skillbookService.SaveFlow(SelectedSkillbookFlow.Model, SkillbookFlowTitleText);
        LoadSkillbookDocument(SelectedSkillbookFlow.Id, SelectedSkillbookSection?.Id ?? "", SelectedSkillbookSkill?.Id ?? "");
        SkillbookEditorStatus = "Flow saved.";
    }

    private void SaveSkillbookSection()
    {
        if (SelectedSkillbookSection is null)
        {
            return;
        }

        _skillbookService.SaveSection(SelectedSkillbookSection.Model, SkillbookSectionTitleText);
        LoadSkillbookDocument(SelectedSkillbookFlow?.Id ?? "", SelectedSkillbookSection.Id, SelectedSkillbookSkill?.Id ?? "");
        SkillbookEditorStatus = "Section saved.";
    }

    private void SaveSkillbookSkill()
    {
        if (SelectedSkillbookSkill is null)
        {
            return;
        }

        SaveSkillbookSkillModel(
            SelectedSkillbookSkill.Model,
            SkillbookSkillTitleText,
            SkillbookEditorText,
            SkillbookEditorEnabled);
        LoadSkillbookDocument(SelectedSkillbookFlow?.Id ?? "", SelectedSkillbookSection?.Id ?? "", SelectedSkillbookSkill.Id);
        SkillbookEditorStatus = "Skill saved.";
        OnPropertyChanged(nameof(PromptTokenomicsLabel));
        OnPropertyChanged(nameof(PromptContextPressureLabel));
    }

    private void SaveSkillbookSkillModel(SkillbookSkill skill, string title, string text, bool enabled)
    {
        if (skill.IsBuiltIn)
        {
            _skillbookService.SaveBuiltInSkillOverride(skill, title, text, enabled);
            return;
        }

        _skillbookService.SaveSkill(skill, title, text, enabled);
    }

    private void RefreshSkillbookSections(SkillbookFlowViewModel? flow)
    {
        SkillbookSections.Clear();
        SkillbookSkills.Clear();
        SelectedSkillbookSection = null;
        SelectedSkillbookSkill = null;
        if (flow is null)
        {
            return;
        }

        foreach (var section in flow.Model.Sections.Select(section => new SkillbookSectionViewModel(flow, section)))
        {
            SkillbookSections.Add(section);
        }

        SelectedSkillbookSection = SkillbookSections.FirstOrDefault();
        RefreshSkillbookProperties();
    }

    private void RefreshSkillbookSkills(SkillbookSectionViewModel? section)
    {
        SkillbookSkills.Clear();
        SelectedSkillbookSkill = null;
        if (section is null)
        {
            return;
        }

        foreach (var skill in section.Model.Skills.Select(skill => new SkillbookSkillViewModel(section.Flow, section, skill)))
        {
            SkillbookSkills.Add(skill);
        }

        SelectedSkillbookSkill = SkillbookSkills.FirstOrDefault();
        RefreshSkillbookProperties();
    }

    private void LoadSelectedSkill(SkillbookSkillViewModel? skill)
    {
        _isSkillbookBuiltInEditMode = false;
        _isLoadingSkillbookSelection = true;
        try
        {
            SkillbookSkillTitleText = skill?.Title ?? "";
            SkillbookEditorText = skill?.Text ?? "";
            SkillbookEditorEnabled = skill?.Enabled ?? true;
        }
        finally
        {
            _isLoadingSkillbookSelection = false;
        }

        RefreshSkillbookProperties();
    }

    private void ToggleSkillbookBuiltInEdit()
    {
        if (SelectedSkillbookSkill?.IsBuiltIn != true)
        {
            return;
        }

        _isSkillbookBuiltInEditMode = !_isSkillbookBuiltInEditMode;
        RefreshSkillbookProperties();
    }

    private void RefreshSkillbookProperties()
    {
        OnPropertyChanged(nameof(CanEditSelectedSkillbookFlow));
        OnPropertyChanged(nameof(CanEditSelectedSkillbookSection));
        OnPropertyChanged(nameof(CanRenameSelectedSkillbookSection));
        OnPropertyChanged(nameof(CanEditSelectedSkillbookSkill));
        OnPropertyChanged(nameof(IsSkillbookFlowTitleReadOnly));
        OnPropertyChanged(nameof(IsSkillbookSectionTitleReadOnly));
        OnPropertyChanged(nameof(IsSkillbookEditorReadOnly));
        OnPropertyChanged(nameof(ShowSkillbookBuiltInEditButton));
        OnPropertyChanged(nameof(SkillbookBuiltInEditButtonLabel));
        OnPropertyChanged(nameof(SkillbookEditorModeLabel));
        OnPropertyChanged(nameof(SkillbookFlowCountSummary));
        OnPropertyChanged(nameof(SkillbookSelectedSummary));
        OnPropertyChanged(nameof(SkillbookSummary));
        OnPropertyChanged(nameof(SkillbookCodexSummary));
        OnPropertyChanged(nameof(SkillbookCcFlowSummary));
        OnPropertyChanged(nameof(SkillbookSkillflowSummary));
        OnPropertyChanged(nameof(SkillbookUserSummary));
        RaiseSkillbookCommandStates();
    }

    private void RefreshPromptFlowSteps()
    {
        if (PromptFlowSteps.Count == 0)
        {
            return;
        }

        var active = PromptFlowStepResolver.Resolve(
            ResolveCapsulePhase(PromptText),
            PhaseTitle,
            PhaseDetail,
            PromptModeKey,
            IsAutopilotEnabled,
            IsPatchPlanReady);
        foreach (var step in PromptFlowSteps)
        {
            step.ApplyCurrent(active);
        }
    }

    private void RaiseSkillbookCommandStates()
    {
        (AddSkillbookSectionCommand as RelayCommand<object>)?.RaiseCanExecuteChanged();
        (AddSkillbookSkillCommand as RelayCommand<object>)?.RaiseCanExecuteChanged();
        (SaveSkillbookFlowCommand as RelayCommand<object>)?.RaiseCanExecuteChanged();
        (SaveSkillbookSectionCommand as RelayCommand<object>)?.RaiseCanExecuteChanged();
        (SaveSkillbookSkillCommand as RelayCommand<object>)?.RaiseCanExecuteChanged();
        (RevertSkillbookSkillCommand as RelayCommand<object>)?.RaiseCanExecuteChanged();
        (ToggleSkillbookBuiltInEditCommand as RelayCommand<object>)?.RaiseCanExecuteChanged();
    }

    private static T? SelectById<T>(IEnumerable<T> items, string id, Func<T, string> selector)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return default;
        }

        return items.FirstOrDefault(item =>
            string.Equals(selector(item), id, StringComparison.OrdinalIgnoreCase));
    }
}
