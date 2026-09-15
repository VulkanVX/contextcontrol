using System.Diagnostics;
using System.Reflection;
using ContextControl.Workbench.Services;
using ContextControl.Workbench.ViewModels;

internal static class ProjectScannerTests
{
    public static void Run(string? realProject = null)
    {
        if (realProject is not null)
        {
            var watch = Stopwatch.StartNew();
            var result = ProjectStackScanner.Scan(realProject, ProjectFileRules.Load(realProject));
            Console.WriteLine($"Scanner: {result.Files.Count:N0} files, {result.Files.Count(file => file.IsCode):N0} code, complete={result.IsComplete}, {watch.Elapsed.TotalSeconds:N2}s");
            Console.WriteLine(result.Summary);
            foreach (var notice in result.Notices.Take(20)) Console.WriteLine(notice);
            return;
        }
        var root = Path.Combine(Path.GetTempPath(), "ContextControlScannerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var checks = 0;
        void Check(bool pass, string message) { if (!pass) throw new InvalidOperationException(message); checks++; }
        void Write(string path, string text = "") => SmokeTestHelpers.WriteFile(root, path, text);
        try
        {
            var sourcePaths = new[] { "src/main.cpp", "include/impl.inl", "include/interface.inc", "include/module.ixx",
                "contracts/main.fc", "contracts/schema.tlb", "contracts/helpers.fif", "contracts/wallet.tolk",
                "shaders/water.gdshader", "shaders/path.rchit", "src/empty.cs", "bin/user-tool.py", "build/user-build.ps1",
                "nested/src/child.rs", "src/.hidden.ts", "Features/Dependencies/Installer.cs", "Dockerfile", "CMakeLists.txt", "scripts/run" };
            foreach (var path in sourcePaths) Write(path, path == "scripts/run" ? "#!/usr/bin/env python3\nprint('fixture')\n" : "");
            File.SetAttributes(Path.Combine(root, "src/.hidden.ts"), FileAttributes.Hidden);
            Write("nested/.git", "gitdir: nowhere");
            Write("src/hand-excluded.cs", "// intentionally excluded");
            Write("private-archive/unused.cs", "// intentionally excluded folder");
            Write("config/tool.customconfig", "key=value\n");
            Write("src/notes.md", "not code\n");
            Write("node_modules/fake/package.json", "{\"dependencies\":{\"next\":\"99.0\"}}");
            Write("node_modules/fake/dependency.ts", "// must never parse or list this");
            Write("third_party/lib/lib.cpp", "");
            Write("generated-build/CMakeCache.txt", "generated");
            Write("generated-build/CMakeFiles/out.cpp", "generated");
            Write("dotnet/obj/project.assets.json", "{}");
            Write("dotnet/obj/Release/AssemblyInfo.cs", "// generated");
            Write("dotnet/bin/Release/app.runtimeconfig.json", "{}");
            Write("dotnet/bin/Release/app.deps.json", "{}");
            Write("dotnet/bin/Release/generated.cs", "// generated");
            Write(".git/objects/metadata.cs", "not source");
            var deep = string.Join('/', Enumerable.Repeat("level", 25)) + "/deep.go";
            Write(deep, "package main\n");
            var rules = ProjectFileRules.Load(root);
            rules.UpdateRules(rules.IgnoredDirectoriesText + "\nprivate-archive", rules.IgnoredFileNamesText + "\nsrc/hand-excluded.cs",
                rules.IgnoredExtensionsText, rules.SupportedExtensionsText + "\n.customconfig", rules.LocExtensionsText);
            rules.SkipExtension(".fc");
            var result = ProjectStackScanner.Scan(root, rules);
            Check(result.IsComplete, "Fixture scan should finish completely.");
            foreach (var path in sourcePaths.Append(deep)) Check(result.Files.Any(file => file.RelativePath == path && file.IsCode), $"Missing source inventory path: {path}");
            Check(!result.Files.Any(file => file.RelativePath.StartsWith("node_modules/") || file.RelativePath.StartsWith("third_party/") || file.RelativePath.StartsWith("generated-build/")), "Dependencies and generated build roots must remain unparsed.");
            Check(!result.StackLabel.Contains("Next.js"), "Dependency manifest must not define the user's project stack.");
            Check(!result.Files.Any(file => file.RelativePath.StartsWith("dotnet/obj/") || file.RelativePath.StartsWith("dotnet/bin/Release/")), "Generated .NET source must stay out of the user's source inventory.");
            Check(result.Notices.Any(value => value.Contains("node_modules") && value.Contains("dependency")), "Skipped packages must be explained.");
            Check(result.Files.Single(file => file.RelativePath == "contracts/main.fc").Status == "Hidden", "Disabled source types should retain their exclusion reason.");
            typeof(WorkbenchViewModel).GetMethod("ApplyAutoSetupRules", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [rules, result.AutoSetupRules]);
            rules.Save();
            var saved = ProjectFileRules.Load(root);
            foreach (var path in sourcePaths.Append(deep)) Check(saved.GetTrackDecision(Path.Combine(root, path)).ShouldTrack, $"Saved autosetup rules still hide {path}");
            Check(!saved.GetTrackDecision(Path.Combine(root, "src/hand-excluded.cs")).ShouldTrack, "Explicit skipped files must be preserved.");
            Check(saved.GetTrackDecision(Path.Combine(root, "config/tool.customconfig")).ShouldTrack && !saved.ShouldCountLocExtension(".customconfig"), "Custom allowed types must survive without forcing LOC.");
            Check(saved.ShouldSkipDirectory("node_modules") && saved.ShouldSkipDirectory("third_party"), "Autosetup must save package exclusions.");
            Check(saved.ShouldSkipDirectory("private-archive") && result.Files.All(file => !file.RelativePath.StartsWith("private-archive/")), "Custom skipped folders must remain excluded from parsing and autosetup.");
            var loaded = ProjectLoader.Load(root);
            IEnumerable<ProjectNodeViewModel> Flatten(IEnumerable<ProjectNodeViewModel> nodes) => nodes.SelectMany(node => new[] { node }.Concat(Flatten(node.Children)));
            var treeFiles = Flatten(loaded.Tree).Where(node => node.IsFile).ToDictionary(node => node.Path);
            foreach (var path in sourcePaths.Append(deep)) Check(treeFiles.ContainsKey(path), $"Project loader still hides saved source: {path}");
            Check(treeFiles["src/empty.cs"].Loc == 0, "Empty source must remain visible with zero LOC.");
            Check(treeFiles.ContainsKey("config/tool.customconfig") && treeFiles["config/tool.customconfig"].Loc == 0, "Inclusion must not depend on line counting.");
            Check(!treeFiles.Keys.Any(path => path.StartsWith("node_modules/") || path.StartsWith("third_party/")), "Project tree must retain dependency exclusions.");
            var again = ProjectStackScanner.Scan(root, saved);
            Check(again.AutoSetupRules.SupportedExtensions.SequenceEqual(result.AutoSetupRules.SupportedExtensions), "Autosetup should stabilize after saving.");
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            try { ProjectStackScanner.Scan(root, saved, cancelled.Token); throw new Exception("Cancelled scan returned a completed result."); }
            catch (OperationCanceledException) { checks++; }
            Check(!ProjectStackScanner.Scan(Path.Combine(root, "missing"), saved).IsComplete, "Missing project must not produce an applicable clean ruleset.");
            for (var index = 0; index < 50005; index++) Write($"many/{index / 5:D5}/{index:D5}.inl");
            var watch = Stopwatch.StartNew();
            var large = ProjectStackScanner.Scan(root, saved);
            Check(large.IsComplete && large.Files.Count(file => file.RelativePath.StartsWith("many/")) == 50005,
                "Large source inventory must not stop at the old file/directory limits.");
            Console.WriteLine($"Project scanner regression passed: {checks} checks; large inventory {large.Files.Count:N0} files in {watch.Elapsed.TotalSeconds:N2}s.");
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
