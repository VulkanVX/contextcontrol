using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using ContextControl.Workbench.Controls;

namespace ContextControl.Workbench.Views.ThemeSettingsWindowParts;

public sealed partial class AppearanceSettingsPage : UserControl
{
    public AppearanceSettingsPage()
    {
        InitializeComponent();
    }

    internal ComboBox SkinPickerControl => SkinPicker;
    internal ComboBox ThemePickerControl => ThemePicker;
    internal ComboBox SyntaxThemePickerControl => SyntaxThemePicker;
    internal ComboBox CodeFontPickerControl => CodeFontPicker;
    internal ComboBox UiFontPickerControl => UiFontPicker;
    internal ComboBox ChatAppearancePickerControl => ChatAppearancePicker;
    internal Button SummaryArrowOptionsToggleButtonControl => SummaryArrowOptionsToggleButton;
    internal Border SummaryArrowOptionsPanelControl => SummaryArrowOptionsPanel;
    internal ComboBox FoldArrowPositionPickerControl => FoldArrowPositionPicker;
    internal TextBlock PreviewCaptionControl => PreviewCaption;
    internal CodeEditor AppearanceCodePreviewControl => AppearanceCodePreview;

    private ThemeSettingsWindow? OwnerWindow => this.FindAncestorOfType<ThemeSettingsWindow>();

    private void OnSkinSelectionChanged(object? sender, SelectionChangedEventArgs e) => OwnerWindow?.OnSkinSelectionChanged(sender, e);

    private void OnThemeSelectionChanged(object? sender, SelectionChangedEventArgs e) => OwnerWindow?.OnThemeSelectionChanged(sender, e);

    private void OnSyntaxThemeSelectionChanged(object? sender, SelectionChangedEventArgs e) => OwnerWindow?.OnSyntaxThemeSelectionChanged(sender, e);

    private void OnCodeFontSelectionChanged(object? sender, SelectionChangedEventArgs e) => OwnerWindow?.OnCodeFontSelectionChanged(sender, e);

    private void OnUiFontSelectionChanged(object? sender, SelectionChangedEventArgs e) => OwnerWindow?.OnUiFontSelectionChanged(sender, e);

    private void OnChatAppearanceSelectionChanged(object? sender, SelectionChangedEventArgs e) => OwnerWindow?.OnChatAppearanceSelectionChanged(sender, e);

    private void OnOptionPickerDropDownClosed(object? sender, EventArgs e) => OwnerWindow?.OnOptionPickerDropDownClosed(sender, e);

    private void OnThemeOptionPointerEntered(object? sender, PointerEventArgs e) => OwnerWindow?.OnThemeOptionPointerEntered(sender, e);

    private void OnThemeOptionPointerExited(object? sender, PointerEventArgs e) => OwnerWindow?.OnThemeOptionPointerExited(sender, e);

    private void OnSyntaxThemeOptionPointerEntered(object? sender, PointerEventArgs e) => OwnerWindow?.OnSyntaxThemeOptionPointerEntered(sender, e);

    private void OnSyntaxThemeOptionPointerExited(object? sender, PointerEventArgs e) => OwnerWindow?.OnSyntaxThemeOptionPointerExited(sender, e);

    private void OnCodeFontOptionPointerEntered(object? sender, PointerEventArgs e) => OwnerWindow?.OnCodeFontOptionPointerEntered(sender, e);

    private void OnCodeFontOptionPointerExited(object? sender, PointerEventArgs e) => OwnerWindow?.OnCodeFontOptionPointerExited(sender, e);

    private void OnSummaryArrowOptionsToggleClick(object? sender, RoutedEventArgs e) => OwnerWindow?.OnSummaryArrowOptionsToggleClick(sender, e);
}
