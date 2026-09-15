# Local models and runtimes

## Context modes and actions

Choose a context mode beside **/ Actions** in the composer, or in **Settings → LLMs**:

| Mode | Allocation policy |
| --- | --- |
| Adaptive | Grows in buckets with prepared input and preferred thinking/output reserves, up to the Adaptive/custom ceiling. Short requests use less context. |
| Fast / Balanced / Long | Requests up to 8K / 16K / 32K; hardware adaptation may lower the result. These are capacity presets, not measured speed guarantees. |
| Maximum fit | Searches estimated available memory in 1K steps up to the known model limit; uses a conservative 32K search ceiling when that limit is unknown. |
| Custom | Uses the entered ceiling, up to 1,048,576 tokens, reduced by hardware/model constraints when adaptation is enabled. |

Selecting a different mode enables automatic context and hardware adaptation. The master Auto switch and individual Context size switch can still restore manual behavior. Existing preferences are preserved; fresh-install settings use Adaptive with a 32K ceiling. GPU and CPU options retain their own switches.

Context covers input, thinking and generated output together. Adaptive uses a conservative character-based estimate (not the exact model tokenizer), adds room for the answer and reasoning, and reserves more for game code. The composer displays the selected request budget and estimated remaining space. A prompt that cannot leave even minimal answer room is rejected before sending. Workflow exporters may intentionally compact source context. Images have additional model-dependent token costs not represented by this text estimate.

The mode updates Ollama on the next request. Managed servers use the selected ceiling on their next Start; requests may budget less, but changing their live allocation requires a restart. External servers remain controlled by their own application. Hardware filters and allocation previews follow the same mode ceiling. Memory fit is an estimate, not proof of runtime support or stability. Saved speed tuning applies only when the exact context and other measured conditions match.

See [Chat actions](CHAT_ACTIONS.md) for explicit Google research, game creation, images and project coding.

## Game Lab and long generations

Turn on **Create game** in the Local composer, or ask it to create a browser game. Completed HTML games gain a **Game Lab** button. The lab supports inline HTML/CSS/JavaScript and simple answers split into HTML, CSS and JavaScript blocks. It provides desktop/mobile previews, editable source, Run/Restart/Stop, screenshots and an error console. **Improve / fix with model** prepares a draft in the original chat for review; it does not automatically send it. Other languages and engine projects use Create Project / file export.

From 0.6.0, `/game` explicitly selects this mode. Automatic final-code checking runs startup/input tests, attempts up to two model repairs on observed errors, and reruns corrections. Versions and validation reports are preserved. Disable automatic checking in Settings → LLMs. A passed smoke check still requires a gameplay test.

Local Ollama and compatible chat streams have no total or idle-duration deadline. Loading, thinking and generation can continue as long as needed. Stop remains available, runtime errors are shown, and partial responses are kept but marked incomplete. Managed runtimes also wait for readiness while their owned process is alive. This does not remove model context/output limits or change a server's own limits.

ContextControl 0.5.0 includes 544 catalog entries and discovers additional models from enabled compatible servers. The metadata snapshot covers 443 verified tags across 240 families from the [official Ollama library](https://ollama.com/library?sort=newest), checked on September 14, 2026. The curated catalog also includes Hugging Face GGUF, image generation and other specialized routes. These are catalog entries, not a claim that every model has been run or that every license is open source.

New coverage includes Granite 4.2, Qwen 3.8 and Flash Next, Ornith 1.5, Nemotron 3.5 Lightning, Muse Glimmer, Laguna 2.1, North Mini Code, LFM 2.5, and MiniCPM V4.6. Hosted-only tags remain labeled as cloud models. Source pages and published download sizes accompany discovered entries; memory estimates include overhead and do not assume that MoE active parameter counts equal weight memory.

## Large downloads and resuming

From 0.5.7, Ollama downloads use the local daemon's structured `/api/pull` stream. There is no total-duration cutoff while bytes or download stages advance. Five minutes without progress is treated as a stall, with up to two automatic resume attempts. Network interruptions and transient server failures also resume; cancellation and permanent errors stop immediately. Cached partial blobs remain owned by Ollama and are never deleted by ContextControl.

The UI shows the actual server error instead of the CLI's initial spinner output. Download speed is calculated from new bytes, excluding data already cached before resuming. Retry the same model to continue an interrupted download; ensure the local Ollama service is running. A success response is required before the app marks the model ready. See [Ollama's pull API](https://github.com/ollama/ollama/blob/main/docs/api.md#pull-a-model).

## Measured speed tuning

In 0.5.6, choose an installed local Ollama chat model in **Settings → LLMs → Resources** (also in the LLM catalog), enable **Auto adapt**, **Auto CPU threads** and **Auto GPU**, then click **Tune speed**. Use **Cancel** to stop. Tuning can take up to 15 minutes; it refuses active chats/downloads and stops when this app starts a chat or transfer. It does not detect requests made by unrelated applications, so keep other inference clients idle while measuring.

The tuner compares the ordinary Auto thread count with half the physical cores, all physical cores and all logical processors. If the installed model already enables speculative decoding through `draft_num_predict`, it also compares draft lengths 0, 2 and 8 against the model default. It keeps Ollama's dynamic GPU placement and the selected context unchanged. Each configuration is warmed first. Decode throughput comes from Ollama's final token counts and evaluation duration; first-text latency is also measured.

A sweep winner is checked against the baseline in alternating order on code and prose prompts. Both checks must improve by at least 3%, combined throughput by at least 5%, without a large first-text regression. A separate arithmetic answer check must pass. Early/truncated output, unstable allocation within the same configuration, cancellation or runtime errors cannot save a new override. These are short synthetic workload checks, not a proof of maximum performance or exhaustive answer quality.

**Use measured speed settings** can disable saved overrides. They apply to subsequent text chats only when Auto threads/GPU remain enabled and model digest, Ollama version, hardware, context and the already loaded GPU allocation match. From 0.5.8, either the verified baseline or tuned allocation can match: draft length can legitimately change how much memory Ollama offloads. Repeated measurements must remain stable for each individual configuration. A cold model first uses normal Auto settings. Changed context, model updates, unknown GPU placement or runtime updates fall back to Auto; retune after such changes or power/driver changes. Benchmark temperature, seed and thinking controls never replace the user's chat settings. No weight downloads, quantization changes, server-wide changes or forced model unloads are performed. Managed llama.cpp, KoboldCpp and external runtimes retain their existing resource adaptation; this measured tuner currently targets Ollama.

Local validation on a Ryzen 5 5600H / RTX 3050 Ti 4 GiB / 64 GiB RAM with Ollama 0.34.0 found 74.3 tok/s baseline versus 75.2 tok/s candidate for Granite 3.3 2B at 8,192 context. The small gain failed the repeatability threshold, so defaults were retained. After completing its download, Qwen 3.8 27B at 32,768 context improved from 1.84 to 2.71 tok/s (47%) across paired warm code/prose tests, using 12 CPU threads and two draft tokens instead of the Auto baseline's five threads and model default of four drafts. Both comparisons and the answer check passed. These measurements cover warm decoding on those prompts, not total answer latency or a theoretical maximum.

Run deterministic checks with `--performance-regression` on the Workbench test executable. For an intentionally idle local runtime, `--performance-live <installed-model-id> <report.json> [context-tokens]` runs the bounded experiment (default 8,192 context) and saves a report without changing application preferences.

The runtime controls are documented by [Ollama](https://github.com/ollama/ollama/blob/main/docs/modelfile.mdx). Speculation can help or hurt depending on acceptance and verification cost; it is measured rather than assumed faster.

## Auto adapt to hardware

Enable **Settings → LLMs → Auto adapt to hardware**, or use **Auto adapt** above the catalog. It is off by default for existing installations. The master switch and the individual GPU layers, context size and CPU threads switches are saved. Each managed runtime can opt out. Switching off restores use of the saved manual values; automatic allocations never overwrite them.

| Runtime | Automatic settings | When applied |
| --- | --- | --- |
| Ollama | Native dynamic GPU placement, CPU threads, bounded context | New requests; context is budgeted before assembling research and attachments |
| Managed llama.cpp / KoboldCpp | GGUF metadata based GPU layers, context, CPU threads | Next Start; **Preview allocation** reads metadata without loading or downloading weights |
| Managed Transformers | CPU threads and conservative context | Next Start; this bridge remains CPU-only and checkpoint memory is unverified |
| External servers, specialized image runtimes | Their own allocation controls | Configure in their runtime; ContextControl does not remotely change the server |

The planner uses available system RAM, detected VRAM (free VRAM when NVIDIA reports it), physical CPU cores, model weight size, and GGUF layer/KV-cache metadata where available. It keeps configurable RAM and VRAM reserves and lowers context if the estimate does not fit. Catalog-only estimates use a conservative KV allowance. Unknown model sizes are not positive fits. Split GGUF files currently require manual configuration. Unknown free VRAM uses a conservative fraction of total VRAM; shared system memory is not added as extra VRAM.

With Auto on, **Adapted fit** includes estimated CPU/RAM and CPU+GPU operation. The VRAM threshold filters use estimated allocation instead of the model's original full-GPU recommendation. Rows show estimated RAM + VRAM, context and allocation type; hover for the assumptions and memory budgets. Hosted, external and specialized runtimes are labeled separately and remain available with **Show all**. Catalog hardware estimates refresh with **Refresh**; available RAM is checked again before preparing a new local chat. Managed starts check RAM and GPU memory again. Running generations are never reconfigured mid-response.

Auto mode is a conservative allocation policy, not an autotuning benchmark. CPU/RAM offload can enable larger models but can be slower than full GPU execution. A runtime can still reject an unsupported architecture or allocate more memory than estimated. Disabling Auto permits manual configuration. Ollama chooses its exact GPU split internally, so its actual allocation may differ from the catalog estimate. Existing loaded models and other apps can reduce the reported free memory; a filtered-out model remains selectable with **Show all**.

In 0.5.5, the explicit **Adapted GPU fit** and **Adapted CPU / RAM** filters also preview fit when Auto launch mode is off. Quantization suffixes and published download sizes are recognized throughout the catalog; ranges use their upper bound. The CPU-safe filter checks CPU-only capacity with Auto enabled, even if GPU offload is also possible.

**Headroom** is the former "Keep free" setting: available memory minus headroom gives the planner's budget. For example, 40 GiB available minus 4 GiB headroom gives 36 GiB. It does not reserve physical memory or enforce a hard allocation limit in every runtime.

The **Resources** panel in Settings and LLMs reads current RAM/VRAM every five seconds while visible. Its model picker previews any catalog entry without changing the chat model or downloading it. Runtime readings and next-allocation estimates are shown separately. Ollama reports total model allocation, VRAM and active context; its displayed CPU allocation is derived by subtraction, not process RAM. Managed servers report resident process RAM and their running context/threads/layers. Missing measurements are labeled unreported. Each loaded Ollama candidate's own reported allocation is credited toward its fit estimate; it is not credited to other models. Per-device attribution is unknown on multi-GPU systems, so aggregate VRAM is not credited there.

**Estimated maximum context** is independent of the chosen Auto target. It searches in 1K steps after headroom and respects known model limits and the runtime ceiling (currently 32K for managed starts; Ollama is checked up to the reported model limit, at most 1M). Unknown model limits are stated explicitly. More context uses additional memory and may shift work to CPU; the estimate is not a runtime allocation or compatibility guarantee. No active model is resized by opening or refreshing this panel.

The runtime arguments follow the upstream [llama.cpp server options](https://github.com/ggml-org/llama.cpp/tree/master/tools/server), [KoboldCpp options](https://github.com/LostRuins/koboldcpp), and [Ollama runner options](https://github.com/ollama/ollama/blob/main/api/types.go). No model weights are downloaded by toggling Auto or previewing an allocation.

## Connect a model server

In **LLMs**, choose **Show all** to clear search, ownership, hardware and other filters. No GB limit is applied to the catalog; downloading remains an explicit action. **Newest** follows the official library listing order before falling back to known dates, so recent entries with an unknown release date are no longer buried. A source listing rank is not presented as a release date. Hosted-only models remain visibly labeled.

Open **Settings → LLMs → Local model servers**. Enable a runtime, set its API base URL and context limit, then select **Apply and connect**. Discovered models appear in the LLM catalog and the chat model selector. Google research, streaming answers, reasoning, cancellation and usage accounting use the same chat workflow for these models.

| Runtime | Default API | Starting and loading |
| --- | --- | --- |
| Ollama | `http://127.0.0.1:11434` | Existing model download and launch flow |
| llama.cpp | `http://127.0.0.1:8080/v1` | Install/update, browse a GGUF, Start model |
| KoboldCpp | `http://127.0.0.1:5001/v1` | Install/update, browse a GGUF, Start model |
| Transformers | `http://127.0.0.1:8091/v1` | Install, enter a local checkpoint folder or Hugging Face ID, Start model |
| LM Studio / llmster | `http://127.0.0.1:1234/v1` | Start its server and load a model in LM Studio or its CLI |
| Other compatible server | `http://127.0.0.1:8000/v1` | Start in its runtime; configure this connection |

The compatible route supports `/v1/models` and streamed `/v1/chat/completions`. It can connect to suitable servers from vLLM, SGLang, LocalAI, MLX, MLC or TabbyAPI. Those integrations require the server to expose this protocol and a supported chat model; they were not all installed or tested on the release machine. Their own tools, drivers, quantizations, context sizes and platform requirements still apply.

Native Windows installation is gated for Linux-oriented vLLM, SGLang and TensorRT-LLM; use Linux/WSL where supported and connect the API. MLX LM needs Apple Silicon macOS. ONNX/OpenVINO and specialized image backends keep their existing dependency routes and require their supported model formats.

With Auto off, managed llama.cpp/KoboldCpp default to **0 GPU layers (CPU)**. Increase layers gradually if VRAM permits. Only one ContextControl-managed model runs at a time. **Stop / cancel** stops its owned process and releases memory; external servers and Ollama are not stopped. Managed servers bind only to `127.0.0.1`. The model weights are not included in the app installer.

Existing Ollama GGUF blobs can be selected through **Browse GGUF → All files**, avoiding another weight download. For a normal GGUF, select the `.gguf` file. The Transformers bridge uses an isolated CPU PyTorch environment and safetensors; it does not enable remote Python code from model repositories. Some custom architectures therefore need another runtime. This bridge currently supports text chat.

API credentials are configured by environment variable **name**; the secret value is not saved in connection settings. After changing the environment outside ContextControl, restart the app so it inherits the variable. Context settings budget prompts but do not resize an externally started server.

Compatible runtime throughput is measured over the whole request, including prompt processing. Servers can buffer tokens into chunks, so measuring only after the first visible chunk would exaggerate speed. Token counts come from the server, and absent counts remain unknown.

## Release validation

On Windows with a Ryzen 5 5600H and a 4 GB RTX 3050 Ti, real streamed answers and token counts passed for llama.cpp b10964, KoboldCpp 1.120, LM Studio/llmster 0.0.24-1, and Transformers 5.17.0 with PyTorch 2.14.0 CPU. Tests used Qwen2.5-Coder 1.5B Q4 for the three GGUF runtimes and SmolLM2 135M Instruct for Transformers. Native tests reused the existing Ollama blob; LM Studio imported one copy. No large model was downloaded for validation.

Repeatable checks:

```powershell
dotnet run --project ide/ContextControl.Workbench.Tests -c Release -- --runtime-regression
dotnet run --project ide/ContextControl.Workbench.Tests -c Release -- --runtime-live llama-cpp <path-to-GGUF>
dotnet run --project ide/ContextControl.Workbench.Tests -c Release -- --runtime-live koboldcpp <path-to-GGUF>
dotnet run --project ide/ContextControl.Workbench.Tests -c Release -- --runtime-live transformers HuggingFaceTB/SmolLM2-135M-Instruct
```

The live commands start and stop their own managed server. For an already started LM Studio server, use `--runtime-live lm-studio`. The catalog can be refreshed for a future release with `python packaging/Update-LocalModelCatalog.py`; it downloads public metadata only and refuses an unexpectedly empty index.

Runtime references: [llama.cpp server](https://github.com/ggml-org/llama.cpp/tree/master/tools/server), [KoboldCpp](https://github.com/LostRuins/koboldcpp/wiki), [LM Studio headless](https://lmstudio.ai/docs/developer/core/headless), [SmolLM2 model card](https://huggingface.co/HuggingFaceTB/SmolLM2-135M-Instruct), [vLLM requirements](https://docs.vllm.ai/en/latest/getting_started/installation/gpu/), [SGLang installation](https://docs.sglang.io/docs/get-started/install), [MLX LM](https://github.com/ml-explore/mlx-lm).
