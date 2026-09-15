using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ContextControl.Workbench.Services;
using ContextControl.Workbench.ViewModels;
using ContextControl.Workbench.Views.ThemeSettingsWindowParts;

internal static partial class UiExperienceTests
{
    private static void ResourceAdaptationUi(WorkbenchViewModel workbench, string root, string output)
    {
        var context = workbench.ContextControl;
        var before = context.AutoAdaptLocalModels;
        var originalProfiles = context.LocalRuntimeProfiles.Select(p => p.ToProfile()).ToArray();
        var page = new LlmsSettingsPage { DataContext = workbench };
        var window = new Window { Content = page, DataContext = workbench };
        WorkbenchThemeResources.Apply(window, "studio");
        workbench.UiFontSize = 11;
        var toggle = page.FindControl<CheckBox>("AutoAdaptModelsToggle")!;
        context.AutoAdaptLocalModels = false;
        Snapshot(window, 1120, 820, Path.Combine(output, "llm-resources-manual.png"));
        Check(toggle.IsChecked == false, "Auto adaptation checkbox reflects the saved mode.");
        toggle.IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        Check(context.AutoAdaptLocalModels && context.HardwareFitFilterLabel == "Adapted fit", "UI toggle enables adaptation and changes the hardware filter label.");
        var runtime = context.LocalRuntimeProfiles.First(p => p.SupportsGpuLayers);
        Check(!runtime.IsGpuManual && !runtime.IsContextManual && !runtime.IsThreadsManual, "Auto managed fields should be read-only while their manual values are retained.");
        runtime.AdaptToHardware = false;
        Check(runtime.IsGpuManual && runtime.IsContextManual && runtime.IsThreadsManual, "Per-runtime opt-out restores manual controls.");
        runtime.AdaptToHardware = true;
        context.AutoModelContext = false;
        Check(runtime.IsContextManual && !runtime.IsGpuManual, "Each automatic field can be switched off independently.");
        context.AutoModelContext = true;
        var manual = runtime.ToProfile();
        context.AutoAdaptLocalModels = false;
        context.AutoAdaptLocalModels = true;
        Check(runtime.ToProfile().ContextTokens == manual.ContextTokens && runtime.ToProfile().GpuLayers == manual.GpuLayers
            && runtime.ToProfile().CpuThreads == manual.CpuThreads, "Repeated mode changes must not replace manual values.");
        Snapshot(window, 1120, 820, Path.Combine(output, "llm-resources-auto.png"));
        var card = page.FindControl<Border>("ResourceAdaptationCard")!;
        Check(card.Bounds.Width > 500 && card.Bounds.Height > 140, "Allocation controls have enough space to read.");
        var restored = WorkbenchSettings.Load(root);
        Check(restored.LocalResources.Enabled && restored.LocalRuntimeProfiles.First(p => p.Id == runtime.Id).AdaptToHardware,
            "Mode and runtime override are saved through the real settings binding.");
        context.AutoAdaptLocalModels = before;
        window.Close();
    }
}
