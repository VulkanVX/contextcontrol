// CC-DESC: Extracted ContextControlViewModel system slice.
// CC-DESC: Owns Context Control workflow state, prompt bar state, and DIR/CC/GO commands.

using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows.Input;
using Avalonia.Collections;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using ContextControl.Workbench.Services;

namespace ContextControl.Workbench.ViewModels;

public sealed partial class ContextControlViewModel
{
    private LocalLlmModelViewModel? ResolveModelForPhase(ContextCapsulePhase phase)
    {
        if (SelectedLocalModel is { IsInstalled: true } selected)
        {
            return selected;
        }

        var preferredId = phase switch
        {
            ContextCapsulePhase.FileRequest => FileRequestModelId,
            ContextCapsulePhase.SourceAudit => PatchReviewModelId,
            ContextCapsulePhase.PatchWrite => PatchWriteModelId,
            ContextCapsulePhase.PatchReview => PatchReviewModelId,
            _ => ChatModelId
        };

        return InstalledLocalModels.FirstOrDefault(model => string.Equals(model.Id, preferredId, StringComparison.OrdinalIgnoreCase))
            ?? InstalledLocalModels.FirstOrDefault();
    }

    private static int ResolveRequestedContextTokens(LocalLlmModelViewModel? model, ContextCapsulePhase phase)
    {
        if (model is null)
        {
            return ContextCapsuleBuilder.DefaultComfortableContextTokens;
        }

        var comfortable = ContextCapsuleBuilder.EstimateContextTokens(
            model.ComfortableContext,
            ContextCapsuleBuilder.DefaultComfortableContextTokens);
        if (phase == ContextCapsulePhase.Chat)
        {
            return comfortable;
        }

        var advertised = ContextCapsuleBuilder.EstimateContextTokens(model.AdvertisedContext, comfortable);
        return Math.Max(comfortable, advertised);
    }

    private async Task<IReadOnlyList<ContextCapsuleAttachment>> BuildCapsuleAttachmentsAsync(ContextCapsulePhase phase)
    {
        var results = new List<ContextCapsuleAttachment>();
        foreach (var attachment in Attachments)
        {
            var included = ShouldIncludeAttachmentForPhase(attachment, phase) && File.Exists(attachment.Path);
            var text = "";
            if (included)
            {
                try
                {
                    text = await File.ReadAllTextAsync(attachment.Path);
                }
                catch (Exception ex)
                {
                    included = false;
                    text = $"[could not read attachment: {ex.Message}]";
                }
            }

            results.Add(new ContextCapsuleAttachment(
                attachment.DisplayTitle,
                attachment.Kind,
                attachment.Path,
                text,
                included));
        }

        return results;
    }

    private static ContextControlAttachmentViewModel[] BuildSentAttachmentSnapshot(IEnumerable<ContextCapsuleAttachment> attachments)
    {
        return (attachments ?? [])
            .Where(attachment => attachment.Included)
            .Select(attachment => new ContextControlAttachmentViewModel(attachment.Label, attachment.Path, attachment.Kind)
            {
                IncludeInPrompt = true
            })
            .ToArray();
    }

    private ContextControlAttachmentViewModel[] BuildPendingAttachmentSnapshotForKinds(params string[] kinds)
    {
        var kindSet = new HashSet<string>(kinds ?? [], StringComparer.OrdinalIgnoreCase);
        return Attachments
            .Where(attachment => attachment.IncludeInPrompt
                && kindSet.Contains(attachment.Kind)
                && !string.IsNullOrWhiteSpace(attachment.Path)
                && File.Exists(attachment.Path))
            .Select(attachment => new ContextControlAttachmentViewModel(attachment.Label, attachment.Path, attachment.Kind)
            {
                IncludeInPrompt = true
            })
            .ToArray();
    }

    private void ConsumeSentAttachments(IReadOnlyList<ContextControlAttachmentViewModel> sentAttachments)
    {
        if (sentAttachments.Count == 0 || Attachments.Count == 0)
        {
            return;
        }

        var removed = false;
        foreach (var sent in sentAttachments)
        {
            for (var index = Attachments.Count - 1; index >= 0; index--)
            {
                var pending = Attachments[index];
                if (!string.Equals(pending.Kind, sent.Kind, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(pending.Path, sent.Path, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                Attachments.RemoveAt(index);
                removed = true;
                break;
            }
        }

        if (removed)
        {
            NotifyAttachmentStateChanged();
        }
    }

    private static bool ShouldIncludeAttachmentForPhase(ContextControlAttachmentViewModel attachment, ContextCapsulePhase phase)
    {
        if (!attachment.IncludeInPrompt)
        {
            return false;
        }

        if (attachment.Kind.Equals("dir", StringComparison.OrdinalIgnoreCase))
        {
            return phase == ContextCapsulePhase.FileRequest;
        }

        if (attachment.Kind.Equals("code", StringComparison.OrdinalIgnoreCase))
        {
            return phase is ContextCapsulePhase.SourceAudit or ContextCapsulePhase.PatchWrite or ContextCapsulePhase.PatchReview;
        }

        if (attachment.Kind.Equals("patch", StringComparison.OrdinalIgnoreCase))
        {
            return phase == ContextCapsulePhase.PatchReview;
        }

        if (attachment.Kind.Equals("image", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private static string FormatCapsulePhase(ContextCapsulePhase phase)
    {
        return phase switch
        {
            ContextCapsulePhase.FileRequest => "DIR + Request",
            ContextCapsulePhase.SourceAudit => "CC",
            ContextCapsulePhase.PatchWrite => "CC",
            ContextCapsulePhase.PatchReview => "CC",
            _ => "chat"
        };
    }

}
