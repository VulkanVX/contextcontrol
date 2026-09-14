// CC-DESC: Deterministically detects project stacks and current file-rule coverage.

using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace ContextControl.Workbench.Services;

public static partial class ProjectStackScanner
{
    private static JsonDocument? TryReadJsonDocument(FileInfo file)
    {
        try
        {
            if (file.Length > MaxManifestReadBytes)
            {
                return null;
            }

            return JsonDocument.Parse(File.ReadAllText(file.FullName));
        }
        catch
        {
            return null;
        }
    }

    private static string TryReadSmallText(FileInfo file)
    {
        try
        {
            return file.Length > MaxManifestReadBytes ? "" : File.ReadAllText(file.FullName);
        }
        catch
        {
            return "";
        }
    }

    private static void AddManifest(ScanState state, string relativePath)
    {
        AddSample(state.ManifestSamples, relativePath, 18);
    }

    private static void AddStack(ScanState state, string stack, string reason)
    {
        if (!state.StackReasons.TryGetValue(stack, out var reasons))
        {
            reasons = new SortedSet<string>(NameComparer);
            state.StackReasons[stack] = reasons;
        }

        if (reasons.Count < 8)
        {
            reasons.Add(reason);
        }
    }

    private static void AddUse(ScanState state, string use, string reason)
    {
        var cleanUse = NormalizeUseLabel(use);
        var cleanReason = string.IsNullOrWhiteSpace(reason) ? "detected" : reason.Trim();
        if (string.IsNullOrWhiteSpace(cleanUse))
        {
            return;
        }

        if (!state.UseReasons.TryGetValue(cleanUse, out var reasons))
        {
            reasons = new SortedSet<string>(NameComparer);
            state.UseReasons[cleanUse] = reasons;
        }

        if (reasons.Count < 8)
        {
            reasons.Add(cleanReason);
        }
    }

    private static void AddSuggestedExtension(ScanState state, string extension, bool isLoc)
    {
        var normalized = NormalizeExtension(extension);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return;
        }

        if (!state.Rules.SupportedExtensions.Contains(normalized, NameComparer))
        {
            state.ExtensionCounts.TryAdd(normalized, 0);
        }

        if (isLoc && !state.Rules.LocExtensions.Contains(normalized, NameComparer))
        {
            state.ExtensionCounts.TryAdd(normalized, 0);
        }
    }

    private static void AddSample(ICollection<string> samples, string value, int max = 10)
    {
        if (samples.Count < max
            && !string.IsNullOrWhiteSpace(value)
            && !samples.Any(sample => string.Equals(sample, value, StringComparison.OrdinalIgnoreCase)))
        {
            samples.Add(value);
        }
    }

    private static void Increment(IDictionary<string, int> map, string key)
    {
        map[key] = map.TryGetValue(key, out var count) ? count + 1 : 1;
    }

    private static string NormalizeUseLabel(string value)
    {
        var clean = (value ?? string.Empty).Trim();
        while (clean.Contains("  ", StringComparison.Ordinal))
        {
            clean = clean.Replace("  ", " ", StringComparison.Ordinal);
        }

        return clean;
    }

    private static string NormalizePackageManagerName(string value)
    {
        var clean = value.Split('@', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault() ?? value;
        return clean.ToLowerInvariant() switch
        {
            "npm" => "npm",
            "pnpm" => "pnpm",
            "yarn" => "Yarn",
            "bun" => "Bun",
            _ => clean
        };
    }

    private static string NormalizeTechnologyName(string value)
    {
        var clean = StripCMakeToken(value);
        if (string.IsNullOrWhiteSpace(clean))
        {
            return "";
        }

        return clean.ToLowerInvariant() switch
        {
            "glfw" or "glfw3" => "GLFW",
            "glm" => "GLM",
            "vulkan" => "Vulkan",
            "jolt" => "Jolt Physics",
            "imgui" or "dearimgui" => "Dear ImGui",
            "opengl" => "OpenGL",
            "sdl" or "sdl2" => "SDL",
            "fmt" => "fmt",
            "spdlog" => "spdlog",
            "entt" => "EnTT",
            "threads" => "Threads",
            _ => clean
        };
    }

    private static bool TryNormalizeLinkedLibrary(string token, out string library)
    {
        library = "";
        var clean = StripCMakeToken(token);
        if (string.IsNullOrWhiteSpace(clean)
            || IsCMakeKeyword(clean)
            || clean.StartsWith("${", StringComparison.Ordinal)
            || clean.StartsWith("$<", StringComparison.Ordinal))
        {
            return false;
        }

        if (clean.Contains("::", StringComparison.Ordinal))
        {
            library = NormalizeTechnologyName(clean.Split("::", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? clean);
            return !string.IsNullOrWhiteSpace(library);
        }

        var normalized = NormalizeTechnologyName(clean);
        if (normalized != clean || IsKnownExternalLibraryName(normalized))
        {
            library = normalized;
            return true;
        }

        return false;
    }

    private static bool IsKnownExternalLibraryName(string value)
    {
        return value is "Vulkan" or "GLFW" or "GLM" or "Jolt Physics" or "Dear ImGui" or "OpenGL" or "SDL" or "fmt" or "spdlog" or "EnTT" or "Threads";
    }

    private static bool IsCMakeKeyword(string value)
    {
        return value.Equals("PRIVATE", StringComparison.OrdinalIgnoreCase)
            || value.Equals("PUBLIC", StringComparison.OrdinalIgnoreCase)
            || value.Equals("INTERFACE", StringComparison.OrdinalIgnoreCase)
            || value.Equals("debug", StringComparison.OrdinalIgnoreCase)
            || value.Equals("optimized", StringComparison.OrdinalIgnoreCase)
            || value.Equals("general", StringComparison.OrdinalIgnoreCase);
    }

    private static string StripCMakeToken(string value)
    {
        return (value ?? string.Empty)
            .Trim()
            .Trim('"', '\'', '(', ')', '[', ']', '{', '}', ',');
    }

    private static IEnumerable<string> SplitCMakeTokens(string value)
    {
        return Regex.Split(value, @"[\s\r\n]+")
            .Select(StripCMakeToken)
            .Where(token => !string.IsNullOrWhiteSpace(token));
    }

    private static bool IsRequirementsFileName(string lowerName)
    {
        return lowerName.Equals("requirements.txt", StringComparison.Ordinal)
            || (lowerName.StartsWith("requirements", StringComparison.Ordinal)
                && lowerName.EndsWith(".txt", StringComparison.Ordinal));
    }

    private static IEnumerable<string> ExtractRequirementsPackages(string text)
    {
        foreach (var line in SplitTextLines(text))
        {
            var clean = line.Split('#')[0].Trim();
            if (string.IsNullOrWhiteSpace(clean)
                || clean.StartsWith("-", StringComparison.Ordinal)
                || clean.StartsWith("git+", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var match = Regex.Match(clean, @"^([A-Za-z0-9_.-]+)");
            if (match.Success)
            {
                yield return match.Groups[1].Value;
            }
        }
    }

    private static IEnumerable<string> ExtractPythonLockPackageNames(string text)
    {
        foreach (Match match in Regex.Matches(text, @"^\s*name\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase | RegexOptions.Multiline))
        {
            yield return match.Groups[1].Value;
        }
    }

    private static IEnumerable<string> ExtractYamlDependencyNames(string text)
    {
        foreach (var line in SplitTextLines(text))
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("-", StringComparison.Ordinal))
            {
                continue;
            }

            var clean = trimmed[1..].Trim();
            if (clean.StartsWith("pip:", StringComparison.OrdinalIgnoreCase)
                || clean.StartsWith("python", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var match = Regex.Match(clean, @"^([A-Za-z0-9_.-]+)");
            if (match.Success)
            {
                yield return match.Groups[1].Value;
            }
        }
    }

    private static IEnumerable<string> ExtractTomlDependencyNames(string text)
    {
        foreach (var package in ExtractTomlSectionKeys(text, "tool.poetry.dependencies", "project.optional-dependencies"))
        {
            yield return package;
        }

        foreach (Match match in Regex.Matches(text, @"dependencies\s*=\s*\[(?<body>[^\]]{1,5000})\]", RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            foreach (Match packageMatch in Regex.Matches(match.Groups["body"].Value, @"[""']([A-Za-z0-9_.-]+)"))
            {
                var package = packageMatch.Groups[1].Value;
                if (!package.Equals("python", StringComparison.OrdinalIgnoreCase))
                {
                    yield return package;
                }
            }
        }
    }

    private static IEnumerable<string> ExtractDescriptionPackages(string text)
    {
        foreach (Match match in Regex.Matches(text, @"^(?:Depends|Imports|Suggests|LinkingTo):\s*(?<body>[^\r\n]+(?:\r?\n\s+[^\r\n]+)*)", RegexOptions.IgnoreCase | RegexOptions.Multiline))
        {
            foreach (var package in SplitDependencyList(match.Groups["body"].Value))
            {
                if (!package.Equals("R", StringComparison.OrdinalIgnoreCase))
                {
                    yield return package;
                }
            }
        }
    }

    private static IEnumerable<string> ExtractYamlListValues(string text, params string[] sectionNames)
    {
        var active = false;
        var sections = sectionNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var line in SplitTextLines(text))
        {
            var trimmed = line.Trim();
            if (Regex.IsMatch(trimmed, @"^[A-Za-z0-9_-]+:\s*$"))
            {
                active = sections.Contains(trimmed.TrimEnd(':'));
                continue;
            }

            if (!active || !trimmed.StartsWith("-", StringComparison.Ordinal))
            {
                continue;
            }

            var clean = trimmed[1..].Trim();
            if (!string.IsNullOrWhiteSpace(clean))
            {
                yield return ShortPackageName(clean);
            }
        }
    }

    private static IEnumerable<string> SplitDependencyList(string value)
    {
        foreach (var token in Regex.Split(value ?? string.Empty, @"[,\s]+"))
        {
            var clean = token.Trim()
                .Trim('"', '\'', '(', ')', '[', ']', '{', '}', ',')
                .Split(['<', '>', '=', '~'], 2, StringSplitOptions.TrimEntries)
                .FirstOrDefault() ?? "";
            if (LooksLikePackageToken(clean))
            {
                yield return ShortPackageName(clean);
            }
        }
    }

    private static bool LooksLikePackageToken(string value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || char.IsDigit(value[0])
            || value is "and" or "or" or "&&" or "||")
        {
            return false;
        }

        return Regex.IsMatch(value, @"^[A-Za-z_@][A-Za-z0-9_.+/@-]*$");
    }

    private static string ShortPackageName(string value)
    {
        var clean = (value ?? string.Empty).Trim().Trim('"', '\'', ',', ';');
        if (clean.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            clean = clean[..^4];
        }

        clean = clean.Replace('\\', '/');
        var last = clean.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault();
        return string.IsNullOrWhiteSpace(last) ? clean : last;
    }

    private static IEnumerable<string> ExtractTomlSectionKeys(string text, params string[] sectionNames)
    {
        var active = false;
        var sections = sectionNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var line in SplitTextLines(text))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("[", StringComparison.Ordinal) && trimmed.EndsWith("]", StringComparison.Ordinal))
            {
                var section = trimmed.Trim('[', ']');
                active = sections.Contains(section);
                continue;
            }

            if (!active || trimmed.StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }

            var match = Regex.Match(trimmed, @"^([A-Za-z0-9_.-]+)\s*=");
            if (match.Success)
            {
                var package = match.Groups[1].Value;
                if (!package.Equals("python", StringComparison.OrdinalIgnoreCase))
                {
                    yield return package;
                }
            }
        }
    }

    private static IEnumerable<string> SplitTextLines(string text)
    {
        return (text ?? string.Empty).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static string NormalizeExtension(string extension)
    {
        var clean = string.IsNullOrWhiteSpace(extension) ? "" : extension.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(clean))
        {
            return "";
        }

        return clean.StartsWith(".", StringComparison.Ordinal) ? clean : "." + clean;
    }

    private static string NormalizePath(string path)
    {
        var normalized = (path ?? string.Empty).Trim().Replace('\\', '/');
        while (normalized.StartsWith("./", StringComparison.Ordinal))
        {
            normalized = normalized[2..];
        }

        return normalized.Trim('/');
    }

    private static bool ShouldSuggestObservedExtension(string extension)
    {
        return SourceOrConfigSuggestionExtensions.Contains(NormalizeExtension(extension));
    }

    private static HashSet<string> BuildSourceOrConfigSuggestionExtensions()
    {
        var extensions = new HashSet<string>(NameComparer);
        foreach (var extension in LanguageByExtension.Keys)
        {
            extensions.Add(extension);
        }

        foreach (var extension in new[]
        {
            ".asmdef",
            ".bat",
            ".cfg",
            ".cmake",
            ".cmd",
            ".config",
            ".cabal",
            ".dockerignore",
            ".editorconfig",
            ".edn",
            ".env",
            ".gemspec",
            ".gitignore",
            ".gradle",
            ".jsonc",
            ".json",
            ".lock",
            ".make",
            ".md",
            ".mod",
            ".nimble",
            ".opam",
            ".props",
            ".rproj",
            ".rockspec",
            ".sbt",
            ".sln",
            ".slnx",
            ".sum",
            ".targets",
            ".toml",
            ".xml",
            ".yaml",
            ".yml"
        })
        {
            extensions.Add(extension);
        }

        return extensions;
    }
}
