using Avalonia.Media;

namespace ContextControl.Workbench.Controls;

public sealed partial class ProjectGraphRenderControl
{
    // Resolve tonal levels once per generation/state, not for every node on every frame.
    private readonly record struct NodeAppearance(IBrush Fill, IBrush Border, IBrush Title, IBrush Meta, IBrush Accent);
    private readonly Dictionary<(int Generation, int Kind, int State), NodeAppearance> _appearanceCache = new();
    private RenderResources? _appearanceResources;
    private string? _paletteValue;
    private Color[] _generationColors = DefaultGenerationBasePalette;

    private NodeAppearance Appearance(GraphNode node, bool selected, bool hovered, RenderResources resources)
    {
        var kind = node.Node?.IsExternal == true ? 4 : node.IsAggregate ? 3 : node.Node?.IsFolder == true ? node.Children.Count > 0 ? 1 : 2 : 0;
        var key = (RegionPaletteIndex(node, _generationColors.Length), kind, selected ? 1 : hovered ? 2 : 0);
        if (_appearanceCache.TryGetValue(key, out var appearance)) return appearance;
        var surface = BrushColor(resources.EditorSurface, resources.IsDark ? Colors.Black : Colors.White);
        var card = BrushColor(resources.CommandBackground, surface);
        var generation = GenerationColor(node);
        var fill = kind == 1 ? BlendColor(card, generation, resources.IsDark ? .14 : .09) : card;
        var border = BlendColor(fill, kind is 1 or 2 ? generation : BrushColor(resources.PanelBorder, generation), kind is 1 or 2 ? .48 : .70);
        var accent = kind == 4 ? BrushColor(resources.ExternalText, generation) : generation;
        if (selected || hovered)
        {
            var focus = BrushColor(resources.AccentBorder, generation);
            fill = BlendColor(fill, focus, selected ? .17 : .08);
            border = EnsureContrast(focus, fill, selected ? 3 : 1.8);
        }
        var primary = BrushColor(kind == 4 ? resources.ExternalText : resources.TextPrimary, resources.IsDark ? Colors.White : Colors.Black);
        var secondary = BrushColor(kind == 4 ? resources.ExternalText : resources.TextMuted, primary);
        appearance = new NodeAppearance(SolidBrush(fill), SolidBrush(border),
            SolidBrush(EnsureContrast(primary, fill, 4.5)), SolidBrush(EnsureContrast(secondary, fill, 4.5)),
            SolidBrush(EnsureContrast(accent, fill, 3)));
        _appearanceCache[key] = appearance;
        return appearance;
    }

    private static double Luminance(Color color) => .2126 * SrgbToLinear(color.R) + .7152 * SrgbToLinear(color.G) + .0722 * SrgbToLinear(color.B);
    private static double Contrast(Color a, Color b)
    {
        var x = Luminance(a); var y = Luminance(b);
        return (Math.Max(x, y) + .05) / (Math.Min(x, y) + .05);
    }
    private static Color EnsureContrast(Color foreground, Color background, double minimum)
    {
        if (Contrast(foreground, background) >= minimum) return foreground;
        var target = Contrast(Colors.White, background) >= Contrast(Colors.Black, background) ? Colors.White : Colors.Black;
        for (var step = 1; step <= 20; step++)
        {
            var adjusted = BlendColor(foreground, target, step / 20d);
            if (Contrast(adjusted, background) >= minimum) return adjusted;
        }
        return target;
    }
}
