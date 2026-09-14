// CC-DESC: Deterministically detects project stacks and current file-rule coverage.

using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace ContextControl.Workbench.Services;

public static partial class ProjectStackScanner
{
    private static void DetectPackageJson(FileInfo file, ScanState state)
    {
        using var document = TryReadJsonDocument(file);
        if (document is null)
        {
            return;
        }

        var root = document.RootElement;
        var packages = new HashSet<string>(NameComparer);
        foreach (var propertyName in new[] { "dependencies", "devDependencies", "peerDependencies", "optionalDependencies" })
        {
            if (!root.TryGetProperty(propertyName, out var dependencies) || dependencies.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            foreach (var dependency in dependencies.EnumerateObject())
            {
                packages.Add(dependency.Name);
            }
        }

        if (root.TryGetProperty("packageManager", out var packageManager)
            && packageManager.ValueKind == JsonValueKind.String
            && packageManager.GetString() is { } packageManagerName)
        {
            AddStack(state, "Node.js", packageManagerName);
            AddUse(state, "Package manager: " + NormalizePackageManagerName(packageManagerName), "packageManager");
        }

        foreach (var package in packages.Take(80))
        {
            AddUse(state, "npm: " + package, "package.json dependency");
        }

        if (packages.Contains("next"))
        {
            AddStack(state, "Next.js", "next dependency");
            AddStack(state, "React", "next dependency");
        }

        if (packages.Contains("react") || packages.Contains("react-dom"))
        {
            AddStack(state, "React", "react dependency");
        }

        if (packages.Contains("vue"))
        {
            AddStack(state, "Vue", "vue dependency");
        }

        if (packages.Contains("svelte") || packages.Contains("@sveltejs/kit"))
        {
            AddStack(state, "Svelte", "svelte dependency");
        }

        if (packages.Contains("vite"))
        {
            AddStack(state, "Vite", "vite dependency");
        }

        if (packages.Contains("@angular/core"))
        {
            AddStack(state, "Angular", "@angular/core dependency");
        }

        if (packages.Contains("astro"))
        {
            AddStack(state, "Astro", "astro dependency");
        }

        if (packages.Contains("nuxt"))
        {
            AddStack(state, "Nuxt", "nuxt dependency");
            AddStack(state, "Vue", "nuxt dependency");
        }

        if (packages.Contains("typescript") || packages.Any(name => name.StartsWith("@types/", StringComparison.OrdinalIgnoreCase)))
        {
            AddStack(state, "TypeScript", "typescript dependency");
        }

        if (packages.Contains("electron"))
        {
            AddStack(state, "Electron", "electron dependency");
        }
    }

    private static void DetectDenoManifest(FileInfo file, string lowerName, ScanState state)
    {
        var text = TryReadSmallText(file);
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        foreach (var match in Regex.Matches(text, @"[""']((?:npm|jsr):[^""']+)[""']", RegexOptions.IgnoreCase).Cast<Match>().Take(80))
        {
            AddUse(state, (lowerName.StartsWith("jsr.", StringComparison.Ordinal) ? "JSR: " : "Deno: ") + match.Groups[1].Value, file.Name);
        }

        foreach (var match in Regex.Matches(text, @"[""'](@?[A-Za-z0-9_.-]+/[A-Za-z0-9_./-]+|@?[A-Za-z0-9_.-]+)[""']\s*:", RegexOptions.IgnoreCase).Cast<Match>().Take(80))
        {
            var package = match.Groups[1].Value;
            if (package is not "imports" and not "tasks" and not "compilerOptions" and not "lint" and not "fmt")
            {
                AddUse(state, (lowerName.StartsWith("jsr.", StringComparison.Ordinal) ? "JSR: " : "Deno import: ") + package, file.Name);
            }
        }
    }

    private static void DetectDotNetProject(FileInfo file, string relativePath, ScanState state)
    {
        AddStack(state, ".NET", relativePath);

        XDocument? document;
        try
        {
            if (file.Length > MaxManifestReadBytes)
            {
                return;
            }

            document = XDocument.Load(file.FullName);
        }
        catch
        {
            return;
        }

        var sdk = document.Root?.Attribute("Sdk")?.Value ?? "";
        if (!string.IsNullOrWhiteSpace(sdk))
        {
            AddUse(state, ".NET SDK: " + sdk, "project sdk");
        }

        if (sdk.Contains("Web", StringComparison.OrdinalIgnoreCase))
        {
            AddStack(state, "ASP.NET Core", "web sdk");
        }

        foreach (var reference in document.Descendants().Where(element => element.Name.LocalName == "PackageReference"))
        {
            var include = reference.Attribute("Include")?.Value
                ?? reference.Attribute("Update")?.Value
                ?? "";
            if (!string.IsNullOrWhiteSpace(include))
            {
                AddUse(state, "NuGet: " + include, "PackageReference");
            }

            if (include.StartsWith("Avalonia", StringComparison.OrdinalIgnoreCase))
            {
                AddStack(state, "Avalonia UI", include);
            }
            else if (include.StartsWith("Microsoft.AspNetCore", StringComparison.OrdinalIgnoreCase)
                || include.Contains("Blazor", StringComparison.OrdinalIgnoreCase))
            {
                AddStack(state, "ASP.NET Core", include);
            }
            else if (include.Contains("Maui", StringComparison.OrdinalIgnoreCase))
            {
                AddStack(state, ".NET MAUI", include);
            }
            else if (include.Contains("Xamarin", StringComparison.OrdinalIgnoreCase))
            {
                AddStack(state, "Xamarin", include);
            }
        }
    }

    private static void DetectDotNetPackagesConfig(FileInfo file, ScanState state)
    {
        XDocument? document;
        try
        {
            if (file.Length > MaxManifestReadBytes)
            {
                return;
            }

            document = XDocument.Load(file.FullName);
        }
        catch
        {
            return;
        }

        foreach (var package in document.Descendants().Where(element => element.Name.LocalName == "package"))
        {
            var id = package.Attribute("id")?.Value ?? "";
            if (!string.IsNullOrWhiteSpace(id))
            {
                AddUse(state, "NuGet: " + id, "packages.config");
            }
        }
    }

    private static void DetectDotNetPackageProps(FileInfo file, ScanState state)
    {
        XDocument? document;
        try
        {
            if (file.Length > MaxManifestReadBytes)
            {
                return;
            }

            document = XDocument.Load(file.FullName);
        }
        catch
        {
            return;
        }

        foreach (var package in document.Descendants().Where(element => element.Name.LocalName == "PackageVersion"))
        {
            var include = package.Attribute("Include")?.Value
                ?? package.Attribute("Update")?.Value
                ?? "";
            if (!string.IsNullOrWhiteSpace(include))
            {
                AddUse(state, "NuGet: " + include, "PackageVersion");
            }
        }
    }

    private static void DetectCMakeManifest(FileInfo file, string relativePath, ScanState state)
    {
        var text = TryReadSmallText(file);
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        foreach (Match match in Regex.Matches(text, @"\bfind_package\s*\(\s*([A-Za-z0-9_.+-]+)", RegexOptions.IgnoreCase))
        {
            AddUse(state, NormalizeTechnologyName(match.Groups[1].Value), $"find_package in {relativePath}");
        }

        foreach (Match match in Regex.Matches(text, @"\bFetchContent_Declare\s*\(\s*([A-Za-z0-9_.+-]+)", RegexOptions.IgnoreCase))
        {
            AddUse(state, NormalizeTechnologyName(match.Groups[1].Value), $"FetchContent in {relativePath}");
        }

        foreach (Match match in Regex.Matches(text, @"\btarget_link_libraries\s*\((?<body>[^)]{1,3000})\)", RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            foreach (var token in SplitCMakeTokens(match.Groups["body"].Value).Skip(1))
            {
                if (TryNormalizeLinkedLibrary(token, out var library))
                {
                    AddUse(state, library, $"target_link_libraries in {relativePath}");
                }
            }
        }

        if (text.Contains("vcpkg.cmake", StringComparison.OrdinalIgnoreCase)
            || text.Contains("CMAKE_TOOLCHAIN_FILE", StringComparison.OrdinalIgnoreCase))
        {
            AddUse(state, "Package manager: vcpkg", relativePath);
        }
    }

    private static void DetectConanManifest(FileInfo file, ScanState state)
    {
        var text = TryReadSmallText(file);
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var inRequires = false;
        foreach (var line in SplitTextLines(text))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("[", StringComparison.Ordinal) && trimmed.EndsWith("]", StringComparison.Ordinal))
            {
                inRequires = trimmed.Equals("[requires]", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (inRequires && !trimmed.StartsWith("#", StringComparison.Ordinal))
            {
                var package = trimmed.Split(['#', ';'], 2)[0].Trim();
                if (!string.IsNullOrWhiteSpace(package))
                {
                    AddUse(state, "Conan: " + package, file.Name);
                }
            }
        }

        foreach (var match in Regex.Matches(text, @"(?:self\.)?requires\s*\(?\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase).Cast<Match>().Take(80))
        {
            AddUse(state, "Conan: " + match.Groups[1].Value, file.Name);
        }
    }

    private static void DetectVcpkgManifest(FileInfo file, ScanState state)
    {
        using var document = TryReadJsonDocument(file);
        if (document is null)
        {
            return;
        }

        if (!document.RootElement.TryGetProperty("dependencies", out var dependencies)
            || dependencies.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var dependency in dependencies.EnumerateArray().Take(80))
        {
            if (dependency.ValueKind == JsonValueKind.String && dependency.GetString() is { } package)
            {
                AddUse(state, "vcpkg: " + package, "vcpkg.json");
            }
            else if (dependency.ValueKind == JsonValueKind.Object
                && dependency.TryGetProperty("name", out var name)
                && name.ValueKind == JsonValueKind.String
                && name.GetString() is { } namedPackage)
            {
                AddUse(state, "vcpkg: " + namedPackage, "vcpkg.json");
            }
        }
    }

    private static void DetectPythonManifest(FileInfo file, string lowerName, ScanState state)
    {
        var text = TryReadSmallText(file);
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        if (IsRequirementsFileName(lowerName))
        {
            AddUse(state, "Package manager: pip", file.Name);
            foreach (var package in ExtractRequirementsPackages(text).Take(80))
            {
                AddUse(state, "pip: " + package, "requirements.txt");
            }

            return;
        }

        if (lowerName == "pipfile")
        {
            AddUse(state, "Package manager: Pipenv", file.Name);
        }

        if (lowerName == "uv.lock")
        {
            AddUse(state, "Package manager: uv", file.Name);
        }

        if (lowerName == "pdm.lock" || text.Contains("[tool.pdm]", StringComparison.OrdinalIgnoreCase))
        {
            AddUse(state, "Package manager: PDM", file.Name);
        }

        if (lowerName is "environment.yml" or "environment.yaml" or "conda.yml" or "conda.yaml")
        {
            AddUse(state, "Package manager: conda", file.Name);
            foreach (var package in ExtractYamlDependencyNames(text).Take(80))
            {
                AddUse(state, "conda: " + package, file.Name);
            }
        }

        if (lowerName == "poetry.lock" || text.Contains("[tool.poetry]", StringComparison.OrdinalIgnoreCase))
        {
            AddUse(state, "Package manager: Poetry", file.Name);
        }

        if (text.Contains("[tool.hatch]", StringComparison.OrdinalIgnoreCase))
        {
            AddUse(state, "Build tool: Hatch", file.Name);
        }

        foreach (var tool in new[] { "pytest", "ruff", "black", "mypy", "isort" })
        {
            if (text.Contains("[tool." + tool, StringComparison.OrdinalIgnoreCase)
                || text.Contains(tool + ">=", StringComparison.OrdinalIgnoreCase))
            {
                AddUse(state, "Python tool: " + tool, file.Name);
            }
        }

        foreach (var package in ExtractTomlDependencyNames(text).Take(80))
        {
            AddUse(state, "Python package: " + package, file.Name);
        }

        foreach (var package in ExtractPythonLockPackageNames(text).Take(80))
        {
            AddUse(state, "Python package: " + package, file.Name);
        }
    }

    private static void DetectCargoManifest(FileInfo file, string lowerName, ScanState state)
    {
        if (lowerName == "cargo.lock")
        {
            return;
        }

        var text = TryReadSmallText(file);
        foreach (var package in ExtractTomlSectionKeys(text, "dependencies", "dev-dependencies", "build-dependencies").Take(80))
        {
            AddUse(state, "Cargo: " + package, "Cargo.toml");
        }
    }

    private static void DetectGoManifest(FileInfo file, string lowerName, ScanState state)
    {
        if (lowerName == "go.sum")
        {
            return;
        }

        var text = TryReadSmallText(file);
        foreach (var line in SplitTextLines(text))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("module ", StringComparison.Ordinal))
            {
                AddUse(state, "Go module: " + trimmed[7..].Trim(), "go.mod");
            }
            else if (trimmed.StartsWith("require ", StringComparison.Ordinal))
            {
                var package = trimmed[8..].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(package) && package != "(")
                {
                    AddUse(state, "Go package: " + package, "go.mod");
                }
            }
            else if (Regex.IsMatch(trimmed, @"^[A-Za-z0-9_.-]+/[A-Za-z0-9_./-]+\s+v\d", RegexOptions.IgnoreCase))
            {
                AddUse(state, "Go package: " + trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0], "go.mod");
            }
        }
    }

    private static void DetectJavaManifest(FileInfo file, string lowerName, ScanState state)
    {
        if (lowerName == "pom.xml")
        {
            DetectMavenManifest(file, state);
            return;
        }

        var text = TryReadSmallText(file);
        foreach (Match match in Regex.Matches(text, @"(?:implementation|api|compileOnly|runtimeOnly|testImplementation)\s*\(?\s*[""']([^:""']+):([^:""']+)", RegexOptions.IgnoreCase))
        {
            AddUse(state, "Gradle: " + match.Groups[1].Value + ":" + match.Groups[2].Value, file.Name);
        }

        foreach (Match match in Regex.Matches(text, @"id\s*\(?\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase))
        {
            AddUse(state, "Gradle plugin: " + match.Groups[1].Value, file.Name);
        }
    }

    private static void DetectScalaManifest(FileInfo file, ScanState state)
    {
        var text = TryReadSmallText(file);
        foreach (var match in Regex.Matches(text, @"""([^""]+)""\s*%%?\s*""([^""]+)""\s*%", RegexOptions.IgnoreCase).Cast<Match>().Take(80))
        {
            AddUse(state, "sbt: " + match.Groups[1].Value + ":" + match.Groups[2].Value, file.Name);
        }

        foreach (var match in Regex.Matches(text, @"addSbtPlugin\s*\(\s*""([^""]+)""\s*%%?\s*""([^""]+)""", RegexOptions.IgnoreCase).Cast<Match>().Take(40))
        {
            AddUse(state, "sbt plugin: " + match.Groups[1].Value + ":" + match.Groups[2].Value, file.Name);
        }
    }

    private static void DetectIvyManifest(FileInfo file, ScanState state)
    {
        XDocument? document;
        try
        {
            if (file.Length > MaxManifestReadBytes)
            {
                return;
            }

            document = XDocument.Load(file.FullName);
        }
        catch
        {
            return;
        }

        foreach (var dependency in document.Descendants().Where(element => element.Name.LocalName == "dependency"))
        {
            var org = dependency.Attribute("org")?.Value ?? "";
            var name = dependency.Attribute("name")?.Value ?? "";
            if (!string.IsNullOrWhiteSpace(org) && !string.IsNullOrWhiteSpace(name))
            {
                AddUse(state, "Ivy: " + org + ":" + name, "ivy.xml");
            }
        }
    }

    private static void DetectMavenManifest(FileInfo file, ScanState state)
    {
        XDocument? document;
        try
        {
            if (file.Length > MaxManifestReadBytes)
            {
                return;
            }

            document = XDocument.Load(file.FullName);
        }
        catch
        {
            return;
        }

        foreach (var dependency in document.Descendants().Where(element => element.Name.LocalName == "dependency"))
        {
            var groupId = dependency.Elements().FirstOrDefault(element => element.Name.LocalName == "groupId")?.Value ?? "";
            var artifactId = dependency.Elements().FirstOrDefault(element => element.Name.LocalName == "artifactId")?.Value ?? "";
            if (!string.IsNullOrWhiteSpace(groupId) && !string.IsNullOrWhiteSpace(artifactId))
            {
                AddUse(state, "Maven: " + groupId + ":" + artifactId, "pom.xml");
            }
        }
    }

    private static void DetectComposerManifest(FileInfo file, string lowerName, ScanState state)
    {
        if (lowerName != "composer.json")
        {
            return;
        }

        using var document = TryReadJsonDocument(file);
        if (document is null)
        {
            return;
        }

        foreach (var section in new[] { "require", "require-dev" })
        {
            if (!document.RootElement.TryGetProperty(section, out var dependencies)
                || dependencies.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            foreach (var dependency in dependencies.EnumerateObject().Take(80))
            {
                AddUse(state, "Composer: " + dependency.Name, "composer.json");
            }
        }
    }

    private static void DetectRubyManifest(FileInfo file, ScanState state)
    {
        var text = TryReadSmallText(file);
        foreach (Match match in Regex.Matches(text, @"\bgem\s+[""']([^""']+)[""']", RegexOptions.IgnoreCase))
        {
            AddUse(state, "Ruby gem: " + match.Groups[1].Value, file.Name);
        }
    }

    private static void DetectClojureManifest(FileInfo file, ScanState state)
    {
        var text = TryReadSmallText(file);
        foreach (var match in Regex.Matches(text, @"([A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+)").Cast<Match>().Take(80))
        {
            AddUse(state, "Clojure: " + match.Groups[1].Value, file.Name);
        }
    }

    private static void DetectElixirManifest(FileInfo file, ScanState state)
    {
        var text = TryReadSmallText(file);
        foreach (var match in Regex.Matches(text, @"\{\s*:([A-Za-z0-9_]+)\s*,", RegexOptions.IgnoreCase).Cast<Match>().Take(80))
        {
            AddUse(state, "Hex: " + match.Groups[1].Value, file.Name);
        }
    }

    private static void DetectErlangManifest(FileInfo file, ScanState state)
    {
        var text = TryReadSmallText(file);
        foreach (var match in Regex.Matches(text, @"\{\s*([A-Za-z0-9_]+)\s*,\s*[""'{]", RegexOptions.IgnoreCase).Cast<Match>().Take(80))
        {
            AddUse(state, "rebar: " + match.Groups[1].Value, file.Name);
        }
    }

    private static void DetectLuaRocksManifest(FileInfo file, ScanState state)
    {
        var text = TryReadSmallText(file);
        foreach (var match in Regex.Matches(text, @"[""']([A-Za-z0-9_.-]+)\s*[<>=~]", RegexOptions.IgnoreCase).Cast<Match>().Take(80))
        {
            AddUse(state, "LuaRocks: " + match.Groups[1].Value, file.Name);
        }
    }

    private static void DetectPerlManifest(FileInfo file, ScanState state)
    {
        var text = TryReadSmallText(file);
        foreach (var match in Regex.Matches(text, @"\b(?:requires|recommends|suggests|test_requires)\s+[""']([^""']+)[""']", RegexOptions.IgnoreCase).Cast<Match>().Take(80))
        {
            AddUse(state, "CPAN: " + match.Groups[1].Value, file.Name);
        }
    }

    private static void DetectAppleManifest(FileInfo file, string lowerName, ScanState state)
    {
        var text = TryReadSmallText(file);
        if (lowerName.StartsWith("podfile", StringComparison.Ordinal))
        {
            foreach (var match in Regex.Matches(text, @"\bpod\s+[""']([^""']+)[""']", RegexOptions.IgnoreCase).Cast<Match>().Take(80))
            {
                AddUse(state, "CocoaPods: " + match.Groups[1].Value, file.Name);
            }

            return;
        }

        if (lowerName.StartsWith("cartfile", StringComparison.Ordinal))
        {
            foreach (var match in Regex.Matches(text, @"\b(?:github|git)\s+[""']([^""']+)[""']", RegexOptions.IgnoreCase).Cast<Match>().Take(80))
            {
                AddUse(state, "Carthage: " + match.Groups[1].Value, file.Name);
            }

            return;
        }

        foreach (var match in Regex.Matches(text, @"\.package\s*\([^)]+(?:url|name)\s*:\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase).Cast<Match>().Take(80))
        {
            AddUse(state, "SwiftPM: " + ShortPackageName(match.Groups[1].Value), file.Name);
        }
    }

    private static void DetectRManifest(FileInfo file, string lowerName, ScanState state)
    {
        if (lowerName == "renv.lock")
        {
            using var document = TryReadJsonDocument(file);
            if (document?.RootElement.TryGetProperty("Packages", out var packages) == true
                && packages.ValueKind == JsonValueKind.Object)
            {
                foreach (var package in packages.EnumerateObject().Take(80))
                {
                    AddUse(state, "R package: " + package.Name, "renv.lock");
                }
            }

            return;
        }

        var text = TryReadSmallText(file);
        foreach (var package in ExtractDescriptionPackages(text).Take(80))
        {
            AddUse(state, "R package: " + package, file.Name);
        }
    }

    private static bool LooksLikeRDescription(FileInfo file)
    {
        var text = TryReadSmallText(file);
        return Regex.IsMatch(text, @"^Package\s*:", RegexOptions.IgnoreCase | RegexOptions.Multiline)
            && Regex.IsMatch(text, @"^Version\s*:", RegexOptions.IgnoreCase | RegexOptions.Multiline);
    }

    private static bool LooksLikeJuliaManifest(FileInfo file)
    {
        var text = TryReadSmallText(file);
        return text.Contains("[deps]", StringComparison.OrdinalIgnoreCase)
            || text.Contains("[compat]", StringComparison.OrdinalIgnoreCase)
            || text.Contains("julia_version", StringComparison.OrdinalIgnoreCase)
            || Regex.IsMatch(text, @"^\s*uuid\s*=", RegexOptions.IgnoreCase | RegexOptions.Multiline);
    }

    private static void DetectJuliaManifest(FileInfo file, ScanState state)
    {
        var text = TryReadSmallText(file);
        var inDeps = false;
        foreach (var line in SplitTextLines(text))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("[[deps.", StringComparison.OrdinalIgnoreCase))
            {
                var package = trimmed.Trim('[', ']').Replace("deps.", "", StringComparison.OrdinalIgnoreCase);
                AddUse(state, "Julia: " + package, file.Name);
                continue;
            }

            if (trimmed.StartsWith("[", StringComparison.Ordinal) && trimmed.EndsWith("]", StringComparison.Ordinal))
            {
                inDeps = trimmed.Equals("[deps]", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (inDeps)
            {
                var match = Regex.Match(trimmed, @"^([A-Za-z0-9_.-]+)\s*=");
                if (match.Success)
                {
                    AddUse(state, "Julia: " + match.Groups[1].Value, file.Name);
                }
            }
        }
    }

    private static void DetectHaskellManifest(FileInfo file, ScanState state)
    {
        var text = TryReadSmallText(file);
        foreach (Match block in Regex.Matches(text, @"build-depends\s*:\s*(?<body>[^\r\n]+(?:\r?\n\s+[^:\r\n]+)*)", RegexOptions.IgnoreCase))
        {
            foreach (var package in SplitDependencyList(block.Groups["body"].Value).Take(80))
            {
                AddUse(state, "Hackage: " + package, file.Name);
            }
        }

        foreach (var package in ExtractYamlListValues(text, "extra-deps", "dependencies").Take(80))
        {
            AddUse(state, "Hackage: " + package, file.Name);
        }
    }

    private static void DetectNimbleManifest(FileInfo file, ScanState state)
    {
        var text = TryReadSmallText(file);
        foreach (var match in Regex.Matches(text, @"\brequires\s+[""']([^""']+)[""']", RegexOptions.IgnoreCase).Cast<Match>().Take(80))
        {
            foreach (var package in SplitDependencyList(match.Groups[1].Value))
            {
                AddUse(state, "Nimble: " + package, file.Name);
            }
        }
    }

    private static void DetectZigManifest(FileInfo file, ScanState state)
    {
        var text = TryReadSmallText(file);
        foreach (var match in Regex.Matches(text, @"\.name\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase).Cast<Match>().Take(80))
        {
            AddUse(state, "Zig package: " + match.Groups[1].Value, file.Name);
        }
    }

    private static void DetectOcamlManifest(FileInfo file, ScanState state)
    {
        var text = TryReadSmallText(file);
        foreach (var match in Regex.Matches(text, @"[""']([A-Za-z0-9_.+-]+)[""']", RegexOptions.IgnoreCase).Cast<Match>().Take(80))
        {
            AddUse(state, "OPAM: " + match.Groups[1].Value, file.Name);
        }

        foreach (Match match in Regex.Matches(text, @"\(depends\s+([^)]{1,1000})\)", RegexOptions.IgnoreCase))
        {
            foreach (var package in SplitDependencyList(match.Groups[1].Value).Take(80))
            {
                AddUse(state, "Dune: " + package, file.Name);
            }
        }
    }

    private static void DetectElmManifest(FileInfo file, ScanState state)
    {
        using var document = TryReadJsonDocument(file);
        if (document is null)
        {
            return;
        }

        foreach (var section in new[] { "dependencies", "test-dependencies" })
        {
            if (!document.RootElement.TryGetProperty(section, out var dependencies))
            {
                continue;
            }

            if (dependencies.ValueKind == JsonValueKind.Object)
            {
                foreach (var dependency in dependencies.EnumerateObject().Take(80))
                {
                    AddUse(state, "Elm: " + dependency.Name, "elm.json");
                }
            }
        }
    }

    private static void DetectPubspecPackages(FileInfo file, ScanState state)
    {
        var text = TryReadSmallText(file);
        var inDependencySection = false;
        foreach (var line in SplitTextLines(text))
        {
            if (Regex.IsMatch(line, @"^(dependencies|dev_dependencies):\s*$", RegexOptions.IgnoreCase))
            {
                inDependencySection = true;
                continue;
            }

            if (inDependencySection && Regex.IsMatch(line, @"^\S"))
            {
                inDependencySection = false;
            }

            if (!inDependencySection)
            {
                continue;
            }

            var match = Regex.Match(line, @"^\s{2}([A-Za-z0-9_][A-Za-z0-9_.-]*):");
            if (match.Success)
            {
                AddUse(state, "pub: " + match.Groups[1].Value, "pubspec");
            }
        }
    }

    private static void DetectPubspec(FileInfo file, ScanState state)
    {
        var text = TryReadSmallText(file);
        if (text.Contains("flutter:", StringComparison.OrdinalIgnoreCase)
            || text.Contains("sdk: flutter", StringComparison.OrdinalIgnoreCase))
        {
            AddStack(state, "Flutter", "pubspec flutter");
        }
    }

}
