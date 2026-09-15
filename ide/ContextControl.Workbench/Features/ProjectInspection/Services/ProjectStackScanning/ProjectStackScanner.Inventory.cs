// CC-DESC: Classifies source files independently of project visibility rules.
namespace ContextControl.Workbench.Services;

public static partial class ProjectStackScanner
{
    // Only private metadata/caches are excluded from inventory. Names such as build,
    // vendor, bin or external are classifications, never evidence that code is absent.
    internal static readonly HashSet<string> InventoryMetadataDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".svn", ".hg", ".ccReplace.versions", ".gptReplace.versions",
        ".ccWorkbench.browser-data", ".ccWorkbench.generated-projects", ".tmp", ".tmp-build",
        ".vs", ".cache", "__pycache__", ".mypy_cache", ".pytest_cache", ".ruff_cache",
        ".godot", ".import", ".next", ".nuxt", ".svelte-kit", ".angular", ".expo", ".turbo"
    };

    internal static readonly HashSet<string> DependencyDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "node_modules", "vendor", "third_party", "thirdparty", "external", "extern", "deps", "dependencies",
        "packages", "vcpkg_installed", "Pods", ".venv", "venv", "site-packages", "PackageCache", "_deps",
        ".nuget", ".pnpm-store", ".yarn", ".m2", ".gradle", ".conan", ".conan2", ".dart_tool"
    };

    public static bool IsNamedSourceFile(string fileName) => CodeFileNames.ContainsKey(fileName)
        || fileName.StartsWith("Dockerfile.", StringComparison.OrdinalIgnoreCase);

    private static bool IsDependencyDirectory(DirectoryInfo directory, string relativePath, ProjectFileRules rules)
    {
        // Generic names also occur in authored application modules. The project's
        // folder rules decide their ownership; package-manager stores are unambiguous.
        if (!DependencyDirectories.Contains(directory.Name)) return false;
        return directory.Name.ToLowerInvariant() is "node_modules" or ".venv" or "venv" or "site-packages"
            or "vcpkg_installed" or ".nuget" or ".pnpm-store" or ".yarn" or ".m2" or ".gradle" or ".conan" or ".conan2" or ".dart_tool"
            || rules.ShouldSkipDirectory(directory.Name, relativePath);
    }

    private static readonly Dictionary<string, string> AdditionalCodeExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        [".inc"] = "Include", [".inl"] = "C++ inline", [".ipp"] = "C++ template", [".tpp"] = "C++ template",
        [".ixx"] = "C++ module", [".cppm"] = "C++ module", [".c++"] = "C++", [".h++"] = "C++ header",
        [".fc"] = "FunC", [".func"] = "FunC", [".fif"] = "Fift", [".tlb"] = "TL-B", [".tl"] = "TL",
        [".tact"] = "Tact", [".tolk"] = "Tolk", [".sol"] = "Solidity", [".vy"] = "Vyper", [".move"] = "Move",
        [".cmake"] = "CMake", [".make"] = "Make", [".mk"] = "Make", [".dockerfile"] = "Dockerfile",
        [".gdshader"] = "Shader", [".gdshaderinc"] = "Shader", [".rgen"] = "Shader", [".rchit"] = "Shader",
        [".rahit"] = "Shader", [".rmiss"] = "Shader", [".rint"] = "Shader", [".rcall"] = "Shader", [".fx"] = "Shader", [".fxh"] = "Shader",
        [".cl"] = "OpenCL", [".proto"] = "Protocol Buffers", [".graphql"] = "GraphQL", [".gql"] = "GraphQL",
        [".f"] = "Fortran", [".f90"] = "Fortran", [".f95"] = "Fortran", [".f03"] = "Fortran", [".f08"] = "Fortran", [".for"] = "Fortran",
        [".v"] = "Verilog / V", [".sv"] = "SystemVerilog", [".svh"] = "SystemVerilog", [".vhd"] = "VHDL", [".vhdl"] = "VHDL",
        [".d"] = "D", [".di"] = "D", [".odin"] = "Odin", [".jai"] = "Jai", [".nix"] = "Nix",
        [".tcl"] = "Tcl", [".rkt"] = "Racket", [".scm"] = "Scheme", [".lisp"] = "Lisp", [".el"] = "Emacs Lisp",
        [".clj"] = "Clojure", [".cr"] = "Crystal", [".purs"] = "PureScript", [".gleam"] = "Gleam",
        [".vbs"] = "VBScript", [".ahk"] = "AutoHotkey", [".au3"] = "AutoIt", [".fish"] = "Shell", [".ksh"] = "Shell",
        [".less"] = "Less", [".sass"] = "Sass", [".htm"] = "HTML", [".phtml"] = "PHP", [".ejs"] = "EJS",
        [".hbs"] = "Handlebars", [".erb"] = "ERB", [".twig"] = "Twig", [".mdx"] = "MDX",
        [".ipynb"] = "Jupyter", [".cu"] = "CUDA", [".slnx"] = "Build configuration"
    };

    private static readonly Dictionary<string, string> CodeFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["CMakeLists.txt"] = "CMake", ["Makefile"] = "Make", ["GNUmakefile"] = "Make", ["Dockerfile"] = "Dockerfile",
        ["Containerfile"] = "Dockerfile", ["Jenkinsfile"] = "Groovy", ["Rakefile"] = "Ruby", ["Gemfile"] = "Ruby",
        ["Vagrantfile"] = "Ruby", ["BUILD"] = "Starlark", ["WORKSPACE"] = "Starlark", ["SConstruct"] = "Python", ["SConscript"] = "Python"
    };

    private static string GetCodeLanguage(FileInfo file, ScanState state)
    {
        if (CodeFileNames.TryGetValue(file.Name, out var language)) return language;
        if (file.Name.StartsWith("Dockerfile.", StringComparison.OrdinalIgnoreCase)) return "Dockerfile";
        if (LanguageByExtension.TryGetValue(file.Extension, out language)) return language;
        if (state.Rules.ShouldCountLocExtension(file.Extension)) return "Custom code";
        if (file.Extension.Length == 0)
        {
            try
            {
                using var stream = file.OpenRead();
                Span<byte> prefix = stackalloc byte[128];
                var count = stream.Read(prefix);
                if (count > 2 && prefix[0] == '#' && prefix[1] == '!') return "Script";
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return "";
    }

}
