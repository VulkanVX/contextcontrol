// CC-DESC: Local LLM provider and provider-icon resolution helpers.

// CC-DESC: Presents a local Ollama model candidate with fit, install, and pull state.

using System.Globalization;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using ContextControl.Workbench.Services;

namespace ContextControl.Workbench.ViewModels;

public sealed partial class LocalLlmModelViewModel
{
    private static bool ContainsAny(string text, params string[] needles)
    {
        return needles.Any(needle => text.Contains(needle, StringComparison.OrdinalIgnoreCase));
    }

    private static string ResolveProviderIcon(string id, string displayName)
    {
        var text = $"{id} {displayName}";
        if (text.Contains("tiny", StringComparison.OrdinalIgnoreCase)
            && text.Contains("dolphin", StringComparison.OrdinalIgnoreCase))
        {
            return ModelIconBase + "ollama.png";
        }

        if (text.Contains("smallthinker", StringComparison.OrdinalIgnoreCase)
            || text.Contains("small-thinker", StringComparison.OrdinalIgnoreCase))
        {
            return ModelIconBase + "ollama-qwen.png";
        }

        return ResolveProvider(id, displayName) switch
        {
            "Qwen" => ModelIconBase + "qwen.png",
            "01.AI Yi" => ModelIconBase + "provider-yi.png",
            "Black Forest Labs" => ModelIconBase + "blackforestlabs.png",
            "Microsoft BitNet" => ModelIconBase + "microsoft.png",
            "Microsoft Phi" => ModelIconBase + "phi4.png",
            "Microsoft WizardLM" => ModelIconBase + "ollama.png",
            "IBM Granite" => ModelIconBase + "granite3.png",
            "Meta Llama" => ModelIconBase + "provider-llama.png",
            "Groq Tool Use" => ModelIconBase + "provider-groq.png",
            "Google Gemma" => ModelIconBase + "provider-gemma.png",
            "Liquid AI" => ModelIconBase + "provider-liquid-ai.png",
            "DeepSeek" => ModelIconBase + "provider-deepseek.png",
            "Agentica DeepCoder" => ModelIconBase + "provider-deepcoder.png",
            "Deep Cogito" => ModelIconBase + "provider-cogito.png",
            "Essential AI" => ModelIconBase + "provider-essential-ai.png",
            "Moonshot Kimi" => ModelIconBase + "provider-kimi.png",
            "MiniMax" => ModelIconBase + "provider-minimax.png",
            "Baidu ERNIE" => ModelIconBase + "provider-ernie.png",
            "Tencent Hunyuan" => ModelIconBase + "provider-hunyuan.png",
            "Mistral AI" => ModelIconBase + "provider-mistral.png",
            "OpenAI OSS" => ModelIconBase + "provider-openai-oss.png",
            "Z.ai GLM" => ModelIconBase + "provider-glm.png",
            "NVIDIA" => ModelIconBase + "provider-nvidia.png",
            "Cohere" => ModelIconBase + "provider-cohere.png",
            "AI2 OLMo" => ModelIconBase + "provider-ai2-olmo.png",
            "TII Falcon" => ModelIconBase + "provider-falcon.png",
            "InternLM" => ModelIconBase + "provider-internlm.png",
            "Zhipu CodeGeeX" => ModelIconBase + "provider-codegeex.png",
            "OpenCoder" => ModelIconBase + "provider-opencoder.png",
            "OpenBMB" => ModelIconBase + "provider-openbmb.png",
            "Upstage Solar" => ModelIconBase + "provider-solar.png",
            "SeaLLMs" => ModelIconBase + "provider-seallms.png",
            "AI Singapore SEA-LION" => ModelIconBase + "provider-sealion.png",
            "AI Singapore MERaLiON" => ModelIconBase + "provider-meralion.png",
            "Sailor SEA" => ModelIconBase + "sailor2.png",
            "Swiss AI Apertus" => ModelIconBase + "swissao.png",
            "AI21 Jamba" => ModelIconBase + "provider-ai21.png",
            "Databricks" => ModelIconBase + "provider-databricks.png",
            "Nous Hermes" => ModelIconBase + "provider-nous.png",
            "Poolside Laguna" => ModelIconBase + "provider-laguna.png",
            "BigCode" => ModelIconBase + "provider-bigcode.png",
            "Hugging Face" => ModelIconBase + "provider-huggingface.png",
            "PowerInfer" => ModelIconBase + "provider-powerinfer.png",
            "LG EXAONE" => ModelIconBase + "provider-exaone.png",
            "H2O.ai" => ModelIconBase + "provider-h2o.png",
            "TinyLlama" => ModelIconBase + "provider-tinyllama.png",
            "Cognitive Computations" => ModelIconBase + "dolphin3.png",
            "Teknium OpenHermes" => ModelIconBase + "provider-teknium.png",
            "Orca Mini" => ModelIconBase + "ollama.png",
            "RWKV" => ModelIconBase + "provider-rwkv.png",
            "OpenThinker" => ModelIconBase + "ollama-qwen.png",
            "OpenChat" => ModelIconBase + "provider-openchat.png",
            "Intel Neural Chat" => ModelIconBase + "provider-intel.png",
            "Stability AI" => ModelIconBase + "provider-stability.png",
            "Nexusflow Athene" => ModelIconBase + "provider-athene.png",
            _ => ModelIconBase + "provider-other.png"
        };
    }

    private static string ResolveProvider(string id, string displayName)
    {
        var text = $"{id} {displayName}";
        if (text.Contains("flux.2", StringComparison.OrdinalIgnoreCase)
            || text.Contains("flux2", StringComparison.OrdinalIgnoreCase)
            || text.Contains("flux1", StringComparison.OrdinalIgnoreCase)
            || text.Contains("klein", StringComparison.OrdinalIgnoreCase))
        {
            return "Black Forest Labs";
        }

        if (text.Contains("tinyllama", StringComparison.OrdinalIgnoreCase))
        {
            return "Orca Mini";
        }

        if (text.Contains("dolphin", StringComparison.OrdinalIgnoreCase))
        {
            return "Cognitive Computations";
        }

        if (text.Contains("openhermes", StringComparison.OrdinalIgnoreCase))
        {
            return "Teknium OpenHermes";
        }

        if (text.Contains("orca-mini", StringComparison.OrdinalIgnoreCase))
        {
            return "Orca Mini";
        }

        if (text.Contains("rwkv", StringComparison.OrdinalIgnoreCase))
        {
            return "RWKV";
        }

        if (text.Contains("athene", StringComparison.OrdinalIgnoreCase))
        {
            return "Nexusflow Athene";
        }

        if (text.Contains("openthinker", StringComparison.OrdinalIgnoreCase)
            || text.Contains("open-thoughts", StringComparison.OrdinalIgnoreCase))
        {
            return "OpenThinker";
        }

        if (text.Contains("wizardlm", StringComparison.OrdinalIgnoreCase))
        {
            return "Microsoft WizardLM";
        }

        if (text.Contains("llama3-groq-tool-use", StringComparison.OrdinalIgnoreCase))
        {
            return "Groq Tool Use";
        }

        if (text.Contains("openchat", StringComparison.OrdinalIgnoreCase))
        {
            return "OpenChat";
        }

        if (text.Contains("neural-chat", StringComparison.OrdinalIgnoreCase))
        {
            return "Intel Neural Chat";
        }

        if (text.Contains("stable diffusion", StringComparison.OrdinalIgnoreCase)
            || text.Contains("stable-diffusion", StringComparison.OrdinalIgnoreCase)
            || text.Contains("sd-turbo", StringComparison.OrdinalIgnoreCase)
            || text.Contains("ssd-1b", StringComparison.OrdinalIgnoreCase)
            || text.Contains("dreamshaper", StringComparison.OrdinalIgnoreCase)
            || text.Contains("stablelm", StringComparison.OrdinalIgnoreCase)
            || text.Contains("stable-code", StringComparison.OrdinalIgnoreCase)
            || text.Contains("stabilityai", StringComparison.OrdinalIgnoreCase))
        {
            return "Stability AI";
        }

        if (text.Contains("qwen", StringComparison.OrdinalIgnoreCase))
        {
            return "Qwen";
        }

        if (text.Contains("qwq", StringComparison.OrdinalIgnoreCase))
        {
            return "Qwen";
        }

        if (text.Contains("/Yi-", StringComparison.OrdinalIgnoreCase)
            || text.Contains(" Yi ", StringComparison.OrdinalIgnoreCase)
            || text.Contains("yi-1.5", StringComparison.OrdinalIgnoreCase)
            || id.StartsWith("yi:", StringComparison.OrdinalIgnoreCase)
            || id.StartsWith("yi-coder:", StringComparison.OrdinalIgnoreCase))
        {
            return "01.AI Yi";
        }

        if (text.Contains("bitnet", StringComparison.OrdinalIgnoreCase))
        {
            return "Microsoft BitNet";
        }

        if (text.Contains("phi", StringComparison.OrdinalIgnoreCase))
        {
            return "Microsoft Phi";
        }

        if (text.Contains("granite", StringComparison.OrdinalIgnoreCase))
        {
            return "IBM Granite";
        }

        if (text.Contains("llama", StringComparison.OrdinalIgnoreCase))
        {
            return "Meta Llama";
        }

        if (text.Contains("gemma", StringComparison.OrdinalIgnoreCase))
        {
            return "Google Gemma";
        }

        if (text.Contains("lfm", StringComparison.OrdinalIgnoreCase)
            || text.Contains("liquidai", StringComparison.OrdinalIgnoreCase)
            || text.Contains("liquid ai", StringComparison.OrdinalIgnoreCase))
        {
            return "Liquid AI";
        }

        if (text.Contains("deepseek", StringComparison.OrdinalIgnoreCase))
        {
            return "DeepSeek";
        }

        if (text.Contains("deepcoder", StringComparison.OrdinalIgnoreCase))
        {
            return "Agentica DeepCoder";
        }

        if (text.Contains("cogito", StringComparison.OrdinalIgnoreCase))
        {
            return "Deep Cogito";
        }

        if (text.Contains("rnj-1", StringComparison.OrdinalIgnoreCase))
        {
            return "Essential AI";
        }

        if (text.Contains("kimi", StringComparison.OrdinalIgnoreCase)
            || text.Contains("moonshot", StringComparison.OrdinalIgnoreCase))
        {
            return "Moonshot Kimi";
        }

        if (text.Contains("minimax", StringComparison.OrdinalIgnoreCase))
        {
            return "MiniMax";
        }

        if (text.Contains("ernie", StringComparison.OrdinalIgnoreCase)
            || text.Contains("baidu", StringComparison.OrdinalIgnoreCase))
        {
            return "Baidu ERNIE";
        }

        if (text.Contains("hunyuan", StringComparison.OrdinalIgnoreCase)
            || text.Contains("tencent", StringComparison.OrdinalIgnoreCase))
        {
            return "Tencent Hunyuan";
        }

        if (text.Contains("mistral", StringComparison.OrdinalIgnoreCase)
            || text.Contains("mixtral", StringComparison.OrdinalIgnoreCase)
            || text.Contains("codestral", StringComparison.OrdinalIgnoreCase)
            || text.Contains("devstral", StringComparison.OrdinalIgnoreCase)
            || text.Contains("magistral", StringComparison.OrdinalIgnoreCase)
            || text.Contains("ministral", StringComparison.OrdinalIgnoreCase))
        {
            return "Mistral AI";
        }

        if (text.Contains("gpt-oss", StringComparison.OrdinalIgnoreCase))
        {
            return "OpenAI OSS";
        }

        if (text.Contains("glm", StringComparison.OrdinalIgnoreCase))
        {
            return "Z.ai GLM";
        }

        if (text.Contains("nemotron", StringComparison.OrdinalIgnoreCase))
        {
            return "NVIDIA";
        }

        if (text.Contains("command-r", StringComparison.OrdinalIgnoreCase)
            || text.Contains("command-r7b", StringComparison.OrdinalIgnoreCase)
            || text.Contains("aya", StringComparison.OrdinalIgnoreCase))
        {
            return "Cohere";
        }

        if (text.Contains("olmo", StringComparison.OrdinalIgnoreCase)
            || text.Contains("tulu", StringComparison.OrdinalIgnoreCase))
        {
            return "AI2 OLMo";
        }

        if (text.Contains("falcon", StringComparison.OrdinalIgnoreCase))
        {
            return "TII Falcon";
        }

        if (text.Contains("internlm", StringComparison.OrdinalIgnoreCase))
        {
            return "InternLM";
        }

        if (text.Contains("codegeex", StringComparison.OrdinalIgnoreCase))
        {
            return "Zhipu CodeGeeX";
        }

        if (text.Contains("opencoder", StringComparison.OrdinalIgnoreCase)
            || text.Contains("open-coder", StringComparison.OrdinalIgnoreCase))
        {
            return "OpenCoder";
        }

        if (text.Contains("mini-cpm", StringComparison.OrdinalIgnoreCase)
            || text.Contains("minicpm", StringComparison.OrdinalIgnoreCase))
        {
            return "OpenBMB";
        }

        if (text.Contains("solar", StringComparison.OrdinalIgnoreCase))
        {
            return "Upstage Solar";
        }

        if (text.Contains("seallm", StringComparison.OrdinalIgnoreCase))
        {
            return "SeaLLMs";
        }

        if (text.Contains("sea-lion", StringComparison.OrdinalIgnoreCase))
        {
            return "AI Singapore SEA-LION";
        }

        if (text.Contains("meralion", StringComparison.OrdinalIgnoreCase))
        {
            return "AI Singapore MERaLiON";
        }

        if (text.Contains("sailor2", StringComparison.OrdinalIgnoreCase)
            || text.Contains("sailor", StringComparison.OrdinalIgnoreCase))
        {
            return "Sailor SEA";
        }

        if (text.Contains("apertus", StringComparison.OrdinalIgnoreCase))
        {
            return "Swiss AI Apertus";
        }

        if (text.Contains("jamba", StringComparison.OrdinalIgnoreCase))
        {
            return "AI21 Jamba";
        }

        if (text.Contains("dbrx", StringComparison.OrdinalIgnoreCase))
        {
            return "Databricks";
        }

        if (text.Contains("hermes", StringComparison.OrdinalIgnoreCase)
            || text.Contains("nous", StringComparison.OrdinalIgnoreCase))
        {
            return "Nous Hermes";
        }

        if (text.Contains("laguna", StringComparison.OrdinalIgnoreCase))
        {
            return "Poolside Laguna";
        }

        if (text.Contains("starcoder", StringComparison.OrdinalIgnoreCase))
        {
            return "BigCode";
        }

        if (text.Contains("smollm", StringComparison.OrdinalIgnoreCase))
        {
            return "Hugging Face";
        }

        if (text.Contains("smallthinker", StringComparison.OrdinalIgnoreCase))
        {
            return "PowerInfer";
        }

        if (text.Contains("exaone", StringComparison.OrdinalIgnoreCase))
        {
            return "LG EXAONE";
        }

        if (text.Contains("danube", StringComparison.OrdinalIgnoreCase)
            || text.Contains("h2oai", StringComparison.OrdinalIgnoreCase))
        {
            return "H2O.ai";
        }

        return "Other";
    }
}
