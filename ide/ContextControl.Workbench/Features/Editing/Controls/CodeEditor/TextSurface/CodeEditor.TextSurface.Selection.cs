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
    private sealed partial class CodeTextSurface
    {
        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);

            var point = e.GetCurrentPoint(this);
            if (!point.Properties.IsLeftButtonPressed)
            {
                return;
            }

            Focus();
            var position = ToDocumentPoint(e.GetPosition(this));
            if (position.Y < TopPadding)
            {
                return;
            }

            var visibleRowIndex = (int)((position.Y - TopPadding) / LineHeight);
            if (visibleRowIndex < 0 || visibleRowIndex >= _visibleRows.Length)
            {
                return;
            }

            var row = _visibleRows[visibleRowIndex];
            if (row.IsFoldSummary)
            {
                return;
            }

            var lineIndex = row.LineIndex;
            SetActiveLine(lineIndex);

            if (_showFoldArrows
                && _foldsByStartLine.TryGetValue(lineIndex, out var fold)
                && IsSummaryFoldEnabled(fold)
                && IsInFoldArrowHitTarget(position, fold))
            {
                ToggleFold(lineIndex);
                e.Handled = true;
                return;
            }

            BeginSelection(position, e.Pointer);
            e.Handled = true;
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            if (_isSelecting)
            {
                UpdateSelection(ToDocumentPoint(e.GetPosition(this)));
                e.Handled = true;
                return;
            }

            SetActiveLine(_useColorfulFamilies || _showVerticalScopeLines ? GetCodeLineIndexFromPoint(ToDocumentPoint(e.GetPosition(this))) : -1);
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            if (!_isSelecting)
            {
                return;
            }

            UpdateSelection(ToDocumentPoint(e.GetPosition(this)));
            EndSelection(e.Pointer);
            e.Handled = true;
        }

        protected override void OnPointerExited(PointerEventArgs e)
        {
            base.OnPointerExited(e);
            if (!_isSelecting)
            {
                SetActiveLine(-1);
            }
        }

        protected override void OnLostFocus(RoutedEventArgs e)
        {
            base.OnLostFocus(e);
            EndSelection(null);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Handled)
            {
                return;
            }

            if (IsControlShortcut(e, Key.A))
            {
                SelectAll();
                e.Handled = true;
                return;
            }

            if (IsControlShortcut(e, Key.C) || IsControlShortcut(e, Key.Insert))
            {
                _ = CopySelectionAsync();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Escape && HasSelection)
            {
                ClearSelectionState();
                InvalidateVisual();
                e.Handled = true;
            }
        }

        private bool HasSelection =>
            _selectionAnchor is { } anchor
            && _selectionCaret is { } caret
            && CompareTextPositions(anchor, caret) != 0;

        private bool IsInFoldArrowHitTarget(Point position, FoldRegion fold)
        {
            var arrowCenterX = GetFoldArrowCenterX(fold);
            return position.X >= arrowCenterX - FoldArrowHitRadius
                && position.X <= arrowCenterX + FoldArrowHitRadius;
        }

        private void ToggleFold(int lineIndex)
        {
            if (!_collapsedStartLines.Add(lineIndex))
            {
                _collapsedStartLines.Remove(lineIndex);
            }

            RebuildVisibleLines();
            InvalidateMeasure();
            InvalidateVisual();
        }

        private void BeginSelection(Point position, IPointer pointer)
        {
            var textPosition = GetTextPositionFromPoint(position);
            _selectionAnchor = textPosition;
            _selectionCaret = textPosition;
            _isSelecting = true;
            RecordSelectionViewportPoint(position);
            UpdateSelectionAutoScroll(position);
            pointer.Capture(this);
            InvalidateVisual();
        }

        private void UpdateSelection(Point position, bool updateAutoScroll = true)
        {
            if (!_isSelecting && updateAutoScroll)
            {
                return;
            }

            var textPosition = GetTextPositionFromPoint(position);
            var changed = _selectionCaret != textPosition;
            _selectionCaret = textPosition;
            SetActiveLine(textPosition.Line);
            RecordSelectionViewportPoint(position);

            if (updateAutoScroll)
            {
                UpdateSelectionAutoScroll(position);
            }

            if (changed)
            {
                InvalidateVisual();
            }
        }

        private void EndSelection(IPointer? pointer)
        {
            if (!_isSelecting)
            {
                return;
            }

            _isSelecting = false;
            _hasSelectionViewportPoint = false;
            _selectionAutoScrollTimer.Stop();
            pointer?.Capture(null);
            if (!HasSelection)
            {
                ClearSelectionState();
            }

            InvalidateVisual();
        }

        private void ClearSelectionState()
        {
            _selectionAnchor = null;
            _selectionCaret = null;
            _isSelecting = false;
            _hasSelectionViewportPoint = false;
            _selectionAutoScrollDeltaY = 0;
            _selectionAutoScrollTimer.Stop();
        }

        private void SelectAll()
        {
            var lastLineIndex = Math.Max(0, _lines.Length - 1);
            _selectionAnchor = new TextPosition(0, 0);
            _selectionCaret = new TextPosition(lastLineIndex, SafeLineLength(lastLineIndex));
            _isSelecting = false;
            _hasSelectionViewportPoint = false;
            _selectionAutoScrollTimer.Stop();
            Focus();
            InvalidateVisual();
        }

        private async Task CopySelectionAsync()
        {
            if (!TryGetOrderedSelection(out var start, out var end))
            {
                return;
            }

            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard is null)
            {
                return;
            }

            try
            {
                await clipboard.SetTextAsync(GetSelectedText(start, end));
            }
            catch
            {
                // Clipboard can be unavailable if another process has it open.
            }
        }

        private string GetSelectedText(TextPosition start, TextPosition end)
        {
            if (IsEntireDocumentSelected(start, end))
            {
                return _documentText;
            }

            if (start.Line == end.Line)
            {
                var line = GetLine(start.Line);
                var startColumn = Math.Clamp(start.Column, 0, line.Length);
                var endColumn = Math.Clamp(end.Column, startColumn, line.Length);
                return line[startColumn..endColumn];
            }

            var builder = new StringBuilder();
            var firstLine = GetLine(start.Line);
            var firstColumn = Math.Clamp(start.Column, 0, firstLine.Length);
            builder.Append(firstLine[firstColumn..]);
            builder.Append(_lineBreak);

            for (var lineIndex = start.Line + 1; lineIndex < end.Line; lineIndex++)
            {
                builder.Append(GetLine(lineIndex));
                builder.Append(_lineBreak);
            }

            var lastLine = GetLine(end.Line);
            var lastColumn = Math.Clamp(end.Column, 0, lastLine.Length);
            builder.Append(lastLine[..lastColumn]);
            return builder.ToString();
        }

        private bool IsEntireDocumentSelected(TextPosition start, TextPosition end)
        {
            var lastLineIndex = Math.Max(0, _lines.Length - 1);
            return start.Line == 0
                && start.Column == 0
                && end.Line == lastLineIndex
                && end.Column == SafeLineLength(lastLineIndex);
        }

        private bool TryGetOrderedSelection(out TextPosition start, out TextPosition end)
        {
            start = default;
            end = default;
            if (_selectionAnchor is not { } anchor || _selectionCaret is not { } caret)
            {
                return false;
            }

            if (CompareTextPositions(anchor, caret) <= 0)
            {
                start = anchor;
                end = caret;
            }
            else
            {
                start = caret;
                end = anchor;
            }

            return CompareTextPositions(start, end) != 0;
        }

        private TextPosition GetTextPositionFromPoint(Point position)
        {
            if (_visibleRows.Length == 0)
            {
                return new TextPosition(0, 0);
            }

            var visibleRowIndex = (int)Math.Floor((position.Y - TopPadding) / LineHeight);
            visibleRowIndex = Math.Clamp(visibleRowIndex, 0, _visibleRows.Length - 1);
            var row = _visibleRows[visibleRowIndex];
            if (row.IsFoldSummary)
            {
                return GetFoldSummaryTextPosition(row, position);
            }

            var lineIndex = Math.Clamp(row.LineIndex, 0, Math.Max(0, _lines.Length - 1));
            return new TextPosition(lineIndex, GetColumnFromPoint(lineIndex, position.X));
        }

        private TextPosition GetFoldSummaryTextPosition(VisibleRow row, Point position)
        {
            var foldStart = Math.Clamp(row.Fold.StartLine, 0, Math.Max(0, _lines.Length - 1));
            var foldEnd = Math.Clamp(row.Fold.EndLine, foldStart, Math.Max(0, _lines.Length - 1));
            var midpoint = _textStart + (GetSummaryStartColumn(row.Fold) * CharWidth);
            return position.X < midpoint
                ? new TextPosition(foldStart, 0)
                : new TextPosition(foldEnd, SafeLineLength(foldEnd));
        }

        private int GetColumnFromPoint(int lineIndex, double x)
        {
            var lineLength = SafeLineLength(lineIndex);
            if (x <= _textStart)
            {
                return 0;
            }

            var column = (int)Math.Round((x - _textStart) / CharWidth, MidpointRounding.AwayFromZero);
            return Math.Clamp(column, 0, lineLength);
        }

        private void DrawSelection(DrawingContext context, VisibleRow row, double y)
        {
            if (!TryGetOrderedSelection(out var start, out var end))
            {
                return;
            }

            if (row.IsFoldSummary)
            {
                DrawFoldSummarySelection(context, row, y, start, end);
                return;
            }

            DrawLineSelection(context, row.LineIndex, y, start, end);
        }

        private void DrawLineSelection(DrawingContext context, int lineIndex, double y, TextPosition start, TextPosition end)
        {
            if (lineIndex < start.Line || lineIndex > end.Line)
            {
                return;
            }

            var lineLength = SafeLineLength(lineIndex);
            var startColumn = lineIndex == start.Line ? Math.Clamp(start.Column, 0, lineLength) : 0;
            var endColumn = lineIndex == end.Line ? Math.Clamp(end.Column, 0, lineLength) : lineLength + 1;
            var widthColumns = endColumn - startColumn;
            if (widthColumns <= 0)
            {
                return;
            }

            var x = _textStart + (startColumn * CharWidth);
            var availableWidth = Math.Max(0, Bounds.Width - x);
            if (availableWidth <= 0)
            {
                return;
            }

            var width = Math.Min(availableWidth, Math.Max(CharWidth * 0.65, widthColumns * CharWidth));
            context.DrawRectangle(Palette.SelectionBackground, null, new Rect(x, y, width, LineHeight));
        }

        private void DrawFoldSummarySelection(DrawingContext context, VisibleRow row, double y, TextPosition start, TextPosition end)
        {
            var foldStart = new TextPosition(row.Fold.StartLine, 0);
            var foldEnd = new TextPosition(row.Fold.EndLine, SafeLineLength(row.Fold.EndLine));
            if (CompareTextPositions(end, foldStart) <= 0 || CompareTextPositions(start, foldEnd) >= 0)
            {
                return;
            }

            var x = _textStart + (GetSummaryStartColumn(row.Fold) * CharWidth);
            var availableWidth = Math.Max(0, Bounds.Width - x);
            if (availableWidth <= 0)
            {
                return;
            }

            var width = Math.Min(availableWidth, Math.Max(CharWidth, BuildCollapsedSummaryText(row.Fold).Length * CharWidth));
            context.DrawRectangle(Palette.SelectionBackground, null, new Rect(x, y, width, LineHeight));
        }

        private void RecordSelectionViewportPoint(Point documentPoint)
        {
            var horizontalOffset = _scrollHost?.Offset.X ?? 0;
            _lastSelectionViewportPoint = new Point(documentPoint.X - horizontalOffset, documentPoint.Y - _viewportOffset);
            _hasSelectionViewportPoint = true;
        }

        private void UpdateSelectionFromViewportPoint()
        {
            if (!_isSelecting || !_hasSelectionViewportPoint)
            {
                return;
            }

            var horizontalOffset = _scrollHost?.Offset.X ?? 0;
            var verticalOffset = _verticalOffsetProvider?.Invoke() ?? _viewportOffset;
            UpdateSelection(new Point(horizontalOffset + _lastSelectionViewportPoint.X, verticalOffset + _lastSelectionViewportPoint.Y), updateAutoScroll: false);
        }

        private void UpdateSelectionAutoScroll(Point documentPoint)
        {
            if (_viewportHeight <= 0 || _verticalScrollRequester is null)
            {
                _selectionAutoScrollTimer.Stop();
                return;
            }

            var viewportY = documentPoint.Y - _viewportOffset;
            var delta = 0.0;
            if (viewportY < SelectionEdgeScrollZone)
            {
                delta = -ComputeSelectionScrollStep(SelectionEdgeScrollZone - viewportY);
            }
            else if (viewportY > _viewportHeight - SelectionEdgeScrollZone)
            {
                delta = ComputeSelectionScrollStep(viewportY - (_viewportHeight - SelectionEdgeScrollZone));
            }

            _selectionAutoScrollDeltaY = delta;
            if (Math.Abs(delta) < 0.1)
            {
                _selectionAutoScrollTimer.Stop();
            }
            else if (!_selectionAutoScrollTimer.IsEnabled)
            {
                _selectionAutoScrollTimer.Start();
            }
        }

        private void OnSelectionAutoScrollTick(object? sender, EventArgs e)
        {
            if (!_isSelecting || _verticalScrollRequester is null)
            {
                _selectionAutoScrollTimer.Stop();
                return;
            }

            var currentOffset = _verticalOffsetProvider?.Invoke() ?? _viewportOffset;
            var maxOffset = Math.Max(0, _maxVerticalOffsetProvider?.Invoke() ?? 0);
            var nextY = Math.Clamp(currentOffset + _selectionAutoScrollDeltaY, 0, maxOffset);
            if (Math.Abs(nextY - currentOffset) >= 0.1)
            {
                _verticalScrollRequester(nextY);
            }

            UpdateSelectionFromViewportPoint();
        }

        private static double ComputeSelectionScrollStep(double distance)
        {
            var ratio = Math.Clamp(distance / SelectionEdgeScrollZone, 0, 4);
            return Math.Clamp(SelectionMinScrollStep + (ratio * 18), SelectionMinScrollStep, SelectionMaxScrollStep);
        }

        private static bool IsControlShortcut(KeyEventArgs e, Key key)
        {
            return e.Key == key
                && (e.KeyModifiers & KeyModifiers.Control) == KeyModifiers.Control
                && (e.KeyModifiers & KeyModifiers.Alt) == 0;
        }

        private string GetLine(int lineIndex)
        {
            if (lineIndex < 0 || lineIndex >= _lines.Length)
            {
                return string.Empty;
            }

            return _lines[lineIndex];
        }

        private int SafeLineLength(int lineIndex)
        {
            return lineIndex >= 0 && lineIndex < _lines.Length ? _lines[lineIndex].Length : 0;
        }

        private static int CompareTextPositions(TextPosition left, TextPosition right)
        {
            var lineComparison = left.Line.CompareTo(right.Line);
            return lineComparison != 0 ? lineComparison : left.Column.CompareTo(right.Column);
        }

        private int GetCodeLineIndexFromPoint(Point position)
        {
            if (position.Y < TopPadding)
            {
                return -1;
            }

            var visibleRowIndex = (int)((position.Y - TopPadding) / LineHeight);
            if (visibleRowIndex < 0 || visibleRowIndex >= _visibleRows.Length)
            {
                return -1;
            }

            var row = _visibleRows[visibleRowIndex];
            return row.IsFoldSummary ? row.Fold.StartLine : row.LineIndex;
        }

        private Point ToDocumentPoint(Point viewportPoint)
        {
            return new Point(viewportPoint.X, viewportPoint.Y + _viewportOffset);
        }

        private void SetActiveLine(int lineIndex)
        {
            if (!_useColorfulFamilies && !_showVerticalScopeLines)
            {
                lineIndex = -1;
            }

            if (_activeLineIndex == lineIndex)
            {
                return;
            }

            _activeLineIndex = lineIndex;
            InvalidateVisual();
        }
    }
}
