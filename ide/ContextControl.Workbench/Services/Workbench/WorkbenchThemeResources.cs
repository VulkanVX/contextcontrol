using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;

namespace ContextControl.Workbench.Services;

public static partial class WorkbenchThemeResources
{
    private const string DefaultUiFontFamily = "fonts:Inter, Segoe UI";
    private const string DefaultCodeFontFamily = "avares://ContextControl.Workbench/Assets/Fonts#Cascadia Code, Consolas";
    private static readonly Uri OriginalAppIcon64 = new("avares://ContextControl.Workbench/Assets/Icons/contextcontrol64x64.png");
    private static readonly Uri OriginalAppIcon32 = new("avares://ContextControl.Workbench/Assets/Icons/contextcontrol32x32.png");
    private static readonly Uri LightAppIcon64 = new("avares://ContextControl.Workbench/Assets/Icons/contextcontrol64x64light.png");
    private static readonly Uri LightAppIcon32 = new("avares://ContextControl.Workbench/Assets/Icons/contextcontrol32x32light.png");
    private static readonly Lazy<Bitmap> OriginalMicroIcon = new(() => LoadBitmap(OriginalAppIcon32));
    private static readonly Lazy<Bitmap> LightMicroIcon = new(() => LoadBitmap(LightAppIcon32));

    public static void Apply(
        Window window,
        string? themeKey,
        string? uiFontFamily = null,
        string? codeFontFamily = null,
        bool updateThemeVariant = true,
        string? skinKey = null,
        string? uiFontColorModeKey = null,
        string? chatAppearanceKey = null,
        string? customUiFontColor = null,
        bool themeAdaptFileCountColor = false,
        bool themeAdaptLocColor = false,
        bool themeAdaptVersionColor = false,
        bool themeAdaptBytesColor = false,
        double? uiFontSize = null)
    {
        var skin = WorkbenchSkins.For(skinKey);
        if (skin.IsActive)
        {
            themeKey = skin.ThemeKey;
            uiFontFamily = skin.UiFontFamily;
            codeFontFamily = skin.CodeFontFamily;
        }

        SetClass(window, "matrix-console-skin", skin.IsMatrixConsole);
        window.Resources["SkinKey"] = skin.Key;
        var palette = ThemePalette.For(themeKey);
        if (updateThemeVariant)
        {
            window.RequestedThemeVariant = palette.IsDark ? ThemeVariant.Dark : ThemeVariant.Light;
        }

        window.Resources["UiFontFamily"] = CreateFontFamily(uiFontFamily, DefaultUiFontFamily, "Segoe UI");
        window.Resources["CodeFontFamily"] = CreateFontFamily(codeFontFamily, DefaultCodeFontFamily, "Consolas");
        window.Resources["UiFontSize"] = NormalizeFontSize(uiFontSize, 11.0);
        Set(window, "AppBackgroundBrush", palette.AppBackground);
        Set(window, "PanelBackgroundBrush", palette.PanelBackground);
        Set(window, "PanelBorderBrush", palette.PanelBorder);
        Set(window, "HeaderBackgroundBrush", palette.HeaderBackground);
        Set(window, "TitleBarBackgroundBrush", palette.TitleBarBackground);
        Set(window, "TitleBarBorderBrush", palette.TitleBarBorder);
        var textPrimary = Color.Parse(palette.TextPrimary);
        var textMuted = Color.Parse(palette.TextMuted);
        if (string.Equals(uiFontColorModeKey?.Trim(), "custom", StringComparison.OrdinalIgnoreCase)
            && TryParseColor(customUiFontColor, out var customText))
        {
            textPrimary = customText;
            textMuted = Blend(customText, Color.Parse(palette.PanelBackground), 0.58);
        }

        Set(window, "TextPrimaryBrush", textPrimary);
        Set(window, "TextMutedBrush", textMuted);
        Set(window, "CommandBackgroundBrush", palette.CommandBackground);
        Set(window, "CommandBorderBrush", palette.CommandBorder);
        Set(window, "CommandPrimaryBackgroundBrush", palette.CommandPrimaryBackground);
        Set(window, "AccentBrush", palette.Accent);
        Set(window, "AccentBorderBrush", palette.AccentBorder);
        Set(window, "ProjectTileBackgroundBrush", palette.ProjectTileBackground);
        Set(window, "ProjectTileActiveBrush", palette.ProjectTileActive);
        Set(window, "DirectoryHighlightBrush", palette.DirectoryHighlight);
        Set(window, "CurrentRowBrush", palette.CurrentRow);
        Set(window, "CurrentRowBorderBrush", palette.CurrentRowBorder);
        Set(window, "FolderTextBrush", palette.FolderText);
        Set(window, "FileTextBrush", palette.FileText);
        Set(window, "ExternalTextBrush", palette.ExternalText);
        Set(window, "NodeTextBrush", palette.NodeText);
        Set(window, "SkipTextBrush", palette.SkipText);
        if (string.Equals(uiFontColorModeKey?.Trim(), "custom", StringComparison.OrdinalIgnoreCase))
        {
            Set(window, "FolderTextBrush", textPrimary);
            Set(window, "FileTextBrush", Blend(textPrimary, Color.Parse(palette.PanelBackground), 0.82));
            Set(window, "NodeTextBrush", Blend(textPrimary, Color.Parse(palette.PanelBackground), 0.72));
        }
        Set(window, "EditorSurfaceBrush", palette.EditorSurface);
        Set(window, "HistoryHoverBrush", palette.HistoryHover);
        Set(window, "HistoryActiveBrush", palette.HistoryActive);
        SetChatResources(window, palette, textPrimary, textMuted, chatAppearanceKey);
        Set(window, "GoodBrush", palette.Good);
        Set(window, "BadBrush", palette.Bad);
        Set(window, "FixedGoodBrush", "#2FA36B");
        Set(window, "FixedBadBrush", "#D95D5D");
        Set(window, "IncludeBackgroundBrush", palette.IncludeBackground);
        Set(window, "IncludeBorderBrush", palette.IncludeBorder);
        Set(window, "IncludeTextBrush", palette.IncludeText);
        Set(window, "SettingsSurfaceBrush", palette.SettingsSurface);
        Set(window, "DropdownBackgroundBrush", palette.DropdownBackground);
        Set(window, "DropdownBorderBrush", palette.DropdownBorder);
        Set(window, "DropdownHoverBrush", palette.DropdownHover);
        Set(window, "DropdownSelectedBrush", palette.DropdownSelected);
        Set(window, "ScopePinBackgroundBrush", palette.Accent);
        Set(window, "ScopePinBorderBrush", palette.AccentBorder);
        Set(window, "MetricFileBrush", palette.Accent);
        Set(window, "MetricLocBrush", palette.IncludeText);
        Set(window, "TreeFileFixedBrush", "#355A86");
        Set(window, "TreeLocFixedBrush", "#D89042");
        Set(window, "TreeVersionFixedBrush", "#858B91");
        Set(window, "TreeBytesFixedBrush", "#858B91");
        Set(window, "MetricFileDisplayBrush", themeAdaptFileCountColor ? palette.Accent : "#355A86");
        Set(window, "MetricLocDisplayBrush", themeAdaptLocColor ? palette.IncludeText : "#D89042");
        Set(window, "MetricVersionDisplayBrush", themeAdaptVersionColor ? palette.TextMuted : "#858B91");
        Set(window, "MetricBytesDisplayBrush", themeAdaptBytesColor ? palette.TextMuted : "#858B91");
        ApplyIconResources(window, palette.UseLightIcons);
    }

    private static void SetChatResources(Window window, ThemePalette palette, Color textPrimary, Color textMuted, string? chatAppearanceKey)
    {
        switch (NormalizeChatAppearanceKey(chatAppearanceKey))
        {
            case "light":
                SetFixedLightChatResources(window);
                return;
            case "adaptive":
                SetAdaptiveChatResources(window, palette, textPrimary, textMuted);
                return;
            default:
                SetFixedDarkChatResources(window);
                return;
        }
    }

    private static void SetFixedDarkChatResources(Window window)
    {
        var bodyText = Color.Parse("#E1E1DD");
        var metaText = Color.Parse("#A4A49E");
        var muted = Color.Parse("#8C8C86");
        var panelBorder = Color.Parse("#282828");
        var commandBorder = Color.Parse("#333333");
        var editorSurface = Color.Parse("#0A0A0A");
        var historyHover = Color.Parse("#171717");
        var accent = Color.Parse("#9F9A8A");
        var accentBorder = Color.Parse("#575246");

        Set(window, "ChatUserBubbleBrush", "#2A2A2A");
        Set(window, "ChatUserBorderBrush", "#3A3A3A");
        Set(window, "ChatAssistantBubbleBrush", "#1A1712");
        Set(window, "ChatAssistantBorderBrush", "#4A3F2B");
        Set(window, "ChatToolBubbleBrush", "#0B0B0B");
        Set(window, "ChatToolBorderBrush", "#2E2E2E");
        Set(window, "ChatLocalBubbleBrush", "#171717");
        Set(window, "ChatLocalBorderBrush", "#383838");

        Set(window, "ChatUserHeaderBrush", "#2A2A2A");
        Set(window, "ChatAssistantHeaderBrush", "#241F17");
        Set(window, "ChatToolHeaderBrush", "#151515");
        Set(window, "ChatLocalHeaderBrush", "#1D1D1D");
        Set(window, "ChatUserOwnerBrush", "#E1E1DD");
        Set(window, "ChatAssistantOwnerBrush", "#E1E1DD");
        Set(window, "ChatToolOwnerBrush", "#E1E1DD");
        Set(window, "ChatLocalOwnerBrush", "#BDBDB6");
        Set(window, "ChatBodyTextBrush", bodyText);
        Set(window, "ChatMetaBrush", metaText);
        Set(window, "ChatHeaderSeparatorBrush", "#2D2D2D");

        SetChatSharedResources(window, bodyText, metaText, muted, panelBorder, commandBorder, editorSurface, historyHover, accent, accentBorder, true);
        Set(window, "ChatSnippetBadgeBrush", "#202020");
        Set(window, "ChatSnippetBadgeBorderBrush", "#3A3A3A");
        Set(window, "ChatSnippetBadgeTextBrush", "#BEBEB8");
        Set(window, "ChatRequestBadgeBrush", "#242424");
        Set(window, "ChatRequestBadgeBorderBrush", "#444444");
        Set(window, "ChatRequestBadgeTextBrush", "#D8D8D0");
    }

    private static void SetFixedLightChatResources(Window window)
    {
        var bodyText = Color.Parse("#253136");
        var metaText = Color.Parse("#65747B");
        var muted = Color.Parse("#74848B");
        var panelBorder = Color.Parse("#C9D3D7");
        var commandBorder = Color.Parse("#BFCBD0");
        var editorSurface = Color.Parse("#F7F9FA");
        var historyHover = Color.Parse("#EEF3F5");
        var accent = Color.Parse("#267A72");
        var accentBorder = Color.Parse("#7CB9B1");

        Set(window, "ChatUserBubbleBrush", "#E3F3F0");
        Set(window, "ChatUserBorderBrush", "#93C9C0");
        Set(window, "ChatAssistantBubbleBrush", "#F2F0EA");
        Set(window, "ChatAssistantBorderBrush", "#CDC4B0");
        Set(window, "ChatToolBubbleBrush", "#F7F9FA");
        Set(window, "ChatToolBorderBrush", "#C9D3D7");
        Set(window, "ChatLocalBubbleBrush", "#F2F0EA");
        Set(window, "ChatLocalBorderBrush", "#C9C0AB");

        Set(window, "ChatUserHeaderBrush", "#D4ECE8");
        Set(window, "ChatAssistantHeaderBrush", "#E7E2D6");
        Set(window, "ChatToolHeaderBrush", "#EAEFF1");
        Set(window, "ChatLocalHeaderBrush", "#E7E2D6");
        Set(window, "ChatUserOwnerBrush", "#17685F");
        Set(window, "ChatAssistantOwnerBrush", "#765E2E");
        Set(window, "ChatToolOwnerBrush", "#253136");
        Set(window, "ChatLocalOwnerBrush", "#59676E");
        Set(window, "ChatBodyTextBrush", bodyText);
        Set(window, "ChatMetaBrush", metaText);
        Set(window, "ChatHeaderSeparatorBrush", "#C8D3D7");

        SetChatSharedResources(window, bodyText, metaText, muted, panelBorder, commandBorder, editorSurface, historyHover, accent, accentBorder, false);
    }

    private static void SetAdaptiveChatResources(Window window, ThemePalette palette, Color textPrimary, Color textMuted)
    {
        var panelBackground = Color.Parse(palette.PanelBackground);
        var panelBorder = Color.Parse(palette.PanelBorder);
        var commandBackground = Color.Parse(palette.CommandBackground);
        var commandBorder = Color.Parse(palette.CommandBorder);
        var commandPrimaryBackground = Color.Parse(palette.CommandPrimaryBackground);
        var accent = Color.Parse(palette.Accent);
        var accentBorder = Color.Parse(palette.AccentBorder);
        var secondary = Color.Parse(palette.IncludeText);
        var secondaryBorder = Color.Parse(palette.IncludeBorder);
        var editorSurface = Color.Parse(palette.EditorSurface);
        var historyHover = Color.Parse(palette.HistoryHover);
        var historyActive = Color.Parse(palette.HistoryActive);

        var userWeight = palette.IsDark ? 0.13 : 0.09;
        var assistantWeight = palette.IsDark ? 0.11 : 0.07;
        var headerWeight = palette.IsDark ? 0.18 : 0.12;
        var softWeight = palette.IsDark ? 0.12 : 0.08;

        var userBubble = Blend(accent, commandPrimaryBackground, userWeight);
        var assistantBubble = Blend(secondary, historyActive, assistantWeight);
        var toolBubble = Blend(textMuted, editorSurface, softWeight * 0.55);
        var localBubble = Blend(textMuted, commandBackground, softWeight * 0.5);

        Set(window, "ChatUserBubbleBrush", userBubble);
        Set(window, "ChatUserBorderBrush", Blend(accentBorder, panelBorder, 0.62));
        Set(window, "ChatAssistantBubbleBrush", assistantBubble);
        Set(window, "ChatAssistantBorderBrush", Blend(secondaryBorder, panelBorder, 0.54));
        Set(window, "ChatToolBubbleBrush", toolBubble);
        Set(window, "ChatToolBorderBrush", commandBorder);
        Set(window, "ChatLocalBubbleBrush", localBubble);
        Set(window, "ChatLocalBorderBrush", panelBorder);

        Set(window, "ChatUserHeaderBrush", Blend(accent, userBubble, headerWeight));
        Set(window, "ChatAssistantHeaderBrush", Blend(secondary, assistantBubble, headerWeight));
        Set(window, "ChatToolHeaderBrush", Blend(textMuted, toolBubble, softWeight));
        Set(window, "ChatLocalHeaderBrush", Blend(textMuted, localBubble, softWeight * 0.9));
        Set(window, "ChatUserOwnerBrush", Blend(accent, textPrimary, palette.IsDark ? 0.68 : 0.56));
        Set(window, "ChatAssistantOwnerBrush", Blend(secondary, textPrimary, palette.IsDark ? 0.66 : 0.54));
        Set(window, "ChatToolOwnerBrush", textPrimary);
        Set(window, "ChatLocalOwnerBrush", Blend(textMuted, textPrimary, 0.42));
        Set(window, "ChatBodyTextBrush", Blend(textPrimary, textMuted, palette.IsDark ? 0.86 : 0.9));
        Set(window, "ChatMetaBrush", Blend(textMuted, textPrimary, palette.IsDark ? 0.86 : 0.82));
        Set(window, "ChatHeaderSeparatorBrush", Blend(commandBorder, panelBorder, palette.IsDark ? 0.72 : 0.64));

        SetChatSharedResources(window, textPrimary, textMuted, textMuted, panelBorder, commandBorder, editorSurface, historyHover, accent, accentBorder, palette.IsDark);
    }

    private static void SetChatSharedResources(
        Window window,
        Color textPrimary,
        Color textMuted,
        Color muted,
        Color panelBorder,
        Color commandBorder,
        Color editorSurface,
        Color historyHover,
        Color accent,
        Color accentBorder,
        bool isDark)
    {
        Set(window, "ChatSnippetShellBrush", Blend(editorSurface, historyHover, isDark ? 0.72 : 0.62));
        Set(window, "ChatSnippetBodyBrush", Blend(historyHover, editorSurface, isDark ? 0.62 : 0.5));
        Set(window, "ChatSnippetBorderBrush", commandBorder);
        Set(window, "ChatSnippetBadgeBrush", Blend(muted, historyHover, isDark ? 0.1 : 0.08));
        Set(window, "ChatSnippetBadgeBorderBrush", Blend(commandBorder, panelBorder, 0.72));
        Set(window, "ChatSnippetBadgeTextBrush", textMuted);
        Set(window, "ChatRequestBadgeBrush", Blend(accent, historyHover, isDark ? 0.23 : 0.16));
        Set(window, "ChatRequestBadgeBorderBrush", Blend(accentBorder, commandBorder, 0.78));
        Set(window, "ChatRequestBadgeTextBrush", Blend(accent, textPrimary, isDark ? 0.74 : 0.62));

        Set(window, "ChatActionHoverBrush", Blend(accent, historyHover, isDark ? 0.15 : 0.1));
        Set(window, "ChatActionBorderBrush", Blend(commandBorder, panelBorder, 0.78));
        Set(window, "ChatActionHoverBorderBrush", Blend(accentBorder, commandBorder, 0.62));
        Set(window, "ChatActionForegroundBrush", textPrimary);
        Set(window, "ChatActionDisabledForegroundBrush", textMuted);

        Set(window, "ChatDiagnosticButtonBrush", Blend(muted, historyHover, isDark ? 0.08 : 0.06));
        Set(window, "ChatDiagnosticButtonHoverBrush", Blend(accent, historyHover, isDark ? 0.12 : 0.08));
        Set(window, "ChatDiagnosticBorderBrush", Blend(commandBorder, panelBorder, 0.8));
        Set(window, "ChatDiagnosticPanelBrush", Blend(muted, editorSurface, isDark ? 0.07 : 0.05));
        Set(window, "ChatDiagnosticTitleBrush", Blend(textMuted, textPrimary, 0.82));
        Set(window, "ChatDiagnosticMarkerBrush", Blend(muted, editorSurface, isDark ? 0.12 : 0.08));
        Set(window, "ChatDiagnosticScrollbarTrackBrush", Blend(commandBorder, editorSurface, 0.7));
        Set(window, "ChatDiagnosticScrollbarThumbBrush", Blend(accent, textMuted, isDark ? 0.42 : 0.32));
        Set(window, "ChatHeaderHoverBrush", isDark
            ? Color.FromArgb(72, accent.R, accent.G, accent.B)
            : Color.FromArgb(62, accent.R, accent.G, accent.B));
        Set(window, "ChatCollapsedHeaderBrush", isDark
            ? Color.FromArgb(92, accent.R, accent.G, accent.B)
            : Color.FromArgb(72, accent.R, accent.G, accent.B));
        Set(window, "ChatCollapsedBorderBrush", Blend(accentBorder, commandBorder, isDark ? 0.78 : 0.62));
    }

    private static string NormalizeChatAppearanceKey(string? key)
    {
        return key?.Trim().ToLowerInvariant() switch
        {
            "light" => "light",
            "adaptive" or "adapt" or "theme" => "adaptive",
            _ => "dark"
        };
    }

    private static void SetClass(Window window, string className, bool enabled)
    {
        if (enabled)
        {
            if (!window.Classes.Contains(className))
            {
                window.Classes.Add(className);
            }

            return;
        }

        window.Classes.Remove(className);
    }

    private static void Set(Window window, string key, string color)
    {
        window.Resources[key] = new SolidColorBrush(Color.Parse(color));
    }

    private static void Set(Window window, string key, Color color)
    {
        window.Resources[key] = new SolidColorBrush(color);
    }

    private static bool TryParseColor(string? value, out Color color)
    {
        try
        {
            color = Color.Parse(string.IsNullOrWhiteSpace(value) ? "#DDE6E8" : value.Trim());
            return true;
        }
        catch
        {
            color = default;
            return false;
        }
    }

    private static Color Blend(Color source, Color target, double sourceWeight)
    {
        sourceWeight = Math.Clamp(sourceWeight, 0, 1);
        var targetWeight = 1 - sourceWeight;
        return Color.FromRgb(
            (byte)Math.Round(source.R * sourceWeight + target.R * targetWeight),
            (byte)Math.Round(source.G * sourceWeight + target.G * targetWeight),
            (byte)Math.Round(source.B * sourceWeight + target.B * targetWeight));
    }

    private static string NormalizeFontFamily(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private static FontFamily CreateFontFamily(string? value, string fallback, string finalFallback)
    {
        try
        {
            return new FontFamily(NormalizeFontFamily(value, fallback));
        }
        catch
        {
            return new FontFamily(finalFallback);
        }
    }

    private static double NormalizeFontSize(double? value, double fallback)
    {
        var next = value.GetValueOrDefault(fallback);
        if (double.IsNaN(next) || double.IsInfinity(next))
        {
            next = fallback;
        }

        return Math.Round(Math.Clamp(next, 8.0, 22.0), 1);
    }

    private static Bitmap LoadBitmap(Uri uri)
    {
        return new Bitmap(AssetLoader.Open(uri));
    }

    private static void ApplyIconResources(Window window, bool useLightIcons)
    {
        var icon64 = useLightIcons ? LightAppIcon64 : OriginalAppIcon64;
        var icon32 = useLightIcons ? LightMicroIcon.Value : OriginalMicroIcon.Value;
        window.Resources["AppMicroIconImage"] = icon32;

        try
        {
            window.Icon = new WindowIcon(AssetLoader.Open(icon64));
        }
        catch
        {
            // A missing icon should not prevent the workbench from opening.
        }
    }

}
