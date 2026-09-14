// CC-DESC: Provides API/browser AI route abstractions for Context Control.

namespace ContextControl.Workbench.Services;

public sealed record AiSendRequest(string RouteLabel, string Message, IReadOnlyList<string> AttachmentPaths);

public sealed record AiSendResult(bool Succeeded, string Status, string? PreparedMessage = null);

public interface IAiConnectionService
{
    Task<AiSendResult> SendAsync(AiSendRequest request, CancellationToken cancellationToken = default);
}

public sealed class ApiAiConnectionService : IAiConnectionService
{
    public Task<AiSendResult> SendAsync(AiSendRequest request, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new AiSendResult(
            false,
            "API route is scaffolded. Add provider credentials before live API sends.",
            request.Message));
    }
}

public sealed class BrowserAiConnectionService : IAiConnectionService
{
    public Task<AiSendResult> SendAsync(AiSendRequest request, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new AiSendResult(
            true,
            $"Browser payload prepared for {request.RouteLabel}.",
            request.Message));
    }
}
