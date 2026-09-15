using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ContextControl.Workbench.Services;
using ContextControl.Workbench.ViewModels;
using ContextControl.Workbench.Views;
using ContextControl.Workbench.Views.MainWindowParts;

internal static partial class UiExperienceTests
{
    private static void ProjectScannerAndRules(WorkbenchViewModel workbench, string root, string output)
    {
        void PumpUntil(Func<bool> ready)
        {
            var watch = Stopwatch.StartNew();
            while (!ready() && watch.Elapsed < TimeSpan.FromSeconds(15)) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(10); }
            Check(ready(), "Project operation must complete.");
        }
        var projectRoot = Path.Combine(root, "scanner-project");
        SmokeTestHelpers.WriteFile(projectRoot, "contracts/wallet.fc", "() recv_internal() {}\n");
        SmokeTestHelpers.WriteFile(projectRoot, "include/math.inl", "inline int add(int a, int b) { return a+b; }\n");
        SmokeTestHelpers.WriteFile(projectRoot, "node_modules/dependency/index.ts", "// dependency\n");
        var projectRules = ProjectFileRules.Load(projectRoot);
        projectRules.SkipExtension(".fc"); projectRules.Save();
        var open = workbench.LoadProjectAsync(projectRoot);
        PumpUntil(() => open.IsCompleted); open.GetAwaiter().GetResult();
        PumpUntil(() => !workbench.IsProjectScanRunning);
        var autosetup = (Task)Call(workbench, "AutoSetupProjectRulesAsync")!;
        PumpUntil(() => autosetup.IsCompleted); autosetup.GetAwaiter().GetResult();
        Check(ProjectFileRules.Load(projectRoot).GetTrackDecision(Path.Combine(projectRoot, "contracts/wallet.fc")).ShouldTrack,
            "The real Autosetup action must save the restored source rule to this project's file.");
        Check(workbench.ProjectScanFiles.Any(file => file.RelativePath == "contracts/wallet.fc" && file.IsTracked)
            && !workbench.ProjectScanFiles.Any(file => file.RelativePath.StartsWith("node_modules/")),
            "Autosetup's final inventory must show the saved rules and exclude packages.");
        var result = new ProjectStackScanResult("C++ · 5,000 source files", "Inventory fixture", root, "C++", "Project rules", "Complete",
            [new("Code", "5,000", "source files"), new("Excluded", "3", "dependency directories")],
            [new("Languages", ["C++: 4,999", "FunC: 1"]), new("Autosetup Plan", ["Restore .fc source files"])], ProjectStackRuleSet.Empty())
        {
            Files = Enumerable.Range(0, 5000).Select(index => new ProjectScanFile(index == 12 ? "contracts/wallet.fc" : $"src/module{index:D4}/Source.cpp",
                index == 12 ? "FunC" : "C++", "Project", true, index != 12, index == 12 ? "ignored extension: .fc" : "", 2048)).ToArray(),
            Notices = ["node_modules: dependency packages", "third_party: dependency packages", ".git: metadata/cache"]
        };
        workbench.UiFontSize = 11;
        Call(workbench, "SwitchWorkspaceMode", workbench.WorkspaceModes.Single(mode => mode.Key == "scanner"));
        PumpUntil(() => !workbench.IsProjectScanRunning);
        Call(workbench, "ApplyProjectScanResult", result, "Autosetup saves only this project's rules.");
        void WaitFor(Func<bool> complete)
        {
            var deadline = Stopwatch.StartNew();
            while (!complete() && deadline.Elapsed < TimeSpan.FromSeconds(5)) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(10); }
            Check(complete(), "Scanner filter should settle promptly.");
        }
        WaitFor(() => workbench.ProjectScanFilteredFiles.Count == 5000);
        var page = new ProjectScannerPage { DataContext = workbench };
        var window = new Window { Content = page, DataContext = workbench };
        WorkbenchThemeResources.Apply(window, "studio");
        Snapshot(window, 1120, 820, Path.Combine(output, "scanner-inventory.png"));
        var list = page.FindControl<ListBox>("ScannerFiles")!;
        Check(list.GetVisualDescendants().OfType<ListBoxItem>().Count() < 100, "Large scanner inventory must virtualize rows.");
        workbench.ProjectScanSearchText = "wallet.fc";
        WaitFor(() => workbench.ProjectScanFilteredFiles.Count == 1);
        Check(workbench.ProjectScanFilteredFiles[0].Language == "FunC", "Inventory search must retain a missing niche-language source.");
        workbench.SelectedProjectScanFile = workbench.ProjectScanFilteredFiles[0];
        Snapshot(window, 860, 720, Path.Combine(output, "scanner-filter.png"));
        Check(list.Bounds.Height > 100, "File results should have usable height in a smaller window.");
        window.Close();
        workbench.ProjectScanSearchText = "";
        workbench.ProjectScanFilter = "Hidden code";
        WaitFor(() => workbench.ProjectScanFilteredFiles.Count == 1);
        Check(workbench.ProjectScanFilteredFiles[0].HiddenReason.Contains(".fc"), "Hidden source filter must show its exact rule.");
        workbench.ProjectScanFilter = "All code";
        var settings = new ThemeSettingsWindow { DataContext = workbench };
        settings.ApplyTheme("studio", uiFontSize: 11);
        Call(settings, "ShowFileRulesPage");
        Snapshot(settings, 1120, 820, Path.Combine(output, "project-rules-settings.png"));
        var cards = settings.GetVisualDescendants().OfType<Border>().Where(border => border.Classes.Contains("rule-card") && border.IsEffectivelyVisible).ToArray();
        Check(cards.Length >= 5 && cards.All(card => card.Bounds.Width >= 280), "Five project rule cards should remain wide enough to read.");
        var ruleWrap = settings.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.UniformGrid>().Single(panel => panel.Children.OfType<Border>().Count(border => border.Classes.Contains("rule-card")) == 5);
        Check(ruleWrap.Children.Select(child => child.Bounds.Y).Distinct().Count() >= 3, "Rule lists must wrap inside the settings viewport instead of disappearing off the right edge.");
        workbench.UiFontSize = 22;
        settings.ApplyTheme("studio", uiFontSize: 22);
        Snapshot(settings, 1120, 820, Path.Combine(output, "project-rules-settings-200.png"));
        Check(WorkbenchTypography.GetScale(settings) == 2, "Project rules must retain the selected 200 percent interface scale.");
        Check(settings.FindControl<Button>("FileRulesNavButton")!.IsEffectivelyVisible, "Settings navigation should remain accessible at large UI sizes.");
        settings.Close();
        workbench.UiFontSize = 11;
    }
}
