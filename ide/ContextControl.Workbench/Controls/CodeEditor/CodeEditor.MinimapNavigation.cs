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
    private void OnScrollerScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (Math.Abs(_scroller.Offset.Y) > 0.1)
        {
            _scroller.Offset = new Vector(_scroller.Offset.X, 0);
        }

        UpdateVerticalScrollbar();
        UpdateMinimapViewport();
    }

    private void OnScrollerSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (_isAnchoringBottomDuringResize)
        {
            UpdateVerticalScrollbar();
            UpdateMinimapViewport();
            return;
        }

        if (!e.HeightChanged)
        {
            UpdateVerticalScrollbar();
            UpdateMinimapViewport();
            return;
        }

        var previousViewportHeight = Math.Max(0, e.PreviousSize.Height);
        var currentViewportHeight = Math.Max(0, ViewportHeight);
        var previousMaxOffset = Math.Max(0, _surface.ContentHeight - previousViewportHeight);
        var currentMaxOffset = Math.Max(0, _surface.ContentHeight - currentViewportHeight);
        var wasAtBottom = _verticalOffset >= previousMaxOffset - BottomAnchorTolerance;

        if (wasAtBottom && currentMaxOffset > 0)
        {
            _isAnchoringBottomDuringResize = true;
            _verticalOffset = currentMaxOffset;
            _isAnchoringBottomDuringResize = false;
        }
        else
        {
            _verticalOffset = Math.Clamp(_verticalOffset, 0, currentMaxOffset);
        }

        UpdateVerticalScrollbar();
        UpdateMinimapViewport();
    }

    private void OnMinimapPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(_minimap);
        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        var targetOffset = _minimap.GetEditorOffsetForPoint(point.Position);
        ScrollToEditorOffset(targetOffset);
        _isMinimapNavigating = true;
        _hasMinimapDragMoved = false;
        _minimapDragStartY = point.Position.Y;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    private void OnMinimapPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isMinimapNavigating)
        {
            return;
        }

        var point = e.GetCurrentPoint(_minimap);
        if (!_hasMinimapDragMoved && Math.Abs(point.Position.Y - _minimapDragStartY) < 2)
        {
            return;
        }

        _hasMinimapDragMoved = true;
        ScrollToEditorOffset(_minimap.GetEditorOffsetForTrackPoint(point.Position));
        e.Handled = true;
    }

    private void OnMinimapPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isMinimapNavigating)
        {
            return;
        }

        _isMinimapNavigating = false;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private void OnEditorPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (Math.Abs(e.Delta.Y) <= 0.01)
        {
            return;
        }

        ScrollToEditorOffset(_verticalOffset - e.Delta.Y * EffectiveEditorLineHeight * 3);
        e.Handled = true;
    }

    private void ScrollToEditorOffset(double offset)
    {
        var nextOffset = Math.Clamp(offset, 0, MaxVerticalOffset);
        if (Math.Abs(nextOffset - _verticalOffset) <= 0.1)
        {
            UpdateVerticalScrollbar();
            return;
        }

        _verticalOffset = nextOffset;
        UpdateVerticalScrollbar();
        UpdateMinimapViewport();
    }
}
