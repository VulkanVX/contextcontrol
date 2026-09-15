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
    private readonly HttpMessageHandler? _chatHandler;

    public LocalLlmService() { }

    internal LocalLlmService(HttpMessageHandler chatHandler) => _chatHandler = chatHandler;

    public async Task<LocalLlmChatResult> SendChatAsync(
        string modelId,
        string message,
        IReadOnlyList<string> attachmentPaths,
        CancellationToken cancellationToken = default)
    {
        return await SendChatAsync(modelId, message, attachmentPaths, null, null, cancellationToken).ConfigureAwait(false);
    }

    public async Task<LocalLlmChatResult> SendChatAsync(
        string modelId,
        string message,
        IReadOnlyList<string> attachmentPaths,
        IProgress<LocalLlmGenerationProgress>? progress,
        IProgress<string>? terminal,
        CancellationToken cancellationToken = default)
    {
        var preparedMessage = BuildChatMessage(message, attachmentPaths);
        return await SendChatAsync(
            new LocalLlmRequest(modelId, preparedMessage, "chat", attachmentPaths.Select(path => Path.GetFileName(path) ?? path).ToArray()),
            progress,
            terminal,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<LocalLlmChatResult> SendChatAsync(
        LocalLlmRequest request,
        IProgress<LocalLlmGenerationProgress>? progress,
        IProgress<string>? terminal,
        CancellationToken cancellationToken = default)
    {
        using var activity = LocalPerformanceActivity.Chat();
        var result = await SendBackendChatAsync(request, progress, terminal, cancellationToken).ConfigureAwait(false);
        // Some reasoning models exhaust their context before producing any answer.
        // Retry once with explicit thinking disabled, retaining the original evidence.
        if (!result.Succeeded && result.Status.StartsWith(ThinkingOnlyStatus, StringComparison.Ordinal)
            && request.Think != false && !cancellationToken.IsCancellationRequested)
        {
            const string status = "The model stopped during thinking. Retrying once with thinking off…";
            terminal?.Report(status);
            progress?.Report(new LocalLlmGenerationProgress(status, null, null, null, null, null, null, null, false));
            result = await SendBackendChatAsync(request with { Think = false }, progress, terminal, cancellationToken).ConfigureAwait(false);
        }
        return result;
    }

    private const string ThinkingOnlyStatus = "The model produced thinking but no answer.";

    private Task<LocalLlmChatResult> SendBackendChatAsync(LocalLlmRequest request, IProgress<LocalLlmGenerationProgress>? progress,
        IProgress<string>? terminal, CancellationToken cancellationToken) => request.ModelId?.StartsWith("runtime:", StringComparison.Ordinal) == true
            ? SendCompatibleChatAsync(request, progress, terminal, cancellationToken) : SendOllamaChatAsync(request, progress, terminal, cancellationToken);

    private async Task<LocalLlmChatResult> SendOllamaChatAsync(
        LocalLlmRequest request,
        IProgress<LocalLlmGenerationProgress>? progress,
        IProgress<string>? terminal,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ModelId))
        {
            return new LocalLlmChatResult(false, "Choose an installed local model first.");
        }

        if (string.IsNullOrWhiteSpace(request.Prompt))
        {
            return new LocalLlmChatResult(false, "Write a chat message first.");
        }

        var requestThinking = request.Think == true;
        IReadOnlyList<string>? encodedImages = null;
        if (request.ImagePaths is { Count: > 0 })
        {
            try
            {
                encodedImages = await EncodeImageAttachmentsAsync(request.ImagePaths, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                return new LocalLlmChatResult(false, $"Could not read image attachment: {ex.Message}");
            }

            if (encodedImages.Count == 0)
            {
                return new LocalLlmChatResult(false, "No readable image attachments were found.");
            }
        }

        var chatRequest = new OllamaChatRequest(
            request.ModelId,
            [new OllamaChatMessage("user", request.Prompt.Trim(), encodedImages)],
            Stream: true,
            Options: await PerformanceOptionsAsync(request, cancellationToken).ConfigureAwait(false),
            Think: request.Think);

        var answerBuilder = new StringBuilder();
        var thinkingBuilder = new StringBuilder();
        LocalLlmChatResult Failure(string status) => IncompleteChatResult(status, answerBuilder, thinkingBuilder);
        try
        {
            var thinkFlag = request.Think is null ? "" : requestThinking ? " --think" : " --think=false";
            terminal?.Report(request.ContextWindowTokens is > 0
                ? $"> ollama chat {chatRequest.Model} --num-ctx {request.ContextWindowTokens.Value}{thinkFlag}"
                : $"> ollama chat {chatRequest.Model}{thinkFlag}");
            progress?.Report(new LocalLlmGenerationProgress("Loading model and preparing prompt...", null, null, null, null, null, null, null, false));
            // Loading, prefill and CPU generation may legitimately take a long time.
            // Only user cancellation or a real transport/runtime failure ends the stream.
            var token = cancellationToken;
            using var http = _chatHandler is null ? CreateHttpClient(Timeout.InfiniteTimeSpan) : new HttpClient(_chatHandler, disposeHandler: false) { Timeout = Timeout.InfiniteTimeSpan };
            using var content = new StringContent(JsonSerializer.Serialize(chatRequest, JsonOptions), Encoding.UTF8, "application/json");
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, new Uri(OllamaBaseUri, "/api/chat")) { Content = content };
            using var response = await http.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var responseText = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
                return new LocalLlmChatResult(false, $"Ollama returned {(int)response.StatusCode}: {FirstLine(responseText)}");
            }

            OllamaChatResponse? finalResponse = null;
            await using var responseStream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            using var reader = new StreamReader(responseStream);
            while (await reader.ReadLineAsync(token).ConfigureAwait(false) is { } line)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var chatResponse = JsonSerializer.Deserialize<OllamaChatResponse>(line, JsonOptions);
                if (!string.IsNullOrWhiteSpace(chatResponse?.Error))
                    return Failure("Ollama generation failed: " + FirstLine(chatResponse.Error));
                var delta = chatResponse?.Message?.Content;
                var thinkingDelta = chatResponse?.Message?.Thinking;
                if (!string.IsNullOrEmpty(delta))
                {
                    answerBuilder.Append(delta);
                }

                if (!string.IsNullOrEmpty(thinkingDelta))
                {
                    thinkingBuilder.Append(thinkingDelta);
                }

                progress?.Report(new LocalLlmGenerationProgress(
                    !string.IsNullOrEmpty(thinkingDelta) && answerBuilder.Length == 0 ? "Model is thinking…" : "Generating local response...",
                    delta,
                    chatResponse?.PromptEvalCount,
                    chatResponse?.EvalCount,
                    chatResponse?.TotalDuration,
                    chatResponse?.LoadDuration,
                    chatResponse?.PromptEvalDuration,
                    chatResponse?.EvalDuration,
                    false,
                    thinkingDelta));

                if (chatResponse?.Done == true)
                {
                    finalResponse = chatResponse;
                    terminal?.Report(BuildGenerationSummary(chatResponse));
                    break;
                }
            }

            var answer = BuildFinalChatAnswer(answerBuilder.ToString(), thinkingBuilder.ToString());
            var stats = finalResponse is null ? null : BuildUsageStats(finalResponse);
            if (finalResponse is null)
                return Failure("Ollama's response stream ended before completion. Please retry.");
            var visibleAnswer = Regex.Replace(answerBuilder.ToString(), @"<think>.*?(?:</think>|$)", "", RegexOptions.Singleline | RegexOptions.IgnoreCase).Trim();
            if (visibleAnswer.Length == 0)
                return new LocalLlmChatResult(false, answer.Length == 0 ? "Ollama returned an empty response. Please retry."
                    : ThinkingOnlyStatus + " Try thinking off, a shorter prompt, or a larger context window.", Stats: stats);
            if (string.Equals(finalResponse.DoneReason, "length", StringComparison.OrdinalIgnoreCase))
            {
                const string status = "The model reached its context or output limit before finishing. Shorten the prompt or increase the context window, then retry.";
                return new LocalLlmChatResult(false, status, answer + "\n\n**Response incomplete.** " + status, stats, OutputLimited: true);
            }
            progress?.Report(new LocalLlmGenerationProgress("Generation complete.", null, finalResponse.PromptEvalCount,
                finalResponse.EvalCount, finalResponse.TotalDuration, finalResponse.LoadDuration, finalResponse.PromptEvalDuration,
                finalResponse.EvalDuration, true));
            return new LocalLlmChatResult(true, $"Local chat completed with {chatRequest.Model}.", answer, stats);
        }
        catch (HttpRequestException ex)
        {
            return Failure($"Ollama is not reachable at {OllamaBaseUri}: {ex.Message}");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Failure("Response was stopped by the user.");
        }
        catch (OperationCanceledException)
        {
            return Failure("The local runtime cancelled the response before completion.");
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return Failure("Could not finish reading Ollama's response: " + ex.Message);
        }
    }

    private static LocalLlmChatResult IncompleteChatResult(string status, StringBuilder answer, StringBuilder thinking)
    {
        var partial = BuildFinalChatAnswer(answer.ToString(), thinking.ToString());
        return new(false, status, partial.Length == 0 ? null : partial + "\n\n**Response incomplete.** " + status);
    }

    private static string BuildFinalChatAnswer(string answerText, string thinkingText)
    {
        var answer = (answerText ?? "").Trim();
        var thinking = (thinkingText ?? "").Trim();
        if (string.IsNullOrWhiteSpace(thinking))
        {
            return answer;
        }

        return string.IsNullOrWhiteSpace(answer)
            ? $"<think>{Environment.NewLine}{thinking}{Environment.NewLine}</think>"
            : $"<think>{Environment.NewLine}{thinking}{Environment.NewLine}</think>{Environment.NewLine}{Environment.NewLine}{answer}";
    }

}
