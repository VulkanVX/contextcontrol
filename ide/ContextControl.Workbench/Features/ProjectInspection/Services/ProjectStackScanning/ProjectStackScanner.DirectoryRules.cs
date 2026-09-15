// CC-DESC: Identifies package-manager and generated build roots from on-disk markers.
namespace ContextControl.Workbench.Services;

public static partial class ProjectStackScanner
{
    private static bool LooksLikeGeneratedBuildRoot(DirectoryInfo directory)
    {
        if (File.Exists(Path.Combine(directory.FullName, "CMakeCache.txt"))
            || Directory.Exists(Path.Combine(directory.FullName, "CMakeFiles"))
            || File.Exists(Path.Combine(directory.FullName, "project.assets.json"))) return true;
        // A .NET output folder is identified by paired runtime/dependency manifests,
        // not by the generic name "bin", which may contain authored scripts.
        return directory.EnumerateFiles("*.runtimeconfig.json", SafeEnumerationOptions)
            .Any(file => File.Exists(Path.Combine(directory.FullName, file.Name.Replace(".runtimeconfig.json", ".deps.json", StringComparison.OrdinalIgnoreCase))));
    }

    private static bool LooksLikePackageManagerRoot(DirectoryInfo directory) =>
        File.Exists(Path.Combine(directory.FullName, ".vcpkg-root"));
}
