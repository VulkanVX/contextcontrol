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
    private void ApplyTheme()
    {
        Palette.Use(ThemeKey, SyntaxThemeKey);
        Background = Palette.Background;
        _root.Background = Palette.Background;
        _scroller.Background = Palette.Background;
        _scrollCornerCover.Background = Palette.Background;
        _surface.ApplyTheme();
        _minimap.ApplyTheme();
    }

    private void ApplyCodeFont()
    {
        Palette.UseFont(CodeFontFamily);
        _surface.InvalidateMeasure();
        _surface.InvalidateVisual();
        _minimap.InvalidateVisual();
    }

    private double EffectiveEditorLineHeight => Math.Max(12.0, Math.Clamp(EditorFontSize, 8.0, 22.0) + 4.0);

    private void ApplyEditorFontSize()
    {
        var fontSize = Math.Clamp(EditorFontSize, 8.0, 22.0);
        var lineHeight = Math.Max(12.0, fontSize + 4.0);
        _surface.SetEditorFontSize(fontSize, lineHeight);
        _minimap.SetEditorLineHeight(lineHeight);
        _verticalScrollbar.SmallChange = lineHeight;
        _verticalScrollbar.LargeChange = lineHeight * 8;
        QueueScrollbarRefresh();
    }

    private void ApplySkin()
    {
        var normalizedSkin = NormalizeSkinKey(SkinKey);
        _surface.SetSkin(normalizedSkin);
        _minimap.SetSkin(normalizedSkin);
        _root.ClipToBounds = IsMatrixConsoleSkin(normalizedSkin);
        UpdateSkinAnimation();
    }

    private void UpdateSkinAnimation()
    {
        if (_isAttachedToVisualTree && IsMatrixConsoleSkin(SkinKey))
        {
            if (!_skinAnimationTimer.IsEnabled)
            {
                _skinAnimationTimer.Start();
            }

            return;
        }

        _skinAnimationTimer.Stop();
    }

    private void OnSkinAnimationTick(object? sender, EventArgs e)
    {
        var phase = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _surface.SetAnimationPhase(phase);
        _minimap.SetAnimationPhase(phase);
    }

    private void ApplyChrome()
    {
        _minimap.IsVisible = ShowMinimap;
        _minimap.IsHitTestVisible = ShowMinimap;
        _minimap.Margin = new Thickness(0, 0, ShowMinimap ? ScrollbarReserve : 0, ShowMinimap ? ScrollbarReserve : 0);
        HoverScrollbarBehavior.SetReserveBottom(_scroller, ShowMinimap ? ScrollbarReserve : 0);
        ApplyVerticalScrollbarMode();
        UpdateMinimapViewport();
    }

    private void ApplyEditorVisualSettings()
    {
        _surface.SetVisualOptions(ShowFoldArrows, ShowSummaryArrowBorders, FoldArrowsInCodeEditor, UseParentChildArrowIndentation, ShowVerticalScopeLines, SummaryFoldKinds, UseColorfulFamilies, ShowFoldSummaryPreview);
    }

    private void ApplyDocument()
    {
        var text = DocumentText ?? "";
        var path = DocumentPath ?? "";
        _surface.SetDocument(text, path, LineChanges);
        _minimap.SetDocument(text, path, LineChanges);
        UpdateMinimapViewport();
        QueueScrollbarRefresh();
    }

    private void UpdateMinimapViewport()
    {
        _surface.SetViewport(_verticalOffset, ViewportHeight);
        _minimap.SetViewport(_verticalOffset, ViewportHeight, _surface.ContentHeight);
    }

    private void ApplyVerticalScrollbarMode()
    {
        _scroller.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
        UpdateVerticalScrollbar();
    }

    private bool UsesPersistentVerticalScrollbar => VerticalScrollBarVisibility == ScrollBarVisibility.Visible;

    private void UpdateVerticalScrollbar()
    {
        var maxOffset = MaxVerticalOffset;
        var isVisible = UsesPersistentVerticalScrollbar && maxOffset > 0;
        _verticalScrollbar.IsVisible = isVisible;
        _verticalScrollbar.IsHitTestVisible = isVisible;
        _verticalScrollbar.Maximum = maxOffset;
        _verticalScrollbar.ViewportSize = Math.Max(0, ViewportHeight);
        _verticalScrollbar.LargeChange = Math.Max(EffectiveEditorLineHeight, ViewportHeight * 0.85);

        var nextValue = Math.Clamp(_verticalOffset, 0, maxOffset);
        if (Math.Abs(_verticalScrollbar.Value - nextValue) <= 0.1)
        {
            return;
        }

        _isSyncingVerticalScrollbar = true;
        _verticalScrollbar.Value = nextValue;
        _isSyncingVerticalScrollbar = false;
    }

    private double MaxVerticalOffset => Math.Max(0, _surface.ContentHeight - ViewportHeight);

    private double ViewportHeight
    {
        get
        {
            var viewportHeight = _scroller.Viewport.Height;
            if (double.IsFinite(viewportHeight) && viewportHeight > 0)
            {
                return viewportHeight;
            }

            viewportHeight = _scroller.Bounds.Height;
            if (double.IsFinite(viewportHeight) && viewportHeight > 0)
            {
                return viewportHeight;
            }

            viewportHeight = Bounds.Height;
            return double.IsFinite(viewportHeight) && viewportHeight > 0
                ? viewportHeight
                : 0;
        }
    }

    private void OnVerticalScrollbarValueChanged(double value)
    {
        if (_isSyncingVerticalScrollbar)
        {
            return;
        }

        ScrollToEditorOffset(value);
    }

    private static string NormalizeSkinKey(string? skinKey)
    {
        return string.IsNullOrWhiteSpace(skinKey) ? "default" : skinKey.Trim().ToLowerInvariant();
    }

    private static bool IsMatrixConsoleSkin(string? skinKey)
    {
        return string.Equals(NormalizeSkinKey(skinKey), MatrixConsoleSkinKey, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class ValueObserver<T>(Action<T> onNext) : IObserver<T>
    {
        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }

        public void OnNext(T value)
        {
            onNext(value);
        }
    }
}
