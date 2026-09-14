using Avalonia.Media;

namespace ContextControl.Workbench.ViewModels;

public sealed class ThemeOptionViewModel(
    string key,
    string name,
    string description,
    string? fontFamily = null,
    string? tone = null,
    string? category = null)
{
    public string Key { get; } = key;
    public string Name { get; } = name;
    public string Description { get; } = description;
    public string FontFamily { get; } = fontFamily ?? "";
    public FontFamily PreviewFontFamily { get; } = CreateFontFamily(fontFamily, "Segoe UI");
    public string Tone { get; } = tone ?? "";
    public string Category { get; } = category ?? "";
    public string DisplayName =>
        string.IsNullOrWhiteSpace(Tone) || string.IsNullOrWhiteSpace(Category)
            ? Name
            : $"{Name} ({Tone}, {Category})";

    public override string ToString() => DisplayName;

    private static FontFamily CreateFontFamily(string? value, string fallback)
    {
        try
        {
            return new FontFamily(string.IsNullOrWhiteSpace(value) ? fallback : value.Trim());
        }
        catch
        {
            return new FontFamily(fallback);
        }
    }
}
