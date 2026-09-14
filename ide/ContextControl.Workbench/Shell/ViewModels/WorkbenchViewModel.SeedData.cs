using System.Collections.ObjectModel;

namespace ContextControl.Workbench.ViewModels;

public sealed partial class WorkbenchViewModel
{
    private static ObservableCollection<ProjectNodeViewModel> BuildProjectTree()
    {
        return
        [
            new ProjectNodeViewModel("contextcontrol", "", true, "root",
            [
                new ProjectNodeViewModel("ide", "ide", true, "v2",
                [
                    new ProjectNodeViewModel("ContextControl.Workbench", "ide/ContextControl.Workbench", true, "v1",
                    [
                        new ProjectNodeViewModel("Shell", "ide/ContextControl.Workbench/Shell", true, "v1",
                        [
                            new ProjectNodeViewModel("MainWindow.axaml", "ide/ContextControl.Workbench/Shell/MainWindow/MainWindow.axaml", false, "v1"),
                            new ProjectNodeViewModel("MainWindow.axaml.cs", "ide/ContextControl.Workbench/Shell/MainWindow/MainWindow.axaml.cs", false, "v1")
                        ]),
                        new ProjectNodeViewModel("Features", "ide/ContextControl.Workbench/Features", true, "v1",
                        [
                            new ProjectNodeViewModel("ContextControlViewModel.cs", "ide/ContextControl.Workbench/Features/ContextWorkflow/ViewModels/Core/ContextControlViewModel.cs", false, "v1"),
                            new ProjectNodeViewModel("ProjectNodeViewModel.cs", "ide/ContextControl.Workbench/Features/ProjectInspection/ViewModels/ProjectNodeViewModel.cs", false, "v1")
                        ]),
                        new ProjectNodeViewModel("ContextControl.Workbench.csproj", "ide/ContextControl.Workbench/ContextControl.Workbench.csproj", false, "v1")
                    ]),
                    new ProjectNodeViewModel("PHASELIST.md", "ide/PHASELIST.md", false, "v1")
                ]),
                new ProjectNodeViewModel("lib", "lib", true, "v10",
                [
                    new ProjectNodeViewModel("export", "lib/export", true, "v7",
                    [
                        new ProjectNodeViewModel("Cc.Export.Source.ps1", "lib/export/Cc.Export.Source.ps1", false, "v7"),
                        new ProjectNodeViewModel("Cc.Export.FileBlocks.ps1", "lib/export/Cc.Export.FileBlocks.ps1", false, "v4")
                    ]),
                    new ProjectNodeViewModel("replace", "lib/replace", true, "v10",
                    [
                        new ProjectNodeViewModel("Cc.Replace.Apply.ps1", "lib/replace/Cc.Replace.Apply.ps1", false, "v10"),
                        new ProjectNodeViewModel("Cc.Replace.Versioning.ps1", "lib/replace/Cc.Replace.Versioning.ps1", false, "v10")
                    ]),
                    new ProjectNodeViewModel("shared", "lib/shared", true, "v3",
                    [
                        new ProjectNodeViewModel("Cc.Settings.ps1", "lib/shared/Cc.Settings.ps1", false, "v3"),
                        new ProjectNodeViewModel("Cc.Clipboard.ps1", "lib/shared/Cc.Clipboard.ps1", false, "v2")
                    ])
                ]),
                new ProjectNodeViewModel("ccStart.ps1", "ccStart.ps1", false, "v3"),
                new ProjectNodeViewModel("ccDir.ps1", "ccDir.ps1", false, "v4"),
                new ProjectNodeViewModel("cc.ps1", "cc.ps1", false, "v6"),
                new ProjectNodeViewModel("ccReplace.ps1", "ccReplace.ps1", false, "v10"),
                new ProjectNodeViewModel("README.md", "README.md", false, "v1")
            ])
        ];
    }

    private static Dictionary<string, FileHistoryViewModel> BuildHistory()
    {
        static FileHistoryViewModel History(string name, string path, params VersionEntryViewModel[] versions)
        {
            return new FileHistoryViewModel(name, path, versions);
        }

        static VersionEntryViewModel Version(
            string label,
            string date,
            string commit,
            string reason,
            string fileName,
            string path)
        {
            return new VersionEntryViewModel(
                label,
                date,
                commit,
                reason,
                fileName,
                path,
                currentFilePath: "");
        }

        var items = new[]
        {
            History("MainWindow.axaml", "ide/ContextControl.Workbench/Shell/MainWindow/MainWindow.axaml",
                Version("v1", "2026-05-10", "b9ef261", "native ceramic shell", "MainWindow.axaml", "ide/ContextControl.Workbench/Shell/MainWindow/MainWindow.axaml")),
            History("MainWindow.axaml.cs", "ide/ContextControl.Workbench/Shell/MainWindow/MainWindow.axaml.cs",
                Version("v1", "2026-05-10", "b9ef261", "history drift behavior", "MainWindow.axaml.cs", "ide/ContextControl.Workbench/Shell/MainWindow/MainWindow.axaml.cs")),
            History("WorkbenchViewModel.cs", "ide/ContextControl.Workbench/Shell/ViewModels/WorkbenchViewModel.cs",
                Version("v1", "2026-05-10", "b9ef261", "project and history state", "WorkbenchViewModel.cs", "ide/ContextControl.Workbench/Shell/ViewModels/WorkbenchViewModel.cs")),
            History("PHASELIST.md", "ide/PHASELIST.md",
                Version("v1", "2026-05-10", "b9ef261", "native IDE phase plan", "PHASELIST.md", "ide/PHASELIST.md")),
            History("ccReplace.ps1", "ccReplace.ps1",
                Version("v10", "2026-05-07", "b9ef261", "applied function", "ccReplace.ps1", "ccReplace.ps1"),
                Version("v9", "2026-05-05", "b9ef261", "applied function", "ccReplace.ps1", "ccReplace.ps1"),
                Version("v8", "2026-05-05", "b9ef261", "applied function", "ccReplace.ps1", "ccReplace.ps1")),
            History("cc.ps1", "cc.ps1",
                Version("v6", "2026-05-08", "b9ef261", "source export update", "cc.ps1", "cc.ps1"),
                Version("v5", "2026-05-07", "b9ef261", "function extraction update", "cc.ps1", "cc.ps1"),
                Version("v4", "2026-05-04", "b9ef261", "baseline", "cc.ps1", "cc.ps1")),
            History("ccDir.ps1", "ccDir.ps1",
                Version("v4", "2026-05-08", "b9ef261", "profile tree filtering", "ccDir.ps1", "ccDir.ps1"),
                Version("v3", "2026-05-07", "b9ef261", "launcher parity", "ccDir.ps1", "ccDir.ps1"),
                Version("v2", "2026-05-04", "b9ef261", "project map export", "ccDir.ps1", "ccDir.ps1"))
        };

        return items.ToDictionary(item => item.Path, StringComparer.OrdinalIgnoreCase);
    }
}
