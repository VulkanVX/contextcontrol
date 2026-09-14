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
    private readonly record struct VisibleRow(int LineIndex, bool IsFoldSummary, FoldRegion Fold)
    {
        public static VisibleRow ForCodeLine(int lineIndex)
        {
            return new VisibleRow(lineIndex, false, default);
        }

        public static VisibleRow ForFoldSummary(FoldRegion fold)
        {
            return new VisibleRow(fold.StartLine, true, fold);
        }
    }

    private readonly record struct FoldRegion(
        int StartLine,
        int EndLine,
        int OpenLine,
        int OpenColumn,
        int CloseColumn,
        char OpenDelimiter,
        char CloseDelimiter,
        int NestingLevel);

    private readonly record struct TokenSpan(int Start, int Length, IBrush Brush);

    private readonly record struct RenderSegment(int Start, int Length, IBrush Brush);

    private readonly record struct TextPosition(int Line, int Column);

    private static IBrush BrushFromScopes(IReadOnlyList<string> scopes)
    {
        for (var index = scopes.Count - 1; index >= 0; index--)
        {
            var scope = scopes[index];
            if (scope.Contains("comment", StringComparison.Ordinal))
            {
                return Palette.Comment;
            }

            if (scope.Contains("string", StringComparison.Ordinal))
            {
                return Palette.String;
            }

            if (scope.Contains("constant.numeric", StringComparison.Ordinal)
                || scope.Contains("constant.character", StringComparison.Ordinal))
            {
                return Palette.Number;
            }

            if (scope.Contains("constant.language", StringComparison.Ordinal)
                || scope.Contains("keyword", StringComparison.Ordinal)
                || scope.Contains("storage.modifier", StringComparison.Ordinal))
            {
                return Palette.Keyword;
            }

            if (scope.Contains("entity.name.function", StringComparison.Ordinal)
                || scope.Contains("support.function", StringComparison.Ordinal))
            {
                return Palette.Function;
            }

            if (scope.Contains("entity.name.type", StringComparison.Ordinal)
                || scope.Contains("support.type", StringComparison.Ordinal)
                || scope.Contains("storage.type", StringComparison.Ordinal)
                || scope.Contains("entity.name.tag", StringComparison.Ordinal))
            {
                return Palette.Type;
            }

            if (scope.Contains("variable", StringComparison.Ordinal)
                || scope.Contains("entity.name.variable", StringComparison.Ordinal))
            {
                return Palette.Variable;
            }

            if (scope.Contains("markup.heading", StringComparison.Ordinal)
                || scope.Contains("markup.bold", StringComparison.Ordinal))
            {
                return Palette.Keyword;
            }
        }

        return Palette.Code;
    }

    private static int PositiveModulo(long value, int divisor)
    {
        if (divisor <= 0)
        {
            return 0;
        }

        var result = value % divisor;
        return (int)(result < 0 ? result + divisor : result);
    }

    private static SolidColorBrush Brush(byte red, byte green, byte blue, byte alpha = 255)
    {
        return new SolidColorBrush(Color.FromArgb(alpha, red, green, blue));
    }

    private static SolidColorBrush BrushWithAlpha(IBrush brush, byte alpha)
    {
        var color = brush is ISolidColorBrush solid ? solid.Color : Colors.White;
        return new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
    }

    private static void DrawText(DrawingContext context, string text, IBrush brush, Point point, double fontSize)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var formatted = new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            Palette.CodeTypeface,
            fontSize,
            brush);

        context.DrawText(formatted, point);
    }

    private static void Fill(IBrush[] brushes, int start, int length, IBrush brush)
    {
        var end = Math.Min(brushes.Length, start + length);
        for (var index = Math.Max(0, start); index < end; index++)
        {
            brushes[index] = brush;
        }
    }

    private static int FindStringEnd(string line, int start, char quote)
    {
        for (var index = start + 1; index < line.Length; index++)
        {
            if (line[index] == quote && (index == 0 || line[index - 1] != '\\'))
            {
                return index;
            }
        }

        return line.Length - 1;
    }

    private static bool IsIdentifierStart(char value)
    {
        return char.IsLetter(value) || value == '_';
    }

    private static bool IsIdentifierPart(char value)
    {
        return char.IsLetterOrDigit(value) || value is '_' or '-';
    }

    private static bool IsMarkupExtension(string extension)
    {
        return extension is ".xml" or ".xaml" or ".axaml" or ".html" or ".htm";
    }

    private static readonly HashSet<string> CommonKeywords = new(StringComparer.Ordinal)
    {
        "break", "case", "catch", "continue", "default", "do", "else", "false", "finally", "for", "foreach",
        "if", "in", "new", "null", "return", "switch", "this", "throw", "true", "try", "while"
    };

    private static readonly HashSet<string> CSharpKeywords = new(StringComparer.Ordinal)
    {
        "abstract", "as", "async", "await", "base", "bool", "break", "case", "catch", "class", "const",
        "continue", "decimal", "default", "do", "double", "else", "enum", "event", "false", "finally",
        "fixed", "float", "for", "foreach", "get", "global", "if", "in", "int", "interface", "internal",
        "is", "lock", "long", "namespace", "new", "not", "null", "object", "operator", "out", "override",
        "params", "private", "protected", "public", "readonly", "record", "ref", "return", "sealed", "set",
        "short", "static", "string", "struct", "switch", "this", "throw", "true", "try", "typeof", "using",
        "var", "virtual", "void", "volatile", "when", "while", "with", "yield"
    };

    private static readonly HashSet<string> PowerShellKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "begin", "break", "catch", "class", "continue", "data", "do", "dynamicparam", "else", "elseif", "end",
        "enum", "exit", "filter", "finally", "for", "foreach", "from", "function", "if", "in", "param",
        "process", "return", "switch", "throw", "trap", "try", "until", "using", "var", "while", "workflow"
    };

    private static readonly HashSet<string> JavaScriptKeywords = new(StringComparer.Ordinal)
    {
        "async", "await", "break", "case", "catch", "class", "const", "continue", "debugger", "default",
        "delete", "do", "else", "export", "extends", "false", "finally", "for", "from", "function", "if",
        "import", "in", "instanceof", "let", "new", "null", "return", "static", "super", "switch", "this",
        "throw", "true", "try", "typeof", "undefined", "var", "void", "while", "yield"
    };

    private static readonly HashSet<string> PythonKeywords = new(StringComparer.Ordinal)
    {
        "and", "as", "assert", "async", "await", "break", "class", "continue", "def", "del", "elif", "else",
        "except", "False", "finally", "for", "from", "global", "if", "import", "in", "is", "lambda", "None",
        "nonlocal", "not", "or", "pass", "raise", "return", "True", "try", "while", "with", "yield"
    };

    private static readonly HashSet<string> JsonKeywords = new(StringComparer.Ordinal)
    {
        "false", "null", "true"
    };

    private static readonly HashSet<string> CssKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "auto", "block", "flex", "grid", "important", "inherit", "inline", "none", "relative", "absolute",
        "fixed", "sticky", "solid", "transparent"
    };
}
