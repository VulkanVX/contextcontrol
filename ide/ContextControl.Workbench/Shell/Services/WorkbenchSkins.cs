namespace ContextControl.Workbench.Services;

public static class WorkbenchSkins
{
    public const string DefaultKey = "default";
    public const string MatrixConsoleKey = "matrix-console";

    private const string MatrixConsoleFontFamily = "OCR A Extended, OCR-A, Fixedsys, Terminal, Lucida Console, Consolas";

    public static IReadOnlyList<WorkbenchSkinDefinition> All { get; } =
    [
        new WorkbenchSkinDefinition(
            DefaultKey,
            "Default",
            "use the selected appearance options",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            ""),
        new WorkbenchSkinDefinition(
            MatrixConsoleKey,
            "Matrix Console",
            "black-and-green Matrix code skin with CRT editor rendering",
            "matrix",
            "Phosphor",
            "matrix-console",
            "Matrix Console",
            "matrix-pixel",
            "OCR-A",
            MatrixConsoleFontFamily,
            "matrix-pixel-ui",
            "OCR-A",
            MatrixConsoleFontFamily)
    ];

    public static WorkbenchSkinDefinition For(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return All[0];
        }

        return All.FirstOrDefault(skin => string.Equals(skin.Key, key.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? All[0];
    }
}

public sealed record WorkbenchSkinDefinition(
    string Key,
    string Name,
    string Description,
    string ThemeKey,
    string ThemeName,
    string SyntaxThemeKey,
    string SyntaxThemeName,
    string CodeFontKey,
    string CodeFontName,
    string CodeFontFamily,
    string UiFontKey,
    string UiFontName,
    string UiFontFamily)
{
    public bool IsActive => !string.Equals(Key, WorkbenchSkins.DefaultKey, StringComparison.OrdinalIgnoreCase);
    public bool IsMatrixConsole => string.Equals(Key, WorkbenchSkins.MatrixConsoleKey, StringComparison.OrdinalIgnoreCase);
}
