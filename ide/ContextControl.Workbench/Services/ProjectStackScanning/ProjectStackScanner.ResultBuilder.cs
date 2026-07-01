// CC-DESC: Deterministically detects project stacks and current file-rule coverage.

using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace ContextControl.Workbench.Services;

public static partial class ProjectStackScanner
{
    private static void AddPostScanStackSignals(ScanState state)
    {
        if (state.LanguageCounts.ContainsKey("C#") && state.StackReasons.ContainsKey(".NET"))
        {
            AddStack(state, ".NET", "C# files");
        }

        if (state.ExtensionCounts.ContainsKey(".tsx") || state.ExtensionCounts.ContainsKey(".jsx"))
        {
            AddStack(state, "React", "*.tsx/*.jsx");
        }

        foreach (var extension in state.ExtensionCounts.Keys)
        {
            if (LanguageByExtension.TryGetValue(extension, out var language))
            {
                AddSuggestedExtension(state, extension, isLoc: language is not "CSS" and not "HTML" and not "XAML");
            }
        }
    }

    private static ProjectStackScanResult BuildResult(ScanState state)
    {
        var stacks = state.StackReasons
            .OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var stackLabel = stacks.Length == 0
            ? "No stack manifests detected"
            : string.Join(", ", stacks.Take(4).Select(item => item.Key)) + (stacks.Length > 4 ? $", +{stacks.Length - 4}" : "");
        var summary = $"{stackLabel} | {state.TrackedFiles:N0}/{state.VisibleFiles:N0} visible files match current rules | {state.FilesSeen:N0} scanned for setup";

        var coveredSupported = new SortedSet<string>(NameComparer);
        var coveredLoc = new SortedSet<string>(NameComparer);
        var supportedSuggestions = new SortedSet<string>(NameComparer);
        var locSuggestions = new SortedSet<string>(NameComparer);
        var skippedDirectorySuggestions = new SortedSet<string>(NameComparer);
        foreach (var stack in stacks.Select(item => item.Key))
        {
            AddCoveredExtensions(coveredSupported, SuggestedSupportedByStack, stack, state.Rules.SupportedExtensions);
            AddCoveredExtensions(coveredLoc, SuggestedLocByStack, stack, state.Rules.LocExtensions);
            AddMissingExtensions(supportedSuggestions, SuggestedSupportedByStack, stack, state.Rules.SupportedExtensions);
            AddMissingExtensions(locSuggestions, SuggestedLocByStack, stack, state.Rules.LocExtensions);
            AddMissingNames(skippedDirectorySuggestions, SuggestedSkippedDirectoriesByStack, stack, state.Rules.IgnoredDirectories);
        }

        foreach (var rule in state.AutoSkippedDirectoryRules)
        {
            if (!state.Rules.IgnoredDirectories.Contains(rule, NameComparer))
            {
                skippedDirectorySuggestions.Add(rule);
            }
        }

        foreach (var extension in state.ExtensionCounts.Keys)
        {
            if (!ShouldSuggestObservedExtension(extension))
            {
                continue;
            }

            if (!state.Rules.SupportedExtensions.Contains(extension, NameComparer))
            {
                supportedSuggestions.Add(extension);
            }

            if (LanguageByExtension.ContainsKey(extension) && !state.Rules.LocExtensions.Contains(extension, NameComparer))
            {
                locSuggestions.Add(extension);
            }
        }

        var builder = new StringBuilder();
        builder.AppendLine($"Project: {state.ProjectRoot}");
        var ruleSummary = $"{state.Rules.SupportedExtensions.Count:N0} allowed | {state.Rules.LocExtensions.Count:N0} LOC | {state.Rules.IgnoredExtensions.Count:N0} skipped types | {state.Rules.IgnoredDirectories.Count:N0} skipped folders";
        var scanSummary = $"{state.FilesSeen:N0} scanned | {state.VisibleFiles:N0} visible | {state.TrackedFiles:N0} matched | {state.UnsupportedVisibleFiles:N0} unsupported | {state.FilesSkippedByRules:N0} hidden files | {state.DirectoriesSkippedByRules:N0} hidden folders | {state.DirectoriesExcluded:N0} excluded folders";
        builder.AppendLine($"Rules: {ruleSummary}");
        builder.AppendLine($"Scan: {scanSummary}");
        if (state.LimitHit)
        {
            builder.AppendLine($"Limit: stopped at {MaxFiles:N0} files or {MaxDirectories:N0} folders");
        }

        var stackItems = stacks.Select(item =>
            $"{item.Key}: {string.Join(", ", item.Value.Take(4))}{(item.Value.Count > 4 ? ", ..." : "")}").ToArray();
        var useItems = BuildUseItems(state);
        var languageItems = state.LanguageCounts
            .OrderByDescending(item => item.Value)
            .ThenBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .Select(item => $"{item.Key}: {item.Value:N0} files")
            .ToArray();
        var topFileTypeItems = state.ExtensionCounts
            .OrderByDescending(item => item.Value)
            .ThenBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .Select(item => $"{item.Key}: {item.Value:N0}")
            .ToArray();
        var unsupportedItems = state.UnsupportedExtensionCounts
            .OrderByDescending(item => item.Value)
            .ThenBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .Select(item => $"{item.Key}: {item.Value:N0}")
            .ToArray();
        var skippedExtensionItems = state.SkippedExtensionCounts
            .OrderByDescending(item => item.Value)
            .ThenBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .Select(item => $"{item.Key}: {item.Value:N0}")
            .ToArray();

        AppendSection(builder, "Stack", stackItems);
        AppendSection(builder, "Uses", useItems);
        AppendSection(builder, "Languages", languageItems);
        AppendSection(builder, "Manifests", state.ManifestSamples);
        AppendSection(builder, "Top file types", topFileTypeItems);
        AppendSection(builder, "Unsupported visible types", unsupportedItems);
        AppendSection(builder, "Skipped file types", skippedExtensionItems);
        AppendSection(builder, "Already allowed stack file types", coveredSupported);
        AppendSection(builder, "Already counted LOC file types", coveredLoc);
        AppendSection(builder, "Suggested allowed types", supportedSuggestions);
        AppendSection(builder, "Suggested LOC types", locSuggestions);
        AppendSection(builder, "Suggested skipped folders", skippedDirectorySuggestions);
        AppendSection(builder, "Skipped samples", state.SkippedDirectorySamples.Concat(state.SkippedFileSamples).Take(8));

        var metrics = new[]
        {
            new ProjectStackMetric("Stack", stackLabel, stacks.Length == 0 ? "No manifests or language anchors found" : $"{stacks.Length:N0} detected signal(s)"),
            new ProjectStackMetric("Scanned", state.FilesSeen.ToString("N0"), "project files considered"),
            new ProjectStackMetric("Visible", state.VisibleFiles.ToString("N0"), "after skip rules"),
            new ProjectStackMetric("Matched", state.TrackedFiles.ToString("N0"), "allowed code/context files"),
            new ProjectStackMetric("Hidden", (state.FilesSkippedByRules + state.DirectoriesSkippedByRules).ToString("N0"), "current rules hide"),
            new ProjectStackMetric("Excluded", state.DirectoriesExcluded.ToString("N0"), "generated/dependency folders")
        };

        var sections = new List<ProjectStackSection>
        {
            new("Detected Stack", stackItems),
            new("Uses", useItems),
            new("Languages", languageItems),
            new("Manifests", state.ManifestSamples.ToArray()),
            new("Top File Types", topFileTypeItems),
            new("Unsupported Visible Types", unsupportedItems),
            new("Skipped File Types", skippedExtensionItems),
            new("Already Allowed", coveredSupported.ToArray()),
            new("Already Counted LOC", coveredLoc.ToArray()),
            new("Autosetup Plan", BuildAutosetupDeltaItems(supportedSuggestions, locSuggestions, skippedDirectorySuggestions)),
            new("Skipped Samples", state.SkippedDirectorySamples.Concat(state.SkippedFileSamples).Take(8).ToArray())
        };

        return new ProjectStackScanResult(
            summary,
            builder.ToString().TrimEnd(),
            state.ProjectRoot,
            stackLabel,
            ruleSummary,
            scanSummary,
            metrics,
            sections,
            BuildAutoSetupRuleSet(state, stacks.Select(item => item.Key), supportedSuggestions, locSuggestions, skippedDirectorySuggestions));
    }

    private static string[] BuildUseItems(ScanState state)
    {
        return state.UseReasons
            .OrderBy(item => GetUseSortGroup(item.Key))
            .ThenBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .Take(80)
            .Select(item => $"{item.Key}: {string.Join(", ", item.Value.Take(4))}{(item.Value.Count > 4 ? ", ..." : "")}")
            .ToArray();
    }

    private static int GetUseSortGroup(string value)
    {
        if (value.StartsWith("Build tool:", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("Package manager:", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (value.StartsWith("NuGet:", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("npm:", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("pip:", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("Cargo:", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("Carthage:", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("Clojure:", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("Conan:", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("CPAN:", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("Deno:", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("Dune:", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("Elm:", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("Go package:", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("Composer:", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("conda:", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("CocoaPods:", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("Ruby gem:", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("Hackage:", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("Hex:", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("Ivy:", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("JSR:", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("Julia:", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("LuaRocks:", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("pub:", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("Maven:", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("Gradle:", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("Nimble:", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("OPAM:", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("R package:", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("rebar:", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("sbt:", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("SwiftPM:", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("vcpkg:", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("Zig package:", StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        return 1;
    }

    private static void AddCoveredExtensions(
        SortedSet<string> target,
        IReadOnlyDictionary<string, string[]> source,
        string stack,
        IReadOnlyCollection<string> existing)
    {
        if (!source.TryGetValue(stack, out var values))
        {
            return;
        }

        foreach (var value in values.Select(NormalizeExtension).Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            if (existing.Contains(value, NameComparer))
            {
                target.Add(value);
            }
        }
    }

    private static void AddMissingExtensions(
        SortedSet<string> target,
        IReadOnlyDictionary<string, string[]> source,
        string stack,
        IReadOnlyCollection<string> existing)
    {
        if (!source.TryGetValue(stack, out var values))
        {
            return;
        }

        foreach (var value in values.Select(NormalizeExtension).Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            if (!existing.Contains(value, NameComparer))
            {
                target.Add(value);
            }
        }
    }

    private static void AddMissingNames(
        SortedSet<string> target,
        IReadOnlyDictionary<string, string[]> source,
        string stack,
        IReadOnlyCollection<string> existing)
    {
        if (!source.TryGetValue(stack, out var values))
        {
            return;
        }

        foreach (var value in values.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            if (!existing.Contains(value, NameComparer))
            {
                target.Add(value);
            }
        }
    }

    private static IReadOnlyList<string> BuildAutosetupDeltaItems(
        IEnumerable<string> supportedSuggestions,
        IEnumerable<string> locSuggestions,
        IEnumerable<string> skippedDirectorySuggestions)
    {
        var items = new List<string>();
        AddDelta("Allowed types", supportedSuggestions);
        AddDelta("LOC types", locSuggestions);
        AddDelta("Skipped folders", skippedDirectorySuggestions);
        return items.Count == 0 ? ["No missing stack rules detected"] : items;

        void AddDelta(string label, IEnumerable<string> values)
        {
            var clean = values
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Take(14)
                .ToArray();
            if (clean.Length > 0)
            {
                items.Add($"{label}: {string.Join(", ", clean)}");
            }
        }
    }

    private static ProjectStackRuleSet BuildAutoSetupRuleSet(
        ScanState state,
        IEnumerable<string> stacks,
        IEnumerable<string> supportedSuggestions,
        IEnumerable<string> locSuggestions,
        IEnumerable<string> skippedDirectorySuggestions)
    {
        var ignoredDirectories = new SortedSet<string>(AlwaysSkippedDirectories, NameComparer);
        foreach (var value in state.AutoSkippedDirectoryRules)
        {
            ignoredDirectories.Add(value);
        }

        foreach (var stack in stacks)
        {
            AddKnownValues(ignoredDirectories, SuggestedSkippedDirectoriesByStack, stack);
        }

        foreach (var value in skippedDirectorySuggestions)
        {
            ignoredDirectories.Add(value);
        }

        var supportedExtensions = new SortedSet<string>(NameComparer);
        var locExtensions = new SortedSet<string>(NameComparer);
        foreach (var stack in stacks)
        {
            AddKnownExtensions(supportedExtensions, SuggestedSupportedByStack, stack);
            AddKnownExtensions(locExtensions, SuggestedLocByStack, stack);
        }

        foreach (var extension in state.ExtensionCounts.Keys.Where(ShouldSuggestObservedExtension))
        {
            supportedExtensions.Add(NormalizeExtension(extension));
            if (LanguageByExtension.ContainsKey(extension))
            {
                locExtensions.Add(NormalizeExtension(extension));
            }
        }

        foreach (var value in supportedSuggestions)
        {
            supportedExtensions.Add(NormalizeExtension(value));
        }

        foreach (var value in locSuggestions)
        {
            locExtensions.Add(NormalizeExtension(value));
        }

        var ignoredExtensions = new SortedSet<string>(
            AutoSetupIgnoredExtensions.Select(NormalizeExtension).Where(value => !string.IsNullOrWhiteSpace(value)),
            NameComparer);
        foreach (var extension in state.ExtensionCounts.Keys)
        {
            var normalized = NormalizeExtension(extension);
            if (!string.IsNullOrWhiteSpace(normalized) && !supportedExtensions.Contains(normalized))
            {
                ignoredExtensions.Add(normalized);
            }
        }

        foreach (var extension in supportedExtensions)
        {
            ignoredExtensions.Remove(extension);
        }

        return new ProjectStackRuleSet(
            ignoredDirectories.ToArray(),
            AutoSetupIgnoredFileNames.OrderBy(value => value, NameComparer).ToArray(),
            ignoredExtensions.ToArray(),
            supportedExtensions.ToArray(),
            locExtensions.Where(extension => supportedExtensions.Contains(extension)).ToArray());
    }

    private static void AddKnownExtensions(
        SortedSet<string> target,
        IReadOnlyDictionary<string, string[]> source,
        string stack)
    {
        if (!source.TryGetValue(stack, out var values))
        {
            return;
        }

        foreach (var value in values.Select(NormalizeExtension).Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            target.Add(value);
        }
    }

    private static void AddKnownValues(
        SortedSet<string> target,
        IReadOnlyDictionary<string, string[]> source,
        string stack)
    {
        if (!source.TryGetValue(stack, out var values))
        {
            return;
        }

        foreach (var value in values.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            target.Add(value);
        }
    }

    private static void AppendSection(StringBuilder builder, string title, IEnumerable<string> values)
    {
        var items = values.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray();
        if (items.Length == 0)
        {
            return;
        }

        builder.AppendLine();
        builder.AppendLine(title + ":");
        foreach (var item in items)
        {
            builder.AppendLine("  - " + item);
        }
    }

    private static bool StartsWithConfigName(string value, string configName)
    {
        return value.Equals(configName, StringComparison.OrdinalIgnoreCase)
            || value.StartsWith(configName + ".", StringComparison.OrdinalIgnoreCase);
    }
}
