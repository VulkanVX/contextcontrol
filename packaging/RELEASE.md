# ContextControl Release Build

Use this to create the GitHub-ready Windows package:

```powershell
.\packaging\Publish-ContextControlRelease.ps1
```

Local output:

- `.tmp\release\ContextControl-win-x64\`
- `.tmp\release\ContextControl-win-x64.zip`
- `.tmp\release\ContextControl-win-x64.zip.sha256.txt`
- `.tmp\release\ContextControl-win-x64-Setup.exe`
- `.tmp\release\ContextControl-win-x64-Setup.exe.sha256.txt`

The setup EXE is the public installer. It embeds the full staged app folder, opens a Windows installer UI, lets the user choose the install folder, creates Start Menu or desktop shortcuts, registers a per-user Windows uninstall entry, optionally installs Microsoft Edge WebView2 Runtime, keeps existing user settings during updates, writes `install.log`, and launches the app when selected.

The zip is kept as a local portable payload and smoke-test artifact. It includes:

- `ContextControl.Workbench.exe`
- runtime `.dll`, `.deps.json`, `.runtimeconfig.json`, and native build files needed beside the EXE
- `.ccWorkbench.settings.json` seeded from `packaging\release-settings.template.json`
- `Install-ContextControl.ps1`
- `Start-ContextControl.cmd`
- `INSTALL.md`
- `release-manifest.json`

Quiet install smoke test:

```powershell
Start-Process -FilePath .\.tmp\release\ContextControl-win-x64-Setup.exe -ArgumentList @('/quiet', "/installDir=$env:TEMP\ContextControlSetupTest", '/noLaunch', '/noStartMenu') -Wait
```

Quiet uninstall smoke test:

```powershell
Start-Process -FilePath "$env:TEMP\ContextControlSetupTest\ContextControl.Uninstall.exe" -ArgumentList @('/uninstall','/quiet') -Wait
```

No LLM weights, dependency runtimes, chat history, source files, tests, or build folders are bundled. The app installs dependencies and downloads local LLMs from inside the Dependencies and Local LLM pages.

The workbench checks GitHub releases on startup and exposes a header-bar **Check updates** button. If a newer release exists, the button downloads `ContextControl-win-x64-Setup.exe` and starts it with the current install folder preselected. The updater reuses an already downloaded installer for the same release, cleans stale temp update folders when possible, hands setup off only after the running Workbench process exits, and setup skips unchanged files while extracting.

Codex prompt mode is gated on Codex CLI setup and authentication. The prompt is read-only while Codex is selected but not installed or logged in, and settings expose Install, Guide, Login, Refresh, Doctor, and Logout actions. Install is best-effort: Windows uses the Codex `winget` package, while macOS/Linux open the official standalone installer route; Guide is the fallback when package managers, admin policy, network, or PATH refresh block automation. Login launches Windows PowerShell by full path inside Windows Terminal when available, avoiding `wt.exe` child-command error 2147942402 on machines where `powershell` is not resolvable by name.

Current app-side autosetup coverage documented in the README and packaged install guide:

- 17/17 dependency cards expose an installer path
- 262/302 catalog entries use local Ollama model pulls
- 12/302 catalog entries use non-Ollama managed/backend setup
- 28/302 catalog entries are Ollama Cloud entries with no local weight download
- 13/13 image-generation catalog entries have a route; 3 experimental Ollama image entries are macOS-only and disabled on Windows/Linux, and FLUX.2 Klein 4B has a Windows-capable Diffusers route

FLUX.2 Klein Diffusers first-run downloads are large. The app downloads only the Diffusers pipeline files instead of the duplicate single-file checkpoint, fresh Diffusers installs request `diffusers>=0.38.0`, the terminal echoes the exact image prompt, authenticated Hugging Face download state is shown, keepalive status is printed while Hugging Face is quiet on a multi-GB shard, and FLUX.2 gets a longer first-run timeout. Users can paste a personal Hugging Face token in View -> Settings -> LLMs; ContextControl passes it as `HF_TOKEN` and `HUGGINGFACE_HUB_TOKEN` to Diffusers subprocesses.

HF token guidance is visible in the app. Diffusers model cards show an HF token warning while no token is configured, selecting a Hugging Face-backed image model logs the warning, and View -> Settings -> LLMs includes a visible token field plus a Tutorial button that opens a step-by-step token window. The warning applies to the 8 Hugging Face-backed Diffusers routes: `runwayml/stable-diffusion-v1-5`, `stabilityai/stable-diffusion-2-1-base`, `segmind/tiny-sd`, `nota-ai/bk-sdm-small`, `SimianLuo/LCM_Dreamshaper_v7`, `stabilityai/sd-turbo`, `segmind/SSD-1B`, and `black-forest-labs/FLUX.2-klein-4B`.

Diffusers runtime validation is managed-only and split by capability. ContextControl ignores external Python environments for Diffusers generation, validates the shared managed venv by importing PyTorch and common Diffusers packages, then checks `Flux2KleinPipeline` only for the FLUX.2 Klein Diffusers model. A Klein-only pipeline issue no longer hides Tiny Stable Diffusion or other SD/LCM Diffusers entries. Repair deletes only `%LOCALAPPDATA%\ContextControl\dependencies\python\diffusers` and recreates the managed venv.

Ollama model uninstall no longer waits on the full dependency scan after `ollama rm`. The app marks the model removed, runs a short install-state refresh, preserves cached Diffusers readiness, and treats "already removed" Ollama responses as successful cleanup.

Fresh Windows Python bootstrap: managed Python dependencies ignore the Microsoft Store `python.exe` alias and install Python 3.12 through `winget` when no real interpreter is present.

SmartScreen/reputation note: the release script can Authenticode-sign the setup EXE when `CONTEXTCONTROL_SIGNING_PFX_BASE64` and `CONTEXTCONTROL_SIGNING_PFX_PASSWORD` are configured. Unsigned public builds can still show Windows reputation warnings.

Skillbook now ships as the v1 flow library UI. The built-in Context Control flow is shown as read-only **CC Flow**, project/global legacy markdown still loads, and editable project markdown-folder flows can be created, renamed, enabled/disabled, saved, and reloaded. Full per-phase custom prompt activation remains a later customization pass.

The GitHub Actions workflow `.github/workflows/contextcontrol-release.yml` builds the installer on `workflow_dispatch` and attaches the setup EXE plus checksum to releases when a `v*` tag is pushed. The zip is not required for end-user install.
