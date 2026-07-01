// CC-DESC: Appearance, typography, folding, and workspace-mode bindable properties.

// CC-DESC: Coordinates project tabs, tree selection, history, and external-change queues.

using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Windows.Input;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Media;
using ContextControl.Workbench.Services;

namespace ContextControl.Workbench.ViewModels;

public sealed partial class WorkbenchViewModel
{
    public ThemeOptionViewModel SelectedTheme
    {
        get => _selectedTheme;
        set
        {
            if (value is null)
            {
                return;
            }

            if (SetProperty(ref _selectedTheme, value))
            {
                OnPropertyChanged(nameof(ThemeKey));
                OnPropertyChanged(nameof(SyntaxThemeKey));
                SaveAppearanceSettings();
            }
        }
    }

    public string ThemeKey => ActiveSkin.IsActive ? ActiveSkin.ThemeKey : SelectedTheme.Key;

    public ThemeOptionViewModel SelectedSyntaxTheme
    {
        get => _selectedSyntaxTheme;
        set
        {
            if (value is null)
            {
                return;
            }

            if (SetProperty(ref _selectedSyntaxTheme, value))
            {
                OnPropertyChanged(nameof(SyntaxThemeKey));
                SaveAppearanceSettings();
            }
        }
    }

    public string SyntaxThemeKey => ActiveSkin.IsActive ? ActiveSkin.SyntaxThemeKey : SelectedSyntaxTheme.Key;

    public ThemeOptionViewModel SelectedCodeFont
    {
        get => _selectedCodeFont;
        set
        {
            if (value is null)
            {
                return;
            }

            if (SetProperty(ref _selectedCodeFont, value))
            {
                OnPropertyChanged(nameof(CodeFontKey));
                OnPropertyChanged(nameof(CodeFontFamily));
                SaveAppearanceSettings();
            }
        }
    }

    public string CodeFontKey => SelectedCodeFont.Key;
    public string CodeFontFamily => ActiveSkin.IsActive ? ActiveSkin.CodeFontFamily : SelectedCodeFont.FontFamily;

    public ThemeOptionViewModel SelectedUiFont
    {
        get => _selectedUiFont;
        set
        {
            if (value is null)
            {
                return;
            }

            if (SetProperty(ref _selectedUiFont, value))
            {
                OnPropertyChanged(nameof(UiFontKey));
                OnPropertyChanged(nameof(UiFontFamily));
                SaveAppearanceSettings();
            }
        }
    }

    public string UiFontKey => SelectedUiFont.Key;
    public string UiFontFamily => ActiveSkin.IsActive ? ActiveSkin.UiFontFamily : SelectedUiFont.FontFamily;

    public ThemeOptionViewModel SelectedUiFontColorMode
    {
        get => _selectedUiFontColorMode;
        set
        {
            if (value is null)
            {
                return;
            }

            if (SetProperty(ref _selectedUiFontColorMode, value))
            {
                OnPropertyChanged(nameof(UiFontColorModeKey));
                OnPropertyChanged(nameof(UseCustomUiFontColor));
                SaveAppearanceSettings();
            }
        }
    }

    public string UiFontColorModeKey => SelectedUiFontColorMode.Key;
    public bool UseCustomUiFontColor => string.Equals(UiFontColorModeKey, "custom", StringComparison.OrdinalIgnoreCase);

    public ThemeOptionViewModel SelectedChatAppearance
    {
        get => _selectedChatAppearance;
        set
        {
            if (value is null)
            {
                return;
            }

            if (SetProperty(ref _selectedChatAppearance, value))
            {
                OnPropertyChanged(nameof(ChatAppearanceKey));
                SaveAppearanceSettings();
            }
        }
    }

    public string ChatAppearanceKey => SelectedChatAppearance.Key;

    public Color CustomUiFontColor
    {
        get => _customUiFontColor;
        set
        {
            if (SetProperty(ref _customUiFontColor, value))
            {
                OnPropertyChanged(nameof(CustomUiFontColorHex));
                OnPropertyChanged(nameof(CustomUiFontColorText));
                SaveAppearanceSettings();
            }
        }
    }

    public string CustomUiFontColorHex => FormatColor(CustomUiFontColor);

    public string CustomUiFontColorText
    {
        get => CustomUiFontColorHex;
        set
        {
            if (TryParseColor(value, out var color))
            {
                CustomUiFontColor = color;
            }
        }
    }

    public ThemeOptionViewModel SelectedSkin
    {
        get => _selectedSkin;
        set
        {
            if (value is null)
            {
                return;
            }

            if (SetProperty(ref _selectedSkin, value))
            {
                NotifySkinAppearanceChanged();
                SaveAppearanceSettings();
            }
        }
    }

    public string SkinKey => SelectedSkin.Key;
    public bool IsSkinActive => ActiveSkin.IsActive;
    public bool AppearanceOptionsEnabled => !IsSkinActive;
    private WorkbenchSkinDefinition ActiveSkin => WorkbenchSkins.For(SelectedSkin.Key);

    private void NotifySkinAppearanceChanged()
    {
        OnPropertyChanged(nameof(SkinKey));
        OnPropertyChanged(nameof(IsSkinActive));
        OnPropertyChanged(nameof(AppearanceOptionsEnabled));
        OnPropertyChanged(nameof(ThemeKey));
        OnPropertyChanged(nameof(SyntaxThemeKey));
        OnPropertyChanged(nameof(CodeFontFamily));
        OnPropertyChanged(nameof(UiFontFamily));
    }

    public ThemeOptionViewModel SelectedFoldArrowPosition
    {
        get => _selectedFoldArrowPosition;
        set
        {
            if (value is null)
            {
                return;
            }

            if (SetProperty(ref _selectedFoldArrowPosition, value))
            {
                OnPropertyChanged(nameof(FoldArrowsInCodeEditor));
                OnPropertyChanged(nameof(CanUseParentChildArrowIndentation));
                OnPropertyChanged(nameof(EffectiveUseParentChildArrowIndentation));
                SaveAppearanceSettings();
            }
        }
    }

    public bool FoldArrowsInCodeEditor =>
        string.Equals(SelectedFoldArrowPosition.Key, "codeEditor", StringComparison.OrdinalIgnoreCase);

    public bool CanUseParentChildArrowIndentation => true;

    public bool EffectiveUseParentChildArrowIndentation =>
        UseParentChildArrowIndentation;

    public bool ShowFoldArrows
    {
        get => _showFoldArrows;
        set
        {
            if (SetProperty(ref _showFoldArrows, value))
            {
                OnPropertyChanged(nameof(CanUseParentChildArrowIndentation));
                OnPropertyChanged(nameof(EffectiveUseParentChildArrowIndentation));
                SaveAppearanceSettings();
            }
        }
    }

    public bool ShowSummaryArrowBorders
    {
        get => _showSummaryArrowBorders;
        set
        {
            if (SetProperty(ref _showSummaryArrowBorders, value))
            {
                SaveAppearanceSettings();
            }
        }
    }

    public bool UseParentChildArrowIndentation
    {
        get => _useParentChildArrowIndentation;
        set
        {
            if (SetProperty(ref _useParentChildArrowIndentation, value))
            {
                OnPropertyChanged(nameof(EffectiveUseParentChildArrowIndentation));
                SaveAppearanceSettings();
            }
        }
    }

    public bool ShowVerticalScopeLines
    {
        get => _showVerticalScopeLines;
        set
        {
            if (SetProperty(ref _showVerticalScopeLines, value))
            {
                SaveAppearanceSettings();
            }
        }
    }

    public string SummaryFoldKinds => BuildSummaryFoldKinds();

    public bool SummarizeNamespace
    {
        get => _summarizeNamespace;
        set => SetSummaryKind(ref _summarizeNamespace, value, nameof(SummarizeNamespace));
    }

    public bool SummarizeClass
    {
        get => _summarizeClass;
        set => SetSummaryKind(ref _summarizeClass, value, nameof(SummarizeClass));
    }

    public bool SummarizeStruct
    {
        get => _summarizeStruct;
        set => SetSummaryKind(ref _summarizeStruct, value, nameof(SummarizeStruct));
    }

    public bool SummarizeInterface
    {
        get => _summarizeInterface;
        set => SetSummaryKind(ref _summarizeInterface, value, nameof(SummarizeInterface));
    }

    public bool SummarizeEnum
    {
        get => _summarizeEnum;
        set => SetSummaryKind(ref _summarizeEnum, value, nameof(SummarizeEnum));
    }

    public bool SummarizeMethod
    {
        get => _summarizeMethod;
        set => SetSummaryKind(ref _summarizeMethod, value, nameof(SummarizeMethod));
    }

    public bool SummarizeProperty
    {
        get => _summarizeProperty;
        set => SetSummaryKind(ref _summarizeProperty, value, nameof(SummarizeProperty));
    }

    public bool SummarizeObject
    {
        get => _summarizeObject;
        set => SetSummaryKind(ref _summarizeObject, value, nameof(SummarizeObject));
    }

    public bool SummarizeBlock
    {
        get => _summarizeBlock;
        set => SetSummaryKind(ref _summarizeBlock, value, nameof(SummarizeBlock));
    }

    public bool SummarizeArray
    {
        get => _summarizeArray;
        set => SetSummaryKind(ref _summarizeArray, value, nameof(SummarizeArray));
    }

    public bool SummarizeArguments
    {
        get => _summarizeArguments;
        set => SetSummaryKind(ref _summarizeArguments, value, nameof(SummarizeArguments));
    }

    public bool UseColorfulFamilies
    {
        get => _useColorfulFamilies;
        set
        {
            if (SetProperty(ref _useColorfulFamilies, value))
            {
                if (!value && _showSummaryArrowBorders)
                {
                    _showSummaryArrowBorders = false;
                    OnPropertyChanged(nameof(ShowSummaryArrowBorders));
                }

                SaveAppearanceSettings();
            }
        }
    }

    public bool ShowAppearanceCodePreview
    {
        get => _showAppearanceCodePreview;
        set
        {
            if (SetProperty(ref _showAppearanceCodePreview, value))
            {
                SaveAppearanceSettings();
            }
        }
    }

    public bool ThemeAdaptFileCountColor
    {
        get => _themeAdaptFileCountColor;
        set
        {
            if (SetProperty(ref _themeAdaptFileCountColor, value))
            {
                SaveAppearanceSettings();
            }
        }
    }

    public bool ThemeAdaptLocColor
    {
        get => _themeAdaptLocColor;
        set
        {
            if (SetProperty(ref _themeAdaptLocColor, value))
            {
                SaveAppearanceSettings();
            }
        }
    }

    public bool ThemeAdaptVersionColor
    {
        get => _themeAdaptVersionColor;
        set
        {
            if (SetProperty(ref _themeAdaptVersionColor, value))
            {
                SaveAppearanceSettings();
            }
        }
    }

    public bool ThemeAdaptBytesColor
    {
        get => _themeAdaptBytesColor;
        set
        {
            if (SetProperty(ref _themeAdaptBytesColor, value))
            {
                SaveAppearanceSettings();
            }
        }
    }

    public double UiFontSize
    {
        get => _uiFontSize;
        set
        {
            var next = NormalizeAppearanceFontSize(value, 11.0);
            if (SetProperty(ref _uiFontSize, next))
            {
                SaveAppearanceSettings();
            }
        }
    }

    public double CodeEditorFontSize
    {
        get => _codeEditorFontSize;
        set
        {
            var next = NormalizeAppearanceFontSize(value, 12.0);
            if (SetProperty(ref _codeEditorFontSize, next))
            {
                OnPropertyChanged(nameof(CodeEditorLineHeight));
                SaveAppearanceSettings();
            }
        }
    }

    public double CodeEditorLineHeight => Math.Max(12.0, CodeEditorFontSize + 4.0);

    public double PromptWindowFontSize
    {
        get => _promptWindowFontSize;
        set
        {
            var next = NormalizeAppearanceFontSize(value, 12.0);
            if (SetProperty(ref _promptWindowFontSize, next))
            {
                OnPropertyChanged(nameof(PromptWindowLineHeight));
                SaveAppearanceSettings();
            }
        }
    }

    public double PromptWindowLineHeight => Math.Max(12.0, PromptWindowFontSize * 1.5);

    public double ChatWindowFontSize
    {
        get => _chatWindowFontSize;
        set
        {
            var next = NormalizeAppearanceFontSize(value, 10.0);
            if (SetProperty(ref _chatWindowFontSize, next))
            {
                SaveAppearanceSettings();
            }
        }
    }

    public bool AutoSwitchPromptModesToChat
    {
        get => _autoSwitchPromptModesToChat;
        set
        {
            if (SetProperty(ref _autoSwitchPromptModesToChat, value))
            {
                SaveAppearanceSettings();
            }
        }
    }

    public WorkbenchModeOptionViewModel SelectedWorkspaceMode
    {
        get => _selectedWorkspaceMode;
        private set
        {
            if (value is null || ReferenceEquals(_selectedWorkspaceMode, value))
            {
                return;
            }

            _selectedWorkspaceMode = value;
            RefreshWorkspaceModeState();
            OnPropertyChanged(nameof(SelectedWorkspaceMode));
            OnPropertyChanged(nameof(IsCodeEditorMode));
            OnPropertyChanged(nameof(IsProjectGraphMode));
            OnPropertyChanged(nameof(IsBrowserMode));
            OnPropertyChanged(nameof(IsLlmsMode));
            OnPropertyChanged(nameof(IsDependenciesMode));
            OnPropertyChanged(nameof(IsChatMode));
            OnPropertyChanged(nameof(IsConversationMode));
            OnPropertyChanged(nameof(IsSkillbookMode));
            OnPropertyChanged(nameof(IsProjectScannerMode));
            SaveAppearanceSettings();
        }
    }

    public bool IsCodeEditorMode =>
        string.Equals(SelectedWorkspaceMode.Key, "code", StringComparison.OrdinalIgnoreCase);

    public bool IsProjectGraphMode =>
        string.Equals(SelectedWorkspaceMode.Key, "graph", StringComparison.OrdinalIgnoreCase);

    public bool IsBrowserMode =>
        string.Equals(SelectedWorkspaceMode.Key, "browser", StringComparison.OrdinalIgnoreCase);

    public bool IsLlmsMode =>
        string.Equals(SelectedWorkspaceMode.Key, "llms", StringComparison.OrdinalIgnoreCase);

    public bool IsDependenciesMode =>
        string.Equals(SelectedWorkspaceMode.Key, "dependencies", StringComparison.OrdinalIgnoreCase);

    public bool IsChatMode =>
        string.Equals(SelectedWorkspaceMode.Key, "chat", StringComparison.OrdinalIgnoreCase);

    public bool IsConversationMode => IsChatMode;

    public bool IsSkillbookMode =>
        string.Equals(SelectedWorkspaceMode.Key, "skillbook", StringComparison.OrdinalIgnoreCase);

    public bool IsProjectScannerMode =>
        string.Equals(SelectedWorkspaceMode.Key, "scanner", StringComparison.OrdinalIgnoreCase);

}
