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
    private static string BuildChatMessage(string message, IReadOnlyList<string> attachmentPaths)
    {
        var clean = message.Trim();
        var attachments = attachmentPaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (attachments.Length == 0)
        {
            return clean;
        }

        var builder = new StringBuilder(clean);
        builder.AppendLine();
        builder.AppendLine();
        builder.AppendLine("Attached local file paths:");
        foreach (var path in attachments)
        {
            builder.AppendLine(path);
        }

        return builder.ToString();
    }

    private static async Task<IReadOnlyList<string>> EncodeImageAttachmentsAsync(
        IReadOnlyList<string> imagePaths,
        CancellationToken cancellationToken)
    {
        var images = new List<string>();
        foreach (var path in imagePaths.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var fullPath = Path.GetFullPath(path);
            if (!File.Exists(fullPath))
            {
                continue;
            }

            var bytes = await File.ReadAllBytesAsync(fullPath, cancellationToken).ConfigureAwait(false);
            if (bytes.Length == 0)
            {
                continue;
            }

            images.Add(Convert.ToBase64String(bytes));
        }

        return images;
    }

    private static string BuildGenerationSummary(OllamaChatResponse response)
    {
        var evalTokens = response.EvalCount ?? 0;
        var evalSeconds = NanosecondsToSeconds(response.EvalDuration);
        var speed = evalTokens > 0 && evalSeconds > 0
            ? $"{evalTokens / evalSeconds:0.#} tok/s"
            : "speed unknown";
        var total = FormatDuration(response.TotalDuration);
        var load = FormatDuration(response.LoadDuration);
        var prompt = response.PromptEvalCount is { } promptTokens
            ? $"{promptTokens} prompt tok"
            : "prompt tok ?";
        return $"done: {response.EvalCount ?? 0} output tok, {prompt}, {speed}, total {total}, load {load}";
    }

    private static LocalLlmUsageStats BuildUsageStats(OllamaChatResponse response)
    {
        return new LocalLlmUsageStats(
            response.PromptEvalCount,
            response.EvalCount,
            response.TotalDuration,
            response.LoadDuration,
            response.PromptEvalDuration,
            response.EvalDuration);
    }

    private static double NanosecondsToSeconds(long? nanoseconds)
    {
        return nanoseconds is > 0 ? nanoseconds.Value / 1_000_000_000d : 0;
    }

    private static string FormatDuration(long? nanoseconds)
    {
        var seconds = NanosecondsToSeconds(nanoseconds);
        return seconds <= 0 ? "?" : $"{seconds:0.##}s";
    }

}
