# UI Element Names

Use these canonical names when prompting an LLM about the ContextControl Workbench UI. The names are user-facing aliases mapped to code locations, so prompts can be precise without needing to quote large XAML blocks.

## Main Window Shell

| Canonical name | Code anchor | Meaning |
|---|---|---|
| Main window | `Views/MainWindow.axaml` | Top-level desktop window. |
| Main title bar | `Views/MainWindowParts/Shell/MainTitleBar.axaml` | Custom drag bar with EDIT, VIEW, and window controls. |
| Edit menu | `EditMenuButton` in `MainTitleBar.axaml` | Project actions flyout with Open Project, New Project, Settings. |
| View menu | `ViewMenuButton` in `MainTitleBar.axaml` | Visibility flyout for Project files tree, Browser routing window, Prompt window. |
| Project tabs bar | `Views/MainWindowParts/Shell/ProjectTabsBar.axaml` | Top row of open project tiles and pane toggles. |
| Workspace root | `WorkspaceRoot` in `MainWindow.axaml` | Main three-column work area. |
| Workspace mode bar | `WorkspaceHeaderBar.axaml` | Mode switcher row for Code, Chat, Browser, Local LLMs, Dependencies, Skillbook, Graph, Scanner. |
| Workspace header | `WorkspaceHeaderBar.axaml` | Second header row whose controls change by active workspace mode. |
| Main overlays | `MainWindowOverlays` in `MainWindowOverlays.axaml` | File drop overlay, modal overlay, and resize hit targets. |

## Navigation And Side Panes

| Canonical name | Code anchor | Meaning |
|---|---|---|
| Project files pane | `ProjectFilesPane` in `ProjectFilesPane.axaml` | Left sidebar containing project tree controls. |
| Project file tree | `ProjectTreeView` in `ProjectFilesPane.axaml` | Rendered tree of folders/files. |
| Project tree search | `ProjectTreeSearchPanel`, `ProjectTreeSearchBox` | Search box overlay for the project file tree. |
| History pane | `HistoryPaneRoot` in `HistoryPane.axaml` | Narrow version/history gutter beside the editor area. |
| Browser routing pane | `BrowserRoutingPane.axaml` | Right sidebar for Context Control route, patch plan, log, and attachments. |
| Routing log | log `ItemsControl` in `BrowserRoutingPane.axaml` | Context Control log entries shown in the routing pane. |
| Patch plan summary | `PatchPlanActions` binding in `BrowserRoutingPane.axaml` | Apply effective/all patch controls and patch action list. |
| Attachment region | `AttachmentRegion`, `AttachmentListHost`, `AttachmentList` | Pending prompt attachment list in the routing pane. |

## Prompt Window

| Canonical name | Code anchor | Meaning |
|---|---|---|
| Prompt window | `ContextPromptBar` in `ContextPromptBar.axaml` | Bottom docked prompt surface. |
| Prompt bar root | `PromptBarRoot` | Animated prompt container. |
| Prompt composer | `ContextPromptComposer` | Inner composer panel with mode controls, input, footer, send button. |
| Prompt input | `ContextPromptTextBox` | Main multi-line text input for chat/context/image prompts. |
| Terminal output | `cc-terminal-output` text box in `ContextPromptBar.axaml` | Read-only terminal output shown in terminal prompt mode. |
| Prompt mode switcher | `IsPromptModeSwitcherVisible` controls | Context/Terminal prompt mode buttons. |
| CC timeline panel | `cc-timeline-panel` | DIR/CC/GO workflow stage strip. |
| Prompt action row | DIR, CC, GO buttons in `ContextPromptBar.axaml` | Runs directory export, code export, and patch preview. |
| Prompt model picker | `cc-local-model-picker` in `ContextPromptBar.axaml` | Active installed local model picker for prompt mode. |
| Prompt send button | `cc-prompt-send` button | Sends the prompt through the current prompt mode. |
| Transfer progress panel | `cc-transfer-progress` blocks | Model/install/download progress UI above the composer. |
| File drop overlay | `PromptFileDropOverlay` | Full-window overlay shown while files are dragged over the prompt. |

## Workspace Pages

| Canonical name | Code anchor | Meaning |
|---|---|---|
| Code editor page | `CodeEditorPage.axaml` | Workspace page for the active source file. |
| File editor | `FileEditor` in `CodeEditorPage.axaml` | Custom code editor control. |
| Conversation page | `ConversationPage.axaml` | Chat transcript workspace page. |
| Chat history hover rail | `ChatHistoryHoverShell` | Narrow hover target that opens chat history. |
| Chat history panel | `ChatHistoryPanel` | Session list with Export and New buttons. |
| Chat transcript | `ChatTranscriptRenderControl` in `ConversationPage.axaml` | Rendered chat messages and code snippets. |
| Browser page | `BrowserSurface.axaml` | Embedded browser workspace page. |
| Browser web view | `BrowserWebView` | Native WebView2 host. |
| Browser address bar | `browser-url-input` in `WorkspaceHeaderBar.axaml` | URL input shown in browser mode. |
| Browser tabs | `browser-tab` template in `WorkspaceHeaderBar.axaml` | Browser tab strip shown in browser mode. |
| Local LLM page | `LocalLlmPage.axaml` | Model catalog workspace page. |
| Local LLM filters | `llm-filter-bar` in `LocalLlmPage.axaml` | Sort/provider/run/purpose/base/context/hardware filters. |
| Local LLM search | `LlmSearchBox` | Overlay search box for model catalog. |
| Local LLM catalog | `LocalLlmCatalogRenderControl` | Rendered local model cards. |
| Dependencies page | `DependenciesPage.axaml` | Backend dependency workspace page. |
| Dependency search | `DependencySearchBox` | Overlay search box for dependencies. |
| Dependency list | `DependencyListRenderControl` | Rendered dependency install/status cards. |
| Skillbook page | `SkillbookPage.axaml` | Flow library workspace page for CC Flow, project/global flows, sections, skills, and markdown editing. |
| Skillbook flow rail | `SkillbookPage.axaml` | Flow selector for Context Control and editable project/global/custom flows. |
| Skillbook section/skill list | `SkillbookPage.axaml` | Grouped section and skill selector for large Skillbook collections. |
| Skillbook editor | `SkillbookPage.axaml` | Native selectable markdown editor/read-only detail pane. |
| CC Flow inspector | `SkillbookPage.axaml` | Prompt-call map showing trigger, source, recipient, attachments, and Skillbook injection state. |
| Project graph page | `ProjectGraphPage.axaml` | Architecture graph workspace page. |
| Project graph view | `ProjectGraphView` | Custom rendered architecture graph canvas. |
| Project graph search | `ProjectGraphSearchPanel`, `ProjectGraphSearchBox` | Graph node search overlay. |
| Project graph tree pane | `graph-tree-sidebar` | Optional current project tree text pane on graph page. |
| Project scanner page | `ProjectScannerPage.axaml` | Project scanner diagnostics workspace page. |
| Project scanner report | `ProjectScannerRenderControl` | Rendered scanner metrics, identity, files, rules, diagnostics. |

## Settings Window

| Canonical name | Code anchor | Meaning |
|---|---|---|
| Settings window | `Views/ThemeSettingsWindow.axaml` | Preferences window. |
| Settings navigation rail | `AppearanceNavButton`, `FileRulesNavButton`, `LlmsNavButton` | Left navigation buttons. |
| Appearance settings page | `AppearancePage` / `AppearanceSettingsPage.axaml` | Skin, theme, syntax, font, and editor style settings. |
| Skin picker | `SkinPicker` | Workbench skin selector. |
| Theme picker | `ThemePicker` | Color theme selector. |
| Syntax theme picker | `SyntaxThemePicker` | Code syntax color selector. |
| Code font picker | `CodeFontPicker` | Code/editor font selector. |
| UI font picker | `UiFontPicker` | App UI font selector. |
| Summary arrow options | `SummaryArrowOptionsToggleButton`, `SummaryArrowOptionsPanel` | Editor summary/fold arrow controls. |
| Appearance code preview | `AppearanceCodePreview` | Live code style preview. |
| LLM settings page | `LlmsPage` / `LlmsSettingsPage.axaml` | Local LLM role defaults and Ollama storage settings. |
| Ollama model storage | `OllamaModelsDirectory` binding | Directory textbox plus Browse/Apply buttons. |
| Local LLM role pickers | File request, Patch write, Patch review, General chat combo boxes | Defaults used by the CC-native chat capsule builder. |
| Project rules settings page | `FileRulesPage` / `FileRulesSettingsPage.axaml` | Project root, output root, version cache, and file rule lists. |
| Project root settings | ProjectRoot, OutputRoot, Version cache text boxes | Context Control target/output configuration. |
| Skipped folders list | `IgnoredDirectoryRules` | File rule list for ignored directories. |
| Skipped files list | `IgnoredFileNameRules` | File rule list for ignored file names. |
| Skipped file types list | `IgnoredFileTypeRules` | File rule list for ignored extensions. |
| Allowed file types list | `SupportedFileTypeRules` | File rule list for source/context extensions. |
| LOC file types list | `LocFileTypeRules` | File rule list for line-counted extensions. |

## Dialogs And Popups

| Canonical name | Code anchor | Meaning |
|---|---|---|
| File rule list editor | `Views/Settings/FileRuleListEditorWindow.cs` | Modal editor for full file-rule lists. |
| Project graph export dialog | `ProjectGraphExportOptionsWindow.cs` | Format/resolution dialog for graph export. |
| Project graph color dialog | `GraphGenerationColorWindow.cs` | Color picker for graph generation swatches. |
| Ollama setup modal | modal block in `MainWindowOverlays.axaml` | In-app Ollama setup message with OK/close buttons. |

## Custom Render Controls

| Canonical name | Code anchor | Meaning |
|---|---|---|
| Code editor control | `Controls/CodeEditor/**` | Syntax highlighting, folding, minimap, find, selection, rendering. |
| Project tree render control | `Controls/ProjectTree/**` | Virtual-looking project tree rendering and hit testing. |
| Project graph render control | `Controls/ProjectGraph/**` | Graph layout, drawing, hit testing, viewport, export. |
| Chat transcript render control | `Controls/Chat/ChatTranscriptRenderControl.cs` | Chat message and snippet rendering. |
| Local LLM catalog render control | `Controls/LocalLlmCatalog/**` | Model card rendering, badges, tooltips, pull action hit areas. |
| Dependency list render control | `Controls/Workspace/DependencyListRenderControl.cs` | Backend dependency cards. |
| Project scanner render control | `Controls/Workspace/ProjectScannerRenderControl.cs` | Scanner report sections. |
| Skillbook render control | `Controls/Workspace/SkillbookRenderControl.cs` | Legacy rendered Skillbook surface retained for compatibility. |
| Prompt dock host | `Controls/Common/PromptDockHost.cs` | Hosts workspace content and bottom prompt dock sizing. |
| Hover scrollbar behavior | `Controls/Common/HoverScrollbarBehavior.cs` | Shared hover/spacing behavior for scrollbars. |
