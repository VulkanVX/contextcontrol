using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

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

    public ConversationPage()
    {
        InitializeComponent();
        _chatHistoryAnimationTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _chatHistoryAnimationTimer.Tick += (_, _) => TickChatHistoryPanelAnimation();
    }

    private MainWindow? OwnerWindow => this.FindAncestorOfType<MainWindow>();

    private void OnChatHistoryRailPressed(object? sender, PointerPressedEventArgs e)
    {
        ExpandChatHistoryPanel();
        e.Handled = true;
    }

    private void ExpandChatHistoryPanel()
    {
        ChatHistoryPanel.Opacity = 1;
        ChatHistoryPanel.IsHitTestVisible = true;
        AnimateChatHistoryWidthTo(ExpandedChatHistoryWidth, collapseWhenDone: false);
        ChatHistoryRailArrow.Angle = ExpandedChatHistoryArrowAngle;
        ChatHistoryRailArrow.Opacity = ExpandedChatHistoryArrowOpacity;
    }

    private void OnChatHistoryHoverExited(object? sender, PointerEventArgs e)
    {
        ChatHistoryPanel.IsHitTestVisible = false;
        AnimateChatHistoryWidthTo(CollapsedChatHistoryWidth, collapseWhenDone: true);
        ChatHistoryRailArrow.Angle = CollapsedChatHistoryArrowAngle;
        ChatHistoryRailArrow.Opacity = CollapsedChatHistoryArrowOpacity;
    }

    private void OnAttachmentRowTapped(object? sender, TappedEventArgs e) => OwnerWindow?.OnAttachmentRowTapped(sender, e);

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
