// CC-DESC: Builds compact CC-native prompts for local LLM workflow phases.

using System.Text;

namespace ContextControl.Workbench.Services;

public enum ContextCapsulePhase
{
    Chat,
    FileRequest,
    SourceAudit,
    PatchWrite,
    PatchReview
}

public sealed record ContextCapsuleAttachment(
    string Label,
    string Kind,
    string Path,
    string Text,
    bool Included);

public sealed record ContextCapsuleBuildRequest(
    string UserMessage,
    ContextCapsulePhase Phase,
    string ModelId,
    string ModelContextLabel,
    int TargetContextTokens,
    string WorkflowInstructions,
    string SkillbookInstructions,
    IReadOnlyList<ContextCapsuleAttachment> Attachments);

public sealed record ContextCapsule(
    string Text,
    ContextCapsulePhase Phase,
    int EstimatedInputTokens,
    int EstimatedAttachmentTokens,
    int ComfortableContextTokens,
    int RequestedContextTokens,
    int OutputReserveTokens,
    double ContextPressurePercent,
    string Summary);

public sealed class ContextCapsuleBuilder
{
    public const int DefaultComfortableContextTokens = 4096;
    public const int DefaultOutputReserveTokens = 900;
    public const int MaxAttachmentCharacters = 28_000;
    public const int MaxExpandedAttachmentCharacters = 512_000;
    public const string AttachmentClipMarker = "[attachment clipped by local context budget]";

    public ContextCapsule Build(ContextCapsuleBuildRequest request)
    {
        var comfortableTokens = EstimateContextTokens(request.ModelContextLabel, DefaultComfortableContextTokens);
        var requestedTokens = Math.Max(comfortableTokens, request.TargetContextTokens);
        var outputReserve = DefaultOutputReserveTokens;
        var builder = new StringBuilder();

        builder.AppendLine("ContextControl local LLM capsule");
        builder.AppendLine();
        builder.AppendLine($"Model: {request.ModelId}");
        builder.AppendLine($"Phase: {FormatPhase(request.Phase)}");
        builder.AppendLine($"Comfortable context target: {comfortableTokens} tokens");
        builder.AppendLine($"Requested Ollama context: {requestedTokens} tokens");
        builder.AppendLine();
        builder.AppendLine("Core rule: the visible project context is included below as attachment text.");
        builder.AppendLine("Use that text directly. You cannot access anything outside this capsule or run tools.");
        builder.AppendLine("This is an authorized local project editing workflow; do not give a generic refusal when the requested edit can be answered from visible context.");
        builder.AppendLine();
        builder.AppendLine(BuildPhaseContract(request.Phase));
        builder.AppendLine();

        if (!string.IsNullOrWhiteSpace(request.WorkflowInstructions))
        {
            builder.AppendLine("ContextControl workflow instructions:");
            builder.AppendLine(request.WorkflowInstructions.Trim());
            builder.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(request.SkillbookInstructions))
        {
            builder.AppendLine("Enabled Skillbook entries:");
            builder.AppendLine(request.SkillbookInstructions.Trim());
            builder.AppendLine();
        }

        builder.AppendLine("User request:");
        builder.AppendLine(string.IsNullOrWhiteSpace(request.UserMessage) ? "(empty)" : request.UserMessage.Trim());
        builder.AppendLine();

        var attachmentTokens = 0;
        var remainingAttachmentChars = EstimateAttachmentCharacterLimit(requestedTokens, outputReserve);
        var included = request.Attachments.Where(attachment => attachment.Included).ToArray();
        if (included.Length > 0)
        {
            builder.AppendLine("Attachment inventory:");
            foreach (var attachment in included)
            {
                var text = attachment.Text ?? "";
                builder.AppendLine($"- {attachment.Label} ({attachment.Kind}) PATH: {attachment.Path}; BODY_CHARS: {text.Length}; EST_TOKENS: {EstimateTokens(text)}");
            }

            builder.AppendLine();
            builder.AppendLine("Included ContextControl attachments:");
            builder.AppendLine("The attachment bodies below are the actual visible context. Do not claim an attachment is empty when text appears between its markers.");
            foreach (var attachment in included)
            {
                var text = attachment.Text ?? "";
                var clipped = text;
                if (remainingAttachmentChars <= 0)
                {
                    clipped = "";
                }
                else if (clipped.Length > remainingAttachmentChars)
                {
                    clipped = clipped[..remainingAttachmentChars] + Environment.NewLine + AttachmentClipMarker;
                }

                remainingAttachmentChars -= Math.Max(0, clipped.Length);
                attachmentTokens += EstimateTokens(clipped);

                builder.AppendLine($"--- ATTACHMENT {attachment.Kind}: {attachment.Label}");
                builder.AppendLine($"PATH: {attachment.Path}");
                builder.AppendLine(clipped.TrimEnd());
                builder.AppendLine($"--- END ATTACHMENT {attachment.Label}");
                builder.AppendLine();
            }
        }

        var textOut = builder.ToString().TrimEnd();
        var inputTokens = EstimateTokens(textOut);
        var pressure = requestedTokens <= 0
            ? 0
            : Math.Clamp(inputTokens * 100d / Math.Max(1, requestedTokens - outputReserve), 0, 999);
        var summary = requestedTokens > comfortableTokens
            ? $"{inputTokens:N0} in tok; {attachmentTokens:N0} attachment tok; {outputReserve:N0} reserve; {pressure:0.#}% of requested ctx; comfy {comfortableTokens:N0}"
            : $"{inputTokens:N0} in tok; {attachmentTokens:N0} attachment tok; {outputReserve:N0} reserve; {pressure:0.#}% of comfortable context";

        return new ContextCapsule(
            textOut,
            request.Phase,
            inputTokens,
            attachmentTokens,
            comfortableTokens,
            requestedTokens,
            outputReserve,
            pressure,
            summary);
    }

    public static int EstimateTokens(string text)
    {
        return string.IsNullOrEmpty(text) ? 0 : (int)Math.Ceiling(text.Length / 4d);
    }

    public static int EstimateContextTokens(string? contextLabel, int fallbackTokens = DefaultComfortableContextTokens)
    {
        var label = contextLabel ?? "";
        var digits = new StringBuilder();
        for (var index = 0; index < label.Length; index++)
        {
            var ch = label[index];
            if (char.IsDigit(ch))
            {
                digits.Append(ch);
                continue;
            }

            if (digits.Length > 0)
            {
                var unit = ch;
                if (char.ToUpperInvariant(unit) == 'K'
                    && int.TryParse(digits.ToString(), out var thousands)
                    && thousands > 0)
                {
                    return thousands * 1024;
                }

                if (char.ToUpperInvariant(unit) == 'M'
                    && int.TryParse(digits.ToString(), out var millions)
                    && millions > 0)
                {
                    return millions * 1024 * 1024;
                }

                digits.Clear();
            }
        }

        return fallbackTokens;
    }

    public static int EstimateAttachmentCharacterLimit(int contextTokens, int outputReserveTokens = DefaultOutputReserveTokens)
    {
        var usableTokens = Math.Max(DefaultComfortableContextTokens - outputReserveTokens, contextTokens - outputReserveTokens);
        var characters = Math.Max(MaxAttachmentCharacters, usableTokens * 4);
        return Math.Min(MaxExpandedAttachmentCharacters, characters);
    }

    private static string BuildPhaseContract(ContextCapsulePhase phase)
    {
        return phase switch
        {
            ContextCapsulePhase.FileRequest => """
                Phase contract:
                Phase 1: DIR + Request.
                DIR project-tree context and the user request are attached. Do not solve the task yet.
                Output only the smallest safe CC request list.
                Do not wrap the list in markdown code fences or backticks.
                Format:
                ide/ContextControl.Workbench/Views/MainWindow.axaml
                FUNCTION ide/ContextControl.Workbench/ViewModels/ContextControlViewModel.cs :: SymbolName
                EXPAND: ide/ContextControl.Workbench/Views/
                END
                Valid lines: exact relative file path, FUNCTION path :: symbol, FUNCTION wildcard-path :: symbol, FUNC: symbol, FIND: text, EXPAND: directory, END.
                Return final source request lines, exactly one FIND, or exactly one EXPAND. Do not mix FIND/EXPAND with file/FUNCTION/FUNC lines.
                FIND must be exactly one request line followed by END.
                EXPAND returns a richer DIR manifest for the selected scope, not source code.
                Do not output SYMBOL, PATH:, FILE:, DIR:, labels, absolute paths, attachment paths, placeholder lines, patch blocks, or prose.
                Prefer exact relative file paths copied from DIR when the target file is visible.
                Every exact file path must be copied from the attached DIR manifest exactly.
                If the user's named path is absent from the DIR manifest, treat it as a hint and return real nearby manifest paths, FIND, or EXPAND.
                Never invent src/, .xaml.cs, .csproj, or framework-style paths that are not present in the tree.
                Ignore diagnostic questions about whether attachments were received; the inventory above is authoritative, and your output must still be a useful CC request list.
                Never return END by itself. If unsure, output the narrowest visible owner files/functions, then END.
                """,
            ContextCapsulePhase.SourceAudit => """
                Phase contract:
                Phase 2: CC export.
                CC source/function context is attached. Use only visible source and the optional user clarification.
                If context is insufficient, output only the next narrow CC request list ending with END.
                If context is sufficient, output one GO patch containing raw BEGIN/END CC-REPLACE blocks only. GO writes that raw output to patch.txt.
                One GO patch may contain many CC-REPLACE blocks across many files.
                Keep analysis internal; do not add prose unless the user explicitly asked for explanation instead of a patch.
                """,
            ContextCapsulePhase.PatchWrite => """
                Phase contract:
                Phase 2: CC export.
                CC source/function context is attached.
                You are not in DIR + Request. Do not output broad discovery; use exactly one FIND only when exact paths are unknown.
                If source is insufficient, output only the next narrow CC request list ending with END.
                If source is sufficient, emit one GO patch containing raw CC-REPLACE blocks only. GO writes that raw output to patch.txt.
                One GO patch may contain many CC-REPLACE blocks across many files. Do not split patches into separate chat answers by file.
                Do not emit prose mixed with the patch, git diff, shell commands, apply_patch syntax, direct file edits, markdown fences, or commentary inside the patch.
                Never put a bare path directly after BEGIN CC-REPLACE.
                Use paths relative to the project root, copied from the CC source export header.
                Use replace_region only when the visible source contains literal CC-REPLACE-BEGIN/END markers for NAME.
                A XAML selector, CSS selector, function name, or style name is not a replace_region marker.
                For unmarked XAML/AXAML style/resource edits, use MODE: whole_file with the complete file contents from the export, or request more CC context if the full file is not visible.
                Supported MODE values: replace_region, insert_include, whole_file, insert_after_function, insert_before_function, delete_function, function, append_to_file, create_directory.
                General rules:
                Use FILE for file-targeting modes.
                Use DIR only for create_directory.
                Body modes require --- followed by replacement text.
                Bodyless modes are insert_include, delete_function, and create_directory.
                function, insert_before_function, insert_after_function, delete_function, and replace_region require NAME.
                insert_include requires HEADER.
                whole_file body must be the complete final file content.
                New files use MODE: whole_file.
                If adding, removing, or renaming C++ source files, include the required CMakeLists.txt/build-file CC-REPLACE block in the same GO patch.
                Ground every edit in visible source and choose the least invasive valid ccReplace mode.
                If a required target, declaration, dependency, or build owner is not visible, request the next narrow CC export instead of guessing.
                Do not ask for more context when the visible export already contains the target file and enough surrounding source to write the edit.
                Mode choice order:
                1. replace_region: use for visible CC-REPLACE-BEGIN/END markers.
                2. insert_include: use for one missing C/C++ include.
                3. whole_file: use for new files, small files, unmarked files, or risky structure edits.
                4. insert_after_function / insert_before_function / delete_function: use around one unique visible function.
                5. function: use only when replacing one unique unambiguous function.
                6. append_to_file: use only for additive tail content.
                7. create_directory: use before creating files inside a new folder.
                Patch block skeleton:
                BEGIN CC-REPLACE
                FILE: path/relative/to/project_root.cpp
                MODE: whole_file
                ---
                replacement text
                END CC-REPLACE
                Header variants: create_directory uses DIR instead of FILE; insert_include adds HEADER and has no body; function/insert_before_function/insert_after_function/delete_function/replace_region require NAME; bodyless modes omit ---.
                """,
            ContextCapsulePhase.PatchReview => """
                Phase contract:
                Phase 2: CC patch review.
                Patch context is attached.
                Review or repair it using only visible context.
                If repaired, emit complete CC-REPLACE blocks.
                """,
            _ => """
                Phase contract:
                Normal local chat. Ask for DIR/CC context when code evidence is needed.
                """
        };
    }

    private static string FormatPhase(ContextCapsulePhase phase)
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

    private static int EstimateComfortableContextTokens(string contextLabel) => EstimateContextTokens(contextLabel);
}
