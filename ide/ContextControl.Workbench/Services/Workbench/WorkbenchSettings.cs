// CC-DESC: Persists Workbench appearance and Context Control preferences between app runs.

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ContextControl.Workbench.Services;

public sealed class WorkbenchSettings
{
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);
    private const string OllamaModelsEnvironmentVariable = "OLLAMA_MODELS";

    private WorkbenchSettings(
        string settingsPath,
        string contextControlRoot,
        string skinKey,
        string themeKey,
        string syntaxThemeKey,
        string codeFontKey,
        string uiFontKey,
        string uiFontColorModeKey,
        string customUiFontColor,
        string foldArrowPositionKey,
        bool showFoldArrows,
        bool showSummaryArrowBorders,
        bool useParentChildArrowIndentation,
        bool showVerticalScopeLines,
        string summaryFoldKinds,
        bool useColorfulFamilies,
        bool showAppearanceCodePreview,
        bool themeAdaptFileCountColor,
        bool themeAdaptLocColor,
        bool themeAdaptVersionColor,
        bool themeAdaptBytesColor,
        double uiFontSize,
        double codeEditorFontSize,
        double promptWindowFontSize,
        double chatWindowFontSize,
        string chatAppearanceKey,
        string selectedAiRoute,
        string selectedLocalModel,
        string selectedImageModel,
        string fileRequestModel,
        string patchWriteModel,
        string patchReviewModel,
        string chatModel,
        string codexModel,
        string codexReasoningEffort,
        string ollamaModelsDirectory,
        string huggingFaceToken,
        string localLlmSortOption,
        string localLlmProviderFilter,
        string localLlmSourceFilter,
        string localLlmPurposeFilter,
        string localLlmBaseFilter,
        string localLlmContextFilter,
        string localLlmRequirementFilter,
        string promptModeKey,
        bool isAutopilotEnabled,
        bool promptBarOpenByDefault,
        bool autoSwitchPromptModesToChat,
        string workspaceModeKey,
        string externalBrowserKey,
        bool showSkippedFiles,
        bool showProjectFilesPane,
        bool projectFilesTopLocMode,
        bool showBrowserRoutingPane,
        bool showProjectGraphTreePane,
        string projectGraphGenerationColors)
    {
        SettingsPath = settingsPath;
        ContextControlRoot = contextControlRoot;
        SkinKey = NormalizeSkinKey(skinKey);
        ThemeKey = NormalizeKey(themeKey, "empty");
        SyntaxThemeKey = NormalizeKey(syntaxThemeKey, "adaptive");
        CodeFontKey = NormalizeKey(codeFontKey, "cascadia-code");
        UiFontKey = NormalizeKey(uiFontKey, "aptos");
        UiFontColorModeKey = NormalizeUiFontColorModeKey(uiFontColorModeKey);
        CustomUiFontColor = NormalizeColor(customUiFontColor, "#DDE6E8");
        FoldArrowPositionKey = NormalizeFoldArrowPositionKey(foldArrowPositionKey);
        ShowFoldArrows = showFoldArrows;
        ShowSummaryArrowBorders = showSummaryArrowBorders;
        UseParentChildArrowIndentation = useParentChildArrowIndentation;
        ShowVerticalScopeLines = showVerticalScopeLines;
        SummaryFoldKinds = NormalizeSummaryFoldKinds(summaryFoldKinds);
        UseColorfulFamilies = useColorfulFamilies;
        ShowAppearanceCodePreview = showAppearanceCodePreview;
        ThemeAdaptFileCountColor = themeAdaptFileCountColor;
        ThemeAdaptLocColor = themeAdaptLocColor;
        ThemeAdaptVersionColor = themeAdaptVersionColor;
        ThemeAdaptBytesColor = themeAdaptBytesColor;
        UiFontSize = NormalizeFontSize(uiFontSize, 11.0);
        CodeEditorFontSize = NormalizeFontSize(codeEditorFontSize, 12.0);
        PromptWindowFontSize = NormalizeFontSize(promptWindowFontSize, 12.0);
        ChatWindowFontSize = NormalizeFontSize(chatWindowFontSize, 10.0);
        ChatAppearanceKey = NormalizeChatAppearanceKey(chatAppearanceKey);
        SelectedAiRoute = string.IsNullOrWhiteSpace(selectedAiRoute) ? "Browser: ChatGPT" : selectedAiRoute.Trim();
        SelectedLocalModel = string.IsNullOrWhiteSpace(selectedLocalModel) ? "qwen2.5-coder:3b" : selectedLocalModel.Trim();
        SelectedImageModel = string.IsNullOrWhiteSpace(selectedImageModel) ? "segmind/tiny-sd" : selectedImageModel.Trim();
        FileRequestModel = NormalizeModelId(fileRequestModel, "qwen2.5-coder:1.5b");
        PatchWriteModel = NormalizeModelId(patchWriteModel, "qwen2.5-coder:3b");
        PatchReviewModel = NormalizeModelId(patchReviewModel, "phi4-mini");
        ChatModel = NormalizeModelId(chatModel, "qwen2.5-coder:3b");
        CodexModel = NormalizeOptionalModelId(codexModel);
        CodexReasoningEffort = NormalizeCodexReasoningEffort(codexReasoningEffort);
        OllamaModelsDirectory = NormalizeDirectoryPath(ollamaModelsDirectory);
        HuggingFaceToken = NormalizeToken(huggingFaceToken);
        LocalLlmSortOption = NormalizeLocalLlmFilter(localLlmSortOption, "Newest");
        LocalLlmProviderFilter = NormalizeLocalLlmFilter(localLlmProviderFilter, "All providers");
        LocalLlmSourceFilter = NormalizeLocalLlmFilter(localLlmSourceFilter, "All");
        LocalLlmPurposeFilter = NormalizeLocalLlmFilter(localLlmPurposeFilter, "All purposes");
        LocalLlmBaseFilter = NormalizeLocalLlmFilter(localLlmBaseFilter, "All bases");
        LocalLlmContextFilter = NormalizeLocalLlmFilter(localLlmContextFilter, "Any");
        LocalLlmRequirementFilter = NormalizeLocalLlmFilter(localLlmRequirementFilter, "Any requirement");
        PromptModeKey = NormalizePromptModeKey(promptModeKey);
        IsAutopilotEnabled = isAutopilotEnabled;
        PromptBarOpenByDefault = promptBarOpenByDefault;
        AutoSwitchPromptModesToChat = autoSwitchPromptModesToChat;
        WorkspaceModeKey = NormalizeWorkspaceModeKey(workspaceModeKey);
        ExternalBrowserKey = NormalizeExternalBrowserKey(externalBrowserKey);
        ShowSkippedFiles = showSkippedFiles;
        ShowProjectFilesPane = showProjectFilesPane;
        ProjectFilesTopLocMode = projectFilesTopLocMode;
        ShowBrowserRoutingPane = showBrowserRoutingPane;
        ShowProjectGraphTreePane = showProjectGraphTreePane;
        ProjectGraphGenerationColors = NormalizeGraphGenerationColors(projectGraphGenerationColors);
    }

    public string SettingsPath { get; }
    public string ContextControlRoot { get; }
    public string SkinKey { get; set; }
    public string ThemeKey { get; set; }
    public string SyntaxThemeKey { get; set; }
    public string CodeFontKey { get; set; }
    public string UiFontKey { get; set; }
    public string UiFontColorModeKey { get; set; }
    public string CustomUiFontColor { get; set; }
    public string FoldArrowPositionKey { get; set; }
    public bool ShowFoldArrows { get; set; }
    public bool ShowSummaryArrowBorders { get; set; }
    public bool UseParentChildArrowIndentation { get; set; }
    public bool ShowVerticalScopeLines { get; set; }
    public string SummaryFoldKinds { get; set; }
    public bool UseColorfulFamilies { get; set; }
    public bool ShowAppearanceCodePreview { get; set; }
    public bool ThemeAdaptFileCountColor { get; set; }
    public bool ThemeAdaptLocColor { get; set; }
    public bool ThemeAdaptVersionColor { get; set; }
    public bool ThemeAdaptBytesColor { get; set; }
    public double UiFontSize { get; set; }
    public double CodeEditorFontSize { get; set; }
    public double PromptWindowFontSize { get; set; }
    public double ChatWindowFontSize { get; set; }
    public string ChatAppearanceKey { get; set; }
    public string SelectedAiRoute { get; set; }
    public string SelectedLocalModel { get; set; }
    public string SelectedImageModel { get; set; }
    public string FileRequestModel { get; set; }
    public string PatchWriteModel { get; set; }
    public string PatchReviewModel { get; set; }
    public string ChatModel { get; set; }
    public string CodexModel { get; set; }
    public string CodexReasoningEffort { get; set; }
    public string OllamaModelsDirectory { get; set; }
    public string HuggingFaceToken { get; set; }
    public string LocalLlmSortOption { get; set; }
    public string LocalLlmProviderFilter { get; set; }
    public string LocalLlmSourceFilter { get; set; }
    public string LocalLlmPurposeFilter { get; set; }
    public string LocalLlmBaseFilter { get; set; }
    public string LocalLlmContextFilter { get; set; }
    public string LocalLlmRequirementFilter { get; set; }
    public string PromptModeKey { get; set; }
    public bool IsAutopilotEnabled { get; set; }
    public bool PromptBarOpenByDefault { get; set; }
    public bool AutoSwitchPromptModesToChat { get; set; }
    public string WorkspaceModeKey { get; set; }
    public string ExternalBrowserKey { get; set; }
    public bool ShowSkippedFiles { get; set; }
    public bool ShowProjectFilesPane { get; set; }
    public bool ProjectFilesTopLocMode { get; set; }
    public bool ShowBrowserRoutingPane { get; set; }
    public bool ShowProjectGraphTreePane { get; set; }
    public string ProjectGraphGenerationColors { get; set; }

    public static WorkbenchSettings Load()
    {
        var contextControlRoot = ResolveContextControlRoot();
        var settingsPath = Path.Combine(contextControlRoot, ".ccWorkbench.settings.json");
        var data = new WorkbenchSettingsJson();

        if (File.Exists(settingsPath))
        {
            try
            {
                data = JsonSerializer.Deserialize<WorkbenchSettingsJson>(File.ReadAllText(settingsPath), JsonOptions)
                    ?? new WorkbenchSettingsJson();
            }
            catch
            {
                data = new WorkbenchSettingsJson();
            }
        }

        return new WorkbenchSettings(
            settingsPath,
            contextControlRoot,
            data.SkinKey ?? WorkbenchSkins.DefaultKey,
            data.ThemeKey ?? "empty",
            data.SyntaxThemeKey ?? "adaptive",
            data.CodeFontKey ?? "cascadia-code",
            data.UiFontKey ?? "aptos",
            data.UiFontColorModeKey ?? "theme",
            data.CustomUiFontColor ?? "#DDE6E8",
            data.FoldArrowPositionKey ?? "codeEditor",
            data.ShowFoldArrows ?? true,
            data.ShowSummaryArrowBorders ?? (data.UseColorfulFamilies ?? true),
            data.UseParentChildArrowIndentation ?? true,
            data.ShowVerticalScopeLines ?? true,
            data.SummaryFoldKinds ?? DefaultSummaryFoldKinds,
            data.UseColorfulFamilies ?? true,
            data.ShowAppearanceCodePreview ?? true,
            data.ThemeAdaptFileCountColor ?? false,
            data.ThemeAdaptLocColor ?? false,
            data.ThemeAdaptVersionColor ?? false,
            data.ThemeAdaptBytesColor ?? false,
            data.UiFontSize ?? 11.0,
            data.CodeEditorFontSize ?? 12.0,
            data.PromptWindowFontSize ?? 12.0,
            data.ChatWindowFontSize ?? 10.0,
            data.ChatAppearanceKey ?? "dark",
            data.SelectedAiRoute ?? "Browser: ChatGPT",
            data.SelectedLocalModel ?? "qwen2.5-coder:3b",
            data.SelectedImageModel ?? "segmind/tiny-sd",
            data.FileRequestModel ?? "qwen2.5-coder:1.5b",
            data.PatchWriteModel ?? "qwen2.5-coder:3b",
            data.PatchReviewModel ?? "phi4-mini",
            data.ChatModel ?? "qwen2.5-coder:3b",
            data.CodexModel ?? "",
            data.CodexReasoningEffort ?? "",
            data.OllamaModelsDirectory ?? ResolveDefaultOllamaModelsDirectory(),
            data.HuggingFaceToken ?? "",
            data.LocalLlmSortOption ?? "Newest",
            data.LocalLlmProviderFilter ?? "All providers",
            data.LocalLlmSourceFilter ?? "All",
            data.LocalLlmPurposeFilter ?? "All purposes",
            data.LocalLlmBaseFilter ?? "All bases",
            data.LocalLlmContextFilter ?? "Any",
            data.LocalLlmRequirementFilter ?? "Any requirement",
            data.PromptModeKey ?? "context",
            data.IsAutopilotEnabled ?? false,
            data.PromptBarOpenByDefault ?? false,
            data.AutoSwitchPromptModesToChat ?? true,
            data.WorkspaceModeKey ?? "code",
            data.ExternalBrowserKey ?? "default",
            data.ShowSkippedFiles ?? false,
            data.ShowProjectFilesPane ?? true,
            data.ProjectFilesTopLocMode ?? false,
            data.ShowBrowserRoutingPane ?? true,
            data.ShowProjectGraphTreePane ?? true,
            data.ProjectGraphGenerationColors ?? DefaultProjectGraphGenerationColors);
    }

    public void Save()
    {
        var parent = Path.GetDirectoryName(SettingsPath);
        if (!string.IsNullOrWhiteSpace(parent))
        {
            Directory.CreateDirectory(parent);
        }

        var data = new WorkbenchSettingsJson
        {
            SkinKey = NormalizeSkinKey(SkinKey),
            ThemeKey = NormalizeKey(ThemeKey, "empty"),
            SyntaxThemeKey = NormalizeKey(SyntaxThemeKey, "adaptive"),
            CodeFontKey = NormalizeKey(CodeFontKey, "cascadia-code"),
            UiFontKey = NormalizeKey(UiFontKey, "aptos"),
            UiFontColorModeKey = NormalizeUiFontColorModeKey(UiFontColorModeKey),
            CustomUiFontColor = NormalizeColor(CustomUiFontColor, "#DDE6E8"),
            FoldArrowPositionKey = NormalizeFoldArrowPositionKey(FoldArrowPositionKey),
            ShowFoldArrows = ShowFoldArrows,
            ShowSummaryArrowBorders = ShowSummaryArrowBorders,
            UseParentChildArrowIndentation = UseParentChildArrowIndentation,
            ShowVerticalScopeLines = ShowVerticalScopeLines,
            SummaryFoldKinds = NormalizeSummaryFoldKinds(SummaryFoldKinds),
            UseColorfulFamilies = UseColorfulFamilies,
            ShowAppearanceCodePreview = ShowAppearanceCodePreview,
            ThemeAdaptFileCountColor = ThemeAdaptFileCountColor,
            ThemeAdaptLocColor = ThemeAdaptLocColor,
            ThemeAdaptVersionColor = ThemeAdaptVersionColor,
            ThemeAdaptBytesColor = ThemeAdaptBytesColor,
            UiFontSize = NormalizeFontSize(UiFontSize, 11.0),
            CodeEditorFontSize = NormalizeFontSize(CodeEditorFontSize, 12.0),
            PromptWindowFontSize = NormalizeFontSize(PromptWindowFontSize, 12.0),
            ChatWindowFontSize = NormalizeFontSize(ChatWindowFontSize, 10.0),
            ChatAppearanceKey = NormalizeChatAppearanceKey(ChatAppearanceKey),
            SelectedAiRoute = string.IsNullOrWhiteSpace(SelectedAiRoute) ? "Browser: ChatGPT" : SelectedAiRoute.Trim(),
            SelectedLocalModel = string.IsNullOrWhiteSpace(SelectedLocalModel) ? "qwen2.5-coder:3b" : SelectedLocalModel.Trim(),
            SelectedImageModel = string.IsNullOrWhiteSpace(SelectedImageModel) ? "segmind/tiny-sd" : SelectedImageModel.Trim(),
            FileRequestModel = NormalizeModelId(FileRequestModel, "qwen2.5-coder:1.5b"),
            PatchWriteModel = NormalizeModelId(PatchWriteModel, "qwen2.5-coder:3b"),
            PatchReviewModel = NormalizeModelId(PatchReviewModel, "phi4-mini"),
            ChatModel = NormalizeModelId(ChatModel, "qwen2.5-coder:3b"),
            CodexModel = string.IsNullOrWhiteSpace(CodexModel) ? null : NormalizeOptionalModelId(CodexModel),
            CodexReasoningEffort = string.IsNullOrWhiteSpace(CodexReasoningEffort) ? null : NormalizeCodexReasoningEffort(CodexReasoningEffort),
            OllamaModelsDirectory = NormalizeDirectoryPath(OllamaModelsDirectory),
            HuggingFaceToken = string.IsNullOrWhiteSpace(HuggingFaceToken) ? null : NormalizeToken(HuggingFaceToken),
            LocalLlmSortOption = NormalizeLocalLlmFilter(LocalLlmSortOption, "Newest"),
            LocalLlmProviderFilter = NormalizeLocalLlmFilter(LocalLlmProviderFilter, "All providers"),
            LocalLlmSourceFilter = NormalizeLocalLlmFilter(LocalLlmSourceFilter, "All"),
            LocalLlmPurposeFilter = NormalizeLocalLlmFilter(LocalLlmPurposeFilter, "All purposes"),
            LocalLlmBaseFilter = NormalizeLocalLlmFilter(LocalLlmBaseFilter, "All bases"),
            LocalLlmContextFilter = NormalizeLocalLlmFilter(LocalLlmContextFilter, "Any"),
            LocalLlmRequirementFilter = NormalizeLocalLlmFilter(LocalLlmRequirementFilter, "Any requirement"),
            PromptModeKey = NormalizePromptModeKey(PromptModeKey),
            IsAutopilotEnabled = IsAutopilotEnabled,
            PromptBarOpenByDefault = PromptBarOpenByDefault,
            AutoSwitchPromptModesToChat = AutoSwitchPromptModesToChat,
            WorkspaceModeKey = NormalizeWorkspaceModeKey(WorkspaceModeKey),
            ExternalBrowserKey = NormalizeExternalBrowserKey(ExternalBrowserKey),
            ShowSkippedFiles = ShowSkippedFiles,
            ShowProjectFilesPane = ShowProjectFilesPane,
            ProjectFilesTopLocMode = ProjectFilesTopLocMode,
            ShowBrowserRoutingPane = ShowBrowserRoutingPane,
            ShowProjectGraphTreePane = ShowProjectGraphTreePane,
            ProjectGraphGenerationColors = NormalizeGraphGenerationColors(ProjectGraphGenerationColors)
        };
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(data, JsonOptions) + Environment.NewLine, Utf8NoBom);
    }

    private static string ResolveContextControlRoot()
    {
        return FindContextControlRoot(Directory.GetCurrentDirectory())
            ?? FindContextControlRoot(AppContext.BaseDirectory)
            ?? AppContext.BaseDirectory;
    }

    private static string? FindContextControlRoot(string startPath)
    {
        if (string.IsNullOrWhiteSpace(startPath))
        {
            return null;
        }

        var directory = File.Exists(startPath)
            ? new DirectoryInfo(Path.GetDirectoryName(startPath) ?? "")
            : new DirectoryInfo(startPath);

        while (directory is not null)
        {
            if (LooksLikeContextControl(directory.FullName))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }

    private static bool LooksLikeContextControl(string path)
    {
        return Directory.Exists(path)
            && File.Exists(Path.Combine(path, "ccStart.ps1"))
            && File.Exists(Path.Combine(path, "ccDir.ps1"))
            && File.Exists(Path.Combine(path, "cc.ps1"))
            && File.Exists(Path.Combine(path, "ccReplace.ps1"));
    }

    private static string NormalizeKey(string? key, string fallback)
    {
        return string.IsNullOrWhiteSpace(key) ? fallback : key.Trim().ToLowerInvariant();
    }

    private static string NormalizeSkinKey(string? key)
    {
        return WorkbenchSkins.For(key).Key;
    }

    private static string NormalizeFoldArrowPositionKey(string? key)
    {
        return string.Equals(key?.Trim(), "locBlock", StringComparison.OrdinalIgnoreCase)
            ? "locBlock"
            : "codeEditor";
    }

    private static string NormalizeUiFontColorModeKey(string? key)
    {
        return string.Equals(key?.Trim(), "custom", StringComparison.OrdinalIgnoreCase)
            ? "custom"
            : "theme";
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

    private static string NormalizeColor(string? value, string fallback)
    {
        var clean = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        try
        {
            var color = Avalonia.Media.Color.Parse(clean);
            return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        }
        catch
        {
            return fallback;
        }
    }

    private static double NormalizeFontSize(double value, double fallback)
    {
        var next = double.IsFinite(value) && value > 0 ? value : fallback;
        return Math.Round(Math.Clamp(next, 8.0, 22.0), 1);
    }

    private static string NormalizeWorkspaceModeKey(string? key)
    {
        return key?.Trim().ToLowerInvariant() switch
        {
            "browser" => "browser",
            "graph" or "cube" => "graph",
            "llms" => "llms",
            "dependencies" => "dependencies",
            "chat" or "imagegen" or "image-gen" or "image" => "chat",
            "skillbook" => "skillbook",
            "scanner" => "scanner",
            _ => "code"
        };
    }

    private static string NormalizePromptModeKey(string? key)
    {
        return key?.Trim().ToLowerInvariant() switch
        {
            "codex" => "codex",
            "imagegen" or "image-gen" or "image" => "imagegen",
            "terminal" => "terminal",
            _ => "context"
        };
    }

    private static string NormalizeModelId(string? modelId, string fallback)
    {
        return string.IsNullOrWhiteSpace(modelId) ? fallback : modelId.Trim();
    }

    private static string NormalizeOptionalModelId(string? modelId)
    {
        return string.IsNullOrWhiteSpace(modelId) ? "" : modelId.Trim();
    }

    private static string NormalizeCodexReasoningEffort(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "low" => "low",
            "medium" => "medium",
            "high" => "high",
            "xhigh" => "xhigh",
            _ => ""
        };
    }

    private static string NormalizeToken(string? token)
    {
        return string.IsNullOrWhiteSpace(token) ? "" : token.Trim();
    }

    private static string NormalizeLocalLlmFilter(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private static string NormalizeExternalBrowserKey(string? key)
    {
        return string.IsNullOrWhiteSpace(key) ? "default" : key.Trim();
    }

    private static string NormalizeDirectoryPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var expanded = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
        try
        {
            return Path.GetFullPath(expanded);
        }
        catch
        {
            return expanded;
        }
    }

    private static string ResolveDefaultOllamaModelsDirectory()
    {
        var userEnvironment = NormalizeDirectoryPath(Environment.GetEnvironmentVariable(
            OllamaModelsEnvironmentVariable,
            EnvironmentVariableTarget.User));
        if (!string.IsNullOrWhiteSpace(userEnvironment))
        {
            return userEnvironment;
        }

        var processEnvironment = NormalizeDirectoryPath(Environment.GetEnvironmentVariable(OllamaModelsEnvironmentVariable));
        return string.IsNullOrWhiteSpace(processEnvironment)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ollama", "models")
            : processEnvironment;
    }

    private const string DefaultSummaryFoldKinds = "namespace,class,struct,interface,enum,method,property,object,block,array,arguments";
    public const string DefaultProjectGraphGenerationColors = "#7A858B,#808B76,#887F90,#918473,#728987,#8C787A,#82866E,#778094";
    private const string LegacyProjectGraphGenerationColors = "#4FA3FF,#22B8A7,#8B5CF6,#EAB308,#EF476F,#06D6A0,#F97316,#A3E635";

    private static string NormalizeSummaryFoldKinds(string? value)
    {
        if (value is null)
        {
            return DefaultSummaryFoldKinds;
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var allowed = DefaultSummaryFoldKinds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var selected = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(allowed.Contains)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return selected.Length == 0 ? string.Empty : string.Join(",", selected.Select(item => item.ToLowerInvariant()));
    }

    private static string NormalizeGraphGenerationColors(string? value)
    {
        var source = string.IsNullOrWhiteSpace(value)
            ? DefaultProjectGraphGenerationColors
            : value;
        var colors = source.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(color => NormalizeColor(color, ""))
            .Where(color => !string.IsNullOrWhiteSpace(color))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToArray();

        return colors.Length == 0
            ? DefaultProjectGraphGenerationColors
            : IsLegacyProjectGraphGenerationPalette(colors)
                ? DefaultProjectGraphGenerationColors
            : string.Join(",", colors);
    }

    private static bool IsLegacyProjectGraphGenerationPalette(IReadOnlyList<string> colors)
    {
        var legacy = LegacyProjectGraphGenerationColors.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return colors.Count == legacy.Length
            && colors.SequenceEqual(legacy, StringComparer.OrdinalIgnoreCase);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private sealed class WorkbenchSettingsJson
    {
        public string? ThemeKey { get; set; }
        public string? SkinKey { get; set; }
        public string? SyntaxThemeKey { get; set; }
        public string? CodeFontKey { get; set; }
        public string? UiFontKey { get; set; }
        public string? UiFontColorModeKey { get; set; }
        public string? CustomUiFontColor { get; set; }
        public string? FoldArrowPositionKey { get; set; }
        public bool? ShowFoldArrows { get; set; }
        public bool? ShowSummaryArrowBorders { get; set; }
        public bool? UseParentChildArrowIndentation { get; set; }
        public bool? ShowVerticalScopeLines { get; set; }
        public string? SummaryFoldKinds { get; set; }
        public bool? UseColorfulFamilies { get; set; }
        public bool? ShowAppearanceCodePreview { get; set; }
        public bool? ThemeAdaptFileCountColor { get; set; }
        public bool? ThemeAdaptLocColor { get; set; }
        public bool? ThemeAdaptVersionColor { get; set; }
        public bool? ThemeAdaptBytesColor { get; set; }
        public double? UiFontSize { get; set; }
        public double? CodeEditorFontSize { get; set; }
        public double? PromptWindowFontSize { get; set; }
        public double? ChatWindowFontSize { get; set; }
        public string? ChatAppearanceKey { get; set; }
        public string? SelectedAiRoute { get; set; }
        public string? SelectedLocalModel { get; set; }
        public string? SelectedImageModel { get; set; }
        public string? FileRequestModel { get; set; }
        public string? PatchWriteModel { get; set; }
        public string? PatchReviewModel { get; set; }
        public string? ChatModel { get; set; }
        public string? CodexModel { get; set; }
        public string? CodexReasoningEffort { get; set; }
        public string? OllamaModelsDirectory { get; set; }
        public string? HuggingFaceToken { get; set; }
        public string? LocalLlmSortOption { get; set; }
        public string? LocalLlmProviderFilter { get; set; }
        public string? LocalLlmSourceFilter { get; set; }
        public string? LocalLlmPurposeFilter { get; set; }
        public string? LocalLlmBaseFilter { get; set; }
        public string? LocalLlmContextFilter { get; set; }
        public string? LocalLlmRequirementFilter { get; set; }
        public string? PromptModeKey { get; set; }
        public bool? IsAutopilotEnabled { get; set; }
        public bool? PromptBarOpenByDefault { get; set; }
        public bool? AutoSwitchPromptModesToChat { get; set; }
        public string? WorkspaceModeKey { get; set; }
        public string? ExternalBrowserKey { get; set; }
        public bool? ShowSkippedFiles { get; set; }
        public bool? ShowProjectFilesPane { get; set; }
        public bool? ProjectFilesTopLocMode { get; set; }
        public bool? ShowBrowserRoutingPane { get; set; }
        public bool? ShowProjectGraphTreePane { get; set; }
        public string? ProjectGraphGenerationColors { get; set; }
    }
}
