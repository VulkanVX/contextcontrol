using Avalonia.Media;

namespace ContextControl.Workbench.ViewModels;

public sealed class ProjectGraphGenerationColorViewModel : ObservableObject
{
    private string _colorHex;
    private IBrush _swatchBrush;

    public ProjectGraphGenerationColorViewModel(int generation, string colorHex, Action<ProjectGraphGenerationColorViewModel> changed)
    {
        Generation = generation;
        Label = generation.ToString();
        _colorHex = NormalizeColor(colorHex);
        _swatchBrush = Brush.Parse(_colorHex);
        Changed = changed;
    }

    public int Generation { get; }
    public string Label { get; }
    public IBrush SwatchBrush => _swatchBrush;
    private Action<ProjectGraphGenerationColorViewModel> Changed { get; }

    public Color Color
    {
        get => Color.Parse(_colorHex);
        set => ColorHex = FormatColor(value);
    }

    public string ColorHex
    {
        get => _colorHex;
        set
        {
            var normalized = NormalizeColor(value);
            if (!SetProperty(ref _colorHex, normalized))
            {
                return;
            }

            _swatchBrush = Brush.Parse(normalized);
            OnPropertyChanged(nameof(SwatchBrush));
            OnPropertyChanged(nameof(Color));
            Changed(this);
        }
    }

    private static string FormatColor(Color color)
    {
        return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    }

    private static string NormalizeColor(string? value)
    {
        try
        {
            var color = Color.Parse(string.IsNullOrWhiteSpace(value) ? "#7A858B" : value.Trim());
            return FormatColor(color);
        }
        catch
        {
            return "#7A858B";
        }
    }
}
