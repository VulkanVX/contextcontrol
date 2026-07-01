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
    private sealed partial class CodeTextSurface : Control
    {
        private const double TopPadding = EditorTopPadding;
        private const double BottomPadding = EditorBottomPadding;
        private const double MinGutterWidth = 0;
        private const double TextStartPadding = 8;
        private double LineHeight => _lineHeight;
        private double FontSize => _fontSize;
        private double LineNumberFontSize => Math.Max(8.0, _fontSize - 1.0);
        private double CharWidth => Math.Max(5.2, _fontSize * 0.596);
        private double FoldSlotWidth => CharWidth;
        private const double FoldArrowHitRadius = 6;
        private const double FoldButtonSize = 11.5;
        private const double FoldButtonCornerRadius = 3;
        private const double FoldArrowStroke = 1.1;
        private const double FoldArrowHalfWidth = 2.7;
        private const double FoldArrowHalfHeight = 1.75;
        private const double FoldArrowCollapsedHalfWidth = 1.9;
        private const double FoldArrowCollapsedHalfHeight = 2.8;
        private double LineNumberLeftPadding => CharWidth;
        private double NumberAreaRightPadding => CharWidth;
        private const double GutterArrowColumnWidth = (FoldArrowHitRadius * 2) + 2;
        private const byte ScopeGuideAlpha = 78;
        private const byte ActiveScopeGuideAlpha = 158;
        private const byte UnifiedScopeGuideAlpha = 124;
        private const byte ActiveUnifiedScopeGuideAlpha = 190;
        private const double ScopeGuideThickness = 1.35;
        private const double ActiveScopeGuideThickness = 1.75;
        private const double ScopeGuideTopInset = 0;
        private const double ScopeGuideBottomInset = 0;
        private const byte ScopeLineFillAlpha = 14;
        private const byte ActiveScopeLineFillAlpha = 26;
        private const double SelectionEdgeScrollZone = 44;
        private const double SelectionMinScrollStep = 6;
        private const double SelectionMaxScrollStep = 56;
        private static readonly IBrush CrtTintBrush = Brush(0, 255, 102, 18);
        private static readonly IBrush CrtScanlineBrush = Brush(0, 0, 0, 72);
        private static readonly IBrush CrtFineScanlineBrush = Brush(0, 255, 102, 22);
        private static readonly IBrush CrtSweepBrush = Brush(183, 255, 210, 44);
        private static readonly IBrush CrtEdgeShadowBrush = Brush(0, 0, 0, 112);
        private static readonly IBrush MatrixGlyphHaloBrush = Brush(0, 255, 102, 120);
        private static readonly IBrush MatrixHotGlyphBrush = Brush(221, 255, 233, 252);
        private static readonly char[] MatrixCodeGlyphs =
        [
            '0', '1', '3', '5', '7', '9', 'Z', ':', '.', '"', '=', '*', '+', '-', '<', '>',
            '\uFF71', '\uFF72', '\uFF73', '\uFF74', '\uFF75', '\uFF76', '\uFF77', '\uFF78',
            '\uFF79', '\uFF7A', '\uFF7B', '\uFF7C', '\uFF7D', '\uFF7E', '\uFF7F', '\uFF80',
            '\uFF81', '\uFF82', '\uFF83', '\uFF84', '\uFF85', '\uFF86', '\uFF87', '\uFF88',
            '\uFF89', '\uFF8A', '\uFF8B', '\uFF8C', '\uFF8D', '\uFF8E', '\uFF8F', '\uFF90',
            '\uFF91', '\uFF92', '\uFF93', '\uFF94', '\uFF95', '\uFF96', '\uFF97', '\uFF98',
            '\uFF99', '\uFF9A', '\uFF9B', '\uFF9C'
        ];

        private readonly DispatcherTimer _selectionAutoScrollTimer;
        private readonly HashSet<int> _collapsedStartLines = [];
        private Dictionary<int, FoldRegion> _foldsByStartLine = [];
        private int[][] _bracketColorIndexes = [];
        private readonly Dictionary<int, List<TokenSpan>> _textMateSpanCache = [];
        private readonly Dictionary<int, RenderSegment[]> _lineSegmentCache = [];
        private readonly Dictionary<int, Pen> _scopeGuidePens = [];
        private readonly Dictionary<int, Pen> _activeScopeGuidePens = [];
        private readonly Dictionary<int, IBrush> _scopeLineFills = [];
        private readonly Dictionary<int, IBrush> _activeScopeLineFills = [];
        private HashSet<string> _summaryFoldKinds = ParseSummaryFoldKinds(AllSummaryFoldKinds);
        private Pen? _unifiedScopeGuidePen;
        private Pen? _activeUnifiedScopeGuidePen;
        private IReadOnlyDictionary<int, string> _lineChanges = new Dictionary<int, string>();
        private double _fontSize = DefaultEditorFontSize;
        private double _lineHeight = DefaultEditorLineHeight;
        private string[] _lines = [""];
        private VisibleRow[] _visibleRows = [VisibleRow.ForCodeLine(0)];
        private FoldRegion[] _foldRegions = [];
        private readonly Dictionary<int, FoldRegion[]> _scopesByLineCache = [];
        private string _extension = "";
        private IGrammar? _grammar;
        private int _maxLineLength;
        private double _viewportOffset;
        private double _viewportHeight = 640;
        private int _visibleLineDigits;
        private int _visibleGuideDepth = -1;
        private int _activeLineIndex = -1;
        private bool _showFoldArrows = true;
        private bool _showSummaryArrowBorders = true;
        private bool _foldArrowsInCodeEditor = true;
        private bool _useParentChildArrowIndentation = true;
        private bool _showVerticalScopeLines = true;
        private bool _useColorfulFamilies = true;
        private bool _showFoldSummaryPreview = true;
        private bool _layoutShowFoldArrows = true;
        private bool _layoutFoldArrowsInCodeEditor = true;
        private bool _layoutUseParentChildArrowIndentation = true;
        private bool _layoutShowVerticalScopeLines = true;
        private string _skinKey = "default";
        private long _animationPhase;
        private double _gutterWidth = MinGutterWidth;
        private double _foldColumnStart = MinGutterWidth + TextStartPadding;
        private double _textStart = MinGutterWidth + (TextStartPadding * 2) + 7.15;
        private double _guideBaseX = MinGutterWidth + TextStartPadding + (7.15 / 2);
        private double _arrowBaseX = MinGutterWidth + TextStartPadding + (7.15 / 2);
        private double _lineNumberRight = 7.15 + 7.15;
        private ScrollViewer? _scrollHost;
        private Action<double>? _verticalScrollRequester;
        private Func<double>? _verticalOffsetProvider;
        private Func<double>? _maxVerticalOffsetProvider;
        private string _documentText = "";
        private string _lineBreak = "\n";
        private TextPosition? _selectionAnchor;
        private TextPosition? _selectionCaret;
        private bool _isSelecting;
        private bool _hasSelectionViewportPoint;
        private Point _lastSelectionViewportPoint;
        private double _selectionAutoScrollDeltaY;

        public CodeTextSurface()
        {
            Focusable = true;
            _selectionAutoScrollTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(16)
            };
            _selectionAutoScrollTimer.Tick += OnSelectionAutoScrollTick;
        }

        public void SetScrollHost(
            ScrollViewer scrollHost,
            Action<double> verticalScrollRequester,
            Func<double> verticalOffsetProvider,
            Func<double> maxVerticalOffsetProvider)
        {
            _scrollHost = scrollHost;
            _verticalScrollRequester = verticalScrollRequester;
            _verticalOffsetProvider = verticalOffsetProvider;
            _maxVerticalOffsetProvider = maxVerticalOffsetProvider;
        }

        public double ContentHeight => TopPadding + (_visibleRows.Length * LineHeight) + BottomPadding;

        public void SetEditorFontSize(double fontSize, double lineHeight)
        {
            var nextFontSize = Math.Clamp(fontSize, 8.0, 22.0);
            var nextLineHeight = Math.Max(12.0, lineHeight);
            if (Math.Abs(_fontSize - nextFontSize) <= 0.01
                && Math.Abs(_lineHeight - nextLineHeight) <= 0.01)
            {
                return;
            }

            _fontSize = nextFontSize;
            _lineHeight = nextLineHeight;
            _textMateSpanCache.Clear();
            _lineSegmentCache.Clear();
            _visibleLineDigits = -1;
            _visibleGuideDepth = -1;
            RebuildVisibleLines();
            InvalidateMeasure();
            InvalidateVisual();
        }

        public void SetDocument(string text, string path, IReadOnlyDictionary<int, string>? lineChanges)
        {
            _documentText = text ?? "";
            _lineBreak = DetectLineBreak(_documentText);
            _extension = Path.GetExtension(path).ToLowerInvariant();
            _lines = NormalizeLines(_documentText);
            _lineChanges = lineChanges ?? new Dictionary<int, string>();
            _collapsedStartLines.Clear();
            ClearSelectionState();
            _textMateSpanCache.Clear();
            _lineSegmentCache.Clear();
            _scopeGuidePens.Clear();
            _activeScopeGuidePens.Clear();
            _scopeLineFills.Clear();
            _activeScopeLineFills.Clear();
            _scopesByLineCache.Clear();
            _activeLineIndex = -1;
            ConfigureGrammar();
            _foldsByStartLine = BuildFoldRegions(_lines);
            _foldRegions = _foldsByStartLine.Values
                .OrderBy(fold => fold.StartLine)
                .ThenByDescending(fold => fold.EndLine)
                .ToArray();
            _bracketColorIndexes = BuildBracketColors(_lines);
            _maxLineLength = _lines.Length == 0 ? 0 : _lines.Max(line => line.Length);
            RebuildVisibleLines();
            InvalidateMeasure();
            InvalidateVisual();
        }

        public void ApplyTheme()
        {
            _textMateSpanCache.Clear();
            _lineSegmentCache.Clear();
            _scopeGuidePens.Clear();
            _activeScopeGuidePens.Clear();
            _scopeLineFills.Clear();
            _activeScopeLineFills.Clear();
            _unifiedScopeGuidePen = null;
            _activeUnifiedScopeGuidePen = null;
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

        public void SetVisualOptions(
            bool showFoldArrows,
            bool showSummaryArrowBorders,
            bool foldArrowsInCodeEditor,
            bool useParentChildArrowIndentation,
            bool showVerticalScopeLines,
            string summaryFoldKinds,
            bool useColorfulFamilies,
            bool showFoldSummaryPreview)
        {
            var normalizedSummaryFoldKinds = ParseSummaryFoldKinds(summaryFoldKinds);
            var layoutMayChange = _showFoldArrows != showFoldArrows
                || _foldArrowsInCodeEditor != foldArrowsInCodeEditor
                || _useParentChildArrowIndentation != useParentChildArrowIndentation
                || _showVerticalScopeLines != showVerticalScopeLines;
            var colorfulFamiliesChanged = _useColorfulFamilies != useColorfulFamilies;
            var showFoldArrowsChanged = _showFoldArrows != showFoldArrows;
            var summaryArrowBordersChanged = _showSummaryArrowBorders != showSummaryArrowBorders;
            var summaryFoldKindsChanged = !_summaryFoldKinds.SetEquals(normalizedSummaryFoldKinds);
            var foldSummaryPreviewChanged = _showFoldSummaryPreview != showFoldSummaryPreview;
            if (!layoutMayChange && !colorfulFamiliesChanged && !summaryArrowBordersChanged && !summaryFoldKindsChanged && !foldSummaryPreviewChanged)
            {
                return;
            }

            _showFoldArrows = showFoldArrows;
            _showSummaryArrowBorders = showSummaryArrowBorders;
            _foldArrowsInCodeEditor = foldArrowsInCodeEditor;
            _useParentChildArrowIndentation = useParentChildArrowIndentation;
            _showVerticalScopeLines = showVerticalScopeLines;
            _summaryFoldKinds = normalizedSummaryFoldKinds;
            _useColorfulFamilies = useColorfulFamilies;
            _showFoldSummaryPreview = showFoldSummaryPreview;

            if (!_useColorfulFamilies)
            {
                _activeLineIndex = -1;
            }

            if (colorfulFamiliesChanged)
            {
                _scopeGuidePens.Clear();
                _activeScopeGuidePens.Clear();
                _scopeLineFills.Clear();
                _activeScopeLineFills.Clear();
                _unifiedScopeGuidePen = null;
                _activeUnifiedScopeGuidePen = null;
            }

            if ((layoutMayChange || summaryFoldKindsChanged || foldSummaryPreviewChanged) && UpdateGutterLayout())
            {
                InvalidateMeasure();
            }

            if ((showFoldArrowsChanged || summaryFoldKindsChanged) && PruneCollapsedSummaryFolds())
            {
                RebuildVisibleLines();
                InvalidateMeasure();
            }
            else if (foldSummaryPreviewChanged)
            {
                RebuildVisibleLines();
            }

            InvalidateVisual();
        }

        public void SetViewport(double offset, double height)
        {
            _viewportOffset = Math.Max(0, offset);
            _viewportHeight = Math.Max(LineHeight, height);
            if (UpdateGutterLayout())
            {
                InvalidateMeasure();
            }

            UpdateSelectionFromViewportPoint();
            InvalidateVisual();
        }

        public (int Current, int Total) FindAndReveal(string query)
        {
            if (string.IsNullOrWhiteSpace(query) || _lines.Length == 0)
            {
                return (0, 0);
            }

            var matches = new List<int>();
            for (var i = 0; i < _lines.Length; i++)
            {
                if (_lines[i].Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    matches.Add(i);
                }
            }

            if (matches.Count == 0)
            {
                return (0, 0);
            }

            var target = matches.FirstOrDefault(line => line >= Math.Max(0, _activeLineIndex), matches[0]);
            SetActiveLine(target);
            var y = Math.Max(0.0, TopPadding + (VisibleIndexForLine(target) * LineHeight) - (_viewportHeight * 0.45));
            _verticalScrollRequester?.Invoke(Math.Clamp(y, 0.0, _maxVerticalOffsetProvider?.Invoke() ?? 0.0));

            return (matches.IndexOf(target) + 1, matches.Count);
        }

        private int VisibleIndexForLine(int lineIndex)
        {
            for (var i = 0; i < _visibleRows.Length; i++)
            {
                var row = _visibleRows[i];
                if (!row.IsFoldSummary && row.LineIndex == lineIndex)
                {
                    return i;
                }
            }

            return Math.Clamp(lineIndex, 0, Math.Max(0, _visibleRows.Length - 1));
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            var contentWidth = _textStart + (_maxLineLength * CharWidth) + 160;
            var width = double.IsInfinity(availableSize.Width)
                ? contentWidth
                : Math.Max(availableSize.Width, contentWidth);
            var height = double.IsFinite(availableSize.Height) && availableSize.Height > 0
                ? availableSize.Height
                : Math.Max(LineHeight, _viewportHeight);
            return new Size(width, height);
        }

    }
}
