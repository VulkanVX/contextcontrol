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
    private async Task RunDirAsync()
    {
        await RunBusyAsync("DIR export", async () =>
        {
            PhaseTitle = "DIR export";
            PhaseDetail = "Exporting the project tree and navigation prompt.";
            var result = await _processService.RunDirectoryExportAsync(ActiveProjectRoot, ActiveProjectRulesPath);
            LogResult(result);

            if (!result.Succeeded)
            {
                PhaseTitle = "DIR failed";
                PhaseDetail = FirstErrorLine(result);
                return;
            }

            var pendingTask = PromptText.Trim();
            if (IsMeaningfulTaskPrompt(pendingTask))
            {
                RememberWorkflowTask(pendingTask, save: false);
            }

            _semanticIndex = null;
            _lastFindDiscoveryRequestPaths = [];

            RemoveAttachmentsByKind("code", "patch");
            _lastAssistantPatchBlocks = "";
            IsPatchPlanReady = false;
            PatchSummary = "No patch loaded.";
            UpdatePatchPlanActions(null);
            AddAttachment(Path.GetFileName(_processService.DirectoryExportPath), _processService.DirectoryExportPath, "dir");
            LastExportPath = _processService.DirectoryExportPath;
            await RefreshSemanticIndexFromDirAsync();
            PromptText = StripLegacyDirPayload(PromptText);
            PhaseTitle = "DIR ready";
            PhaseDetail = IsAutopilotEnabled
                ? "Phase 1: project tree attached. Send the request with DIR so the model returns exact CC file/FUNCTION lines."
                : "Project tree attached. Send a concrete request for CC lines, or paste your own lines and press CC.";
            AppendTerminalOutput($"DIR export ready: {Path.GetFileName(_processService.DirectoryExportPath)} attached.");
        });
    }

    private async Task RunDirTreeExportAsync()
    {
        await RunBusyAsync("TREE export", async () =>
        {
            var result = await _processService.RunDirectoryTreeExportAsync(ActiveProjectRoot, ActiveProjectRulesPath);
            LogResult(result);

            if (!result.Succeeded)
            {
                PhaseTitle = "TREE export failed";
                PhaseDetail = FirstErrorLine(result);
                AppendTerminalOutput($"TREE export failed: {PhaseDetail}");
                return;
            }

            LastExportPath = _processService.DirectoryTreeExportPath;
            PhaseTitle = "TREE ready";
            PhaseDetail = "Legacy tree export created. File uses compact directory tree format.";
            AppendTerminalOutput($"TREE export ready: {Path.GetFileName(_processService.DirectoryTreeExportPath)} written.");
        });
    }

    private async Task RefreshSemanticIndexFromDirAsync()
    {
        try
        {
            var dirText = await _processService.ReadOutputFileAsync(_processService.DirectoryExportPath);
            var semantic = await _semanticMapBuilder.BuildIndexAsync(ActiveProjectRoot, dirText);
            _semanticIndex = semantic.Index;
            await _processService.WriteSemanticMapAsync(semantic.SemanticMapText);
            AppendTerminalOutput($"DIR semantic resolver ready: {_semanticIndex.Files.Count:N0} indexed file(s).");
        }
        catch (Exception ex)
        {
            _semanticIndex = null;
            AppendTerminalOutput($"DIR semantic resolver skipped: {ex.Message}");
            Log("warn", $"DIR semantic resolver skipped: {ex.Message}");
        }
    }

    private async Task RunCcAsync()
    {
        await RunBusyAsync("CC export", async () =>
        {
            var effectiveProjectRoot = ResolveEffectiveProjectRootPath();
            var parse = RepairFunctionOwnerRequestLines(
                _promptBuilder.ParsePhase1RequestLines(PromptText),
                effectiveProjectRoot,
                out var repairMessages);
            foreach (var repairMessage in repairMessages)
            {
                AppendTerminalOutput(repairMessage);
            }

            var requestLines = parse.RequestLines;
            if (requestLines.Count == 0)
            {
                PhaseTitle = "CC needs input";
                PhaseDetail = "Paste clean file/function lines, or a quoted model list, into the prompt bar first.";
                Log("warn", "CC cancelled: no request lines.");
                AppendTerminalOutput("CC cancelled: no usable file/function request lines found.");
                return;
            }

            var manifest = await LoadCurrentDirManifestAsync();
            var writeActiveRequestToPrompt = true;
            IReadOnlyList<string> queuedFindLines = [];
            IReadOnlyList<MixedCcInvalidSourceLine> mixedInvalidSourceLines = [];
            ContextPhase1ValidationResult validation;
            if (TryBuildMixedCcRequestPlan(
                    parse,
                    manifest,
                    effectiveProjectRoot,
                    _lastFindDiscoveryRequestPaths,
                    out var mixedPlan))
            {
                queuedFindLines = mixedPlan.FindLines;
                mixedInvalidSourceLines = mixedPlan.InvalidSourceLines;
                foreach (var invalidSourceLine in mixedInvalidSourceLines)
                {
                    AppendTerminalOutput($"Mixed CC source line invalid: {invalidSourceLine.Line} -> {invalidSourceLine.Error}");
                }

                if (mixedPlan.ValidSourceLines.Count == 0)
                {
                    PhaseTitle = "CC mixed request blocked";
                    PhaseDetail = mixedInvalidSourceLines.Count > 0
                        ? "Source request lines are invalid. FIND was not run automatically."
                        : "Mixed source/FIND request has no valid source lines. FIND was not run automatically.";
                    AppendTerminalOutput("CC mixed request stopped: FIND discovery remains queued and was not run automatically.");
                    AppendQueuedFindRequestChatMessage(queuedFindLines, mixedInvalidSourceLines);
                    return;
                }

                validation = ContextPhase1RequestValidator.Validate(
                    new ContextRequestLineParseResult(mixedPlan.ValidSourceLines, [], true),
                    manifest,
                    effectiveProjectRoot,
                    _lastFindDiscoveryRequestPaths);
                writeActiveRequestToPrompt = false;
                AppendTerminalOutput("Mixed CC request staged: exporting valid source lines first; FIND remains queued separately.");
            }
            else
            {
                validation = ContextPhase1RequestValidator.Validate(
                    parse,
                    manifest,
                    effectiveProjectRoot,
                    _lastFindDiscoveryRequestPaths);
            }

            if (!validation.IsValid)
            {
                PhaseTitle = "CC request invalid";
                PhaseDetail = validation.Error;
                Log("warn", $"CC cancelled: {validation.Error}");
                AppendTerminalOutput($"CC cancelled: {validation.Error}");
                if (validation.Candidates.Count > 0)
                {
                    AppendTerminalOutput("Nearest visible manifest candidates:");
                    foreach (var candidate in validation.Candidates.Take(6))
                    {
                        AppendTerminalOutput($"  {candidate}");
                    }
                }

                return;
            }

            requestLines = validation.RequestLines;
            if (validation.Kind.Equals("expand", StringComparison.OrdinalIgnoreCase))
            {
                var scope = validation.ExpandScope;
                _lastFindDiscoveryRequestPaths = [];
                PromptText = EnsureEndsWithEnd($"EXPAND: {scope}");
                PhaseTitle = "DIR expand";
                PhaseDetail = $"Expanding scoped DIR manifest: {scope}";
                AppendTerminalOutput($"DIR expand started for {scope}.");
                var expandResult = await _processService.RunDirectoryExpandAsync(scope, ActiveProjectRoot, ActiveProjectRulesPath);
                LogResult(expandResult);

                if (!expandResult.Succeeded)
                {
                    PhaseTitle = "DIR expand failed";
                    PhaseDetail = FirstErrorLine(expandResult);
                    AppendTerminalOutput($"DIR expand failed: {PhaseDetail}");
                    return;
                }

                RemoveAttachmentsByKind("dir", "code", "patch");
                _lastAssistantPatchBlocks = "";
                IsPatchPlanReady = false;
                PatchSummary = "No patch loaded.";
                UpdatePatchPlanActions(null);
                AddAttachment(Path.GetFileName(_processService.DirectoryExportPath), _processService.DirectoryExportPath, "dir");
                LastExportPath = _processService.DirectoryExportPath;
                await RefreshSemanticIndexFromDirAsync();
                PromptText = "";
                PhaseTitle = "DIR expanded";
                PhaseDetail = "Scoped manifest attached. Send the request again so the model returns exact source request lines.";
                AppendTerminalOutput($"DIR expand complete: {Path.GetFileName(_processService.DirectoryExportPath)} replaced the previous DIR manifest.");
                return;
            }

            if (writeActiveRequestToPrompt)
            {
                PromptText = EnsureEndsWithEnd(string.Join(Environment.NewLine, requestLines));
            }

            PhaseTitle = "CC export";
            PhaseDetail = $"Exporting {requestLines.Count} selected source/function request line(s).";
            AppendTerminalOutput($"CC export started with {requestLines.Count} request line(s).");
            var result = await _processService.RunCodeExportAsync(requestLines, ActiveProjectRoot, ActiveProjectRulesPath);
            LogResult(result);

            if (!result.Succeeded)
            {
                PhaseTitle = "CC failed";
                PhaseDetail = FirstErrorLine(result);
                AppendTerminalOutput($"CC export failed: {PhaseDetail}");
                return;
            }

            if (requestLines.All(line => line.StartsWith("FIND:", StringComparison.OrdinalIgnoreCase)))
            {
                var discoveryText = await _processService.ReadOutputFileAsync(_processService.CodeExportPath);
                var matchedFiles = ExtractMatchedFileRequestsFromFindExport(discoveryText);
                RemoveAttachmentsByKind("dir", "code", "patch");
                LastExportPath = _processService.CodeExportPath;
                IsPatchPlanReady = false;
                UpdatePatchPlanActions(null);

                if (matchedFiles.Count == 0)
                {
                    _lastFindDiscoveryRequestPaths = [];
                    PromptText = "";
                    PhaseTitle = "FIND found nothing";
                    PhaseDetail = "Discovery exported no matching source files. Try a narrower exact term from the DIR tree.";
                    AppendTerminalOutput("CC FIND discovery complete: no matching code files found.");
                    AppendChatMessage(new LocalLlmChatMessageViewModel(
                        "assistant",
                        "FIND discovery returned no matching code files. Run DIR again or try a more exact FIND: term from the project tree.",
                        "ContextControl",
                        "CC discovery"));
                    return;
                }

                _lastFindDiscoveryRequestPaths = matchedFiles.ToArray();
                PromptText = EnsureEndsWithEnd(string.Join(Environment.NewLine, matchedFiles));
                PhaseTitle = "FIND matched files";
                PhaseDetail = $"Discovery found {matchedFiles.Count:N0} candidate file(s). Review the prompt lines, then press CC again to export source.";
                AppendTerminalOutput($"CC FIND discovery complete: loaded {matchedFiles.Count:N0} exact file request line(s) into the prompt.");
                AppendChatMessage(new LocalLlmChatMessageViewModel(
                    "assistant",
                    BuildFindDiscoveryChatText(matchedFiles),
                    "ContextControl",
                    "CC discovery"));
                return;
            }

            RemoveAttachmentsByKind("dir", "patch");
            IsPatchPlanReady = false;
            UpdatePatchPlanActions(null);
            AddAttachment(Path.GetFileName(_processService.CodeExportPath), _processService.CodeExportPath, "code");
            LastExportPath = _processService.CodeExportPath;
            _lastFindDiscoveryRequestPaths = [];
            PromptText = "";
            PhaseTitle = "Context ready";
            PhaseDetail = IsAutopilotEnabled
                ? "Phase 2: source attached. The prompt is intentionally empty; send for the CC decision or add a short clarification."
                : "Source context attached. Send an optional clarification for analysis, more context, or CC-REPLACE patch blocks.";
            AppendTerminalOutput($"CC export complete: {requestLines.Count} request line(s) exported.");
            if (queuedFindLines.Count > 0)
            {
                AppendTerminalOutput("Queued FIND discovery remains available as a separate CC request.");
                AppendQueuedFindRequestChatMessage(queuedFindLines, mixedInvalidSourceLines);
            }
        });
    }

    private sealed record MixedCcRequestPlan(
        IReadOnlyList<string> ValidSourceLines,
        IReadOnlyList<MixedCcInvalidSourceLine> InvalidSourceLines,
        IReadOnlyList<string> FindLines);

    private sealed record MixedCcInvalidSourceLine(string Line, string Error);

    private static bool TryBuildMixedCcRequestPlan(
        ContextRequestLineParseResult parse,
        ContextDirManifest manifest,
        string projectRoot,
        IReadOnlyCollection<string>? trustedFindRequestPaths,
        out MixedCcRequestPlan plan)
    {
        plan = new MixedCcRequestPlan([], [], []);
        if (parse.ExtraLines.Count > 0 || !parse.EndsWithEnd)
        {
            return false;
        }

        var findLines = parse.RequestLines
            .Where(line => line.StartsWith("FIND:", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var sourceLines = parse.RequestLines
            .Where(line => !line.StartsWith("FIND:", StringComparison.OrdinalIgnoreCase)
                && !line.StartsWith("EXPAND:", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (findLines.Length == 0 || sourceLines.Length == 0)
        {
            return false;
        }

        var validSourceLines = new List<string>();
        var invalidSourceLines = new List<MixedCcInvalidSourceLine>();
        foreach (var sourceLine in sourceLines)
        {
            var validation = ContextPhase1RequestValidator.Validate(
                new ContextRequestLineParseResult([sourceLine], [], true),
                manifest,
                projectRoot,
                trustedFindRequestPaths);
            if (validation.IsValid)
            {
                validSourceLines.AddRange(validation.RequestLines);
                continue;
            }

            invalidSourceLines.Add(new MixedCcInvalidSourceLine(sourceLine, validation.Error));
        }

        plan = new MixedCcRequestPlan(
            validSourceLines.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            invalidSourceLines,
            findLines);
        return true;
    }

    private static ContextRequestLineParseResult RepairFunctionOwnerRequestLines(
        ContextRequestLineParseResult parse,
        string projectRoot,
        out IReadOnlyList<string> repairMessages)
    {
        var messages = new List<string>();
        if (string.IsNullOrWhiteSpace(projectRoot) || parse.RequestLines.Count == 0)
        {
            repairMessages = messages;
            return parse;
        }

        var repairedLines = new List<string>(parse.RequestLines.Count);
        foreach (var line in parse.RequestLines)
        {
            if (TryRepairFunctionOwnerLine(projectRoot, line, out var repairedLine)
                && !string.Equals(line, repairedLine, StringComparison.Ordinal))
            {
                repairedLines.Add(repairedLine);
                messages.Add($"CC request repaired: {line} -> {repairedLine}");
                continue;
            }

            repairedLines.Add(line);
        }

        repairMessages = messages;
        return messages.Count == 0
            ? parse
            : new ContextRequestLineParseResult(repairedLines, parse.ExtraLines, parse.EndsWithEnd);
    }

    private static bool TryRepairFunctionOwnerLine(
        string projectRoot,
        string line,
        out string repairedLine)
    {
        repairedLine = line;
        const string prefix = "FUNCTION ";
        if (!line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var payload = line[prefix.Length..].Trim();
        var separatorIndex = payload.LastIndexOf(" :: ", StringComparison.Ordinal);
        var separatorLength = " :: ".Length;
        if (separatorIndex < 0)
        {
            separatorIndex = payload.LastIndexOf("::", StringComparison.Ordinal);
            separatorLength = "::".Length;
        }

        if (separatorIndex <= 0)
        {
            return false;
        }

        var relativePath = payload[..separatorIndex].Trim();
        var symbol = payload[(separatorIndex + separatorLength)..].Trim();
        if (relativePath.Length == 0
            || symbol.Length == 0
            || relativePath.Contains('*')
            || Path.IsPathRooted(relativePath))
        {
            return false;
        }

        var requestedAbsolutePath = Path.Combine(
            projectRoot,
            relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(requestedAbsolutePath))
        {
            var requestedContent = TryReadTextFile(requestedAbsolutePath);
            if (ContainsSymbolDeclaration(requestedContent, symbol))
            {
                return false;
            }
        }

        var ownerPath = TryFindFunctionOwnerPath(projectRoot, relativePath, symbol);
        if (string.IsNullOrWhiteSpace(ownerPath))
        {
            return false;
        }

        repairedLine = $"FUNCTION {ownerPath} :: {symbol}";
        return true;
    }

    private static string? TryFindFunctionOwnerPath(
        string projectRoot,
        string requestedRelativePath,
        string symbol)
    {
        var requestedDirectory = Path.GetDirectoryName(requestedRelativePath.Replace('/', Path.DirectorySeparatorChar));
        var requestedFileName = Path.GetFileNameWithoutExtension(requestedRelativePath);
        if (string.IsNullOrWhiteSpace(requestedFileName))
        {
            return null;
        }

        var searchRoot = string.IsNullOrWhiteSpace(requestedDirectory)
            ? projectRoot
            : Path.Combine(projectRoot, requestedDirectory);
        if (!Directory.Exists(searchRoot))
        {
            searchRoot = projectRoot;
        }

        try
        {
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                MatchCasing = MatchCasing.CaseInsensitive
            };
            var candidates = Directory.EnumerateFiles(searchRoot, $"{requestedFileName}*.cs", options)
                .Select(path => new
                {
                    AbsolutePath = path,
                    RelativePath = Path.GetRelativePath(projectRoot, path).Replace('\\', '/')
                })
                .Where(candidate => !candidate.RelativePath.Contains("/bin/", StringComparison.OrdinalIgnoreCase)
                    && !candidate.RelativePath.Contains("/obj/", StringComparison.OrdinalIgnoreCase))
                .Select(candidate => new
                {
                    candidate.AbsolutePath,
                    candidate.RelativePath,
                    Content = TryReadTextFile(candidate.AbsolutePath)
                })
                .Where(candidate => !string.IsNullOrEmpty(candidate.Content)
                    && candidate.Content.Contains(symbol, StringComparison.Ordinal))
                .OrderByDescending(candidate => ContainsSymbolDeclaration(candidate.Content, symbol))
                .ThenBy(candidate => candidate.RelativePath.Length)
                .ThenBy(candidate => candidate.RelativePath, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return candidates.Length == 1 || candidates.Length > 0
                ? candidates[0].RelativePath
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static string TryReadTextFile(string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch
        {
            return "";
        }
    }

    private static bool ContainsSymbolDeclaration(string content, string symbol)
    {
        foreach (var rawLine in content.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || !line.Contains(symbol, StringComparison.Ordinal))
            {
                continue;
            }

            var symbolIndex = line.IndexOf(symbol, StringComparison.Ordinal);
            if (symbolIndex <= 0)
            {
                continue;
            }

            var prefix = line[..symbolIndex].Trim();
            if (!LooksLikeCSharpMemberDeclarationPrefix(prefix))
            {
                continue;
            }

            var suffix = line[(symbolIndex + symbol.Length)..];
            if (suffix.StartsWith("(", StringComparison.Ordinal)
                || suffix.StartsWith(" ", StringComparison.Ordinal)
                || suffix.StartsWith("\t", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool LooksLikeCSharpMemberDeclarationPrefix(string prefix)
    {
        if (prefix.Contains("=>", StringComparison.Ordinal)
            || prefix.Contains("=", StringComparison.Ordinal))
        {
            return false;
        }

        var tokens = prefix.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        return tokens.Any(token => token is "public" or "private" or "protected" or "internal");
    }

    private void AppendQueuedFindRequestChatMessage(
        IReadOnlyList<string> findLines,
        IReadOnlyList<MixedCcInvalidSourceLine> invalidSourceLines)
    {
        if (findLines.Count == 0)
        {
            return;
        }

        var builder = new StringBuilder();
        builder.AppendLine("Source and FIND run separately.");
        if (invalidSourceLines.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Invalid source lines:");
            foreach (var invalidSourceLine in invalidSourceLines)
            {
                builder.AppendLine($"- {invalidSourceLine.Line} ({invalidSourceLine.Error})");
            }
        }

        builder.AppendLine();
        builder.AppendLine("Queued FIND discovery request:");
        foreach (var findLine in findLines)
        {
            builder.AppendLine(findLine);
        }

        builder.Append("END");
        AppendChatMessage(new LocalLlmChatMessageViewModel(
            "assistant",
            builder.ToString(),
            "ContextControl",
            "CC request"));
    }

    private async Task RunGoPreviewAsync()
    {
        await RunBusyAsync("GO preview", async () =>
        {
            var promptPatchBlocks = _promptBuilder.ExtractPatchBlocks(PromptText);
            var patchText = !string.IsNullOrWhiteSpace(promptPatchBlocks)
                ? promptPatchBlocks
                : _lastAssistantPatchBlocks;
            if (string.IsNullOrWhiteSpace(patchText))
            {
                PhaseTitle = "GO needs patch";
                PhaseDetail = "Paste BEGIN/END CC-REPLACE blocks, or press GO on a patch snippet. Send talks to the model; GO only previews patches.";
                Log("warn", "GO preview cancelled: no CC-REPLACE blocks found.");
                AppendTerminalOutput("GO cancelled: no CC-REPLACE blocks found in the prompt or latest assistant patch.");
                UpdatePatchPlanActions(null);
                return;
            }

            var shapeError = _promptBuilder.ValidatePatchBlocks(patchText);
            if (!string.IsNullOrWhiteSpace(shapeError))
            {
                PhaseTitle = "GO patch shape";
                PhaseDetail = shapeError;
                Log("warn", $"GO preview cancelled: {shapeError}");
                AppendTerminalOutput($"GO cancelled: {shapeError}");
                UpdatePatchPlanActions(null);
                IsPatchPlanReady = false;
                PatchSummary = shapeError;
                AppendChatMessage(new LocalLlmChatMessageViewModel(
                    "assistant",
                    BuildPatchShapeFailureChatText(shapeError),
                    "ContextControl",
                    "GO preview"));
                return;
            }

            PhaseTitle = "GO preview";
            PhaseDetail = "Writing patch.txt and asking ccReplace for a non-writing plan.";
            AppendTerminalOutput("GO preview started: writing patch.txt and running ccReplace -PlanOnly -Json.");
            await _processService.WritePatchAsync(patchText);
            AddAttachment(Path.GetFileName(_processService.PatchPath), _processService.PatchPath, "patch");

            var result = await _processService.PreviewPatchAsync(ActiveProjectRoot, ActiveProjectRulesPath);
            LogResult(result);
            var summary = _promptBuilder.ParsePatchPlanSummary(result.StandardOutput);
            var planReady = result.Succeeded && string.IsNullOrWhiteSpace(summary.Error);
            var failureDetail = BuildPatchFailureDetail(result, summary);
            IsPatchPlanReady = planReady;
            PatchSummary = planReady ? summary.CompactLabel : failureDetail;
            UpdatePatchPlanActions(planReady ? summary : null);
            PhaseTitle = planReady ? "Patch planned" : "Patch preview failed";
            PhaseDetail = planReady
                ? "Review the patch files below, then apply effective edits or apply all."
                : failureDetail;
            AppendTerminalOutput(planReady ? PatchSummary : $"GO preview failed: {PhaseDetail}");
            AppendChatMessage(new LocalLlmChatMessageViewModel(
                "assistant",
                planReady ? BuildPatchPlanChatText(summary) : BuildPatchFailureChatText("GO preview failed", result, summary),
                "ccReplace",
                "GO preview"));
        });
    }

    private async Task ApplyPatchAsync()
    {
        await ApplyPatchAsync("effective");
    }

    private async Task ApplyPatchAsync(string decision)
    {
        await RunBusyAsync("GO apply", async () =>
        {
            var plannedFiles = PatchPlanFiles.ToArray();
            PhaseTitle = "Applying patch";
            PhaseDetail = string.Equals(decision, "all", StringComparison.OrdinalIgnoreCase)
                ? "Applying all ccReplace actions."
                : "Applying effective edits through ccReplace.";
            var result = await _processService.ApplyPatchAsync(decision, ActiveProjectRoot, ActiveProjectRulesPath);
            LogResult(result);
            IsPatchPlanReady = false;
            PhaseTitle = result.Succeeded ? "Patch applied" : "Patch failed";
            PhaseDetail = result.Succeeded ? "ccReplace applied the selected edits." : FirstErrorLine(result);
            AppendTerminalOutput(result.Succeeded ? $"GO apply complete: {decision} edits applied." : $"GO apply failed: {PhaseDetail}");
            AppendChatMessage(new LocalLlmChatMessageViewModel(
                "assistant",
                BuildPatchApplyChatText(result, decision, plannedFiles),
                "ccReplace",
                "GO apply"));
            if (result.Succeeded)
            {
                UpdatePatchPlanActions(null);
            }
        });
    }

}
