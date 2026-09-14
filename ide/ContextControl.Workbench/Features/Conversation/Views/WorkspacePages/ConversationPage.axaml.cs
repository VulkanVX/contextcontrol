using System.ComponentModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ContextControl.Workbench.ViewModels;
using ContextControl.Workbench.Views;

namespace ContextControl.Workbench.Views.MainWindowParts;

public sealed partial class ConversationPage : UserControl
{
    private const double CollapsedChatHistoryWidth = 10.0;
    private const double ExpandedChatHistoryWidth = 220.0;
    private const double ChatHistoryAnimationMs = 130.0;
    private const double CollapsedChatHistoryArrowAngle = 0.0;
    private const double ExpandedChatHistoryArrowAngle = 180.0;
    private const double CollapsedChatHistoryArrowOpacity = 0.78;
    private const double ExpandedChatHistoryArrowOpacity = 0.0;
    private readonly DispatcherTimer _chatHistoryAnimationTimer;
    private readonly Stopwatch _chatHistoryAnimationClock = new();
    private double _chatHistoryAnimationFromWidth;
    private double _chatHistoryAnimationToWidth;
    private bool _collapseChatHistoryAfterAnimation;
    private bool _isPointerOverChatHistoryShell;
    private bool _isChatSessionContextMenuOpen;
    private ContextMenu? _chatSessionContextMenu;

    public ConversationPage()
    {
        InitializeComponent();
        _chatHistoryAnimationTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _chatHistoryAnimationTimer.Tick += (_, _) => TickChatHistoryPanelAnimation();
        ChatSessionList.AddHandler(InputElement.PointerPressedEvent, OnChatSessionPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        DataContextChanged += (_, _) => BindReasoningContext();
        DetachedFromVisualTree += (_, _) => { if (_reasoningContext is not null) _reasoningContext.PropertyChanged -= OnReasoningContextChanged; _reasoningContext = null; };
        AttachedToVisualTree += (_, _) => BindReasoningContext();
    }

    private ContextControlViewModel? _reasoningContext;
    private bool _followReasoning = true;
    private void BindReasoningContext()
    {
        if (_reasoningContext is not null) _reasoningContext.PropertyChanged -= OnReasoningContextChanged;
        _reasoningContext = ContextControl;
        if (_reasoningContext is not null) _reasoningContext.PropertyChanged += OnReasoningContextChanged;
        UpdateReasoningColumns();
    }
    private void UpdateReasoningColumns()
    {
        var open = ContextControl?.IsReasoningPaneOpen == true;
        ReasoningSplitGrid.ColumnDefinitions[1].Width = new GridLength(open ? 5 : 0);
        ReasoningSplitGrid.ColumnDefinitions[2].Width = new GridLength(open ? Math.Clamp(ReasoningSplitGrid.Bounds.Width * .38, 220, 380) : 0);
    }
    private void OnOpenReasoning(object? sender, RoutedEventArgs e) => ContextControl?.OpenReasoning(null);
    private void OnCloseReasoning(object? sender, RoutedEventArgs e) { if (ContextControl is { } vm) vm.IsReasoningPaneOpen = false; }
    private void OnReasoningContextChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ContextControlViewModel.IsReasoningPaneOpen)) UpdateReasoningColumns();
        if (e.PropertyName != nameof(ContextControlViewModel.SelectedReasoningMessage)) return;
        var target = ContextControl?.SelectedReasoningMessage;
        Dispatcher.UIThread.Post(() =>
        {
            var card = ReasoningItems.GetVisualDescendants().OfType<Border>()
                .FirstOrDefault(control => control.Classes.Contains("reasoning-entry") && ReferenceEquals(control.DataContext, target));
            _followReasoning = false;
            card?.BringIntoView();
            if (target?.IsAwaitingAnswer == true)
            {
                _followReasoning = true;
                ReasoningScroll.ScrollToEnd();
            }
        }, DispatcherPriority.Loaded);
    }
    private void OnReasoningScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (Math.Abs(e.ExtentDelta.Y) < 0.5 && Math.Abs(e.OffsetDelta.Y) > 0.5)
            _followReasoning = ReasoningScroll.Extent.Height - ReasoningScroll.Viewport.Height - ReasoningScroll.Offset.Y <= 24;
        if (_followReasoning && e.ExtentDelta.Y > 0.5)
            Dispatcher.UIThread.Post(() => { if (_followReasoning) ReasoningScroll.ScrollToEnd(); }, DispatcherPriority.Loaded);
    }

    private MainWindow? OwnerWindow => this.FindAncestorOfType<MainWindow>();

    private ContextControlViewModel? ContextControl =>
        DataContext is WorkbenchViewModel workbench ? workbench.ContextControl : null;

    private void OnChatHistoryRailPressed(object? sender, PointerPressedEventArgs e)
    {
        ExpandChatHistoryPanel();
        e.Handled = true;
    }

    private void ExpandChatHistoryPanel()
    {
        _isPointerOverChatHistoryShell = true;
        ChatHistoryPanel.Opacity = 1;
        ChatHistoryPanel.IsHitTestVisible = true;
        AnimateChatHistoryWidthTo(ExpandedChatHistoryWidth, collapseWhenDone: false);
        ChatHistoryRailArrow.Angle = ExpandedChatHistoryArrowAngle;
        ChatHistoryRailArrow.Opacity = ExpandedChatHistoryArrowOpacity;
    }

    private void OnChatHistoryHoverEntered(object? sender, PointerEventArgs e)
    {
        _isPointerOverChatHistoryShell = true;
    }

    private void OnChatHistoryHoverExited(object? sender, PointerEventArgs e)
    {
        _isPointerOverChatHistoryShell = false;
        if (_isChatSessionContextMenuOpen)
        {
            return;
        }

        CollapseChatHistoryPanel();
    }

    private void OnAttachmentRowTapped(object? sender, TappedEventArgs e) => OwnerWindow?.OnAttachmentRowTapped(sender, e);

    private void OnChatSessionPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(ChatSessionList);
        if (!point.Properties.IsRightButtonPressed
            || FindChatSessionControl(e.Source, out var session) is not { } target
            || session is null)
        {
            return;
        }

        ContextControl?.SelectChatSessionCommand.Execute(session);
        target.Focus();
        e.Handled = true;
        OpenChatSessionContextMenu(target, session);
    }

    private void OpenChatSessionContextMenu(Control target, ChatSessionViewModel session)
    {
        CloseChatSessionContextMenu();

        var menu = new ContextMenu();
        menu.Classes.Add("project-tree-context-menu");
        menu.Closing += OnChatSessionContextMenuClosing;
        menu.Closed += OnChatSessionContextMenuClosed;

        var rename = new MenuItem
        {
            Header = "Rename"
        };
        rename.Classes.Add("project-tree-context-item");
        rename.Click += (_, _) =>
        {
            CloseChatSessionContextMenu();
            _ = RenameChatSessionAsync(session);
        };
        menu.Items.Add(rename);

        var monitor = new MenuItem
        {
            Header = ContextControl?.IsChatMonitored(session) == true ? "Remove from Chat Monitor" : "Add to Chat Monitor"
        };
        monitor.Classes.Add("project-tree-context-item");
        monitor.Click += (_, _) =>
        {
            CloseChatSessionContextMenu();
            ContextControl?.ToggleChatMonitorSession(session);
        };
        menu.Items.Add(monitor);

        _chatSessionContextMenu = menu;
        _isChatSessionContextMenuOpen = true;
        menu.Open(target);
    }

    private async Task RenameChatSessionAsync(ChatSessionViewModel session)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }

        var dialog = new SkillbookRenameWindow("Rename chat", session.Title);
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

        var title = await dialog.ShowDialog<string?>(owner);
        if (!string.IsNullOrWhiteSpace(title))
        {
            ContextControl?.RenameChatSession(session, title);
        }
    }

    private void OnChatSessionContextMenuClosing(object? sender, CancelEventArgs e)
    {
        if (ReferenceEquals(sender, _chatSessionContextMenu))
        {
            _chatSessionContextMenu = null;
        }
    }

    private void OnChatSessionContextMenuClosed(object? sender, RoutedEventArgs e)
    {
        if (sender is ContextMenu menu)
        {
            menu.Closing -= OnChatSessionContextMenuClosing;
            menu.Closed -= OnChatSessionContextMenuClosed;
        }

        _isChatSessionContextMenuOpen = false;
        if (!_isPointerOverChatHistoryShell)
        {
            Dispatcher.UIThread.Post(CollapseChatHistoryPanel);
        }
    }

    private void CloseChatSessionContextMenu()
    {
        if (_chatSessionContextMenu is null)
        {
            return;
        }

        _chatSessionContextMenu.Closing -= OnChatSessionContextMenuClosing;
        _chatSessionContextMenu.Closed -= OnChatSessionContextMenuClosed;
        _chatSessionContextMenu.Close();
        _chatSessionContextMenu = null;
        _isChatSessionContextMenuOpen = false;
        if (!_isPointerOverChatHistoryShell)
        {
            Dispatcher.UIThread.Post(CollapseChatHistoryPanel);
        }
    }

    private void CollapseChatHistoryPanel()
    {
        ChatHistoryPanel.IsHitTestVisible = false;
        AnimateChatHistoryWidthTo(CollapsedChatHistoryWidth, collapseWhenDone: true);
        ChatHistoryRailArrow.Angle = CollapsedChatHistoryArrowAngle;
        ChatHistoryRailArrow.Opacity = CollapsedChatHistoryArrowOpacity;
    }

    private static Control? FindChatSessionControl(object? source, out ChatSessionViewModel? session)
    {
        session = null;
        for (var current = source as Visual; current is not null; current = current.GetVisualParent())
        {
            if (current is Control { DataContext: ChatSessionViewModel chatSession } control)
            {
                session = chatSession;
                return control;
            }
        }

        return null;
    }

    private void AnimateChatHistoryWidthTo(double targetWidth, bool collapseWhenDone)
    {
        _chatHistoryAnimationFromWidth = ChatHistoryHoverShell.Width;
        _chatHistoryAnimationToWidth = targetWidth;
        _collapseChatHistoryAfterAnimation = collapseWhenDone;
        if (Math.Abs(_chatHistoryAnimationFromWidth - targetWidth) < 0.1)
        {
            ChatHistoryHoverShell.Width = targetWidth;
            if (collapseWhenDone && !ChatHistoryPanel.IsHitTestVisible)
            {
                ChatHistoryPanel.Opacity = 0;
            }

            return;
        }

        _chatHistoryAnimationClock.Restart();
        _chatHistoryAnimationTimer.Stop();
        _chatHistoryAnimationTimer.Start();
    }

    private void TickChatHistoryPanelAnimation()
    {
        var progress = Math.Clamp(_chatHistoryAnimationClock.Elapsed.TotalMilliseconds / ChatHistoryAnimationMs, 0.0, 1.0);
        var eased = 1.0 - Math.Pow(1.0 - progress, 3.0);
        ChatHistoryHoverShell.Width = _chatHistoryAnimationFromWidth + ((_chatHistoryAnimationToWidth - _chatHistoryAnimationFromWidth) * eased);
        if (progress < 1.0)
        {
            return;
        }

        _chatHistoryAnimationTimer.Stop();
        _chatHistoryAnimationClock.Reset();
        ChatHistoryHoverShell.Width = _chatHistoryAnimationToWidth;
        if (_collapseChatHistoryAfterAnimation && !ChatHistoryPanel.IsHitTestVisible)
        {
            ChatHistoryPanel.Opacity = 0;
        }
    }
}
