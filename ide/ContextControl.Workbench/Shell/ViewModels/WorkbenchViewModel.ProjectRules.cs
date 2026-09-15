// CC-DESC: Owns project settings, file rule editors, and scanner automation.

using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Windows.Input;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Media;
using ContextControl.Workbench.Services;

namespace ContextControl.Workbench.ViewModels;

public sealed partial class WorkbenchViewModel
{
    private void LoadProjectSettings()
    {
        ProjectSettingsPath = Path.Combine(_workbenchSettings.ContextControlRoot, ".ccReplace.settings.json");

        var settings = ReadProjectSettingsObject();
        ProjectSettingsProjectRootText = GetProjectSetting(settings, "ProjectRoot", "auto");
        ProjectSettingsOutputRootText = GetProjectSetting(settings, "OutputRoot", ".");
        ProjectSettingsVersionCacheRootText = GetProjectSetting(settings, "VersionCacheRoot", ".ccReplace.versions");
        ProjectSettingsStatus = File.Exists(ProjectSettingsPath)
            ? $"Loaded {ProjectSettingsPath}"
            : "Using default project settings; save to create the file.";
    }

    private void SaveProjectSettings()
    {
        ProjectSettingsPath = Path.Combine(_workbenchSettings.ContextControlRoot, ".ccReplace.settings.json");
        var settings = ReadProjectSettingsObject();
        settings["ProjectRoot"] = NormalizeProjectSettingText(ProjectSettingsProjectRootText, "auto");
        settings["OutputRoot"] = NormalizeProjectSettingText(ProjectSettingsOutputRootText, ".");
        settings["VersionCacheRoot"] = NormalizeProjectSettingText(ProjectSettingsVersionCacheRootText, ".ccReplace.versions");

        var parent = Path.GetDirectoryName(ProjectSettingsPath);
        if (!string.IsNullOrWhiteSpace(parent))
        {
            Directory.CreateDirectory(parent);
        }

        File.WriteAllText(
            ProjectSettingsPath,
            settings.ToJsonString(ProjectSettingsJsonOptions) + Environment.NewLine);
        ProjectSettingsStatus = $"Saved {ProjectSettingsPath}";
    }

    private void UseActiveProjectRootForSettings()
    {
        if (CurrentProject is null || string.IsNullOrWhiteSpace(CurrentProject.ProjectRoot))
        {
            ProjectSettingsStatus = "Open a project before using the active project root.";
            return;
        }

        ProjectSettingsProjectRootText = CurrentProject.ProjectRoot;
        ProjectSettingsStatus = "ProjectRoot set to active project. Save to persist.";
    }

    private void UseContextControlProjectRootForSettings()
    {
        ProjectSettingsProjectRootText = ".";
        ProjectSettingsStatus = "ProjectRoot set to the Context Control tool folder. Save to persist.";
    }

    private JsonObject ReadProjectSettingsObject()
    {
        try
        {
            if (File.Exists(ProjectSettingsPath))
            {
                return JsonNode.Parse(File.ReadAllText(ProjectSettingsPath)) as JsonObject ?? [];
            }
        }
        catch
        {
            ProjectSettingsStatus = "Could not read project settings; editing defaults.";
        }

        return [];
    }

    private static string GetProjectSetting(JsonObject settings, string key, string fallback)
    {
        return settings.TryGetPropertyValue(key, out var node) && node is not null
            ? (node.GetValue<string>() ?? fallback)
            : fallback;
    }

    private static string NormalizeProjectSettingText(string value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private async Task SaveFileRulesAsync()
    {
        if (CurrentProject is null || !_workspaceByProjectId.TryGetValue(CurrentProject.Id, out var workspace))
        {
            FileRulesStatus = "Open a project before saving file rules.";
            return;
        }

        var rules = workspace.FileRules;
        SyncFileRuleTextsFromEntries();
        rules.UpdateRules(IgnoredDirectoriesText, IgnoredFileNamesText, IgnoredFileTypesText, SupportedFileTypesText, LocFileTypesText);
        rules.Save();
        var status = $"Saved file rules to {rules.RulesPath}";
        ApplyFileRulesToEditor(rules, status);
        ReloadTrackerRules(CurrentProject);

        await RefreshCurrentProjectFromDiskAsync();
        FileRulesStatus = status;
    }

    private async Task ResetFileRulesAsync()
    {
        if (CurrentProject is null || !_workspaceByProjectId.TryGetValue(CurrentProject.Id, out var workspace))
        {
            FileRulesStatus = "Open a project before resetting file rules.";
            return;
        }

        var rules = workspace.FileRules;
        rules.ResetToDefaults();
        rules.Save();
        var status = $"Reset file rules to defaults at {rules.RulesPath}";
        ApplyFileRulesToEditor(rules, status);
        ReloadTrackerRules(CurrentProject);

        await RefreshCurrentProjectFromDiskAsync();
        FileRulesStatus = status;
    }

    private async Task ScanProjectRulesAsync()
    {
        if (IsProjectScanRunning)
        {
            return;
        }

        var project = CurrentProject;
        if (project is null || !_workspaceByProjectId.TryGetValue(project.Id, out var workspace))
        {
            ProjectScanSummary = "No project open.";
            ProjectScanResultText = "Open a project before scanning.";
            FileRulesStatus = "Open a project before scanning.";
            return;
        }

        BeginProjectScan();
        ApplyProjectScanBusy(workspace);
        FileRulesStatus = "Scanning project rules...";

        try
        {
            var result = await ScanProjectRulesCoreAsync(project, workspace);
            workspace.ProjectScanResult = result;
            if (CurrentProject?.Id != project.Id)
            {
                return;
            }

            ApplyProjectScanResult(result, workspace.ProjectScanAutoSetupStatus);
            FileRulesStatus = "Project scan complete.";
        }
        catch (OperationCanceledException)
        {
            if (CurrentProject?.Id == project.Id) ApplyProjectScanResult(workspace.ProjectScanResult, "Scan cancelled; saved rules unchanged.");
        }
        catch (Exception ex)
        {
            workspace.ProjectScanResult = null;
            workspace.ProjectScanAutoSetupStatus = "";
            if (CurrentProject?.Id == project.Id)
            {
                ApplyProjectScanError("Scan failed.", ex.Message);
                FileRulesStatus = "Project scan failed.";
            }
        }
        finally
        {
            EndProjectScan();
        }
    }

    public async Task EnsureProjectScanDetailsAsync()
    {
        if (HasProjectScanDetailsForExport())
        {
            return;
        }

        while (IsProjectScanRunning)
        {
            await Task.Delay(100);
            if (HasProjectScanDetailsForExport())
            {
                return;
            }
        }

        await ScanProjectRulesAsync();
    }

    private bool HasProjectScanDetailsForExport()
    {
        if (ProjectScanMetrics.Count == 0 && ProjectScanSections.Count == 0)
        {
            return false;
        }

        return !ProjectScanSummary.Equals("No scan yet.", StringComparison.OrdinalIgnoreCase)
            && !ProjectScanSummary.Equals("Scanning...", StringComparison.OrdinalIgnoreCase)
            && !ProjectScanSummary.Equals("Scan failed.", StringComparison.OrdinalIgnoreCase)
            && !ProjectScanSummary.Equals("No project open.", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<ProjectStackScanResult> ScanProjectRulesCoreAsync(ProjectTabViewModel project, ProjectWorkspaceState workspace)
    {
        SyncFileRuleTextsFromEntries();
        var rules = workspace.FileRules.CreateSnapshot(
            IgnoredDirectoriesText,
            IgnoredFileNamesText,
            IgnoredFileTypesText,
            SupportedFileTypesText,
            LocFileTypesText);

        return await ProjectStackScanner.ScanAsync(project.ProjectRoot, rules, _projectScanCancellation?.Token ?? default,
            new Progress<ProjectScanProgress>(progress =>
            {
                if (CurrentProject?.Id == project.Id && IsProjectScanRunning)
                    ProjectScanProgressText = $"{progress.Files:N0} files · {progress.Directories:N0} folders · {progress.Path}";
            }));
    }

    private async Task AutoSetupProjectRulesAsync()
    {
        if (IsProjectScanRunning)
        {
            return;
        }

        var project = CurrentProject;
        if (project is null || !_workspaceByProjectId.TryGetValue(project.Id, out var workspace))
        {
            ProjectScanAutoSetupStatus = "Open a project before autosetup.";
            return;
        }

        BeginProjectScan();
        ApplyProjectScanBusy(workspace);
        FileRulesStatus = "Scanning project rules for autosetup...";

        try
        {
            var result = await ScanProjectRulesCoreAsync(project, workspace);
            if (!result.IsComplete)
            {
                workspace.ProjectScanResult = result;
                if (CurrentProject?.Id == project.Id) ApplyProjectScanResult(result, "Some locations could not be read.");
                throw new IOException("Autosetup needs a complete scan. Check the unreadable paths in the inventory notices.");
            }
            var currentRules = workspace.FileRules;
            var updatedRules = currentRules.CreateSnapshot(currentRules.IgnoredDirectoriesText, currentRules.IgnoredFileNamesText,
                currentRules.IgnoredExtensionsText, currentRules.SupportedExtensionsText, currentRules.LocExtensionsText);
            ApplyAutoSetupRules(updatedRules, result.AutoSetupRules);
            _projectScanCancellation?.Token.ThrowIfCancellationRequested();
            _projectScanCancellation?.Dispose();
            _projectScanCancellation = null;
            OnPropertyChanged(nameof(CanCancelProjectScan));
            ProjectScanProgressText = "Saving project rules and refreshing the inventory…";
            updatedRules.Save();
            workspace.FileRules = updatedRules;
            workspace.ProjectScanAutoSetupStatus = $"Autosetup saved to {updatedRules.RulesPath}";
            // Report the rules actually saved, with no fire-and-forget rescan race.
            workspace.ProjectScanResult = await ProjectStackScanner.ScanAsync(project.ProjectRoot, updatedRules);
            ReloadTrackerRules(project);
            if (CurrentProject?.Id == project.Id)
            {
                ApplyFileRulesToEditor(updatedRules, workspace.ProjectScanAutoSetupStatus);
                ApplyProjectScanResult(workspace.ProjectScanResult, workspace.ProjectScanAutoSetupStatus);
            }
            await RefreshCurrentProjectFromDiskAsync(project);
        }
        catch (OperationCanceledException)
        {
            if (CurrentProject?.Id == project.Id) ApplyProjectScanResult(workspace.ProjectScanResult, "Autosetup cancelled; saved rules unchanged.");
        }
        catch (Exception ex)
        {
            workspace.ProjectScanAutoSetupStatus = "Autosetup failed: " + ex.Message;
            if (CurrentProject?.Id == project.Id)
            {
                ProjectScanAutoSetupStatus = workspace.ProjectScanAutoSetupStatus;
                ProjectScanSummary = "Autosetup failed.";
                ProjectScanResultText = ex.Message;
                FileRulesStatus = "Autosetup failed.";
            }
        }
        finally
        {
            EndProjectScan();
        }
    }

    private static void ApplyAutoSetupRules(ProjectFileRules rules, ProjectStackRuleSet ruleSet)
    {
        rules.ApplyCleanRules(
            ruleSet.IgnoredDirectories,
            ruleSet.IgnoredFileNames,
            ruleSet.IgnoredExtensions,
            ruleSet.SupportedExtensions,
            ruleSet.LocExtensions);
        foreach (var path in ruleSet.ShownFiles) rules.ShowFile(path);
    }

    private void ApplyProjectScanBusy(ProjectWorkspaceState workspace)
    {
        ProjectScanSummary = "Scanning...";
        ProjectScanResultText = "";
        ProjectScanRuleSummary = "";
        ClearProjectScanCollections();
        ProjectScanAutoSetupStatus = workspace.ProjectScanAutoSetupStatus;
    }

    private void ApplyProjectScanError(string summary, string details)
    {
        ProjectScanSummary = summary;
        ProjectScanResultText = details;
        ProjectScanRuleSummary = "";
        ClearProjectScanCollections();
        ProjectScanAutoSetupStatus = "";
    }

    private void ApplyProjectScanResult(ProjectStackScanResult? result, string autoSetupStatus)
    {
        ClearProjectScanCollections();
        ProjectScanAutoSetupStatus = autoSetupStatus;

        if (result is null)
        {
            ProjectScanSummary = "No scan yet.";
            ProjectScanResultText = "Run Scan to inspect this project.";
            ProjectScanRuleSummary = "";
            return;
        }

        ProjectScanSummary = result.Summary;
        ProjectScanResultText = result.DetailsText;
        ProjectScanRuleSummary = result.RuleSummary;
        SetProjectScanInventory(result);
        foreach (var metric in result.Metrics)
        {
            ProjectScanMetrics.Add(metric);
        }

        var sections = result.Sections.Where(section => section.Items.Count > 0).ToArray();
        foreach (var section in sections)
        {
            ProjectScanSections.Add(section);
        }

        AddScanSections(ProjectScanIdentitySections, sections, "Detected Stack", "Uses");
        AddScanSections(ProjectScanFileSections, sections, "Languages", "Top File Types", "Manifests");
        AddScanSections(ProjectScanRuleSections, sections, "Unsupported Visible Types", "Autosetup Plan", "Already Allowed", "Already Counted LOC");
        AddScanSections(ProjectScanDiagnosticSections, sections, "Skipped File Types", "Skipped Samples");
    }

    private void ClearProjectScanCollections()
    {
        SetProjectScanInventory(null);
        ProjectScanMetrics.Clear();
        ProjectScanSections.Clear();
        ProjectScanIdentitySections.Clear();
        ProjectScanFileSections.Clear();
        ProjectScanRuleSections.Clear();
        ProjectScanDiagnosticSections.Clear();
    }

    private static void AddScanSections(
        ICollection<ProjectStackSection> target,
        IReadOnlyCollection<ProjectStackSection> sections,
        params string[] titles)
    {
        foreach (var title in titles)
        {
            var section = sections.FirstOrDefault(item => string.Equals(item.Title, title, StringComparison.OrdinalIgnoreCase));
            if (section is not null)
            {
                target.Add(section);
            }
        }
    }

    private void ApplyFileRulesToEditor(ProjectFileRules rules, string status)
    {
        SupportedFileTypesLabel = rules.SupportedLabel;
        IgnoredFileTypesLabel = rules.IgnoredLabel;
        FileRulesPath = rules.RulesPath;
        ContextControl.ActiveProjectRulesPath = rules.RulesPath;
        SupportedFileTypesText = rules.SupportedExtensionsText;
        IgnoredFileTypesText = rules.IgnoredExtensionsText;
        LocFileTypesText = rules.LocExtensionsText;
        IgnoredFileNamesText = rules.IgnoredFileNamesText;
        IgnoredDirectoriesText = rules.IgnoredDirectoriesText;
        ReplaceRuleEntries(IgnoredDirectoryRules, RuleKindIgnoredDirectories, rules.IgnoredDirectories);
        ReplaceRuleEntries(IgnoredFileNameRules, RuleKindIgnoredFileNames, rules.IgnoredFileNames);
        ReplaceRuleEntries(IgnoredFileTypeRules, RuleKindIgnoredFileTypes, rules.IgnoredExtensions);
        ReplaceRuleEntries(SupportedFileTypeRules, RuleKindSupportedFileTypes, rules.SupportedExtensions);
        ReplaceRuleEntries(LocFileTypeRules, RuleKindLocFileTypes, rules.LocExtensions);
        FileRulesStatus = status;
        RefreshFileRuleSummary();
    }

    public IReadOnlyList<string> GetFileRuleEntries(string kind)
    {
        return GetRuleCollection(kind)
            .Select(entry => entry.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();
    }

    public void ReplaceFileRuleEntries(string kind, IEnumerable<string> values)
    {
        ReplaceRuleEntries(GetRuleCollection(kind), kind, NormalizeRuleEntries(values));
        SyncFileRuleTextsFromEntries();
        FileRulesStatus = "File rules changed. Save to apply.";
        RefreshFileRuleSummary();
    }

    public string GetFileRuleTitle(string kind)
    {
        return kind switch
        {
            RuleKindIgnoredDirectories => "Skipped folders",
            RuleKindIgnoredFileNames => "Skipped files",
            RuleKindIgnoredFileTypes => "Skipped file types",
            RuleKindSupportedFileTypes => "Allowed file types",
            RuleKindLocFileTypes => "LOC file types",
            _ => "File rules"
        };
    }

    public string GetFileRuleWatermark(string kind)
    {
        return kind switch
        {
            RuleKindIgnoredDirectories => "bin, obj, node_modules",
            RuleKindIgnoredFileNames => ".DS_Store, CMakeCache.txt",
            RuleKindIgnoredFileTypes => ".dll, .png, .tmp",
            RuleKindSupportedFileTypes => ".cs, .cpp, .ps1",
            RuleKindLocFileTypes => ".cs, .cpp, .md",
            _ => "new entry"
        };
    }

    private void AddFileRuleEntry(string? kind)
    {
        if (string.IsNullOrWhiteSpace(kind))
        {
            return;
        }

        var text = GetNewRuleText(kind);
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        AddRuleEntries(GetRuleCollection(kind), kind, SplitRuleText(text));
        SetNewRuleText(kind, "");
        SyncFileRuleTextsFromEntries();
        FileRulesStatus = "File rules changed. Save to apply.";
        RefreshFileRuleSummary();
    }

    private void RemoveFileRuleEntry(FileRuleEntryViewModel? entry)
    {
        if (entry is null)
        {
            return;
        }

        GetRuleCollection(entry.Kind).Remove(entry);
        SyncFileRuleTextsFromEntries();
        FileRulesStatus = "File rules changed. Save to apply.";
        RefreshFileRuleSummary();
    }

    private ObservableCollection<FileRuleEntryViewModel> GetRuleCollection(string kind)
    {
        return kind switch
        {
            RuleKindIgnoredDirectories => IgnoredDirectoryRules,
            RuleKindIgnoredFileNames => IgnoredFileNameRules,
            RuleKindIgnoredFileTypes => IgnoredFileTypeRules,
            RuleKindSupportedFileTypes => SupportedFileTypeRules,
            RuleKindLocFileTypes => LocFileTypeRules,
            _ => IgnoredDirectoryRules
        };
    }

    private string GetNewRuleText(string kind)
    {
        return kind switch
        {
            RuleKindIgnoredDirectories => NewIgnoredDirectoryRuleText,
            RuleKindIgnoredFileNames => NewIgnoredFileNameRuleText,
            RuleKindIgnoredFileTypes => NewIgnoredFileTypeRuleText,
            RuleKindSupportedFileTypes => NewSupportedFileTypeRuleText,
            RuleKindLocFileTypes => NewLocFileTypeRuleText,
            _ => ""
        };
    }

    private void SetNewRuleText(string kind, string value)
    {
        switch (kind)
        {
            case RuleKindIgnoredDirectories:
                NewIgnoredDirectoryRuleText = value;
                break;
            case RuleKindIgnoredFileNames:
                NewIgnoredFileNameRuleText = value;
                break;
            case RuleKindIgnoredFileTypes:
                NewIgnoredFileTypeRuleText = value;
                break;
            case RuleKindSupportedFileTypes:
                NewSupportedFileTypeRuleText = value;
                break;
            case RuleKindLocFileTypes:
                NewLocFileTypeRuleText = value;
                break;
        }
    }

    private static void ReplaceRuleEntries(
        ObservableCollection<FileRuleEntryViewModel> target,
        string kind,
        IEnumerable<string> values)
    {
        target.Clear();
        AddRuleEntries(target, kind, values);
    }

    private static void AddRuleEntries(
        ObservableCollection<FileRuleEntryViewModel> target,
        string kind,
        IEnumerable<string> values)
    {
        var existing = target
            .Select(entry => entry.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var value in NormalizeRuleEntries(values))
        {
            if (existing.Add(value))
            {
                target.Add(new FileRuleEntryViewModel(kind, value));
            }
        }
    }

    private void SyncFileRuleTextsFromEntries()
    {
        SupportedFileTypesText = JoinRuleEntries(SupportedFileTypeRules);
        IgnoredFileTypesText = JoinRuleEntries(IgnoredFileTypeRules);
        LocFileTypesText = JoinRuleEntries(LocFileTypeRules);
        IgnoredFileNamesText = JoinRuleEntries(IgnoredFileNameRules);
        IgnoredDirectoriesText = JoinRuleEntries(IgnoredDirectoryRules);
    }

    private void RefreshFileRuleSummary()
    {
        OnPropertyChanged(nameof(FileRulesSummary));
    }

    private static string JoinRuleEntries(IEnumerable<FileRuleEntryViewModel> entries)
    {
        return string.Join(Environment.NewLine, NormalizeRuleEntries(entries.Select(entry => entry.Value)));
    }

    private static IEnumerable<string> NormalizeRuleEntries(IEnumerable<string> values)
    {
        return values
            .SelectMany(SplitRuleText)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> SplitRuleText(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        return value
            .Split(['\r', '\n', ',', ';', '\t', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(item => !string.IsNullOrWhiteSpace(item));
    }

    private void ReloadTrackerRules(ProjectTabViewModel project)
    {
        if (_trackersByProjectId.TryGetValue(project.Id, out var tracker))
        {
            tracker.ReloadRules();
        }
    }
}
