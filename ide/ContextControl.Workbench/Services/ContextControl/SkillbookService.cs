// CC-DESC: Loads editable Skillbook flows and ContextControl instruction entries.

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace ContextControl.Workbench.Services;

public sealed record SkillbookEntry(
    string Key,
    string Title,
    string Text,
    string Source,
    bool Enabled,
    string FlowId = "",
    string FlowTitle = "",
    string SectionId = "",
    string SectionTitle = "",
    string FilePath = "",
    bool IsBuiltIn = false,
    bool IsEditable = false);

public sealed record SkillbookDocument(
    IReadOnlyList<SkillbookFlow> Flows,
    IReadOnlyList<PromptFlowStep> PromptFlowSteps);

public sealed record SkillbookFlow(
    string Id,
    string Title,
    string Summary,
    string Source,
    string RootPath,
    bool Enabled,
    bool IsBuiltIn,
    bool IsEditable,
    IReadOnlyList<SkillbookSection> Sections);

public sealed record SkillbookSection(
    string Id,
    string Title,
    string Summary,
    string Source,
    string RootPath,
    bool Enabled,
    bool IsBuiltIn,
    bool IsEditable,
    IReadOnlyList<SkillbookSkill> Skills);

public sealed record SkillbookSkill(
    string Id,
    string Title,
    string Text,
    string Source,
    string FlowId,
    string FlowTitle,
    string SectionId,
    string SectionTitle,
    string FilePath,
    bool Enabled,
    bool IsBuiltIn,
    bool IsEditable);

public sealed record PromptFlowStep(
    string Key,
    string Title,
    string Trigger,
    string PromptSource,
    string Recipient,
    string Attachments,
    string SkillbookInjection,
    bool SendsModelPrompt);

public sealed record SkillbookCodexFlowSettings(
    string Model,
    string ReasoningEffort);

public sealed class SkillbookService
{
    private const string FlowsFolderName = "flows";
    private const string BuiltInOverridesFolderName = "built-in-overrides";
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

    public SkillbookService(string contextControlRoot, string? globalRoot = null)
    {
        ContextControlRoot = contextControlRoot;
        GlobalRoot = string.IsNullOrWhiteSpace(globalRoot)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ContextControl",
                "skillbook")
            : Path.GetFullPath(globalRoot);
        ProjectRoot = Path.Combine(contextControlRoot, "skillbook");
        GlobalFlowsRoot = Path.Combine(GlobalRoot, FlowsFolderName);
        ProjectFlowsRoot = Path.Combine(ProjectRoot, FlowsFolderName);
        BuiltInOverridesRoot = Path.Combine(ProjectRoot, BuiltInOverridesFolderName);
    }

    public string ContextControlRoot { get; }
    public string GlobalRoot { get; }
    public string ProjectRoot { get; }
    public string GlobalFlowsRoot { get; }
    public string ProjectFlowsRoot { get; }
    public string BuiltInOverridesRoot { get; }

    public SkillbookDocument LoadDocument()
    {
        EnsureSeedFiles();

        var flows = new List<SkillbookFlow>
        {
            BuildBuiltInContextControlFlow()
        };

        var projectLegacy = BuildLegacyFlow(ProjectRoot, "project", "Project Skillbook", "Project-local markdown instructions.");
        if (projectLegacy is not null)
        {
            flows.Add(projectLegacy);
        }

        var globalLegacy = BuildLegacyFlow(GlobalRoot, "global", "Global Skillbook", "Reusable markdown instructions from the user profile.");
        if (globalLegacy is not null)
        {
            flows.Add(globalLegacy);
        }

        flows.AddRange(ReadFlowDirectories(ProjectFlowsRoot, "project"));
        flows.AddRange(ReadFlowDirectories(GlobalFlowsRoot, "global"));

        return new SkillbookDocument(flows, BuildPromptFlowSteps());
    }

    public IReadOnlyList<SkillbookEntry> LoadEntries()
    {
        var document = LoadDocument();
        return document.Flows
            .SelectMany(flow => flow.Sections)
            .SelectMany(section => section.Skills)
            .Select(skill => new SkillbookEntry(
                skill.Id,
                skill.Title,
                skill.Text,
                NormalizeSource(skill.Source),
                skill.Enabled,
                skill.FlowId,
                skill.FlowTitle,
                skill.SectionId,
                skill.SectionTitle,
                skill.FilePath,
                skill.IsBuiltIn,
                skill.IsEditable))
            .OrderBy(entry => SourceRank(entry.Source))
            .ThenBy(entry => entry.FlowTitle, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.SectionTitle, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public string BuildEnabledInstructionText()
    {
        var entries = LoadEntries()
            .Where(entry => entry.Enabled
                && !IsBuiltInCodexEntry(entry)
                && !string.IsNullOrWhiteSpace(entry.Text))
            .ToArray();

        if (entries.Length == 0)
        {
            return "";
        }

        var byKey = new Dictionary<string, SkillbookEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries.OrderBy(entry => SourceRank(entry.Source)))
        {
            byKey[entry.Key] = entry;
        }

        var builder = new StringBuilder();
        foreach (var entry in byKey.Values
            .OrderBy(entry => SourceRank(entry.Source))
            .ThenBy(entry => entry.FlowTitle, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.SectionTitle, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Title, StringComparer.OrdinalIgnoreCase))
        {
            builder.AppendLine($"# {entry.Title}");
            builder.AppendLine(entry.Text.Trim());
            builder.AppendLine();
        }

        return builder.ToString().TrimEnd();
    }

    public string BuildCodexInstructionText(ContextCapsulePhase phase)
    {
        var skills = LoadBuiltInContextControlSkills();
        var builder = new StringBuilder();
        var codexSkills = skills
            .Where(skill => skill.Source.Equals("codex", StringComparison.OrdinalIgnoreCase) && skill.Enabled)
            .ToArray();
        if (codexSkills.Length > 0)
        {
            builder.AppendLine("Visible Codex harness instructions:");
            foreach (var entry in codexSkills)
            {
                builder.AppendLine();
                builder.AppendLine($"## {entry.Title}");
                builder.AppendLine(entry.Text.Trim());
            }

            builder.AppendLine();
        }

        AppendBuiltInSkillBlock(builder, "CC Main", skills, CodexInstructionCatalog.CcMainKey, CodexInstructionCatalog.BuildCcMain());
        builder.AppendLine();
        builder.AppendLine("## Active phase contract");
        builder.AppendLine(CodexInstructionCatalog.BuildPhaseContract(phase));
        builder.AppendLine();
        AppendBuiltInSkillBlock(
            builder,
            "CC Flow phase",
            skills,
            GetCcFlowPhaseKey(phase),
            CodexInstructionCatalog.BuildCcFlowPhase(phase));

        return builder.ToString().TrimEnd();
    }

    public string BuildFlowInstructionText(ContextCapsulePhase phase)
    {
        var skills = LoadBuiltInContextControlSkills();
        var builder = new StringBuilder();
        AppendBuiltInSkillBlock(builder, "CC Main", skills, CodexInstructionCatalog.CcMainKey, CodexInstructionCatalog.BuildCcMain());
        builder.AppendLine();
        AppendBuiltInSkillBlock(
            builder,
            "CC Flow phase",
            skills,
            GetCcFlowPhaseKey(phase),
            CodexInstructionCatalog.BuildCcFlowPhase(phase));

        return builder.ToString().TrimEnd();
    }

    private SkillbookSkill[] LoadBuiltInContextControlSkills()
    {
        return LoadDocument()
            .Flows
            .FirstOrDefault(flow => flow.Id.Equals("context-control", StringComparison.OrdinalIgnoreCase))
            ?.Sections
            .SelectMany(section => section.Skills)
            .ToArray()
            ?? [];
    }

    private static void AppendBuiltInSkillBlock(
        StringBuilder builder,
        string heading,
        IReadOnlyList<SkillbookSkill> skills,
        string key,
        string fallbackText)
    {
        var skill = skills.FirstOrDefault(skill => skill.Id.Equals(key, StringComparison.OrdinalIgnoreCase));
        var text = skill is null
            ? fallbackText
            : skill.Enabled
                ? skill.Text
                : "";
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        builder.AppendLine($"## {heading}");
        builder.AppendLine(text.Trim());
    }

    public SkillbookCodexFlowSettings ResolveCodexFlowSettings(ContextCapsulePhase phase)
    {
        var phaseKey = phase == ContextCapsulePhase.FileRequest ? "dir" : "cc";
        var model = "";
        var effort = "";
        foreach (var skill in LoadDocument().Flows.SelectMany(flow => flow.Sections).SelectMany(section => section.Skills))
        {
            if (!skill.Enabled || string.IsNullOrWhiteSpace(skill.Text))
            {
                continue;
            }

            foreach (var rawLine in skill.Text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n'))
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }

                var separator = line.IndexOf(':', StringComparison.Ordinal);
                if (separator <= 0)
                {
                    continue;
                }

                var key = NormalizeModelDirectiveKey(line[..separator]);
                var value = line[(separator + 1)..].Trim();
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                if (key.Equals($"codex-{phaseKey}-model", StringComparison.Ordinal)
                    || phaseKey.Equals("dir", StringComparison.Ordinal) && key.Equals("codex-request-model", StringComparison.Ordinal)
                    || key.Equals("codex-model", StringComparison.Ordinal))
                {
                    model = value;
                }
                else if (key.Equals($"codex-{phaseKey}-effort", StringComparison.Ordinal)
                    || phaseKey.Equals("dir", StringComparison.Ordinal) && key.Equals("codex-request-effort", StringComparison.Ordinal)
                    || key.Equals("codex-effort", StringComparison.Ordinal))
                {
                    effort = value;
                }
            }
        }

        return new SkillbookCodexFlowSettings(model, effort);
    }

    public SkillbookFlow CreateProjectFlow(string title)
    {
        EnsureSeedFiles();
        Directory.CreateDirectory(ProjectFlowsRoot);

        var cleanTitle = CleanTitle(title, "New Flow");
        var id = CreateUniqueId(ProjectFlowsRoot, cleanTitle);
        var flowRoot = Path.Combine(ProjectFlowsRoot, id);
        var sectionRoot = Path.Combine(flowRoot, "sections", "general");
        Directory.CreateDirectory(sectionRoot);
        Directory.CreateDirectory(Path.Combine(sectionRoot, "skills"));

        WriteJson(Path.Combine(flowRoot, "flow.json"), new FlowMetadata
        {
            Id = id,
            Title = cleanTitle,
            Summary = "Project flow",
            Enabled = true,
            Order = 0
        });
        WriteJson(Path.Combine(sectionRoot, "section.json"), new SectionMetadata
        {
            Id = "general",
            Title = "General",
            Summary = "General project skills",
            Enabled = true,
            Order = 0
        });

        return ReadFlowDirectory(flowRoot, "project") ?? BuildEmptyEditableFlow(id, cleanTitle, "project", flowRoot);
    }

    public SkillbookSection CreateProjectSection(SkillbookFlow flow, string title)
    {
        if (!flow.IsEditable || string.IsNullOrWhiteSpace(flow.RootPath))
        {
            throw new InvalidOperationException("Only editable project/global flows can add sections.");
        }

        var sectionsRoot = Path.Combine(flow.RootPath, "sections");
        Directory.CreateDirectory(sectionsRoot);
        var cleanTitle = CleanTitle(title, "New Section");
        var id = CreateUniqueId(sectionsRoot, cleanTitle);
        var sectionRoot = Path.Combine(sectionsRoot, id);
        Directory.CreateDirectory(Path.Combine(sectionRoot, "skills"));
        WriteJson(Path.Combine(sectionRoot, "section.json"), new SectionMetadata
        {
            Id = id,
            Title = cleanTitle,
            Summary = "Skill section",
            Enabled = true,
            Order = 0
        });

        return ReadSectionDirectory(sectionRoot, flow, flow.Source)
            ?? new SkillbookSection(id, cleanTitle, "Skill section", flow.Source, sectionRoot, true, false, true, []);
    }

    public SkillbookSkill CreateProjectSkill(SkillbookFlow flow, SkillbookSection section, string title)
    {
        if (!section.IsEditable || string.IsNullOrWhiteSpace(section.RootPath))
        {
            throw new InvalidOperationException("Only editable project/global sections can add skills.");
        }

        var skillsRoot = section.Id.Equals("legacy", StringComparison.OrdinalIgnoreCase)
            ? section.RootPath
            : Path.Combine(section.RootPath, "skills");
        Directory.CreateDirectory(skillsRoot);
        var cleanTitle = CleanTitle(title, "New Skill");
        var id = CreateUniqueMarkdownId(skillsRoot, cleanTitle);
        var path = Path.Combine(skillsRoot, $"{id}.md");
        WriteMarkdownSkill(path, cleanTitle, true, "# New Skill\n\nDescribe the reusable instruction here.");

        return ReadSkillFile(path, flow, section, NormalizeSource(section.Source))
            ?? new SkillbookSkill(id, cleanTitle, "# New Skill", section.Source, flow.Id, flow.Title, section.Id, section.Title, path, true, false, true);
    }

    public void SaveFlow(SkillbookFlow flow, string title)
    {
        if (!flow.IsEditable || string.IsNullOrWhiteSpace(flow.RootPath))
        {
            throw new InvalidOperationException("Only editable flows can be renamed.");
        }

        var path = Path.Combine(flow.RootPath, "flow.json");
        var metadata = ReadJson<FlowMetadata>(path) ?? new FlowMetadata { Id = flow.Id };
        metadata.Id = string.IsNullOrWhiteSpace(metadata.Id) ? flow.Id : metadata.Id;
        metadata.Title = CleanTitle(title, flow.Title);
        metadata.Summary = string.IsNullOrWhiteSpace(metadata.Summary) ? flow.Summary : metadata.Summary;
        metadata.Enabled = flow.Enabled;
        WriteJson(path, metadata);
    }

    public void SaveSection(SkillbookSection section, string title)
    {
        if (!section.IsEditable || string.IsNullOrWhiteSpace(section.RootPath))
        {
            throw new InvalidOperationException("Only editable sections can be renamed.");
        }

        var path = Path.Combine(section.RootPath, "section.json");
        var metadata = ReadJson<SectionMetadata>(path) ?? new SectionMetadata { Id = section.Id };
        metadata.Id = string.IsNullOrWhiteSpace(metadata.Id) ? section.Id : metadata.Id;
        metadata.Title = CleanTitle(title, section.Title);
        metadata.Summary = string.IsNullOrWhiteSpace(metadata.Summary) ? section.Summary : metadata.Summary;
        metadata.Enabled = section.Enabled;
        WriteJson(path, metadata);
    }

    public void SaveSkill(SkillbookSkill skill, string title, string text, bool enabled)
    {
        if (!skill.IsEditable || string.IsNullOrWhiteSpace(skill.FilePath))
        {
            throw new InvalidOperationException("Only editable skills can be saved.");
        }

        WriteMarkdownSkill(skill.FilePath, CleanTitle(title, skill.Title), enabled, text ?? "");
    }

    public void SaveBuiltInSkillOverride(SkillbookSkill skill, string title, string text, bool enabled)
    {
        if (!skill.IsBuiltIn)
        {
            throw new InvalidOperationException("Only built-in skills can be saved as overrides.");
        }

        var path = GetBuiltInOverridePath(skill.SectionId, skill.Id);
        WriteMarkdownSkill(path, CleanTitle(title, skill.Title), enabled, text ?? "");
    }

    public static IReadOnlyList<PromptFlowStep> BuildPromptFlowSteps()
    {
        return
        [
            new("request-dir", "DIR + Request", "Run DIR with a concrete request, then Send", "User request + ccDir.ps1 project map", "LLM", "cc_project_dir.md", "CC Main + DIR + Request contract", true),
            new("cc", "CC", "Run CC from the model file/function list, then optionally Send", "cc.ps1 source export; prompt starts empty", "LLM", "cc_code_export.md", "CC Main + CC Export + Patch contract", true),
            new("go-apply", "GO / Apply", "Preview CC-REPLACE blocks, then apply selected actions", "patch.txt + ccReplace plan", "Local", "patch.txt / plan JSON", "No model prompt", false)
        ];
    }

    private SkillbookFlow BuildBuiltInContextControlFlow()
    {
        var builtIns = CodexInstructionCatalog.SkillbookEntries;
        var mainSkills = builtIns
            .Where(entry => IsCcMainSource(entry.Source))
            .Select(entry => ToBuiltInSkill(entry, "context-control", "Context Control", "cc-main", "CC Main"))
            .ToArray();
        var codexSkills = builtIns
            .Where(entry => entry.Source.Equals("codex", StringComparison.OrdinalIgnoreCase))
            .Select(entry => ToBuiltInSkill(entry, "context-control", "Context Control", "codex", "Codex Instructions"))
            .ToArray();
        var ccFlowSkills = builtIns
            .Where(entry => IsCcFlowSource(entry.Source))
            .Select(entry => ToBuiltInSkill(entry, "context-control", "Context Control", "cc-flow", "CC Flow"))
            .ToArray();

        var sections = new List<SkillbookSection>();
        if (codexSkills.Length > 0)
        {
            sections.Add(new SkillbookSection("codex", "Codex Instructions", "Read-only Codex harness instructions.", "codex", "", true, true, false, codexSkills));
        }

        sections.Add(new SkillbookSection("cc-main", "CC Main", "Read-only Context Control operating law.", "cc-main", "", true, true, false, mainSkills));
        sections.Add(new SkillbookSection("cc-flow", "CC Flow", "Read-only Context Control phase map.", "cc-flow", "", true, true, false, ccFlowSkills));

        return new SkillbookFlow(
            "context-control",
            "Context Control",
            "Built-in CC Main, CC Flow, and phase contracts.",
            "cc-flow",
            "",
            true,
            true,
            false,
            sections);
    }

    private SkillbookSkill ToBuiltInSkill(
        SkillbookEntry entry,
        string flowId,
        string flowTitle,
        string sectionId,
        string sectionTitle)
    {
        var skill = new SkillbookSkill(
            entry.Key,
            entry.Title,
            entry.Text,
            NormalizeSource(entry.Source),
            flowId,
            flowTitle,
            sectionId,
            sectionTitle,
            "",
            entry.Enabled,
            true,
            false);
        return ApplyBuiltInOverride(skill);
    }

    private SkillbookSkill ApplyBuiltInOverride(SkillbookSkill skill)
    {
        var path = GetBuiltInOverridePath(skill.SectionId, skill.Id);
        if (!File.Exists(path))
        {
            return skill;
        }

        try
        {
            var parsed = ParseMarkdownSkill(File.ReadAllText(path), skill.Id);
            return skill with
            {
                Title = parsed.Title,
                Text = parsed.Body,
                Enabled = parsed.Enabled,
                FilePath = path
            };
        }
        catch
        {
            return skill;
        }
    }

    private string GetBuiltInOverridePath(string sectionId, string skillId)
    {
        var section = Slugify(string.IsNullOrWhiteSpace(sectionId) ? "built-in" : sectionId);
        var skill = Slugify(string.IsNullOrWhiteSpace(skillId) ? "skill" : skillId);
        return Path.Combine(BuiltInOverridesRoot, section, $"{skill}.md");
    }

    private static SkillbookFlow? BuildLegacyFlow(string root, string source, string title, string summary)
    {
        if (!Directory.Exists(root))
        {
            return null;
        }

        var flow = new SkillbookFlow(
            source,
            title,
            summary,
            source,
            root,
            true,
            false,
            false,
            []);
        var section = new SkillbookSection(
            "legacy",
            title,
            "Flat markdown files kept for compatibility.",
            source,
            root,
            true,
            false,
            true,
            []);
        var skills = Directory.EnumerateFiles(root, "*.md", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path => ReadLegacySkill(path, flow, section, source))
            .Where(skill => skill is not null)
            .Cast<SkillbookSkill>()
            .ToArray();

        if (skills.Length == 0)
        {
            return null;
        }

        return flow with
        {
            Sections =
            [
                section with
                {
                    Skills = skills
                }
            ]
        };
    }

    private static SkillbookSkill? ReadLegacySkill(string path, SkillbookFlow flow, SkillbookSection section, string source)
    {
        try
        {
            var text = File.ReadAllText(path);
            var id = Path.GetFileNameWithoutExtension(path);
            return new SkillbookSkill(
                id,
                MakeTitle(id),
                text,
                source,
                flow.Id,
                flow.Title,
                section.Id,
                section.Title,
                path,
                true,
                false,
                true);
        }
        catch
        {
            return null;
        }
    }

    private static IEnumerable<SkillbookFlow> ReadFlowDirectories(string flowsRoot, string source)
    {
        if (!Directory.Exists(flowsRoot))
        {
            yield break;
        }

        foreach (var flowRoot in Directory.EnumerateDirectories(flowsRoot).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var flow = ReadFlowDirectory(flowRoot, source);
            if (flow is not null)
            {
                yield return flow;
            }
        }
    }

    private static SkillbookFlow? ReadFlowDirectory(string flowRoot, string source)
    {
        var fallbackId = Path.GetFileName(flowRoot);
        var metadata = ReadJson<FlowMetadata>(Path.Combine(flowRoot, "flow.json")) ?? new FlowMetadata();
        var id = string.IsNullOrWhiteSpace(metadata.Id) ? fallbackId : metadata.Id.Trim();
        var title = CleanTitle(metadata.Title, MakeTitle(id));
        var flow = new SkillbookFlow(
            id,
            title,
            string.IsNullOrWhiteSpace(metadata.Summary) ? $"{SourceLabel(source)} flow" : metadata.Summary.Trim(),
            source,
            flowRoot,
            metadata.Enabled ?? true,
            false,
            true,
            []);

        var sectionsRoot = Path.Combine(flowRoot, "sections");
        var sections = Directory.Exists(sectionsRoot)
            ? Directory.EnumerateDirectories(sectionsRoot)
                .Select(path => ReadSectionDirectory(path, flow, source))
                .Where(section => section is not null)
                .Cast<SkillbookSection>()
                .OrderBy(section => section.Title, StringComparer.OrdinalIgnoreCase)
                .ToArray()
            : [];

        return flow with { Sections = sections };
    }

    private static SkillbookSection? ReadSectionDirectory(string sectionRoot, SkillbookFlow flow, string source)
    {
        var fallbackId = Path.GetFileName(sectionRoot);
        var metadata = ReadJson<SectionMetadata>(Path.Combine(sectionRoot, "section.json")) ?? new SectionMetadata();
        var id = string.IsNullOrWhiteSpace(metadata.Id) ? fallbackId : metadata.Id.Trim();
        var title = CleanTitle(metadata.Title, MakeTitle(id));
        var section = new SkillbookSection(
            id,
            title,
            string.IsNullOrWhiteSpace(metadata.Summary) ? $"{SourceLabel(source)} section" : metadata.Summary.Trim(),
            source,
            sectionRoot,
            metadata.Enabled ?? true,
            false,
            true,
            []);

        var skillsRoot = Path.Combine(sectionRoot, "skills");
        var skills = Directory.Exists(skillsRoot)
            ? Directory.EnumerateFiles(skillsRoot, "*.md", SearchOption.TopDirectoryOnly)
                .Select(path => ReadSkillFile(path, flow, section, source))
                .Where(skill => skill is not null)
                .Cast<SkillbookSkill>()
                .OrderBy(skill => skill.Title, StringComparer.OrdinalIgnoreCase)
                .ToArray()
            : [];

        return section with { Skills = skills };
    }

    private static SkillbookSkill? ReadSkillFile(string path, SkillbookFlow flow, SkillbookSection section, string source)
    {
        try
        {
            var raw = File.ReadAllText(path);
            var parsed = ParseMarkdownSkill(raw, Path.GetFileNameWithoutExtension(path));
            return new SkillbookSkill(
                Path.GetFileNameWithoutExtension(path),
                parsed.Title,
                parsed.Body,
                source,
                flow.Id,
                flow.Title,
                section.Id,
                section.Title,
                path,
                parsed.Enabled,
                false,
                true);
        }
        catch
        {
            return null;
        }
    }

    private void EnsureSeedFiles()
    {
        Directory.CreateDirectory(GlobalRoot);
        Directory.CreateDirectory(ProjectRoot);
        Directory.CreateDirectory(GlobalFlowsRoot);
        Directory.CreateDirectory(ProjectFlowsRoot);
        Directory.CreateDirectory(BuiltInOverridesRoot);
    }

    private static (string Title, bool Enabled, string Body) ParseMarkdownSkill(string raw, string fallbackKey)
    {
        var body = raw ?? "";
        var title = MakeTitle(fallbackKey);
        var enabled = true;
        if (!body.StartsWith("---", StringComparison.Ordinal))
        {
            return (title, enabled, body);
        }

        var normalized = body.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var end = normalized.IndexOf("\n---\n", 4, StringComparison.Ordinal);
        if (end < 0)
        {
            return (title, enabled, body);
        }

        var frontMatter = normalized[4..end];
        foreach (var line in frontMatter.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = line.IndexOf(':', StringComparison.Ordinal);
            if (separator <= 0)
            {
                continue;
            }

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim().Trim('"');
            if (key.Equals("title", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(value))
            {
                title = value;
            }
            else if (key.Equals("enabled", StringComparison.OrdinalIgnoreCase) && bool.TryParse(value, out var parsedEnabled))
            {
                enabled = parsedEnabled;
            }
        }

        return (title, enabled, normalized[(end + "\n---\n".Length)..].TrimStart('\n'));
    }

    private static void WriteMarkdownSkill(string path, string title, bool enabled, string text)
    {
        var parent = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(parent))
        {
            Directory.CreateDirectory(parent);
        }

        var builder = new StringBuilder();
        builder.AppendLine("---");
        builder.Append("title: ");
        builder.AppendLine(JsonEncodedText.Encode(CleanTitle(title, "Skill")).ToString());
        builder.Append("enabled: ");
        builder.AppendLine(enabled ? "true" : "false");
        builder.AppendLine("---");
        builder.AppendLine((text ?? "").TrimEnd());
        File.WriteAllText(path, builder.ToString(), Utf8NoBom);
    }

    private static SkillbookFlow BuildEmptyEditableFlow(string id, string title, string source, string rootPath)
    {
        return new SkillbookFlow(id, title, "", source, rootPath, true, false, true, []);
    }

    private static T? ReadJson<T>(string path)
    {
        if (!File.Exists(path))
        {
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions);
        }
        catch
        {
            return default;
        }
    }

    private static void WriteJson<T>(string path, T value)
    {
        var parent = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(parent))
        {
            Directory.CreateDirectory(parent);
        }

        File.WriteAllText(path, JsonSerializer.Serialize(value, JsonOptions) + Environment.NewLine, Utf8NoBom);
    }

    private static string CreateUniqueId(string parent, string title)
    {
        var baseId = Slugify(title);
        var id = baseId;
        var index = 2;
        while (Directory.Exists(Path.Combine(parent, id)))
        {
            id = $"{baseId}-{index}";
            index++;
        }

        return id;
    }

    private static string CreateUniqueMarkdownId(string parent, string title)
    {
        var baseId = Slugify(title);
        var id = baseId;
        var index = 2;
        while (File.Exists(Path.Combine(parent, $"{id}.md")))
        {
            id = $"{baseId}-{index}";
            index++;
        }

        return id;
    }

    private static string Slugify(string value)
    {
        var clean = Regex.Replace((value ?? "").Trim().ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-');
        return string.IsNullOrWhiteSpace(clean) ? "item" : clean;
    }

    private static string CleanTitle(string? value, string fallback)
    {
        var clean = (value ?? "").Replace('\r', ' ').Replace('\n', ' ').Trim();
        return string.IsNullOrWhiteSpace(clean) ? fallback : clean;
    }

    private static string MakeTitle(string key)
    {
        var clean = (key ?? "")
            .Replace('-', ' ')
            .Replace('_', ' ')
            .Replace('/', ' ')
            .Replace('\\', ' ')
            .Trim();
        return string.IsNullOrWhiteSpace(clean) ? "Instruction" : clean;
    }

    private static string NormalizeModelDirectiveKey(string key)
    {
        return Regex.Replace((key ?? "").Trim().ToLowerInvariant(), @"[\s_]+", "-");
    }

    private static string GetCcFlowPhaseKey(ContextCapsulePhase phase)
    {
        return CodexInstructionCatalog.GetCcFlowPhaseKey(phase);
    }

    public static string NormalizeSource(string source)
    {
        return source.Equals("skillflow", StringComparison.OrdinalIgnoreCase)
            ? "cc-flow"
            : source;
    }

    public static bool IsCcFlowSource(string source)
    {
        return source.Equals("cc-flow", StringComparison.OrdinalIgnoreCase)
            || source.Equals("skillflow", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsCcMainSource(string source)
    {
        return source.Equals("cc-main", StringComparison.OrdinalIgnoreCase);
    }

    public static int SourceRank(string source)
    {
        return NormalizeSource(source).ToLowerInvariant() switch
        {
            "cc-main" => 0,
            "cc-flow" => 1,
            "codex" => 2,
            "project" => 3,
            "global" => 4,
            _ => 5
        };
    }

    public static string SourceLabel(string source)
    {
        return NormalizeSource(source).ToLowerInvariant() switch
        {
            "project" => "Project",
            "codex" => "Codex",
            "cc-main" => "CC Main",
            "cc-flow" => "CC Flow",
            "global" => "Global",
            _ => "Skillflow"
        };
    }

    private static bool IsBuiltInCodexEntry(SkillbookEntry entry)
    {
        return entry.Source.Equals("codex", StringComparison.OrdinalIgnoreCase)
            || IsCcMainSource(entry.Source)
            || IsCcFlowSource(entry.Source);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private sealed class FlowMetadata
    {
        public string? Id { get; set; }
        public string? Title { get; set; }
        public string? Summary { get; set; }
        public bool? Enabled { get; set; }
        public int? Order { get; set; }
    }

    private sealed class SectionMetadata
    {
        public string? Id { get; set; }
        public string? Title { get; set; }
        public string? Summary { get; set; }
        public bool? Enabled { get; set; }
        public int? Order { get; set; }
    }
}
