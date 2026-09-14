# Interface and Chat Monitor

## Typography

**Settings → Appearance → Interface scale** controls one shared layout scale for standard controls and custom-rendered surfaces. It preserves the proportions of authored text, icons and spacing. The existing stored `UiFontSize` value is retained: 11 means 100%, 16.5 means 150%, and 22 means 200%.

Code Editor, Prompt Window and Chat/ImageGen text sizes set their sizes within that shared scale. Changing chat size also changes the message header, its metadata and timestamp. The timestamp remains smaller than the body. Settings provide scrolling when the enlarged interface exceeds the window.

Scale changes settle after a short pause in slider movement. They do not recreate the theme palette. Appearance settings are saved after changes settle and flushed on exit. Studio adds coordinated dark surfaces and accents across the editor, tree, graph, chat and settings. Existing themes, skins and appearance preferences remain selectable.

## Floating Chat Monitor

The monitor stays above other windows while ContextControl is running. Turn it on or off in **Settings → Prompt Window**; its header × hides it. Drag the header to reposition it. Its saved position is brought back onto an available screen when necessary.

Each monitored chat occupies one row, with animated dots and a moving highlight during a request. Available statistics come from the existing provider progress stream; providers that do not report token counts or speed show their current status instead. Hover to read the full title, project and statistics.

- **Open:** switch the workbench to that row's project and conversation, restore a minimized main window, and bring it forward.
- **Reply:** open that conversation and expand a compact composer without activating the main window. It shares the selected chat's draft, model/mode and attachments. Configure models and authentication in the workbench.
- **Remove:** stop displaying that row without deleting its chat history. A new user message in that conversation adds it again.

Right-click a conversation in the chat history sidebar for **Add to Chat Monitor** or **Remove from Chat Monitor**. Explicitly starting a new chat also adds it. Browsing old chats alone does not re-add removed rows.

Drop local files anywhere in the expanded reply panel, or use its file picker. Attachment chips remove individual files. Sending uses the same pipeline as the workbench's Send button. Ctrl+Enter sends; Escape collapses the reply. Drafts are kept when collapsing or changing conversations. If another conversation becomes selected, the reply closes to prevent sending or attaching files to the wrong chat.

Membership is saved in `.ccWorkbench.chat-monitor.json`; visibility and screen position are in `.ccWorkbench.settings.json`. These local state files are excluded from Git. Restart restores membership, not active requests. The monitor is part of the running workbench and closes when the workbench exits.

## Validation

Local chats also expose **Google auto** in the composer and Chat Monitor quick reply. The model's planning, search and page-reading stages use the same progress and cancellation controls. See [Google research](GOOGLE_RESEARCH.md).

Run the regular smoke checks and the isolated UI regression harness:

```powershell
dotnet run --project ide/ContextControl.Workbench.Tests --configuration Release
dotnet run --project ide/ContextControl.Workbench.Tests --configuration Release -- --ui-experience-regression .tmp/ui-review
dotnet run --project ide/ContextControl.Workbench.Tests --configuration Release -- --chat-renderer-perf
```

The UI harness uses Avalonia Headless with Skia to render the real views. It checks typography geometry and deferred scaling, monitor persistence and navigation, concurrent request completion, draft and attachment separation, visibility controls, off-screen position clamping and animation lifetime. It uses isolated local fixtures with provider refresh disabled and sends no model requests. It is not a substitute for native Windows drag-and-drop, DPI, or live provider interaction checks.
