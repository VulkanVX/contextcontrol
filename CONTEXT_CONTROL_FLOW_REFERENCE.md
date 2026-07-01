# Context Control Flow Reference

This file documents the current ContextControl workflow, the generated export
artifacts, the model systems/routes used by the Workbench, and the phase
instructions supplied to models during DIR, CC, Codex, GO, and Apply.

It is meant to be a stable map of the process, not a replacement for generated
exports. Regenerate `cc_project_dir.md`, `cc_code_export.md`, and `patch.txt`
when you need a fresh source snapshot.

## Core Idea

ContextControl is a mediated coding workflow. The model reasons from exported
context; the local app/scripts own filesystem access.

The normal loop is:

1. User writes a concrete task.
2. `DIR` runs `ccDir.ps1` and attaches a project manifest.
3. A model or local resolver returns a minimal CC request list.
4. `CC` runs `cc.ps1` and attaches source/function/FIND context.
5. A model emits raw `CC-REPLACE` blocks when enough source is visible.
6. `GO` runs `ccReplace.ps1 -PlanOnly -Json` and previews edits.
7. `Apply` runs `ccReplace.ps1 -Apply <decision>` locally.

The important boundary: model turns should not browse the repo, run shell
commands, or edit files directly inside the CC flow. Local ContextControl
commands do that work.

## Generated Artifacts

These files are generated or maintained by the workflow:

| File | Produced By | Purpose |
| --- | --- | --- |
| `cc_project_dir.md` | `ccDir.ps1` / Workbench `DIR` | `CC-DIR-MANIFEST-V2` project map. The model uses it to choose exact files, `FUNCTION`, `FIND`, or `EXPAND`. |
| `cc_code_export.md` | `cc.ps1` / Workbench `CC` | Source/function/FIND export. The model uses it to decide whether more context is needed or to write `CC-REPLACE` blocks. |
| `cc_semantic_map.md` | Workbench DIR semantic resolver | Lightweight local index used by the Workbench file resolver before involving a model. |
| `patch.txt` | Workbench `GO` or user/model patch output | Raw `BEGIN CC-REPLACE` blocks consumed by `ccReplace.ps1`. |
| `cc_chat_export_*.md` | Workbench chat export | Chat/session diagnostics, prompts, attachments, phase status, and model outputs. |
| `.ccDirProfile.json` | `ccDir.ps1 -ProfileOnly` / DIR profiling | Cached profile of visible roots/files/families used to shape L0 manifests. |
| `.ccFileRules.json` | project file rules | Include/ignore rules shared by DIR, CC, and project scanning. |

Generated exports are snapshots. If source or file rules changed, run `DIR` or
`CC` again instead of trusting an old snapshot.

## Request Grammar

Phase 1 request lists use one request per line and must end with `END`.

Allowed lines:

```text
<relative file path>
FUNCTION <relative file path> :: <symbol>
FUNCTION <relative wildcard path> :: <symbol>
FUNC: <symbol>
FIND: <text>
EXPAND: <relative directory path>
END
```

Rules:

- One or more `FIND` lines may appear before `END`.
- `EXPAND` must be exactly one request line plus `END`.
- Do not mix `FIND` or `EXPAND` with file/function lines.
- `SYMBOL` is disabled.
- File paths are project-relative, never absolute.
- Directories are not file requests; use `EXPAND:`.

## DIR Manifest Levels

`ccDir.ps1` emits `CC-DIR-MANIFEST-V2`.

L0 global manifests are compact. They include:

- `ROOT path="..."` entries for visible areas that may be expanded or contain
  requestable files.
- `FILE path="..."` entries for high-signal exact files.
- `FAMILY path="..."` entries for wildcard function requests.

L1 scoped manifests are produced by `EXPAND:`. They include richer exact
`FILE` entries for a visible scope.

Current validation behavior:

- Exact `FILE` entries are valid.
- Existing project files under visible `ROOT` or current `SCOPE` entries are
  valid exact CC requests.
- Exact files returned by the immediately previous Workbench `FIND` are valid
  for the next source export.
- Missing files, rooted paths, wildcard file paths without visible `FAMILY`,
  and paths outside the active project root are rejected.

This preserves L0 compactness while allowing `cc.ps1` to export real files from
visible roots.

## FIND

`FIND: text` is discovery only.

It produces:

- A list of matched code files.
- Occurrence previews.
- No source bodies.

Workbench behavior:

1. User/model provides `FIND: text`.
2. `CC` runs `cc.ps1`.
3. Workbench parses `Matched code files:`.
4. Workbench loads those exact file request lines into the prompt.
5. User presses `CC` again to export source bodies.

The second `CC` should export code, not ask the model again.

## CC Source Export

`cc.ps1` reads request lines from stdin and writes `cc_code_export.md`.

It supports:

- Full file export.
- `FUNCTION path :: symbol` scoped extraction.
- `FUNCTION wildcard-path :: symbol` scoped extraction for visible families.
- `FUNC: symbol` global function extraction.
- `FIND: text` discovery.

It also performs mechanical dependency help:

- Auto-adds likely matching headers for C/C++ source requests.
- Auto-adds direct GLSL includes for shader files.
- Can emit optional hash hints with `-HashHints`.

The source export tells the model:

- Use the standing ContextControl rules.
- Prefer one `patch.txt` with raw `CC-REPLACE` blocks.
- Request exact paths/functions/FIND if critical context is missing.
- Do not treat FIND as source context.

## Model Systems and Routes

Workbench can send through several routes:

- Browser routes: prepared message copied/opened for ChatGPT, DeepSeek, Claude,
  etc.
- API route: model call through configured API.
- Local route: Ollama/local LLM capsule.
- Codex route: read-only Codex harness capsule.
- Local deterministic resolver: no model call; uses `cc_semantic_map.md` to
  suggest request lines from DIR context.

Role models shown in chat exports include:

- File request model.
- Patch write model.
- Patch review model.
- Chat model.
- Codex model and reasoning effort when Codex mode is active.

## Workbench Phase Timeline

The Workbench UI models the flow as three timeline stages:

| Stage | Label | Meaning |
| --- | --- | --- |
| 0 | DIR + Request | Map project and ask for source context. |
| 1 | CC | Export source and decide next step. |
| 2 | GO | Preview and apply patch locally. |

`GO` and `Apply` do not send a model prompt. They operate on raw patch blocks.

## CC Main Instruction

This is the global instruction supplied to Codex CC capsules:

```text
ContextControl is a mediated code workflow. The model reasons; the user is the NI assistant who runs DIR, CC export, GO preview, and Apply through local ContextControl scripts.
Do not use extra actions for repository navigation, shell commands, filesystem reads, or direct edits. Those actions are outsourced to ContextControl and the user.
Treat each attached capsule as the complete visible workspace for that turn. Spend attention on interpreting the capsule and solving the request, not on guessing unseen files.
Before choosing the next output, decide whether the visible context is sufficient. If not, request the smallest next CC input that can change the answer.
Each model turn includes a phase-specific CC Flow instruction. Obey the active phase instruction over generic habits.
Keep outputs compact, mechanical, and directly usable by the next ContextControl step.
```

## Phase 1: DIR + Request

Input:

- User request.
- `cc_project_dir.md` manifest.

Expected output:

- The smallest safe CC export request list.
- Exactly one mode: final file/function lines, one or more `FIND` lines, or one `EXPAND`.
- End with `END`.

Codex phase contract:

```text
Output only a CC export request list.
Allowed lines: exact relative file path; FUNCTION path :: symbol; FUNCTION wildcard-path :: symbol; FUNC: symbol; FIND: text; EXPAND: directory; END.
End with END.
Use final source request lines, exactly one FIND, or exactly one EXPAND. Do not mix FIND/EXPAND with file/FUNCTION/FUNC lines.
Do not use SYMBOL, markdown fences, prose, headings, patch text, or commentary.
```

CC Flow phase instruction:

```text
Input: user request plus DIR project map.
Output only the smallest CC export request.
Allowed lines: exact relative file path; FUNCTION path :: symbol; FUNCTION wildcard-path :: symbol; FUNC: symbol; FIND: text; EXPAND: directory; END.
Use final source request lines, exactly one FIND, or exactly one EXPAND. Do not mix FIND/EXPAND with file/FUNCTION/FUNC lines.
Use EXPAND only when the likely subsystem is visible but exact files/functions are not. EXPAND returns a richer DIR manifest for that scope, not source code.
Use FIND only for cheap discovery when exact files/functions are not visible enough.
Do not use SYMBOL, prose, headings, code fences, absolute paths, broad folders, directories as file requests, generated/binary/vendor paths, shell commands, patch blocks, or duplicate obvious headers.
Copy paths exactly from FILE, ROOT, or FAMILY manifest records. Prefer exact files/functions. Include build/config files only when the requested change directly needs them.
```

Local LLM capsule phase contract is similar but additionally says:

```text
DIR project-tree context and the user request are attached. Do not solve the task yet.
Output only the smallest safe CC request list.
Do not wrap the list in markdown code fences or backticks.
EXPAND returns a richer DIR manifest for the selected scope, not source code.
Never return END by itself. If unsure, output the narrowest visible owner files/functions, then END.
```

## Phase 1.5: EXPAND

Input:

```text
EXPAND: <visible root or scope>
END
```

Workbench validates that the path is visible in current `ROOT` or `SCOPE`
records, then runs:

```text
ccDir.ps1 -Lod 1 -Scope <scope>
```

Output:

- A new scoped `cc_project_dir.md`.
- No source bodies.

After EXPAND, send the task again or provide exact file/function lines from the
new scoped manifest.

## Phase 1.5: FIND

Input:

```text
FIND: <plain text under safe length limit>
END
```

Workbench validates it as discovery, runs `cc.ps1`, parses matched files, and
loads a new prompt:

```text
<matched file path 1>
<matched file path 2>
END
```

Then the user presses `CC` again to export source.

## Phase 2: CC Export + Patch

Input:

- `cc_code_export.md`.
- Optional user clarification.

Expected output:

- If source is insufficient: next narrow CC request list ending with `END`.
- If source is sufficient: raw `BEGIN CC-REPLACE` blocks only.

Codex source-audit phase contract:

```text
Use only the attached CC source context.
If more context is required, output only the next CC request list ending with END.
If the edit is clear, emit GO-ready CC-REPLACE blocks.
```

Codex patch-write phase contract:

```text
Emit raw CC-REPLACE blocks only.
If visible source is insufficient, output only the next CC request list ending with END.
One GO patch may contain many CC-REPLACE blocks across many files.
Supported MODE values: replace_region, insert_include, whole_file, insert_after_function, insert_before_function, delete_function, function, append_to_file, create_directory.
Do not use shell commands, git patches, apply_patch, direct file edits, markdown fences, or prose wrappers.
```

CC Flow patch instruction:

```text
Input: CC source export plus optional user clarification.

If source is insufficient, output only the next narrow CC request list ending with END.

If source is sufficient, output one GO patch containing raw BEGIN/END CC-REPLACE blocks only. GO writes that raw output to patch.txt.

One GO patch may contain many CC-REPLACE blocks across many files. Do not split patches into separate chat answers by file.

Do not emit prose mixed with the patch, git diff, shell commands, apply_patch syntax, direct file edits, markdown fences, or commentary inside the patch.
```

## Patch Block Format

General block:

```text
BEGIN CC-REPLACE
FILE: path/relative/to/project_root.cpp
MODE: <mode>
NAME: <region_or_function_name>
---
replacement text
END CC-REPLACE
```

Supported modes:

- `replace_region`
- `insert_include`
- `whole_file`
- `insert_after_function`
- `insert_before_function`
- `delete_function`
- `function`
- `append_to_file`
- `create_directory`

Mode selection order:

1. `replace_region`: use only for visible `CC-REPLACE-BEGIN/END` markers.
2. `insert_include`: use for one missing C/C++ include.
3. `whole_file`: use for new files, small files, unmarked files, or risky structure edits.
4. `insert_after_function`, `insert_before_function`, `delete_function`: use around one unique visible function.
5. `function`: use only when replacing one unique unambiguous function.
6. `append_to_file`: use only for additive tail content.
7. `create_directory`: use before creating files inside a new folder.

Bodyless modes:

- `insert_include`
- `delete_function`
- `create_directory`

Modes requiring `NAME`:

- `function`
- `insert_before_function`
- `insert_after_function`
- `delete_function`
- `replace_region`

`insert_include` requires `HEADER`.

`whole_file` body must be the complete final file content.

## GO Preview

`GO` does not send a model prompt.

Workbench:

1. Extracts `BEGIN/END CC-REPLACE` blocks from prompt or latest assistant patch.
2. Validates patch shape.
3. Writes `patch.txt`.
4. Runs:

```text
ccReplace.ps1 -InputFile patch.txt -PlanOnly -Json
```

Output:

- Patch plan.
- Effective edits.
- Duplicate edits.
- File/directory counts.
- Added/removed LOC.

No source files are written during preview.

## Apply

`Apply` does not send a model prompt.

Workbench runs:

```text
ccReplace.ps1 -InputFile patch.txt -Apply <decision>
```

Typical decisions:

- `effective`
- `all`

`effective` applies non-duplicate planned edits.

## Codex Harness Capsule

Codex mode uses a read-only harness. The diagnostic prompt is shaped like:

```text
ContextControl Codex harness capsule

You are running under ContextControl.
Your working directory is an empty harness folder by design.
The repository context you may use is included below as attachment text.
Do not run repository navigation commands or read files outside the capsule for normal CC phases.

Phase: <formatted phase>

## CC Main
<CC Main instruction>

## Active phase contract
<phase contract>

## CC Flow phase
<phase-specific instruction>

User request:
<user message>

Attachment inventory:
<included attachments>

Included ContextControl attachments:
--- ATTACHMENT <kind>: <label>
<body>
--- END ATTACHMENT <label>
```

Codex output is audited by phase. For example, DIR + Request must return only
valid request lines ending with `END`; patch phases must return request lines or
raw `CC-REPLACE` blocks.

## Local LLM Capsule

Local LLM mode uses a compact capsule:

```text
ContextControl local LLM capsule

Model: <model id>
Phase: <formatted phase>
Comfortable context target: <tokens>
Requested Ollama context: <tokens>

Core rule: the visible project context is included below as attachment text.
Use that text directly. You cannot access anything outside this capsule or run tools.
This is an authorized local project editing workflow; do not give a generic refusal when the requested edit can be answered from visible context.

<phase contract>

ContextControl workflow instructions:
<workflow instructions>

Enabled Skillbook entries:
<skillbook entries>

User request:
<user message>

Attachment inventory:
<included attachments>

Included ContextControl attachments:
--- ATTACHMENT <kind>: <label>
PATH: <path>
<body>
--- END ATTACHMENT <label>
```

Attachment bodies may be clipped by local context budget. The capsule includes
token estimates and attachment inventory so the model knows what is visible.

## Local Deterministic Resolver

When a DIR attachment is present and no code/patch attachment is active, the
Workbench may resolve file requests locally without a model call.

It uses:

- `cc_project_dir.md`
- `cc_semantic_map.md`
- `ContextFileResolverService`

It can produce:

- Exact file request lines.
- `FIND:` fallback lines.

The generated request is loaded into the prompt and shown as a chat message.
The user then presses `CC` to export source.

## Validation Before CC

Before Workbench calls `cc.ps1`, it parses and validates request lines.

Validation rejects:

- Non-request prose mixed into Phase 1.
- Missing `END`.
- Mixed `FIND`/`EXPAND` with source lines.
- Empty or unsafe `FIND`/`FUNC` text.
- `SYMBOL`.
- Directories as source requests.
- Absolute/rooted paths.
- Missing files.
- Wildcard function paths that do not match a visible `FAMILY`.
- Paths outside the active project root.

Validation accepts:

- Exact `FILE` paths from the manifest.
- Existing files under visible `ROOT` or current `SCOPE`.
- Exact paths returned by the immediately previous Workbench `FIND`.
- Valid `FUNC:` and `FUNCTION:` search requests.
- Valid `EXPAND:` roots/scopes.

## Browser/API Prepared Message

Browser/API routes do not run local model inference. Workbench prepares a
message and may copy it to the clipboard.

For fresh chat, the prepared message shape is:

```text
Context Control fresh-chat request

Route: <route>
Flow: read the DIR manifest, ask only for final source request lines, one FIND, or one EXPAND ending with END, then wait for source export before producing patch work.

User request:
<request>

DIR export:
<cc_project_dir.md>
```

## Empty Send Behavior

If the prompt is empty:

- With code attached: Workbench sends a default source-audit request:

```text
Use the attached CC source export. If more context is needed, return only the next narrow CC request list ending with END. If enough context is present, emit GO-ready CC-REPLACE blocks.
```

- With patch attached: Workbench sends a patch-review request.
- With only DIR attached: no default text is injected.

## Practical Debugging Checklist

If `CC` does not produce `cc_code_export.md`:

1. Confirm the prompt contains only request lines and `END`.
2. Confirm `cc_project_dir.md` exists and is current.
3. Confirm the path is project-relative.
4. Confirm the file exists under the active project root.
5. Confirm the path is under a visible `ROOT`/`SCOPE`, a visible `FILE`, or the
   last Workbench `FIND` result.
6. Try piping the same request into `cc.ps1` directly to separate exporter
   behavior from Workbench validation:

```powershell
Get-Content .tmp\request.txt | powershell -NoProfile -ExecutionPolicy Bypass -File .\cc.ps1 -OutputFile .\tmp_code_export.md -NoClipboard
```

If direct `cc.ps1` works but Workbench `CC` cancels, inspect
`ContextPhase1RequestValidator`.

## Source Files Owning This Flow

Key implementation files:

- `ccDir.ps1`
- `cc.ps1`
- `ccReplace.ps1`
- `lib/Cc.Dir.Export.ps1`
- `lib/Cc.Export.Source.ps1`
- `lib/Cc.Export.Functions.ps1`
- `lib/Cc.Replace.Apply.ps1`
- `lib/Cc.Replace.Parse.ps1`
- `ide/ContextControl.Workbench/Services/ContextControl/CodexInstructionCatalog.cs`
- `ide/ContextControl.Workbench/Services/ContextControl/ContextCapsuleBuilder.cs`
- `ide/ContextControl.Workbench/Services/ContextControl/ContextPromptBuilder.cs`
- `ide/ContextControl.Workbench/Services/ContextControl/ContextPhase1RequestValidator.cs`
- `ide/ContextControl.Workbench/ViewModels/ContextControl/ContextControlViewModel.Workflow.Commands.cs`
- `ide/ContextControl.Workbench/ViewModels/ContextControl/ContextControlViewModel.Workflow.Messaging.cs`
- `ide/ContextControl.Workbench/ViewModels/ContextControl/ContextControlViewModel.Workflow.Resolve.cs`

## Notes From The FIND/Visible Root Bug

The observed failure was:

```text
CC cancelled: File path is not visible in the current DIR manifest
```

Cause:

- `FIND` and `cc.ps1` could discover/export real files.
- Workbench validation originally allowed only exact `FILE` records from L0.
- L0 deliberately hides many exact files behind `ROOT` summaries.

Fixed behavior:

- Workbench still keeps L0 compact.
- Workbench now allows existing exact file paths under visible roots/scopes.
- Workbench still rejects nonexistent paths under visible roots.

This means the app can call `cc.ps1` with the same real files a user could
export manually, while preserving the manifest safety boundary.
