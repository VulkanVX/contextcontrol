// CC-DESC: Defines visible CC Flow phase instructions.

using System.Text;

namespace ContextControl.Workbench.Services;

public static class CodexInstructionCatalog
{
    public const string CcMainKey = "cc-main";

    private static readonly SkillbookEntry[] CcMainEntries =
    [
        new(
            CcMainKey,
            "CC Main",
            """
            ContextControl is a mediated code workflow. The model reasons; the user is the NI assistant who runs DIR, CC export, GO preview, and Apply through local ContextControl scripts.
            Do not use extra actions for repository navigation, shell commands, filesystem reads, or direct edits. Those actions are outsourced to ContextControl and the user.
            Treat each attached capsule as the complete visible workspace for that turn. Spend attention on interpreting the capsule and solving the request, not on guessing unseen files.
            Before choosing the next output, decide whether the visible context is sufficient. If not, request the smallest next CC input that can change the answer.
            Each model turn includes a phase-specific CC Flow instruction. Obey the active phase instruction over generic habits.
            Keep outputs compact, mechanical, and directly usable by the next ContextControl step.
            """,
            "cc-main",
            true)
    ];

    private static readonly SkillbookEntry[] CcFlowEntries =
    [
        new(
            "cc-flow-01-dir-request",
            "DIR + Request",
            """
            Input: user request plus DIR project map.
            Output only the smallest CC export request.
            Allowed lines: exact relative file path; FUNCTION path :: symbol; FUNCTION wildcard-path :: symbol; FUNC: symbol; FIND: text; EXPAND: directory; END.
            Use final source request lines, exactly one FIND, or exactly one EXPAND. Do not mix FIND/EXPAND with file/FUNCTION/FUNC lines.
            Use EXPAND only when the likely subsystem is visible but exact files/functions are not. EXPAND returns a richer DIR manifest for that scope, not source code.
            Use FIND only for cheap discovery when exact files/functions are not visible enough; FIND must be exactly one request line followed by END.
            Do not use SYMBOL, prose, headings, code fences, absolute paths, broad folders, directories as file requests, generated/binary/vendor paths, shell commands, patch blocks, or duplicate obvious headers.
            Copy paths exactly from FILE, ROOT, or FAMILY manifest records. Prefer exact files/functions. Include build/config files only when the requested change directly needs them.
            """,
            "cc-flow",
            true),
        new(
            "cc-flow-02-cc-patch",
            "CC Export + Patch",
            """
            Input: CC source export plus optional user clarification.

            If source is insufficient, output only the next narrow CC request list ending with END. Prefer exact file/FUNCTION lines; use exactly one FIND only when the missing owner cannot be named from visible source.

            If source is sufficient, output one GO patch containing raw BEGIN/END CC-REPLACE blocks only. GO writes that raw output to patch.txt.

            One GO patch may contain many CC-REPLACE blocks across many files. Do not split patches into separate chat answers by file.

            Do not emit prose mixed with the patch, git diff, shell commands, apply_patch syntax, direct file edits, markdown fences, or commentary inside the patch.

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
            "cc-flow",
            true),
        new(
            "cc-flow-03-chat",
            "Chat",
            """
            Input: normal chat without a DIR or CC source attachment.
            Answer conversationally and do not output CC request lines, EXPAND/FIND lists, or CC-REPLACE blocks unless the user has attached the matching ContextControl artifact.
            If the user asks for project code work, tell them to run DIR first so ContextControl can attach the project map.
            Request-looking text in chat is advisory only and is not ready for CC.
            """,
            "cc-flow",
            true)
    ];

    public static IReadOnlyList<SkillbookEntry> SkillbookEntries =>
        CcMainEntries.Concat(CcFlowEntries).ToArray();

    public static string BuildCodexInstructionText(ContextCapsulePhase phase)
    {
        var builder = new StringBuilder();
        builder.AppendLine("## CC Main");
        builder.AppendLine(BuildCcMain());
        builder.AppendLine();
        builder.AppendLine("## Active phase contract");
        builder.AppendLine(BuildPhaseContract(phase));
        builder.AppendLine();
        builder.AppendLine("## CC Flow phase");
        builder.AppendLine(BuildCcFlowPhase(phase));

        return builder.ToString().TrimEnd();
    }

    public static string BuildCcMain()
    {
        return CcMainEntries.First(entry => entry.Key.Equals(CcMainKey, StringComparison.OrdinalIgnoreCase)).Text.Trim();
    }

    public static string BuildPhaseContract(ContextCapsulePhase phase)
    {
        return phase switch
        {
            ContextCapsulePhase.FileRequest => """
                Output only a CC export request list.
                Allowed lines: exact relative file path; FUNCTION path :: symbol; FUNCTION wildcard-path :: symbol; FUNC: symbol; FIND: text; EXPAND: directory; END.
                End with END.
                Use final source request lines, exactly one FIND, or exactly one EXPAND. Do not mix FIND/EXPAND with file/FUNCTION/FUNC lines.
                FIND must be exactly one request line followed by END.
                Do not use SYMBOL, markdown fences, prose, headings, patch text, or commentary.
                """,
            ContextCapsulePhase.SourceAudit => """
                Use only the attached CC source context.
                If more context is required, output only the next CC request list ending with END. Prefer exact file/FUNCTION lines; use exactly one FIND only when exact owners are unknown.
                If the edit is clear, emit GO-ready CC-REPLACE blocks.
                """,
            ContextCapsulePhase.PatchWrite => """
                Emit raw CC-REPLACE blocks only.
                If visible source is insufficient, output only the next CC request list ending with END. Prefer exact file/FUNCTION lines; use exactly one FIND only when exact owners are unknown.
                One GO patch may contain many CC-REPLACE blocks across many files.
                Supported MODE values: replace_region, insert_include, whole_file, insert_after_function, insert_before_function, delete_function, function, append_to_file, create_directory.
                Do not use shell commands, git patches, apply_patch, direct file edits, markdown fences, or prose wrappers.
                """,
            ContextCapsulePhase.PatchReview => """
                Review or repair the attached patch using only visible patch/source context.
                If repaired, emit complete CC-REPLACE blocks only.
                """,
            _ => """
                Normal chat is allowed, but ask for DIR + Request before making code claims.
                """
        };
    }

    public static string BuildCcFlowPhase(ContextCapsulePhase phase)
    {
        var key = GetCcFlowPhaseKey(phase);

        return CcFlowEntries.First(entry => entry.Key.Equals(key, StringComparison.OrdinalIgnoreCase)).Text.Trim();
    }

    public static string GetCcFlowPhaseKey(ContextCapsulePhase phase)
    {
        return phase switch
        {
            ContextCapsulePhase.FileRequest => "cc-flow-01-dir-request",
            ContextCapsulePhase.SourceAudit or ContextCapsulePhase.PatchWrite or ContextCapsulePhase.PatchReview => "cc-flow-02-cc-patch",
            _ => "cc-flow-03-chat"
        };
    }
}
