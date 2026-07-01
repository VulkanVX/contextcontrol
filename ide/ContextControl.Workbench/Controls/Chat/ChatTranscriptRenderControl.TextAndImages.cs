// CC-DESC: Draws the shared chat/image-generation transcript as one cached virtualized surface.

using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ContextControl.Workbench.ViewModels;

namespace ContextControl.Workbench.Controls;

public sealed partial class ChatTranscriptRenderControl
{
    private int FindRowIndexAtOrAfter(double y)
    {
        if (_itemCount == 0)
        {
            return -1;
        }

        var lo = 0;
        var hi = _itemCount - 1;
        while (lo < hi)
        {
            var mid = lo + ((hi - lo) / 2);
            if (GetRowTop(mid + 1) <= y)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid;
            }
        }

        return lo;
    }

    private IReadOnlyList<string> WrapLines(
        string text,
        double width,
        FontFamily fontFamily,
        FontWeight weight,
        FontStyle style,
        double fontSize)
    {
        var normalized = NormalizeWrapText(text);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return [""];
        }

        width = Math.Max(1.0, Math.Round(width, 1));
        var fontFamilyName = fontFamily.ToString();
        var useMonospaceWrap = ShouldUseMonospaceWrap(fontFamilyName, weight, style);
        var key = new WrapCacheKey(normalized, width, fontFamilyName, weight, style, fontSize, useMonospaceWrap);
        if (_wrapCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        if (_wrapCache.Count > MaxWrapCacheEntries)
        {
            _wrapCache.Clear();
        }

        var lines = new List<string>();
        if (useMonospaceWrap)
        {
            WrapMonospaceLines(normalized, width, fontFamily, weight, style, fontSize, lines);
        }
        else
        {
            WrapMeasuredLines(normalized, width, fontFamily, weight, style, fontSize, lines);
        }

        cached = lines.Count == 0 ? [""] : lines.ToArray();
        _wrapCache[key] = cached;
        return cached;
    }

    private void WrapMeasuredLines(
        string text,
        double width,
        FontFamily fontFamily,
        FontWeight weight,
        FontStyle style,
        double fontSize,
        List<string> lines)
    {
        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                lines.Add("");
                continue;
            }

            WrapSingleLine(line, width, fontFamily, weight, style, fontSize, lines);
        }
    }

    private void WrapMonospaceLines(
        string text,
        double width,
        FontFamily fontFamily,
        FontWeight weight,
        FontStyle style,
        double fontSize,
        List<string> lines)
    {
        var charWidth = Math.Max(1.0, MeasureTextWidth("M", fontFamily, weight, style, fontSize));
        var maxColumns = Math.Max(1, (int)Math.Floor(width / charWidth));
        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                lines.Add("");
                continue;
            }

            while (line.Length > maxColumns)
            {
                var splitAt = FindMonospaceBreak(line, maxColumns);
                lines.Add(line[..splitAt].TrimEnd());
                line = line[splitAt..].TrimStart();
            }

            if (line.Length > 0)
            {
                lines.Add(line);
            }
        }
    }

    private int FindMonospaceBreak(string line, int maxColumns)
    {
        maxColumns = Math.Clamp(maxColumns, 1, line.Length);
        var minUsefulBreak = Math.Max(8, maxColumns / 3);
        for (var index = maxColumns; index >= minUsefulBreak; index--)
        {
            if (char.IsWhiteSpace(line[index - 1]))
            {
                return index;
            }
        }

        return maxColumns;
    }

    private bool ShouldUseMonospaceWrap(string fontFamilyName, FontWeight weight, FontStyle style)
    {
        if (weight != FontWeight.Normal || style != FontStyle.Normal)
        {
            return false;
        }

        var codeFont = ResolveFontFamily(CodeFontFamily, Resource("CodeFontFamily", DefaultCodeFontFamily)).ToString();
        return string.Equals(fontFamilyName, codeFont, StringComparison.Ordinal)
            || fontFamilyName.Contains("Cascadia", StringComparison.OrdinalIgnoreCase)
            || fontFamilyName.Contains("Consolas", StringComparison.OrdinalIgnoreCase)
            || fontFamilyName.Contains("Courier", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeWrapText(string text)
    {
        return (text ?? "")
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
    }

    private void WrapSingleLine(
        string line,
        double width,
        FontFamily fontFamily,
        FontWeight weight,
        FontStyle style,
        double fontSize,
        List<string> lines)
    {
        var words = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var current = "";
        foreach (var word in words)
        {
            var candidate = string.IsNullOrEmpty(current) ? word : $"{current} {word}";
            if (MeasureTextWidth(candidate, fontFamily, weight, style, fontSize) <= width)
            {
                current = candidate;
                continue;
            }

            if (!string.IsNullOrEmpty(current))
            {
                lines.Add(current);
                current = "";
            }

            if (MeasureTextWidth(word, fontFamily, weight, style, fontSize) <= width)
            {
                current = word;
            }
            else
            {
                BreakLongWord(word, width, fontFamily, weight, style, fontSize, lines);
            }
        }

        if (!string.IsNullOrEmpty(current))
        {
            lines.Add(current);
        }
    }

    private void BreakLongWord(
        string word,
        double width,
        FontFamily fontFamily,
        FontWeight weight,
        FontStyle style,
        double fontSize,
        List<string> lines)
    {
        var remaining = word;
        while (remaining.Length > 0)
        {
            var lo = 1;
            var hi = remaining.Length;
            var best = 1;
            while (lo <= hi)
            {
                var mid = lo + ((hi - lo) / 2);
                if (MeasureTextWidth(remaining[..mid], fontFamily, weight, style, fontSize) <= width)
                {
                    best = mid;
                    lo = mid + 1;
                }
                else
                {
                    hi = mid - 1;
                }
            }

            lines.Add(remaining[..best]);
            remaining = remaining[best..];
        }
    }

    private double MeasureTextWidth(string text, FontFamily fontFamily, FontWeight weight, FontStyle style, double fontSize)
    {
        return GetFormattedText(text, Resource("TextPrimaryBrush", TextPrimaryFallbackBrush), fontFamily, weight, style, fontSize).Width;
    }

    private string FitLineToWidth(
        string text,
        double width,
        IBrush brush,
        FontFamily fontFamily,
        FontWeight weight,
        FontStyle style,
        double fontSize)
    {
        var clean = text ?? "";
        if (GetFormattedText(clean, brush, fontFamily, weight, style, fontSize).Width <= width)
        {
            return clean;
        }

        while (clean.Length > 4)
        {
            clean = clean[..^4].TrimEnd() + "...";
            if (GetFormattedText(clean, brush, fontFamily, weight, style, fontSize).Width <= width)
            {
                return clean;
            }
        }

        return clean;
    }

    private FormattedText GetFormattedText(
        string text,
        IBrush brush,
        FontFamily fontFamily,
        FontWeight weight,
        FontStyle style,
        double fontSize)
    {
        var key = new TextCacheKey(
            text,
            RuntimeHelpers.GetHashCode(brush),
            fontFamily.ToString(),
            weight,
            style,
            fontSize);
        if (_textCache.TryGetValue(key, out var formatted))
        {
            return formatted;
        }

        if (_textCache.Count > MaxTextCacheEntries)
        {
            _textCache.Clear();
        }

        formatted = new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface(fontFamily, style, weight),
            fontSize,
            brush);
        _textCache[key] = formatted;
        return formatted;
    }

    private T Resource<T>(string key, T fallback)
    {
        for (var control = this as Control; control is not null; control = control.GetVisualParent() as Control)
        {
            if (control.Resources.TryGetValue(key, out var value) && value is T typed)
            {
                return typed;
            }
        }

        if (Application.Current?.Resources.TryGetValue(key, out var appValue) == true && appValue is T appTyped)
        {
            return appTyped;
        }

        return fallback;
    }

    private static FontFamily ResolveFontFamily(string? value, FontFamily fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        try
        {
            return new FontFamily(value);
        }
        catch
        {
            return fallback;
        }
    }

    private Rect BuildImagePreviewRect(string path, double x, double y, double contentWidth)
    {
        var width = Math.Max(96.0, contentWidth);
        var height = ImagePreviewFallbackHeight;
        if (TryGetImageBitmap(path) is { } bitmap
            && bitmap.PixelSize.Width > 0
            && bitmap.PixelSize.Height > 0)
        {
            height = width * bitmap.PixelSize.Height / bitmap.PixelSize.Width;
            if (height > ImagePreviewMaxHeight)
            {
                var scale = ImagePreviewMaxHeight / height;
                width = Math.Max(96.0, width * scale);
                height = ImagePreviewMaxHeight;
            }
        }

        return new Rect(x, y, Math.Min(contentWidth, width), Math.Clamp(height, 96.0, ImagePreviewMaxHeight));
    }

    private Bitmap? TryGetImageBitmap(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            var fullPath = Path.GetFullPath(path);
            if (!File.Exists(fullPath))
            {
                return null;
            }

            var info = new FileInfo(fullPath);
            if (_imageCache.TryGetValue(fullPath, out var cached)
                && cached.LastWriteUtc == info.LastWriteTimeUtc
                && cached.Length == info.Length)
            {
                return cached.Bitmap;
            }

            if (_imageCache.Count > 64)
            {
                ClearImageCache();
            }

            using var stream = File.OpenRead(fullPath);
            var bitmap = new Bitmap(stream);
            if (_imageCache.TryGetValue(fullPath, out var previous))
            {
                previous.Bitmap.Dispose();
            }

            _imageCache[fullPath] = new ImageCacheEntry(bitmap, info.LastWriteTimeUtc, info.Length);
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    private void OpenImagePreviewWindow(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return;
        }

        try
        {
            using var stream = File.OpenRead(path);
            var bitmap = new Bitmap(stream);
            var owner = TopLevel.GetTopLevel(this) as Window;
            var image = new Image
            {
                Source = bitmap,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(48)
            };
            var closeButton = new Button
            {
                Content = "x",
                Width = 34,
                Height = 30,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 16, 16, 0)
            };
            var grid = new Grid
            {
                Background = new SolidColorBrush(Color.FromArgb(235, 13, 17, 20))
            };
            grid.Children.Add(image);
            grid.Children.Add(closeButton);

            var window = new Window
            {
                Title = Path.GetFileName(path),
                Content = grid,
                WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
                WindowState = WindowState.FullScreen,
                SystemDecorations = SystemDecorations.None,
                Background = Brushes.Transparent
            };
            closeButton.Click += (_, _) => window.Close();
            window.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    window.Close();
                    e.Handled = true;
                }
            };
            window.Closed += (_, _) => bitmap.Dispose();
            if (owner is null)
            {
                window.Show();
            }
            else
            {
                window.Show(owner);
            }
        }
        catch
        {
            // A failed preview should not disrupt the chat surface.
        }
    }

    private async Task DownloadImageAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return;
        }

        try
        {
            var suggestedName = Path.GetFileName(path);
            var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
            if (storage is not null)
            {
                var target = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Download image",
                    SuggestedFileName = suggestedName,
                    FileTypeChoices =
                    [
                        new FilePickerFileType("Image")
                        {
                            Patterns = ["*.png", "*.jpg", "*.jpeg", "*.webp", "*.bmp", "*.gif", "*.tif", "*.tiff"]
                        }
                    ]
                });
                if (target is null)
                {
                    return;
                }

                await using var source = File.OpenRead(path);
                await using var destination = await target.OpenWriteAsync();
                try
                {
                    destination.SetLength(0);
                }
                catch
                {
                    // Some storage-backed streams do not support truncation before writing.
                }

                await source.CopyToAsync(destination);
                return;
            }

            var downloads = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Downloads");
            Directory.CreateDirectory(downloads);
            File.Copy(path, CreateUniqueDownloadPath(Path.Combine(downloads, suggestedName)), overwrite: false);
        }
        catch
        {
            // Download is a convenience action; ignore IO failures here.
        }
    }

    private static string CreateUniqueDownloadPath(string path)
    {
        if (!File.Exists(path))
        {
            return path;
        }

        var directory = Path.GetDirectoryName(path) ?? "";
        var name = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        for (var index = 1; index < 10_000; index++)
        {
            var candidate = Path.Combine(directory, $"{name} ({index}){extension}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }

        return Path.Combine(directory, $"{name}-{DateTime.Now:yyyyMMdd-HHmmss}{extension}");
    }

    private void ClearImageCache()
    {
        foreach (var cached in _imageCache.Values)
        {
            cached.Bitmap.Dispose();
        }

        _imageCache.Clear();
    }

    private static bool IsInlineImageAttachment(ContextControlAttachmentViewModel attachment)
    {
        if (!string.Equals(attachment.Kind, "image", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(attachment.Path))
        {
            return false;
        }

        return Path.GetExtension(attachment.Path).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".webp" or ".bmp" or ".gif" or ".tif" or ".tiff";
    }

    private static Rect FitImageRect(PixelSize pixelSize, Rect bounds)
    {
        var sourceWidth = Math.Max(1.0, pixelSize.Width);
        var sourceHeight = Math.Max(1.0, pixelSize.Height);
        var scale = Math.Min(bounds.Width / sourceWidth, bounds.Height / sourceHeight);
        if (!double.IsFinite(scale) || scale <= 0.0)
        {
            scale = 1.0;
        }

        var width = Math.Min(bounds.Width, sourceWidth * scale);
        var height = Math.Min(bounds.Height, sourceHeight * scale);
        return new Rect(
            bounds.X + Math.Max(0.0, (bounds.Width - width) * 0.5),
            bounds.Y + Math.Max(0.0, (bounds.Height - height) * 0.5),
            width,
            height);
    }

    private static Rect OffsetY(Rect rect, double y)
    {
        return new Rect(rect.X, rect.Y + y, rect.Width, rect.Height);
    }

    private static void DrawClippedText(DrawingContext context, FormattedText formatted, Rect clip, Point point)
    {
        if (clip.Width <= 0 || clip.Height <= 0)
        {
            return;
        }

        using (context.PushClip(clip))
        {
            context.DrawText(formatted, point);
        }
    }

    private static string NormalizeText(string? value)
    {
        return (value ?? "")
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Replace("<br />", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("<br/>", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("<br>", "\n", StringComparison.OrdinalIgnoreCase)
            .Trim();
    }

    private string GetNormalizedPartText(LocalLlmChatPartViewModel part)
    {
        if (_normalizedPartTextCache.TryGetValue(part, out var text))
        {
            return text;
        }

        text = NormalizeText(part.Text);
        _normalizedPartTextCache[part] = text;
        return text;
    }

    private string GetNormalizedThinkingText(LocalLlmChatMessageViewModel message)
    {
        if (_normalizedThinkingTextCache.TryGetValue(message, out var text))
        {
            return text;
        }

        text = NormalizeText(message.ThinkingText);
        _normalizedThinkingTextCache[message] = text;
        return text;
    }

    private string GetNormalizedDiagnosticText(LocalLlmChatMessageViewModel message)
    {
        if (_normalizedDiagnosticTextCache.TryGetValue(message, out var text))
        {
            return text;
        }

        text = NormalizeText(message.DiagnosticPrompt);
        _normalizedDiagnosticTextCache[message] = text;
        return text;
    }

    private static string CleanOneLine(string? value, int maxLength)
    {
        var clean = (value ?? "")
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();
        if (clean.Length <= maxLength)
        {
            return clean;
        }

        return clean[..Math.Max(0, maxLength - 1)] + "...";
    }

    private static IReadOnlyList<string> SplitLines(string? value)
    {
        var text = NormalizeText(value);
        return text.Length == 0
            ? [""]
            : text.Split('\n').ToArray();
    }

    private IReadOnlyList<string> GetSnippetDisplayLines(ChatSnippetViewModel snippet, bool expanded)
    {
        var key = new SnippetLineCacheKey(snippet, expanded);
        if (_snippetLineCache.TryGetValue(key, out var lines))
        {
            return lines;
        }

        if (_snippetLineCache.Count > MaxWrapCacheEntries)
        {
            _snippetLineCache.Clear();
        }

        lines = SplitLines(expanded ? snippet.Text : snippet.PreviewText);
        _snippetLineCache[key] = lines;
        return lines;
    }

    private readonly record struct TextCacheKey(
        string Text,
        int BrushId,
        string FontFamily,
        FontWeight Weight,
        FontStyle Style,
        double FontSize);

    private readonly record struct WrapCacheKey(
        string Text,
        double Width,
        string FontFamily,
        FontWeight Weight,
        FontStyle Style,
        double FontSize,
        bool IsMonospace);

    private readonly record struct SnippetLineCacheKey(ChatSnippetViewModel Snippet, bool Expanded);

    private readonly record struct TextPosition(int RowIndex, int BlockIndex, int LineIndex, int Column);
    private readonly record struct ViewportAnchor(int RowIndex, double OffsetWithinRow);
    private sealed record ExtensionScrollbarHit(
        LocalLlmChatMessageViewModel Message,
        Dictionary<LocalLlmChatMessageViewModel, double> Offsets,
        double TrackY,
        double TrackHeight,
        double ThumbHeight,
        double MaxOffset,
        double ThumbY);

    private sealed record ExtensionScrollDrag(
        LocalLlmChatMessageViewModel Message,
        Dictionary<LocalLlmChatMessageViewModel, double> Offsets,
        double TrackY,
        double TrackHeight,
        double ThumbHeight,
        double MaxOffset,
        double PointerOffsetWithinThumb);

    private sealed class MessageLayout(LocalLlmChatMessageViewModel message)
    {
        public LocalLlmChatMessageViewModel Message { get; } = message;
        public double Height { get; set; }
        public Rect CardRect { get; set; }
        public Rect HeaderRect { get; set; }
        public Rect HeaderMetaClip { get; set; }
        public Rect ToolIconRect { get; set; }
        public Rect TimeRect { get; set; }
        public List<SelectableTextBlockLayout> TextBlocks { get; } = [];
        public List<SelectableTextBlockLayout> SelectableTextBlocks { get; } = [];
        public List<AttachmentLayout> Attachments { get; } = [];
        public List<SnippetLayout> Snippets { get; } = [];
        public ThinkingLayout? Thinking { get; set; }
        public DiagnosticLayout? Diagnostic { get; set; }
        public List<HitRegion> Hits { get; } = [];
    }

    private sealed record TextBlockLayout(
        Rect Rect,
        IReadOnlyList<string> Lines,
        double FontSize,
        double LineHeight,
        FontWeight Weight,
        FontStyle Style,
        bool UseCodeFont,
        IBrush Brush);

    private sealed record SelectableTextBlockLayout(int BlockIndex, TextBlockLayout TextBlock);

    private sealed record AttachmentLayout(Rect Rect, string Label, ContextControlAttachmentViewModel Attachment, bool IsImagePreview);

    private sealed record ImageCacheEntry(Bitmap Bitmap, DateTime LastWriteUtc, long Length);

    private sealed class SnippetLayout(ChatSnippetViewModel snippet, Rect headerRect, Rect codeRect, IReadOnlyList<string> codeLines)
    {
        public ChatSnippetViewModel Snippet { get; } = snippet;
        public Rect HeaderRect { get; } = headerRect;
        public Rect CodeRect { get; } = codeRect;
        public IReadOnlyList<string> CodeLines { get; } = codeLines;
        public SelectableTextBlockLayout? CodeTextBlock { get; set; }
        public Rect MetaClip { get; set; }
        public List<ButtonLayout> Buttons { get; } = [];
    }

    private sealed record ButtonLayout(Rect Rect, string Label, bool IsEnabled, HitRegion Hit);

    private sealed class ThinkingLayout(Rect buttonRect, HitRegion hit, bool isExpanded)
    {
        public Rect ButtonRect { get; } = buttonRect;
        public HitRegion Hit { get; } = hit;
        public bool IsExpanded { get; } = isExpanded;
        public Rect BodyRect { get; set; }
        public TextBlockLayout? TextBlock { get; set; }
    }

    private sealed class DiagnosticLayout(Rect buttonRect, HitRegion hit, bool isExpanded)
    {
        public Rect ButtonRect { get; } = buttonRect;
        public HitRegion Hit { get; } = hit;
        public bool IsExpanded { get; } = isExpanded;
        public Rect BodyRect { get; set; }
        public TextBlockLayout? TextBlock { get; set; }
    }

    private sealed class HitRegion(Rect rect, ChatTranscriptHitKind kind, object? parameter)
    {
        public Rect Rect { get; } = rect;
        public ChatTranscriptHitKind Kind { get; } = kind;
        public object? Parameter { get; } = parameter;
    }

    private sealed class CollapseAnimation(double startProgress, double targetProgress, double startMilliseconds)
    {
        private const double DurationMilliseconds = 150.0;

        public double TargetProgress { get; } = Math.Clamp(targetProgress, 0.0, 1.0);

        public double GetProgress(double nowMilliseconds)
        {
            var progress = Math.Clamp((nowMilliseconds - startMilliseconds) / DurationMilliseconds, 0.0, 1.0);
            var eased = 1.0 - Math.Pow(1.0 - progress, 3.0);
            return startProgress + ((TargetProgress - startProgress) * eased);
        }

        public bool IsComplete(double nowMilliseconds)
        {
            return nowMilliseconds - startMilliseconds >= DurationMilliseconds;
        }
    }

    private sealed class HeightDeltaIndex
    {
        private const int BlockSize = 256;
        private const int BlocksPerGroup = 256;

        private readonly Dictionary<int, double> _deltas = [];
        private readonly Dictionary<int, double> _blockSums = [];
        private readonly Dictionary<int, double> _groupSums = [];
        private int _count;

        public void Reset(int count)
        {
            _count = Math.Max(0, count);
            _deltas.Clear();
            _blockSums.Clear();
            _groupSums.Clear();
        }

        public void Resize(int count)
        {
            count = Math.Max(0, count);
            if (count < _count)
            {
                Reset(count);
                return;
            }

            _count = count;
        }

        public void SetDelta(int index, double delta)
        {
            if (index < 0 || index >= _count)
            {
                return;
            }

            _deltas.TryGetValue(index, out var previousDelta);
            var difference = delta - previousDelta;
            if (Math.Abs(difference) < 0.01)
            {
                return;
            }

            if (Math.Abs(delta) < 0.01)
            {
                _deltas.Remove(index);
            }
            else
            {
                _deltas[index] = delta;
            }

            var block = index / BlockSize;
            AddToSparseSum(_blockSums, block, difference);
            AddToSparseSum(_groupSums, block / BlocksPerGroup, difference);
        }

        public double PrefixSum(int count)
        {
            count = Math.Clamp(count, 0, _count);
            if (count == 0 || _deltas.Count == 0)
            {
                return 0.0;
            }

            var fullBlocks = count / BlockSize;
            var remainingRows = count % BlockSize;
            var fullGroups = fullBlocks / BlocksPerGroup;
            var remainingBlocks = fullBlocks % BlocksPerGroup;

            var sum = 0.0;
            for (var group = 0; group < fullGroups; group++)
            {
                if (_groupSums.TryGetValue(group, out var groupSum))
                {
                    sum += groupSum;
                }
            }

            var blockStart = fullGroups * BlocksPerGroup;
            for (var blockOffset = 0; blockOffset < remainingBlocks; blockOffset++)
            {
                if (_blockSums.TryGetValue(blockStart + blockOffset, out var blockSum))
                {
                    sum += blockSum;
                }
            }

            var rowStart = fullBlocks * BlockSize;
            for (var offset = 0; offset < remainingRows; offset++)
            {
                if (_deltas.TryGetValue(rowStart + offset, out var delta))
                {
                    sum += delta;
                }
            }

            return sum;
        }

        private static void AddToSparseSum(Dictionary<int, double> sums, int key, double delta)
        {
            sums.TryGetValue(key, out var previous);
            var next = previous + delta;
            if (Math.Abs(next) < 0.01)
            {
                sums.Remove(key);
            }
            else
            {
                sums[key] = next;
            }
        }
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
