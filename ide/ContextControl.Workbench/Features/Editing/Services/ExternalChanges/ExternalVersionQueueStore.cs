// CC-DESC: Applies queued external update acceptance policies to the CC version index.

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ContextControl.Workbench.Services;

public static class ExternalVersionQueueStore
{
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

    public static void AcceptOnlyFinal(string projectRoot, IEnumerable<ExternalFileChange> queuedChanges)
    {
        var changes = queuedChanges
            .Where(change => !string.IsNullOrWhiteSpace(change.RelativePath))
            .ToArray();
        if (changes.Length == 0)
        {
            return;
        }

        var versionRoot = ResolveVersionRoot(projectRoot);
        var indexPath = Path.Combine(versionRoot, "index.json");
        if (!File.Exists(indexPath))
        {
            return;
        }

        VersionIndexJson index;
        try
        {
            index = JsonSerializer.Deserialize<VersionIndexJson>(File.ReadAllText(indexPath)) ?? new VersionIndexJson();
        }
        catch
        {
            return;
        }

        var changed = false;
        foreach (var group in changes.GroupBy(change => NormalizePath(change.RelativePath), StringComparer.OrdinalIgnoreCase))
        {
            var ordered = group.OrderBy(change => change.VersionAfter).ToArray();
            var finalVersion = ordered[^1].VersionAfter;
            var queuedVersions = ordered.Select(change => change.VersionAfter).ToHashSet();
            queuedVersions.Remove(finalVersion);

            if (queuedVersions.Count == 0)
            {
                continue;
            }

            var record = FindRecord(index, group.Key);
            if (record is null)
            {
                continue;
            }

            var removed = record.Versions
                .Where(version => queuedVersions.Contains(version.Version))
                .ToArray();
            if (removed.Length == 0)
            {
                continue;
            }

            foreach (var snapshot in removed)
            {
                record.Versions.Remove(snapshot);
                DeleteSnapshotBestEffort(versionRoot, snapshot.Snapshot);
            }

            var finalSnapshot = record.Versions.FirstOrDefault(version => version.Version == finalVersion);
            if (finalSnapshot is not null)
            {
                finalSnapshot.Reason = "accepted external final edit";
            }

            if (record.CurrentVersion < finalVersion)
            {
                record.CurrentVersion = finalVersion;
            }

            changed = true;
        }

        if (!changed)
        {
            return;
        }

        Directory.CreateDirectory(versionRoot);
        var json = JsonSerializer.Serialize(index, JsonOptions);
        var tempPath = Path.Combine(versionRoot, $".index.accept.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp");
        File.WriteAllText(tempPath, json + Environment.NewLine, Utf8NoBom);
        File.Replace(tempPath, indexPath, null, true);
    }

    private static VersionFileJson? FindRecord(VersionIndexJson index, string relativePath)
    {
        return index.Files.FirstOrDefault(file =>
            string.Equals(NormalizePath(file.Path), relativePath, StringComparison.OrdinalIgnoreCase)
            || (!string.IsNullOrWhiteSpace(file.FullPath)
                && string.Equals(NormalizePath(file.FullPath), relativePath, StringComparison.OrdinalIgnoreCase))
            || (!string.IsNullOrWhiteSpace(file.FullPath)
                && NormalizePath(file.FullPath).EndsWith("/" + relativePath, StringComparison.OrdinalIgnoreCase)));
    }

    private static string ResolveVersionRoot(string projectRoot)
    {
        var ccRoot = FindContextControlRoot(projectRoot) ?? projectRoot;
        return Path.Combine(ccRoot, ".ccReplace.versions");
    }

    private static string? FindContextControlRoot(string projectRoot)
    {
        if (LooksLikeContextControl(projectRoot))
        {
            return projectRoot;
        }

        var nested = Path.Combine(projectRoot, "contextcontrol");
        return LooksLikeContextControl(nested) ? nested : null;
    }

    private static bool LooksLikeContextControl(string path)
    {
        return Directory.Exists(path)
            && File.Exists(Path.Combine(path, "ccStart.ps1"))
            && File.Exists(Path.Combine(path, "ccDir.ps1"))
            && File.Exists(Path.Combine(path, "cc.ps1"))
            && File.Exists(Path.Combine(path, "ccReplace.ps1"));
    }

    private static void DeleteSnapshotBestEffort(string versionRoot, string snapshotPath)
    {
        if (string.IsNullOrWhiteSpace(snapshotPath))
        {
            return;
        }

        try
        {
            var fullPath = Path.IsPathRooted(snapshotPath)
                ? snapshotPath
                : Path.GetFullPath(Path.Combine(versionRoot, snapshotPath));
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }
        }
        catch
        {
            // Best effort. The index change is the source of truth for the IDE history.
        }
    }

    private static string NormalizePath(string path)
    {
        var normalized = (path ?? string.Empty).Replace('\\', '/');
        while (normalized.StartsWith("./", StringComparison.Ordinal))
        {
            normalized = normalized[2..];
        }

        return normalized.TrimStart('/');
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    private sealed class VersionIndexJson
    {
        public List<VersionFileJson> Files { get; set; } = [];
    }

    private sealed class VersionFileJson
    {
        public string Path { get; set; } = "";
        public string FullPath { get; set; } = "";
        public string Key { get; set; } = "";
        public int CurrentVersion { get; set; }
        public List<VersionSnapshotJson> Versions { get; set; } = [];
    }

    private sealed class VersionSnapshotJson
    {
        public int Version { get; set; }
        public string Timestamp { get; set; } = "";
        public string Snapshot { get; set; } = "";
        public string Reason { get; set; } = "";
    }
}
