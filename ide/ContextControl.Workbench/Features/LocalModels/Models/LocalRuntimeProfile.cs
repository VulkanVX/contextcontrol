namespace ContextControl.Workbench.Services;

/// <summary>User-configured OpenAI-compatible model server. Secrets stay in environment variables.</summary>
public sealed record LocalRuntimeProfile(string Id, string Name, string Endpoint, bool Enabled = true,
    string ApiKeyEnvironmentVariable = "", int ContextTokens = 4096, string ModelPath = "", int GpuLayers = 0)
{
    public static IReadOnlyList<LocalRuntimeProfile> Defaults =>
    [
        new("llama-cpp", "llama.cpp", "http://127.0.0.1:8080/v1"),
        new("lm-studio", "LM Studio / llmster", "http://127.0.0.1:1234/v1"),
        new("koboldcpp", "KoboldCpp", "http://127.0.0.1:5001/v1"),
        new("transformers", "Transformers", "http://127.0.0.1:8091/v1", Enabled: false),
        new("custom", "Other compatible server", "http://127.0.0.1:8000/v1", Enabled: false)
    ];

    public Uri ApiUri(string resource)
    {
        if (!Uri.TryCreate(Endpoint?.Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")
            || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new InvalidOperationException("Enter an HTTP(S) API base URL without credentials, query or fragment.");
        var baseUrl = uri.AbsoluteUri.TrimEnd('/');
        if (!uri.AbsolutePath.TrimEnd('/').EndsWith("/v1", StringComparison.OrdinalIgnoreCase)) baseUrl += "/v1";
        return new Uri(baseUrl + "/" + resource);
    }
}

public sealed record LocalRuntimeStatus(string Id, string Name, bool Reachable, int ModelCount, string Status);
