# ContextControl

ContextControl is a native desktop workbench for keeping local code context, local models, and patch workflows under the user's control.

The current release focuses on a Windows x64 desktop app that can be opened like a normal EXE, then used to install local LLM dependencies and download model weights on demand. The older PowerShell CLI pipeline is still included as the core deterministic context engine.

## Install On Windows

Latest release:

https://github.com/VulkanVX/contextcontrol/releases/latest

For a fresh Windows PC, download only the installer:

```text
ContextControl-win-x64-Setup.exe
```

You do not need to download a separate app zip. The setup EXE is a single-file installer that already contains the full ContextControl app folder. It asks for the install location, shortcut options, optional WebView2 Runtime install, and whether to launch when setup finishes.

Default install folder:

```text
%LOCALAPPDATA%\Programs\ContextControl
```

After install, run ContextControl from the Start Menu shortcut or from:

```text
<install folder>\ContextControl.Workbench.exe
```

The app is self-contained, so a separate .NET runtime install is not required.
The installer registers a per-user Windows uninstall entry, so you can remove it from Windows **Installed apps / Apps & features** or from the Start Menu `Uninstall ContextControl` shortcut.

After this version is installed, ContextControl checks GitHub releases on startup when internet is available. The header bar also has a **Check updates** button; when a newer release exists, the same button downloads the newest setup EXE with the normal transfer progress bar, starts it against the current install folder, then closes the running Workbench so setup can replace the app files safely.

Update behavior:

- The updater reuses an already downloaded installer for the same release instead of downloading the full setup EXE again.
- Stale update downloads from older versions are cleaned from the temp update cache when possible.
- The live-update handoff waits for the running Workbench process to exit before opening setup, so app files are not replaced while the app is still using them.
- Setup compares installed files with the embedded payload and writes only changed files; unchanged files are skipped.

ContextControl currently ships updates as a full setup EXE. That means a new release still downloads the full installer once, but repeated attempts for the same release should not download another copy.

GitHub's automatic **Source code** downloads are source snapshots, not runnable app packages. Use them only if you want to build from source.

## Interface and Chat Monitor

Version 0.4 adds a consistent **Interface scale**, adaptive chat headers and timestamps, animated request feedback, and a floating **Chat Monitor**. The new **Studio** theme is the default for fresh installs; existing appearance preferences are preserved.

- Adjust the entire interface in **View → Settings → Appearance → Interface scale**. The value 11 is 100%; 16.5 is 150%. Code, prompt and chat sizes remain available for adjusting their relative text sizes.
- Enable or hide the floating window in **Settings → Prompt Window → Floating Chat Monitor**. Drag its title bar to move it.
- New chats and chats you continue are added automatically. Each row shows live status and available token, speed and elapsed-time statistics. Hover for full details.
- Use the **open** icon to bring the exact chat forward, **reply** to expand its shared draft, and **×** to remove only the monitor row. Right-click any chat in history to add or remove it.
- Drop files onto the expanded reply panel or use its attachment button. **Ctrl+Enter** sends; **Escape** collapses the composer and keeps the draft. Switching to another chat closes the composer to keep replies attached to the correct conversation.

The monitor follows chats in the running ContextControl instance, including while its main window is minimized. Saved rows and position survive restart; live request state does not. It does not observe unrelated Codex CLI sessions or other applications.

See [the interface guide](docs/INTERFACE.md) and [v0.4.0 release notes](docs/releases/v0.4.0.md).

## What Is Bundled

Bundled inside the installer:

- ContextControl Workbench native desktop app
- PowerShell ContextControl CLI scripts
- ContextControl `lib/` modules and PowerShell-based `skillbook/` instructions
- Release appearance defaults
- Full Windows app folder with runtime files beside the EXE
- Setup UI with install folder picker, shortcut options, uninstall registration, logs, and quiet install mode

Not bundled:

- LLM model weights
- Ollama models
- Python packages for model backends
- GPU drivers, CUDA toolkits, or vendor runtimes
- Chat history, project exports, patch files, or local runtime state

Those are created or downloaded only after the user chooses them in the app.

If the app fails before the main window opens, it writes a crash log beside the installed EXE and to `%LOCALAPPDATA%\ContextControl\workbench-crash.log`.

## Current Status

Stable enough to test:

- Windows x64 installer
- Project file tree and project scanner views
- Local LLM catalog
- Dependency detection and one-click installers where safe
- Ollama model pull/remove workflow
- Basic local chat through supported chat-ready models
- Image generation through Diffusers models and the stable-diffusion.cpp runner route on Windows; experimental Ollama image models are cataloged but macOS-only
- Codex prompt mode through Codex CLI after the user logs in from **View -> Settings -> LLMs**
- Startup and manual GitHub release update checks
- Theme and appearance settings

Work in progress:

- Context Control prompting flow in the desktop app
- Full per-phase activation of custom Skillbook flows
- Non-Windows packaged releases
- Some advanced GPU/server model backends

The CLI scripts remain the conservative path for the original DIR/CC/GO patch pipeline while the desktop prompting flow matures.

Codex mode requires the Codex CLI. If Codex mode is selected before setup, the prompt is locked and shows whether Codex needs to be installed or logged in. Use **View -> Settings -> LLMs -> Codex CLI** to Install, Guide, Login, Refresh, Doctor, or Logout. Install is best-effort: Windows uses the Codex `winget` package, while macOS/Linux open the official standalone installer route; the Guide button is the fallback when package managers, admin policy, network, or PATH refresh block automation. Codex credentials are owned by the Codex CLI; ContextControl only checks status and opens the setup/login/logout commands.

## Local LLM And Dependency Install

The app separates dependencies from model weights.

Dependencies are runtimes and libraries such as Ollama, llama.cpp, Python packages, or backend servers. Models are the actual weights, usually much larger. ContextControl does not download large model weights during app install.

One-click dependency install currently covers **17/17** dependency cards shown by the app. These are installer buttons for runtimes/backends, not model weights:

| Category | Autosetup dependencies |
|---|---|
| Package manager apps | Ollama, LM Studio, Python 3.12 bootstrap for managed Python backends |
| Managed Python environments | Diffusers, Transformers, MLX LM, MLC LLM, vLLM, SGLang, OpenVINO GenAI, ONNX Runtime GenAI, TensorRT-LLM, ExLlamaV2 / TabbyAPI |
| Native portable downloads | llama.cpp server, KoboldCpp, stable-diffusion.cpp, RWKV Runner |
| Source archive setup | bitnet.cpp source checkout |

On a fresh Windows PC, ContextControl ignores the Microsoft Store `python.exe` alias in `%LOCALAPPDATA%\Microsoft\WindowsApps` because that is not a real interpreter. If no usable Python is found, installing a Python-backed dependency such as Diffusers bootstraps Python 3.12 through `winget`, then creates a ContextControl-managed virtual environment. Diffusers generation uses only ContextControl's managed venv under `%LOCALAPPDATA%\ContextControl\dependencies\python\diffusers\.venv`; it does not use or modify Python packages from the user's PATH, user site-packages, Conda, or other development environments.

Catalog-wide model autosetup coverage in the current catalog:

| Model route | Count |
|---|---:|
| Ollama local model pull | 262/302 |
| Non-Ollama managed/backend setup | 12/302 |
| Ollama Cloud entries, no local weight download | 28/302 |
| Local autosetup path, excluding cloud | 274/302 |
| Any app route, including cloud | 302/302 |
| Windows/Linux enabled routes, excluding macOS-only Ollama image models | 299/302 |

Important caveat: "autosetup" means ContextControl has a button or route for the next safe setup step. It does not mean every backend is fully hands-off after that. Large model weights, vendor drivers, CUDA/WSL setup, cloud sign-in, model licenses, and some server launch steps can still be external.

Autosetup pieces that are still WIP or partial:

| Dependency | Current state |
|---|---|
| LM Studio | App install can be started through the OS package manager; enabling and managing its local server is still manual. |
| stable-diffusion.cpp | Runner install is automatic; GGUF diffusion model file selection/download is still manual through `CC_IMAGE_MODEL_PATH`. |
| bitnet.cpp | Source checkout is automatic; full environment setup and BitNet model weight flow are still WIP. |
| RWKV Runner | Runner download is automatic; RWKV model weights and launch integration are still WIP. |
| MLX LM | Python package setup exists, but it is useful only on Apple Silicon/macOS. |
| MLC LLM | Package setup exists; compiled model artifacts and target-specific runtime flow are still WIP. |
| vLLM, SGLang | Python package setup exists; CUDA/WSL/server validation and model serving flow are still WIP. |
| ONNX Runtime GenAI, OpenVINO GenAI | Package setup exists; converted model artifacts and runtime wiring are still WIP. |
| TensorRT-LLM, ExLlamaV2 / TabbyAPI | Package setup exists; NVIDIA/CUDA environment, model artifacts, and server workflow are still WIP. |

Image generation status:

- 13/13 image-generation catalog entries have a route in the app.
- 3 use experimental Ollama image models: FLUX.2 Klein 4B, FLUX.2 Klein 9B, and Z-Image Turbo. Ollama currently documents these image-generation models as macOS-only, so ContextControl disables their Ollama download/use buttons on Windows/Linux to avoid raw Ollama HTTP 500/EOF failures. Already-pulled copies can still be uninstalled.
- 8 use Diffusers and expose a model-card **Download** action for Hugging Face weights after the managed Diffusers dependency passes a runtime health check. Diffusers image models are not added to the prompt model selector until both the managed dependency and local model cache validate. This includes the Windows/Linux-capable `black-forest-labs/FLUX.2-klein-4B` route for FLUX.2 Klein. Its first run is large: ContextControl now downloads only the Diffusers pipeline files, but that is still roughly 15-16 GB and the Hugging Face file counter can sit on one percentage while a multi-GB shard downloads. Fresh Diffusers installs request `diffusers>=0.38.0` so the Klein pipeline is available. Add a personal Hugging Face token in **View -> Settings -> LLMs** to avoid anonymous Hub rate limits during large downloads.
- 2 use stable-diffusion.cpp and still need the user to point `CC_IMAGE_MODEL_PATH` at a local GGUF diffusion model file.

When no HF token is configured, ContextControl warns on each Hugging Face-backed Diffusers model card, logs a warning when one becomes the selected image-generation model, and repeats the warning before model download/generation. **View -> Settings -> LLMs** contains a visible token field and a **Tutorial** button with the exact steps for creating a Read or fine-grained read token from the Hugging Face Access Tokens page.

HF token warnings currently apply to these Diffusers routes:

```text
runwayml/stable-diffusion-v1-5
stabilityai/stable-diffusion-2-1-base
segmind/tiny-sd
nota-ai/bk-sdm-small
SimianLuo/LCM_Dreamshaper_v7
stabilityai/sd-turbo
segmind/SSD-1B
black-forest-labs/FLUX.2-klein-4B
```

The default image-generation selection is Tiny Stable Diffusion (`segmind/tiny-sd`) because it is small enough for fresh Windows installs and is useful for validating that Python, Torch, and Diffusers are working before downloading larger checkpoints. For FLUX.2 Klein on Windows, use the Diffusers entry, not the `x/flux2-klein` Ollama entry. The terminal echoes the exact prompt being generated, reports whether Hugging Face downloads are authenticated, and prints keepalive status while first-run Diffusers downloads or CPU-offload loading are quiet.

ContextControl validates the managed Diffusers runtime before download, cache detection, and generation by importing PyTorch and the shared Diffusers packages in a timed health check. FLUX.2 Klein has an additional model-specific check for `Flux2KleinPipeline`; that check gates only Klein, so a Klein package issue will not hide Tiny Stable Diffusion or the other SD/LCM Diffusers models. If a managed Diffusers check fails or times out, image generation does not start and the affected model is not considered selectable. Reinstall **Hugging Face Diffusers** in Dependencies to repair it; repair deletes only the ContextControl-managed Diffusers venv and recreates it.

## Windows Download Warnings

Windows SmartScreen may warn on new unsigned installers even when the file is clean. The technical fix is Authenticode code signing with an OV/EV certificate and enough download reputation over time. The release workflow supports optional certificate-based signing through repository secrets, but public builds remain unsigned until a signing certificate is configured.

## Main Workbench Areas

- **Local LLMs**: browse models, see fit notes, pull Ollama models, and route chat/image tasks.
- **Dependencies**: detect installed backends and install managed dependencies.
- **Project Files**: open a project folder and inspect source structure.
- **Project Graph**: visualize project structure and export graph views.
- **Scanner**: summarize project stack, languages, manifests, and important files.
- **Conversation**: use local model chat with ContextControl context where supported.
- **Skillbook**: inspect and edit the CC Main and CC Flow instructions, or organize custom flows, sections, and skills.
- **Browser**: embedded WebView2 surface on Windows.

## Context Control Prompting Flow

The original ContextControl pipeline is deterministic:

1. `ccDir.ps1` exports a filtered project tree.
2. `cc.ps1` exports selected files or functions.
3. `ccReplace.ps1` applies explicit `CC-REPLACE` patch blocks.

The desktop app uses the same sequence: **DIR -> Send -> CC -> Send -> GO -> Apply**. GO previews a plan with `ccReplace.ps1 -PlanOnly -Json`; Apply executes the selected actions locally. The prompting flow is still under active development.

## Skillbook

The Skillbook section turns the PowerShell workflow into visible model instructions. Open **Skillbook -> Context Control** to inspect **CC Main**, the shared operating rules, and **CC Flow**, the instructions for each kind of attached context.

| Skill or step | PowerShell basis | Expected result |
|---|---|---|
| [CC Main](skillbook/built-in-overrides/cc-main/cc-main.md) | The complete DIR/CC/GO pipeline | The model reasons from attached exports; ContextControl runs local file operations. |
| [DIR + Request](skillbook/built-in-overrides/cc-flow/cc-flow-01-dir-request.md) | [ccDir.ps1](ccDir.ps1) | A minimal file/function request ending with `END`, or a focused `FIND:` / `EXPAND:` request. |
| [CC Export + Patch](skillbook/built-in-overrides/cc-flow/cc-flow-02-cc-patch.md) | [cc.ps1](cc.ps1) and [ccReplace.ps1](ccReplace.ps1) | Another narrow context request, or raw `CC-REPLACE` blocks ready for GO. |
| [Chat](skillbook/built-in-overrides/cc-flow/cc-flow-03-chat.md) | No DIR/CC source attachment | A normal conversational answer; project code work starts with DIR. |
| GO / Apply | [ccReplace.ps1](ccReplace.ps1); [ccStart.ps1](ccStart.ps1) starts its terminal watcher | A local patch preview and application, with no model prompt. |

Built-in skills open read-only. Select **Edit** to change an instruction, then **Save** to persist its markdown override under `skillbook/built-in-overrides/`. Custom flows support adding and renaming flows, sections, and skills, enabling/disabling skills, and saving markdown under `skillbook/flows/<flow-id>/sections/<section-id>/skills/`.

CC Main and the active CC Flow instruction are included in ContextControl model turns. Raw mode sends the prompt without Skillbook instructions. The page's flow inspector shows the attachments and instruction injection for each step. Full per-phase activation of arbitrary custom flows remains work in progress.

See the [Skillbook guide](docs/SKILLBOOK.md) for file layout, editing, and a PowerShell walkthrough, and the [flow reference](CONTEXT_CONTROL_FLOW_REFERENCE.md) for the complete request and patch contracts.

## Build From Source

Requirements:

- Windows for the release installer EXE
- .NET 9 SDK
- PowerShell
- CMake and a C++17 compiler for the native exporter (optional; the app can fall back to PowerShell)

Run the tests:

```powershell
dotnet run --project .\ide\ContextControl.Workbench.Tests\ContextControl.Workbench.Tests.csproj --configuration Release
```

Run the app from source:

```powershell
dotnet run --project .\ide\ContextControl.Workbench\ContextControl.Workbench.csproj
```

Build release artifacts:

```powershell
.\packaging\Publish-ContextControlRelease.ps1
```

Output goes to:

```text
.tmp\release\
```

The GitHub Actions workflow in `.github/workflows/contextcontrol-release.yml` publishes the Windows installer when a `v*` tag is pushed. The release script also creates a local app-folder zip as the installer payload and for developer smoke testing, but end users only need the setup EXE.

## Repository Layout

```text
.github/workflows/                 Release workflow
docs/SKILLBOOK.md                  Skills and PowerShell workflow guide
ide/ContextControl.Workbench/       Avalonia desktop app
ide/ContextControl.Workbench.Tests/ Focused smoke tests
lib/                                Shared PowerShell pipeline modules
native/contextcontrol/             Native DIR/CC exporter
packaging/                          Release and installer scripts
skillbook/built-in-overrides/        CC Main and CC Flow markdown instructions
skillbook/flows/                     User-created flows, sections, and skills
cc*.ps1, cc*.cmd                    CLI entry points
```

Ignored local runtime files include `.tmp/`, `.ccReplace.versions/`, `.ccWorkbench.*`, generated exports, patch files, build output, and user settings.

## Privacy Model

ContextControl is local-first. The app scans projects and runs local child processes on the user's machine. Model weights and dependency runtimes are installed only after the user chooses them. Local LLM backends are separate programs, so review each backend before installing it.
