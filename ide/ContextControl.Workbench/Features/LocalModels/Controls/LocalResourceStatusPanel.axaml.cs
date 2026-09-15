using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ContextControl.Workbench.ViewModels;

namespace ContextControl.Workbench.Controls;

public partial class LocalResourceStatusPanel : UserControl
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(5) };
    private CancellationTokenSource? _lifetime;
    public LocalResourceStatusPanel()
    {
        InitializeComponent();
        _timer.Tick += (_, _) => Refresh(false);
        this.FindControl<ComboBox>("ResourceModelPicker")!.SelectionChanged += (_, _) => Refresh(true);
    }
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _lifetime = new();
        _timer.Start();
        Refresh(false);
    }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _timer.Stop();
        _lifetime?.Cancel();
        _lifetime?.Dispose();
        _lifetime = null;
        base.OnDetachedFromVisualTree(e);
    }
    private void OnRefreshClick(object? sender, RoutedEventArgs e) => Refresh(true);
    private async void OnTuneClick(object? sender, RoutedEventArgs e)
    {
        if (_lifetime is not null && DataContext is WorkbenchViewModel vm)
            await vm.ContextControl.TuneSelectedModelAsync(_lifetime.Token);
    }
    private void OnCancelTuneClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is WorkbenchViewModel vm) vm.ContextControl.CancelPerformanceTuning();
    }
    private void Refresh(bool force)
    {
        if (_lifetime is not null && IsEffectivelyVisible && TopLevel.GetTopLevel(this) is not Window { WindowState: WindowState.Minimized }
            && DataContext is WorkbenchViewModel vm)
            _ = vm.ContextControl.RefreshResourceStatusAsync(_lifetime.Token, force);
    }
}
