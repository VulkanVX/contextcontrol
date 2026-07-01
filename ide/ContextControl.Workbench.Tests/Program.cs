// CC-DESC: Runs focused smoke checks for deterministic ContextControl file resolution.

using System.Diagnostics;
using System.Reflection;
using System.Text.Json.Nodes;
using Avalonia;
using ContextControl.Workbench;
using ContextControl.Workbench.Controls;
using ContextControl.Workbench.Services;
using ContextControl.Workbench.ViewModels;
using static SmokeTestHelpers;

if (args.Any(arg => arg.Equals("--chat-renderer-regression", StringComparison.OrdinalIgnoreCase)))
{
    RunChatRendererRegression();
    Console.WriteLine("Chat renderer regression tests passed.");
    return;
}

if (args.Any(arg => arg.Equals("--chat-renderer-perf", StringComparison.OrdinalIgnoreCase)))
{
    RunChatRendererPerfSanity();
    return;
}

var root = Path.Combine(Path.GetTempPath(), "ContextControlResolverSmoke", Guid.NewGuid().ToString("N"));
try
{
    WriteFile(root, 
        "Views/MainWindow.axaml",
        """
        <Window>
          <Button Classes="cc-prompt-send" Content="{Binding ContextControl.PromptSendButtonLabel}" Command="{Binding ContextControl.SendCommand}" />
          <TextBox Classes="cc-prompt-input" Watermark="Prompt" />
        </Window>
        """);
    WriteFile(root, 
        "Styles/WorkbenchDesign.axaml",
        """
        <Styles>
          <Style Selector="Button.cc-prompt-send">
            <Setter Property="Background" Value="#18222E" />
          </Style>
        </Styles>
        """);
    WriteFile(root, 
        "Services/LocalLlmService.cs",
        """
        namespace Smoke;
        public sealed class LocalLlmService
        {
            public string OllamaInstallProgressLabel => "Ollama installer download progress";
        }
        """);
    WriteFile(root, 
        "ViewModels/ContextControlViewModel.cs",
        """
        namespace Smoke;
        public sealed class ContextControlViewModel
        {
            public object SendCommand { get; } = new();
            public string PromptSendButtonLabel => IsCodexPromptMode ? "Send to Codex" : "Send";
            public bool IsCodexPromptMode { get; set; }
        }
        """);
    WriteFile(root,
        "Views/WorkspacePages/SkillbookPage.axaml",
        """
        <UserControl>
          <TextBlock Text="SkillbookFlow" />
        </UserControl>
        """);

    var rules = ProjectFileRules.Load(root);
    if (rules.ShouldShowFile(".ccWorkbench.chat-history.2026.json", ".ccWorkbench.chat-history.2026.json", ".json")
        || rules.ShouldShowFile("cc_chat_export_20260602.md", "cc_chat_export_20260602.md", ".md")
        || rules.ShouldShowFile("notes-bug-hunt.md", "notes-bug-hunt.md", ".md"))
    {
        throw new InvalidOperationException("Project file rules should apply wildcard ignored file names.");
    }

    if (!rules.ShouldShowFile("Services/LocalLlmService.cs", "LocalLlmService.cs", ".cs"))
    {
        throw new InvalidOperationException("Wildcard ignored file names must not hide ordinary source files.");
    }

    RunProjectScannerShaderAutosetupSmoke();

    var builder = new ContextSemanticMapBuilder();
    var index = (await builder.BuildIndexAsync(root, "", rules)).Index;
    var resolver = new ContextFileResolverService();

    var sendButton = resolver.Resolve("Change the button send in prompt window to red", index);
    RequireContains(sendButton, "Views/MainWindow.axaml");
    RequireContains(sendButton, "Styles/WorkbenchDesign.axaml");
    RequireNotContains(sendButton, "Services/LocalLlmService.cs");

    var ollama = resolver.Resolve("fix ollama install progress stuck", index);
    RequireContains(ollama, "Services/LocalLlmService.cs");

    var typo = resolver.Resolve("MainWindoiw.axaml.cs", index);
    RequireNotContains(typo, "MainWindoiw.axaml.cs");

    var unknown = resolver.Resolve("make bananas sparkle", index);
    if (!unknown.UsesFindTerms)
    {
        throw new InvalidOperationException("Low-confidence request should fall back to FIND terms.");
    }

    var manifestText = """
        CC-DIR-MANIFEST-V2
        LOD: L0_GLOBAL

        ROOTS:
        ROOT path="Views/" role="view files" files=2

        FILES:
        FILE path="Views/MainWindow.axaml" tier=L0 kind="avalonia-xaml" role="main workbench UI" exports="full,find"
        FILE path="ViewModels/Workbench/WorkbenchViewModel.cs" tier=L0 kind="csharp" role="workbench state" exports="full,function,find"
        FILE path="ViewModels/Workbench/WorkbenchViewModel.Shell.cs" tier=L0 kind="csharp" role="workbench shell state" exports="full,function,find"

        FAMILIES:
        FAMILY path="ViewModels/Workbench/WorkbenchViewModel*.cs" role="split workbench viewmodel" exports="wildcard-function,find"
        """;
    var manifest = ContextDirManifestParser.Parse(manifestText);
    if (!manifest.IsV2
        || manifest.Files.Count != 3
        || !manifest.Files.First(file => file.Path.Equals("Views/MainWindow.axaml", StringComparison.OrdinalIgnoreCase)).Exports.Equals("full,find", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("CC-DIR-MANIFEST-V2 parser should read FILE records and per-file exports.");
    }

    var legacyManifest = ContextDirManifestParser.Parse(
        """
        project/
        ├── Views/
        │   └── MainWindow.axaml
        └── Services/
            └── LocalLlmService.cs
        """);
    if (legacyManifest.IsV2
        || !legacyManifest.Files.Any(file => file.Path.Equals("Views/MainWindow.axaml", StringComparison.OrdinalIgnoreCase))
        || !legacyManifest.Files.Any(file => file.Path.Equals("Services/LocalLlmService.cs", StringComparison.OrdinalIgnoreCase)))
    {
        throw new InvalidOperationException("Legacy visual DIR trees should remain parseable as fallback only.");
    }

    var promptBuilder = new ContextPromptBuilder();
    RequirePhase1Valid(promptBuilder, manifest, "Views/MainWindow.axaml\nFUNC: SendCommand\nEND", "source");
    RequirePhase1Valid(promptBuilder, manifest, "FUNCTION: SendCommand\nEND", "source");
    RequirePhase1Valid(promptBuilder, manifest, "FUNCTION ViewModels/Workbench/WorkbenchViewModel.cs :: SaveAsync\nEND", "source");
    RequirePhase1Valid(promptBuilder, manifest, "FUNCTION ViewModels/Workbench/WorkbenchViewModel*.cs :: SaveAsync\nEND", "source");
    RequirePhase1Valid(promptBuilder, manifest, "FIND: SkillbookFlow\nEND", "find");
    RequirePhase1Invalid(root, promptBuilder, manifest, "FIND: SkillbookFlow\nFIND: SendCommand\nEND", "exactly one");
    RequirePhase1Valid(promptBuilder, manifest, "EXPAND: Views/\nEND", "expand");
    RequirePhase1Invalid(root, promptBuilder, manifest, "FUNCTION ViewModels/Workbench/Missing*.cs :: SaveAsync\nEND", "visible FAMILY");
    RequirePhase1Invalid(root, promptBuilder, manifest, "EXPAND: Missing/\nEND", "not visible");
    RequirePhase1Valid(root, promptBuilder, manifest, "Views/WorkspacePages/SkillbookPage.axaml\nEND", "source");
    RequirePhase1Invalid(root, promptBuilder, manifest, "Views/WorkspacePages/MissingPage.axaml\nEND", "not visible");
    RequirePhase1Invalid(root, promptBuilder, manifest, "Services/LocalLlmService.cs\nEND", "not visible");
    var trustedFindResult = ContextPhase1RequestValidator.Validate(
        promptBuilder.ParsePhase1RequestLines("Services/LocalLlmService.cs\nEND"),
        manifest,
        root,
        ["Services/LocalLlmService.cs"]);
    if (!trustedFindResult.IsValid || !trustedFindResult.Kind.Equals("source", StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException($"FIND-discovered file paths should be valid for the next source export. Error: {trustedFindResult.Error}");
    }

    var extractFindMatches = typeof(ContextControlViewModel).GetMethod(
        "ExtractMatchedFileRequestsFromFindExport",
        BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("Expected FIND matched-file extraction helper to exist.");
    var extractedFindMatches = (IReadOnlyList<string>)(extractFindMatches.Invoke(
        null,
        [
            """
            ## FIND: cc-prompt-send

            Matched code files:
            - Views/MainWindow.axaml
            - Views/PromptBar.axaml

            ## FIND: SendCommand

            Matched code files:
            - Views/PromptBar.axaml
            - ViewModels/PromptViewModel.cs

            """
        ]) ?? Array.Empty<string>());
    if (!extractedFindMatches.SequenceEqual(
        [
            "Views/MainWindow.axaml",
            "Views/PromptBar.axaml",
            "ViewModels/PromptViewModel.cs"
        ],
        StringComparer.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException("Multiple FIND sections should produce one deduplicated matched-file request list.");
    }

    WriteFile(root,
        "Repair/ViewModels/ContextControlViewModel.cs",
        """
        namespace Smoke;
        public sealed partial class ContextControlViewModel
        {
            public void ShellOnly() { }
            public string PromptLabelName => nameof(PromptSendButtonLabel);
            public object SendCommand => new Func<Task>(SendAsync);
        }
        """);
    WriteFile(root,
        "Repair/ViewModels/ContextControl/ContextControlViewModel.ShellPromptProperties.cs",
        """
        namespace Smoke;
        public sealed partial class ContextControlViewModel
        {
            public string PromptSendButtonLabel => "Send";
        }
        """);
    WriteFile(root,
        "Repair/ViewModels/ContextControl/ContextControlViewModel.Workflow.Messaging.cs",
        """
        namespace Smoke;
        public sealed partial class ContextControlViewModel
        {
            private async Task SendAsync()
            {
                await Task.CompletedTask;
            }
        }
        """);
    var repairFunctionOwners = typeof(ContextControlViewModel).GetMethod(
        "RepairFunctionOwnerRequestLines",
        BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("Expected mixed CC function-owner repair helper to exist.");
    var staleFunctionOwnerParse = promptBuilder.ParsePhase1RequestLines(
        """
        FUNCTION Repair/ViewModels/ContextControlViewModel.cs :: PromptSendButtonLabel
        FUNCTION Repair/ViewModels/ContextControlViewModel.cs :: SendAsync
        END
        """);
    object?[] repairArgs = [staleFunctionOwnerParse, root, null];
    var repairedFunctionOwnerParse = (ContextRequestLineParseResult)(repairFunctionOwners.Invoke(null, repairArgs)
        ?? throw new InvalidOperationException("Function-owner repair returned null."));
    if (!repairedFunctionOwnerParse.RequestLines.SequenceEqual(
        [
            "FUNCTION Repair/ViewModels/ContextControl/ContextControlViewModel.ShellPromptProperties.cs :: PromptSendButtonLabel",
            "FUNCTION Repair/ViewModels/ContextControl/ContextControlViewModel.Workflow.Messaging.cs :: SendAsync"
        ],
        StringComparer.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException("Stale partial-class FUNCTION owners should repair to the files that declare the requested symbols.");
    }

    RequirePhase1Invalid(root, promptBuilder, manifest, "C:\\repo\\Views\\MainWindow.axaml\nEND", "non-request");
    RequirePhase1Invalid(root, promptBuilder, manifest, "SYMBOL: SendCommand\nEND", "non-request");
    RequirePhase1Invalid(root, promptBuilder, manifest, "FIND: SkillbookFlow\nViews/MainWindow.axaml\nEND", "do not mix FIND");
    RequirePhase1Invalid(root, promptBuilder, manifest, "EXPAND: Views/\nViews/MainWindow.axaml\nEND", "EXPAND must be exactly one line");
    RequirePhase1Invalid(root, promptBuilder, manifest, "Views/\nEND", "Directories are not source request lines");

    var codexDirAuditContext = new CodexPhaseAuditContext(manifest, root);
    var invalidExpandCodexAudit = CodexPhaseAuditor.Audit(
        ContextCapsulePhase.FileRequest,
        """
        EXPAND: .
        END
        """,
        promptBuilder,
        auditContext: codexDirAuditContext);
    if (invalidExpandCodexAudit.Passed
        || invalidExpandCodexAudit.Level != CodexPhaseAuditLevel.Error
        || !invalidExpandCodexAudit.Summary.Contains("not valid for the current DIR manifest", StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException("Codex DIR audit should reject EXPAND: . before it can be loaded for CC.");
    }

    var validExpandCodexAudit = CodexPhaseAuditor.Audit(
        ContextCapsulePhase.FileRequest,
        """
        EXPAND: Views/
        END
        """,
        promptBuilder,
        auditContext: codexDirAuditContext);
    if (!validExpandCodexAudit.Passed
        || validExpandCodexAudit.Level != CodexPhaseAuditLevel.Pass
        || validExpandCodexAudit.RequestLines.Count != 1
        || !validExpandCodexAudit.RequestLines[0].Equals("EXPAND: Views/", StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException("Codex DIR audit should pass valid EXPAND paths copied from visible ROOT records.");
    }

    var invalidMultiFindCodexAudit = CodexPhaseAuditor.Audit(
        ContextCapsulePhase.FileRequest,
        """
        FIND: cc-prompt-send
        FIND: SendCommand
        END
        """,
        promptBuilder,
        auditContext: codexDirAuditContext);
    if (invalidMultiFindCodexAudit.Passed
        || invalidMultiFindCodexAudit.Level != CodexPhaseAuditLevel.Error
        || !invalidMultiFindCodexAudit.Details.Any(detail => detail.Contains("FIND must be exactly one line", StringComparison.OrdinalIgnoreCase)))
    {
        throw new InvalidOperationException("Codex DIR audit should reject multiple FIND lines so discovery stays narrow.");
    }

    RunCcDirManifestScriptSmoke();
    RunCcExportCommandSmoke();
    await RunContextControlProcessServiceFallbackSmoke();

    var codexCatalogEntries = CodexInstructionCatalog.SkillbookEntries;
    if (codexCatalogEntries.Any(entry => entry.Source.Equals("codex", StringComparison.OrdinalIgnoreCase))
        || !codexCatalogEntries.Any(entry => entry.Source.Equals("cc-main", StringComparison.OrdinalIgnoreCase))
        || !codexCatalogEntries.Any(entry => entry.Source.Equals("cc-flow", StringComparison.OrdinalIgnoreCase)))
    {
        throw new InvalidOperationException("Skillbook should expose CC Main and CC Flow entries without Agentic Flow/Codex skill entries.");
    }

    var isolatedGlobalSkillbook = Path.Combine(root, ".test-global-skillbook");
    var skillbook = new SkillbookService(root, isolatedGlobalSkillbook);
    var skillbookEntries = skillbook.LoadEntries();
    if (skillbookEntries.Any(entry => entry.Key.Equals("codex-agentic-flow", StringComparison.OrdinalIgnoreCase))
        || !skillbookEntries.Any(entry => entry.Key.Equals("cc-main", StringComparison.OrdinalIgnoreCase))
        || !skillbookEntries.Any(entry => entry.Key.Equals("cc-flow-01-dir-request", StringComparison.OrdinalIgnoreCase))
        || !skillbookEntries.Any(entry => entry.Key.Equals("cc-flow-02-cc-patch", StringComparison.OrdinalIgnoreCase)))
    {
        throw new InvalidOperationException("Skillbook entries should include CC Main and CC Flow phases without Agentic Flow.");
    }

    var ccMainEntryView = new SkillbookEntryViewModel(skillbookEntries.First(entry => SkillbookService.IsCcMainSource(entry.Source)));
    var ccFlowEntryView = new SkillbookEntryViewModel(skillbookEntries.First(entry => SkillbookService.IsCcFlowSource(entry.Source)));
    if (!ccMainEntryView.SectionTitle.Equals("CC Main", StringComparison.Ordinal)
        || ccMainEntryView.SourceRank != 0
        || !ccFlowEntryView.SectionTitle.Equals("CC Flow", StringComparison.Ordinal)
        || ccFlowEntryView.SourceRank != 1)
    {
        throw new InvalidOperationException("Skillbook view models should expose CC Main and CC Flow entries without an Agentic Flow section.");
    }

    var legacyAliasEntry = new SkillbookEntry(
        "skillflow-legacy",
        "Legacy Skillflow",
        "legacy",
        "skillflow",
        true);
    var legacyAliasView = new SkillbookEntryViewModel(legacyAliasEntry);
    if (!legacyAliasView.Source.Equals("cc-flow", StringComparison.OrdinalIgnoreCase)
        || !legacyAliasView.SectionTitle.Equals("CC Flow", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("Legacy skillflow sources should display as CC Flow.");
    }

    var document = skillbook.LoadDocument();
    var contextControlFlow = document.Flows.FirstOrDefault(flow => flow.Id.Equals("context-control", StringComparison.OrdinalIgnoreCase));
    if (contextControlFlow is null
        || !contextControlFlow.Sections.Any(section => section.Id.Equals("cc-main", StringComparison.OrdinalIgnoreCase))
        || !contextControlFlow.Sections.Any(section => section.Id.Equals("cc-flow", StringComparison.OrdinalIgnoreCase))
        || !document.PromptFlowSteps.Any(step => step.Key.Equals("request-dir", StringComparison.OrdinalIgnoreCase))
        || document.PromptFlowSteps.Any(step => step.Key.Equals("request", StringComparison.OrdinalIgnoreCase))
        || document.PromptFlowSteps.Any(step => step.Key.Equals("dir", StringComparison.OrdinalIgnoreCase))
        || !document.PromptFlowSteps.Any(step => step.Key.Equals("go-apply", StringComparison.OrdinalIgnoreCase)))
    {
        throw new InvalidOperationException("Skillbook document should expose the built-in Context Control flow and a paired Request + DIR prompt flow map.");
    }

    var createdFlow = skillbook.CreateProjectFlow("Smoke Flow");
    var createdSection = skillbook.CreateProjectSection(createdFlow, "Smoke Section");
    var createdSkill = skillbook.CreateProjectSkill(createdFlow, createdSection, "Smoke Skill");
    skillbook.SaveSkill(createdSkill, "Saved Smoke Skill", "Use the saved smoke instruction.", enabled: false);
    var customFlowSkill = skillbook.CreateProjectSkill(createdFlow, createdSection, "Codex Customflow");
    skillbook.SaveSkill(
        customFlowSkill,
        "Codex Customflow",
        """
        codex-dir-model: gpt-5.5
        codex-dir-effort: high
        codex-cc-model: gpt-5.4-mini
        codex-cc-effort: xhigh
        """,
        enabled: true);
    var customDir = skillbook.ResolveCodexFlowSettings(ContextCapsulePhase.FileRequest);
    var customCc = skillbook.ResolveCodexFlowSettings(ContextCapsulePhase.PatchWrite);
    if (!customDir.Model.Equals("gpt-5.5", StringComparison.Ordinal)
        || !customDir.ReasoningEffort.Equals("high", StringComparison.Ordinal)
        || !customCc.Model.Equals("gpt-5.4-mini", StringComparison.Ordinal)
        || !customCc.ReasoningEffort.Equals("xhigh", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("Skillbook customflow directives should resolve Codex model and effort per CC phase.");
    }

    var builtInSkill = document.Flows
        .SelectMany(flow => flow.Sections)
        .SelectMany(section => section.Skills)
        .First(skill => skill.Id.Equals("cc-main", StringComparison.OrdinalIgnoreCase));
    skillbook.SaveBuiltInSkillOverride(builtInSkill, "CC Main edited", "Keep the edited built-in override terse.", enabled: true);
    var reloadedDocument = skillbook.LoadDocument();
    var savedSkill = reloadedDocument.Flows
        .SelectMany(flow => flow.Sections)
        .SelectMany(section => section.Skills)
        .FirstOrDefault(skill => skill.Title.Equals("Saved Smoke Skill", StringComparison.Ordinal));
    var editedBuiltIn = reloadedDocument.Flows
        .SelectMany(flow => flow.Sections)
        .SelectMany(section => section.Skills)
        .FirstOrDefault(skill => skill.Id.Equals("cc-main", StringComparison.OrdinalIgnoreCase));
    if (savedSkill is null
        || savedSkill.Enabled
        || !savedSkill.Text.Contains("saved smoke instruction", StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException("Markdown-folder Skillbook skills should save and reload title, enabled state, and body text.");
    }

    if (editedBuiltIn is null
        || !editedBuiltIn.Title.Equals("CC Main edited", StringComparison.Ordinal)
        || !editedBuiltIn.Text.Contains("edited built-in override", StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException("Built-in Skillbook entries should support project-side editable overrides.");
    }

    var legacySkillPath = Path.Combine(root, "skillbook", "legacy-project-skill.md");
    Directory.CreateDirectory(Path.GetDirectoryName(legacySkillPath)!);
    File.WriteAllText(
        legacySkillPath,
        """
        # Legacy project skill

        Keep flat markdown files visible.
        """);
    var flatLegacyProject = skillbook.LoadDocument().Flows.FirstOrDefault(flow => flow.Id.Equals("project", StringComparison.OrdinalIgnoreCase));
    if (flatLegacyProject is null
        || !flatLegacyProject.Sections.SelectMany(section => section.Skills).Any(skill => skill.Id.Equals("legacy-project-skill", StringComparison.OrdinalIgnoreCase)))
    {
        throw new InvalidOperationException("Legacy project markdown files should remain visible under Project Skillbook.");
    }

    var localSkillbookText = skillbook.BuildEnabledInstructionText();
    if (localSkillbookText.Contains("Codex is the reasoning engine inside ContextControl", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("Local LLM Skillbook text should not silently absorb Codex-only harness instructions.");
    }

    var codexPrompt = CodexHarnessService.BuildPrompt(new CodexHarnessRequest(
        "find the smallest files for this edit",
        ContextCapsulePhase.FileRequest,
        root,
        [
            new ContextCapsuleAttachment(
                "cc_project_dir.md",
                "dir",
                Path.Combine(root, "cc_project_dir.md"),
                "Views/MainWindow.axaml\nStyles/WorkbenchDesign.axaml",
                true)
        ],
        skillbook.BuildCodexInstructionText(ContextCapsulePhase.FileRequest),
        localSkillbookText));
    RequireTextContains(codexPrompt, "Your working directory is an empty harness folder by design.");
    RequireTextContains(codexPrompt, "CC Main");
    RequireTextContains(codexPrompt, "edited built-in override");
    RequireTextContains(codexPrompt, "CC Flow phase");
    RequireTextContains(codexPrompt, "Views/MainWindow.axaml");
    RequireTextNotContains(codexPrompt, "Agentic flow");

    var codexPatchPrompt = CodexHarnessService.BuildPrompt(new CodexHarnessRequest(
        "write the GO patch",
        ContextCapsulePhase.PatchWrite,
        root,
        [
            new ContextCapsuleAttachment(
                "cc_code_export.md",
                "source",
                Path.Combine(root, "cc_code_export.md"),
                "FILE: Views/MainWindow.axaml\n<Button />",
                true)
        ],
        skillbook.BuildCodexInstructionText(ContextCapsulePhase.PatchWrite),
        localSkillbookText));
    RequireTextContains(codexPatchPrompt, "One GO patch may contain many CC-REPLACE blocks across many files.");
    RequireTextContains(codexPatchPrompt, "patch.txt");
    RequireTextContains(codexPatchPrompt, "insert_after_function");
    RequireTextContains(codexPatchPrompt, "insert_before_function");
    RequireTextContains(codexPatchPrompt, "delete_function");
    RequireTextContains(codexPatchPrompt, "Patch block skeleton:");
    RequireTextContains(codexPatchPrompt, "Header variants:");
    RequireTextContains(codexPatchPrompt, "create_directory");
    RequireTextContains(codexPatchPrompt, "insert_include");
    RequireTextContains(codexPatchPrompt, "CMakeLists.txt");
    RequireTextContains(codexPatchPrompt, "If a required target, declaration, dependency, or build owner is not visible, request the next narrow CC export instead of guessing.");
    RequireTextContains(codexPatchPrompt, "Do not ask for more context when the visible export already contains the target file");
    RequireTextNotContains(codexPatchPrompt, "Block schemas:");

    var localCapsule = new ContextCapsuleBuilder().Build(new ContextCapsuleBuildRequest(
        "find the smallest files for this edit",
        ContextCapsulePhase.FileRequest,
        "smoke-model",
        "8k",
        8192,
        skillbook.BuildFlowInstructionText(ContextCapsulePhase.FileRequest),
        localSkillbookText,
        [
            new ContextCapsuleAttachment(
                "cc_project_dir.md",
                "dir",
                Path.Combine(root, "cc_project_dir.md"),
                "Views/MainWindow.axaml\nStyles/WorkbenchDesign.axaml",
                true)
        ]));
    RequireTextContains(localCapsule.Text, "ContextControl workflow instructions:");
    RequireTextContains(localCapsule.Text, "CC Main");
    RequireTextContains(localCapsule.Text, "CC Flow phase");

    var localPatchCapsule = new ContextCapsuleBuilder().Build(new ContextCapsuleBuildRequest(
        "write the GO patch",
        ContextCapsulePhase.PatchWrite,
        "smoke-model",
        "8k",
        8192,
        skillbook.BuildFlowInstructionText(ContextCapsulePhase.PatchWrite),
        localSkillbookText,
        [
            new ContextCapsuleAttachment(
                "cc_code_export.md",
                "source",
                Path.Combine(root, "cc_code_export.md"),
                "FILE: Views/MainWindow.axaml\n<Button />",
                true)
        ]));
    RequireTextContains(localPatchCapsule.Text, "Patch block skeleton:");
    RequireTextContains(localPatchCapsule.Text, "Bodyless modes are insert_include, delete_function, and create_directory.");
    RequireTextContains(localPatchCapsule.Text, "Header variants:");
    RequireTextContains(localPatchCapsule.Text, "If a required target, declaration, dependency, or build owner is not visible, request the next narrow CC export instead of guessing.");
    RequireTextContains(localPatchCapsule.Text, "Do not ask for more context when the visible export already contains the target file");

    if (!PromptFlowStepResolver.Resolve(
            ContextCapsulePhase.Chat,
            "DIR export",
            "cc_project_dir.md ready",
            "context",
            isAutopilotEnabled: true,
            isPatchPlanReady: false).Equals(PromptFlowStepResolver.RequestDir, StringComparison.Ordinal)
        || !PromptFlowStepResolver.Resolve(
            ContextCapsulePhase.FileRequest,
            "DIR ready",
            "project tree attached",
            "context",
            isAutopilotEnabled: true,
            isPatchPlanReady: false).Equals(PromptFlowStepResolver.FileRequestSend, StringComparison.Ordinal)
        || !PromptFlowStepResolver.Resolve(
            ContextCapsulePhase.SourceAudit,
            "Context ready",
            "source context attached",
            "codex",
            isAutopilotEnabled: true,
            isPatchPlanReady: false).Equals(PromptFlowStepResolver.SourceAuditSend, StringComparison.Ordinal)
        || !PromptFlowStepResolver.Resolve(
            ContextCapsulePhase.PatchWrite,
            "Local CC chat",
            "patch write",
            "context",
            isAutopilotEnabled: true,
            isPatchPlanReady: false).Equals(PromptFlowStepResolver.PatchWriteSend, StringComparison.Ordinal)
        || !PromptFlowStepResolver.Resolve(
            ContextCapsulePhase.PatchReview,
            "Codex CC chat",
            "patch review",
            "codex",
            isAutopilotEnabled: true,
            isPatchPlanReady: false).Equals(PromptFlowStepResolver.PatchReviewSend, StringComparison.Ordinal)
        || !PromptFlowStepResolver.Resolve(
            ContextCapsulePhase.Chat,
            "GO preview",
            "writing patch.txt",
            "context",
            isAutopilotEnabled: true,
            isPatchPlanReady: false).Equals(PromptFlowStepResolver.GoPreview, StringComparison.Ordinal)
        || !PromptFlowStepResolver.Resolve(
            ContextCapsulePhase.Chat,
            "Applying patch",
            "ccReplace",
            "context",
            isAutopilotEnabled: true,
            isPatchPlanReady: true).Equals(PromptFlowStepResolver.Apply, StringComparison.Ordinal)
        || !PromptFlowStepResolver.Resolve(
            ContextCapsulePhase.Chat,
            "Raw chat",
            "clean chat",
            "context",
            isAutopilotEnabled: false,
            isPatchPlanReady: false).Equals(PromptFlowStepResolver.RawImageBrowser, StringComparison.Ordinal))
    {
        throw new InvalidOperationException("Prompt flow resolver should map DIR, CC, patch, GO, apply, and raw states.");
    }

    var codexPlan = CodexHarnessService.BuildExecutionPlan(root);
    if (!codexPlan.HarnessRoot.EndsWith(Path.Combine(".tmp", "codex-harness"), StringComparison.OrdinalIgnoreCase)
        || !codexPlan.Arguments.Contains("--json", StringComparer.Ordinal)
        || !codexPlan.Arguments.Contains("--ephemeral", StringComparer.Ordinal)
        || !codexPlan.Arguments.Contains("--ignore-rules", StringComparer.Ordinal)
        || !HasAdjacentArguments(codexPlan.Arguments, "--sandbox", "read-only")
        || codexPlan.Arguments.Contains("--ask-for-approval", StringComparer.Ordinal)
        || !HasAdjacentArguments(codexPlan.Arguments, "-C", codexPlan.HarnessRoot))
    {
        throw new InvalidOperationException("Codex harness execution plan should force the optimized read-only CC capsule route without legacy exec flags.");
    }

    var configuredCodexPlan = CodexHarnessService.BuildExecutionPlan(root, "gpt-5.5", "xhigh");
    if (!HasAdjacentArguments(configuredCodexPlan.Arguments, "--model", "gpt-5.5")
        || !HasAdjacentArguments(configuredCodexPlan.Arguments, "-c", "model_reasoning_effort=\"xhigh\""))
    {
        throw new InvalidOperationException("Codex harness execution plan should pass selected model and reasoning overrides.");
    }

    var timelineStage = new CcTimelineStageViewModel("dir", "DIR", "Attach project tree", "DIR sends the tree capsule.");
    if (!timelineStage.ToolTipText.Contains("Attach project tree", StringComparison.Ordinal)
        || !timelineStage.ToolTipText.Contains("Capsule: DIR sends the tree capsule.", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("CC timeline stages should expose phase and capsule hover text.");
    }

    var codexReadyStatus = new CodexAvailabilityResult(true, "Codex CLI ready", "codex-cli test", IsAuthenticated: true);
    if (!codexReadyStatus.Available || !codexReadyStatus.IsAuthenticated || codexReadyStatus.RequiresLogin)
    {
        throw new InvalidOperationException("Codex availability should distinguish installed/authenticated from login-required states.");
    }

    var codexLoginStatus = new CodexAvailabilityResult(true, "Codex login required", "codex-cli test", RequiresLogin: true);
    if (!codexLoginStatus.Available || codexLoginStatus.IsAuthenticated || !codexLoginStatus.RequiresLogin)
    {
        throw new InvalidOperationException("Codex availability should expose login-required setup state for fresh machines.");
    }

    var codexInstallStatus = new CodexAvailabilityResult(false, "Codex CLI was not found", "", RequiresInstall: true);
    if (codexInstallStatus.Available || codexInstallStatus.IsAuthenticated || !codexInstallStatus.RequiresInstall)
    {
        throw new InvalidOperationException("Codex availability should expose install-required setup state before login is possible.");
    }

    RequireTextContains(CodexHarnessService.OfficialGuideUrl, "developers.openai.com/codex/cli");

    if (!CodexHarnessService.IsLoginRequiredText("an error occurred trying to access token")
        || !CodexHarnessService.IsLoginRequiredText("not logged in")
        || CodexHarnessService.IsLoginRequiredText("max output tokens reached"))
    {
        throw new InvalidOperationException("Codex login error detection should catch auth/token failures without treating ordinary token counts as auth setup.");
    }

    var cancellableProgress = new ChatRequestProgressViewModel("session", "Codex file request", isCancellable: true);
    if (!cancellableProgress.IsCancellable)
    {
        throw new InvalidOperationException("Codex chat progress rows should expose cancellation affordances.");
    }

    cancellableProgress.AppendThinking("Reading capsule");
    if (!cancellableProgress.HasThinking || !cancellableProgress.ThinkingPreviewText.Contains("Reading capsule", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("Codex chat progress rows should expose live thinking text when the CLI emits it.");
    }

    var codexUsage = new CodexUsageSnapshot(
        new CodexTokenUsage(11176, 3456, 16, 9, null),
        null,
        new CodexRateLimitSnapshot(
            new CodexRateLimitWindow(13, 300, DateTimeOffset.UtcNow.AddHours(1)),
            new CodexRateLimitWindow(2, 10080, DateTimeOffset.UtcNow.AddDays(3))));
    if (!codexUsage.UsageSummary.Contains("cached", StringComparison.Ordinal)
        || !codexUsage.UsageSummary.Contains("reasoning", StringComparison.Ordinal)
        || !codexUsage.RateLimitSummary.Contains("5h", StringComparison.Ordinal)
        || !codexUsage.RateLimitSummary.Contains("weekly", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("Codex usage snapshots should summarize per-prompt tokens and 5h/weekly limits.");
    }

    var bodylessDeleteFunctionValidation = promptBuilder.ValidatePatchBlocks(
        """
        BEGIN CC-REPLACE
        FILE: src/sample.cpp
        MODE: delete_function
        NAME: removeMe
        END CC-REPLACE
        """);
    if (!string.IsNullOrWhiteSpace(bodylessDeleteFunctionValidation))
    {
        throw new InvalidOperationException("GO patch validation should allow bodyless MODE:delete_function blocks.");
    }

    var richPatchPlan = promptBuilder.ParsePatchPlanSummary(
        """
        {
          "EffectiveCount": 4,
          "DuplicateCount": 1,
          "FileCount": 2,
          "DirectoryCount": 1,
          "CreatedCount": 2,
          "ChangedCount": 1,
          "RemovedCount": 1,
          "Added": 5,
          "Removed": 4,
          "Actions": [
            {
              "Mode": "whole_file",
              "Target": "generated/new.txt",
              "Part": "generated/new.txt",
              "Action": "create_file",
              "Kind": "CREATE FILE",
              "Bucket": "created",
              "Added": 2,
              "Removed": 0,
              "TotalLocAfter": 2,
              "IsDirectory": false,
              "IsDuplicate": false,
              "IsEffective": true,
              "CreatesFile": true,
              "CreatesDirectory": false,
              "VersionBefore": 0,
              "VersionAfter": 1
            },
            {
              "Mode": "function",
              "Target": "src/sample.cpp",
              "Name": "add",
              "Part": "add",
              "Action": "function",
              "Kind": "CHANGE",
              "Bucket": "changed",
              "Added": 3,
              "Removed": 2,
              "TotalLocAfter": 15,
              "IsDirectory": false,
              "IsDuplicate": false,
              "IsEffective": true,
              "CreatesFile": false,
              "CreatesDirectory": false,
              "VersionBefore": 1,
              "VersionAfter": 2
            },
            {
              "Mode": "delete_function",
              "Target": "src/sample.cpp",
              "Name": "removeMe",
              "Part": "removeMe",
              "Action": "delete_function",
              "Kind": "REMOVE",
              "Bucket": "removed",
              "Added": 0,
              "Removed": 2,
              "TotalLocAfter": 13,
              "IsDirectory": false,
              "IsDuplicate": false,
              "IsEffective": true,
              "CreatesFile": false,
              "CreatesDirectory": false,
              "VersionBefore": 1,
              "VersionAfter": 2
            },
            {
              "Mode": "create_directory",
              "Target": "generated",
              "Part": "generated",
              "Action": "create_directory",
              "Kind": "CREATE DIR",
              "Bucket": "created",
              "Added": 0,
              "Removed": 0,
              "TotalLocAfter": 0,
              "IsDirectory": true,
              "IsDuplicate": false,
              "IsEffective": true,
              "CreatesFile": false,
              "CreatesDirectory": true,
              "VersionBefore": 0,
              "VersionAfter": 0
            },
            {
              "Mode": "insert_include",
              "Target": "src/sample.cpp",
              "Part": "#include <string>",
              "Action": "insert_include",
              "Kind": "DUP",
              "Bucket": "duplicate",
              "Added": 0,
              "Removed": 0,
              "TotalLocAfter": 15,
              "IsDirectory": false,
              "IsDuplicate": true,
              "IsEffective": false,
              "CreatesFile": false,
              "CreatesDirectory": false,
              "DuplicateAction": "Include already exists",
              "DuplicateTarget": "src/sample.cpp :: #include <string>",
              "VersionBefore": 1,
              "VersionAfter": 2
            }
          ]
        }
        """);
    if (richPatchPlan.DisplayCreatedCount != 2
        || richPatchPlan.DisplayChangedCount != 1
        || richPatchPlan.DisplayRemovedCount != 1
        || richPatchPlan.DisplayDirectoryCount != 1
        || !richPatchPlan.Actions[0].VersionLabel.Equals("v0 > v1", StringComparison.Ordinal)
        || !richPatchPlan.Actions[1].KindLabel.Equals("CHANGE", StringComparison.Ordinal)
        || !richPatchPlan.Actions[4].BucketLabel.Equals("duplicate", StringComparison.Ordinal)
        || !richPatchPlan.CompactLabel.Contains("1 dirs", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("Patch plan summaries should preserve ccReplace action buckets, kinds, directory counts, and version hints.");
    }

    var compactPatchFile = new PatchPlanFileViewModel(
        "ide/ContextControl.Workbench/Styles/WorkbenchDesign/ContextControlDock/PromptComposer.axaml",
        [
            new PatchPlanActionSummary(
                "whole_file",
                "ide/ContextControl.Workbench/Styles/WorkbenchDesign/ContextControlDock/PromptComposer.axaml",
                "",
                "whole_file",
                "Replace whole file",
                "CHANGE",
                "changed",
                466,
                466,
                466,
                false,
                false,
                true,
                false,
                false,
                "",
                "",
                0,
                2)
        ]);
    if (!compactPatchFile.Summary.Equals("PromptComposer.axaml +466 -466 | 466 LOC | v0 > v2", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("Patch file rows should be compact one-liners without directory paths.");
    }

    var buildPatchPlanChatText = typeof(ContextControlViewModel).GetMethod(
        "BuildPatchPlanChatText",
        BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("Expected patch plan chat builder to exist.");
    var patchPlanChatText = (string)(buildPatchPlanChatText.Invoke(null, [richPatchPlan]) ?? "");
    if (patchPlanChatText.Contains("GO preview ready", StringComparison.OrdinalIgnoreCase)
        || patchPlanChatText.Contains("Files:", StringComparison.OrdinalIgnoreCase)
        || !patchPlanChatText.Contains("ccReplace | GO preview COMPLETE.", StringComparison.Ordinal)
        || !patchPlanChatText.Contains("```cc-patch-plan", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("GO preview chat text should be compact and use the patch-plan snippet directly.");
    }

    var buildPatchApplyChatText = typeof(ContextControlViewModel).GetMethod(
        "BuildPatchApplyChatText",
        BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("Expected patch apply chat builder to exist.");
    var patchApplyChatText = (string)(buildPatchApplyChatText.Invoke(
        null,
        [
            new ContextControlCommandResult(
                "GO apply",
                0,
                "Replace whole file: ide\\ContextControl.Workbench\\Styles\\WorkbenchDesign\\ContextControlDock\\PromptComposer.axaml\nDone. Applied CC-REPLACE actions: 1",
                ""),
            "effective",
            new[] { compactPatchFile }
        ]) ?? "");
    if (patchApplyChatText.Contains("Applied files:", StringComparison.OrdinalIgnoreCase)
        || patchApplyChatText.Contains("ccReplace output:", StringComparison.OrdinalIgnoreCase)
        || patchApplyChatText.Contains("Replace whole file:", StringComparison.OrdinalIgnoreCase)
        || !patchApplyChatText.Contains("ccReplace | GO apply COMPLETE.", StringComparison.Ordinal)
        || !patchApplyChatText.Contains("PromptComposer.axaml", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("GO apply chat text should not echo raw ccReplace output that can be parsed as a request.");
    }

    var diagnosticMessage = new LocalLlmChatMessageViewModel(
        "user",
        "Please use Codex mode.",
        "Codex CLI",
        "Codex file request",
        diagnosticPrompt: codexPrompt);
    if (!diagnosticMessage.HasDiagnosticPrompt || diagnosticMessage.IsDiagnosticExpanded)
    {
        throw new InvalidOperationException("Codex chat messages should keep the harness capsule available but collapsed by default.");
    }

    diagnosticMessage.ToggleDiagnostic();
    if (!diagnosticMessage.IsDiagnosticExpanded)
    {
        throw new InvalidOperationException("Codex harness diagnostics should be expandable from chat.");
    }

    var validCodexFileAudit = CodexPhaseAuditor.Audit(
        ContextCapsulePhase.FileRequest,
        """
        Views/MainWindow.axaml
        Styles/WorkbenchDesign.axaml
        END
        """);
    if (!validCodexFileAudit.Passed
        || validCodexFileAudit.Level != CodexPhaseAuditLevel.Pass
        || validCodexFileAudit.RequestLines.Count != 2)
    {
        throw new InvalidOperationException("Codex file-request audit should pass clean CC request lists ending with END.");
    }

    var warningCodexFileAudit = CodexPhaseAuditor.Audit(
        ContextCapsulePhase.FileRequest,
        """
        Here are the files:
        Views/MainWindow.axaml
        """);
    if (!warningCodexFileAudit.Passed || warningCodexFileAudit.Level != CodexPhaseAuditLevel.Warning)
    {
        throw new InvalidOperationException("Codex file-request audit should warn when usable request lines include prose or omit END.");
    }

    var invalidCodexFileAudit = CodexPhaseAuditor.Audit(ContextCapsulePhase.FileRequest, "I need to inspect the repo first.");
    if (invalidCodexFileAudit.Passed || invalidCodexFileAudit.Level != CodexPhaseAuditLevel.Error)
    {
        throw new InvalidOperationException("Codex file-request audit should fail responses without usable CC request lines.");
    }

    var validCodexPatchAudit = CodexPhaseAuditor.Audit(
        ContextCapsulePhase.PatchWrite,
        """
        BEGIN CC-REPLACE
        FILE: Views/MainWindow.axaml
        MODE: replace_region
        NAME: send-button
        ---
        <Button Classes="cc-prompt-send red" />
        END CC-REPLACE
        """,
        sourceContext: "<!-- CC-REPLACE-BEGIN: send-button -->\n<Button />\n<!-- CC-REPLACE-END: send-button -->");
    if (!validCodexPatchAudit.Passed
        || validCodexPatchAudit.Level != CodexPhaseAuditLevel.Pass
        || validCodexPatchAudit.PatchBlockCount != 1
        || validCodexPatchAudit.RequestLines.Count != 0)
    {
        throw new InvalidOperationException("Codex patch-write audit should pass valid CC-REPLACE blocks.");
    }

    var unmarkedRegionAudit = CodexPhaseAuditor.Audit(
        ContextCapsulePhase.PatchWrite,
        """
        BEGIN CC-REPLACE
        FILE: Views/MainWindow.axaml
        MODE: replace_region
        NAME: Button.cc-prompt-send
        ---
        <Style Selector="Button.cc-prompt-send" />
        END CC-REPLACE
        """,
        sourceContext: "<Style Selector=\"Button.cc-prompt-send\" />");
    if (unmarkedRegionAudit.Passed || unmarkedRegionAudit.Level != CodexPhaseAuditLevel.Error)
    {
        throw new InvalidOperationException("Codex patch-write audit should reject replace_region blocks when the visible source has no matching marker.");
    }

    var needMoreCodexAudit = CodexPhaseAuditor.Audit(
        ContextCapsulePhase.PatchWrite,
        """
        NEED_MORE_CONTEXT
        FUNCTION ViewModels/ContextControlViewModel.cs :: SendAsync
        END
        """);
    if (!needMoreCodexAudit.Passed
        || needMoreCodexAudit.Level != CodexPhaseAuditLevel.Pass
        || needMoreCodexAudit.RequestLines.Count != 1)
    {
        throw new InvalidOperationException("Codex patch-write audit should allow NEED_MORE_CONTEXT plus valid CC request lines.");
    }

    var bareFindPatchAudit = CodexPhaseAuditor.Audit(
        ContextCapsulePhase.PatchWrite,
        """
        FIND: cc-prompt-send
        END
        """);
    if (!bareFindPatchAudit.Passed
        || bareFindPatchAudit.Level != CodexPhaseAuditLevel.Pass
        || bareFindPatchAudit.RequestLines.Count != 1)
    {
        throw new InvalidOperationException("Codex patch-write audit should allow bare valid CC request lines when more context is needed.");
    }

    var multiFindPatchAudit = CodexPhaseAuditor.Audit(
        ContextCapsulePhase.PatchWrite,
        """
        FIND: cc-prompt-send
        FIND: SendCommand
        END
        """);
    if (multiFindPatchAudit.Passed
        || multiFindPatchAudit.Level != CodexPhaseAuditLevel.Error
        || !multiFindPatchAudit.Details.Any(detail => detail.Contains("FIND must be exactly one line", StringComparison.OrdinalIgnoreCase)))
    {
        throw new InvalidOperationException("Codex patch-write audit should reject multiple FIND lines when more context is needed.");
    }

    var invalidCodexPatchAudit = CodexPhaseAuditor.Audit(
        ContextCapsulePhase.PatchWrite,
        """
        BEGIN CC-REPLACE
        Views/MainWindow.axaml
        MODE: whole_file
        ---
        <Window />
        END CC-REPLACE
        """);
    if (invalidCodexPatchAudit.Passed || invalidCodexPatchAudit.Level != CodexPhaseAuditLevel.Error)
    {
        throw new InvalidOperationException("Codex patch-write audit should fail malformed CC-REPLACE blocks.");
    }

    var actionClaimAudit = CodexPhaseAuditor.Audit(ContextCapsulePhase.Chat, "I ran rg and edited the file.");
    if (!actionClaimAudit.Passed || actionClaimAudit.Level != CodexPhaseAuditLevel.Warning)
    {
        throw new InvalidOperationException("Codex audit should warn when read-only harness output claims direct repo actions.");
    }

    var chatRequestLike = new LocalLlmChatMessageViewModel(
        "assistant",
        """
        EXPAND: .
        END
        """,
        "gpt-5.3-codex",
        "Codex chat");
    if (chatRequestLike.Snippets.Any(snippet => snippet.IsRequestList))
    {
        throw new InvalidOperationException("Codex chat without DIR should not expose request-looking output as a Use-for-CC snippet.");
    }

    var invalidAuditedFileRequest = new LocalLlmChatMessageViewModel(
        "assistant",
        """
        <think>
        Codex phase audit: Error
        Codex phase audit failed: file-request output is not valid for the current DIR manifest.
        </think>

        EXPAND: .
        END
        """,
        "gpt-5.3-codex",
        "Codex file request");
    if (invalidAuditedFileRequest.Snippets.Any(snippet => snippet.IsRequestList))
    {
        throw new InvalidOperationException("Invalid audited Codex file-request output should not expose a Use-for-CC snippet.");
    }

    var invalidAuditedCcRequest = new LocalLlmChatMessageViewModel(
        "assistant",
        """
        <think>
        Codex phase audit: Error
        Codex phase audit failed: patch-write returned an invalid context request.
        </think>

        FIND: cc-prompt-send
        Views/MainWindow.axaml
        END
        """,
        "gpt-5.3-codex",
        "Codex CC");
    if (invalidAuditedCcRequest.Snippets.Any(snippet => snippet.IsRequestList))
    {
        throw new InvalidOperationException("Invalid audited Codex CC output should not expose a Use-for-CC snippet.");
    }

    var mixedRequestSnippetMessage = new LocalLlmChatMessageViewModel(
        "assistant",
        """
        Views/MainWindow.axaml
        FIND: cc-prompt-send
        FIND: SendCommand
        END
        """,
        "ContextControl",
        "CC request");
    var mixedRequestSnippets = mixedRequestSnippetMessage.Snippets
        .Where(snippet => snippet.IsRequestList)
        .ToArray();
    if (mixedRequestSnippets.Length != 2
        || mixedRequestSnippets.Count(snippet => snippet.IsSourceRequestList) != 1
        || mixedRequestSnippets.Count(snippet => snippet.IsFindRequestList) != 1
        || !mixedRequestSnippetMessage.Parts.Any(part => part.IsText
            && part.Text.Contains("Source and FIND run separately.", StringComparison.Ordinal)))
    {
        throw new InvalidOperationException("Mixed source/FIND request output should render separate actionable snippets with a compact warning.");
    }

    var multiBlockPatchMessage = new LocalLlmChatMessageViewModel(
        "assistant",
        """
        BEGIN CC-REPLACE
        FILE: Views/MainWindow.axaml
        MODE: whole_file
        ---
        <Window />
        END CC-REPLACE

        BEGIN CC-REPLACE
        FILE: Styles/WorkbenchDesign.axaml
        MODE: whole_file
        ---
        <Styles />
        END CC-REPLACE
        """,
        "gpt-5.3-codex",
        "Codex patch write");
    var aggregatePatchSnippet = multiBlockPatchMessage.Snippets.SingleOrDefault(snippet => snippet.IsPatch);
    if (aggregatePatchSnippet is null
        || aggregatePatchSnippet.Text.Split("BEGIN CC-REPLACE", StringSplitOptions.None).Length - 1 != 2
        || multiBlockPatchMessage.Parts.Count(part => part.IsSnippet && part.Snippet?.IsPatch == true) != 1)
    {
        throw new InvalidOperationException("Multiple CC-REPLACE blocks in one assistant answer should produce one aggregate patch snippet.");
    }
    if (multiBlockPatchMessage.Text.Contains("[CC-REPLACE patch", StringComparison.Ordinal)
        || !aggregatePatchSnippet.CompactMetaLabel.Contains("CC-Replace patch: 2 blocks", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("Multiple CC-REPLACE blocks should render the block count inside the patch snippet, not as separate visible chat text.");
    }
    if (multiBlockPatchMessage.Snippets.Any(snippet => snippet.IsRequestList))
    {
        throw new InvalidOperationException("Compact CC-REPLACE placeholders should not be misread as CC request snippets.");
    }

    if (!aggregatePatchSnippet.PreviewText.Contains("MainWindow.axaml", StringComparison.OrdinalIgnoreCase)
        || !aggregatePatchSnippet.PreviewText.Contains("whole_file", StringComparison.OrdinalIgnoreCase)
        || aggregatePatchSnippet.PreviewText.Contains("BEGIN CC-REPLACE", StringComparison.OrdinalIgnoreCase)
        || aggregatePatchSnippet.CollapsedPreviewHeight > 48.0)
    {
        throw new InvalidOperationException("Collapsed CC-REPLACE snippets should show compact file/mode summaries instead of raw patch headers.");
    }

    RunChatRendererRegression();

    var historyRoot = Path.Combine(root, "history-root");
    var historyService = new ChatHistoryService(historyRoot);
    var savedSession = new ChatHistorySessionData
    {
        Id = "workflow-task-session",
        Title = "Task memory",
        CreatedUtc = DateTime.UtcNow,
        UpdatedUtc = DateTime.UtcNow,
        WorkflowTaskText = "Fix the prompt send button color"
    };
    historyService.Save(
        new ChatHistoryDocument
        {
            ConversationKind = "chat",
            SelectedSessionId = savedSession.Id,
            Sessions = [savedSession]
        },
        "task-memory",
        "chat",
        mirrorDefaultScope: false);
    var reloadedHistory = historyService.Load("task-memory", "chat", includeFallbacks: false);
    if (!reloadedHistory.Sessions.Single().WorkflowTaskText.Equals(savedSession.WorkflowTaskText, StringComparison.Ordinal))
    {
        throw new InvalidOperationException("Workflow task memory should persist through chat history save/load.");
    }

    var fenced = new LocalLlmChatMessageViewModel(
        "assistant",
        """
        ## Result

        This keeps **bold**, _italic_, `inline code`, lists, and tables in the text surface.

        ~~~json
        {"ok": true}
        ~~~

        ````csharp
        public sealed class Demo {}
        ````
        """);
    if (fenced.Snippets.Count != 2
        || fenced.Snippets[0].Language != "json"
        || fenced.Snippets[1].Language != "csharp"
        || fenced.Parts.Count(part => part.IsSnippet) != 2)
    {
        var languages = string.Join(", ", fenced.Snippets.Select(snippet => snippet.Language));
        throw new InvalidOperationException(
            $"Markdown fence parsing should support tilde and longer backtick fences. Snippets={fenced.Snippets.Count}, PartsSnippets={fenced.Parts.Count(part => part.IsSnippet)}, Languages=[{languages}]");
    }

    var rwkv = LocalLlmService.Catalog.First(model => model.Id.Equals("mollysama/rwkv-7-g1f:1.5b", StringComparison.OrdinalIgnoreCase));
    var rwkvViewModel = new LocalLlmModelViewModel(rwkv);
    rwkvViewModel.ApplyState(
        isInstalled: false,
        isAvailable: false,
        new LocalLlmHardwareProfile(Array.Empty<LocalLlmGpuInfo>()),
        isBackendDependencyReady: true);
    if (!rwkvViewModel.UsesOllamaPull
        || rwkvViewModel.RequiresManualBackend
        || !rwkvViewModel.CanPull
        || !rwkvViewModel.PullButtonLabel.Equals("Download", StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException("Ollama-packaged RWKV catalog models should stay downloadable through Ollama instead of becoming dependency-only ready.");
    }

    var tinySd = LocalLlmService.Catalog.First(model => model.Id.Equals("segmind/tiny-sd", StringComparison.OrdinalIgnoreCase));
    var tinySdViewModel = new LocalLlmModelViewModel(tinySd);
    tinySdViewModel.ApplyState(
        isInstalled: false,
        isAvailable: false,
        new LocalLlmHardwareProfile(Array.Empty<LocalLlmGpuInfo>()),
        isBackendDependencyReady: false);
    tinySdViewModel.ApplyBackendDependencyState(true);
    if (!tinySdViewModel.IsImageGenerationModel
        || !tinySdViewModel.RequiresManualBackend
        || !tinySdViewModel.DependencyId.Equals("diffusers", StringComparison.OrdinalIgnoreCase)
        || !tinySdViewModel.CanDownloadBackendModel
        || tinySdViewModel.CanUseManualBackend
        || tinySdViewModel.IsAvailable
        || !tinySdViewModel.PullButtonLabel.Equals("Download", StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException("Diffusers-backed image models should expose a model download action, but should not be selectable until weights are cached.");
    }

    tinySdViewModel.ApplyBackendModelState(true);
    if (!tinySdViewModel.IsBackendModelReady
        || tinySdViewModel.CanDownloadBackendModel
        || !tinySdViewModel.CanUseManualBackend
        || !tinySdViewModel.IsAvailable
        || !tinySdViewModel.PullButtonLabel.Equals("Ready", StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException("Diffusers-backed image models should become ready after their Hugging Face weights are cached.");
    }

    RequireImageDependency("nota-ai/bk-sdm-small", "diffusers");
    RequireImageDependency("SimianLuo/LCM_Dreamshaper_v7", "diffusers");
    RequireImageDependency("stabilityai/sd-turbo", "diffusers");
    RequireImageDependency("black-forest-labs/FLUX.2-klein-4B", "diffusers");
    RequireImageDependency("x/flux1-dev-q4", "stable_diffusion_cpp");
    RequireOllamaImagePlatformGate("x/flux2-klein");
    RequireOllamaImagePlatformGate("x/flux2-klein:9b");
    RequireOllamaImagePlatformGate("x/z-image-turbo");
    RequireOllamaDownload("qwen3:235b");
    RequireOllamaDownload("llama3.1:405b");
    RequireOllamaDownload("devstral-small-2:24b");
    RequireOllamaDownload("qwen2.5vl:3b");
    RequireOllamaDownload("hf.co/ggml-org/Qwen2.5-Omni-7B-GGUF:Q4_K_M");
    RequireBackendDependenciesAutoinstallable();

    var diffusersDependency = new LlmBackendDependencyViewModel(
        "diffusers",
        "Hugging Face Diffusers",
        "image generation library",
        "Python library",
        "Windows, macOS, Linux",
        "Runs local image checkpoints.",
        "Install Python plus diffusers.",
        isRequired: false,
        isRecommended: true);
    diffusersDependency.ApplyStatus(true, "Ready", "External Python", isManaged: false);
    if (diffusersDependency.CanForceInstall
        || diffusersDependency.CanUninstall
        || !diffusersDependency.InstallActionLabel.Equals("External", StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException("External Python dependencies should not expose destructive repair actions.");
    }

    diffusersDependency.ApplyStatus(true, "Ready", "Managed Python", isManaged: true);
    if (diffusersDependency.CanForceInstall
        || !diffusersDependency.CanUninstall
        || !diffusersDependency.InstallActionLabel.Equals("Uninstall", StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException("Managed Python dependencies should keep the normal uninstall action.");
    }

    var graphRoot = new ProjectNodeViewModel(
        "sample",
        "",
        true,
        "v1",
        [
            new ProjectNodeViewModel(
                "src",
                "src",
                true,
                "v1",
                [
                    new ProjectNodeViewModel(
                        "Controllers",
                        "src/Controllers",
                        true,
                        "v2",
                        [new ProjectNodeViewModel("HomeController.cs", "src/Controllers/HomeController.cs", false, "v3", loc: 31)],
                        fileCount: 1,
                        diskFileCount: 1),
                    new ProjectNodeViewModel("App.config", "src/App.config", false, "v2", loc: 8),
                    new ProjectNodeViewModel("Program.cs", "src/Program.cs", false, "v2", loc: 42)
                ],
                fileCount: 3,
                diskFileCount: 3),
            new ProjectNodeViewModel("README.md", "README.md", false, "v1", loc: 12)
        ],
        fileCount: 4,
        diskFileCount: 4);
    var graph = new ProjectGraphRenderControl
    {
        Items = [graphRoot]
    };
    var exportDetails = new ProjectGraphExportDetails(
        root,
        ".NET | 2/2 visible files match current rules | 3 scanned for setup",
        "2 allowed | 2 LOC | 0 skipped types | 0 skipped folders",
        [
            new ProjectStackSection("Detected Stack", [".NET: C# files"]),
            new ProjectStackSection("Uses", ["Build tool: dotnet"]),
            new ProjectStackSection("Languages", ["C#: 1 files", "Markdown: 1 files"]),
            new ProjectStackSection("Top File Types", [".cs: 1", ".md: 1"]),
            new ProjectStackSection("Autosetup Plan", ["No missing stack rules detected"])
        ]);

    var dot = graph.ExportGraphText("dot", exportDetails);
    RequireTextContains(dot, "digraph ProjectGraph");
    RequireTextContains(dot, "n0 [label=");
    RequireTextContains(dot, "n0 -> n1");
    RequireTextContains(dot, "Project Details");

    var graphMl = graph.ExportGraphText("graphml", exportDetails);
    RequireTextContains(graphMl, "<graph id=\"ProjectGraph\" edgedefault=\"directed\">");
    RequireTextContains(graphMl, "<data key=\"parent\">n0</data>");
    RequireTextContains(graphMl, "<data key=\"projectDetails\">");

    var mermaid = graph.ExportGraphText("mmd", exportDetails);
    RequireTextContains(mermaid, "graph TD");
    RequireTextContains(mermaid, "n0[\"");
    RequireTextContains(mermaid, "n0 --> n1");
    RequireTextContains(mermaid, "%% Project Details");

    var json = JsonNode.Parse(graph.ExportGraphText("json", exportDetails))!.AsObject();
    if (json["graph"]?["roots"]?.AsArray().FirstOrDefault()?.GetValue<string>() != "n0"
        || json["nodes"]?.AsArray().Count < 4
        || json["projectDetails"]?["sections"]?.AsArray().Count < 10)
    {
        throw new InvalidOperationException("Graph JSON export should preserve roots, full node declarations, and scanner details.");
    }

    var graphNodes = json["nodes"]!.AsArray().Select(node => node!.AsObject()).ToArray();
    var controllersY = NodeNumberByPath(graphNodes, "src/Controllers", "y");
    if (NodeNumberByPath(graphNodes, "src/App.config", "y") >= controllersY
        || NodeNumberByPath(graphNodes, "src/Program.cs", "y") >= controllersY)
    {
        throw new InvalidOperationException("Generation-local files should be laid out above directories that continue the graph.");
    }

    Console.WriteLine("ContextFileResolver smoke tests passed.");
}
finally
{
    try
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
    catch
    {
        // Best-effort cleanup only; temp leftovers must not fail resolver checks.
    }
}

static void RunChatRendererRegression()
{
    var scrollbarGeometry = typeof(ChatTranscriptRenderControl).GetMethod(
        "TryResolveExtensionScrollbarGeometry",
        BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("Chat transcript scrollbar geometry helper should exist for animation regression checks.");
    object?[] tinyTrackArgs = [15.0, 3.0, 420.0, 0.0, null, null, null];
    if ((bool)scrollbarGeometry.Invoke(null, tinyTrackArgs)!)
    {
        throw new InvalidOperationException("Tiny thinking/harness animation bodies should skip scrollbar drawing instead of constructing invalid thumb geometry.");
    }

    object?[] shortTrackArgs = [26.936581858310596, 14.936581858310596, 420.0, 0.0, null, null, null];
    if (!(bool)scrollbarGeometry.Invoke(null, shortTrackArgs)!)
    {
        throw new InvalidOperationException("Short but drawable thinking/harness animation bodies should still resolve scrollbar geometry.");
    }

    var trackHeight = (double)shortTrackArgs[4]!;
    var thumbHeight = (double)shortTrackArgs[5]!;
    if (trackHeight <= 0.0
        || thumbHeight <= 0.0
        || thumbHeight > trackHeight)
    {
        throw new InvalidOperationException("Thinking/harness scrollbar thumb height must never exceed its animated track height.");
    }

    EnsureAvaloniaForChatRenderer();
    var control = new ChatTranscriptRenderControl();
    var message = new LocalLlmChatMessageViewModel(
        "assistant",
        """
        ```request
        ide/ContextControl.Workbench/Controls/Chat/ChatTranscriptRenderControl.cs
        END
        ```
        """,
        "gpt-5-codex",
        "Codex CC");
    message.AppendLiveThinking("Thinking trace for measured-height invalidation regression.");
    message.IsThinkingExpanded = false;
    control.Items = [message];

    var resetLayoutCache = typeof(ChatTranscriptRenderControl).GetMethod(
        "ResetLayoutCache",
        BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("Chat transcript layout cache reset should exist for invalidation regression checks.");
    var getOrBuildLayout = typeof(ChatTranscriptRenderControl).GetMethod(
        "GetOrBuildLayout",
        BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("Chat transcript row layout builder should exist for invalidation regression checks.");
    var rowHeightsField = typeof(ChatTranscriptRenderControl).GetField(
        "_rowHeights",
        BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("Chat transcript row height cache should exist for invalidation regression checks.");

    resetLayoutCache.Invoke(control, [900.0, 1]);
    _ = getOrBuildLayout.Invoke(control, [0]);
    var rowHeights = (IDictionary<int, double>)rowHeightsField.GetValue(control)!;
    if (!rowHeights.ContainsKey(0))
    {
        throw new InvalidOperationException("Measured chat row should populate the row-height cache before toggle invalidation checks.");
    }

    message.IsThinkingExpanded = true;
    if (!rowHeights.ContainsKey(0))
    {
        throw new InvalidOperationException("Thinking expand/collapse invalidation should preserve measured row height during animation.");
    }

    var snippet = message.Snippets.FirstOrDefault()
        ?? throw new InvalidOperationException("Chat renderer invalidation regression needs a parsed snippet.");
    snippet.IsExpanded = true;
    if (!rowHeights.ContainsKey(0))
    {
        throw new InvalidOperationException("Snippet expand/collapse invalidation should preserve measured row height during animation.");
    }
}

static void RunChatRendererPerfSanity()
{
    EnsureAvaloniaForChatRenderer();

    var control = new ChatTranscriptRenderControl
    {
        ChatFontSize = 10.0
    };
    var diagnostic = string.Join(
        Environment.NewLine,
        Enumerable.Range(0, 260).Select(index => $"Harness capsule line {index:000}: source export and route metadata for layout warming."));
    var thinking = string.Join(
        Environment.NewLine,
        Enumerable.Range(0, 260).Select(index => $"Thinking trace line {index:000}: evaluating context and preparing the next compact action."));
    var message = new LocalLlmChatMessageViewModel(
        "assistant",
        """
        Here is the compact request.

        ```request
        ide/ContextControl.Workbench/Controls/Chat/ChatTranscriptRenderControl.cs
        END
        ```

        ```patch
        BEGIN CC-REPLACE
        FILE: ide/ContextControl.Workbench/Controls/Chat/ChatTranscriptRenderControl.cs
        MODE: whole_file
        ---
        placeholder
        END CC-REPLACE
        ```
        """,
        "gpt-5-codex",
        "Codex patch write",
        diagnosticPrompt: diagnostic);
    message.AppendLiveThinking(thinking);
    message.IsThinkingExpanded = true;
    message.IsDiagnosticExpanded = true;

    var buildLayout = typeof(ChatTranscriptRenderControl).GetMethod(
        "BuildMessageLayout",
        BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("Chat transcript layout builder should exist for performance sanity checks.");

    object?[] args = [message, 900.0];
    _ = buildLayout.Invoke(control, args);
    var iterations = 240;
    var stopwatch = Stopwatch.StartNew();
    for (var index = 0; index < iterations; index++)
    {
        _ = buildLayout.Invoke(control, args);
    }

    stopwatch.Stop();
    if (stopwatch.ElapsedMilliseconds > 3_000)
    {
        throw new InvalidOperationException($"Chat renderer warm layout should stay comfortably below frame-budget risk; {iterations} layouts took {stopwatch.ElapsedMilliseconds:N0} ms.");
    }

    Console.WriteLine($"Chat renderer perf sanity passed: {iterations} warm layouts in {stopwatch.ElapsedMilliseconds:N0} ms.");
}

static void EnsureAvaloniaForChatRenderer()
{
    if (Application.Current is not null)
    {
        return;
    }

    AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .WithInterFont()
        .SetupWithoutStarting();
}
