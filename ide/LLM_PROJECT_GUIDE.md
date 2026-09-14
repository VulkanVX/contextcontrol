# ContextControl Workbench LLM Project Guide

Use this file as the first stop when asking an LLM to modify the desktop IDE. It names the main source areas, the usual edit targets, and the boundaries that should stay stable.

## Source Map

| Area | Main files | Purpose |
|---|---|---|
| App startup | `App.axaml`, `App.axaml.cs`, `Program.cs` | Avalonia app bootstrapping and desktop lifetime. |
| Main shell | `Shell/MainWindow/**`, `Shell/Views/**`, `Shell/ViewModels/**`, `Shell/Services/**` | Top-level workbench layout, shell chrome, app-level state, project tabs, updates, themes, and history. |
| Context workflow | `Features/ContextWorkflow/**` | DIR/CC/GO orchestration, context request resolution, prompt/capsule building, instruction auditing, attachments, patch plans, prompt composer, and browser-routing workflow UI. |
| Local models | `Features/LocalModels/**` | Model catalogs, Ollama/runtime integration, dependency environments, model view models, catalog rendering, settings, and local-model workspace pages. |
| Project inspection | `Features/ProjectInspection/**` | Project loading, file rules, stack detection, scanner output, tree rows, project graph/tree controls, scanner pages, graph pages, and project navigation. |
| Editing | `Features/Editing/**` | Custom code editor, text-surface rendering, minimap/navigation, syntax helpers, editor page, and external-change tracking. |
| Conversation | `Features/Conversation/**` | Chat transcript rendering, chat state, conversation page, snippets, request progress, and chat-history integration. |
| Browser | `Features/Browser/**` | WebView2 host, browser tabs, browser pane view models, external browser routing, and browser workspace page. |
| Skillbook | `Features/Skillbook/**` | Skillbook rendering, service, flow view models, workspace page, rename dialog, and Context Control Skillbook workflow partials. |
| Settings shell | `Features/Settings/**` | Appearance, prompt-window, project-rule, local-model, and dialog settings UI plus small settings view models. |
| Shared primitives | `Shared/**` | Generic controls and infrastructure view-model primitives used across features. |
| Styling | `Styles/WorkbenchDesign.axaml`, `Styles/WorkbenchDesign/**` | Theme resources and control styling split by feature area. |
| Tests | `ContextControl.Workbench.Tests/Program.cs` | Focused smoke checks for resolver behavior, markdown parsing, and model catalog state. |

## Edit Rules

- Keep UI layout in XAML/user controls and behavior in the matching `.axaml.cs` bridge or view model partial.
- Prefer extending the existing partial class files inside the owning feature instead of creating another large monolithic file.
- Keep namespaces stable unless a dedicated namespace migration is part of the task. The current feature folders are a physical architecture refactor; many public types intentionally still use the existing `ContextControl.Workbench.*` namespaces.
- Do not put long-running work on the UI thread. Project scans, PowerShell calls, model refreshes, and network/process checks belong in services.
- Treat `lib/**` PowerShell scripts as the CLI/core pipeline. The desktop app orchestrates them; it should not silently fork their behavior.
- Treat `.tmp/`, `bin/`, `obj/`, `.ccReplace.versions/`, `.ccWorkbench.browser-data/`, chat exports, code exports, and `patch.txt` as runtime output.
- Use [UI_ELEMENT_NAMES.md](UI_ELEMENT_NAMES.md) for exact user-facing element names in prompts.

## Fast Prompt Examples

- "Change the prompt window input behavior in `ContextPromptBar`, especially `ContextPromptTextBox`."
- "Modify the project file tree rendering in `ProjectTreeRenderControl`, not the graph view."
- "Add a project rules setting in `FileRulesSettingsPage` and persist it through `WorkbenchViewModel.ProjectRules`."
- "Adjust Local LLM catalog card layout in `LocalLlmCatalogRenderControl`, leaving dependency cards alone."
