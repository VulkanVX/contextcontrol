<img width="1280" height="640" alt="ccbanner" src="https://github.com/user-attachments/assets/25b89f87-e4a7-4470-a9aa-1e8d4580c518" />

# ContextControl

A deterministic source-context pipeline for working with large codebases through any chat assistant.

ContextControl reduces context bloat and drives the LLM compute toward reasoning about your problem instead of wasting it on repository exploration, repeated rule text, or tool mechanics. It works with **any LLM**: ChatGPT, Claude, Codex, GitHub Copilot, Gemini, DeepSeek, local models, or any other assistant that supports instructions.

The LLM model **never touches your disk**. You export context locally, the assistant writes a mechanical patch recipe, and ContextControl applies that recipe locally on your machine.

---

## Status

ContextControl is designed to be **Windows / macOS / Linux** compatible.

The core is PowerShell, and the platform launchers make the pipeline behave the same way across supported desktop systems:

| Platform | Launcher | Core used |
|---|---|---|
| Windows | `.cmd` files | `cc*.ps1` |
| macOS | extensionless shell files | `cc*.ps1` through `pwsh` |
| Linux | extensionless shell files | `cc*.ps1` through `pwsh` |

macOS/Linux require **PowerShell 7+** so the `pwsh` command exists. After that, the ContextControl pipeline has the same functional flow as Windows: `DIR`, `CC`, agent-mode patch watching, generated exports, settings, and version cache.

---

## Why ContextControl

If you have worked on a codebase larger than a few thousand lines through a chat LLM, you know the pattern: the assistant burns half its tokens re-exploring the repo, asking for the same files over and over, and dumping rule preambles instead of solving your actual problem.

Moreover, it became an extreme pain due to recent price increases and rate-limits.

ContextControl flips that. You ship the assistant exactly what it needs: first a filtered project tree, then specific files or functions. The assistant ships back a `patch.txt` of mechanical edits. No browsing your disk. No guessing. No full-agent drift.

The goal is simple:

```text
Spend model tokens on engineering reasoning, not repo navigation.
```

---

## How It Works

ContextControl has three pipeline phases plus one launcher.

| Script | Role |
|---|---|
| **`ccDir.ps1`** | **Phase 1** — exports a filtered project tree plus a small navigation prompt. The assistant replies with the minimal file/function list. |
| **`cc.ps1`** | **Phase 2** — exports the selected source files or function bodies. The assistant replies with `patch.txt`. |
| **`ccReplace.ps1`** | **Phase 3** — applies `CC-REPLACE` blocks from `patch.txt` mechanically. |
| **`ccStart.ps1`** | Starts `ccReplace.ps1` in Agent Mode, so the whole pipeline can run from one terminal. |

The `.ps1` files are the core. The `.cmd` and extensionless files are launchers.

---

## Recommended Project Layout

Put the whole tool inside a single `contextcontrol/` folder in your project root:

```text
your-project/
├── contextcontrol/
│   ├── ccStart.ps1
│   ├── ccDir.ps1
│   ├── cc.ps1
│   ├── ccReplace.ps1
│   ├── ccStart.cmd
│   ├── ccDir.cmd
│   ├── cc.cmd
│   ├── ccReplace.cmd
│   ├── ccStart
│   ├── ccDir
│   ├── cc
│   ├── ccReplace
│   ├── .ccReplace.settings.json
│   ├── patch.txt
│   ├── cc_project_dir.md
│   ├── cc_code_export.md
│   └── .ccReplace.versions/
├── src/
├── include/
└── ...
```

By default:

```json
{
  "ProjectRoot": "..",
  "OutputRoot": ".",
  "DefaultPatchFile": "patch.txt",
  "VersionCacheEnabled": true,
  "VersionCacheRoot": ".ccReplace.versions"
}
```

Meaning:

```text
ProjectRoot = parent folder of contextcontrol/
OutputRoot  = contextcontrol/ itself
```

So ContextControl scans and patches your real project root, while keeping its own generated files inside `contextcontrol/`.

---

## Requirements

### Windows

- Windows PowerShell 5.1+ or PowerShell 7+
- No external dependencies

### macOS

- PowerShell 7+
- `pwsh` available in your terminal path
- `pbcopy` is used as the macOS clipboard fallback

Install PowerShell with your preferred method. Example with Homebrew:

```sh
brew install --cask powershell
pwsh --version
```

### Linux

- PowerShell 7+
- Optional clipboard backend for auto-copy: `wl-copy`, `xclip`, or `xsel`

If no clipboard backend exists, exports still write to disk and print the file path.

---

## Quick Start

### Windows

From your project root:

```bat
contextcontrol\ccStart.cmd
```

### macOS / Linux

From your project root:

```sh
sh ./contextcontrol/ccStart
```

Optional executable mode:

```sh
chmod +x ./contextcontrol/ccStart ./contextcontrol/ccDir ./contextcontrol/cc ./contextcontrol/ccReplace
./contextcontrol/ccStart
```

That starts Agent Mode and watches:

```text
contextcontrol/patch.txt
```

---

## Standard Workflow

Once `ccStart` is running:

1. **Type `DIR`.**
   ContextControl exports a filtered project tree to `contextcontrol/cc_project_dir.md` and copies it to clipboard.
   Paste that into your assistant chat.

2. **The assistant replies** with the minimum file/function list it needs, ending with `END`.

3. **Type `CC`.**
   Paste the assistant's file/function list when prompted.
   ContextControl exports those sources to `contextcontrol/cc_code_export.md` and copies it to clipboard.
   Paste that into your assistant chat.

4. **The assistant replies** with a `patch.txt` artifact containing raw `CC-REPLACE` blocks.
   Save or drop that patch into `contextcontrol/patch.txt`.

5. **Agent Mode detects the file change**, shows a preflight plan, and applies it after confirmation.

You stay in one terminal the entire time.

---

## Direct Commands

### Windows

```bat
contextcontrol\ccDir.cmd
contextcontrol\cc.cmd
contextcontrol\ccReplace.cmd
```

### macOS / Linux

```sh
sh ./contextcontrol/ccDir
sh ./contextcontrol/cc
sh ./contextcontrol/ccReplace
```

---

## `ccDir.ps1` — Project Tree Export

Exports a filtered project tree with a small navigation prompt.

### Usage

```powershell
.\contextcontrol\ccDir.ps1
.\contextcontrol\ccDir.ps1 -OutputFile .\contextcontrol\mytree.md
.\contextcontrol\ccDir.ps1 -MaxDepth 10
.\contextcontrol\ccDir.ps1 -Profile cmake-cpp
```

On macOS/Linux, prefer the launcher:

```sh
sh ./contextcontrol/ccDir
```

### Parameters

| Parameter | Default | Description |
|---|---|---|
| `-OutputFile` | `cc_project_dir.md` | Where to write the tree. Launchers default this into `contextcontrol/`. |
| `-MaxDepth` | `20` | Maximum recursion depth for the tree. |
| `-Profile` | `auto` | Project profile. Auto-detects common layouts. |
| `-IncludeAllTopLevel` | off | Include extra top-level folders that are normally hidden or filtered. |

### What it filters out

Build outputs, dependency caches, IDE folders, binaries, generated assets, previous ContextControl exports, and other files that pollute context without helping the assistant.

---

## `cc.ps1` — Source / Function Export

Exports the files or function bodies you specify, plus a compact prompt reminding the assistant how to produce patches.

### Usage

```powershell
.\contextcontrol\cc.ps1
.\contextcontrol\cc.ps1 -OutputFile .\contextcontrol\mycode.md
.\contextcontrol\cc.ps1 -MaxFileKB 1024
.\contextcontrol\cc.ps1 -ForceLargeFiles
.\contextcontrol\cc.ps1 -NoClipboard
.\contextcontrol\cc.ps1 -HashHints
```

On macOS/Linux, prefer the launcher:

```sh
sh ./contextcontrol/cc
```

### Parameters

| Parameter | Default | Description |
|---|---|---|
| `-OutputFile` | `cc_code_export.md` | Where to write the export. Launchers default this into `contextcontrol/`. |
| `-MaxFileKB` | `512` | Soft size limit per file. Files above this get skipped unless `-ForceLargeFiles` is set. |
| `-ForceLargeFiles` | off | Bypass the size limit. |
| `-NoClipboard` | off | Skip auto copy-to-clipboard. |
| `-HashHints` | off | Emit compact `HASH:` hints for safer patch verification. |

### Input syntax

After running, paste paths and function requests one per line, finishing with an empty line or `END`.

**Whole-file requests:**

```text
src/core/Engine.cpp
include/core/Engine.h
CMakeLists.txt
```

**Function-body extraction from an exact file:**

```text
FUNCTION src/core/Engine.cpp :: Engine::initialize
```

**Function-body extraction with a wildcard path:**

```text
FUNCTION src/world/World*.cpp :: World::tickChunk
```

**Function search across known source roots:**

```text
FUNC: tickChunk
```

**Whole-file export by symbol:**

```text
SYMBOL: kMaxChunkBudget
```

### Auto-added dependencies

`cc.ps1` can auto-add mechanical dependencies the assistant would usually ask for anyway:

- Matching headers for requested `.cpp`, `.cc`, `.cxx`, or `.c` files.
- Direct GLSL `#include` files referenced near the top of requested shader files.

These are logged in the export output.

---

## `ccReplace.ps1` — Patch Applier

The only script that writes to your project. It applies `CC-REPLACE` blocks from a patch file or pasted text.

### Usage

```powershell
.\contextcontrol\ccReplace.ps1
.\contextcontrol\ccReplace.ps1 -InputFile .\contextcontrol\patch.txt
.\contextcontrol\ccReplace.ps1 -InputFile .\contextcontrol\patch.txt -DryRun
.\contextcontrol\ccReplace.ps1 -InputFile .\contextcontrol\patch.txt -NoBackup
.\contextcontrol\ccReplace.ps1 -AgentMode
.\contextcontrol\ccReplace.ps1 -Help
```

On macOS/Linux, prefer the launcher:

```sh
sh ./contextcontrol/ccReplace
```

### Parameters

| Parameter | Description |
|---|---|
| `-InputFile <path>` | Patch file containing raw `BEGIN CC-REPLACE` blocks. |
| `-DryRun` | Preview every action without writing to disk. |
| `-NoBackup` | Disable version-cache writes for this run. |
| `-AgentMode` | Watch the default patch file and apply on every change. |
| `-Help` | Print full help and exit. |

### Interactive menu

Running `ccReplace.ps1` with no arguments shows an interactive menu for:

- Running existing `patch.txt`
- Choosing another patch file
- Pasting patch text
- Settings
- Agent Mode
- Running `ccDir.ps1`
- Running `cc.ps1`

---

## Agent Mode

Agent Mode is the recommended flow.

It watches:

```text
contextcontrol/patch.txt
```

It accepts these commands while watching:

- **`DIR`** — runs the project-tree export.
- **`CC`** — runs the source/function export.

The watcher creates `patch.txt` on first startup if it does not exist. That keeps first-run behavior consistent across Windows, macOS, and Linux.

---

## Clipboard Behavior

`ccDir.ps1` and `cc.ps1` copy exports to clipboard where possible.

| Platform | Clipboard path |
|---|---|
| Windows | `Set-Clipboard`, then `clip.exe` fallback |
| macOS | `Set-Clipboard`, then `pbcopy` fallback |
| Linux | `Set-Clipboard`, then `wl-copy`, `xclip`, or `xsel` fallback |

If clipboard copy fails, the export still writes to disk and prints the path.

---

## Patch Format Reference

Every patch block looks like this:

```text
BEGIN CC-REPLACE
FILE: path/relative/to/project_root.cpp
MODE: replace_region
NAME: my_region_name
HASH: <optional 8-char hex>
---
<code body>
END CC-REPLACE
```

`insert_include` is special — no body, just a header:

```text
BEGIN CC-REPLACE
FILE: src/core/Engine.cpp
MODE: insert_include
HEADER: <memory>
END CC-REPLACE
```

For region replacement, mark targets in your source with:

```cpp
// CC-REPLACE-BEGIN: my_region_name
static constexpr uint32_t kMaxItems = 8;
// CC-REPLACE-END: my_region_name
```

---

## Patch Modes

| Mode | Purpose |
|---|---|
| `replace_region` | Safest. Replaces text between `CC-REPLACE-BEGIN` / `CC-REPLACE-END` markers. |
| `insert_include` | Adds a single C/C++ include line idempotently. |
| `whole_file` | Creates or fully replaces a file. |
| `function` | Best-effort function body replacement. |
| `insert_after_function` | Inserts code after a named function. |
| `insert_before_function` | Inserts code before a named function. |
| `delete_function` | Deletes a named function. |
| `append_to_file` | Appends text to a file. |
| `create_directory` | Creates a directory with `DIR:` instead of `FILE:`. |

---

## Optional HASH Safety

`function` and `replace_region` modes accept an optional `HASH:` line. When present, `ccReplace.ps1` verifies the original target region before writing. If the hash does not match, the patch fails cleanly without modifying the file.

Use:

```powershell
.\contextcontrol\cc.ps1 -HashHints
```

or the equivalent platform launcher flow when you want the assistant to have hash hints.

---

## Version Cache and Rollback

Version cache is enabled by default. Successful writes create snapshots under:

```text
contextcontrol/.ccReplace.versions/
```

You get:

- Baseline snapshot before the first edit to an existing file.
- A new version on each non-duplicate write.
- Rollback through the Settings menu.

Disable per run:

```powershell
.\contextcontrol\ccReplace.ps1 -InputFile .\contextcontrol\patch.txt -NoBackup
```

Or disable globally in Settings.

---

## Settings

Settings persist in:

```text
contextcontrol/.ccReplace.settings.json
```

Important defaults:

```json
{
  "ProjectRoot": "..",
  "OutputRoot": ".",
  "DefaultPatchFile": "patch.txt",
  "VersionCacheEnabled": true,
  "VersionCacheRoot": ".ccReplace.versions"
}
```

You can override the project root with `CC_PROJECT_ROOT`:

```sh
CC_PROJECT_ROOT=/path/to/project sh ./contextcontrol/ccStart
```

```bat
set CC_PROJECT_ROOT=D:\Projects\YourProject
contextcontrol\ccStart.cmd
```

---

## Assistant Trigger Tags

ContextControl includes two optional tags for steering the assistant.

### `#preparationForResearch`

Use when you are staging a research-heavy coding turn. The assistant should reply with only a minimal file/folder list and a one-line research scope.

### `#deepcoding`

Use when you want a full patch. The assistant should produce:

- Purpose
- Technique chosen
- `patch.txt`
- Validation steps

Without a tag, the assistant follows normal phase behavior based on what you pasted.

---

## What ContextControl Is Not

ContextControl is not a full IDE agent. It does not browse your disk remotely, does not run tools on your behalf inside an LLM provider, and does not make hidden edits.

It is intentionally boring:

```text
local export -> assistant reasoning -> local mechanical patch
```

That is the point.

---

## Migration from Old Root-Level Layout

Older versions placed scripts directly in the project root. After moving to `contextcontrol/`, you can remove old root-level helper files with:

```powershell
powershell -ExecutionPolicy Bypass -File .\contextcontrol\cleanup-root-contextcontrol-files.ps1
```

Old generated root files such as `cc_project_dir.md` or `cc_code_export.md` can be deleted manually after confirming new outputs are appearing inside `contextcontrol/`.

---

## Pricing

ContextControl is currently free.

A subscription product is planned at a maximum of **$10/month** with a more advanced UI. Anyone using the GitHub release before **May 31st, 2026** gets **free access to the paid product on release**. After that date, the lifetime-access window is closed.

---

## License

See `LICENSE`.

---

## Feedback

Issues, edge cases, and feature requests are welcome on the GitHub issue tracker.
