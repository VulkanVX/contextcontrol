# Context Control IDE Architecture

## Stack Decision

The IDE shell uses Avalonia on .NET as a native desktop application.

Rejected for this project:

- Electron
- Tauri
- WebView shells
- browser-hosted HTML prototypes

Reasons:

- The current Context Control core is already cross-platform through `pwsh`.
- Avalonia ships one desktop UI codebase across Windows, Linux, and macOS.
- The app can call the existing PowerShell scripts as local child processes without changing their behavior.
- Native controls avoid the memory and process overhead of a browser runtime.
- The UI can stay data-driven and virtualized for large project trees and histories.

## Performance Rules

- Prefer native list/tree controls with virtualization.
- Keep the main work area empty until the core project shell is stable.
- Avoid blur, live transparency, shader-heavy decoration, and expensive animated layout.
- Use short, transform/opacity or single-panel width transitions only where they materially improve navigation.
- Load project trees and version histories asynchronously once real data wiring begins.
- Keep PowerShell execution off the UI thread.

## Browser Mode

Browser mode is an embedded native WebView surface.

- Windows hosts Edge WebView2 directly through a thin Avalonia `NativeControlHost` bridge.
- Other platforms keep the browser mode boundary but need their own native host implementation before the surface is enabled there.
- The embedded browser stores its WebView2 user data under the local ContextControl root in `.ccWorkbench.browser-data/`.
- The browser toolbar can open the current URL in detected external system browsers when the user wants existing browser sessions.
- The app does not store DOM selectors or automate browser UI.
- The editor, Context Control workflow, and browser surface stay separated through view models and window event bridges.

## Backend Contract

The existing scripts remain the source of truth:

- `ccDir.ps1` exports project structure.
- `cc.ps1` exports selected context.
- `ccReplace.ps1` applies patch blocks and writes version cache snapshots.
- `ccStart.ps1` remains the terminal agent-mode entry point.

The desktop app is a native orchestration layer over that core, not a replacement for it.

## Workbench Source Layout

The Avalonia workbench is organized by feature ownership instead of broad layer buckets.
Namespaces intentionally remain stable (`ContextControl.Workbench.Controls`,
`ContextControl.Workbench.Services`, `ContextControl.Workbench.ViewModels`, and
`ContextControl.Workbench.Views`) so XAML bindings and existing references stay low-risk
while the physical layout is easier to navigate.

- `Shell/` owns the application window, shell chrome, app-level view model, theme services, updates, and history/project-tab orchestration.
- `Features/ContextWorkflow/` owns DIR/CC/GO workflow orchestration, prompt/capsule building, file resolution, instruction auditing, attachments, patch plans, and prompt composer/browser-routing UI.
- `Features/LocalModels/` owns Local LLM catalogs, runtime/process integration, dependency environments, model view models, catalog rendering, settings, and local-model workspace pages.
- `Features/ProjectInspection/` owns project loading, file rules, stack scanning, project tree/graph controls, project workspace view models, scanner pages, graph pages, and navigation panes.
- `Features/Editing/` owns the custom code editor, text-surface rendering, minimap/navigation, syntax helpers, editor workspace page, and external-change tracking.
- `Features/Conversation/` owns chat transcript rendering, chat view models, conversation page, and chat-history integration.
- `Features/Browser/` owns the WebView2 host, browser service, browser pane/tab view models, and browser workspace page.
- `Features/Skillbook/` owns Skillbook rendering, service, view models, workspace page, rename dialog, and Context Control Skillbook workflow partials.
- `Features/Settings/` owns the settings window, settings page parts, dialog windows, and small settings view models.
- `Shared/` owns generic controls and infrastructure view-model primitives that are not feature-specific.
- `Styles/` and `Assets/` remain centralized because Avalonia resource URIs are path-sensitive and shared across features.

New files should usually land inside the owning feature. Add to `Shared/` only when at
least two features already need the same primitive.

## LLM Navigation

- Use `LLM_PROJECT_GUIDE.md` for a quick map of source folders and edit boundaries.
- Use `UI_ELEMENT_NAMES.md` for canonical UI element names when prompting an LLM about a visual change.
