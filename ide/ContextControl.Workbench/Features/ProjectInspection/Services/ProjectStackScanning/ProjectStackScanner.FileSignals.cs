// CC-DESC: Deterministically detects project stacks and current file-rule coverage.

using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace ContextControl.Workbench.Services;

public static partial class ProjectStackScanner
{
    private static void DetectFileSignals(FileInfo file, string relativePath, ScanState state)
    {
        var name = file.Name;
        var lowerName = name.ToLowerInvariant();
        var extension = NormalizeExtension(file.Extension);

        switch (extension)
        {
            case ".axaml":
                AddStack(state, "Avalonia UI", "*.axaml");
                break;
            case ".asm":
            case ".s":
            case ".nasm":
                AddStack(state, "Assembly", $"*{extension}");
                break;
            case ".bat":
            case ".cmd":
                AddStack(state, "Batch", $"*{extension}");
                break;
            case ".bzl":
                AddStack(state, "Bazel", "*.bzl");
                break;
            case ".cs":
                AddStack(state, "C#", "*.cs");
                AddStack(state, ".NET", "*.cs");
                break;
            case ".fs":
            case ".fsi":
            case ".fsx":
                AddStack(state, "F#", $"*{extension}");
                AddStack(state, ".NET", $"*{extension}");
                break;
            case ".vb":
                AddStack(state, ".NET", $"*{extension}");
                break;
            case ".cpp":
            case ".cc":
            case ".cxx":
            case ".c":
            case ".hpp":
            case ".hh":
            case ".hxx":
            case ".h":
                AddStack(state, "C/C++", $"*{extension}");
                break;
            case ".cu":
            case ".cuh":
                AddStack(state, "CUDA", $"*{extension}");
                AddStack(state, "C/C++", $"*{extension}");
                break;
            case ".clj":
            case ".cljs":
            case ".cljc":
                AddStack(state, "Clojure", $"*{extension}");
                break;
            case ".coffee":
                AddStack(state, "CoffeeScript", "*.coffee");
                AddStack(state, "JavaScript", "*.coffee");
                break;
            case ".dart":
                AddStack(state, "Dart", "*.dart");
                break;
            case ".elm":
                AddStack(state, "Elm", "*.elm");
                break;
            case ".erl":
            case ".hrl":
                AddStack(state, "Erlang", $"*{extension}");
                break;
            case ".ex":
            case ".exs":
                AddStack(state, "Elixir", $"*{extension}");
                break;
            case ".go":
                AddStack(state, "Go", "*.go");
                break;
            case ".gradle":
            case ".groovy":
                AddStack(state, "Groovy", $"*{extension}");
                break;
            case ".hs":
            case ".lhs":
                AddStack(state, "Haskell", $"*{extension}");
                break;
            case ".java":
                AddStack(state, "Java", "*.java");
                break;
            case ".jl":
                AddStack(state, "Julia", "*.jl");
                break;
            case ".js":
            case ".mjs":
            case ".cjs":
                AddStack(state, "JavaScript", $"*{extension}");
                break;
            case ".jsx":
                AddStack(state, "JavaScript", "*.jsx");
                AddStack(state, "React", "*.jsx");
                break;
            case ".kt":
            case ".kts":
                AddStack(state, "Kotlin", $"*{extension}");
                break;
            case ".lua":
                AddStack(state, "Lua", "*.lua");
                break;
            case ".m":
            case ".mm":
                AddStack(state, "Objective-C", $"*{extension}");
                break;
            case ".ml":
            case ".mli":
                AddStack(state, "OCaml", $"*{extension}");
                break;
            case ".nim":
                AddStack(state, "Nim", "*.nim");
                break;
            case ".pas":
                AddStack(state, "Pascal", "*.pas");
                break;
            case ".php":
                AddStack(state, "PHP", "*.php");
                break;
            case ".pl":
            case ".pm":
                AddStack(state, "Perl", $"*{extension}");
                break;
            case ".ps1":
            case ".psm1":
            case ".psd1":
                AddStack(state, "PowerShell", $"*{extension}");
                break;
            case ".py":
                AddStack(state, "Python", "*.py");
                break;
            case ".r":
            case ".rmd":
                AddStack(state, "R", $"*{extension}");
                break;
            case ".raku":
                AddStack(state, "Raku", "*.raku");
                break;
            case ".rb":
                AddStack(state, "Ruby", "*.rb");
                break;
            case ".rs":
                AddStack(state, "Rust", "*.rs");
                break;
            case ".scala":
            case ".sc":
            case ".sbt":
                AddStack(state, "Scala", $"*{extension}");
                break;
            case ".sh":
            case ".bash":
            case ".zsh":
                AddStack(state, "Shell", $"*{extension}");
                break;
            case ".sql":
                AddStack(state, "SQL", "*.sql");
                break;
            case ".swift":
                AddStack(state, "Swift", "*.swift");
                break;
            case ".svelte":
                AddStack(state, "Svelte", "*.svelte");
                break;
            case ".tf":
            case ".tfvars":
            case ".hcl":
                AddStack(state, "Terraform", $"*{extension}");
                break;
            case ".ts":
            case ".tsx":
            case ".mts":
            case ".cts":
                AddStack(state, "TypeScript", $"*{extension}");
                break;
            case ".vue":
                AddStack(state, "Vue", "*.vue");
                break;
            case ".zig":
            case ".zon":
                AddStack(state, "Zig", $"*{extension}");
                break;
        }

        if (string.Equals(lowerName, "package.json", StringComparison.Ordinal))
        {
            AddManifest(state, relativePath);
            AddStack(state, "Node.js", relativePath);
            DetectPackageJson(file, state);
            return;
        }

        if (string.Equals(lowerName, "tsconfig.json", StringComparison.Ordinal))
        {
            AddManifest(state, relativePath);
            AddStack(state, "TypeScript", relativePath);
            return;
        }

        if (StartsWithConfigName(lowerName, "next.config"))
        {
            AddManifest(state, relativePath);
            AddStack(state, "Next.js", relativePath);
            AddStack(state, "React", relativePath);
            AddStack(state, "Node.js", relativePath);
            return;
        }

        if (StartsWithConfigName(lowerName, "vite.config"))
        {
            AddManifest(state, relativePath);
            AddStack(state, "Vite", relativePath);
            AddStack(state, "Node.js", relativePath);
            return;
        }

        if (StartsWithConfigName(lowerName, "svelte.config"))
        {
            AddManifest(state, relativePath);
            AddStack(state, "Svelte", relativePath);
            AddStack(state, "Node.js", relativePath);
            return;
        }

        if (StartsWithConfigName(lowerName, "astro.config"))
        {
            AddManifest(state, relativePath);
            AddStack(state, "Astro", relativePath);
            AddStack(state, "Node.js", relativePath);
            return;
        }

        if (StartsWithConfigName(lowerName, "nuxt.config"))
        {
            AddManifest(state, relativePath);
            AddStack(state, "Nuxt", relativePath);
            AddStack(state, "Vue", relativePath);
            AddStack(state, "Node.js", relativePath);
            return;
        }

        if (string.Equals(lowerName, "angular.json", StringComparison.Ordinal))
        {
            AddManifest(state, relativePath);
            AddStack(state, "Angular", relativePath);
            AddStack(state, "TypeScript", relativePath);
            AddStack(state, "Node.js", relativePath);
            return;
        }

        if (extension is ".csproj" or ".fsproj" or ".vbproj")
        {
            AddManifest(state, relativePath);
            DetectDotNetProject(file, relativePath, state);
            return;
        }

        if (extension == ".sln"
            || string.Equals(lowerName, "global.json", StringComparison.Ordinal)
            || string.Equals(lowerName, "directory.build.props", StringComparison.Ordinal)
            || string.Equals(lowerName, "directory.build.targets", StringComparison.Ordinal))
        {
            AddManifest(state, relativePath);
            AddStack(state, ".NET", relativePath);
            return;
        }

        if (string.Equals(lowerName, "pyproject.toml", StringComparison.Ordinal)
            || string.Equals(lowerName, "requirements.txt", StringComparison.Ordinal)
            || string.Equals(lowerName, "poetry.lock", StringComparison.Ordinal)
            || string.Equals(lowerName, "pipfile", StringComparison.Ordinal)
            || string.Equals(lowerName, "setup.py", StringComparison.Ordinal)
            || string.Equals(lowerName, "setup.cfg", StringComparison.Ordinal))
        {
            AddManifest(state, relativePath);
            AddStack(state, "Python", relativePath);
            return;
        }

        if (string.Equals(lowerName, "cargo.toml", StringComparison.Ordinal)
            || string.Equals(lowerName, "cargo.lock", StringComparison.Ordinal))
        {
            AddManifest(state, relativePath);
            AddStack(state, "Rust", relativePath);
            return;
        }

        if (string.Equals(lowerName, "go.mod", StringComparison.Ordinal)
            || string.Equals(lowerName, "go.sum", StringComparison.Ordinal))
        {
            AddManifest(state, relativePath);
            AddStack(state, "Go", relativePath);
            return;
        }

        if (string.Equals(lowerName, "pom.xml", StringComparison.Ordinal)
            || string.Equals(lowerName, "build.gradle", StringComparison.Ordinal)
            || string.Equals(lowerName, "build.gradle.kts", StringComparison.Ordinal)
            || string.Equals(lowerName, "settings.gradle", StringComparison.Ordinal)
            || string.Equals(lowerName, "settings.gradle.kts", StringComparison.Ordinal))
        {
            AddManifest(state, relativePath);
            AddStack(state, string.Equals(extension, ".kts", StringComparison.OrdinalIgnoreCase) ? "Kotlin" : "Java", relativePath);
            return;
        }

        if (string.Equals(lowerName, "composer.json", StringComparison.Ordinal)
            || string.Equals(lowerName, "composer.lock", StringComparison.Ordinal))
        {
            AddManifest(state, relativePath);
            AddStack(state, "PHP", relativePath);
            return;
        }

        if (string.Equals(lowerName, "gemfile", StringComparison.Ordinal)
            || string.Equals(lowerName, "gemfile.lock", StringComparison.Ordinal)
            || extension == ".gemspec")
        {
            AddManifest(state, relativePath);
            AddStack(state, "Ruby", relativePath);
            return;
        }

        if (string.Equals(lowerName, "pubspec.yaml", StringComparison.Ordinal)
            || string.Equals(lowerName, "pubspec.yml", StringComparison.Ordinal))
        {
            AddManifest(state, relativePath);
            AddStack(state, "Dart", relativePath);
            DetectPubspec(file, state);
            return;
        }

        if (string.Equals(lowerName, "project.godot", StringComparison.Ordinal))
        {
            AddManifest(state, relativePath);
            AddStack(state, "Godot", relativePath);
            return;
        }

        if (string.Equals(lowerName, "cmakelists.txt", StringComparison.Ordinal)
            || extension == ".cmake"
            || string.Equals(lowerName, "makefile", StringComparison.Ordinal)
            || string.Equals(lowerName, "meson.build", StringComparison.Ordinal))
        {
            AddManifest(state, relativePath);
            AddStack(state, "C/C++", relativePath);
            return;
        }

        if (string.Equals(lowerName, "dockerfile", StringComparison.Ordinal)
            || lowerName.EndsWith(".dockerfile", StringComparison.Ordinal)
            || string.Equals(lowerName, "docker-compose.yml", StringComparison.Ordinal)
            || string.Equals(lowerName, "docker-compose.yaml", StringComparison.Ordinal)
            || string.Equals(lowerName, "compose.yml", StringComparison.Ordinal)
            || string.Equals(lowerName, "compose.yaml", StringComparison.Ordinal))
        {
            AddManifest(state, relativePath);
            AddStack(state, "Docker", relativePath);
        }
    }

    private static void DetectTextUseSignals(FileInfo file, string relativePath, string extension, ScanState state)
    {
        if (state.TextSignalFilesScanned >= MaxTextSignalFiles
            || file.Length > MaxTextSignalReadBytes
            || !ShouldReadTextSignals(file.Name, extension))
        {
            return;
        }

        var text = TryReadSmallText(file);
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        state.TextSignalFilesScanned++;
        foreach (var pattern in TextUsePatterns)
        {
            if (pattern.Extensions.Contains(extension, NameComparer)
                && pattern.Needles.Any(needle => text.Contains(needle, StringComparison.OrdinalIgnoreCase)))
            {
                AddUse(state, pattern.Name, relativePath);
            }
        }
    }

    private static bool ShouldReadTextSignals(string fileName, string extension)
    {
        if (TextSignalExtensions.Contains(extension))
        {
            return true;
        }

        var lowerName = fileName.ToLowerInvariant();
        return lowerName is "cmakelists.txt"
            or "makefile"
            or "dockerfile"
            or "gemfile"
            or "pipfile";
    }
}
