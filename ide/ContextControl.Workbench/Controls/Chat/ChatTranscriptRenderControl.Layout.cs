// CC-DESC: Draws the shared chat/image-generation transcript as one cached virtualized surface.

using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ContextControl.Workbench.ViewModels;

namespace ContextControl.Workbench.Controls;

public sealed partial class ChatTranscriptRenderControl
{
    private MessageLayout BuildMessageLayout(LocalLlmChatMessageViewModel message, double width)
    {
        var layout = new MessageLayout(message);
        var availableCardWidth = Math.Max(0.0, width - HorizontalInset * 2.0 - 2.0);
        var collapseProgress = GetCollapseProgress(message);
        var cardWidth = ResolveMessageCardWidth(message, availableCardWidth, collapseProgress);
        var card = new Rect(ResolveMessageCardX(message, width, cardWidth), 0, cardWidth, 1.0);
        var bodyCard = ResolveMessageBodyCard(message, width, availableCardWidth, card);
        var contentLeft = bodyCard.X + CardPaddingX;
        var contentWidth = Math.Max(1.0, bodyCard.Width - CardPaddingX * 2.0);
        var y = CardPaddingY;

        BuildHeader(layout, message, card, y, contentWidth);
        y += HeaderHeight;

        var collapsedHeight = HeaderHeight + 2.0;
        var isFullyCollapsed = collapseProgress >= 0.999 && !_collapseAnimations.ContainsKey(message);
        if (isFullyCollapsed)
        {
            layout.CardRect = new Rect(card.X, 0, card.Width, collapsedHeight);
            layout.Height = collapsedHeight + MessageGap;
            return layout;
        }

        var attachmentsAfterText = ShouldShowAttachmentsAfterText(message);
        if (message.HasAttachments && !attachmentsAfterText)
        {
            y = BuildAttachments(layout, message, contentLeft, y, contentWidth);
            y += 4.0;
        }

        var uiFontFamily = ResolveFontFamily(UiFontFamily, Resource("UiFontFamily", DefaultUiFontFamily));
        foreach (var part in message.Parts)
        {
            if (part.IsText)
            {
                var text = GetNormalizedPartText(part);
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                var lines = WrapLines(text, contentWidth, uiFontFamily, FontWeight.Normal, FontStyle.Normal, ChatTextFontSize);
                var height = Math.Max(ChatTextLineHeight, lines.Count * ChatTextLineHeight);
                AddSelectableTextBlock(layout, new TextBlockLayout(
                    new Rect(contentLeft, y, contentWidth, height),
                    lines,
                    ChatTextFontSize,
                    ChatTextLineHeight,
                    FontWeight.Normal,
                    FontStyle.Normal,
                    false,
                    Resource("ChatBodyTextBrush", TextPrimaryFallbackBrush)),
                    drawInMessage: true);
                y += height + 6.0;
            }
            else if (part.Snippet is { } snippet)
            {
                y = BuildSnippet(layout, snippet, contentLeft, y, contentWidth);
            }
        }

        if (message.HasAttachments && attachmentsAfterText)
        {
            y = BuildAttachments(layout, message, contentLeft, y, contentWidth);
            y += 4.0;
        }

        if (message.HasThinking)
        {
            y = BuildThinking(layout, message, contentLeft, y, contentWidth);
        }

        if (message.HasDiagnosticPrompt)
        {
            y = BuildDiagnostic(layout, message, contentLeft, y, contentWidth);
        }

        var expandedHeight = Math.Max(34.0, y + CardPaddingY);
        var cardHeight = expandedHeight + ((collapsedHeight - expandedHeight) * collapseProgress);
        layout.CardRect = new Rect(card.X, 0, card.Width, Math.Max(collapsedHeight, cardHeight));
        layout.Height = layout.CardRect.Height + MessageGap;
        return layout;
    }

    private double ResolveMessageCardWidth(
        LocalLlmChatMessageViewModel message,
        double availableCardWidth,
        double collapseProgress)
    {
        if (availableCardWidth <= 1.0)
        {
            return 1.0;
        }

        var columnWidth = ResolveConversationColumnWidth(availableCardWidth);
        if (message.IsContextControlGenerated)
        {
            var collapsedWidth = ResolveCollapsedToolCallWidth(message, columnWidth);
            return columnWidth + ((collapsedWidth - columnWidth) * Math.Clamp(collapseProgress, 0.0, 1.0));
        }

        if (IsFlatTranscriptMessage(message))
        {
            return columnWidth;
        }

        var maxCardWidth = Math.Max(160.0, columnWidth * 0.72);
        var maxContentWidth = Math.Max(1.0, maxCardWidth - CardPaddingX * 2.0);
        var desiredContentWidth = MeasureMessageDesiredContentWidth(message, maxContentWidth);
        var minCardWidth = message.HasSnippets || message.HasAttachments || message.HasDiagnosticPrompt || message.HasThinking
            ? Math.Min(maxCardWidth, 300.0)
            : Math.Min(maxCardWidth, 96.0);
        var desiredCardWidth = desiredContentWidth + CardPaddingX * 2.0;
        return Math.Clamp(desiredCardWidth, minCardWidth, maxCardWidth);
    }

    private static double ResolveConversationColumnWidth(double availableCardWidth)
    {
        return Math.Max(1.0, Math.Min(availableCardWidth, ConversationColumnMaxWidth));
    }

    private double ResolveCollapsedToolCallWidth(LocalLlmChatMessageViewModel message, double expandedWidth)
    {
        var uiFontFamily = ResolveFontFamily(UiFontFamily, Resource("UiFontFamily", DefaultUiFontFamily));
        var codeFontFamily = ResolveFontFamily(CodeFontFamily, Resource("CodeFontFamily", DefaultCodeFontFamily));
        var maxContentWidth = Math.Max(1.0, expandedWidth - CardPaddingX * 2.0);
        var desiredContentWidth = MeasureHeaderDesiredContentWidth(message, maxContentWidth, uiFontFamily, codeFontFamily);
        var desiredWidth = desiredContentWidth + CardPaddingX * 2.0;
        var maxWidth = Math.Min(expandedWidth, ToolCallCollapsedMaxWidth);
        var minWidth = Math.Min(maxWidth, ToolCallCollapsedMinWidth);
        return Math.Clamp(Math.Ceiling(desiredWidth), minWidth, maxWidth);
    }

    private static double ResolveConversationColumnX(double width, double columnWidth)
    {
        return Math.Max(HorizontalInset, (width - columnWidth) * 0.5);
    }

    private double ResolveMessageCardX(LocalLlmChatMessageViewModel message, double width, double cardWidth)
    {
        var availableCardWidth = Math.Max(0.0, width - HorizontalInset * 2.0 - 2.0);
        var columnWidth = ResolveConversationColumnWidth(availableCardWidth);
        var columnX = ResolveConversationColumnX(width, columnWidth);
        if (message.IsUser)
        {
            return Math.Max(HorizontalInset, columnX + columnWidth - cardWidth);
        }

        if (message.IsContextControlGenerated)
        {
            return Math.Max(HorizontalInset, columnX + Math.Max(0.0, columnWidth - cardWidth) * 0.5);
        }

        if (IsFlatTranscriptMessage(message))
        {
            return columnX;
        }

        return columnX;
    }

    private static Rect ResolveMessageBodyCard(
        LocalLlmChatMessageViewModel message,
        double width,
        double availableCardWidth,
        Rect animatedCard)
    {
        if (!message.IsContextControlGenerated)
        {
            return animatedCard;
        }

        var bodyWidth = ResolveConversationColumnWidth(availableCardWidth);
        return new Rect(ResolveConversationColumnX(width, bodyWidth), animatedCard.Y, bodyWidth, animatedCard.Height);
    }

    private double MeasureMessageDesiredContentWidth(LocalLlmChatMessageViewModel message, double maxContentWidth)
    {
        var uiFontFamily = ResolveFontFamily(UiFontFamily, Resource("UiFontFamily", DefaultUiFontFamily));
        var codeFontFamily = ResolveFontFamily(CodeFontFamily, Resource("CodeFontFamily", DefaultCodeFontFamily));
        var desired = MeasureHeaderDesiredContentWidth(message, maxContentWidth, uiFontFamily, codeFontFamily);

        foreach (var attachment in message.AttachedFiles)
        {
            desired = Math.Max(desired, IsInlineImageAttachment(attachment) ? Math.Min(maxContentWidth, 360.0) : 160.0);
        }

        foreach (var part in message.Parts)
        {
            if (part.IsText)
            {
                desired = Math.Max(desired, MeasureWrappedTextDesiredWidth(GetNormalizedPartText(part), maxContentWidth, uiFontFamily, ChatTextFontSize));
            }
            else if (part.Snippet is { } snippet)
            {
                desired = Math.Max(desired, MeasureSnippetDesiredContentWidth(snippet, maxContentWidth, codeFontFamily));
            }
        }

        if (message.HasThinking)
        {
            desired = Math.Max(desired, Math.Min(maxContentWidth, 420.0));
        }

        if (message.HasDiagnosticPrompt)
        {
            desired = Math.Max(desired, Math.Min(maxContentWidth, 460.0));
        }

        return Math.Clamp(Math.Ceiling(desired), 1.0, maxContentWidth);
    }

    private double MeasureHeaderDesiredContentWidth(
        LocalLlmChatMessageViewModel message,
        double maxContentWidth,
        FontFamily uiFontFamily,
        FontFamily codeFontFamily)
    {
        var role = MeasureTextWidth(CleanOneLine(message.HeaderTitle, 48), uiFontFamily, FontWeight.Bold, FontStyle.Normal, 9.2);
        var meta = string.IsNullOrWhiteSpace(message.MetaLabel)
            ? 0.0
            : Math.Min(MeasureTextWidth(CleanOneLine(message.MetaLabel, 120), uiFontFamily, FontWeight.SemiBold, FontStyle.Normal, ChatMetaFontSize), maxContentWidth * 0.45);
        var time = MeasureTextWidth(message.Time, codeFontFamily, FontWeight.Bold, FontStyle.Normal, 8.8);
        var actionWidth = string.IsNullOrWhiteSpace(message.PrimaryTextPart?.Text) ? 0.0 : 20.0;
        return Math.Min(maxContentWidth, role + (meta > 0.0 ? 9.0 + meta : 0.0) + actionWidth + time + 24.0);
    }

    private double MeasureWrappedTextDesiredWidth(string text, double maxContentWidth, FontFamily fontFamily, double fontSize)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0.0;
        }

        var desired = 0.0;
        foreach (var rawLine in text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            desired = Math.Max(desired, Math.Min(maxContentWidth, MeasureTextWidth(line, fontFamily, FontWeight.Normal, FontStyle.Normal, fontSize)));
        }

        return desired;
    }

    private double MeasureSnippetDesiredContentWidth(ChatSnippetViewModel snippet, double maxContentWidth, FontFamily codeFontFamily)
    {
        var desired = snippet.IsPatchPlan ? 360.0 : 260.0;
        foreach (var line in SplitLines(snippet.DisplayText))
        {
            desired = Math.Max(desired, Math.Min(maxContentWidth, MeasureTextWidth(line, codeFontFamily, FontWeight.Normal, FontStyle.Normal, ChatCodeFontSize) + 18.0));
        }

        if (snippet.IsPatchPlan)
        {
            foreach (var row in snippet.PatchPlanRows)
            {
                var rowText = $"{row.FileName} {row.Added} {row.Removed} {row.Loc} {row.Version}";
                desired = Math.Max(desired, Math.Min(maxContentWidth, MeasureTextWidth(rowText, codeFontFamily, FontWeight.Bold, FontStyle.Normal, 8.2) + 42.0));
            }
        }

        return Math.Clamp(desired, 1.0, maxContentWidth);
    }

    private static bool ShouldShowAttachmentsAfterText(LocalLlmChatMessageViewModel message)
    {
        return false;
    }

    private void BuildHeader(MessageLayout layout, LocalLlmChatMessageViewModel message, Rect card, double y, double contentWidth)
    {
        var headerRect = new Rect(card.X, 0, card.Width, HeaderHeight);
        var centerY = headerRect.Y + headerRect.Height * 0.5;
        var right = card.Right - CardPaddingX;
        var timeWidth = 36.0;
        right -= timeWidth;

        if (message.PrimaryTextPart is { IsText: true } primaryTextPart
            && !string.IsNullOrWhiteSpace(primaryTextPart.Text))
        {
            var copyRect = new Rect(right - 17.0, centerY - 7.0, 14.0, 14.0);
            layout.Hits.Add(new HitRegion(copyRect, ChatTranscriptHitKind.CopyMessage, primaryTextPart));
            right = copyRect.X - 4.0;
        }

        var imagePath = message.AttachedFiles.FirstOrDefault(IsInlineImageAttachment)?.Path;
        if (!string.IsNullOrWhiteSpace(imagePath))
        {
            var downloadRect = new Rect(right - 17.0, centerY - 7.0, 14.0, 14.0);
            layout.Hits.Add(new HitRegion(downloadRect, ChatTranscriptHitKind.DownloadAttachment, imagePath));
            right = downloadRect.X - 4.0;
        }

        if (message.CanCreateProject)
        {
            var createRect = new Rect(
                Math.Max(card.X + CardPaddingX, right - 88.0),
                centerY - ButtonHeight * 0.5,
                Math.Min(88.0, Math.Max(0.0, right - card.X - CardPaddingX)),
                ButtonHeight);
            if (createRect.Width >= 58.0)
            {
                layout.Hits.Add(new HitRegion(createRect, ChatTranscriptHitKind.CreateProject, message));
                right = createRect.X - 4.0;
            }
        }

        var left = card.X + CardPaddingX;
        if (message.IsContextControlGenerated)
        {
            layout.ToolIconRect = new Rect(left, centerY - 7.0, 14.0, 14.0);
            left += 18.0;
        }

        layout.HeaderRect = headerRect;
        layout.HeaderMetaClip = new Rect(left, headerRect.Y, Math.Max(0.0, right - left), headerRect.Height);
        layout.TimeRect = new Rect(card.Right - CardPaddingX - timeWidth, centerY - 7.0, timeWidth, 14.0);
        layout.Hits.Add(new HitRegion(headerRect, ChatTranscriptHitKind.ToggleMessageCollapse, message));
    }

    private double BuildAttachments(
        MessageLayout layout,
        LocalLlmChatMessageViewModel message,
        double x,
        double y,
        double contentWidth)
    {
        var codeFontFamily = ResolveFontFamily(CodeFontFamily, Resource("CodeFontFamily", DefaultCodeFontFamily));
        var left = x;
        var maxRight = x + contentWidth;
        var rowY = y;
        var bottom = y;
        foreach (var attachment in message.AttachedFiles)
        {
            if (IsInlineImageAttachment(attachment))
            {
                if (left > x)
                {
                    bottom = Math.Max(bottom, rowY + AttachmentHeight);
                    rowY = bottom + AttachmentGap;
                    left = x;
                }

                var imageRect = BuildImagePreviewRect(attachment.Path, x, rowY, contentWidth);
                layout.Attachments.Add(new AttachmentLayout(imageRect, "Generated image", attachment, true));
                layout.Hits.Add(new HitRegion(imageRect, ChatTranscriptHitKind.OpenImagePreview, attachment.Path));
                bottom = imageRect.Bottom;
                rowY = bottom + AttachmentGap;
                left = x;
                continue;
            }

            var title = CleanOneLine(attachment.DisplayTitle, 80);
            var text = GetFormattedText(title, Resource("ChatMetaBrush", TextMutedFallbackBrush), codeFontFamily, FontWeight.Bold, FontStyle.Normal, 9.0);
            var chipWidth = Math.Clamp(text.Width + 14.0, 42.0, Math.Min(180.0, contentWidth));
            if (left > x && left + chipWidth > maxRight)
            {
                bottom = Math.Max(bottom, rowY + AttachmentHeight);
                left = x;
                rowY = bottom + AttachmentGap;
            }

            var rect = new Rect(left, rowY, chipWidth, AttachmentHeight);
            layout.Attachments.Add(new AttachmentLayout(rect, title, attachment, false));
            layout.Hits.Add(new HitRegion(rect, ChatTranscriptHitKind.OpenAttachment, attachment.Path));
            left += chipWidth + AttachmentGap;
            bottom = Math.Max(bottom, rect.Bottom);
        }

        return bottom;
    }

    private double BuildSnippet(MessageLayout layout, ChatSnippetViewModel snippet, double x, double y, double contentWidth)
    {
        var header = new Rect(x, y, contentWidth, SnippetHeaderHeight);
        var expansionProgress = GetSnippetExpansionProgress(snippet);
        var codeHeight = ResolveSnippetBodyHeight(snippet, expansionProgress);
        var codeRect = new Rect(x, y + SnippetHeaderHeight + SnippetBodyGap, contentWidth, codeHeight);
        var codeLines = snippet.IsPatchPlan
            ? Array.Empty<string>()
            : GetSnippetDisplayLines(snippet, snippet.IsExpanded || expansionProgress > 0.02);
        var snippetLayout = new SnippetLayout(snippet, header, codeRect, codeLines);
        if (snippet.IsPatchPlan)
        {
            var rows = snippet.PatchPlanRows;
            for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
            {
                var row = rows[rowIndex];
                if (string.IsNullOrWhiteSpace(row.Target))
                {
                    continue;
                }

                layout.Hits.Add(new HitRegion(
                    new Rect(codeRect.X, codeRect.Y + 4.0 + (rowIndex * PatchPlanRowHeight), codeRect.Width, PatchPlanRowHeight),
                    ChatTranscriptHitKind.OpenPatchPlanFile,
                    new PatchPlanFileViewModel(row.Target, [])));
            }
        }
        else
        {
            var codeTextBlock = new TextBlockLayout(
                new Rect(codeRect.X + 6.0, codeRect.Y + 4.0, Math.Max(0.0, codeRect.Width - 12.0), Math.Max(0.0, codeRect.Height - 8.0)),
                codeLines,
                ChatCodeFontSize,
                ChatCodeLineHeight,
                FontWeight.Normal,
                FontStyle.Normal,
                true,
                Resource("TextPrimaryBrush", TextPrimaryFallbackBrush));
            snippetLayout.CodeTextBlock = AddSelectableTextBlock(layout, codeTextBlock, drawInMessage: false);
        }

        var right = header.Right - SnippetPadding;
        AddSnippetButton(layout, snippetLayout, ChatTranscriptHitKind.ApplyEffectivePatch, "Apply effective", ApplyPatchCommand, snippet, snippet.IsPatchPlan, ref right);
        AddSnippetButton(layout, snippetLayout, ChatTranscriptHitKind.ApplyAllPatch, "Apply all", ApplyAllPatchCommand, snippet, snippet.IsPatchPlan, ref right);
        AddSnippetButton(layout, snippetLayout, ChatTranscriptHitKind.PreviewSnippet, "GO", PreviewSnippetCommand, snippet, snippet.IsPatch, ref right);
        AddSnippetButton(layout, snippetLayout, ChatTranscriptHitKind.UseSnippet, snippet.ActionLabel, UseSnippetForCcCommand, snippet, snippet.HasPromptAction, ref right);
        AddSnippetButton(layout, snippetLayout, ChatTranscriptHitKind.SaveSnippet, "Save as", SaveSnippetAsCommand, snippet, snippet.CanSaveAsFile, ref right);
        AddSnippetButton(layout, snippetLayout, ChatTranscriptHitKind.CopySnippet, "Copy", CopySnippetCommand, snippet, true, ref right);
        AddSnippetButton(layout, snippetLayout, ChatTranscriptHitKind.ToggleSnippet, snippet.ToggleLabel, ToggleSnippetCommand, snippet, !snippet.IsPatchPlan, ref right);
        snippetLayout.MetaClip = new Rect(header.X + 48.0, header.Y, Math.Max(0.0, right - header.X - 52.0), header.Height);

        layout.Snippets.Add(snippetLayout);
        return codeRect.Bottom + 5.0;
    }

    private double ResolveSnippetBodyHeight(ChatSnippetViewModel snippet, double expansionProgress)
    {
        if (snippet.IsPatchPlan)
        {
            return Math.Clamp(6.0 + Math.Max(1, snippet.PatchPlanRows.Count) * PatchPlanRowHeight, 24.0, 132.0);
        }

        var progress = Math.Clamp(expansionProgress, 0.0, 1.0);
        var collapsedHeight = Math.Clamp(snippet.CollapsedPreviewHeight, 24.0, 88.0);
        var expandedHeight = snippet.IsRequestList
            ? ResolveRequestSnippetExpandedBodyHeight(snippet)
            : Math.Clamp(snippet.CodePreviewHeight, 28.0, 260.0);
        return collapsedHeight + ((expandedHeight - collapsedHeight) * progress);
    }

    private double ResolveRequestSnippetExpandedBodyHeight(ChatSnippetViewModel snippet)
    {
        var lines = GetSnippetDisplayLines(snippet, expanded: true);
        return Math.Clamp(8.0 + Math.Max(1, lines.Count) * ChatCodeLineHeight, 24.0, 160.0);
    }

    private static SelectableTextBlockLayout AddSelectableTextBlock(
        MessageLayout layout,
        TextBlockLayout block,
        bool drawInMessage)
    {
        var selectable = new SelectableTextBlockLayout(layout.SelectableTextBlocks.Count, block);
        layout.SelectableTextBlocks.Add(selectable);
        if (drawInMessage)
        {
            layout.TextBlocks.Add(selectable);
        }

        return selectable;
    }

    private void AddSnippetButton(
        MessageLayout layout,
        SnippetLayout snippetLayout,
        ChatTranscriptHitKind kind,
        string label,
        ICommand? command,
        ChatSnippetViewModel snippet,
        bool isVisible,
        ref double right)
    {
        if (!isVisible)
        {
            return;
        }

        var width = Math.Clamp(label.Length * 5.8 + 12.0, 28.0, 96.0);
        if (right - width < snippetLayout.HeaderRect.X + 100.0)
        {
            return;
        }

        var rect = new Rect(right - width, snippetLayout.HeaderRect.Y + 1.0, width, SnippetButtonHeight);
        var hit = new HitRegion(rect, kind, snippet);
        layout.Hits.Add(hit);
        snippetLayout.Buttons.Insert(0, new ButtonLayout(rect, label, command?.CanExecute(snippet) == true, hit));
        right = rect.X - 3.0;
    }

    private double BuildThinking(MessageLayout layout, LocalLlmChatMessageViewModel message, double x, double y, double contentWidth)
    {
        var buttonRect = new Rect(x, y, contentWidth, ButtonHeight);
        var hit = new HitRegion(buttonRect, ChatTranscriptHitKind.ToggleThinking, message);
        layout.Hits.Add(hit);
        var thinking = new ThinkingLayout(buttonRect, hit, message.IsThinkingExpanded);
        var expansionProgress = GetThinkingExpansionProgress(message);
        y += ButtonHeight + 2.0;

        if (expansionProgress > 0.01)
        {
            var codeFontFamily = ResolveFontFamily(CodeFontFamily, Resource("CodeFontFamily", DefaultCodeFontFamily));
            var lines = WrapLines(GetNormalizedThinkingText(message), contentWidth - 26.0, codeFontFamily, FontWeight.Normal, FontStyle.Normal, ChatCodeFontSize);
            var expandedTextHeight = Math.Clamp(lines.Count * ChatCodeLineHeight, 44.0, 232.0);
            var bodyHeight = Math.Max(1.0, (expandedTextHeight + 12.0) * expansionProgress);
            var textHeight = Math.Max(1.0, bodyHeight - 12.0);
            thinking.BodyRect = new Rect(x, y, contentWidth, bodyHeight);
            thinking.TextBlock = new TextBlockLayout(
                new Rect(x + 8.0, y + 6.0, Math.Max(1.0, contentWidth - 26.0), textHeight),
                lines,
                ChatCodeFontSize,
                ChatCodeLineHeight,
                FontWeight.Normal,
                FontStyle.Normal,
                true,
                Resource("TextPrimaryBrush", TextPrimaryFallbackBrush));
            y += bodyHeight + 5.0 * expansionProgress;
        }
        else
        {
            y += 2.0;
        }

        layout.Thinking = thinking;
        return y;
    }

    private double BuildDiagnostic(MessageLayout layout, LocalLlmChatMessageViewModel message, double x, double y, double contentWidth)
    {
        var buttonRect = new Rect(x, y, contentWidth, ButtonHeight);
        var hit = new HitRegion(buttonRect, ChatTranscriptHitKind.ToggleDiagnostic, message);
        layout.Hits.Add(hit);
        var diagnostic = new DiagnosticLayout(buttonRect, hit, message.IsDiagnosticExpanded);
        var expansionProgress = GetDiagnosticExpansionProgress(message);
        y += ButtonHeight + 2.0;

        if (expansionProgress > 0.01)
        {
            var codeFontFamily = ResolveFontFamily(CodeFontFamily, Resource("CodeFontFamily", DefaultCodeFontFamily));
            var lines = WrapLines(GetNormalizedDiagnosticText(message), contentWidth - 26.0, codeFontFamily, FontWeight.Normal, FontStyle.Normal, ChatCodeFontSize);
            var expandedTextHeight = Math.Clamp(lines.Count * ChatCodeLineHeight, 60.0, 292.0);
            var bodyHeight = Math.Max(1.0, (expandedTextHeight + 12.0) * expansionProgress);
            var textHeight = Math.Max(1.0, bodyHeight - 12.0);
            diagnostic.BodyRect = new Rect(x, y, contentWidth, bodyHeight);
            diagnostic.TextBlock = new TextBlockLayout(
                new Rect(x + 8.0, y + 6.0, Math.Max(1.0, contentWidth - 26.0), textHeight),
                lines,
                ChatCodeFontSize,
                ChatCodeLineHeight,
                FontWeight.Normal,
                FontStyle.Normal,
                true,
                Resource("TextPrimaryBrush", TextPrimaryFallbackBrush));
            y += bodyHeight + 5.0 * expansionProgress;
        }
        else
        {
            y += 2.0;
        }

        layout.Diagnostic = diagnostic;
        return y;
    }
}
