# ContextControl

A deterministic source-context pipeline for working with large codebases through any chat assistant.

ContextControl reduces context bloat and drives all the LLM compute toward reasoning about your problem — instead of wasting it on repository exploration, repeated rule text, or tool mechanics. It works with **any LLM**: Claude, ChatGPT, Codex, GitHub Copilot, Gemini, DeepSeek — fully independent from the provider.

The assistant **never touches your disk**. You export context locally, the assistant writes a mechanical patch recipe, and ContextControl applies that recipe locally on your machine.

---

## Why ContextControl

If you've worked on a codebase larger than a few thousand lines through a chat LLM, you know the pattern: the assistant burns half its tokens re-exploring the repo, asking for the same files over and over, and dumping rule preambles instead of solving your actual problem.

ContextControl flips that. You ship the assistant exactly what it needs — a filtered project tree, then specific files or functions — and it ships you back a `patch.txt` of mechanical edits. No browsing. No guessing. No drift.

---

## The Pipeline

Three scripts, three phases:

| Script | Role |
|---|---|
| **`ccDir.ps1`** | Phase 1 — Exports a filtered project tree + a small navigation prompt. Assistant replies with the minimal file/function list. |
| **`cc.ps1`** | Phase 2 — Exports the selected source files / function bodies. Assistant replies with `patch.txt`. |
| **`ccReplace.ps1`** | Phase 3 — Applies `CC-REPLACE` blocks from `patch.txt` mechanically. |
| **`ccStart.ps1`** | One-line launcher that starts `ccReplace.ps1` in **Agent Mode** so the whole pipeline runs without leaving the terminal. |

---

## Quick Install

Clone the repo (or drop the four `.ps1` files) into your project root, then unblock them once:

```powershell
cd D:\Projects\YourProject
Set-ExecutionPolicy -Scope CurrentUser -ExecutionPolicy RemoteSigned
Unblock-File .\cc.ps1
Unblock-File .\ccDir.ps1
Unblock-File .\ccReplace.ps1
Unblock-File .\ccStart.ps1
```

Start the pipeline:

```powershell
.\ccStart.ps1
```

That's it. You're now in Agent Mode, watching `patch.txt`. From here you can type `DIR` or `CC` at any time to export — no need to leave the terminal.

> **Requirements:** Windows PowerShell 5.1+ or PowerShell 7+. No external dependencies.

---

## Standard Workflow

Once `ccStart.ps1` is running:

1. **Type `DIR`** → exports a filtered project tree to `cc_project_dir.md` and copies it to your clipboard. Paste it into your assistant chat.
2. **Assistant replies** with the minimum file/function list it needs, ending with `END`.
3. **Type `CC`** → paste that file list when prompted. `cc.ps1` exports just those sources to `cc_code_export.md` and copies it to clipboard. Paste into chat.
4. **Assistant replies** with a `patch.txt` artifact containing raw `CC-REPLACE` blocks. Save or drop it into your project root.
5. **Agent Mode detects the file change**, runs the patch through `ccReplace.ps1`, and applies it.

You stay in one terminal the entire time.

---

## `ccStart.ps1` — Launcher

The simplest entry point. It locates `ccReplace.ps1` next to itself (or in the current directory) and starts it in Agent Mode.

```powershell
.\ccStart.ps1
```

Any extra arguments are forwarded to `ccReplace.ps1`. Example:

```powershell
.\ccStart.ps1 -DryRun
```

---

## `ccDir.ps1` — Project Tree Export (Phase 1)

Exports a filtered directory tree with a small navigation prompt and copies the result to your clipboard.

### Usage

```powershell
.\ccDir.ps1
.\ccDir.ps1 -OutputFile mytree.md
.\ccDir.ps1 -MaxDepth 10
.\ccDir.ps1 -Profile cmake-cpp
.\ccDir.ps1 -IncludeHiddenTopLevel
```

### Parameters

| Parameter | Default | Description |
|---|---|---|
| `-OutputFile` | `cc_project_dir.md` | Where to write the tree. |
| `-MaxDepth` | `20` | Maximum recursion depth for the tree. |
| `-Profile` | `auto` | Project profile. `auto` detects from project files. Manual values: `cmake-cpp`, `web`, `python`, `generic`. |
| `-IncludeHiddenTopLevel` | off | Include dot-folders at the top level (excluding `.git`, IDE folders, etc.). |

### What it filters out

Build outputs, dependency caches, IDE folders, binaries, generated assets — everything that pollutes context without helping the assistant. The tree you ship is the tree that actually matters.

The tool also auto-excludes its own previous exports (`cc_code_export*.md`, `cc_project_dir*.md`) so they don't recursively pollute future runs.

---

## `cc.ps1` — Source / Function Export (Phase 2)

Exports the files or function bodies you specify, plus an export header reminding the assistant of patch-format rules.

### Usage

```powershell
.\cc.ps1
.\cc.ps1 -OutputFile mycode.md
.\cc.ps1 -MaxFileKB 1024
.\cc.ps1 -ForceLargeFiles
.\cc.ps1 -NoClipboard
.\cc.ps1 -HashHints
```

### Parameters

| Parameter | Default | Description |
|---|---|---|
| `-OutputFile` | `cc_code_export.md` | Where to write the export. |
| `-MaxFileKB` | `512` | Soft size limit per file. Files above this get skipped unless `-ForceLargeFiles` is set. |
| `-ForceLargeFiles` | off | Bypass the size limit. |
| `-NoClipboard` | off | Skip the auto copy-to-clipboard step. |
| `-HashHints` | off | Emit compact `HASH:` hints for safer patch verification. |

### Input syntax

After running, paste paths and function requests one per line, finishing with an empty line or `END`.

**Whole-file requests:**
```
src/core/Engine.cpp
include/core/Engine.h
CMakeLists.txt
```

**Function-body extraction (exact file):**
```
FUNCTION src/core/Engine.cpp :: Engine::initialize
```

**Function-body extraction (wildcard, for split implementation families):**
```
FUNCTION src/world/World*.cpp :: World::tickChunk
```

**Function search across all source roots:**
```
FUNC: tickChunk
```

**Whole-file export by symbol (for declarations, types, constants, macros):**
```
SYMBOL: kMaxChunkBudget
```

### Auto-added dependencies

`cc.ps1` does some mechanical work the assistant would otherwise have to ask for:

- When you request a `.cpp` / `.cc` / `.cxx` / `.c` file, **the matching header is auto-added** if it exists.
- When you request a shader file, **direct `#include` files referenced in the first 100 lines are auto-added**.

These are logged so you can see what was pulled in.

---

## `ccReplace.ps1` — Patch Applier (Phase 3)

The only script that actually writes to your project. Applies `CC-REPLACE` blocks from a patch file or pasted text.

### Usage

```powershell
.\ccReplace.ps1                                  # interactive menu
.\ccReplace.ps1 -InputFile .\patch.txt           # apply a patch file
.\ccReplace.ps1 -InputFile .\patch.txt -DryRun   # preview only, no writes
.\ccReplace.ps1 -InputFile .\patch.txt -NoBackup # skip version-cache writes
.\ccReplace.ps1 -AgentMode                       # watch default patch file
.\ccReplace.ps1 -Help
```

### Parameters

| Parameter | Description |
|---|---|
| `-InputFile <path>` | Patch file containing raw `BEGIN CC-REPLACE` blocks. |
| `-DryRun` | Preview every action without writing to disk. |
| `-NoBackup` | Disable version-cache writes for this run. |
| `-AgentMode` | Watch the default patch file and apply on every change. |
| `-Help` | Print full help and exit. |

### Interactive menu (no parameters)

Running `.\ccReplace.ps1` with no arguments shows:

```
1. Run using existing patch.txt
2. Provide your own filepath to another patch file
3. Paste your own text
4. Settings
5. Agent Mode - watch patch.txt and apply when it changes
6. Run ccDir.ps1 directory export
7. Run cc.ps1 source/function export
0. Exit
```

Options 6 and 7 mean you can run the entire pipeline from inside `ccReplace.ps1` if you don't want to use `ccStart.ps1`.

### Agent Mode

Enter Agent Mode (via `ccStart.ps1` or menu option 5) and the script:

- Watches your default patch file (`patch.txt` by default).
- Applies it automatically every time it changes.
- Accepts inline pipeline commands without exiting:
  - **`DIR`** → run `ccDir.ps1`
  - **`CC`** → run `cc.ps1`
- Returns to watching after every action.

This is the recommended way to work. One terminal, full pipeline, never break flow.

### Patch modes

`ccReplace.ps1` supports these `MODE:` values inside `CC-REPLACE` blocks. The assistant picks the right one — you don't need to memorize them, but here's the reference:

| Mode | When it's used |
|---|---|
| `replace_region` | Safest. Replaces text between `CC-REPLACE-BEGIN` / `CC-REPLACE-END` markers. Preferred when possible. |
| `insert_include` | Adds a single `#include` line, idempotent (skips if already present). |
| `whole_file` | New files, small files, or risky patches better solved by full rewrite. |
| `function` | Best-effort function body replacement. |
| `insert_after_function` | Insert code immediately after a named function. |
| `insert_before_function` | Insert code immediately before a named function. |
| `delete_function` | Remove a named function. |
| `append_to_file` | Add to the end of an existing file (logs, config). |
| `create_directory` | Create a directory (uses `DIR:` instead of `FILE:`). |

### Optional HASH safety

`function` and `replace_region` modes accept an optional `HASH:` line. When present, ccReplace verifies the original target region matches before writing. Mismatch = clean failure, no corruption. Use `cc.ps1 -HashHints` to make hash hints available to the assistant.

### Version cache (rollback)

Every successful write creates a snapshot under `.ccReplace.versions/` (configurable). You get:

- Automatic baseline before the first edit to any existing file.
- A new version on every non-duplicate write.
- Rollback to any previous version from **Settings → option 9** (Version log / rollback / remove cached versions).

Disable per-run with `-NoBackup`, or globally in Settings.

### Settings

`ccReplace.ps1` Settings menu (option 4):

| # | Setting |
|---|---|
| 1 | Preflight statistics |
| 2 | File detail statistics |
| 3 | Created/removed lists |
| 4 | Directory prefixes |
| 5 | Confirmation stage (review and approve before writes) |
| 6 | Repeat after incorrect input |
| 7 | Reconfigure default patch file |
| 8 | Version cache on/off |
| 9 | Version log / rollback / remove cached versions |
| 10 | Reconfigure version cache directory |

Settings persist between runs.

---

## Patch Format Reference

Every block looks like this:

```
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

```
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

## Trigger Tags for Assistants

Two trigger tags steer the assistant toward the right phase behavior:

- **`#preparationForResearch`** — research-heavy turn. Assistant replies with file/folder list only, no code.
- **`#deepcoding`** — full patch turn. Assistant produces purpose, technique chosen, `patch.txt`, and validation steps.

Without a tag, the assistant uses default phase behavior based on what you sent (a tree → file list, a code export → patch).

---

## Project Layout

```
your-project/
├── ccStart.ps1
├── ccDir.ps1
├── cc.ps1
├── ccReplace.ps1
├── patch.txt              # written by you (paste from assistant), watched by Agent Mode
├── .ccReplace.versions/   # auto-created snapshot folder for rollback
├── src/
├── include/
└── ...
```

Run all four scripts from your project root.

---

## Pricing

ContextControl is currently free.

A subscription product is planned at a maximum of **$10/month** with a more advanced UI. Anyone using the GitHub release before **May 31st, 2026** gets **free access to the paid product on release**. After that date, the lifetime-access window is closed.

---

## License

See `LICENSE`.

---

## Feedback

Issues, edge cases, and feature requests welcome on the GitHub issue tracker.
