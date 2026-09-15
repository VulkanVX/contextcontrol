using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Threading;
using ContextControl.Workbench.Controls;
using ContextControl.Workbench.Services;
using ContextControl.Workbench.ViewModels;
using ContextControl.Workbench.Views;
using ContextControl.Workbench.Views.MainWindowParts;

internal static partial class UiExperienceTests
{
    private static void ChatActionsUi(WorkbenchViewModel workbench, string root, string output)
    {
        var context = workbench.ContextControl;
        Check(ChatActionCatalog.All.All(a => WorkspaceIcon.HasIcon(a.Icon)), "All actions have a drawn icon.");
        var originalMode = context.LocalContextMode;
        var originalMax = context.AutoModelMaxContext;
        var originalAuto = context.AutoAdaptLocalModels;
        var prompt = new ContextPromptBar { DataContext = workbench };
        var window = new Window { Content = prompt, DataContext = workbench, Width = 1080, Height = 400 };
        WorkbenchThemeResources.Apply(window, "studio");
        context.IsPromptOpen = true;
        window.Show();
        var input = prompt.FindControl<TextBox>("ContextPromptTextBox")!;
        input.Focus(); input.Text = "/"; Dispatcher.UIThread.RunJobs();
        var popup = prompt.FindControl<Popup>("ActionPopup")!;
        var suggestions = prompt.FindControl<ListBox>("SlashActions")!;
        Check(popup.IsOpen && suggestions.ItemCount == ChatActionCatalog.All.Count, "Typing / opens all actions.");
        input.Text = "/ga"; Dispatcher.UIThread.RunJobs();
        Check(suggestions.ItemCount == 1 && ((ChatAction)suggestions.SelectedItem!).Command == "/game", "Slash list filters while typing.");
        Call(prompt, "OnPromptTextBoxKeyDown", input, new KeyEventArgs { Key = Key.Tab });
        Check(input.Text == "/game " && !popup.IsOpen, "Tab inserts a command without sending it.");
        var mode = prompt.FindControl<ComboBox>("ComposerContextMode")!;
        mode.SelectedItem = "Adaptive"; Dispatcher.UIThread.RunJobs();
        Check(context.LocalContextMode == "Adaptive" && context.AutoAdaptLocalModels && context.AutoModelMaxContext >= 32768, "Composer selection enables adaptive context with a usable ceiling.");
        context.AutoModelMaxContext = 65536;
        Check(SpinWait.SpinUntil(() => WorkbenchSettings.Load(root).LocalResources is { ContextMode: "Adaptive", MaxContextTokens: 65536 }, TimeSpan.FromSeconds(3)), "Mode and ceiling persist.");
        Snapshot(window, 1080, 400, Path.Combine(output, "actions-composer.png"));
        var method = context.GetType().GetMethod("DispatchChatAction", Private)!;
        object?[] args = ["/game Snake"];
        Check((bool)method.Invoke(context, args)! && (string)args[0]! == "Snake" && context.IsGameCreationEnabled && !context.IsAutopilotEnabled && !context.IsGoogleSearchEnabled, "The game action chooses the game route and strips the command.");
        args = ["/chat explain recursion"];
        Check((bool)method.Invoke(context, args)! && !context.IsGameCreationEnabled && !context.IsGoogleSearchEnabled, "Chat exits game and research modes.");
        args = ["/search new game releases"];
        Check((bool)method.Invoke(context, args)! && context.IsGoogleSearchEnabled && !context.IsGameCreationEnabled, "Search explicitly selects research.");
        args = ["/image a forest"];
        Check((bool)method.Invoke(context, args)! && context.IsImageGenPromptMode, "Image action selects the existing image runtime route.");
        args = ["/context fix camera"];
        Check((bool)method.Invoke(context, args)! && context.IsAutopilotEnabled && context.IsLocalPromptMode, "Project action selects the CC workflow.");
        args = ["/game"];
        Check(!(bool)method.Invoke(context, args)!, "Bare prompt actions show help without generating.");
        args = ["/unknown command"];
        Check(!(bool)method.Invoke(context, args)!, "Unknown actions never dispatch to the model.");
        var library = new ActionLibraryWindow();
        WorkbenchThemeResources.Apply(library, "studio");
        Snapshot(library, 760, 700, Path.Combine(output, "actions-library.png"));
        Check(library.FindControl<ItemsControl>("ActionItems")!.ItemCount == ChatActionCatalog.All.Count, "Action library shares the exact slash catalog.");
        library.Close(); window.Close();
        context.LocalContextMode = originalMode; context.AutoModelMaxContext = originalMax; context.AutoAdaptLocalModels = originalAuto;
    }
}
