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
    private sealed class CodeMinimap : Control
    {
        private const double HorizontalPadding = 2;
        private const double TopPadding = EditorTopPadding;
        private const double BottomPadding = EditorBottomPadding;
        private const double MiniLineHeight = 2.45;
        private const double MiniFontSize = 1.95;
        private const double MiniCharWidth = 0.92;
        private static readonly IBrush MiniCrtScanlineBrush = Brush(0, 0, 0, 76);
        private static readonly IBrush MiniCrtTintBrush = Brush(0, 255, 102, 24);

        private readonly Dictionary<int, List<TokenSpan>> _textMateSpanCache = [];
        private readonly Dictionary<int, RenderSegment[]> _lineSegmentCache = [];
        private IReadOnlyDictionary<int, string> _lineChanges = new Dictionary<int, string>();
        private string[] _lines = [""];
        private string _extension = "";
        private IGrammar? _grammar;
        private int[][] _bracketColorIndexes = [];
        private double _viewportOffset;
        private double _viewportHeight;
        private double _extentHeight;
        private double _editorLineHeight = DefaultEditorLineHeight;
        private string _skinKey = "default";
        private long _animationPhase;

        public CodeMinimap()
        {
            ClipToBounds = true;
        }

        public void SetDocument(string text, string path, IReadOnlyDictionary<int, string>? lineChanges)
        {
            _extension = Path.GetExtension(path).ToLowerInvariant();
            _lineChanges = lineChanges ?? new Dictionary<int, string>();
            _lines = string.IsNullOrEmpty(text)
                ? [""]
                : text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');

            _textMateSpanCache.Clear();
            _lineSegmentCache.Clear();
            ConfigureGrammar();
            _bracketColorIndexes = BuildMiniBracketColors(_lines);
            InvalidateMeasure();
            InvalidateVisual();
        }

        public void SetViewport(double offset, double viewportHeight, double extentHeight)
        {
            _viewportOffset = Math.Max(0, offset);
            _viewportHeight = Math.Max(_editorLineHeight, viewportHeight);
            _extentHeight = Math.Max(0, extentHeight);
            InvalidateVisual();
        }

        public void SetEditorLineHeight(double lineHeight)
        {
            var next = Math.Max(12.0, lineHeight);
            if (Math.Abs(_editorLineHeight - next) <= 0.01)
            {
                return;
            }

            _editorLineHeight = next;
            InvalidateVisual();
        }

        public void ApplyTheme()
        {
            _textMateSpanCache.Clear();
            _lineSegmentCache.Clear();
            InvalidateVisual();
        }

        public void SetSkin(string? skinKey)
        {
            var normalizedSkin = NormalizeSkinKey(skinKey);
            if (string.Equals(_skinKey, normalizedSkin, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _skinKey = normalizedSkin;
            InvalidateVisual();
        }

        public void SetAnimationPhase(long phase)
        {
            _animationPhase = phase;
            if (IsMatrixConsoleSkin(_skinKey))
            {
                InvalidateVisual();
            }
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            var height = double.IsInfinity(availableSize.Height) ? 0 : availableSize.Height;
            return new Size(MinimapWidth, height);
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);

            var bounds = Bounds;
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                return;
            }

            var content = new Rect(
                HorizontalPadding,
                0,
                Math.Max(0, bounds.Width - (HorizontalPadding * 2)),
                bounds.Height);

            context.DrawRectangle(Palette.Background, null, bounds);
            var miniScrollOffset = GetMiniScrollOffset(bounds.Height);
            using (context.PushClip(content))
            {
                DrawDocument(context, content, miniScrollOffset);
            }

            DrawViewport(context, content, miniScrollOffset);
            if (IsMatrixConsoleSkin(_skinKey))
            {
                DrawMiniCrtOverlay(context, bounds);
            }
        }

        private void DrawMiniCrtOverlay(DrawingContext context, Rect bounds)
        {
            context.DrawRectangle(MiniCrtTintBrush, null, bounds);
            var firstScanline = PositiveModulo(_animationPhase / 52, 5);
            for (var y = firstScanline; y < bounds.Height; y += 5)
            {
                context.DrawRectangle(MiniCrtScanlineBrush, null, new Rect(0, y, bounds.Width, 1));
            }
        }

        private double GetMiniContentHeight()
        {
            return TopPadding + (_lines.Length * MiniLineHeight) + BottomPadding;
        }

        private double GetMiniScrollOffset(double height)
        {
            var contentHeight = GetMiniContentHeight();
            var maxMiniOffset = Math.Max(0, contentHeight - height);
            var maxEditorOffset = Math.Max(0, _extentHeight - _viewportHeight);
            if (maxMiniOffset <= 0 || maxEditorOffset <= 0)
            {
                return 0;
            }

            return Math.Clamp(_viewportOffset / maxEditorOffset, 0, 1) * maxMiniOffset;
        }

        private void DrawDocument(DrawingContext context, Rect content, double miniScrollOffset)
        {
            var firstLine = Math.Max(0, (int)Math.Floor((miniScrollOffset - TopPadding) / MiniLineHeight) - 2);
            var lastLine = Math.Min(
                _lines.Length - 1,
                (int)Math.Ceiling((miniScrollOffset + content.Height - TopPadding) / MiniLineHeight) + 2);

            for (var lineIndex = firstLine; lineIndex <= lastLine; lineIndex++)
            {
                var y = TopPadding + (lineIndex * MiniLineHeight) - miniScrollOffset;
                DrawMiniLine(context, content, lineIndex, y);
            }
        }

        private void DrawMiniLine(DrawingContext context, Rect content, int lineIndex, double y)
        {
            if (lineIndex < 0 || lineIndex >= _lines.Length || y > content.Bottom)
            {
                return;
            }

            if (_lineChanges.TryGetValue(lineIndex, out var change))
            {
                var brush = change == "delete" ? Palette.MinimapDelete : Palette.MinimapAdd;
                context.DrawRectangle(brush, null, new Rect(content.X, y, content.Width, Math.Max(1.4, MiniLineHeight)));
            }

            var line = _lines[lineIndex];
            if (line.Length == 0)
            {
                return;
            }

            var visibleColumns = Math.Min(line.Length, (int)Math.Ceiling(content.Width / MiniCharWidth) + 2);
            foreach (var segment in GetLineSegments(line, lineIndex))
            {
                var start = Math.Max(0, segment.Start);
                var end = Math.Min(visibleColumns, segment.Start + segment.Length);
                if (end <= start)
                {
                    continue;
                }

                var text = line.Substring(start, end - start);
                var x = content.X + (start * MiniCharWidth);
                DrawText(context, text, segment.Brush, new Point(x, y), MiniFontSize);
            }
        }

        private void DrawViewport(DrawingContext context, Rect content, double miniScrollOffset)
        {
            if (_extentHeight <= 0 || _viewportHeight <= 0 || _lines.Length == 0)
            {
                return;
            }

            var lineOffset = Math.Max(0, (_viewportOffset - TopPadding) / _editorLineHeight);
            var top = TopPadding + (lineOffset * MiniLineHeight) - miniScrollOffset;
            var height = ClampViewportHeight(
                (_viewportHeight / _editorLineHeight) * MiniLineHeight,
                7,
                Math.Min(content.Height, GetMiniContentHeight()));
            if (top + height > content.Bottom)
            {
                top = content.Bottom - height;
            }

            var rect = new Rect(content.X - 1, Math.Max(0, top), content.Width + 2, Math.Min(content.Height, height));
            context.DrawRectangle(Palette.MinimapViewport, new Pen(Palette.MinimapViewportBorder, 1), rect, 2, 2);
        }

        public double GetEditorOffsetForPoint(Point point)
        {
            if (_lines.Length == 0)
            {
                return 0;
            }

            var miniScrollOffset = GetMiniScrollOffset(Bounds.Height);
            var lineIndex = (miniScrollOffset + point.Y - TopPadding) / MiniLineHeight;

            var targetLine = Math.Clamp((int)Math.Floor(lineIndex), 0, Math.Max(0, _lines.Length - 1));
            return TopPadding + (targetLine * _editorLineHeight);
        }

        public double GetEditorOffsetForTrackPoint(Point point)
        {
            var maxEditorOffset = Math.Max(0, _extentHeight - _viewportHeight);
            var height = Bounds.Height;
            if (height <= 0 || maxEditorOffset <= 0)
            {
                return 0;
            }

            var ratio = Math.Clamp(point.Y / height, 0, 1);
            return ratio * maxEditorOffset;
        }

        private static double ClampViewportHeight(double value, double minimum, double maximum)
        {
            var usableMaximum = Math.Max(0, maximum);
            if (usableMaximum <= minimum)
            {
                return usableMaximum;
            }

            return Math.Clamp(value, minimum, usableMaximum);
        }

        private RenderSegment[] GetLineSegments(string line, int lineIndex)
        {
            if (_lineSegmentCache.TryGetValue(lineIndex, out var cached))
            {
                return cached;
            }

            if (line.Length == 0)
            {
                return [];
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
            Array.Fill(brushes, Palette.MinimapCode);

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

        private void ConfigureGrammar()
        {
            _grammar = GetGrammarForExtension(_extension);
        }

        private List<TokenSpan> GetTextMateSpans(string line, int lineIndex)
        {
            if (_textMateSpanCache.TryGetValue(lineIndex, out var cached))
            {
                return cached;
            }

            var spans = new List<TokenSpan>();
            _textMateSpanCache[lineIndex] = spans;
            if (_grammar is null || line.Length == 0)
            {
                return spans;
            }

            try
            {
                var result = _grammar.TokenizeLine(line);
                foreach (var token in result.Tokens)
                {
                    var start = Math.Clamp(token.StartIndex, 0, line.Length);
                    var end = Math.Clamp(token.EndIndex, start, line.Length);
                    if (end <= start)
                    {
                        continue;
                    }

                    var brush = MiniBrushFromScopes(token.Scopes);
                    if (!ReferenceEquals(brush, Palette.MinimapCode))
                    {
                        spans.Add(new TokenSpan(start, end - start, brush));
                    }
                }
            }
            catch
            {
                spans.Clear();
            }

            return spans;
        }

        private static IBrush MiniBrushFromScopes(IReadOnlyList<string> scopes)
        {
            var brush = BrushFromScopes(scopes);
            if (ReferenceEquals(brush, Palette.Code))
            {
                return Palette.MinimapCode;
            }

            if (ReferenceEquals(brush, Palette.Comment))
            {
                return Palette.MinimapComment;
            }

            return ReferenceEquals(brush, Palette.Keyword) ? Palette.MinimapKeyword : brush;
        }

        private void HighlightStringsAndComments(string line, IBrush[] brushes)
        {
            for (var index = 0; index < line.Length; index++)
            {
                if (IsLineCommentStart(line, index))
                {
                    Fill(brushes, index, line.Length - index, Palette.MinimapComment);
                    return;
                }

                if (IsBlockCommentStart(line, index))
                {
                    Fill(brushes, index, line.Length - index, Palette.MinimapComment);
                    return;
                }

                var current = line[index];
                if (current is '"' or '\'')
                {
                    var end = FindStringEnd(line, index, current);
                    Fill(brushes, index, end - index + 1, Palette.String);
                    index = end;
                }
            }
        }

        private void HighlightWords(string line, IBrush[] brushes)
        {
            for (var index = 0; index < line.Length;)
            {
                if (!ReferenceEquals(brushes[index], Palette.MinimapCode))
                {
                    index++;
                    continue;
                }

                if (line[index] == '$' && _extension is ".ps1" or ".psm1" or ".psd1")
                {
                    var end = index + 1;
                    while (end < line.Length && IsIdentifierPart(line[end]))
                    {
                        end++;
                    }

                    Fill(brushes, index, end - index, Palette.Variable);
                    index = end;
                    continue;
                }

                if (char.IsDigit(line[index]))
                {
                    var end = index + 1;
                    while (end < line.Length && (char.IsDigit(line[end]) || line[end] == '.'))
                    {
                        end++;
                    }

                    Fill(brushes, index, end - index, Palette.Number);
                    index = end;
                    continue;
                }

                if (!IsIdentifierStart(line[index]))
                {
                    index++;
                    continue;
                }

                var start = index;
                index++;
                while (index < line.Length && IsIdentifierPart(line[index]))
                {
                    index++;
                }

                var word = line[start..index];
                if (IsKeyword(word))
                {
                    Fill(brushes, start, index - start, Palette.Keyword);
                }
                else if (IsTypeLike(word))
                {
                    Fill(brushes, start, index - start, Palette.Type);
                }
            }
        }

        private static void HighlightMarkup(string line, IBrush[] brushes)
        {
            if (line.TrimStart().StartsWith("<!--", StringComparison.Ordinal))
            {
                Fill(brushes, 0, line.Length, Palette.MinimapComment);
                return;
            }

            var inTag = false;
            for (var index = 0; index < line.Length; index++)
            {
                var current = line[index];
                if (current == '<')
                {
                    inTag = true;
                    brushes[index] = Palette.Keyword;
                    continue;
                }

                if (current == '>')
                {
                    brushes[index] = Palette.Keyword;
                    inTag = false;
                    continue;
                }

                if (!inTag)
                {
                    continue;
                }

                if (current is '"' or '\'')
                {
                    var end = FindStringEnd(line, index, current);
                    Fill(brushes, index, end - index + 1, Palette.String);
                    index = end;
                    continue;
                }

                brushes[index] = char.IsLetterOrDigit(current) || current is '/' or '-' or ':' ? Palette.Type : Palette.Keyword;
            }
        }

        private bool IsLineCommentStart(string line, int index)
        {
            return _extension is ".ps1" or ".psm1" or ".psd1" or ".py" or ".sh" or ".yaml" or ".yml"
                ? line[index] == '#'
                : index + 1 < line.Length && line[index] == '/' && line[index + 1] == '/';
        }

        private bool IsBlockCommentStart(string line, int index)
        {
            return _extension is ".css" or ".js" or ".ts" or ".cs" or ".cpp" or ".c" or ".h" or ".hpp"
                && index + 1 < line.Length
                && line[index] == '/'
                && line[index + 1] == '*';
        }

        private bool IsKeyword(string word)
        {
            return _extension switch
            {
                ".ps1" or ".psm1" or ".psd1" => PowerShellKeywords.Contains(word),
                ".cs" => CSharpKeywords.Contains(word),
                ".js" or ".ts" or ".tsx" or ".jsx" => JavaScriptKeywords.Contains(word),
                ".py" => PythonKeywords.Contains(word),
                ".json" => JsonKeywords.Contains(word),
                ".css" => CssKeywords.Contains(word),
                _ => CommonKeywords.Contains(word)
            };
        }

        private static bool IsTypeLike(string word)
        {
            return word.Length > 1 && char.IsUpper(word[0]) && word.Skip(1).Any(char.IsLower);
        }

        private static int[][] BuildMiniBracketColors(IReadOnlyList<string> lines)
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
