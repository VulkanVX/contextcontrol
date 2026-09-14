// CC-DESC: Resolves the active visible step in the Context Control prompt flow.

namespace ContextControl.Workbench.Services;

public static class PromptFlowStepResolver
{
    public const string RequestDir = "request-dir";
    public const string Request = RequestDir;
    public const string Dir = RequestDir;
    public const string Cc = "cc";
    public const string FileRequestSend = RequestDir;
    public const string SourceAuditSend = Cc;
    public const string PatchWriteSend = Cc;
    public const string PatchReviewSend = Cc;
    public const string GoApply = "go-apply";
    public const string GoPreview = GoApply;
    public const string Apply = GoApply;
    public const string RawImageBrowser = "raw-image-browser";

    public static string Resolve(
        ContextCapsulePhase phase,
        string phaseTitle,
        string phaseDetail,
        string promptModeKey,
        bool isAutopilotEnabled,
        bool isPatchPlanReady)
    {
        var text = $"{phaseTitle} {phaseDetail}";
        if (Contains(text, "Applying patch")
            || Contains(text, "GO apply")
            || Contains(text, "Patch applied")
            || Contains(text, "Patch failed"))
        {
            return GoApply;
        }

        if (Contains(text, "GO preview")
            || Contains(text, "Patch preview")
            || Contains(text, "Patch planned")
            || Contains(text, "GO needs patch")
            || Contains(text, "GO patch shape"))
        {
            return GoApply;
        }

        if (Contains(text, "CC export")
            || Contains(text, "FIND matched")
            || Contains(text, "FIND found")
            || Contains(text, "CC needs input")
            || Contains(text, "CC path not found")
            || Contains(text, "CC request detected"))
        {
            return Cc;
        }

        if (Contains(text, "DIR export"))
        {
            return RequestDir;
        }

        if (Contains(text, "DIR ready")
            || Contains(text, "File request fallback")
            || Contains(text, "Resolver suggested")
            || Contains(text, "Resolver needs discovery"))
        {
            return RequestDir;
        }

        if (Contains(text, "Context ready"))
        {
            return Cc;
        }

        if (string.Equals(promptModeKey, "imagegen", StringComparison.OrdinalIgnoreCase)
            || string.Equals(promptModeKey, "terminal", StringComparison.OrdinalIgnoreCase)
            || (!isAutopilotEnabled && phase == ContextCapsulePhase.Chat))
        {
            return RawImageBrowser;
        }

        return phase switch
        {
            ContextCapsulePhase.FileRequest => RequestDir,
            ContextCapsulePhase.SourceAudit => Cc,
            ContextCapsulePhase.PatchWrite => Cc,
            ContextCapsulePhase.PatchReview => Cc,
            _ => RequestDir
        };
    }

    private static bool Contains(string text, string value)
    {
        return text.Contains(value, StringComparison.OrdinalIgnoreCase);
    }
}
