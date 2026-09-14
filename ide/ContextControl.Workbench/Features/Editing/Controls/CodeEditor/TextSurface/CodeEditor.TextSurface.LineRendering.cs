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
        private void DrawLineNumber(DrawingContext context, int number, double y)
        {
            var text = number.ToString(CultureInfo.InvariantCulture);
            DrawLineMarker(context, text, y);
        }

        private void DrawLineMarker(DrawingContext context, string text, double y)
        {
            var x = _lineNumberRight - (text.Length * CharWidth);
            DrawText(context, text, Palette.LineNumber, new Point(Math.Max(LineNumberLeftPadding, x), y + 1), LineNumberFontSize);
        }

        private void DrawFoldArrow(DrawingContext context, double y, FoldRegion fold, bool collapsed)
        {
            var arrowCenterX = GetFoldArrowCenterX(fold);
            var centerY = y + (LineHeight / 2) + 0.5;
            var buttonRect = new Rect(
                arrowCenterX - (FoldButtonSize / 2),
                centerY - (FoldButtonSize / 2),
                FoldButtonSize,
                FoldButtonSize);
            var buttonBorder = _showSummaryArrowBorders
                ? new Pen(GetFoldButtonBorderBrush(fold), 1)
                : null;
            context.DrawRectangle(
                null,
                buttonBorder,
                buttonRect,
                FoldButtonCornerRadius,
                FoldButtonCornerRadius);

            var pen = new Pen(Palette.FoldArrow, FoldArrowStroke);

            if (collapsed)
            {
                context.DrawLine(
                    pen,
                    new Point(arrowCenterX - FoldArrowCollapsedHalfWidth, centerY - FoldArrowCollapsedHalfHeight),
                    new Point(arrowCenterX + FoldArrowCollapsedHalfWidth, centerY));
                context.DrawLine(
                    pen,
                    new Point(arrowCenterX + FoldArrowCollapsedHalfWidth, centerY),
                    new Point(arrowCenterX - FoldArrowCollapsedHalfWidth, centerY + FoldArrowCollapsedHalfHeight));
                return;
            }

            context.DrawLine(
                pen,
                new Point(arrowCenterX - FoldArrowHalfWidth, centerY - FoldArrowHalfHeight),
                new Point(arrowCenterX, centerY + FoldArrowHalfHeight));
            context.DrawLine(
                pen,
                new Point(arrowCenterX, centerY + FoldArrowHalfHeight),
                new Point(arrowCenterX + FoldArrowHalfWidth, centerY - FoldArrowHalfHeight));
        }

        private IBrush GetFoldButtonBorderBrush(FoldRegion fold)
        {
            if (!_useColorfulFamilies)
            {
                return Palette.FoldButtonBorder;
            }

            return ApplyAlpha(Palette.Brackets[fold.NestingLevel % Palette.Brackets.Length], 220);
        }

        private void DrawCollapsedSummary(DrawingContext context, FoldRegion fold, double y)
        {
            var summaryStartColumn = GetSummaryStartColumn(fold);
            var summaryStartX = _textStart + Math.Max(0, summaryStartColumn * CharWidth);
            DrawText(context, BuildCollapsedSummaryText(fold), Palette.LineNumber, new Point(summaryStartX, y + 1), LineNumberFontSize);
        }

        private string BuildCollapsedSummaryText(FoldRegion fold)
        {
            var ownerText = fold.StartLine >= 0 && fold.StartLine < _lines.Length
                ? _lines[fold.StartLine].Trim()
                : string.Empty;
            var hiddenLoc = Math.Max(1, fold.EndLine - fold.StartLine);
            var itemCount = TryCountFoldItems(fold);
            var continuationCount = itemCount.HasValue
                ? (itemCount.Value == 1 ? "1 item" : $"{itemCount.Value} items")
                : $"{hiddenLoc} LOC";
            var type = GetFoldSummaryKindKey(fold, ownerText);
            var prefix = BuildCollapsedPrefix(fold, ownerText);
            var terminator = ownerText.EndsWith(';') ? ";" : string.Empty;

            return $"{type}: {prefix} … {continuationCount} {fold.CloseDelimiter}{terminator}";
        }

        private bool IsSummaryFoldEnabled(FoldRegion fold)
        {
            if (!_showFoldArrows)
            {
                return false;
            }

            var ownerText = fold.StartLine >= 0 && fold.StartLine < _lines.Length
                ? _lines[fold.StartLine].Trim()
                : string.Empty;
            return _summaryFoldKinds.Contains(GetFoldSummaryKindKey(fold, ownerText));
        }

        private double GetFoldArrowCenterX(FoldRegion fold)
        {
            if (!_foldArrowsInCodeEditor)
            {
                return _arrowBaseX;
            }

            var levelOffset = _useParentChildArrowIndentation
                ? fold.NestingLevel * FoldSlotWidth
                : 0;
            return _arrowBaseX + levelOffset;
        }

        private double GetScopeGuideX(FoldRegion fold)
        {
            var levelOffset = _useParentChildArrowIndentation
                ? fold.NestingLevel * FoldSlotWidth
                : 0;
            return Math.Floor(_guideBaseX + levelOffset) + 0.5;
        }

        private static string GetFoldSummaryKindKey(FoldRegion fold, string ownerText)
        {
            var normalized = ownerText.ToLowerInvariant();
            if (fold.OpenDelimiter == '[')
            {
                return "array";
            }

            if (fold.OpenDelimiter == '(')
            {
                return "arguments";
            }

            if (normalized.Contains(" class ", StringComparison.Ordinal) || normalized.StartsWith("class ", StringComparison.Ordinal))
            {
                return "class";
            }

            if (normalized.Contains(" struct ", StringComparison.Ordinal) || normalized.StartsWith("struct ", StringComparison.Ordinal))
            {
                return "struct";
            }

            if (normalized.Contains(" interface ", StringComparison.Ordinal) || normalized.StartsWith("interface ", StringComparison.Ordinal))
            {
                return "interface";
            }

            if (normalized.Contains(" enum ", StringComparison.Ordinal) || normalized.StartsWith("enum ", StringComparison.Ordinal))
            {
                return "enum";
            }

            if (normalized.Contains(" namespace ", StringComparison.Ordinal) || normalized.StartsWith("namespace ", StringComparison.Ordinal))
            {
                return "namespace";
            }

            if (normalized.Contains(" get ", StringComparison.Ordinal)
                || normalized.Contains(" set ", StringComparison.Ordinal)
                || normalized.StartsWith("get ", StringComparison.Ordinal)
                || normalized.StartsWith("set ", StringComparison.Ordinal))
            {
                return "property";
            }

            if (ownerText.Contains('(') && ownerText.Contains(')'))
            {
                return "method";
            }

            if (ownerText.Contains('='))
            {
                return "object";
            }

            return "block";
        }

        private static HashSet<string> ParseSummaryFoldKinds(string? value)
        {
            var allowed = AllSummaryFoldKinds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var source = value is null ? AllSummaryFoldKinds : value;
            var selected = source.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(allowed.Contains)
                .Select(item => item.ToLowerInvariant())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            return selected;
        }

        private static string BuildCollapsedPrefix(FoldRegion fold, string ownerText)
        {
            if (string.IsNullOrWhiteSpace(ownerText))
            {
                return fold.OpenDelimiter.ToString();
            }

            var openIndex = ownerText.IndexOf(fold.OpenDelimiter);
            if (openIndex >= 0)
            {
                return CompactWhitespace(TruncateCollapsedPreview(ownerText[..(openIndex + 1)]));
            }

            return CompactWhitespace(TruncateCollapsedPreview($"{ownerText} {fold.OpenDelimiter}"));
        }

        private static string CompactWhitespace(string value)
        {
            return string.Join(" ", value.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries));
        }

        private static string TruncateCollapsedPreview(string value)
        {
            const int maxLength = 74;
            if (value.Length <= maxLength)
            {
                return value;
            }

            var head = value[..50].TrimEnd();
            var tail = value[^18..].TrimStart();
            return $"{head} … {tail}";
        }

        private int GetSummaryStartColumn(FoldRegion fold)
        {
            if (fold.StartLine < 0 || fold.StartLine >= _lines.Length)
            {
                return Math.Max(0, fold.OpenColumn);
            }

            var ownerLine = _lines[fold.StartLine];
            for (var index = 0; index < ownerLine.Length; index++)
            {
                if (!char.IsWhiteSpace(ownerLine[index]))
                {
                    return index;
                }
            }

            return Math.Max(0, fold.OpenColumn);
        }

        private int? TryCountFoldItems(FoldRegion fold)
        {
            if (fold.OpenLine < 0
                || fold.OpenLine >= _lines.Length
                || fold.EndLine < fold.OpenLine
                || fold.OpenColumn < 0
                || fold.CloseColumn < 0)
            {
                return null;
            }

            var commaCount = 0;
            var hasValueToken = false;
            var nestedDepth = 0;
            var inSingleQuote = false;
            var inDoubleQuote = false;
            var isEscaped = false;

            for (var lineIndex = fold.OpenLine; lineIndex <= fold.EndLine; lineIndex++)
            {
                var line = _lines[lineIndex];
                var startColumn = lineIndex == fold.OpenLine
                    ? Math.Min(line.Length, fold.OpenColumn + 1)
                    : 0;
                var endColumnExclusive = lineIndex == fold.EndLine
                    ? Math.Min(line.Length, fold.CloseColumn)
                    : line.Length;

                for (var column = startColumn; column < endColumnExclusive; column++)
                {
                    var current = line[column];

                    if (inSingleQuote || inDoubleQuote)
                    {
                        if (isEscaped)
                        {
                            isEscaped = false;
                            continue;
                        }

                        if (current == '\\')
                        {
                            isEscaped = true;
                            continue;
                        }

                        if (inSingleQuote && current == '\'')
                        {
                            inSingleQuote = false;
                        }
                        else if (inDoubleQuote && current == '"')
                        {
                            inDoubleQuote = false;
                        }

                        continue;
                    }

                    if (current == '\'')
                    {
                        inSingleQuote = true;
                        continue;
                    }

                    if (current == '"')
                    {
                        inDoubleQuote = true;
                        continue;
                    }

                    if (TryGetOpenDelimiter(current, out _))
                    {
                        nestedDepth++;
                        continue;
                    }

                    if (IsCloseDelimiter(current))
                    {
                        if (nestedDepth > 0)
                        {
                            nestedDepth--;
                        }

                        continue;
                    }

                    if (nestedDepth != 0)
                    {
                        continue;
                    }

                    if (current == ',')
                    {
                        commaCount++;
                        continue;
                    }

                    if (!char.IsWhiteSpace(current))
                    {
                        hasValueToken = true;
                    }
                }
            }

            if (!hasValueToken)
            {
                return 0;
            }

            if (commaCount == 0)
            {
                return null;
            }

            return commaCount + 1;
        }

        private void DrawCodeLine(DrawingContext context, int lineIndex, string line, double y)
        {
            if (line.Length == 0)
            {
                return;
            }

            foreach (var segment in GetLineSegments(line, lineIndex))
            {
                DrawSegment(context, lineIndex, line, segment.Start, segment.Length, segment.Brush, y);
            }
        }

        private void DrawSegment(DrawingContext context, int lineIndex, string line, int start, int length, IBrush brush, double y)
        {
            if (length <= 0)
            {
                return;
            }

            var text = line.Substring(start, length);
            var x = _textStart + (start * CharWidth);
            if (IsMatrixConsoleSkin(_skinKey))
            {
                DrawMatrixSegment(context, text, lineIndex, start, brush, x, y);
                return;
            }

            DrawText(context, text, brush, new Point(x, y), FontSize);
        }

        private void DrawMatrixSegment(DrawingContext context, string text, int lineIndex, int startColumn, IBrush brush, double x, double y)
        {
            DrawText(context, text, BrushWithAlpha(brush, 78), new Point(x + 0.8, y), FontSize);
            DrawText(context, text, brush, new Point(x, y), FontSize);

            var tick = _animationPhase / 76;
            for (var index = 0; index < text.Length; index++)
            {
                var current = text[index];
                if (char.IsWhiteSpace(current))
                {
                    continue;
                }

                var column = startColumn + index;
                var shimmer = PositiveModulo(tick + (lineIndex * 17L) + (column * 7L), 41);
                if (shimmer > 2)
                {
                    continue;
                }

                var glyph = MatrixGlyphAt(lineIndex, column, tick).ToString();
                var jitterX = shimmer == 0 ? 0.62 : -0.34;
                var glyphX = x + (index * CharWidth);
                var glyphBrush = shimmer == 0 ? MatrixHotGlyphBrush : BrushWithAlpha(brush, 220);
                DrawText(context, glyph, MatrixGlyphHaloBrush, new Point(glyphX - 0.7, y - 0.2), FontSize);
                DrawText(context, glyph, glyphBrush, new Point(glyphX + jitterX, y), FontSize);
            }
        }

        private static char MatrixGlyphAt(int lineIndex, int column, long tick)
        {
            var index = PositiveModulo((lineIndex * 31L) + (column * 17L) + tick, MatrixCodeGlyphs.Length);
            return MatrixCodeGlyphs[index];
        }

        private RenderSegment[] GetLineSegments(string line, int lineIndex)
        {
            if (_lineSegmentCache.TryGetValue(lineIndex, out var cached))
            {
                return cached;
            }

            var brushes = BuildLineBrushes(line, lineIndex);
            var segments = new List<RenderSegment>(Math.Min(16, line.Length));
            var start = 0;
            var current = brushes[0];

            for (var index = 1; index < line.Length; index++)
            {
                if (ReferenceEquals(brushes[index], current))
                {
                    continue;
                }

                segments.Add(new RenderSegment(start, index - start, current));
                start = index;
                current = brushes[index];
            }

            segments.Add(new RenderSegment(start, line.Length - start, current));
            cached = segments.ToArray();
            _lineSegmentCache[lineIndex] = cached;
            return cached;
        }

        private IBrush[] BuildLineBrushes(string line, int lineIndex)
        {
            var brushes = new IBrush[line.Length];
            Array.Fill(brushes, Palette.Code);

            if (line.Length == 0)
            {
                return brushes;
            }

            var textMateSpans = GetTextMateSpans(line, lineIndex);
            if (textMateSpans.Count > 0)
            {
                foreach (var span in textMateSpans)
                {
                    Fill(brushes, span.Start, span.Length, span.Brush);
                }
            }
            else if (IsMarkupExtension(_extension))
            {
                HighlightMarkup(line, brushes);
            }
            else
            {
                HighlightStringsAndComments(line, brushes);
                HighlightWords(line, brushes);
            }

            if (lineIndex < _bracketColorIndexes.Length)
            {
                var bracketLine = _bracketColorIndexes[lineIndex];
                for (var index = 0; index < Math.Min(line.Length, bracketLine.Length); index++)
                {
                    var colorIndex = bracketLine[index];
                    if (colorIndex >= 0)
                    {
                        brushes[index] = Palette.Brackets[colorIndex % Palette.Brackets.Length];
                    }
                }
            }

            return brushes;
        }
    }
}
