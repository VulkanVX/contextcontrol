using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace ContextControl.Workbench.Controls;

public sealed class SyntaxPreviewControl : Control
{
    public static readonly StyledProperty<string> ThemeKeyProperty =
        AvaloniaProperty.Register<SyntaxPreviewControl, string>(nameof(ThemeKey), "empty");

    public static readonly StyledProperty<string> SyntaxThemeKeyProperty =
        AvaloniaProperty.Register<SyntaxPreviewControl, string>(nameof(SyntaxThemeKey), "adaptive");

    private const double TopPadding = 13;
    private const double GutterWidth = 44;
    private const double TextStart = 56;
    private const double LineHeight = 17;
    private const double FontSize = 12;
    private const double LineNumberFontSize = 10.5;
    private const double CharWidth = 7.15;

    private static readonly FontFamily CodeFont = new("avares://ContextControl.Workbench/Assets/Fonts#Cascadia Code, Consolas");
    private static readonly Typeface CodeTypeface = new(CodeFont);

    private static readonly PreviewLine[] Lines =
    [
        new([S("public ", TokenKind.Keyword), S("sealed ", TokenKind.Keyword), S("class ", TokenKind.Keyword), S("ThemeProbe", TokenKind.Type)]),
        new([S("{", TokenKind.Bracket)]),
        new([S("    private ", TokenKind.Keyword), S("const ", TokenKind.Keyword), S("int ", TokenKind.Type), S("MaxItems", TokenKind.Variable), S(" = ", TokenKind.Code), S("12", TokenKind.Number), S(";", TokenKind.Code)]),
        new([]),
        new([S("    public ", TokenKind.Keyword), S("string ", TokenKind.Type), S("Render", TokenKind.Function), S("(", TokenKind.Bracket), S("Option ", TokenKind.Type), S("option", TokenKind.Variable), S(")", TokenKind.Bracket)]),
        new([S("    {", TokenKind.Bracket)]),
        new([S("        // Keep the preview close to the real editor.", TokenKind.Comment)]),
        new([S("        return ", TokenKind.Keyword), S("$\"{", TokenKind.String), S("option", TokenKind.Variable), S(".", TokenKind.Code), S("Name", TokenKind.Variable), S("}: {", TokenKind.String), S("MaxItems", TokenKind.Variable), S("}\"", TokenKind.String), S(";", TokenKind.Code)]),
        new([S("    }", TokenKind.Bracket)]),
        new([S("}", TokenKind.Bracket)])
    ];

    public SyntaxPreviewControl()
    {
        ClipToBounds = true;
        MinHeight = 178;
    }

    public string ThemeKey
    {
        get => GetValue(ThemeKeyProperty);
        set => SetValue(ThemeKeyProperty, value);
    }

    public string SyntaxThemeKey
    {
        get => GetValue(SyntaxThemeKeyProperty);
        set => SetValue(SyntaxThemeKeyProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        return new Size(360, 178);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ThemeKeyProperty || change.Property == SyntaxThemeKeyProperty)
        {
            InvalidateVisual();
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var palette = PreviewPalette.For(ThemeKey, SyntaxThemeKey);
        var bounds = new Rect(Bounds.Size);
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        context.DrawRectangle(palette.Background, null, bounds, 6, 6);
        context.DrawRectangle(palette.GutterBackground, null, new Rect(0, 0, GutterWidth, bounds.Height));
        context.DrawLine(
            new Pen(palette.GutterRule, 1),
            new Point(GutterWidth - 0.5, 0),
            new Point(GutterWidth - 0.5, bounds.Height));

        var visibleLines = Math.Min(Lines.Length, Math.Max(0, (int)((bounds.Height - TopPadding) / LineHeight)));
        for (var index = 0; index < visibleLines; index++)
        {
            var y = TopPadding + (index * LineHeight);
            DrawText(context, (index + 1).ToString(CultureInfo.InvariantCulture), palette.LineNumber, new Point(16, y + 1), LineNumberFontSize);

            var x = TextStart;
            foreach (var span in Lines[index].Spans)
            {
                var brush = palette.BrushFor(span.Kind);
                DrawText(context, span.Text, brush, new Point(x, y), FontSize);
                x += span.Text.Length * CharWidth;
            }
        }
    }

    private static PreviewSpan S(string text, TokenKind kind)
    {
        return new PreviewSpan(text, kind);
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
            CodeTypeface,
            fontSize,
            brush);

        context.DrawText(formatted, point);
    }

    private sealed record PreviewLine(IReadOnlyList<PreviewSpan> Spans);
    private sealed record PreviewSpan(string Text, TokenKind Kind);

    private enum TokenKind
    {
        Code,
        Keyword,
        Type,
        String,
        Number,
        Comment,
        Variable,
        Function,
        Bracket
    }

    private sealed class PreviewPalette
    {
        public required IBrush Background { get; init; }
        public required IBrush GutterBackground { get; init; }
        public required IBrush GutterRule { get; init; }
        public required IBrush LineNumber { get; init; }
        public required IBrush Code { get; set; }
        public required IBrush Keyword { get; set; }
        public required IBrush Type { get; set; }
        public required IBrush String { get; set; }
        public required IBrush Number { get; set; }
        public required IBrush Comment { get; set; }
        public required IBrush Variable { get; set; }
        public required IBrush Function { get; set; }
        public required IBrush[] Brackets { get; set; }

        public static PreviewPalette For(string? themeKey, string? syntaxThemeKey)
        {
            var normalizedTheme = string.IsNullOrWhiteSpace(themeKey)
                ? "empty"
                : themeKey.ToLowerInvariant();
            var palette = normalizedTheme switch
            {
                "dark" or "nocturne" or "onyx" or "smoke" or "carbon" or "obsidian" or "ash" or "graphene" or "ruby" or "amethyst" or "ember" or "cobalt" or "contrast" => CreateDark(),
                "matrix" => CreateMatrix(),
                _ => CreateEmpty()
            };

            ApplySyntax(palette, normalizedTheme, syntaxThemeKey);
            return palette;
        }

        public IBrush BrushFor(TokenKind kind)
        {
            return kind switch
            {
                TokenKind.Keyword => Keyword,
                TokenKind.Type => Type,
                TokenKind.String => String,
                TokenKind.Number => Number,
                TokenKind.Comment => Comment,
                TokenKind.Variable => Variable,
                TokenKind.Function => Function,
                TokenKind.Bracket => Brackets[0],
                _ => Code
            };
        }

        private static PreviewPalette CreateEmpty()
        {
            return new PreviewPalette
            {
                Background = Brush(255, 255, 255),
                GutterBackground = Brush(245, 248, 247),
                GutterRule = Brush(213, 223, 224),
                LineNumber = Brush(140, 153, 157),
                Code = Brush(39, 48, 52),
                Keyword = Brush(13, 107, 114),
                Type = Brush(43, 122, 104),
                String = Brush(155, 92, 36),
                Number = Brush(107, 94, 183),
                Comment = Brush(122, 133, 136),
                Variable = Brush(135, 90, 37),
                Function = Brush(113, 93, 31),
                Brackets =
                [
                    Brush(198, 143, 26),
                    Brush(13, 107, 114),
                    Brush(132, 91, 183),
                    Brush(43, 122, 104),
                    Brush(178, 74, 66),
                    Brush(71, 113, 161)
                ]
            };
        }

        private static PreviewPalette CreateDark()
        {
            return new PreviewPalette
            {
                Background = Brush(11, 14, 16),
                GutterBackground = Brush(17, 22, 24),
                GutterRule = Brush(42, 54, 58),
                LineNumber = Brush(126, 140, 145),
                Code = Brush(221, 228, 230),
                Keyword = Brush(107, 211, 209),
                Type = Brush(127, 205, 184),
                String = Brush(230, 183, 116),
                Number = Brush(200, 176, 255),
                Comment = Brush(132, 146, 151),
                Variable = Brush(226, 184, 124),
                Function = Brush(226, 212, 127),
                Brackets =
                [
                    Brush(230, 191, 92),
                    Brush(107, 211, 209),
                    Brush(200, 176, 255),
                    Brush(127, 205, 184),
                    Brush(255, 123, 114),
                    Brush(130, 170, 222)
                ]
            };
        }

        private static PreviewPalette CreateMatrix()
        {
            return new PreviewPalette
            {
                Background = Brush(2, 6, 4),
                GutterBackground = Brush(6, 17, 15),
                GutterRule = Brush(23, 55, 47),
                LineNumber = Brush(103, 172, 143),
                Code = Brush(220, 232, 226),
                Keyword = Brush(101, 240, 178),
                Type = Brush(129, 220, 188),
                String = Brush(230, 183, 116),
                Number = Brush(209, 247, 122),
                Comment = Brush(92, 153, 126),
                Variable = Brush(226, 184, 124),
                Function = Brush(226, 212, 127),
                Brackets =
                [
                    Brush(215, 247, 122),
                    Brush(101, 240, 178),
                    Brush(157, 205, 255),
                    Brush(129, 220, 188),
                    Brush(255, 121, 121),
                    Brush(189, 162, 255)
                ]
            };
        }

        private static void ApplySyntax(PreviewPalette palette, string? themeKey, string? syntaxThemeKey)
        {
            var key = syntaxThemeKey?.ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(key) || key == "adaptive")
            {
                key = AdaptiveSyntaxKey(themeKey);
            }

            var syntax = key switch
            {
                "porcelain-adaptive" => Syntax(
                    Brush(38, 50, 55), Brush(0, 103, 122), Brush(40, 124, 104), Brush(154, 91, 33),
                    Brush(111, 88, 176), Brush(112, 126, 130), Brush(133, 86, 41), Brush(93, 105, 34),
                    [Brush(189, 134, 26), Brush(0, 103, 122), Brush(111, 88, 176), Brush(40, 124, 104), Brush(178, 74, 66), Brush(67, 109, 156)]),
                "alabaster-adaptive" => Syntax(
                    Brush(48, 58, 57), Brush(35, 111, 118), Brush(45, 123, 101), Brush(138, 91, 34),
                    Brush(111, 90, 174), Brush(123, 130, 124), Brush(122, 91, 42), Brush(110, 104, 48),
                    [Brush(184, 134, 33), Brush(35, 111, 118), Brush(124, 97, 178), Brush(45, 123, 101), Brush(170, 76, 73), Brush(73, 110, 156)]),
                "pearl-adaptive" => Syntax(
                    Brush(46, 58, 64), Brush(47, 111, 146), Brush(40, 125, 122), Brush(147, 97, 45),
                    Brush(105, 91, 170), Brush(122, 133, 140), Brush(122, 92, 55), Brush(95, 110, 53),
                    [Brush(176, 127, 34), Brush(47, 111, 146), Brush(125, 98, 180), Brush(40, 125, 122), Brush(168, 78, 82), Brush(74, 117, 160)]),
                "opal-adaptive" => Syntax(
                    Brush(48, 64, 72), Brush(61, 113, 139), Brush(60, 122, 104), Brush(139, 93, 66),
                    Brush(116, 95, 166), Brush(120, 132, 135), Brush(125, 90, 74), Brush(101, 113, 48),
                    [Brush(181, 135, 42), Brush(61, 113, 139), Brush(140, 102, 174), Brush(60, 122, 104), Brush(169, 80, 89), Brush(82, 117, 154)]),
                "mist-adaptive" => Syntax(
                    Brush(43, 57, 62), Brush(61, 119, 130), Brush(55, 126, 109), Brush(139, 95, 47),
                    Brush(106, 94, 166), Brush(118, 130, 134), Brush(121, 91, 55), Brush(93, 112, 58),
                    [Brush(174, 130, 39), Brush(61, 119, 130), Brush(126, 99, 174), Brush(55, 126, 109), Brush(169, 80, 82), Brush(76, 116, 158)]),
                "limestone-adaptive" => Syntax(
                    Brush(45, 58, 53), Brush(82, 111, 101), Brush(47, 124, 103), Brush(136, 94, 51),
                    Brush(109, 93, 161), Brush(119, 128, 122), Brush(121, 91, 60), Brush(92, 116, 57),
                    [Brush(172, 127, 38), Brush(82, 111, 101), Brush(126, 99, 171), Brush(47, 124, 103), Brush(169, 81, 76), Brush(77, 114, 153)]),
                "graphite-adaptive" => Syntax(
                    Brush(218, 224, 230), Brush(169, 183, 198), Brush(211, 178, 119), Brush(191, 160, 106),
                    Brush(185, 155, 214), Brush(126, 137, 148), Brush(208, 167, 131), Brush(184, 201, 135),
                    [Brush(218, 184, 111), Brush(169, 183, 198), Brush(185, 155, 214), Brush(184, 201, 135), Brush(226, 128, 120), Brush(141, 169, 203)]),
                "nocturne-adaptive" => Syntax(
                    Brush(225, 232, 235), Brush(134, 200, 216), Brush(146, 211, 180), Brush(230, 193, 126),
                    Brush(182, 161, 240), Brush(132, 147, 160), Brush(224, 183, 139), Brush(216, 207, 138),
                    [Brush(226, 195, 111), Brush(134, 200, 216), Brush(182, 161, 240), Brush(146, 211, 180), Brush(240, 128, 120), Brush(142, 174, 209)]),
                "onyx-adaptive" => Syntax(
                    Brush(234, 229, 219), Brush(208, 179, 107), Brush(146, 208, 176), Brush(232, 201, 132),
                    Brush(200, 176, 240), Brush(143, 138, 128), Brush(217, 181, 142), Brush(216, 198, 125),
                    [Brush(208, 179, 107), Brush(159, 211, 190), Brush(200, 176, 240), Brush(184, 212, 137), Brush(240, 122, 114), Brush(159, 181, 216)]),
                "smoke-adaptive" => Syntax(
                    Brush(226, 228, 230), Brush(164, 196, 175), Brush(142, 200, 193), Brush(219, 193, 124),
                    Brush(196, 175, 234), Brush(138, 146, 153), Brush(211, 181, 138), Brush(204, 214, 131),
                    [Brush(219, 193, 124), Brush(164, 196, 175), Brush(196, 175, 234), Brush(142, 200, 193), Brush(240, 122, 114), Brush(143, 170, 209)]),
                "carbon-adaptive" => Syntax(
                    Brush(227, 231, 233), Brush(142, 178, 188), Brush(147, 205, 178), Brush(220, 190, 122),
                    Brush(190, 170, 230), Brush(137, 146, 153), Brush(211, 178, 137), Brush(206, 205, 132),
                    [Brush(220, 190, 122), Brush(142, 178, 188), Brush(190, 170, 230), Brush(147, 205, 178), Brush(239, 122, 114), Brush(146, 172, 208)]),
                "obsidian-adaptive" => Syntax(
                    Brush(232, 235, 236), Brush(155, 176, 194), Brush(154, 210, 181), Brush(225, 197, 129),
                    Brush(197, 178, 235), Brush(136, 146, 157), Brush(216, 182, 142), Brush(212, 203, 133),
                    [Brush(225, 197, 129), Brush(155, 176, 194), Brush(197, 178, 235), Brush(154, 210, 181), Brush(241, 124, 116), Brush(155, 181, 215)]),
                "ash-adaptive" => Syntax(
                    Brush(225, 227, 227), Brush(157, 180, 158), Brush(144, 202, 191), Brush(219, 193, 125),
                    Brush(196, 177, 229), Brush(140, 148, 148), Brush(211, 181, 139), Brush(203, 214, 132),
                    [Brush(219, 193, 125), Brush(157, 180, 158), Brush(196, 177, 229), Brush(144, 202, 191), Brush(239, 123, 115), Brush(145, 171, 207)]),
                "graphene-adaptive" => Syntax(
                    Brush(225, 232, 234), Brush(127, 174, 176), Brush(146, 209, 181), Brush(220, 190, 122),
                    Brush(190, 171, 230), Brush(134, 147, 151), Brush(211, 180, 138), Brush(205, 209, 132),
                    [Brush(220, 190, 122), Brush(127, 174, 176), Brush(190, 171, 230), Brush(146, 209, 181), Brush(239, 122, 114), Brush(144, 173, 207)]),
                "ruby-adaptive" => Syntax(
                    Brush(242, 228, 229), Brush(255, 138, 145), Brush(215, 183, 255), Brush(255, 198, 109),
                    Brush(240, 143, 181), Brush(153, 114, 122), Brush(232, 176, 154), Brush(255, 210, 138),
                    [Brush(255, 198, 109), Brush(255, 138, 145), Brush(215, 183, 255), Brush(154, 215, 178), Brush(255, 111, 125), Brush(155, 188, 232)]),
                "amethyst-adaptive" => Syntax(
                    Brush(238, 231, 247), Brush(201, 167, 255), Brush(142, 200, 255), Brush(241, 199, 126),
                    Brush(255, 147, 206), Brush(142, 128, 164), Brush(221, 187, 255), Brush(189, 229, 138),
                    [Brush(241, 199, 126), Brush(201, 167, 255), Brush(255, 147, 206), Brush(142, 200, 255), Brush(255, 127, 146), Brush(189, 229, 138)]),
                "ember-adaptive" => Syntax(
                    Brush(240, 229, 215), Brush(255, 179, 107), Brush(157, 214, 176), Brush(255, 208, 135),
                    Brush(211, 176, 255), Brush(140, 122, 106), Brush(240, 163, 108), Brush(233, 208, 120),
                    [Brush(255, 208, 135), Brush(255, 179, 107), Brush(157, 214, 176), Brush(211, 176, 255), Brush(255, 132, 109), Brush(140, 190, 230)]),
                "verdant-adaptive" => Syntax(
                    Brush(33, 47, 37), Brush(44, 122, 67), Brush(46, 125, 130), Brush(154, 91, 27),
                    Brush(123, 97, 184), Brush(114, 128, 111), Brush(122, 90, 21), Brush(93, 127, 31),
                    [Brush(172, 124, 22), Brush(44, 122, 67), Brush(123, 97, 184), Brush(46, 125, 130), Brush(183, 76, 67), Brush(60, 112, 158)]),
                "cobalt-adaptive" => Syntax(
                    Brush(225, 234, 242), Brush(120, 199, 255), Brush(135, 221, 180), Brush(240, 200, 121),
                    Brush(184, 164, 255), Brush(121, 141, 161), Brush(255, 176, 120), Brush(214, 223, 120),
                    [Brush(240, 200, 121), Brush(120, 199, 255), Brush(184, 164, 255), Brush(135, 221, 180), Brush(255, 125, 125), Brush(130, 170, 222)]),
                "phosphor-adaptive" => Syntax(
                    Brush(223, 255, 239), Brush(101, 240, 178), Brush(129, 220, 188), Brush(230, 183, 116),
                    Brush(215, 247, 122), Brush(92, 153, 126), Brush(226, 184, 124), Brush(157, 205, 255),
                    [Brush(215, 247, 122), Brush(101, 240, 178), Brush(157, 205, 255), Brush(129, 220, 188), Brush(255, 121, 121), Brush(189, 162, 255)]),
                "empty-light" => Syntax(
                    Brush(39, 48, 52), Brush(13, 107, 114), Brush(43, 122, 104), Brush(155, 92, 36),
                    Brush(107, 94, 183), Brush(122, 133, 136), Brush(135, 90, 37), Brush(113, 93, 31),
                    [Brush(198, 143, 26), Brush(13, 107, 114), Brush(132, 91, 183), Brush(43, 122, 104), Brush(178, 74, 66), Brush(71, 113, 161)]),
                "solarized-light" => Syntax(
                    Brush(88, 110, 117), Brush(38, 139, 210), Brush(42, 161, 152), Brush(203, 75, 22),
                    Brush(108, 113, 196), Brush(147, 161, 161), Brush(181, 137, 0), Brush(133, 153, 0),
                    [Brush(181, 137, 0), Brush(38, 139, 210), Brush(211, 54, 130), Brush(42, 161, 152), Brush(220, 50, 47), Brush(108, 113, 196)]),
                "a11y-dark" => Syntax(
                    Brush(221, 228, 230), Brush(107, 211, 209), Brush(127, 205, 184), Brush(230, 183, 116),
                    Brush(200, 176, 255), Brush(132, 146, 151), Brush(226, 184, 124), Brush(226, 212, 127),
                    [Brush(230, 191, 92), Brush(107, 211, 209), Brush(200, 176, 255), Brush(127, 205, 184), Brush(255, 123, 114), Brush(130, 170, 222)]),
                "github-dark" => Syntax(
                    Brush(201, 209, 217), Brush(255, 123, 114), Brush(121, 192, 255), Brush(165, 214, 255),
                    Brush(121, 192, 255), Brush(139, 148, 158), Brush(255, 166, 87), Brush(210, 168, 255),
                    [Brush(255, 212, 128), Brush(121, 192, 255), Brush(210, 168, 255), Brush(86, 211, 100), Brush(255, 123, 114), Brush(165, 214, 255)]),
                "one-dark" => Syntax(
                    Brush(171, 178, 191), Brush(198, 120, 221), Brush(97, 175, 239), Brush(152, 195, 121),
                    Brush(209, 154, 102), Brush(92, 99, 112), Brush(224, 108, 117), Brush(229, 192, 123),
                    [Brush(229, 192, 123), Brush(97, 175, 239), Brush(198, 120, 221), Brush(152, 195, 121), Brush(224, 108, 117), Brush(86, 182, 194)]),
                "nord" => Syntax(
                    Brush(216, 222, 233), Brush(129, 161, 193), Brush(143, 188, 187), Brush(163, 190, 140),
                    Brush(180, 142, 173), Brush(97, 110, 128), Brush(235, 203, 139), Brush(136, 192, 208),
                    [Brush(235, 203, 139), Brush(129, 161, 193), Brush(180, 142, 173), Brush(143, 188, 187), Brush(191, 97, 106), Brush(136, 192, 208)]),
                "monokai" => Syntax(
                    Brush(248, 248, 242), Brush(249, 38, 114), Brush(102, 217, 239), Brush(230, 219, 116),
                    Brush(174, 129, 255), Brush(117, 113, 94), Brush(253, 151, 31), Brush(166, 226, 46),
                    [Brush(230, 219, 116), Brush(102, 217, 239), Brush(174, 129, 255), Brush(166, 226, 46), Brush(249, 38, 114), Brush(253, 151, 31)]),
                "dracula" => Syntax(
                    Brush(248, 248, 242), Brush(255, 121, 198), Brush(139, 233, 253), Brush(241, 250, 140),
                    Brush(189, 147, 249), Brush(98, 114, 164), Brush(255, 184, 108), Brush(80, 250, 123),
                    [Brush(241, 250, 140), Brush(139, 233, 253), Brush(189, 147, 249), Brush(80, 250, 123), Brush(255, 121, 198), Brush(255, 184, 108)]),
                "solarized-dark" => Syntax(
                    Brush(131, 148, 150), Brush(38, 139, 210), Brush(42, 161, 152), Brush(203, 75, 22),
                    Brush(108, 113, 196), Brush(88, 110, 117), Brush(181, 137, 0), Brush(133, 153, 0),
                    [Brush(181, 137, 0), Brush(38, 139, 210), Brush(211, 54, 130), Brush(42, 161, 152), Brush(220, 50, 47), Brush(108, 113, 196)]),
                "high-contrast-dark" => Syntax(
                    Brush(245, 247, 250), Brush(87, 166, 255), Brush(126, 231, 135), Brush(255, 214, 128),
                    Brush(214, 181, 255), Brush(170, 181, 191), Brush(255, 176, 87), Brush(255, 235, 120),
                    [Brush(255, 235, 120), Brush(87, 166, 255), Brush(214, 181, 255), Brush(126, 231, 135), Brush(255, 125, 125), Brush(122, 221, 255)]),
                _ => Syntax(
                    Brush(212, 216, 225), Brush(120, 168, 255), Brush(121, 192, 170), Brush(230, 177, 126),
                    Brush(197, 165, 255), Brush(126, 135, 147), Brush(224, 181, 127), Brush(220, 210, 138),
                    [Brush(238, 205, 122), Brush(111, 177, 255), Brush(205, 153, 255), Brush(113, 212, 174), Brush(242, 132, 130), Brush(160, 170, 246)])
            };

            palette.Code = syntax.Code;
            palette.Keyword = syntax.Keyword;
            palette.Type = syntax.Type;
            palette.String = syntax.String;
            palette.Number = syntax.Number;
            palette.Comment = syntax.Comment;
            palette.Variable = syntax.Variable;
            palette.Function = syntax.Function;
            palette.Brackets = syntax.Brackets;
        }

        private static string AdaptiveSyntaxKey(string? themeKey)
        {
            return themeKey switch
            {
                "alabaster" => "alabaster-adaptive",
                "pearl" => "pearl-adaptive",
                "opal" => "opal-adaptive",
                "mist" => "mist-adaptive",
                "limestone" => "limestone-adaptive",
                "dark" => "graphite-adaptive",
                "nocturne" => "nocturne-adaptive",
                "onyx" => "onyx-adaptive",
                "smoke" => "smoke-adaptive",
                "carbon" => "carbon-adaptive",
                "obsidian" => "obsidian-adaptive",
                "ash" => "ash-adaptive",
                "graphene" => "graphene-adaptive",
                "ruby" => "ruby-adaptive",
                "amethyst" => "amethyst-adaptive",
                "ember" => "ember-adaptive",
                "verdant" => "verdant-adaptive",
                "cobalt" => "cobalt-adaptive",
                "matrix" => "phosphor-adaptive",
                "solarized" => "solarized-light",
                "contrast" => "high-contrast-dark",
                _ => "porcelain-adaptive"
            };
        }

        private static SyntaxPalette Syntax(
            IBrush code,
            IBrush keyword,
            IBrush type,
            IBrush @string,
            IBrush number,
            IBrush comment,
            IBrush variable,
            IBrush function,
            IBrush[] brackets)
        {
            return new SyntaxPalette(code, keyword, type, @string, number, comment, variable, function, brackets);
        }

        private static SolidColorBrush Brush(byte red, byte green, byte blue, byte alpha = 255)
        {
            return new SolidColorBrush(Color.FromArgb(alpha, red, green, blue));
        }
    }

    private sealed record SyntaxPalette(
        IBrush Code,
        IBrush Keyword,
        IBrush Type,
        IBrush String,
        IBrush Number,
        IBrush Comment,
        IBrush Variable,
        IBrush Function,
        IBrush[] Brackets);
}
