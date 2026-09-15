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
        context.ShowAllLocalModelsCommand.Execute(null);
        context.AutoModelRamReserve = 16;
        context.AutoModelMaxContext = 20480;
        foreach (var model in context.LocalLlmModels) model.UpdateResourceHardware(LocalResourceTests.Hardware());
        context.ShowOnlyHardwareUsableLocalLlms = true;
        var expected = context.LocalLlmModels.Where(m => m.AdaptedFitPlan?.Fits == true).ToHashSet();
        Check(expected.Count > 300 && context.VisibleLocalLlmModels.ToHashSet().SetEquals(expected),
            "Real full-catalog hardware filter includes every adapted RAM/VRAM fit.");
        Check(context.VisibleLocalLlmModels.Any(m => m.DisplayName.Contains("Solar Pro Preview", StringComparison.Ordinal)),
            "Large-GPU recommendation must not hide a RAM-offload model from the real UI list.");
        context.SelectedLocalLlmRequirementFilter = "4 GB VRAM or less";
        Check(context.VisibleLocalLlmModels.ToHashSet().SetEquals(expected), "Legacy VRAM threshold does not suppress adapted fits.");
        context.SelectedLocalLlmRequirementFilter = "Adapted CPU / RAM";
        var withAuto = context.VisibleLocalLlmModels.Select(m => m.Id).ToHashSet();
        context.AutoAdaptLocalModels = false;
        Check(withAuto.Count > 200 && context.VisibleLocalLlmModels.Select(m => m.Id).ToHashSet().SetEquals(withAuto),
            "Explicit adapted fit plus hardware checkbox still works when Auto launch mode is off.");
        context.AutoAdaptLocalModels = true;
        context.SelectedLocalLlmRequirementFilter = "Any requirement";
        foreach (var model in context.LocalLlmModels) model.UpdateResourceHardware(LocalResourceTests.Hardware(freeRam: 17, freeGpu: 0.1));
        Call(context, "ApplyLocalLlmFilters");
        Check(context.VisibleLocalLlmModels.Count < expected.Count, "Filter reacts to current memory pressure.");
        var changes = 0;
        context.VisibleLocalLlmModels.CollectionChanged += (_, _) => changes++;
        Call(context, "ApplyLocalLlmFilters");
        Check(changes == 0, "Unchanged resource refresh does not clear/rebuild the visible catalog.");
        var resourcePanel = page.GetVisualDescendants().OfType<ContextControl.Workbench.Controls.LocalResourceStatusPanel>().Single();
        var picker = resourcePanel.FindControl<ComboBox>("ResourceModelPicker")!;
        var selectedChatModel = context.SelectedLocalModel;
        picker.SelectedItem = context.LocalLlmModels.First(m => m.DisplayName.Contains("Solar Pro Preview", StringComparison.Ordinal));
        Dispatcher.UIThread.RunJobs();
        Check(context.SelectedResourceModel == picker.SelectedItem && context.SelectedLocalModel == selectedChatModel,
            "Preview any model without changing the active chat model.");
        Check(resourcePanel.FindControl<TextBlock>("ActualAllocation")!.Text == context.ResourceAllocationSummary,
            "Runtime allocation status is bound in settings.");
        context.AutoAdaptLocalModels = before;
        window.Close();
    }

    internal static void ResourcePanelLive(WorkbenchViewModel workbench, string output)
    {
        var context = workbench.ContextControl;
        var model = context.LocalLlmModels.Single(m => m.Id == "granite3.3:2b");
        model.ApplyState(true, true, LocalResourceTests.Hardware());
        context.SelectedResourceModel = model;
        context.AutoAdaptLocalModels = true;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var refresh = context.RefreshResourceStatusAsync(deadline.Token, force: true);
        while (!refresh.IsCompleted && !deadline.IsCancellationRequested)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(10);
        }
        Check(refresh.IsCompletedSuccessfully, "Live selected-model resource polling completes.");
        Console.WriteLine(context.ResourceBudgetSummary);
        Console.WriteLine(context.ResourceAllocationSummary);
        Console.WriteLine(context.ResourceEstimateSummary);
        Console.WriteLine(context.ResourceContextSummary);
        Check(context.ResourceAllocationSummary.StartsWith("Loaded:", StringComparison.Ordinal)
            && context.ResourceAllocationSummary.Contains("8,192", StringComparison.Ordinal), "Live resource panel displays runtime-reported loaded context.");
        Check(context.ResourceContextSummary.Contains("131,072", StringComparison.Ordinal), "Live panel reads the model's actual trained context limit.");
        var page = new LlmsSettingsPage { DataContext = workbench };
        var window = new Window { Content = page, DataContext = workbench };
        WorkbenchThemeResources.Apply(window, "studio");
        Snapshot(window, 1120, 900, Path.Combine(output, "llm-resources-live.png"));
        window.Close();
    }
}
