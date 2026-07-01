using System.Globalization;
using System.Text;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TextMateSharp.Grammars;
using TextMateSharp.Registry;

namespace ContextControl.Workbench.Controls;

public sealed partial class CodeEditor
{
    public string DocumentText
    {
        get => GetValue(DocumentTextProperty);
        set => SetValue(DocumentTextProperty, value);
    }

    public string DocumentPath
    {
        get => GetValue(DocumentPathProperty);
        set => SetValue(DocumentPathProperty, value);
    }

    public IReadOnlyDictionary<int, string>? LineChanges
    {
        get => GetValue(LineChangesProperty);
        set => SetValue(LineChangesProperty, value);
    }

    public string ThemeKey
    {
        get => GetValue(ThemeKeyProperty);
        set => SetValue(ThemeKeyProperty, value);
    }

    public string SyntaxThemeKey
    {
        get => GetValue(SyntaxThemeKeyProperty);
        set => SetValue(SyntaxThemeKeyProperty, value);
    }

    public string CodeFontFamily
    {
        get => GetValue(CodeFontFamilyProperty);
        set => SetValue(CodeFontFamilyProperty, value);
    }

    public double EditorFontSize
    {
        get => GetValue(EditorFontSizeProperty);
        set => SetValue(EditorFontSizeProperty, value);
    }

    public string SkinKey
    {
        get => GetValue(SkinKeyProperty);
        set => SetValue(SkinKeyProperty, value);
    }

    public bool ShowMinimap
    {
        get => GetValue(ShowMinimapProperty);
        set => SetValue(ShowMinimapProperty, value);
    }

    public ScrollBarVisibility VerticalScrollBarVisibility
    {
        get => GetValue(VerticalScrollBarVisibilityProperty);
        set => SetValue(VerticalScrollBarVisibilityProperty, value);
    }

    public bool ShowFoldArrows
    {
        get => GetValue(ShowFoldArrowsProperty);
        set => SetValue(ShowFoldArrowsProperty, value);
    }

    public bool ShowSummaryArrowBorders
    {
        get => GetValue(ShowSummaryArrowBordersProperty);
        set => SetValue(ShowSummaryArrowBordersProperty, value);
    }

    public bool FoldArrowsInCodeEditor
    {
        get => GetValue(FoldArrowsInCodeEditorProperty);
        set => SetValue(FoldArrowsInCodeEditorProperty, value);
    }

    public bool UseParentChildArrowIndentation
    {
        get => GetValue(UseParentChildArrowIndentationProperty);
        set => SetValue(UseParentChildArrowIndentationProperty, value);
    }

    public bool ShowVerticalScopeLines
    {
        get => GetValue(ShowVerticalScopeLinesProperty);
        set => SetValue(ShowVerticalScopeLinesProperty, value);
    }

    public string SummaryFoldKinds
    {
        get => GetValue(SummaryFoldKindsProperty);
        set => SetValue(SummaryFoldKindsProperty, value);
    }

    public bool UseColorfulFamilies
    {
        get => GetValue(UseColorfulFamiliesProperty);
        set => SetValue(UseColorfulFamiliesProperty, value);
    }

    public bool ShowFoldSummaryPreview
    {
        get => GetValue(ShowFoldSummaryPreviewProperty);
        set => SetValue(ShowFoldSummaryPreviewProperty, value);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _isAttachedToVisualTree = true;
        UpdateSkinAnimation();
        QueueScrollbarRefresh();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _isAttachedToVisualTree = false;
        _skinAnimationTimer.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    private void OnEditorSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        QueueScrollbarRefresh();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == DocumentTextProperty
            || change.Property == DocumentPathProperty
            || change.Property == LineChangesProperty)
        {
            ApplyDocument();
        }
        else if (change.Property == ThemeKeyProperty
            || change.Property == SyntaxThemeKeyProperty)
        {
            ApplyTheme();
        }
        else if (change.Property == CodeFontFamilyProperty)
        {
            ApplyCodeFont();
        }
        else if (change.Property == EditorFontSizeProperty)
        {
            ApplyEditorFontSize();
        }
        else if (change.Property == SkinKeyProperty)
        {
            ApplySkin();
        }
        else if (change.Property == ShowMinimapProperty)
        {
            ApplyChrome();
        }
        else if (change.Property == VerticalScrollBarVisibilityProperty)
        {
            ApplyVerticalScrollbarMode();
            QueueScrollbarRefresh();
        }
        else if (change.Property == ShowFoldArrowsProperty
            || change.Property == ShowSummaryArrowBordersProperty
            || change.Property == FoldArrowsInCodeEditorProperty
            || change.Property == UseParentChildArrowIndentationProperty
            || change.Property == ShowVerticalScopeLinesProperty
            || change.Property == SummaryFoldKindsProperty
            || change.Property == UseColorfulFamiliesProperty
            || change.Property == ShowFoldSummaryPreviewProperty)
        {
            ApplyEditorVisualSettings();
        }
    }

    private void QueueScrollbarRefresh()
    {
        RefreshScrollbarLayout();
        if (_isScrollbarRefreshQueued)
        {
            return;
        }

        _isScrollbarRefreshQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _isScrollbarRefreshQueued = false;
            RefreshScrollbarLayout();
        });
    }

    private void RefreshScrollbarLayout()
    {
        ScrollViewer.SetAllowAutoHide(_scroller, false);
        _surface.InvalidateMeasure();
        _scroller.InvalidateMeasure();
        _scroller.InvalidateArrange();
        HoverScrollbarBehavior.Refresh(_scroller);
        RefreshViewportState();
    }

    private void RefreshViewportState()
    {
        _verticalOffset = Math.Clamp(_verticalOffset, 0, MaxVerticalOffset);
        UpdateVerticalScrollbar();
        UpdateMinimapViewport();
    }
}
