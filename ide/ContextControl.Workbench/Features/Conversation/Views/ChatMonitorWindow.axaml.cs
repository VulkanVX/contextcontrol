using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ContextControl.Workbench.Services;
using ContextControl.Workbench.ViewModels;

namespace ContextControl.Workbench.Views;

public sealed partial class ChatMonitorWindow : Window
{
    private readonly WorkbenchViewModel _workbench;
    private readonly Func<ChatMonitorEntryViewModel, bool, Task<bool>> _navigate;
    private ChatMonitorEntryViewModel? _replyTarget;
    private bool _ownerClosing;
    private bool _navigating;
    private bool _opened;

    public ChatMonitorWindow(WorkbenchViewModel workbench, Func<ChatMonitorEntryViewModel, bool, Task<bool>> navigate)
    {
        _workbench = workbench;
        _navigate = navigate;
        InitializeComponent();
        DataContext = workbench;
        workbench.ContextControl.ChatMonitor.Entries.CollectionChanged += OnEntriesChanged;
        workbench.ContextControl.PropertyChanged += OnContextChanged;
        workbench.ContextControl.SendCommand.CanExecuteChanged += OnSendAvailabilityChanged;
        Closing += (_, e) =>
        {
            if (_ownerClosing) return;
            e.Cancel = true;
            _workbench.IsChatMonitorEnabled = false;
            Hide();
        };
        PositionChanged += (_, _) => { if (_opened) _workbench.SaveChatMonitorPosition(Position); };
        SizeChanged += (_, _) => KeepOnScreen();
        DragDrop.AddDragOverHandler(ReplyPanel, OnFileDragOver);
        DragDrop.AddDropHandler(ReplyPanel, OnFileDrop);
        RefreshAppearance();
        RefreshCount();
    }

    public void RefreshAppearance()
    {
        WorkbenchThemeResources.Apply(this, _workbench.ThemeKey, _workbench.UiFontFamily, _workbench.CodeFontFamily,
            skinKey: _workbench.SkinKey, uiFontColorModeKey: _workbench.UiFontColorModeKey,
            chatAppearanceKey: _workbench.ChatAppearanceKey, customUiFontColor: _workbench.CustomUiFontColorHex,
            uiFontSize: _workbench.UiFontSize);
        Width = 460 * WorkbenchTypography.GetScale(this);
    }

    public void ScheduleScale()
    {
        WorkbenchTypography.Schedule(this, _workbench.UiFontSize);
        Width = 460 * WorkbenchTypography.NormalizeSize(_workbench.UiFontSize) / WorkbenchTypography.BaseUiSize;
    }

    private void KeepOnScreen()
    {
        if (!_opened) return;
        var screen = Screens.ScreenFromPoint(Position) ?? Screens.Primary;
        if (screen is null) return;
        MaxHeight = Math.Max(120, screen.WorkingArea.Height / RenderScaling - 16);
        var size = new PixelSize((int)Math.Ceiling(Bounds.Width * RenderScaling), (int)Math.Ceiling(Bounds.Height * RenderScaling));
        Position = ClampPosition(Position, screen.WorkingArea, size);
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        var screen = Screens.ScreenFromPoint(_workbench.ChatMonitorPosition ?? Position) ?? Screens.Primary;
        if (screen is { } display)
        {
            var area = display.WorkingArea;
            MaxHeight = Math.Max(120, area.Height / RenderScaling - 16);
            var size = new PixelSize((int)Math.Ceiling(Width * RenderScaling), (int)Math.Ceiling(Math.Max(80, Bounds.Height) * RenderScaling));
            var requested = _workbench.ChatMonitorPosition ?? new PixelPoint(area.Right - size.Width - 24, area.Y + 48);
            Position = ClampPosition(requested, area, size);
        }
        _opened = true;
    }

    public static PixelPoint ClampPosition(PixelPoint requested, PixelRect area, PixelSize size) => new(
        Math.Clamp(requested.X, area.X, Math.Max(area.X, area.Right - size.Width)),
        Math.Clamp(requested.Y, area.Y, Math.Max(area.Y, area.Bottom - size.Height)));

    public void CloseWithWorkbench()
    {
        _ownerClosing = true;
        _workbench.ContextControl.ChatMonitor.Entries.CollectionChanged -= OnEntriesChanged;
        _workbench.ContextControl.PropertyChanged -= OnContextChanged;
        _workbench.ContextControl.SendCommand.CanExecuteChanged -= OnSendAvailabilityChanged;
        Close();
    }

    private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_replyTarget is not null && !_workbench.ContextControl.ChatMonitor.Entries.Contains(_replyTarget)) CloseReply();
        RefreshCount();
    }

    private void RefreshCount()
    {
        var count = _workbench.ContextControl.ChatMonitor.Entries.Count;
        MonitorCount.Text = count == 1 ? "1 chat" : $"{count} chats";
        EmptyState.IsVisible = count == 0;
    }

    private void OnDragHeader(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed &&
            e.Source is Control source && source is not Button && source.FindAncestorOfType<Button>() is null)
            BeginMoveDrag(e);
    }

    private void OnHideMonitor(object? sender, RoutedEventArgs e) => _workbench.IsChatMonitorEnabled = false;
    private async void OnOpenChat(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: ChatMonitorEntryViewModel entry }) await Navigate(entry, activate: true);
    }

    private async Task<bool> Navigate(ChatMonitorEntryViewModel entry, bool activate)
    {
        if (_navigating) return false;
        _navigating = true;
        try
        {
            var opened = await _navigate(entry, activate);
            MonitorNotice.IsVisible = !opened;
            MonitorNotice.Text = opened ? "" : "This chat could not be found. Its project may have moved or its history was removed.";
            return opened;
        }
        catch (Exception ex)
        {
            MonitorNotice.Text = $"Could not open chat: {ex.Message}";
            MonitorNotice.IsVisible = true;
            return false;
        }
        finally { _navigating = false; }
    }

    private async void OnReply(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: ChatMonitorEntryViewModel entry }) return;
        if (!await Navigate(entry, activate: false)) return;
        _replyTarget = entry;
        ReplyTitle.Text = $"Reply to {entry.Title}";
        ReplyNotice.Text = "Shares this chat's draft and attachments with the workbench.";
        ReplyPanel.IsVisible = true;
        RefreshSendAvailability();
        Activate();
        Dispatcher.UIThread.Post(() => ReplyInput.Focus());
    }

    private void OnRemoveChat(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: ChatMonitorEntryViewModel entry }) _workbench.ContextControl.ChatMonitor.Remove(entry);
    }

    private void OnContextChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ContextControlViewModel.SelectedChatSession) && _replyTarget is not null && !_workbench.ContextControl.IsMonitorTargetSelected(_replyTarget))
            CloseReply();
        RefreshSendAvailability();
    }

    private void OnSendAvailabilityChanged(object? sender, EventArgs e) => RefreshSendAvailability();
    private void RefreshSendAvailability() => ReplySendButton.IsEnabled = CanUseReplyTarget() && _workbench.ContextControl.SendCommand.CanExecute(null);

    private void OnCloseReply(object? sender, RoutedEventArgs e) => CloseReply();
    private void CloseReply() { ReplyPanel.IsVisible = false; _replyTarget = null; RefreshSendAvailability(); }
    private bool CanUseReplyTarget() => _replyTarget is not null && _workbench.ContextControl.IsMonitorTargetSelected(_replyTarget);

    private void OnSendReply(object? sender, RoutedEventArgs e)
    {
        if (!CanUseReplyTarget()) { CloseReply(); return; }
        var context = _workbench.ContextControl;
        if (!context.SendCommand.CanExecute(null)) { ReplyNotice.Text = context.PhaseDetail; return; }
        context.SendCommand.Execute(null);
    }

    private void OnReplyKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && e.KeyModifiers.HasFlag(KeyModifiers.Control)) { OnSendReply(sender, new RoutedEventArgs()); e.Handled = true; }
        else if (e.Key == Key.Escape) { CloseReply(); e.Handled = true; }
    }

    private async void OnAttachFiles(object? sender, RoutedEventArgs e)
    {
        var target = _replyTarget;
        if (!CanUseReplyTarget()) return;
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Attach to reply", AllowMultiple = true });
            if (!ReferenceEquals(target, _replyTarget) || !CanUseReplyTarget()) return;
            _workbench.ContextControl.AttachFiles(files.Select(file => file.TryGetLocalPath()).OfType<string>());
        }
        catch (Exception ex) { ReplyNotice.Text = $"Could not attach files: {ex.Message}"; }
    }

    private void OnFileDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = CanUseReplyTarget() && e.DataTransfer.Formats.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnFileDrop(object? sender, DragEventArgs e)
    {
        if (CanUseReplyTarget())
        {
            var files = e.DataTransfer.TryGetFiles()?.Select(file => file.TryGetLocalPath()).OfType<string>() ?? [];
            var count = _workbench.ContextControl.AttachFiles(files);
            ReplyNotice.Text = count > 0 ? $"Attached {count} file(s). They will be included when you send." : "Drop local files to attach them.";
        }
        e.Handled = true;
    }

    private void OnRemoveAttachment(object? sender, RoutedEventArgs e)
    {
        if (CanUseReplyTarget() && sender is Control { DataContext: ContextControlAttachmentViewModel attachment })
            _workbench.ContextControl.RemoveAttachmentCommand.Execute(attachment);
    }
}
