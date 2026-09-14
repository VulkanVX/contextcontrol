using Avalonia.Media;

namespace ContextControl.Workbench.Controls;

public sealed partial class CodeEditor
{
    private static class Palette
    {
        private static PaletteSet _current = CreateEmpty();

        public static FontFamily CodeFont { get; private set; } = new("avares://ContextControl.Workbench/Assets/Fonts#Cascadia Code, Consolas");
        public static Typeface CodeTypeface { get; private set; } = new(CodeFont);

        public static IBrush Background => _current.Background;
        public static IBrush GutterBackground => _current.GutterBackground;
        public static IBrush GutterRule => _current.GutterRule;
        public static IBrush LineNumber => _current.LineNumber;
        public static IBrush FoldArrow => _current.FoldArrow;
        public static IBrush FoldButtonBackground => _current.FoldButtonBackground;
        public static IBrush FoldButtonBorder => _current.FoldButtonBorder;
        public static IBrush Code => _current.Code;
        public static IBrush Keyword => _current.Keyword;
        public static IBrush Type => _current.Type;
        public static IBrush String => _current.String;
        public static IBrush Number => _current.Number;
        public static IBrush Comment => _current.Comment;
        public static IBrush Variable => _current.Variable;
        public static IBrush Function => _current.Function;
        public static IBrush[] Brackets => _current.Brackets;
        public static IBrush MinimapShell => _current.MinimapShell;
        public static IBrush MinimapCanvas => _current.MinimapCanvas;
        public static IBrush MinimapBorder => _current.MinimapBorder;
        public static IBrush MinimapCode => _current.MinimapCode;
        public static IBrush MinimapKeyword => _current.MinimapKeyword;
        public static IBrush MinimapComment => _current.MinimapComment;
        public static IBrush MinimapAdd => _current.MinimapAdd;
        public static IBrush MinimapDelete => _current.MinimapDelete;
        public static IBrush MinimapViewport => _current.MinimapViewport;
        public static IBrush MinimapViewportBorder => _current.MinimapViewportBorder;
        public static IBrush AddLineBackground => _current.AddLineBackground;
        public static IBrush DeleteLineBackground => _current.DeleteLineBackground;
        public static IBrush AddStripe => _current.AddStripe;
        public static IBrush DeleteStripe => _current.DeleteStripe;
        public static IBrush SelectionBackground => _current.SelectionBackground;

        public static void Use(string? themeKey, string? syntaxThemeKey)
        {
            var normalizedTheme = themeKey?.ToLowerInvariant();
            _current = normalizedTheme switch
            {
                "dark" or "nocturne" or "onyx" or "smoke" or "carbon" or "obsidian" or "ash" or "graphene" or "ruby" or "amethyst" or "ember" or "cobalt" or "contrast" => CreateDark(),
                "matrix" => CreateMatrix(),
                _ => CreateEmpty()
            };
            ApplySyntax(_current, normalizedTheme, syntaxThemeKey);
        }

        public static void UseFont(string? codeFontFamily)
        {
            var family = string.IsNullOrWhiteSpace(codeFontFamily)
                ? "avares://ContextControl.Workbench/Assets/Fonts#Cascadia Code, Consolas"
                : codeFontFamily.Trim();
            try
            {
                CodeFont = new FontFamily(family);
            }
            catch
            {
                CodeFont = new FontFamily("Consolas");
            }

            CodeTypeface = new Typeface(CodeFont);
        }

        private static PaletteSet CreateEmpty()
        {
            return new PaletteSet
            {
                Background = Brush(255, 255, 255, 218),
                GutterBackground = Brush(245, 248, 247, 168),
                GutterRule = Brush(213, 223, 224),
                LineNumber = Brush(140, 153, 157),
                FoldArrow = Brush(83, 97, 102),
                FoldButtonBackground = Brush(254, 254, 250, 236),
                FoldButtonBorder = Brush(184, 197, 199, 190),
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
                ],
                MinimapShell = Brush(234, 240, 240, 248),
                MinimapCanvas = Brush(255, 255, 255, 255),
                MinimapBorder = Brush(184, 197, 199, 190),
                MinimapCode = Brush(64, 76, 80, 148),
                MinimapKeyword = Brush(13, 107, 114, 168),
                MinimapComment = Brush(122, 133, 136, 110),
                MinimapAdd = Brush(30, 127, 87, 160),
                MinimapDelete = Brush(178, 74, 66, 160),
                MinimapViewport = Brush(13, 107, 114, 38),
                MinimapViewportBorder = Brush(13, 107, 114, 96),
                AddLineBackground = Brush(30, 127, 87, 34),
                DeleteLineBackground = Brush(178, 74, 66, 34),
                AddStripe = Brush(30, 127, 87, 130),
                DeleteStripe = Brush(178, 74, 66, 130),
                SelectionBackground = Brush(13, 107, 114, 72)
            };
        }

        private static PaletteSet CreateDark()
        {
            return new PaletteSet
            {
                Background = Brush(11, 14, 16, 210),
                GutterBackground = Brush(17, 22, 24, 164),
                GutterRule = Brush(42, 54, 58),
                LineNumber = Brush(126, 140, 145),
                FoldArrow = Brush(183, 199, 203),
                FoldButtonBackground = Brush(22, 29, 32, 236),
                FoldButtonBorder = Brush(65, 80, 85, 210),
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
                ],
                MinimapShell = Brush(22, 29, 32, 248),
                MinimapCanvas = Brush(11, 14, 16, 255),
                MinimapBorder = Brush(65, 80, 85, 210),
                MinimapCode = Brush(221, 228, 230, 120),
                MinimapKeyword = Brush(107, 211, 209, 150),
                MinimapComment = Brush(132, 146, 151, 105),
                MinimapAdd = Brush(115, 213, 155, 155),
                MinimapDelete = Brush(255, 123, 114, 155),
                MinimapViewport = Brush(107, 211, 209, 34),
                MinimapViewportBorder = Brush(107, 211, 209, 100),
                AddLineBackground = Brush(115, 213, 155, 34),
                DeleteLineBackground = Brush(255, 123, 114, 34),
                AddStripe = Brush(115, 213, 155, 150),
                DeleteStripe = Brush(255, 123, 114, 150),
                SelectionBackground = Brush(107, 211, 209, 58)
            };
        }

        private static PaletteSet CreateMatrix()
        {
            return new PaletteSet
            {
                Background = Brush(0, 0, 0, 245),
                GutterBackground = Brush(0, 12, 4, 210),
                GutterRule = Brush(0, 166, 80),
                LineNumber = Brush(38, 180, 89),
                FoldArrow = Brush(0, 255, 102),
                FoldButtonBackground = Brush(0, 24, 8, 238),
                FoldButtonBorder = Brush(0, 255, 102, 218),
                Code = Brush(183, 255, 210),
                Keyword = Brush(0, 255, 102),
                Type = Brush(112, 255, 168),
                String = Brush(76, 255, 140),
                Number = Brush(160, 255, 194),
                Comment = Brush(35, 132, 74),
                Variable = Brush(91, 255, 153),
                Function = Brush(221, 255, 233),
                Brackets =
                [
                    Brush(0, 255, 102),
                    Brush(79, 255, 145),
                    Brush(132, 255, 181),
                    Brush(183, 255, 210),
                    Brush(46, 205, 104),
                    Brush(221, 255, 233)
                ],
                MinimapShell = Brush(0, 10, 4, 252),
                MinimapCanvas = Brush(0, 0, 0, 255),
                MinimapBorder = Brush(0, 255, 102, 220),
                MinimapCode = Brush(122, 255, 174, 118),
                MinimapKeyword = Brush(0, 255, 102, 160),
                MinimapComment = Brush(35, 132, 74, 122),
                MinimapAdd = Brush(0, 255, 102, 155),
                MinimapDelete = Brush(91, 255, 153, 138),
                MinimapViewport = Brush(0, 255, 102, 42),
                MinimapViewportBorder = Brush(0, 255, 102, 125),
                AddLineBackground = Brush(0, 255, 102, 32),
                DeleteLineBackground = Brush(91, 255, 153, 24),
                AddStripe = Brush(0, 255, 102, 160),
                DeleteStripe = Brush(91, 255, 153, 130),
                SelectionBackground = Brush(0, 255, 102, 64)
            };
        }

        private static void ApplySyntax(PaletteSet palette, string? themeKey, string? syntaxThemeKey)
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
                "matrix-console" => Syntax(
                    Brush(183, 255, 210), Brush(0, 255, 102), Brush(112, 255, 168), Brush(76, 255, 140),
                    Brush(160, 255, 194), Brush(35, 132, 74), Brush(91, 255, 153), Brush(221, 255, 233),
                    [Brush(0, 255, 102), Brush(79, 255, 145), Brush(132, 255, 181), Brush(183, 255, 210), Brush(46, 205, 104), Brush(221, 255, 233)]),
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
            palette.MinimapCode = WithAlpha(syntax.Code, 132);
            palette.MinimapKeyword = WithAlpha(syntax.Keyword, 160);
            palette.MinimapComment = WithAlpha(syntax.Comment, 112);
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

        private static SolidColorBrush WithAlpha(IBrush brush, byte alpha)
        {
            var color = brush is ISolidColorBrush solid ? solid.Color : Colors.White;
            return new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
        }

        private static SolidColorBrush Brush(byte red, byte green, byte blue, byte alpha = 255)
        {
            return new SolidColorBrush(Color.FromArgb(alpha, red, green, blue));
        }
    }

    private sealed class PaletteSet
    {
        public required IBrush Background { get; init; }
        public required IBrush GutterBackground { get; init; }
        public required IBrush GutterRule { get; init; }
        public required IBrush LineNumber { get; init; }
        public required IBrush FoldArrow { get; init; }
        public required IBrush FoldButtonBackground { get; init; }
        public required IBrush FoldButtonBorder { get; init; }
        public required IBrush Code { get; set; }
        public required IBrush Keyword { get; set; }
        public required IBrush Type { get; set; }
        public required IBrush String { get; set; }
        public required IBrush Number { get; set; }
        public required IBrush Comment { get; set; }
        public required IBrush Variable { get; set; }
        public required IBrush Function { get; set; }
        public required IBrush[] Brackets { get; set; }
        public required IBrush MinimapShell { get; init; }
        public required IBrush MinimapCanvas { get; init; }
        public required IBrush MinimapBorder { get; init; }
        public required IBrush MinimapCode { get; set; }
        public required IBrush MinimapKeyword { get; set; }
        public required IBrush MinimapComment { get; set; }
        public required IBrush MinimapAdd { get; init; }
        public required IBrush MinimapDelete { get; init; }
        public required IBrush MinimapViewport { get; init; }
        public required IBrush MinimapViewportBorder { get; init; }
        public required IBrush AddLineBackground { get; init; }
        public required IBrush DeleteLineBackground { get; init; }
        public required IBrush AddStripe { get; init; }
        public required IBrush DeleteStripe { get; init; }
        public required IBrush SelectionBackground { get; init; }
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
