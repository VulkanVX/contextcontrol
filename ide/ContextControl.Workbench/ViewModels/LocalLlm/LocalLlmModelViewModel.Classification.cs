// CC-DESC: Local LLM model classification and backend requirement helpers.

// CC-DESC: Presents a local Ollama model candidate with fit, install, and pull state.

using System.Globalization;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using ContextControl.Workbench.Services;

namespace ContextControl.Workbench.ViewModels;

public sealed partial class LocalLlmModelViewModel
{
    public void ApplyBackendDependencyState(bool isBackendDependencyReady)
    {
        IsBackendDependencyReady = isBackendDependencyReady;
        if (RequiresManualBackend)
        {
            IsAvailable = IsInstalled || CanUseManualBackend;
        }
    }

    public void ApplyBackendModelState(bool isBackendModelReady)
    {
        IsBackendModelReady = isBackendModelReady;
        if (RequiresManualBackend)
        {
            IsAvailable = IsInstalled || CanUseManualBackend;
        }
    }

    private static bool IsCloudModelId(string id)
    {
        return id.Contains(":cloud", StringComparison.OrdinalIgnoreCase)
            || id.EndsWith("-cloud", StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatVram(double vram)
    {
        if (vram <= 0)
        {
            return "CPU";
        }

        return Math.Abs(vram - Math.Round(vram)) < 0.05
            ? $"{vram:0} GB"
            : $"{vram:0.#} GB";
    }

    private static bool SupportsThinkingById(string id)
    {
        return id.Contains("deepseek", StringComparison.OrdinalIgnoreCase)
            || id.Contains("qwen3", StringComparison.OrdinalIgnoreCase)
            || id.Contains("reasoning", StringComparison.OrdinalIgnoreCase)
            || id.Contains("r1", StringComparison.OrdinalIgnoreCase)
            || id.Contains("gpt-oss", StringComparison.OrdinalIgnoreCase)
            || id.Contains("magistral", StringComparison.OrdinalIgnoreCase)
            || id.Contains("mistral-medium-3.5", StringComparison.OrdinalIgnoreCase)
            || id.Contains("lfm2.5-thinking", StringComparison.OrdinalIgnoreCase)
            || id.Contains("nemotron-3-nano", StringComparison.OrdinalIgnoreCase)
            || id.Contains("nemotron-3-super", StringComparison.OrdinalIgnoreCase)
            || id.Contains("minimax-m2", StringComparison.OrdinalIgnoreCase)
            || id.Contains("kimi-k2", StringComparison.OrdinalIgnoreCase)
            || id.Contains("glm-", StringComparison.OrdinalIgnoreCase)
            || id.Contains("gemma4", StringComparison.OrdinalIgnoreCase)
            || id.Contains("qwq", StringComparison.OrdinalIgnoreCase)
            || id.Contains("cogito", StringComparison.OrdinalIgnoreCase)
            || id.Contains("deepcoder", StringComparison.OrdinalIgnoreCase)
            || id.Contains("exaone-deep", StringComparison.OrdinalIgnoreCase)
            || id.Contains("EXAONE-4.0", StringComparison.OrdinalIgnoreCase)
            || id.Contains("olmo-3", StringComparison.OrdinalIgnoreCase)
            || id.Contains("olmo-3.1", StringComparison.OrdinalIgnoreCase)
            || id.Contains("laguna-xs.2", StringComparison.OrdinalIgnoreCase)
            || id.Contains("think", StringComparison.OrdinalIgnoreCase);
    }

    private static bool DetectImageModel(LocalLlmCatalogModel model)
    {
        var text = BuildClassificationText(model);
        return ContainsAny(
            text,
            "qwen2.5vl",
            "qwen3-vl",
            "qwen-vl",
            "qwen2.5-omni",
            "omni",
            "minicpm-v",
            "llama3.2-vision",
            "vision",
            "vlm",
            "visual",
            "screenshot",
            "ocr",
            "image-aware",
            "image-grounded",
            "image/text",
            "medical text/image",
            "multimodal");
    }

    private static bool DetectImageGenerationModel(LocalLlmCatalogModel model)
    {
        var text = BuildClassificationText(model);
        return ContainsAny(
            text,
            "x/z-image",
            "z-image-turbo",
            "z image turbo",
            "x/flux1",
            "flux.1",
            "flux1",
            "bk-sdm",
            "lcm_dreamshaper",
            "sd-turbo",
            "sd turbo",
            "flux.2",
            "flux 2",
            "flux-2",
            "x/flux2-klein",
            "flux2-klein",
            "flux klein",
            "stable diffusion",
            "sdxl",
            "image generation",
            "image-generation",
            "text-to-image",
            "text to image",
            "prompt-to-image",
            "generate images",
            "generates images",
            "diffusion");
    }

    private static bool DetectOllamaChatBlocked(LocalLlmCatalogModel model)
    {
        var text = BuildClassificationText(model);
        return ContainsAny(text, "ollama chat blocked", "ollama completion-only", "not cc chat-ready");
    }

    private static IReadOnlyList<string> ResolvePurposeTags(LocalLlmCatalogModel model)
    {
        var text = BuildClassificationText(model);
        var tags = new List<string>();
        var isImageGeneration = DetectImageGenerationModel(model);

        AddTagIf(tags, isImageGeneration, "ImageGen");
        AddTagIf(tags, DetectImageModel(model), "Image");
        AddTagIf(
            tags,
            ContainsAny(text, "coder", "coding", "code", "codestral", "devstral", "starcoder", "opencoder", "codegeex", "repository", "refactor", "patch", "completion"),
            "Coding");
        AddTagIf(
            tags,
            SupportsThinkingById(model.Id)
                || ContainsAny(text, "reason", "reasoning", "math", "logic", "planning", "critique", "review", "qwq", "r1", "think"),
            "Reasoning");
        AddTagIf(
            tags,
            ContainsAny(text, "tool", "function", "agent", "agentic", "json", "structured"),
            "Tool use");
        AddTagIf(
            tags,
            ContainsAny(text, "rag", "retrieval", "citation", "citations", "document", "documents", "long-document"),
            "RAG");
        AddTagIf(
            tags,
            ContainsAny(text, "translate", "translation", "multilingual", "bilingual", "chinese", "korean", "arabic", "sea", "language"),
            "Multilingual");
        AddTagIf(
            tags,
            ContainsAny(text, "medical", "healthcare", "clinical"),
            "Medical");
        AddTagIf(
            tags,
            ContainsAny(text, "rwkv", "bitnet", "research", "experimental", "baseline", "transparent", "alternative architecture"),
            "Research");
        AddTagIf(
            tags,
            ContextCapsuleBuilder.EstimateContextTokens(model.AdvertisedContext, 0) >= 100_000
                || ContainsAny(text, "long-context", "long context"),
            "Long context");

        if (!isImageGeneration
            && (tags.Count == 0 || ContainsAny(text, "chat", "assistant", "instruction", "general")))
        {
            AddTagIf(tags, true, "Chat");
        }

        return tags.Count <= 5 ? tags : tags.Take(5).ToArray();
    }

    private static void AddTagIf(ICollection<string> tags, bool condition, string tag)
    {
        if (condition && !tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
        {
            tags.Add(tag);
        }
    }

    private static string ResolveModelBaseLabel(LocalLlmCatalogModel model)
    {
        var text = BuildClassificationText(model);
        if (DetectImageGenerationModel(model))
        {
            return "ImageGen";
        }

        if (ContainsAny(text, "embedding", "embed", "bge-m3", "nomic-embed", "jina-embeddings"))
        {
            return "Embedding";
        }

        if (ContainsAny(text, "reranker", "rerank"))
        {
            return "Reranker";
        }

        if (ContainsAny(text, "minicpm-o", "omni", "audio", "speech", "voice"))
        {
            return "Omni-modal";
        }

        if (ContainsAny(text, "qwen-vl", "qwen3-vl", "minicpm-v", "fastvlm", "llava", "bakllava", "vision", "vlm", "visual", "screenshot", "ocr", "image", "video"))
        {
            return "VLM";
        }

        if (ContainsAny(text, "rwkv"))
        {
            return "RWKV";
        }

        if (ContainsAny(text, "bitnet", "1.58-bit", "1-bit"))
        {
            return "BitNet";
        }

        if (ContainsAny(text, "falcon-h1", "jamba", "granite 4 hybrid", "granite4-h", "lfm", "liquid", "hybrid ssm", "ssm"))
        {
            return "Hybrid LLM";
        }

        if (ContainsAny(text, "moe", "mixtral", "-a3b", "-a22b", "a3b", "a22b", "active parameters", "active-parameter"))
        {
            return "MoE LLM";
        }

        return "LLM";
    }

    private static string ResolveModelBaseDetail(LocalLlmCatalogModel model)
    {
        return ResolveModelBaseLabel(model) switch
        {
            "ImageGen" => "Text-to-image generation model; prompts produce image files rather than chat answers.",
            "Embedding" => "Encoder model for vector search and retrieval, not chat generation.",
            "Reranker" => "Ranking model for ordering retrieval candidates, not normal chat.",
            "VLM" => "Vision-language model for text plus image, screenshot, OCR, or video-style inputs.",
            "Omni-modal" => "Multimodal model family aimed at text plus audio/video interaction.",
            "RWKV" => "Attention-free recurrent language model; use RWKV-specific runners when needed.",
            "BitNet" => "Native ultra-low-bit language model; use BitNet-specific runtime when needed.",
            "Hybrid LLM" => "Language model using hybrid attention/SSM or similar efficient architecture.",
            "MoE LLM" => "Mixture-of-experts language model with sparse active parameters.",
            _ => "Transformer-style text language model for chat, code, reasoning, or tool use."
        };
    }

    private static string ResolveBackendRequirementLabel(LocalLlmCatalogModel model)
    {
        if (IsCloudModelId(model.Id))
        {
            return "Ollama Cloud";
        }

        var text = BuildClassificationText(model);
        var baseLabel = ResolveModelBaseLabel(model);
        if (baseLabel.Equals("BitNet", StringComparison.OrdinalIgnoreCase))
        {
            return "bitnet.cpp";
        }

        if (baseLabel.Equals("RWKV", StringComparison.OrdinalIgnoreCase))
        {
            return IsOllamaRwkvTag(model) ? "Ollama" : "RWKV runner";
        }

        if (baseLabel.Equals("ImageGen", StringComparison.OrdinalIgnoreCase))
        {
            if (ContainsAny(text, "gguf", "stable-diffusion.cpp", "comfyui"))
            {
                return "stable-diffusion.cpp";
            }

            if (ContainsAny(text, "runwayml/", "stabilityai/", "segmind/", "nota-ai/", "simianluo/", "black-forest-labs/"))
            {
                return "Diffusers";
            }

            return "Ollama image";
        }

        if (model.Id.StartsWith("hf.co/", StringComparison.OrdinalIgnoreCase))
        {
            return "Ollama/GGUF";
        }

        if (ContainsAny(text, "fastvlm", "litert"))
        {
            return "Apple/LiteRT";
        }

        if (baseLabel.Equals("Omni-modal", StringComparison.OrdinalIgnoreCase))
        {
            return "Transformers";
        }

        if (baseLabel.Equals("VLM", StringComparison.OrdinalIgnoreCase))
        {
            return IsOllamaHostedCandidate(model.Id)
                || ContainsAny(text, "llava", "bakllava", "gemma3", "gemma 3", "minicpm-v", "qwen2.5vl", "qwen3-vl")
                ? "Ollama/VLM"
                : "Transformers/vLLM";
        }

        if (baseLabel.Equals("Embedding", StringComparison.OrdinalIgnoreCase)
            || baseLabel.Equals("Reranker", StringComparison.OrdinalIgnoreCase))
        {
            return ContainsAny(text, "nomic-embed", "mxbai-embed", "snowflake-arctic-embed", "all-minilm")
                ? "Ollama"
                : "Transformers";
        }

        if (DetectOllamaChatBlocked(model))
        {
            return "Transformers";
        }

        if (ContainsAny(text, "gguf", "llama.cpp", "lm studio", "koboldcpp"))
        {
            return "Ollama/GGUF";
        }

        if (IsOllamaHostedCandidate(model.Id))
        {
            return "Ollama";
        }

        if (ContainsAny(
                text,
                "qwen3-coder-next",
                "235b",
                "405b",
                "80b",
                "devstral",
                "nemotron",
                "kimi-k2",
                "minimax-m2",
                "glm-4.5"))
        {
            return "vLLM/SGLang";
        }

        if (baseLabel.Equals("Hybrid LLM", StringComparison.OrdinalIgnoreCase)
            && ContainsAny(text, "lfm", "liquid", "falcon-h1", "granite 4", "jamba"))
        {
            return "Transformers";
        }

        return "Ollama";
    }

    private static string ResolveBackendRequirementDetail(LocalLlmCatalogModel model)
    {
        return ResolveBackendRequirementLabel(model) switch
        {
            "Ollama Cloud" => "Requires Ollama Desktop/API plus cloud access; the model is not stored as a local download.",
            "Ollama" => "Requires Ollama. Pull the model, then chat through the local Ollama API.",
            "Ollama image" => "Requires Ollama image generation support; currently macOS-only and disabled by ContextControl on Windows/Linux.",
            "Diffusers" => "Requires Python with diffusers, huggingface-hub, transformers, torch, accelerate, safetensors, and Pillow for Hugging Face image checkpoints.",
            "stable-diffusion.cpp" => "Requires stable-diffusion.cpp or another GGUF diffusion runner for quantized SD/FLUX files.",
            "Ollama/GGUF" => "Runs through Ollama when a tag exists, or through a GGUF backend such as llama.cpp, LM Studio, or KoboldCpp.",
            "Ollama/VLM" => "Use Ollama when the vision tag is available; otherwise use a VLM-capable Transformers/vLLM route.",
            "Transformers" => "Requires a Hugging Face Transformers-compatible Python runtime or wrapper.",
            "Transformers/vLLM" => "Requires Transformers for local experimentation, or vLLM/SGLang for server-style GPU serving.",
            "vLLM/SGLang" => "Best served by a GPU/server backend such as vLLM or SGLang, especially for large or agentic models.",
            "bitnet.cpp" => "Requires the BitNet runtime; do not treat this as a normal quantized Ollama model.",
            "RWKV runner" => "Requires an RWKV-specific runner or compatible special library.",
            "Apple/LiteRT" => "Requires the Apple or LiteRT route for efficient vision-language inference.",
            _ => "Requires a compatible local or server backend before it can be used."
        };
    }

    private static string ResolveDependencyId(string? backendRequirementLabel)
    {
        return (backendRequirementLabel ?? "").Trim() switch
        {
            "Ollama" or "Ollama/GGUF" or "Ollama/VLM" or "Ollama image" => "ollama",
            "Diffusers" => "diffusers",
            "stable-diffusion.cpp" => "stable_diffusion_cpp",
            "bitnet.cpp" => "bitnet_cpp",
            "RWKV runner" => "rwkv_runner",
            "Apple/LiteRT" => "mlx_lm",
            "Transformers" or "Transformers/vLLM" => "transformers",
            "vLLM/SGLang" => "vllm",
            _ => ""
        };
    }

    private static string BuildClassificationText(LocalLlmCatalogModel model)
    {
        return $"{model.Id} {model.DisplayName} {model.ExpectedSpeed} {model.PracticalUse}";
    }

    private static bool IsOllamaRwkvTag(LocalLlmCatalogModel model)
    {
        var id = model.Id ?? "";
        return id.StartsWith("mollysama/rwkv-", StringComparison.OrdinalIgnoreCase)
            || id.Contains("/rwkv-7-", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsOllamaHostedCandidate(string? modelId)
    {
        var id = (modelId ?? "").Trim();
        if (string.IsNullOrWhiteSpace(id) || id.StartsWith("hf.co/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !id.Contains('/', StringComparison.Ordinal)
            || id.StartsWith("LiquidAI/", StringComparison.OrdinalIgnoreCase)
            || id.StartsWith("mollysama/", StringComparison.OrdinalIgnoreCase);
    }
}
