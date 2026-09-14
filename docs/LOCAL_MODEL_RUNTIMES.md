# Local models and runtimes

ContextControl 0.5.0 includes 544 catalog entries and discovers additional models from enabled compatible servers. The metadata snapshot covers 443 verified tags across 240 families from the [official Ollama library](https://ollama.com/library?sort=newest), checked on September 14, 2026. The curated catalog also includes Hugging Face GGUF, image generation and other specialized routes. These are catalog entries, not a claim that every model has been run or that every license is open source.

New coverage includes Granite 4.2, Qwen 3.8 and Flash Next, Ornith 1.5, Nemotron 3.5 Lightning, Muse Glimmer, Laguna 2.1, North Mini Code, LFM 2.5, and MiniCPM V4.6. Hosted-only tags remain labeled as cloud models. Source pages and published download sizes accompany discovered entries; memory estimates include overhead and do not assume that MoE active parameter counts equal weight memory.

## Connect a model server

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

Managed llama.cpp/KoboldCpp default to **0 GPU layers (CPU)**. Increase layers gradually if VRAM permits. Only one ContextControl-managed model runs at a time. **Stop / cancel** stops its owned process and releases memory; external servers and Ollama are not stopped. Managed servers bind only to `127.0.0.1`. The model weights are not included in the app installer.

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
