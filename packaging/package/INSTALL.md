# ContextControl Install Guide

This is a self-contained Windows x64 build. A separate .NET runtime install is not required.

## Recommended Install

Download and run:

```text
ContextControl-win-x64-Setup.exe
```

You do not need a separate zip. The setup EXE contains the full app folder and installs it to the folder you choose.

The setup window lets you:

- choose the install folder
- create Start Menu and desktop shortcuts
- register ContextControl in Windows Installed apps / Apps & features
- optionally install Microsoft Edge WebView2 Runtime for the Browser workspace
- launch ContextControl when setup finishes

Default install folder:

```text
%LOCALAPPDATA%\Programs\ContextControl
```

Run the installed app from the Start Menu shortcut or from:

```text
<install folder>\ContextControl.Workbench.exe
```

After this version is installed, ContextControl checks GitHub releases on startup when internet is available. Use the **Check updates** button in the header bar to manually check, download, and start a newer setup EXE against the current install folder. A new release still downloads the full setup EXE once, but repeated attempts for the same release reuse the cached installer. During install, setup waits for the running app to close and then writes only changed files.

## Local LLM Autosetup

ContextControl does not bundle LLM weights or backend runtimes. Install the app first, then use the **Dependencies** and **Local LLMs** pages.

Codex mode requires the Codex CLI and an authenticated session. Open **View -> Settings -> LLMs -> Codex CLI** to Install, open the official Guide, Login, Refresh, Doctor, or Logout. Install is best-effort: Windows uses the Codex `winget` package, while macOS/Linux open the official standalone installer route; Guide is the fallback when package managers, admin policy, network, or PATH refresh block automation. If Codex mode is selected before setup, the prompt is locked until the CLI is installed and reports an authenticated session.

Current autosetup coverage:

- dependencies: 17/17 app dependency cards have an installer path
- local Ollama model pulls: 262/302 catalog entries
- non-Ollama managed/backend setup: 12/302 catalog entries
- Ollama Cloud entries: 28/302 catalog entries, no local weight download
- image generation: 13/13 image-generation catalog entries have a route; 3 experimental Ollama image entries are macOS-only and disabled on Windows/Linux, and FLUX.2 Klein 4B has a Windows-capable Diffusers route

FLUX.2 Klein Diffusers is much larger than Tiny Stable Diffusion. Its first run downloads roughly 15-16 GB of Diffusers pipeline files and can pause on the same Hugging Face percentage while one large shard downloads. Fresh Diffusers installs request `diffusers>=0.38.0` so the Klein pipeline is available. The terminal shows the exact prompt, reports whether Hugging Face downloads are authenticated, and prints keepalive status while the download/load step is quiet. For large Hugging Face models, paste a personal token in View -> Settings -> LLMs to avoid anonymous Hub rate limits.

When no HF token is configured, Diffusers model cards show an HF token warning and repeat it before download/generation. Open View -> Settings -> LLMs -> Tutorial for the guided Hugging Face token steps, then paste the token in the visible HF token field. A Read token or fine-grained token with read access is enough for model downloads.

HF token warnings currently apply to these Diffusers routes: `runwayml/stable-diffusion-v1-5`, `stabilityai/stable-diffusion-2-1-base`, `segmind/tiny-sd`, `nota-ai/bk-sdm-small`, `SimianLuo/LCM_Dreamshaper_v7`, `stabilityai/sd-turbo`, `segmind/SSD-1B`, and `black-forest-labs/FLUX.2-klein-4B`.

ContextControl validates the managed Diffusers runtime before download, cache detection, and generation by importing PyTorch and the shared Diffusers packages in a timed health check. FLUX.2 Klein has an additional model-specific check for `Flux2KleinPipeline`; that check gates only Klein, so a Klein package issue will not hide Tiny Stable Diffusion or the other SD/LCM Diffusers models. If a managed Diffusers check fails or times out, image generation does not start and the affected model is not considered selectable. Reinstall **Hugging Face Diffusers** in Dependencies to repair it; repair deletes only the ContextControl-managed Diffusers venv and recreates it.

On a raw Windows PC, the Microsoft Store `python.exe` app alias is ignored because it is not a real Python interpreter. Python-backed dependencies such as Diffusers automatically install Python 3.12 through `winget` when no usable Python exists, then create a ContextControl-managed virtual environment. Diffusers generation uses only ContextControl's managed venv under `%LOCALAPPDATA%\ContextControl\dependencies\python\diffusers\.venv`; it does not use or modify Python packages from the user's PATH, user site-packages, Conda, or other development environments.

Still partial/WIP after the first install click: LM Studio server enablement, stable-diffusion.cpp GGUF model file selection through `CC_IMAGE_MODEL_PATH`, bitnet.cpp environment/model setup, RWKV model weight flow, CUDA/WSL/server validation for vLLM/SGLang/TensorRT-LLM/ExLlamaV2, and converted model artifacts for ONNX Runtime GenAI/OpenVINO GenAI.

Setup writes logs to:

```text
<install folder>\install.log
%LOCALAPPDATA%\ContextControl\install.log
```

## Uninstall

Use one of these:

- Windows Settings -> Installed apps / Apps & features -> ContextControl -> Uninstall
- Start Menu -> ContextControl -> Uninstall ContextControl
- `<install folder>\ContextControl.Uninstall.exe /uninstall`

Quiet uninstall:

```powershell
.\ContextControl.Uninstall.exe /uninstall /quiet
```

Add `/removeUserData` to remove ContextControl logs and user data. The final uninstall log may be recreated under `%LOCALAPPDATA%\ContextControl\uninstall.log`. Local LLM model weights and third-party runtimes such as Ollama are separate programs and are not removed by the ContextControl uninstaller.

If the app fails before its window opens, it writes a crash log to:

```text
<install folder>\ContextControl.Workbench.crash.log
%LOCALAPPDATA%\ContextControl\workbench-crash.log
```

## Quiet Install

For automated testing:

```powershell
Start-Process -FilePath .\ContextControl-win-x64-Setup.exe -ArgumentList @('/quiet','/installDir=C:\Tools\ContextControl','/noLaunch') -Wait
```

Useful switches:

```text
/installDir=<path>
/desktopShortcut
/noStartMenu
/installWebView2
/noLaunch
```

## Portable Folder

If you already have a full app folder from a local release build, run:

```text
ContextControl.Workbench.exe
```

Keep the files together. The EXE needs the DLLs, runtime files, assets, and `runtimes\` folder beside it.

From a local app folder you can also run:

```powershell
.\Install-ContextControl.ps1
```

Optional script switches:

```powershell
.\Install-ContextControl.ps1 -DesktopShortcut
.\Install-ContextControl.ps1 -InstallWebView2Runtime
.\Install-ContextControl.ps1 -NoLaunch
```

GitHub's automatic source-code zip is not a portable app package. Use it only for building from source.
