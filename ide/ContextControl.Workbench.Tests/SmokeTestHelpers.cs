using System.Diagnostics;
using System.Text.Json.Nodes;
using ContextControl.Workbench.Controls;
using ContextControl.Workbench.Services;
using ContextControl.Workbench.ViewModels;

internal static partial class SmokeTestHelpers
{
internal static void WriteFile(string root, string relativePath, string text)
{
    var fullPath = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
    Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
    File.WriteAllText(fullPath, text);
}

internal static void RunProjectScannerShaderAutosetupSmoke()
{
    var projectRoot = Path.Combine(Path.GetTempPath(), "ContextControlShaderAutosetupSmoke", Guid.NewGuid().ToString("N"));
    string[] shaderExtensions = [".glsl", ".vert", ".frag", ".comp"];
    (string Path, string Extension)[] shaderFiles =
    [
        ("shaders/common.glsl", ".glsl"),
        ("shaders/fullscreen.vert", ".vert"),
        ("shaders/lighting.frag", ".frag"),
        ("shaders/compute.comp", ".comp")
    ];

    try
    {
        WriteProjectFile("CMakeLists.txt", "add_executable(shader-autosetup src/main.cpp)\n");
        WriteProjectFile("src/main.cpp", "int main() { return 0; }\n");
        foreach (var shaderFile in shaderFiles)
        {
            WriteProjectFile(shaderFile.Path, "void main() {}\n");
        }

        var rules = ProjectFileRules.Load(projectRoot);
        foreach (var extension in shaderExtensions)
        {
            rules.SkipExtension(extension);
        }

        var result = ProjectStackScanner.Scan(projectRoot, rules);
        foreach (var extension in shaderExtensions)
        {
            if (!result.AutoSetupRules.SupportedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Autosetup should restore shader extension {extension}. Details: {result.DetailsText}");
            }

            if (result.AutoSetupRules.IgnoredExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Autosetup should not keep shader extension {extension} skipped. Details: {result.DetailsText}");
            }

            if (!result.AutoSetupRules.LocExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Autosetup should count shader extension {extension} as LOC/code-like.");
            }
        }

        var skippedTypes = result.Sections
            .FirstOrDefault(section => section.Title.Equals("Skipped File Types", StringComparison.OrdinalIgnoreCase))
            ?.Items
            ?? [];
        foreach (var extension in shaderExtensions)
        {
            if (!skippedTypes.Any(item => item.StartsWith($"{extension}:", StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException($"Project scanner should report skipped shader extension {extension}. Details: {result.DetailsText}");
            }
        }

        var languages = result.Sections
            .FirstOrDefault(section => section.Title.Equals("Languages", StringComparison.OrdinalIgnoreCase))
            ?.Items
            ?? [];
        if (!languages.Any(item => item.StartsWith("Shader:", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"Project scanner should classify shader files as code-like Shader files. Details: {result.DetailsText}");
        }

        rules.ApplyCleanRules(
            result.AutoSetupRules.IgnoredDirectories,
            result.AutoSetupRules.IgnoredFileNames,
            result.AutoSetupRules.IgnoredExtensions,
            result.AutoSetupRules.SupportedExtensions,
            result.AutoSetupRules.LocExtensions);
        foreach (var shaderFile in shaderFiles)
        {
            var fileName = Path.GetFileName(shaderFile.Path);
            if (!rules.ShouldTrackFile(shaderFile.Path, fileName, shaderFile.Extension))
            {
                throw new InvalidOperationException($"Applied autosetup rules should track {shaderFile.Path}.");
            }
        }
    }
    finally
    {
        try
        {
            if (Directory.Exists(projectRoot))
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup only; temp leftovers must not fail smoke checks.
        }
    }

    void WriteProjectFile(string relativePath, string text)
    {
        var fullPath = Path.Combine(projectRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, text);
    }
}

internal static void RunCcDirManifestScriptSmoke()
{
    var repoRoot = FindRepositoryRoot(Directory.GetCurrentDirectory());
    if (repoRoot is null)
    {
        throw new InvalidOperationException("Could not locate repository root for ccDir manifest smoke test.");
    }

    var projectRoot = Path.Combine(Path.GetTempPath(), "ContextControlDirManifestSmoke", Guid.NewGuid().ToString("N"));
    try
    {
        Directory.CreateDirectory(Path.Combine(projectRoot, "src", "app"));
        Directory.CreateDirectory(Path.Combine(projectRoot, "docs"));
        Directory.CreateDirectory(Path.Combine(projectRoot, "lib"));
        Directory.CreateDirectory(Path.Combine(projectRoot, "ide", "ContextControl.Setup"));
        Directory.CreateDirectory(Path.Combine(projectRoot, "ide", "ContextControl.Workbench.Tests"));
        Directory.CreateDirectory(Path.Combine(projectRoot, "ide", "ContextControl.Workbench", "Services"));
        Directory.CreateDirectory(Path.Combine(projectRoot, "ide", "ContextControl.Workbench", "Services", "ContextControl"));
        Directory.CreateDirectory(Path.Combine(projectRoot, "ide", "ContextControl.Workbench", "ViewModels"));
        Directory.CreateDirectory(Path.Combine(projectRoot, "ide", "ContextControl.Workbench", "Views"));
        Directory.CreateDirectory(Path.Combine(projectRoot, "ide", "ContextControl.Workbench", "Views", "MainWindowParts", "WorkspacePages"));
        Directory.CreateDirectory(Path.Combine(projectRoot, "ide", "ContextControl.Workbench", "Styles", "WorkbenchDesign", "ContextControlDock"));
        Directory.CreateDirectory(Path.Combine(projectRoot, "ide", "ContextControl.Workbench", "Controls", "Workspace"));
        File.WriteAllText(Path.Combine(projectRoot, "src", "app", "main.cpp"), "int main() { return 0; }\nint helper() { return 1; }\n");
        File.WriteAllText(Path.Combine(projectRoot, "src", "app", "helper.cpp"), "int helperTwo() { return 2; }\n");
        File.WriteAllText(Path.Combine(projectRoot, "README.md"), "# Smoke\n");
        File.WriteAllText(Path.Combine(projectRoot, "ccDir.ps1"), "function Invoke-SmokeDir { }\n");
        File.WriteAllText(Path.Combine(projectRoot, "lib", "Cc.Dir.Export.ps1"), "function Invoke-CcDirExport { }\n");
        File.WriteAllText(Path.Combine(projectRoot, "lib", "Cc.Export.Source.ps1"), "function Invoke-CcSourceExport { }\n");
        File.WriteAllText(Path.Combine(projectRoot, "lib", "Cc.Export.Functions.ps1"), "function Export-CcFunction { }\n");
        File.WriteAllText(Path.Combine(projectRoot, "lib", "Cc.Replace.Parse.ps1"), "function Parse-CcReplaceBlocks { }\n");
        Directory.CreateDirectory(Path.Combine(projectRoot, "lib", "export"));
        File.WriteAllText(Path.Combine(projectRoot, "lib", "export", "Cc.Export.Source.ps1"), "function Invoke-LegacyExportMirror { }\n");
        File.WriteAllText(Path.Combine(projectRoot, "ide", "ContextControl.Setup", "ContextControl.Setup.csproj"), "<Project />\n");
        File.WriteAllText(Path.Combine(projectRoot, "ide", "ContextControl.Setup", "Program.cs"), "public static class SetupProgram { public static void Main() { } }\n");
        File.WriteAllText(Path.Combine(projectRoot, "ide", "ContextControl.Workbench.Tests", "ContextControl.Workbench.Tests.csproj"), "<Project />\n");
        File.WriteAllText(Path.Combine(projectRoot, "ide", "ContextControl.Workbench.Tests", "Program.cs"), "public static class TestsProgram { public static void Main() { } }\n");
        File.WriteAllText(Path.Combine(projectRoot, "ide", "ContextControl.Workbench", "ContextControl.Workbench.csproj"), "<Project />\n");
        File.WriteAllText(Path.Combine(projectRoot, "ide", "ContextControl.Workbench", "Services", "ContextControl", "ContextDirManifestParser.cs"), "public sealed class ContextDirManifestParser { }\n");
        File.WriteAllText(Path.Combine(projectRoot, "ide", "ContextControl.Workbench", "Services", "ContextControl", "ContextControlProcessService.cs"), "public sealed class ContextControlProcessService { }\n");
        File.WriteAllText(Path.Combine(projectRoot, "ide", "ContextControl.Workbench", "Services", "ContextControlProcessService.cs"), "public sealed class ContextControlProcessService { }\n");
        File.WriteAllText(Path.Combine(projectRoot, "ide", "ContextControl.Workbench", "ViewModels", "WorkbenchViewModel.cs"), "public sealed class WorkbenchViewModel { }\n");
        File.WriteAllText(Path.Combine(projectRoot, "ide", "ContextControl.Workbench", "Views", "MainWindow.axaml"), "<Window />\n");
        File.WriteAllText(Path.Combine(projectRoot, "ide", "ContextControl.Workbench", "Views", "MainWindow.axaml.cs"), "public sealed class MainWindow { }\n");
        File.WriteAllText(Path.Combine(projectRoot, "ide", "ContextControl.Workbench", "Styles", "WorkbenchDesign.axaml"), "<Styles />\n");
        File.WriteAllText(Path.Combine(projectRoot, "ide", "ContextControl.Workbench", "Views", "MainWindowParts", "WorkspacePages", "SkillbookPage.axaml"), "<UserControl />\n");
        File.WriteAllText(Path.Combine(projectRoot, "ide", "ContextControl.Workbench", "Styles", "WorkbenchDesign", "ContextControlDock", "ConversationAndSkillbook.axaml"), "<Styles />\n");
        File.WriteAllText(Path.Combine(projectRoot, "ide", "ContextControl.Workbench", "Controls", "Workspace", "SkillbookRenderControl.cs"), "public sealed class SkillbookRenderControl { }\n");
        for (var index = 0; index < 4; index++)
        {
            File.WriteAllText(Path.Combine(projectRoot, "ide", "ContextControl.Workbench", "Controls", $"SmokeControl{index}.cs"), $"public sealed class SmokeControl{index} {{ }}\n");
            File.WriteAllText(Path.Combine(projectRoot, "ide", "ContextControl.Workbench", "Services", $"SmokeService{index}.cs"), $"public sealed class SmokeService{index} {{ }}\n");
            File.WriteAllText(Path.Combine(projectRoot, "ide", "ContextControl.Workbench", "Styles", $"SmokeStyle{index}.axaml"), "<Styles />\n");
            File.WriteAllText(Path.Combine(projectRoot, "ide", "ContextControl.Workbench", "ViewModels", $"SmokeViewModel{index}.cs"), $"public sealed class SmokeViewModel{index} {{ }}\n");
            File.WriteAllText(Path.Combine(projectRoot, "ide", "ContextControl.Workbench", "Views", $"SmokeView{index}.axaml"), "<UserControl />\n");
        }

        File.WriteAllText(Path.Combine(projectRoot, "patch.txt"), "artifact\n");
        File.WriteAllText(Path.Combine(projectRoot, ".ccWorkbench.chat-history.json"), "{}\n");
        File.WriteAllText(Path.Combine(projectRoot, ".ccWorkbench.chat-history.2026.json"), "{}\n");
        File.WriteAllText(Path.Combine(projectRoot, "cc_chat_export_20260602.md"), "# Chat export\n");
        File.WriteAllText(Path.Combine(projectRoot, "old-bug-hunt-notes.md"), "# Bug hunt\n");
        File.WriteAllText(Path.Combine(projectRoot, "ps1-nat-artifact.ps1"), "Write-Host 'artifact'\n");
        var generatedPath = Path.Combine(projectRoot, ".ccWorkbench.generated-projects", "snake", "GeneratedSnake.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(generatedPath)!);
        File.WriteAllText(generatedPath, "public sealed class GeneratedSnake { }\n");
        var generatedFolderPath = Path.Combine(projectRoot, "generated", "HarnessProject.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(generatedFolderPath)!);
        File.WriteAllText(generatedFolderPath, "public sealed class HarnessProject { }\n");
        var testHarnessPath = Path.Combine(projectRoot, "test-harness", "HarnessProject.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(testHarnessPath)!);
        File.WriteAllText(testHarnessPath, "public sealed class TestHarnessProject { }\n");
        File.WriteAllText(Path.Combine(projectRoot, "docs", "readme.md"), "# Docs\n");
        for (var index = 0; index < 805; index++)
        {
            var bulkPath = Path.Combine(projectRoot, "bulk", $"file{index:D3}.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(bulkPath)!);
            File.WriteAllText(bulkPath, $"bulk {index}");
        }

        var profilePath = Path.Combine(projectRoot, ".ccDirProfile.json");
        RunCcDir(repoRoot, projectRoot, Path.Combine(projectRoot, "profile_only_output.md"), ["-ProfileOnly", "-ProfileOutput", profilePath]);
        if (!File.Exists(profilePath))
        {
            throw new InvalidOperationException("ccDir -ProfileOnly should create .ccDirProfile.json.");
        }

        var profileJson = JsonNode.Parse(File.ReadAllText(profilePath))?.AsObject()
            ?? throw new InvalidOperationException("DIR profile should be valid JSON.");
        if ((int?)profileJson["SchemaVersion"] != 1
            || (int?)profileJson["VisibleFileCount"] is null
            || profileJson["Roots"] is null
            || profileJson["Files"] is null
            || profileJson["Families"] is null)
        {
            throw new InvalidOperationException("DIR profile should expose schema, visible count, roots, files, and families.");
        }

        var output = Path.Combine(projectRoot, "cc_project_dir.md");
        RunCcDir(repoRoot, projectRoot, output, ["-Lod", "0", "-ProfileFile", profilePath]);
        var l0 = File.ReadAllText(output);
        RequireTextContains(l0, "CC-DIR-MANIFEST-V2");
        RequireTextContains(l0, "LOD: L0_GLOBAL");
        RequireTextContains(l0, "EXPAND: ");
        RequireTextContains(l0, "ccDir.ps1");
        RequireTextContains(l0, "lib/Cc.Dir.Export.ps1");
        RequireTextContains(l0, "lib/Cc.Export.Source.ps1");
        RequireTextContains(l0, "ROOT path=\"src/\"");
        RequireTextContains(l0, "ROOT path=\"ide/ContextControl.Workbench/Views/\" role=\"workbench views\"");
        RequireTextContains(l0, "ROOT path=\"ide/ContextControl.Workbench/Controls/\" role=\"workbench custom controls\"");
        RequireTextContains(l0, "ROOT path=\"ide/ContextControl.Workbench/Services/\" role=\"workbench services\"");
        RequireTextContains(l0, "ROOT path=\"ide/ContextControl.Workbench/Services/ContextControl/\"");
        RequireTextContains(l0, "ROOT path=\"ide/ContextControl.Workbench/Styles/\" role=\"workbench styling resources\"");
        RequireTextContains(l0, "ROOT path=\"ide/ContextControl.Workbench/ViewModels/\" role=\"workbench viewmodels\"");
        RequireTextContains(l0, "ROOT path=\"ide/ContextControl.Setup/\"");
        RequireTextContains(l0, "ROOT path=\"ide/ContextControl.Workbench.Tests/\"");
        RequireTextContains(l0, "FILE path=\"ide/ContextControl.Workbench/ContextControl.Workbench.csproj\" tier=L0 kind=\"csharp-project\" role=\"dotnet project file\" exports=\"full,find\"");
        RequireTextContains(l0, "FILE path=\"ide/ContextControl.Workbench/Views/MainWindow.axaml\" tier=L0 kind=\"avalonia-xaml\" role=\"main UI\" exports=\"full,find\"");
        RequireTextContains(l0, "FILE path=\"ide/ContextControl.Workbench/Views/MainWindow.axaml.cs\" tier=L0 kind=\"csharp\" role=\"main UI\" exports=\"full,function,find\"");
        RequireTextContains(l0, "FILE path=\"ide/ContextControl.Workbench/Styles/WorkbenchDesign.axaml\" tier=L0 kind=\"avalonia-xaml\" role=\"styling resource\" exports=\"full,find\"");
        RequireTextContains(l0, "FIND: ");
        RequireTextContains(l0, "FIND lines may repeat before END; do not mix FIND with source or EXPAND lines.");
        RequireTextContains(l0, "EXPAND must be the only request line before END.");
        RequireTextContains(l0, "FUNC may appear only with final source request lines.");
        var l0LineCount = l0.Split('\n').Length;
        if (l0LineCount > 120)
        {
            throw new InvalidOperationException($"L0 DIR manifest should stay compact; got {l0LineCount} lines.");
        }

        RequireUniversalDirExportQuality(AnalyzeUniversalDirExportQuality(
            l0,
            new DirExportQualityOptions
            {
                Name = "ContextControlSmoke",
                RelevantFileCount = 850
            }));

        RequireTextNotContains(l0, "├──");
        RequireTextNotContains(l0, "└──");
        RequireTextNotContains(l0, "```");
        RequireTextNotContains(l0, "patch.txt");
        RequireTextNotContains(l0, ".ccWorkbench.chat-history.json");
        RequireTextNotContains(l0, ".ccWorkbench.chat-history.2026.json");
        RequireTextNotContains(l0, "cc_chat_export_20260602.md");
        RequireTextNotContains(l0, "old-bug-hunt-notes.md");
        RequireTextNotContains(l0, "ps1-nat-artifact.ps1");
        RequireTextNotContains(l0, ".ccWorkbench.generated-projects");
        RequireTextNotContains(l0, "generated/HarnessProject.cs");
        RequireTextNotContains(l0, "test-harness/HarnessProject.cs");
        RequireTextNotContains(l0, "bulk/file000.txt");
        RequireTextNotContains(l0, "lib/export/Cc.Export.Source.ps1");
        RequireTextNotContains(l0, "SkillbookPage.axaml");
        RequireTextNotContains(l0, "ConversationAndSkillbook.axaml");
        RequireTextNotContains(l0, "role=\"csharp source file\"");
        RequireTextNotContains(l0, "FAMILY path=\"lib/Cc*.ps1\"");
        RequireTextNotContains(l0, ".ccDirProfile.json");

        var compactManifest = ContextDirManifestParser.Parse(l0);
        if (!compactManifest.Roots.Any(root => root.Path.Equals("bulk/", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Large L0 DIR manifests should include ROOT summaries for expandable areas.");
        }

        var scopedOutput = Path.Combine(projectRoot, "cc_project_dir_l1.md");
        RunCcDir(repoRoot, projectRoot, scopedOutput, ["-Lod", "1", "-Scope", "src/app"]);
        var l1 = File.ReadAllText(scopedOutput);
        RequireTextContains(l1, "LOD: L1_SCOPED");
        RequireTextContains(l1, "SCOPE: src/app/");
        RequireTextContains(l1, "FILE path=\"src/app/main.cpp\"");
        RequireTextContains(l1, "symbols=\"main,helper\"");
        RequireTextNotContains(l1, "docs/readme.md");
        RequireTextNotContains(l1, "├──");
        RequireTextNotContains(l1, "```");

        var ideScopedOutput = Path.Combine(projectRoot, "cc_project_dir_ide_l1.md");
        RunCcDir(repoRoot, projectRoot, ideScopedOutput, ["-Lod", "1", "-Scope", "ide/ContextControl.Workbench/Views/MainWindowParts/WorkspacePages"]);
        var ideL1 = File.ReadAllText(ideScopedOutput);
        RequireTextContains(ideL1, "LOD: L1_SCOPED");
        RequireTextContains(ideL1, "SCOPE: ide/ContextControl.Workbench/Views/MainWindowParts/WorkspacePages/");
        RequireTextContains(ideL1, "FILE path=\"ide/ContextControl.Workbench/Views/MainWindowParts/WorkspacePages/SkillbookPage.axaml\" tier=L1 kind=\"avalonia-xaml\" role=\"skillbook page layout\"");
        RequireTextNotContains(ideL1, "ConversationAndSkillbook.axaml");

        var beforeProfileCount = (int?)profileJson["VisibleFileCount"] ?? 0;
        File.WriteAllText(Path.Combine(projectRoot, "src", "app", "new_feature.cpp"), "int new_feature() { return 3; }\n");
        var refreshedProfilePath = Path.Combine(projectRoot, ".ccDirProfile.refreshed.json");
        RunCcDir(repoRoot, projectRoot, Path.Combine(projectRoot, "profile_only_refreshed.md"), ["-ProfileOnly", "-ProfileOutput", refreshedProfilePath]);
        var refreshedProfile = JsonNode.Parse(File.ReadAllText(refreshedProfilePath))?.AsObject()
            ?? throw new InvalidOperationException("Refreshed DIR profile should be valid JSON.");
        if (((int?)refreshedProfile["VisibleFileCount"] ?? 0) <= beforeProfileCount)
        {
            throw new InvalidOperationException("DIR profile refresh should reflect visible file-count changes.");
        }

        RunUniversalCcDirFixtureSmoke(
            repoRoot,
            "RustProject",
            [
                ("Cargo.toml", "[package]\nname = \"smoke\"\n"),
                ("Cargo.lock", "# lock\n"),
                ("src/main.rs", "fn main() {}\n"),
                ("src/lib.rs", "pub fn run() {}\n"),
                ("crates/core/src/lib.rs", "pub fn core() {}\n"),
                ("tests/integration.rs", "#[test]\nfn smoke() {}\n")
            ],
            [
                "ROOT path=\"src/\"",
                "ROOT path=\"crates/\"",
                "FILE path=\"Cargo.toml\" tier=L0 kind=\"toml\" role=\"rust package manifest\" exports=\"full,find\"",
                "FILE path=\"src/main.rs\" tier=L0 kind=\"rust\" role=\"runtime entrypoint\" exports=\"full,function,find\""
            ]);

        RunUniversalCcDirFixtureSmoke(
            repoRoot,
            "TypeScriptProject",
            [
                ("package.json", "{\"scripts\":{\"dev\":\"vite\"}}\n"),
                ("tsconfig.json", "{}\n"),
                ("vite.config.ts", "export default {}\n"),
                ("src/main.ts", "export function main() {}\n"),
                ("components/Button.tsx", "export function Button() { return null; }\n"),
                ("routes/index.ts", "export function route() {}\n"),
                ("server/api.ts", "export function handler() {}\n")
            ],
            [
                "ROOT path=\"src/\"",
                "ROOT path=\"components/\"",
                "ROOT path=\"routes/\"",
                "ROOT path=\"server/\"",
                "FILE path=\"package.json\" tier=L0 kind=\"json\" role=\"node package manifest\" exports=\"full,find\"",
                "FILE path=\"components/Button.tsx\" tier=L0 kind=\"typescript-react\""
            ]);

        RunUniversalCcDirFixtureSmoke(
            repoRoot,
            "JavaProject",
            [
                ("pom.xml", "<project />\n"),
                ("src/main/java/com/acme/Application.java", "class Application { public static void main(String[] args) {} }\n"),
                ("src/main/java/com/acme/Controller.java", "class Controller {}\n"),
                ("src/test/java/com/acme/ApplicationTest.java", "class ApplicationTest {}\n")
            ],
            [
                "ROOT path=\"src/\"",
                "FILE path=\"pom.xml\" tier=L0 kind=\"xml\" role=\"java maven manifest\" exports=\"full,find\"",
                "FILE path=\"src/main/java/com/acme/Application.java\" tier=L0 kind=\"java\""
            ]);

        RunUniversalCcDirFixtureSmoke(
            repoRoot,
            "GoProject",
            [
                ("go.mod", "module smoke\n"),
                ("go.sum", "\n"),
                ("cmd/server/main.go", "package main\nfunc main() {}\n"),
                ("internal/app/service.go", "package app\nfunc Run() {}\n"),
                ("pkg/api/api.go", "package api\nfunc Handler() {}\n")
            ],
            [
                "ROOT path=\"cmd/\"",
                "ROOT path=\"internal/\"",
                "ROOT path=\"pkg/\"",
                "FILE path=\"go.mod\" tier=L0 kind=\"go-module\" role=\"go module manifest\" exports=\"full,find\"",
                "FILE path=\"cmd/server/main.go\" tier=L0 kind=\"go\" role=\"runtime entrypoint\" exports=\"full,function,find\""
            ]);

        RunUniversalCcDirFixtureSmoke(
            repoRoot,
            "PythonProject",
            [
                ("pyproject.toml", "[project]\nname = \"smoke\"\n"),
                ("src/smoke/__main__.py", "def main(): pass\n"),
                ("src/smoke/app.py", "def app(): pass\n"),
                ("tests/test_app.py", "def test_app(): pass\n"),
                ("scripts/cli.py", "def cli(): pass\n")
            ],
            [
                "ROOT path=\"src/\"",
                "ROOT path=\"tests/\"",
                "ROOT path=\"scripts/\"",
                "FILE path=\"pyproject.toml\" tier=L0 kind=\"toml\" role=\"python project manifest\" exports=\"full,find\"",
                "FILE path=\"src/smoke/__main__.py\" tier=L0 kind=\"python\" role=\"runtime entrypoint\" exports=\"full,function,find\""
            ]);

        RunUniversalCcDirFixtureSmoke(
            repoRoot,
            "ContextControlWithoutProductCoreProject",
            [
                ("cc.ps1", "function Invoke-Cc { }\n"),
                ("ccDir.ps1", "function Invoke-CcDir { }\n"),
                ("ccReplace.ps1", "function Invoke-CcReplace { }\n"),
                ("lib/Cc.Dir.Export.ps1", "function Invoke-CcDirExport { }\n"),
                ("lib/Cc.Replace.Apply.ps1", "function Invoke-CcReplaceApply { }\n"),
                ("ide/ContextControl.Setup/ContextControl.Setup.csproj", "<Project />\n"),
                ("ide/ContextControl.Setup/Program.cs", "public static class Program { public static void Main() { } }\n"),
                ("ide/ContextControl.Workbench.Tests/ContextControl.Workbench.Tests.csproj", "<Project />\n"),
                ("ide/ContextControl.Workbench.Tests/Program.cs", "public static class TestsProgram { public static void Main() { } }\n"),
                ("ide/ContextControl.Workbench/ContextControl.Workbench.csproj", "<Project />\n"),
                ("ide/ContextControl.Workbench/Services/ProjectService.cs", "public sealed class ProjectService { }\n"),
                ("ide/ContextControl.Workbench/Views/MainWindow.axaml", "<Window />\n")
            ],
            [
                "ROOT path=\"ide/ContextControl.Setup/\"",
                "ROOT path=\"ide/ContextControl.Workbench.Tests/\"",
                "FILE path=\"ccDir.ps1\""
            ],
            [
                "ROOT path=\"ide/ContextControl.Workbench/Services/ContextControl/\""
            ]);

        var shaderInvariantManifest = RunUniversalCcDirFixtureSmoke(
            repoRoot,
            "ShaderRootInvariantProject",
            [
                ("CMakeLists.txt", "add_executable(shader-invariant src/main.cpp)\n"),
                ("src/main.cpp", "int main() { return 0; }\n"),
                ("shaders/common.glsl", "void main() {}\n"),
                ("shaders/fullscreen.vert", "void main() {}\n"),
                ("shaders/lighting.frag", "void main() {}\n"),
                ("shaders/compute.comp", "void main() {}\n")
            ],
            [
                "CC-DIR-MANIFEST-V2",
                "LOD: L0_GLOBAL"
            ]);
        RequireShaderRootInvariant(shaderInvariantManifest);

        var cppEngineManifest = RunUniversalCcDirFixtureSmoke(
            repoRoot,
            "CppEngineProject",
            [
                ("CMakeLists.txt", "add_executable(smoke src/main.cpp)\n"),
                ("src/main.cpp", "int main() { return 0; }\n"),
                ("src/rendering/Renderer.cpp", "void render() {}\n"),
                ("src/rendering/RenderGraph.cpp", "void render_graph() {}\n"),
                ("src/rendering/RenderPipeline.cpp", "void render_pipeline() {}\n"),
                ("src/rendering/RenderPass.cpp", "void render_pass() {}\n"),
                ("src/rendering/RenderQueue.cpp", "void render_queue() {}\n"),
                ("src/world/World.cpp", "void world() {}\n"),
                ("src/world/WorldChunks.cpp", "void world_chunks() {}\n"),
                ("src/world/WorldStreaming.cpp", "void world_streaming() {}\n"),
                ("src/world/WorldTerrain.cpp", "void world_terrain() {}\n"),
                ("src/world/WorldUpdate.cpp", "void world_update() {}\n"),
                ("include/core/Engine.h", "struct Engine {};\n"),
                ("shaders/common.glsl", "void main() {}\n"),
                ("tools/importer.cpp", "int main() { return 0; }\n"),
                ("vendor/ignored.cpp", "int ignored() { return 0; }\n"),
                ("build/generated.cpp", "int generated() { return 0; }\n")
            ],
            [
                "ROOT path=\"src/\"",
                "ROOT path=\"include/\"",
                "ROOT path=\"shaders/\"",
                "ROOT path=\"tools/\"",
                "ROOT path=\"src/rendering/\"",
                "ROOT path=\"src/world/\"",
                "FILE path=\"CMakeLists.txt\" tier=L0 kind=\"cmake\" role=\"cmake build configuration\" exports=\"full,find\"",
                "FILE path=\"shaders/common.glsl\" tier=L0 kind=\"shader\"",
                "FILE path=\"src/main.cpp\" tier=L0 kind=\"cpp\" role=\"runtime entrypoint\" exports=\"full,function,find\""
            ],
            [
                "vendor/ignored.cpp",
                "build/generated.cpp"
            ]);
        RequireBalancedRouterQuality(cppEngineManifest, expectAsset: true, isTinyProject: true);

        var vulkanSplitManifest = RunUniversalCcDirFixtureSmoke(
            repoRoot,
            "VulkanSplitFamilyProject",
            [
                ("CMakeLists.txt", "add_executable(vulkanas src/main.cpp)\n"),
                ("src/main.cpp", "int main() { return 0; }\n"),
                ("src/player/Player.cpp", "void player() {}\n"),
                ("src/player/PlayerInput.cpp", "void player_input() {}\n"),
                ("src/player/PlayerCamera.cpp", "void player_camera() {}\n"),
                ("src/rendering/culling/GPUCulling.cpp", "void gpu_culling() {}\n"),
                ("src/rendering/culling/GPUCullingBuild.cpp", "void gpu_culling_build() {}\n"),
                ("src/rendering/culling/GPUCullingDispatch.cpp", "void gpu_culling_dispatch() {}\n"),
                ("src/vulkan/VulkanDevice.cpp", "void vulkan_device() {}\n"),
                ("include/vulkan/VulkanDevice.h", "struct VulkanDevice {};\n"),
                ("shaders/terrain/terrain.frag", "void main() {}\n"),
                ("shaders/terrain/terrain.vert", "void main() {}\n"),
                ("shaders/culling/cull.comp", "void main() {}\n"),
                ("tools/shader_pack.cpp", "int main() { return 0; }\n")
            ],
            [
                "ROOT path=\"src/player/\"",
                "ROOT path=\"src/rendering/culling/\"",
                "ROOT path=\"shaders/terrain/\"",
                "ROOT path=\"shaders/culling/\"",
                "ROOT path=\"src/vulkan/\"",
                "ROOT path=\"include/vulkan/\"",
                "ROOT path=\"tools/\"",
                "FILE path=\"tools/shader_pack.cpp\"",
                "FAMILY path=\"src/player/Player*.cpp\"",
                "FAMILY path=\"src/rendering/culling/GPUCulling*.cpp\""
            ],
            qualityOptions: new DirExportQualityOptions
            {
                Name = "VulkanSplitFamilyProject",
                ExpectAsset = true,
                StrictRoleSpecificity = true
            });
        RequireBalancedRouterQuality(vulkanSplitManifest, expectAsset: true, isTinyProject: true);

        var tonManifest = RunUniversalCcDirFixtureSmoke(
            repoRoot,
            "TonLikeProject",
            [
                ("CMakeLists.txt", "add_executable(ton src/runtime/main.cpp)\n"),
                ("src/runtime/main.cpp", "int main() { return 0; }\n"),
                ("src/runtime/Node.cpp", "void node() {}\n"),
                ("protocol/BlockParser.cpp", "void parse_block() {}\n"),
                ("protocol/MessageCodec.cpp", "void encode_message() {}\n"),
                ("storage/CellStore.cpp", "void cell_store() {}\n"),
                ("storage/ArchiveStore.cpp", "void archive_store() {}\n"),
                ("include/ton/Node.h", "struct Node {};\n"),
                ("test/NodeTest.cpp", "void test_node() {}\n"),
                (".github/workflows/ci.yml", "name: ci\n"),
                ("assembly/package.ps1", "function Invoke-Package { }\n"),
                ("docs/architecture.md", "# Architecture\n"),
                ("generated/Generated.cpp", "void generated() {}\n"),
                ("vendor/ThirdParty.cpp", "void vendor() {}\n")
            ],
            [
                "ROOT path=\"src/\"",
                "ROOT path=\"protocol/\"",
                "ROOT path=\"storage/\"",
                "ROOT path=\"include/\"",
                "ROOT path=\"test/\"",
                "ROOT path=\".github/\"",
                "ROOT path=\"assembly/\"",
                "FILE path=\"CMakeLists.txt\" tier=L0 kind=\"cmake\" role=\"cmake build configuration\" exports=\"full,find\"",
                "FILE path=\"src/runtime/main.cpp\" tier=L0 kind=\"cpp\" role=\"runtime entrypoint\" exports=\"full,function,find\""
            ],
            [
                "generated/Generated.cpp",
                "vendor/ThirdParty.cpp"
            ]);
        RequireRootBefore(tonManifest, "src/", "test/");
        RequireRootBefore(tonManifest, "protocol/", "test/");
        RequireRootBefore(tonManifest, "storage/", ".github/");
        RequireRootBefore(tonManifest, "include/", "assembly/");
        RequireBalancedRouterQuality(tonManifest, expectAsset: false);

        var tonSparseFiles = new List<(string Path, string Text)>
        {
            ("CMakeLists.txt", "add_executable(ton lite-client/lite-client.cpp)\n"),
            ("lite-client/lite-client.cpp", "int main() { return 0; }\n"),
            ("validator/validator.cpp", "void validator() {}\n"),
            ("crypto/vm/vm.cpp", "void vm() {}\n"),
            ("tonlib/tonlib.cpp", "void tonlib() {}\n"),
            ("storage/storage.cpp", "void storage() {}\n"),
            ("dht/dht.cpp", "void dht() {}\n"),
            ("adnl/adnl.cpp", "void adnl() {}\n"),
            ("overlay/overlay-manager.cpp", "void overlay_manager() {}\n"),
            ("quic/quic-server.cpp", "void quic_server() {}\n"),
            ("http/http-server.cpp", "void http_server() {}\n"),
            ("tdnet/td/net/UdpServer.cpp", "void udp_server() {}\n"),
            ("dht-server/dht-server.cpp", "void dht_server() {}\n"),
            ("include/ton/ton.h", "struct Ton {};\n"),
            ("test/ton-test.cpp", "void test_ton() {}\n"),
            ("docs/readme.md", "# TON\n")
        };
        for (var index = 0; index < 52; index++)
        {
            tonSparseFiles.Add(($"module{index:D2}/CMakeLists.txt", $"add_library(module{index:D2} lite-client/lite-client.cpp)\n"));
        }

        var tonSparseManifest = RunUniversalCcDirFixtureSmoke(
            repoRoot,
            "TonSparseSubsystemProject",
            tonSparseFiles.ToArray(),
            [
                "ROOT path=\"quic/\"",
                "ROOT path=\"overlay/\"",
                "ROOT path=\"http/\"",
                "ROOT path=\"tdnet/\"",
                "ROOT path=\"dht-server/\"",
                "FILE path=\"quic/quic-server.cpp\"",
                "FILE path=\"overlay/overlay-manager.cpp\"",
                "FILE path=\"http/http-server.cpp\"",
                "FILE path=\"tdnet/td/net/UdpServer.cpp\"",
                "FILE path=\"dht-server/dht-server.cpp\""
            ],
            [
                "module00/CMakeLists.txt",
                "module51/CMakeLists.txt"
            ]);
        RequireUniversalDirExportQuality(AnalyzeUniversalDirExportQuality(
            tonSparseManifest,
            new DirExportQualityOptions
            {
                Name = "TonSparseSubsystemProject",
                RelevantFileCount = tonSparseFiles.Count
            }));

        var largeCppFiles = new List<(string Path, string Text)>
        {
            ("CMakeLists.txt", "add_executable(large src/main.cpp)\n"),
            ("src/main.cpp", "int main() { return 0; }\n"),
            ("shaders/large_common.frag", "void main() {}\n")
        };
        for (var index = 0; index < 60; index++)
        {
            largeCppFiles.Add(($"config/module{index:D2}/CMakeLists.txt", $"add_library(module{index:D2} src/core/Engine{index:D2}.cpp)\n"));
            largeCppFiles.Add(($"src/core/Engine{index:D2}.cpp", $"void Engine{index:D2}() {{ }}\n"));
            largeCppFiles.Add(($"notes/note{index:D2}.txt", $"note {index}\n"));
        }

        var largeCppManifest = RunUniversalCcDirFixtureSmoke(
            repoRoot,
            "LargeCppBudgetProject",
            largeCppFiles.ToArray(),
            [
                "ROOT path=\"src/\"",
                "ROOT path=\"shaders/\"",
                "FILE path=\"CMakeLists.txt\" tier=L0 kind=\"cmake\" role=\"cmake build configuration\" exports=\"full,find\"",
                "FILE path=\"src/main.cpp\" tier=L0 kind=\"cpp\" role=\"runtime entrypoint\" exports=\"full,function,find\"",
                "FILE path=\"shaders/large_common.frag\" tier=L0 kind=\"shader\""
            ]);
        RequireAnchorBudget(largeCppManifest);

        var balancedFiles = new List<(string Path, string Text)>
        {
            ("CMakeLists.txt", "add_executable(router src/runtime/main.cpp)\n"),
            ("src/runtime/main.cpp", "int main() { return 0; }\n"),
            ("include/router/Api.h", "struct Api {};\n"),
            ("shaders/router.frag", "void main() {}\n"),
            ("tools/importer.cpp", "int main() { return 0; }\n"),
            ("tests/router_test.cpp", "void test_router() {}\n"),
            (".github/workflows/ci.yml", "name: ci\n"),
            ("docs/architecture.md", "# Architecture\n")
        };
        string[] balancedRoots = ["runtime", "rendering", "world", "network"];
        foreach (var rootName in balancedRoots)
        {
            for (var index = 0; index < 20; index++)
            {
                var typeName = rootName switch
                {
                    "runtime" => $"RuntimeManager{index:D2}",
                    "rendering" => $"RenderSystem{index:D2}",
                    "world" => $"WorldManager{index:D2}",
                    _ => $"NetworkRouter{index:D2}"
                };
                balancedFiles.Add(($"src/{rootName}/{typeName}.cpp", $"void {typeName}() {{ }}\n"));
            }
        }

        for (var index = 0; index < 60; index++)
        {
            balancedFiles.Add(($"config/module{index:D2}/CMakeLists.txt", $"add_library(module{index:D2} src/runtime/RuntimeManager00.cpp)\n"));
        }

        var balancedManifestA = RunUniversalCcDirFixtureSmoke(
            repoRoot,
            "BalancedConflictProjectA",
            balancedFiles.ToArray(),
            [
                "ROOT path=\"src/\"",
                "ROOT path=\"src/rendering/\"",
                "ROOT path=\"src/runtime/\"",
                "ROOT path=\"src/world/\"",
                "ROOT path=\"include/\"",
                "ROOT path=\"shaders/\"",
                "FILE path=\"shaders/router.frag\" tier=L0 kind=\"shader\""
            ]);
        var balancedManifestB = RunUniversalCcDirFixtureSmoke(
            repoRoot,
            "BalancedConflictProjectB",
            balancedFiles.ToArray(),
            [
                "CC-DIR-MANIFEST-V2",
                "LOD: L0_GLOBAL"
            ]);
        if (!balancedManifestA.Equals(balancedManifestB, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Balanced L0 router output should be deterministic across repeated runs with the same relative project shape.");
        }

        RequireBalancedRouterQuality(
            balancedManifestA,
            expectAsset: true);

        RunUniversalCcDirFixtureSmoke(
            repoRoot,
            "DeepNestedUiProject",
            [
                ("package.json", "{\"scripts\":{\"dev\":\"vite\"}}\n"),
                ("src/main.ts", "export function main() {}\n"),
                ("src/views/dashboard/DashboardPage.tsx", "export function DashboardPage() { return null; }\n"),
                ("src/views/dashboard/DashboardChart.tsx", "export function DashboardChart() { return null; }\n"),
                ("src/components/Button.tsx", "export function Button() { return null; }\n"),
                ("src/styles/theme.css", ".root {}\n")
            ],
            [
                "FILE path=\"src/views/dashboard/DashboardPage.tsx\"",
                "FILE path=\"src/components/Button.tsx\""
            ]);

        RunUniversalCcDirFixtureSmoke(
            repoRoot,
            "DocsHeavyProject",
            [
                ("README.md", "# Docs heavy\n"),
                ("docs/architecture.md", "# Architecture\n"),
                ("docs/api.md", "# API\n"),
                ("docs/install.md", "# Install\n"),
                ("src/main.py", "def main(): pass\n")
            ],
            [
                "FILE path=\"README.md\"",
                "FILE path=\"src/main.py\""
            ],
            qualityOptions: new DirExportQualityOptions
            {
                Name = "DocsHeavyProject",
                AllowDocsHeavy = true
            });

        RunUniversalCcDirFixtureSmoke(
            repoRoot,
            "ToolsHeavyProject",
            [
                ("README.md", "# Tools\n"),
                ("scripts/build.ps1", "function Invoke-Build { }\n"),
                ("scripts/release.ps1", "function Invoke-Release { }\n"),
                ("scripts/audit.ps1", "function Invoke-Audit { }\n"),
                ("tools/importer.py", "def main(): pass\n")
            ],
            [
                "ROOT path=\"scripts/\"",
                "FILE path=\"scripts/build.ps1\""
            ],
            qualityOptions: new DirExportQualityOptions
            {
                Name = "ToolsHeavyProject",
                AllowToolsHeavy = true
            });

        RunUniversalCcDirFixtureSmoke(
            repoRoot,
            "OwnedToolsAcronymProject",
            [
                ("CMakeLists.txt", "add_executable(tools src/main.cpp)\n"),
                ("src/main.cpp", "int main() { return 0; }\n"),
                ("src/crypto/vm/CryptoVM.cpp", "void crypto_vm() {}\n"),
                ("src/td/utility/TdUtilityCore.cpp", "void td_utility_core() {}\n"),
                ("tools/convert_heightmap_to_meshes.cpp", "int main() { return 0; }\n"),
                ("tools/dccm/DCCMAOSmoothing.cpp", "void dccm_ao_smoothing() {}\n")
            ],
            [
                "ROOT path=\"tools/\"",
                "FILE path=\"tools/convert_heightmap_to_meshes.cpp\" tier=L0 kind=\"cpp\" role=\"heightmap mesh converter\"",
                "FILE path=\"tools/dccm/DCCMAOSmoothing.cpp\" tier=L0 kind=\"cpp\" role=\"dccm ao smoothing source\"",
                "FILE path=\"src/crypto/vm/CryptoVM.cpp\" tier=L0 kind=\"cpp\" role=\"crypto vm source\"",
                "FILE path=\"src/td/utility/TdUtilityCore.cpp\" tier=L0 kind=\"cpp\" role=\"td utility core source\""
            ],
            qualityOptions: new DirExportQualityOptions
            {
                Name = "OwnedToolsAcronymProject",
                StrictRoleSpecificity = true
            });

        RunUniversalCcDirFixtureSmoke(
            repoRoot,
            "ProfileThirdPartyToolsProject",
            [
                (".contextcontrol/dir-profile.json",
                    """
                    {
                      "thirdPartyRoots": [ "tools/" ],
                      "preferredRepresentativeRoots": [ "src/" ]
                    }
                    """),
                ("CMakeLists.txt", "add_executable(profile-tools src/main.cpp)\n"),
                ("src/main.cpp", "int main() { return 0; }\n"),
                ("src/core/Engine.cpp", "void engine() {}\n"),
                ("tools/vendor_tool.cpp", "int vendor_tool() { return 0; }\n"),
                ("third_party/tools/also_ignored.cpp", "int ignored() { return 0; }\n")
            ],
            [
                "ROOT path=\"src/\"",
                "FILE path=\"src/main.cpp\""
            ],
            [
                "ROOT path=\"tools/\"",
                "tools/vendor_tool.cpp",
                "third_party/tools/also_ignored.cpp"
            ]);

        RunUniversalCcDirFixtureSmoke(
            repoRoot,
            "ShaderClusterMutationProject",
            [
                ("CMakeLists.txt", "add_executable(shader src/main.cpp)\n"),
                ("src/main.cpp", "int main() { return 0; }\n"),
                ("shaders/terrain/terrain.frag", "void main() {}\n"),
                ("shaders/terrain/terrain.vert", "void main() {}\n"),
                ("shaders/culling/cull.comp", "void main() {}\n")
            ],
            [
                "ROOT path=\"shaders/terrain/\"",
                "ROOT path=\"shaders/culling/\""
            ],
            qualityOptions: new DirExportQualityOptions
            {
                Name = "ShaderClusterMutationProject",
                ExpectAsset = true
            });
    }
    finally
    {
        try
        {
            if (Directory.Exists(projectRoot))
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup only; temp leftovers must not fail resolver checks.
        }
    }
}
}
