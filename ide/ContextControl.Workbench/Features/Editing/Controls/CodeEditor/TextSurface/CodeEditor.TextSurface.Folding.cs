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
        private void RebuildVisibleLines()
        {
            var visible = new List<VisibleRow>(_lines.Length);
            for (var index = 0; index < _lines.Length; index++)
            {
                visible.Add(VisibleRow.ForCodeLine(index));
                if (_collapsedStartLines.Contains(index)
                    && _foldsByStartLine.TryGetValue(index, out var fold)
                    && IsSummaryFoldEnabled(fold))
                {
                    if (_showFoldSummaryPreview)
                    {
                        visible.Add(VisibleRow.ForFoldSummary(fold));
                    }

                    index = Math.Min(fold.EndLine, _lines.Length - 1);
                }
            }

            _visibleRows = visible.Count == 0 ? [VisibleRow.ForCodeLine(0)] : visible.ToArray();
            UpdateGutterLayout();
        }

        private bool PruneCollapsedSummaryFolds()
        {
            if (_collapsedStartLines.Count == 0)
            {
                return false;
            }

            var removed = false;
            foreach (var startLine in _collapsedStartLines.ToArray())
            {
                if (!_foldsByStartLine.TryGetValue(startLine, out var fold) || !IsSummaryFoldEnabled(fold))
                {
                    _collapsedStartLines.Remove(startLine);
                    removed = true;
                }
            }

            return removed;
        }

        private bool UpdateGutterLayout()
        {
            var maxVisibleLineNumber = GetMaxVisibleLineNumber();
            var visibleDigits = maxVisibleLineNumber.ToString(CultureInfo.InvariantCulture).Length;
            var visibleGuideDepth = GetMaxVisibleGuideDepth();
            if (visibleDigits == _visibleLineDigits
                && visibleGuideDepth == _visibleGuideDepth
                && _layoutShowFoldArrows == _showFoldArrows
                && _layoutFoldArrowsInCodeEditor == _foldArrowsInCodeEditor
                && _layoutUseParentChildArrowIndentation == _useParentChildArrowIndentation
                && _layoutShowVerticalScopeLines == _showVerticalScopeLines)
            {
                return false;
            }

            _visibleLineDigits = visibleDigits;
            _visibleGuideDepth = visibleGuideDepth;
            _layoutShowFoldArrows = _showFoldArrows;
            _layoutFoldArrowsInCodeEditor = _foldArrowsInCodeEditor;
            _layoutUseParentChildArrowIndentation = _useParentChildArrowIndentation;
            _layoutShowVerticalScopeLines = _showVerticalScopeLines;
            var requiredNumberWidth = Math.Max(_visibleLineDigits * CharWidth, 2 * CharWidth);
            var gutterArrowReserve = _showFoldArrows && !_foldArrowsInCodeEditor ? GutterArrowColumnWidth : 0;
            _lineNumberRight = LineNumberLeftPadding + requiredNumberWidth;
            _gutterWidth = Math.Max(MinGutterWidth, _lineNumberRight + NumberAreaRightPadding + gutterArrowReserve);
            _foldColumnStart = _gutterWidth + TextStartPadding;
            _guideBaseX = _foldColumnStart + (FoldSlotWidth / 2);
            _arrowBaseX = _foldArrowsInCodeEditor
                ? _guideBaseX
                : ((_lineNumberRight + _gutterWidth) / 2);
            var guideSlots = _showVerticalScopeLines && _useParentChildArrowIndentation
                ? Math.Max(1, _visibleGuideDepth + 1)
                : 1;
            _textStart = _foldColumnStart + (guideSlots * FoldSlotWidth) + TextStartPadding;
            return true;
        }

        private int GetMaxVisibleLineNumber()
        {
            if (_visibleRows.Length == 0)
            {
                return 1;
            }

            var firstVisibleIndex = Math.Max(0, (int)Math.Floor((_viewportOffset - TopPadding) / LineHeight));
            var lastVisibleIndex = Math.Min(
                _visibleRows.Length - 1,
                (int)Math.Ceiling((_viewportOffset + _viewportHeight - TopPadding) / LineHeight));

            if (lastVisibleIndex < firstVisibleIndex)
            {
                lastVisibleIndex = firstVisibleIndex;
            }

            var maxVisible = 1;
            for (var index = firstVisibleIndex; index <= lastVisibleIndex; index++)
            {
                var row = _visibleRows[index];
                if (row.IsFoldSummary)
                {
                    continue;
                }

                maxVisible = Math.Max(maxVisible, row.LineIndex + 1);
            }

            return maxVisible;
        }

        private int GetMaxVisibleGuideDepth()
        {
            if (_visibleRows.Length == 0)
            {
                return 0;
            }

            var firstVisibleIndex = Math.Max(0, (int)Math.Floor((_viewportOffset - TopPadding) / LineHeight));
            var lastVisibleIndex = Math.Min(
                _visibleRows.Length - 1,
                (int)Math.Ceiling((_viewportOffset + _viewportHeight - TopPadding) / LineHeight));

            if (lastVisibleIndex < firstVisibleIndex)
            {
                lastVisibleIndex = firstVisibleIndex;
            }

            var depth = 0;
            for (var index = firstVisibleIndex; index <= lastVisibleIndex; index++)
            {
                var row = _visibleRows[index];
                var lineIndex = row.IsFoldSummary ? row.Fold.StartLine : row.LineIndex;
                var scopes = GetScopesForLine(lineIndex);
                for (var scopeIndex = 0; scopeIndex < scopes.Count; scopeIndex++)
                {
                    depth = Math.Max(depth, scopes[scopeIndex].NestingLevel);
                }

                if (!row.IsFoldSummary && _foldsByStartLine.TryGetValue(row.LineIndex, out var fold))
                {
                    depth = Math.Max(depth, fold.NestingLevel);
                }
            }

            return depth;
        }

        private static string[] NormalizeLines(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return [""];
            }

            return text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        }

        private static string DetectLineBreak(string text)
        {
            if (text.Contains("\r\n", StringComparison.Ordinal))
            {
                return "\r\n";
            }

            if (text.Contains('\n'))
            {
                return "\n";
            }

            if (text.Contains('\r'))
            {
                return "\r";
            }

            return Environment.NewLine;
        }

        private static Dictionary<int, FoldRegion> BuildFoldRegions(IReadOnlyList<string> lines)
        {
            var result = new Dictionary<int, FoldRegion>();
            var stack = new Stack<(int OpenLine, int OwnerLine, int OpenColumn, char OpenDelimiter, char CloseDelimiter)>();
            var inBlockComment = false;
            var inSingleQuote = false;
            var inDoubleQuote = false;
            var inVerbatimString = false;
            var escapeStringCharacter = false;

            for (var lineIndex = 0; lineIndex < lines.Count; lineIndex++)
            {
                var line = lines[lineIndex];
                for (var column = 0; column < line.Length; column++)
                {
                    var current = line[column];
                    var next = column + 1 < line.Length ? line[column + 1] : '\0';

                    if (inBlockComment)
                    {
                        if (current == '*' && next == '/')
                        {
                            inBlockComment = false;
                            column++;
                        }

                        continue;
                    }

                    if (inSingleQuote)
                    {
                        if (escapeStringCharacter)
                        {
                            escapeStringCharacter = false;
                            continue;
                        }

                        if (current == '\\')
                        {
                            escapeStringCharacter = true;
                            continue;
                        }

                        if (current == '\'')
                        {
                            inSingleQuote = false;
                        }

                        continue;
                    }

                    if (inDoubleQuote)
                    {
                        if (inVerbatimString)
                        {
                            if (current == '"' && next == '"')
                            {
                                column++;
                                continue;
                            }

                            if (current == '"')
                            {
                                inDoubleQuote = false;
                                inVerbatimString = false;
                            }
                        }
                        else
                        {
                            if (escapeStringCharacter)
                            {
                                escapeStringCharacter = false;
                                continue;
                            }

                            if (current == '\\')
                            {
                                escapeStringCharacter = true;
                                continue;
                            }

                            if (current == '"')
                            {
                                inDoubleQuote = false;
                            }
                        }

                        continue;
                    }

                    if (current == '/' && next == '/')
                    {
                        break;
                    }

                    if (current == '/' && next == '*')
                    {
                        inBlockComment = true;
                        column++;
                        continue;
                    }

                    if (current == '#')
                    {
                        var leading = line[..column];
                        if (string.IsNullOrWhiteSpace(leading))
                        {
                            break;
                        }
                    }

                    if (current == '\'')
                    {
                        inSingleQuote = true;
                        escapeStringCharacter = false;
                        continue;
                    }

                    if (current == '"')
                    {
                        var previous = column > 0 ? line[column - 1] : '\0';
                        var beforePrevious = column > 1 ? line[column - 2] : '\0';
                        inDoubleQuote = true;
                        inVerbatimString = previous == '@'
                            || (previous == '$' && beforePrevious == '@')
                            || (previous == '@' && beforePrevious == '$');
                        escapeStringCharacter = false;
                        continue;
                    }

                    if (TryGetOpenDelimiter(current, out var closeDelimiter))
                    {
                        stack.Push((lineIndex, FindFoldOwnerLine(lines, lineIndex, column, current), column, current, closeDelimiter));
                    }
                    else if (IsCloseDelimiter(current))
                    {
                        while (stack.Count > 0 && stack.Peek().CloseDelimiter != current)
                        {
                            stack.Pop();
                        }

                        if (stack.Count == 0)
                        {
                            continue;
                        }

                        var open = stack.Pop();
                        if (lineIndex > open.OwnerLine
                            && lineIndex > open.OpenLine
                            && (!result.TryGetValue(open.OwnerLine, out var existing) || lineIndex > existing.EndLine))
                        {
                            result[open.OwnerLine] = new FoldRegion(
                                open.OwnerLine,
                                lineIndex,
                                open.OpenLine,
                                open.OpenColumn,
                                column,
                                open.OpenDelimiter,
                                open.CloseDelimiter,
                                0);
                        }
                    }
                }

                if (inSingleQuote)
                {
                    inSingleQuote = false;
                    escapeStringCharacter = false;
                }

                if (inDoubleQuote && !inVerbatimString)
                {
                    inDoubleQuote = false;
                    escapeStringCharacter = false;
                }
            }

            return ApplyFoldNestingLevels(result);
        }

        private static Dictionary<int, FoldRegion> ApplyFoldNestingLevels(Dictionary<int, FoldRegion> foldsByStartLine)
        {
            if (foldsByStartLine.Count == 0)
            {
                return foldsByStartLine;
            }

            var ordered = foldsByStartLine.Values
                .OrderBy(fold => fold.StartLine)
                .ThenByDescending(fold => fold.EndLine)
                .ToList();
            var parentStack = new Stack<FoldRegion>();
            var result = new Dictionary<int, FoldRegion>(foldsByStartLine.Count);

            foreach (var fold in ordered)
            {
                while (parentStack.Count > 0 && !IsNestedFold(parentStack.Peek(), fold))
                {
                    parentStack.Pop();
                }

                var foldWithLevel = fold with { NestingLevel = parentStack.Count };
                result[fold.StartLine] = foldWithLevel;
                parentStack.Push(foldWithLevel);
            }

            return result;
        }

        private static bool IsNestedFold(FoldRegion parent, FoldRegion child)
        {
            if (child.StartLine <= parent.StartLine)
            {
                return false;
            }

            if (child.EndLine > parent.EndLine)
            {
                return false;
            }

            if (child.EndLine < child.StartLine)
            {
                return false;
            }

            if (child.StartLine == parent.EndLine)
            {
                return false;
            }

            return true;
        }

        private static bool TryGetOpenDelimiter(char value, out char closeDelimiter)
        {
            switch (value)
            {
                case '{':
                    closeDelimiter = '}';
                    return true;
                case '[':
                    closeDelimiter = ']';
                    return true;
                case '(':
                    closeDelimiter = ')';
                    return true;
                default:
                    closeDelimiter = '\0';
                    return false;
            }
        }

        private static bool IsCloseDelimiter(char value)
        {
            return value is '}' or ']' or ')';
        }

        private static int FindFoldOwnerLine(IReadOnlyList<string> lines, int braceLine, int braceColumn, char openDelimiter)
        {
            if (braceColumn > 0 && lines[braceLine][..braceColumn].Trim().Length > 0)
            {
                return braceLine;
            }

            for (var lineIndex = braceLine - 1; lineIndex >= 0; lineIndex--)
            {
                var text = lines[lineIndex].Trim();
                if (text.Length == 0)
                {
                    continue;
                }

                if (openDelimiter == '[' && !ShouldAttachArrayFoldToPreviousLine(text))
                {
                    return braceLine;
                }

                return lineIndex;
            }

            return braceLine;
        }

        private static bool ShouldAttachArrayFoldToPreviousLine(string previousLine)
        {
            var text = previousLine.TrimEnd();
            if (text.Length == 0)
            {
                return false;
            }

            if (text.EndsWith(",", StringComparison.Ordinal)
                || text.EndsWith("[", StringComparison.Ordinal)
                || text.EndsWith("{", StringComparison.Ordinal)
                || text.EndsWith("(", StringComparison.Ordinal))
            {
                return false;
            }

            if (text.EndsWith("=", StringComparison.Ordinal)
                || text.EndsWith(":", StringComparison.Ordinal)
                || text.EndsWith("=>", StringComparison.Ordinal))
            {
                return true;
            }

            var last = text[^1];
            return char.IsLetterOrDigit(last) || last is ')' or ']' or '"' or '\'';
        }

        private static int[][] BuildBracketColors(IReadOnlyList<string> lines)
        {
            var result = new int[lines.Count][];
            var stack = new Stack<int>();
            var depth = 0;

            for (var lineIndex = 0; lineIndex < lines.Count; lineIndex++)
            {
                var line = lines[lineIndex];
                var colors = new int[line.Length];
                Array.Fill(colors, -1);

                for (var column = 0; column < line.Length; column++)
                {
                    var current = line[column];
                    if (current is '{' or '[' or '(')
                    {
                        var color = depth % Palette.Brackets.Length;
                        colors[column] = color;
                        stack.Push(color);
                        depth++;
                    }
                    else if (current is '}' or ']' or ')')
                    {
                        depth = Math.Max(0, depth - 1);
                        colors[column] = stack.Count > 0 ? stack.Pop() : depth % Palette.Brackets.Length;
                    }
                }

                result[lineIndex] = colors;
            }

            return result;
        }
    }
}
