// CC-DESC: Local LLM service slice extracted from LocalLlmService.cs.

using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace ContextControl.Workbench.Services;

public sealed partial class LocalLlmService
{
    private const string HuggingFaceTokenEnvironmentVariable = "HF_TOKEN";
    private const string HuggingFaceHubTokenEnvironmentVariable = "HUGGINGFACE_HUB_TOKEN";
    private const string ContextControlHuggingFaceTokenEnvironmentVariable = "CC_HF_TOKEN";
    private static readonly object HuggingFaceTokenEnvironmentGate = new();
    private static bool _capturedOriginalHuggingFaceTokenEnvironment;
    private static string? _originalHuggingFaceToken;
    private static string? _originalHuggingFaceHubToken;

    public static string DefaultOllamaModelsDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ollama", "models");

    public static string ResolveOllamaModelsDirectory(string? configuredDirectory)
    {
        var configured = NormalizeModelsDirectory(configuredDirectory);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        var userEnvironment = NormalizeModelsDirectory(Environment.GetEnvironmentVariable(
            OllamaModelsEnvironmentVariable,
            EnvironmentVariableTarget.User));
        if (!string.IsNullOrWhiteSpace(userEnvironment))
        {
            return userEnvironment;
        }

        var processEnvironment = NormalizeModelsDirectory(Environment.GetEnvironmentVariable(OllamaModelsEnvironmentVariable));
        return string.IsNullOrWhiteSpace(processEnvironment)
            ? DefaultOllamaModelsDirectory
            : processEnvironment;
    }

    public static LocalLlmStorageConfigurationResult ConfigureOllamaModelsDirectory(string? directory)
    {
        var resolved = ResolveOllamaModelsDirectory(directory);
        try
        {
            System.IO.Directory.CreateDirectory(resolved);
            Environment.SetEnvironmentVariable(OllamaModelsEnvironmentVariable, resolved, EnvironmentVariableTarget.User);
            Environment.SetEnvironmentVariable(OllamaModelsEnvironmentVariable, resolved, EnvironmentVariableTarget.Process);
            return new LocalLlmStorageConfigurationResult(
                true,
                resolved,
                $"Ollama model storage set to {resolved}. Restart Ollama if it is already running.");
        }
        catch (Exception ex)
        {
            return new LocalLlmStorageConfigurationResult(
                false,
                resolved,
                $"Could not set Ollama model storage: {ex.Message}");
        }
    }

    public static void ApplyOllamaModelsDirectoryToProcess(string? directory)
    {
        var resolved = ResolveOllamaModelsDirectory(directory);
        Environment.SetEnvironmentVariable(OllamaModelsEnvironmentVariable, resolved, EnvironmentVariableTarget.Process);
    }

    public static void ApplyHuggingFaceTokenToProcess(string? configuredToken)
    {
        var configured = NormalizeHuggingFaceToken(configuredToken);
        lock (HuggingFaceTokenEnvironmentGate)
        {
            CaptureOriginalHuggingFaceTokenEnvironment();
            if (string.IsNullOrWhiteSpace(configured))
            {
                Environment.SetEnvironmentVariable(HuggingFaceTokenEnvironmentVariable, _originalHuggingFaceToken, EnvironmentVariableTarget.Process);
                Environment.SetEnvironmentVariable(HuggingFaceHubTokenEnvironmentVariable, _originalHuggingFaceHubToken, EnvironmentVariableTarget.Process);
                return;
            }

            Environment.SetEnvironmentVariable(HuggingFaceTokenEnvironmentVariable, configured, EnvironmentVariableTarget.Process);
            Environment.SetEnvironmentVariable(HuggingFaceHubTokenEnvironmentVariable, configured, EnvironmentVariableTarget.Process);
        }
    }

    public static bool HasHuggingFaceToken(string? configuredToken)
    {
        return !string.IsNullOrWhiteSpace(ResolveHuggingFaceToken(configuredToken));
    }

    public static string ResolveHuggingFaceTokenStatus(string? configuredToken)
    {
        if (!string.IsNullOrWhiteSpace(NormalizeHuggingFaceToken(configuredToken)))
        {
            return "Hugging Face token saved locally; Diffusers downloads use authenticated requests.";
        }

        return string.IsNullOrWhiteSpace(ResolveHuggingFaceToken(null))
            ? "No Hugging Face token set; large Diffusers downloads use anonymous rate limits. Please add a Hugging Face access token to not get rate-limited while downloading larger models."
            : "Using Hugging Face token from environment for Diffusers downloads.";
    }

    private static string ResolveHuggingFaceToken(string? configuredToken)
    {
        var configured = NormalizeHuggingFaceToken(configuredToken);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        foreach (var name in new[]
                 {
                     HuggingFaceTokenEnvironmentVariable,
                     HuggingFaceHubTokenEnvironmentVariable,
                     ContextControlHuggingFaceTokenEnvironmentVariable
                 })
        {
            var token = NormalizeHuggingFaceToken(Environment.GetEnvironmentVariable(name));
            if (!string.IsNullOrWhiteSpace(token))
            {
                return token;
            }
        }

        return "";
    }

    private static void CaptureOriginalHuggingFaceTokenEnvironment()
    {
        if (_capturedOriginalHuggingFaceTokenEnvironment)
        {
            return;
        }

        _originalHuggingFaceToken = Environment.GetEnvironmentVariable(HuggingFaceTokenEnvironmentVariable, EnvironmentVariableTarget.Process);
        _originalHuggingFaceHubToken = Environment.GetEnvironmentVariable(HuggingFaceHubTokenEnvironmentVariable, EnvironmentVariableTarget.Process);
        _capturedOriginalHuggingFaceTokenEnvironment = true;
    }

    private static LocalLlmCatalogModel CatalogModel(
        string id,
        string displayName,
        string releaseDate,
        string iconSource,
        string downloadSize,
        string license,
        string minimumRequirement,
        string advertisedContext,
        string comfortableContext,
        string sourceBudget,
        string expectedSpeed,
        string practicalUse,
        double minimumVramGiB,
        double recommendedVramGiB,
        bool worksOnCpu)
    {
        return new LocalLlmCatalogModel(
            id,
            displayName,
            releaseDate,
            iconSource,
            downloadSize,
            license,
            minimumRequirement,
            advertisedContext,
            comfortableContext,
            sourceBudget,
            expectedSpeed,
            practicalUse,
            minimumVramGiB,
            recommendedVramGiB,
            worksOnCpu,
            $"ollama pull {id}");
    }

    private static string NormalizeModelsDirectory(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return string.Empty;
        }

        var expanded = Environment.ExpandEnvironmentVariables(directory.Trim().Trim('"'));
        try
        {
            return Path.GetFullPath(expanded);
        }
        catch
        {
            return expanded;
        }
    }

    private static string NormalizeHuggingFaceToken(string? token)
    {
        return string.IsNullOrWhiteSpace(token)
            ? ""
            : token.Trim();
    }

}
