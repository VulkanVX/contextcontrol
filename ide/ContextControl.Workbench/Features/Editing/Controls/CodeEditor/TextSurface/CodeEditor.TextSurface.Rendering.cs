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
        public override void Render(DrawingContext context)
        {
            base.Render(context);

            var viewportTop = 0.0;
            var viewportBottom = Math.Max(LineHeight, Bounds.Height);
            var viewportRect = new Rect(0, viewportTop, Bounds.Width, viewportBottom - viewportTop);
            var useMatrixSkin = IsMatrixConsoleSkin(_skinKey);
            context.DrawRectangle(Palette.Background, null, viewportRect);
            if (useMatrixSkin)
            {
                DrawCrtBackdrop(context, viewportRect);
            }

            context.DrawRectangle(Palette.GutterBackground, null, new Rect(0, viewportTop, _gutterWidth, viewportRect.Height));
            context.DrawLine(new Pen(Palette.GutterRule, 1), new Point(_gutterWidth - 0.5, viewportTop), new Point(_gutterWidth - 0.5, viewportBottom));

            var firstVisibleIndex = Math.Max(0, (int)Math.Floor((_viewportOffset - TopPadding) / LineHeight) - 2);
            var lastVisibleIndex = Math.Min(
                _visibleRows.Length - 1,
                (int)Math.Ceiling((_viewportOffset + _viewportHeight - TopPadding) / LineHeight) + 2);
            var activeScopes = _useColorfulFamilies || _showVerticalScopeLines
                ? BuildActiveScopeSet()
                : new HashSet<int>();

            for (var visibleIndex = firstVisibleIndex; visibleIndex <= lastVisibleIndex; visibleIndex++)
            {
                var row = _visibleRows[visibleIndex];
                var y = TopPadding + (visibleIndex * LineHeight) - _viewportOffset;
                if (row.IsFoldSummary)
                {
                    var summaryScopeLine = GetSummaryScopeProbeLine(row.Fold);
                    if (_useColorfulFamilies)
                    {
                        DrawScopeLineBackground(context, summaryScopeLine, y, activeScopes, treatLineChangeAsTransparent: true);
                    }

                    if (_showVerticalScopeLines)
                    {
                        DrawScopeGuides(context, summaryScopeLine, y, activeScopes);
                    }

                    DrawSelection(context, row, y);
                    DrawLineMarker(context, $"{row.Fold.OpenDelimiter}{row.Fold.CloseDelimiter}", y);
                    if (_showFoldSummaryPreview)
                    {
                        DrawCollapsedSummary(context, row.Fold, y);
                    }

                    continue;
                }

                var lineIndex = row.LineIndex;
                if (_useColorfulFamilies)
                {
                    DrawScopeLineBackground(context, lineIndex, y, activeScopes, treatLineChangeAsTransparent: false);
                }

                if (_showVerticalScopeLines)
                {
                    DrawScopeGuides(context, lineIndex, y, activeScopes);
                }

                DrawLineChangeBackground(context, lineIndex, y);
                DrawSelection(context, row, y);
                DrawLineNumber(context, lineIndex + 1, y);

                if (_showFoldArrows
                    && _foldsByStartLine.TryGetValue(lineIndex, out var fold)
                    && IsSummaryFoldEnabled(fold))
                {
                    DrawFoldArrow(context, y, fold, _collapsedStartLines.Contains(fold.StartLine));
                }

                DrawCodeLine(context, lineIndex, _lines[lineIndex], y);
            }

            if (useMatrixSkin)
            {
                DrawCrtOverlay(context, viewportRect);
            }
        }

        private void DrawCrtBackdrop(DrawingContext context, Rect viewportRect)
        {
            context.DrawRectangle(CrtTintBrush, null, viewportRect);
            var sweepOffset = PositiveModulo(_animationPhase / 22, Math.Max(1, (int)Math.Ceiling(viewportRect.Height)));
            var sweepY = viewportRect.Y + sweepOffset;
            context.DrawRectangle(CrtSweepBrush, null, new Rect(viewportRect.X, sweepY, viewportRect.Width, 2));
        }

        private void DrawCrtOverlay(DrawingContext context, Rect viewportRect)
        {
            var width = Math.Max(0, viewportRect.Width);
            var firstScanline = viewportRect.Y + PositiveModulo(_animationPhase / 48, 4);
            for (var y = firstScanline; y < viewportRect.Bottom; y += 4)
            {
                context.DrawRectangle(CrtScanlineBrush, null, new Rect(viewportRect.X, Math.Floor(y), width, 1));
            }

            var firstFineLine = viewportRect.Y + PositiveModulo(_animationPhase / 85, 11);
            for (var y = firstFineLine; y < viewportRect.Bottom; y += 11)
            {
                context.DrawRectangle(CrtFineScanlineBrush, null, new Rect(viewportRect.X, Math.Floor(y), width, 1));
            }

            var edge = Math.Min(34, Math.Max(12, viewportRect.Height * 0.08));
            context.DrawRectangle(CrtEdgeShadowBrush, null, new Rect(viewportRect.X, viewportRect.Y, width, edge));
            context.DrawRectangle(CrtEdgeShadowBrush, null, new Rect(viewportRect.X, viewportRect.Bottom - edge, width, edge));
            context.DrawRectangle(CrtEdgeShadowBrush, null, new Rect(viewportRect.X, viewportRect.Y, 12, viewportRect.Height));
            context.DrawRectangle(CrtEdgeShadowBrush, null, new Rect(viewportRect.Right - 12, viewportRect.Y, 12, viewportRect.Height));
        }

        private void DrawLineChangeBackground(DrawingContext context, int lineIndex, double y)
        {
            if (!_lineChanges.TryGetValue(lineIndex, out var change))
            {
                return;
            }

            var brush = change == "delete" ? Palette.DeleteLineBackground : Palette.AddLineBackground;
            var stripe = change == "delete" ? Palette.DeleteStripe : Palette.AddStripe;
            context.DrawRectangle(brush, null, new Rect(_gutterWidth, y, Math.Max(0, Bounds.Width - _gutterWidth), LineHeight));
            context.DrawRectangle(stripe, null, new Rect(_gutterWidth, y + 2, 2, LineHeight - 4));
        }

        private HashSet<int> BuildActiveScopeSet()
        {
            var active = new HashSet<int>();
            if (_activeLineIndex < 0)
            {
                return active;
            }

            foreach (var scope in GetScopesForLine(_activeLineIndex))
            {
                active.Add(scope.StartLine);
            }

            return active;
        }

        private void DrawScopeLineBackground(
            DrawingContext context,
            int lineIndex,
            double y,
            HashSet<int> activeScopeStarts,
            bool treatLineChangeAsTransparent)
        {
            if (!treatLineChangeAsTransparent && _lineChanges.ContainsKey(lineIndex))
            {
                return;
            }

            var scopes = GetScopesForLine(lineIndex);
            if (scopes.Count == 0)
            {
                return;
            }

            var lineWidth = Math.Max(0, Bounds.Width - _gutterWidth);
            if (lineWidth <= 0)
            {
                return;
            }

            var right = _gutterWidth + lineWidth;
            if (!_useParentChildArrowIndentation)
            {
                var laneStartX = GetUnifiedScopeGuideX();
                if (laneStartX >= right)
                {
                    return;
                }

                var dominantScope = GetDominantScope(scopes, activeScopeStarts);
                var active = activeScopeStarts.Contains(dominantScope.StartLine);
                context.DrawRectangle(
                    GetScopeLineFill(dominantScope.NestingLevel, active),
                    null,
                    new Rect(laneStartX, y, right - laneStartX, LineHeight));
                return;
            }

            for (var index = 0; index < scopes.Count; index++)
            {
                var scope = scopes[index];
                var laneStartX = GetScopeGuideX(scope);
                if (laneStartX >= right)
                {
                    continue;
                }

                var laneEndX = index + 1 < scopes.Count
                    ? Math.Min(right, GetScopeGuideX(scopes[index + 1]))
                    : right;
                if (laneEndX <= laneStartX)
                {
                    laneEndX = Math.Min(right, laneStartX + FoldSlotWidth);
                }

                var active = activeScopeStarts.Contains(scope.StartLine);
                var brush = GetScopeLineFill(scope.NestingLevel, active);
                context.DrawRectangle(
                    brush,
                    null,
                    new Rect(laneStartX, y, laneEndX - laneStartX, LineHeight));
            }
        }

        private void DrawScopeGuides(DrawingContext context, int lineIndex, double y, HashSet<int> activeScopeStarts)
        {
            var scopes = GetScopesForLine(lineIndex);
            if (scopes.Count == 0)
            {
                return;
            }

            var top = y + ScopeGuideTopInset;
            var bottom = y + LineHeight - ScopeGuideBottomInset;
            if (!_useParentChildArrowIndentation)
            {
                var active = scopes.Any(scope => activeScopeStarts.Contains(scope.StartLine));
                var guideX = GetUnifiedScopeGuideX();
                var guideTop = GetGuideTopAfterFoldButton(lineIndex, top, bottom);
                DrawVerticalGuide(context, GetUnifiedScopeGuidePen(active), guideX, guideTop, bottom);
                return;
            }

            for (var index = 0; index < scopes.Count; index++)
            {
                var scope = scopes[index];
                var active = activeScopeStarts.Contains(scope.StartLine);
                var guideX = GetScopeGuideX(scope);
                var guideTop = scope.StartLine == lineIndex
                    ? GetGuideTopAfterFoldButton(lineIndex, top, bottom)
                    : top;
                DrawVerticalGuide(context, GetScopeGuidePen(scope.NestingLevel, active), guideX, guideTop, bottom);
            }
        }

        private IReadOnlyList<FoldRegion> GetScopesForLine(int lineIndex)
        {
            if (lineIndex < 0 || lineIndex >= _lines.Length || _foldRegions.Length == 0)
            {
                return [];
            }

            if (_scopesByLineCache.TryGetValue(lineIndex, out var cached))
            {
                return cached;
            }

            var scopes = new List<FoldRegion>();
            for (var index = 0; index < _foldRegions.Length; index++)
            {
                var fold = _foldRegions[index];
                if (fold.StartLine > lineIndex)
                {
                    break;
                }

                if (fold.EndLine >= lineIndex)
                {
                    scopes.Add(fold);
                }
            }

            cached = scopes.Count == 0
                ? []
                : scopes
                    .OrderBy(fold => fold.NestingLevel)
                    .ThenBy(fold => fold.StartLine)
                    .ToArray();
            _scopesByLineCache[lineIndex] = cached;
            return cached;
        }

        private static FoldRegion GetDominantScope(IReadOnlyList<FoldRegion> scopes, HashSet<int> activeScopeStarts)
        {
            for (var index = scopes.Count - 1; index >= 0; index--)
            {
                if (activeScopeStarts.Contains(scopes[index].StartLine))
                {
                    return scopes[index];
                }
            }

            return scopes[^1];
        }

        private IBrush GetScopeLineFill(int level, bool active)
        {
            var cache = active ? _activeScopeLineFills : _scopeLineFills;
            if (cache.TryGetValue(level, out var brush))
            {
                return brush;
            }

            brush = ApplyAlpha(Palette.Brackets[level % Palette.Brackets.Length], active ? ActiveScopeLineFillAlpha : ScopeLineFillAlpha);
            cache[level] = brush;
            return brush;
        }

        private static int GetSummaryScopeProbeLine(FoldRegion fold)
        {
            if (fold.EndLine > fold.StartLine)
            {
                return fold.StartLine + 1;
            }

            return fold.StartLine;
        }

        private Pen GetScopeGuidePen(int level, bool active)
        {
            var cache = active ? _activeScopeGuidePens : _scopeGuidePens;
            if (cache.TryGetValue(level, out var pen))
            {
                return pen;
            }

            var baseBrush = _useColorfulFamilies
                ? Palette.Brackets[level % Palette.Brackets.Length]
                : Palette.FoldArrow;
            var brush = ApplyAlpha(baseBrush, active ? ActiveScopeGuideAlpha : ScopeGuideAlpha);
            pen = new Pen(brush, active ? ActiveScopeGuideThickness : ScopeGuideThickness);
            cache[level] = pen;
            return pen;
        }

        private Pen GetUnifiedScopeGuidePen(bool active)
        {
            if (active)
            {
                return _activeUnifiedScopeGuidePen ??= new Pen(
                    ApplyAlpha(Palette.FoldArrow, ActiveUnifiedScopeGuideAlpha),
                    ActiveScopeGuideThickness);
            }

            return _unifiedScopeGuidePen ??= new Pen(
                ApplyAlpha(Palette.FoldArrow, UnifiedScopeGuideAlpha),
                ScopeGuideThickness);
        }

        private double GetUnifiedScopeGuideX()
        {
            return Math.Floor(_guideBaseX) + 0.5;
        }

        private double GetGuideTopAfterFoldButton(int lineIndex, double top, double bottom)
        {
            if (!_showFoldArrows
                || !_foldArrowsInCodeEditor
                || !_foldsByStartLine.TryGetValue(lineIndex, out var fold)
                || !IsSummaryFoldEnabled(fold))
            {
                return top;
            }

            var centerY = top + (LineHeight / 2) + 0.5;
            var buttonBottom = centerY + (FoldButtonSize / 2);
            return Math.Min(bottom, Math.Max(top, buttonBottom));
        }

        private static void DrawVerticalGuide(DrawingContext context, Pen pen, double x, double top, double bottom)
        {
            if (bottom <= top)
            {
                return;
            }

            context.DrawLine(pen, new Point(x, top), new Point(x, bottom));
        }

        private static IBrush ApplyAlpha(IBrush brush, byte alpha)
        {
            if (brush is not ISolidColorBrush solid)
            {
                return brush;
            }

            var color = solid.Color;
            return new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
        }

    }
}
