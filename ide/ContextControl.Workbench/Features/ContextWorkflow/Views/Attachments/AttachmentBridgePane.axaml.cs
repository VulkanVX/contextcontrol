using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace ContextControl.Workbench.Views.MainWindowParts;

public sealed partial class AttachmentBridgePane : UserControl
{
    public AttachmentBridgePane()
    {
        InitializeComponent();
    }

    internal Grid AttachmentRegionControl => AttachmentRegion;
    internal Border AttachmentListHostControl => AttachmentListHost;
    internal ListBox AttachmentListControl => AttachmentList;

    private MainWindow? OwnerWindow => this.FindAncestorOfType<MainWindow>();

    private void OnAttachmentRegionSizeChanged(object? sender, SizeChangedEventArgs e) => OwnerWindow?.OnAttachmentRegionSizeChanged(sender, e);

    private void OnAttachmentRowTapped(object? sender, TappedEventArgs e) => OwnerWindow?.OnAttachmentRowTapped(sender, e);

    private void OnAttachmentRemoveTapped(object? sender, TappedEventArgs e) => OwnerWindow?.OnAttachmentRemoveTapped(sender, e);
}
