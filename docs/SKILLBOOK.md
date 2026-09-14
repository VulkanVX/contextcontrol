# Skillbook and the PowerShell Workflow

Skillbook stores the instructions a model receives while ContextControl gathers context and prepares edits. Its built-in **Context Control** flow follows the public PowerShell entry points: [ccDir.ps1](../ccDir.ps1), [cc.ps1](../cc.ps1), [ccReplace.ps1](../ccReplace.ps1), and [ccStart.ps1](../ccStart.ps1).

These are ContextControl prompt instructions. The scripts and Workbench execute the local operations described by them.

## Built-in Skills

| Section | Skill | Input and output |
|---|---|---|
| CC Main | [CC Main](../skillbook/built-in-overrides/cc-main/cc-main.md) | Shared rules: reason from attached context, request only missing context, and return the format required by the active phase. |
| CC Flow | [DIR + Request](../skillbook/built-in-overrides/cc-flow/cc-flow-01-dir-request.md) | A task and `cc_project_dir.md` become a small request for source, functions, discovery, or a more detailed directory map. |
| CC Flow | [CC Export + Patch](../skillbook/built-in-overrides/cc-flow/cc-flow-02-cc-patch.md) | `cc_code_export.md` becomes either another context request or one response containing the required `CC-REPLACE` blocks. |
| CC Flow | [Chat](../skillbook/built-in-overrides/cc-flow/cc-flow-03-chat.md) | Ordinary conversation without a DIR/CC attachment; project code work is directed to DIR first. |

The normal desktop sequence is **DIR -> Send -> CC -> Send -> GO -> Apply**. DIR and CC gather context, Send asks the selected model to interpret it, GO previews the patch plan, and Apply writes the selected changes locally. GO and Apply do not send a model prompt. Raw mode bypasses the ContextControl instructions and attachments.

## Editing Instructions

Open **Skillbook**, select the **Context Control** flow, then choose **CC Main** or **CC Flow** and a skill. Built-in instructions open read-only; use **Edit**, make the change, and **Save**. **Lock** returns the editor to its read-only view.

The saved overrides have this layout beneath the ContextControl root:

```text
skillbook/
  built-in-overrides/
    cc-main/
      cc-main.md
    cc-flow/
      cc-flow-01-dir-request.md
      cc-flow-02-cc-patch.md
      cc-flow-03-chat.md
```

Each file has a small front matter block followed by the instruction body:

```markdown
---
title: DIR + Request
enabled: true
---
Input: user request plus DIR project map.
Output only the smallest CC export request.
```

If an override is absent, the app uses the matching default from [CodexInstructionCatalog.cs](../ide/ContextControl.Workbench/Features/ContextWorkflow/Services/Instructions/CodexInstructionCatalog.cs). [SkillbookService.cs](../ide/ContextControl.Workbench/Features/Skillbook/Services/SkillbookService.cs) loads overrides and selects the active phase. The files are maintained instructions based on the script contracts; changing a `.ps1` file does not automatically regenerate them.

CC Main and the active CC Flow skill are included in normal ContextControl model prompts. The Codex route also includes its phase contract. Disabling a built-in override omits that skill's instruction block; it does not change the script parser or remove the Codex phase contract.

## Custom Flows

Use the Skillbook page to add a flow, add a section, and add a skill. Flows and sections can be renamed; skill titles, enabled state, and markdown bodies can be saved and reloaded.

```text
skillbook/flows/<flow-id>/
  flow.json
  sections/<section-id>/
    section.json
    skills/<skill-id>.md
```

Project entries live under the ContextControl root's `skillbook/` directory. On Windows, global entries live under `%APPDATA%\ContextControl\skillbook\`. Legacy markdown files directly beneath either root are also loaded as instruction entries. Keep ordinary documentation outside these directories so it does not become a skill.

The flow library and built-in instruction overrides are implemented. Full per-phase activation of arbitrary custom flows remains in development; creating a flow does not by itself replace the built-in CC Flow. The page's flow inspector shows which context and instructions are used by each step.

## PowerShell Walkthrough

Run the commands from your ContextControl script folder. Confirm the target project in Settings first: the standard `<project>/contextcontrol` layout resolves `ProjectRoot: auto` to the parent project. To work on ContextControl itself, set ProjectRoot to `.` or to its absolute path in `.ccReplace.settings.json`.

Export the project map:

```powershell
.\ccDir.ps1 -NoClipboard
```

Give the map and your task to the model with the DIR + Request instruction. For a ContextControl project, a small request might be:

```text
cc.ps1
lib/Cc.Export.Source.ps1
END
```

Run the source exporter, paste the request into its input, and finish with `END`:

```powershell
.\cc.ps1 -NoClipboard
```

Use `cc_code_export.md` with the CC Export + Patch instruction. If more context is needed, repeat the narrow request/export step. `FIND:` discovers owners without exporting source bodies; `EXPAND:` asks the Workbench to run a richer scoped DIR export. The built-in model contract requests one FIND or one EXPAND at a time, separately from file/function requests.

Save the model's raw patch blocks to `patch.txt`, then preview them:

```powershell
.\ccReplace.ps1 -InputFile .\patch.txt -PlanOnly -Json
```

After reviewing the plan, apply the effective changes:

```powershell
.\ccReplace.ps1 -InputFile .\patch.txt -Apply effective
```

For the interactive terminal workflow, `.\ccStart.ps1` starts `ccReplace.ps1 -AgentMode`, which watches the configured patch file and exposes DIR, CC, GO, and Settings commands.

The [flow reference](../CONTEXT_CONTROL_FLOW_REFERENCE.md) describes the full request grammar, generated artifacts, supported patch modes, and phase contracts.
