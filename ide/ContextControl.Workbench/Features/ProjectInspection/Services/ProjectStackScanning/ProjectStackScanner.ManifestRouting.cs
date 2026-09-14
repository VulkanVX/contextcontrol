// CC-DESC: Deterministically detects project stacks and current file-rule coverage.

using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace ContextControl.Workbench.Services;

public static partial class ProjectStackScanner
{
    private static void DetectManifestSignals(FileInfo file, string relativePath, ScanState state)
    {
        var lowerName = file.Name.ToLowerInvariant();
        var extension = NormalizeExtension(file.Extension);

        if (string.Equals(lowerName, "package.json", StringComparison.Ordinal))
        {
            AddManifest(state, relativePath);
            AddStack(state, "Node.js", relativePath);
            AddUse(state, "Package manager: npm", relativePath);
            DetectPackageJson(file, state);
            return;
        }

        if (lowerName is "package-lock.json" or "npm-shrinkwrap.json")
        {
            AddManifest(state, relativePath);
            AddUse(state, "Package manager: npm", relativePath);
            return;
        }

        if (lowerName is "pnpm-lock.yaml" or "pnpm-lock.yml")
        {
            AddManifest(state, relativePath);
            AddUse(state, "Package manager: pnpm", relativePath);
            return;
        }

        if (lowerName is "yarn.lock")
        {
            AddManifest(state, relativePath);
            AddUse(state, "Package manager: Yarn", relativePath);
            return;
        }

        if (lowerName is "bun.lockb" or "bun.lock")
        {
            AddManifest(state, relativePath);
            AddUse(state, "Package manager: Bun", relativePath);
            return;
        }

        if (lowerName is "deno.json" or "deno.jsonc" or "deno.lock" or "jsr.json" or "jsr.jsonc")
        {
            AddManifest(state, relativePath);
            AddStack(state, "Deno", relativePath);
            AddStack(state, "TypeScript", relativePath);
            AddUse(state, lowerName.StartsWith("jsr.", StringComparison.Ordinal) ? "Package manager: JSR" : "Runtime: Deno", relativePath);
            DetectDenoManifest(file, lowerName, state);
            return;
        }

        if (string.Equals(lowerName, "tsconfig.json", StringComparison.Ordinal))
        {
            AddManifest(state, relativePath);
            AddStack(state, "TypeScript", relativePath);
            AddUse(state, "TypeScript", relativePath);
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
            AddUse(state, "Package manager: NuGet", relativePath);
            DetectDotNetProject(file, relativePath, state);
            return;
        }

        if (extension is ".sln" or ".slnx"
            || string.Equals(lowerName, "packages.config", StringComparison.Ordinal)
            || string.Equals(lowerName, "global.json", StringComparison.Ordinal)
            || string.Equals(lowerName, "directory.build.props", StringComparison.Ordinal)
            || string.Equals(lowerName, "directory.build.targets", StringComparison.Ordinal)
            || string.Equals(lowerName, "directory.packages.props", StringComparison.Ordinal))
        {
            AddManifest(state, relativePath);
            AddStack(state, ".NET", relativePath);
            AddUse(state, ".NET", relativePath);
            if (string.Equals(lowerName, "packages.config", StringComparison.Ordinal))
            {
                AddUse(state, "Package manager: NuGet", relativePath);
                DetectDotNetPackagesConfig(file, state);
            }

            DetectDotNetPackageProps(file, state);
            return;
        }

        if (string.Equals(lowerName, "pyproject.toml", StringComparison.Ordinal)
            || IsRequirementsFileName(lowerName)
            || string.Equals(lowerName, "poetry.lock", StringComparison.Ordinal)
            || string.Equals(lowerName, "pdm.lock", StringComparison.Ordinal)
            || string.Equals(lowerName, "uv.lock", StringComparison.Ordinal)
            || string.Equals(lowerName, "pipfile", StringComparison.Ordinal)
            || string.Equals(lowerName, "setup.py", StringComparison.Ordinal)
            || string.Equals(lowerName, "setup.cfg", StringComparison.Ordinal)
            || string.Equals(lowerName, "tox.ini", StringComparison.Ordinal)
            || string.Equals(lowerName, "environment.yml", StringComparison.Ordinal)
            || string.Equals(lowerName, "environment.yaml", StringComparison.Ordinal)
            || string.Equals(lowerName, "conda.yml", StringComparison.Ordinal)
            || string.Equals(lowerName, "conda.yaml", StringComparison.Ordinal))
        {
            AddManifest(state, relativePath);
            AddStack(state, "Python", relativePath);
            AddUse(state, "Package manager: Python", relativePath);
            DetectPythonManifest(file, lowerName, state);
            return;
        }

        if (string.Equals(lowerName, "cargo.toml", StringComparison.Ordinal)
            || string.Equals(lowerName, "cargo.lock", StringComparison.Ordinal))
        {
            AddManifest(state, relativePath);
            AddStack(state, "Rust", relativePath);
            AddUse(state, "Package manager: Cargo", relativePath);
            DetectCargoManifest(file, lowerName, state);
            return;
        }

        if (string.Equals(lowerName, "go.mod", StringComparison.Ordinal)
            || string.Equals(lowerName, "go.sum", StringComparison.Ordinal))
        {
            AddManifest(state, relativePath);
            AddStack(state, "Go", relativePath);
            AddUse(state, "Package manager: Go modules", relativePath);
            DetectGoManifest(file, lowerName, state);
            return;
        }

        if (string.Equals(lowerName, "pom.xml", StringComparison.Ordinal)
            || string.Equals(lowerName, "build.gradle", StringComparison.Ordinal)
            || string.Equals(lowerName, "build.gradle.kts", StringComparison.Ordinal)
            || string.Equals(lowerName, "settings.gradle", StringComparison.Ordinal)
            || string.Equals(lowerName, "settings.gradle.kts", StringComparison.Ordinal)
            || string.Equals(lowerName, "build.sbt", StringComparison.Ordinal)
            || string.Equals(lowerName, "ivy.xml", StringComparison.Ordinal))
        {
            AddManifest(state, relativePath);
            if (lowerName.EndsWith(".sbt", StringComparison.Ordinal))
            {
                AddStack(state, "Scala", relativePath);
                AddUse(state, "Build tool: sbt", relativePath);
                DetectScalaManifest(file, state);
                return;
            }

            if (lowerName == "ivy.xml")
            {
                AddStack(state, "Java", relativePath);
                AddUse(state, "Package manager: Ivy", relativePath);
                DetectIvyManifest(file, state);
                return;
            }

            AddStack(state, string.Equals(extension, ".kts", StringComparison.OrdinalIgnoreCase) ? "Kotlin" : "Java", relativePath);
            AddUse(state, lowerName == "pom.xml" ? "Build tool: Maven" : "Build tool: Gradle", relativePath);
            DetectJavaManifest(file, lowerName, state);
            return;
        }

        if (string.Equals(lowerName, "composer.json", StringComparison.Ordinal)
            || string.Equals(lowerName, "composer.lock", StringComparison.Ordinal))
        {
            AddManifest(state, relativePath);
            AddStack(state, "PHP", relativePath);
            AddUse(state, "Package manager: Composer", relativePath);
            DetectComposerManifest(file, lowerName, state);
            return;
        }

        if (string.Equals(lowerName, "gemfile", StringComparison.Ordinal)
            || string.Equals(lowerName, "gemfile.lock", StringComparison.Ordinal)
            || extension == ".gemspec")
        {
            AddManifest(state, relativePath);
            AddStack(state, "Ruby", relativePath);
            AddUse(state, "Package manager: Bundler", relativePath);
            DetectRubyManifest(file, state);
            return;
        }

        if (string.Equals(lowerName, "deps.edn", StringComparison.Ordinal)
            || string.Equals(lowerName, "project.clj", StringComparison.Ordinal)
            || string.Equals(lowerName, "build.boot", StringComparison.Ordinal))
        {
            AddManifest(state, relativePath);
            AddStack(state, "Clojure", relativePath);
            AddUse(state, "Package manager: Clojure", relativePath);
            DetectClojureManifest(file, state);
            return;
        }

        if (string.Equals(lowerName, "mix.exs", StringComparison.Ordinal)
            || string.Equals(lowerName, "mix.lock", StringComparison.Ordinal))
        {
            AddManifest(state, relativePath);
            AddStack(state, "Elixir", relativePath);
            AddUse(state, "Package manager: Hex", relativePath);
            DetectElixirManifest(file, state);
            return;
        }

        if (string.Equals(lowerName, "rebar.config", StringComparison.Ordinal)
            || string.Equals(lowerName, "rebar.lock", StringComparison.Ordinal))
        {
            AddManifest(state, relativePath);
            AddStack(state, "Erlang", relativePath);
            AddUse(state, "Package manager: rebar3", relativePath);
            DetectErlangManifest(file, state);
            return;
        }

        if (extension == ".rockspec")
        {
            AddManifest(state, relativePath);
            AddStack(state, "Lua", relativePath);
            AddUse(state, "Package manager: LuaRocks", relativePath);
            DetectLuaRocksManifest(file, state);
            return;
        }

        if (string.Equals(lowerName, "cpanfile", StringComparison.Ordinal)
            || string.Equals(lowerName, "makefile.pl", StringComparison.Ordinal)
            || string.Equals(lowerName, "build.pl", StringComparison.Ordinal))
        {
            AddManifest(state, relativePath);
            AddStack(state, "Perl", relativePath);
            AddUse(state, "Package manager: CPAN", relativePath);
            DetectPerlManifest(file, state);
            return;
        }

        if (string.Equals(lowerName, "package.swift", StringComparison.Ordinal)
            || string.Equals(lowerName, "package.resolved", StringComparison.Ordinal)
            || string.Equals(lowerName, "podfile", StringComparison.Ordinal)
            || string.Equals(lowerName, "podfile.lock", StringComparison.Ordinal)
            || string.Equals(lowerName, "cartfile", StringComparison.Ordinal)
            || string.Equals(lowerName, "cartfile.resolved", StringComparison.Ordinal))
        {
            AddManifest(state, relativePath);
            AddStack(state, "Swift", relativePath);
            AddUse(state, lowerName.StartsWith("podfile", StringComparison.Ordinal) ? "Package manager: CocoaPods" : "Package manager: SwiftPM", relativePath);
            DetectAppleManifest(file, lowerName, state);
            return;
        }

        if ((string.Equals(lowerName, "description", StringComparison.Ordinal) && LooksLikeRDescription(file))
            || string.Equals(lowerName, "renv.lock", StringComparison.Ordinal)
            || string.Equals(lowerName, "packrat.lock", StringComparison.Ordinal)
            || extension == ".rproj")
        {
            AddManifest(state, relativePath);
            AddStack(state, "R", relativePath);
            AddUse(state, "Package manager: R", relativePath);
            DetectRManifest(file, lowerName, state);
            return;
        }

        if (string.Equals(lowerName, "project.toml", StringComparison.Ordinal)
            || string.Equals(lowerName, "manifest.toml", StringComparison.Ordinal))
        {
            if (LooksLikeJuliaManifest(file))
            {
                AddManifest(state, relativePath);
                AddStack(state, "Julia", relativePath);
                AddUse(state, "Package manager: Julia Pkg", relativePath);
                DetectJuliaManifest(file, state);
                return;
            }
        }

        if (extension == ".cabal"
            || string.Equals(lowerName, "cabal.project", StringComparison.Ordinal)
            || string.Equals(lowerName, "stack.yaml", StringComparison.Ordinal)
            || string.Equals(lowerName, "package.yaml", StringComparison.Ordinal))
        {
            AddManifest(state, relativePath);
            AddStack(state, "Haskell", relativePath);
            AddUse(state, lowerName == "stack.yaml" ? "Build tool: Stack" : "Build tool: Cabal", relativePath);
            DetectHaskellManifest(file, state);
            return;
        }

        if (extension == ".nimble")
        {
            AddManifest(state, relativePath);
            AddStack(state, "Nim", relativePath);
            AddUse(state, "Package manager: Nimble", relativePath);
            DetectNimbleManifest(file, state);
            return;
        }

        if (string.Equals(lowerName, "build.zig", StringComparison.Ordinal)
            || string.Equals(lowerName, "build.zig.zon", StringComparison.Ordinal))
        {
            AddManifest(state, relativePath);
            AddStack(state, "Zig", relativePath);
            AddUse(state, "Build tool: Zig", relativePath);
            DetectZigManifest(file, state);
            return;
        }

        if (string.Equals(lowerName, "dune-project", StringComparison.Ordinal)
            || string.Equals(lowerName, "dune", StringComparison.Ordinal)
            || extension == ".opam")
        {
            AddManifest(state, relativePath);
            AddStack(state, "OCaml", relativePath);
            AddUse(state, "Package manager: OPAM", relativePath);
            DetectOcamlManifest(file, state);
            return;
        }

        if (string.Equals(lowerName, "elm.json", StringComparison.Ordinal))
        {
            AddManifest(state, relativePath);
            AddStack(state, "Elm", relativePath);
            AddUse(state, "Package manager: Elm", relativePath);
            DetectElmManifest(file, state);
            return;
        }

        if (string.Equals(lowerName, "pubspec.yaml", StringComparison.Ordinal)
            || string.Equals(lowerName, "pubspec.yml", StringComparison.Ordinal))
        {
            AddManifest(state, relativePath);
            AddStack(state, "Dart", relativePath);
            AddUse(state, "Package manager: pub", relativePath);
            DetectPubspecPackages(file, state);
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
            || string.Equals(lowerName, "meson.build", StringComparison.Ordinal)
            || string.Equals(lowerName, "conanfile.txt", StringComparison.Ordinal)
            || string.Equals(lowerName, "conanfile.py", StringComparison.Ordinal)
            || string.Equals(lowerName, "vcpkg.json", StringComparison.Ordinal)
            || string.Equals(lowerName, "xmake.lua", StringComparison.Ordinal)
            || string.Equals(lowerName, "premake5.lua", StringComparison.Ordinal)
            || string.Equals(lowerName, "build", StringComparison.Ordinal)
            || string.Equals(lowerName, "build.bazel", StringComparison.Ordinal)
            || string.Equals(lowerName, "workspace", StringComparison.Ordinal)
            || string.Equals(lowerName, "workspace.bazel", StringComparison.Ordinal))
        {
            AddManifest(state, relativePath);
            if (string.Equals(lowerName, "makefile", StringComparison.Ordinal))
            {
                AddUse(state, "Build tool: Make", relativePath);
            }
            else if (string.Equals(lowerName, "meson.build", StringComparison.Ordinal))
            {
                AddUse(state, "Build tool: Meson", relativePath);
            }
            else if (string.Equals(lowerName, "conanfile.txt", StringComparison.Ordinal)
                || string.Equals(lowerName, "conanfile.py", StringComparison.Ordinal))
            {
                AddUse(state, "Package manager: Conan", relativePath);
                DetectConanManifest(file, state);
            }
            else if (string.Equals(lowerName, "vcpkg.json", StringComparison.Ordinal))
            {
                AddUse(state, "Package manager: vcpkg", relativePath);
                DetectVcpkgManifest(file, state);
            }
            else if (string.Equals(lowerName, "xmake.lua", StringComparison.Ordinal))
            {
                AddUse(state, "Build tool: Xmake", relativePath);
            }
            else if (string.Equals(lowerName, "premake5.lua", StringComparison.Ordinal))
            {
                AddUse(state, "Build tool: Premake", relativePath);
            }
            else if (string.Equals(lowerName, "build", StringComparison.Ordinal)
                || string.Equals(lowerName, "build.bazel", StringComparison.Ordinal)
                || string.Equals(lowerName, "workspace", StringComparison.Ordinal)
                || string.Equals(lowerName, "workspace.bazel", StringComparison.Ordinal))
            {
                AddUse(state, "Build tool: Bazel", relativePath);
                AddStack(state, "Bazel", relativePath);
                return;
            }
            else
            {
                AddUse(state, "Build tool: CMake", relativePath);
                DetectCMakeManifest(file, relativePath, state);
            }

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
            AddUse(state, "Container: Docker", relativePath);
            return;
        }

        if (extension == ".tf" || extension == ".tfvars")
        {
            AddManifest(state, relativePath);
            AddStack(state, "Terraform", relativePath);
            AddUse(state, "Infrastructure: Terraform", relativePath);
        }
    }
}
