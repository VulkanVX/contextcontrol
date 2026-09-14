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

                    var brush = BrushFromScopes(token.Scopes);
                    if (!ReferenceEquals(brush, Palette.Code))
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

        private void HighlightStringsAndComments(string line, IBrush[] brushes)
        {
            for (var index = 0; index < line.Length; index++)
            {
                if (IsLineCommentStart(line, index))
                {
                    Fill(brushes, index, line.Length - index, Palette.Comment);
                    return;
                }

                if (IsBlockCommentStart(line, index))
                {
                    Fill(brushes, index, line.Length - index, Palette.Comment);
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
                if (!ReferenceEquals(brushes[index], Palette.Code))
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
                Fill(brushes, 0, line.Length, Palette.Comment);
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
    }
}
