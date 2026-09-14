using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using ContextControl.Workbench.ViewModels;
using ContextControl.Workbench.Views;

namespace ContextControl.Workbench.Views.MainWindowParts;

public sealed partial class SkillbookPage : UserControl
{
    private const string ActiveNodeListClass = "active-node-list";
    private ContextMenu? _skillbookContextMenu;

    public SkillbookPage()
    {
        InitializeComponent();
        SkillbookFlowList.AddHandler(InputElement.PointerPressedEvent, OnSkillbookFlowPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        SkillbookSectionList.AddHandler(InputElement.PointerPressedEvent, OnSkillbookSectionPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        SkillbookSkillList.AddHandler(InputElement.PointerPressedEvent, OnSkillbookSkillPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    private ContextControlViewModel? ContextControl =>
        DataContext is WorkbenchViewModel workbench ? workbench.ContextControl : null;

    private void OnSkillbookFlowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not ListBox target || FindListBoxItem(e.Source) is not { DataContext: SkillbookFlowViewModel flow } item)
        {
            return;
        }

        SetActiveSkillbookList(target);
        var point = e.GetCurrentPoint(target);
        if (!point.Properties.IsRightButtonPressed)
        {
            return;
        }

        if (ContextControl is { } contextControl)
        {
            contextControl.SelectedSkillbookFlow = flow;
        }

        target.Focus();
        e.Handled = true;
        OpenRenameContextMenu(item, "Rename", flow.IsEditable, () => RenameFlowAsync(flow));
    }

    private void OnSkillbookSectionPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not ListBox target || FindListBoxItem(e.Source) is not { DataContext: SkillbookSectionViewModel section } item)
        {
            return;
        }

        SetActiveSkillbookList(target);
        var point = e.GetCurrentPoint(target);
        if (!point.Properties.IsRightButtonPressed)
        {
            return;
        }

        if (ContextControl is { } contextControl)
        {
            contextControl.SelectedSkillbookSection = section;
        }

        target.Focus();
        e.Handled = true;
        var canRename = section.IsEditable && !section.Id.Equals("legacy", StringComparison.OrdinalIgnoreCase);
        OpenRenameContextMenu(item, "Rename", canRename, () => RenameSectionAsync(section));
    }

    private void OnSkillbookSkillPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not ListBox target || FindListBoxItem(e.Source) is not { DataContext: SkillbookSkillViewModel skill } item)
        {
            return;
        }

        SetActiveSkillbookList(target);
        var point = e.GetCurrentPoint(target);
        if (!point.Properties.IsRightButtonPressed)
        {
            return;
        }

        if (ContextControl is { } contextControl)
        {
            contextControl.SelectedSkillbookSkill = skill;
        }

        target.Focus();
        e.Handled = true;
        OpenRenameContextMenu(item, "Rename", skill.IsEditable || skill.IsBuiltIn, () => RenameSkillAsync(skill));
    }

    private async Task RenameFlowAsync(SkillbookFlowViewModel flow)
    {
        var title = await ShowRenameWindowAsync("Rename flow", flow.Title);
        if (title is null)
        {
            return;
        }

        ContextControl?.RenameSkillbookFlow(flow, title);
    }

    private async Task RenameSectionAsync(SkillbookSectionViewModel section)
    {
        var title = await ShowRenameWindowAsync("Rename section", section.Title);
        if (title is null)
        {
            return;
        }

        ContextControl?.RenameSkillbookSection(section, title);
    }

    private async Task RenameSkillAsync(SkillbookSkillViewModel skill)
    {
        var title = await ShowRenameWindowAsync("Rename skill", skill.Title);
        if (title is null)
        {
            return;
        }

        ContextControl?.RenameSkillbookSkill(skill, title);
    }

    private async Task<string?> ShowRenameWindowAsync(string title, string currentName)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner)
        {
            return null;
        }

        var dialog = new SkillbookRenameWindow(title, currentName);
        if (DataContext is WorkbenchViewModel workbench)
        {
            dialog.ApplyTheme(
                workbench.ThemeKey,
                workbench.UiFontFamily,
                workbench.CodeFontFamily,
                workbench.SkinKey,
                workbench.UiFontColorModeKey,
                workbench.CustomUiFontColorHex);
        }

        return await dialog.ShowDialog<string?>(owner);
    }

    private void OpenRenameContextMenu(Control target, string header, bool isEnabled, Func<Task> action)
    {
        CloseSkillbookContextMenu();

        var menu = new ContextMenu();
        menu.Classes.Add("project-tree-context-menu");
        menu.Closing += OnSkillbookContextMenuClosing;

        var item = new MenuItem
        {
            Header = header,
            IsEnabled = isEnabled
        };
        item.Classes.Add("project-tree-context-item");
        item.Click += (_, _) =>
        {
            CloseSkillbookContextMenu();
            _ = action();
        };
        menu.Items.Add(item);

        _skillbookContextMenu = menu;
        menu.Open(target);
    }

    private void SetActiveSkillbookList(ListBox activeList)
    {
        SkillbookFlowList.Classes.Remove(ActiveNodeListClass);
        SkillbookSectionList.Classes.Remove(ActiveNodeListClass);
        SkillbookSkillList.Classes.Remove(ActiveNodeListClass);
        activeList.Classes.Add(ActiveNodeListClass);
    }

    private void CloseSkillbookContextMenu()
    {
        var menu = _skillbookContextMenu;
        if (menu is null)
        {
            return;
        }

        menu.Closing -= OnSkillbookContextMenuClosing;
        _skillbookContextMenu = null;
        menu.Close();
    }

    private void OnSkillbookContextMenuClosing(object? sender, CancelEventArgs e)
    {
        if (ReferenceEquals(sender, _skillbookContextMenu))
        {
            _skillbookContextMenu = null;
        }
    }

    private static ListBoxItem? FindListBoxItem(object? source)
    {
        for (var current = source as Visual; current is not null; current = current.GetVisualParent())
        {
            if (current is ListBoxItem item)
            {
                return item;
            }
        }

        return null;
    }
}
