// CC-DESC: Watches project files and records external saves as chained Context Control versions.

using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ContextControl.Workbench.Services;

public sealed class ExternalChangeTracker : IDisposable
{
    private const int DebounceMilliseconds = 90;
    private const int PollIntervalMilliseconds = 5_000;
    private const int MaxExactDiffCells = 3_000_000;
    private static readonly StringComparer PathComparer = StringComparer.OrdinalIgnoreCase;
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);
    private static readonly EnumerationOptions SafeEnumerationOptions = new()
    {
        AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
        IgnoreInaccessible = true,
        RecurseSubdirectories = false
    };

    private readonly string _projectRoot;
    private readonly string _versionRoot;
    private readonly string _indexPath;
    private readonly string _watchLogPath;
    private readonly FileSystemWatcher _watcher;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _pending = new(PathComparer);
    private readonly ConcurrentDictionary<string, FileFingerprint> _fingerprints = new(PathComparer);
    private readonly CancellationTokenSource _pollCancel = new();
    private readonly SemaphoreSlim _scanGate = new(1, 1);
    private readonly object _saveGate = new();
    private ProjectFileRules _fileRules;
    private Task? _pollTask;
    private int _forceScanQueued;
    private bool _fingerprintsPrimed;
    private bool _disposed;

    public ExternalChangeTracker(string projectRoot)
    {
        _projectRoot = Path.GetFullPath(projectRoot);
        _versionRoot = Path.Combine(FindContextControlRoot(_projectRoot) ?? _projectRoot, ".ccReplace.versions");
        _indexPath = Path.Combine(_versionRoot, "index.json");
        _watchLogPath = Path.Combine(_versionRoot, "external-watch.log");
        _fileRules = ProjectFileRules.Load(_projectRoot);
        _fileRules.Save();

        Directory.CreateDirectory(_versionRoot);

        _watcher = new FileSystemWatcher(_projectRoot)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.CreationTime
                | NotifyFilters.FileName
                | NotifyFilters.LastWrite
                | NotifyFilters.Size,
            InternalBufferSize = 64 * 1024,
            EnableRaisingEvents = false
        };
        _watcher.Changed += OnChanged;
        _watcher.Created += OnChanged;
        _watcher.Renamed += OnRenamed;
        _watcher.Deleted += OnDeleted;
        _watcher.Error += OnWatcherError;
        _watcher.EnableRaisingEvents = true;

        AppendWatchLog($"started root={_projectRoot} rules={_fileRules.RulesPath}");
        _pollTask = Task.Run(() => PollLoopAsync(_pollCancel.Token));
    }

    public event EventHandler<ExternalFileChange>? ChangeCaptured;

    public ProjectFileRules FileRules => _fileRules;
    public string ProjectRoot => _projectRoot;
    public string WatchLogPath => _watchLogPath;

    public void ForceScanNow()
    {
        if (_disposed)
        {
            return;
        }

        if (Interlocked.Exchange(ref _forceScanQueued, 1) == 1)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await PrimeOrScanAsync("force scan", CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                Interlocked.Exchange(ref _forceScanQueued, 0);
            }
        });
    }

    public void ReloadRules()
    {
        _fileRules = ProjectFileRules.Load(_projectRoot);
        _fileRules.Save();
        _fingerprints.Clear();
        _fingerprintsPrimed = false;
        AppendWatchLog($"rules reloaded rules={_fileRules.RulesPath}");
        ForceScanNow();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _pollCancel.Cancel();

        _watcher.EnableRaisingEvents = false;
        _watcher.Changed -= OnChanged;
        _watcher.Created -= OnChanged;
        _watcher.Renamed -= OnRenamed;
        _watcher.Deleted -= OnDeleted;
        _watcher.Error -= OnWatcherError;
        _watcher.Dispose();

        foreach (var pending in _pending.Values)
        {
            pending.Cancel();
            pending.Dispose();
        }

        _pending.Clear();
        _pollCancel.Dispose();
        AppendWatchLog("stopped");
    }

    private void OnChanged(object sender, FileSystemEventArgs e)
    {
        Schedule(e.FullPath, "watcher");
    }

    private void OnRenamed(object sender, RenamedEventArgs e)
    {
        _fingerprints.TryRemove(Path.GetFullPath(e.OldFullPath), out _);
        Schedule(e.FullPath, "watcher rename");
    }

    private void OnDeleted(object sender, FileSystemEventArgs e)
    {
        _fingerprints.TryRemove(Path.GetFullPath(e.FullPath), out _);
    }

    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        AppendWatchLog($"watcher error: {e.GetException().Message}; polling fallback remains active");
    }

    private async Task PollLoopAsync(CancellationToken token)
    {
        try
        {
            await PrimeOrScanAsync("initial", token).ConfigureAwait(false);
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(PollIntervalMilliseconds, token).ConfigureAwait(false);
                await PrimeOrScanAsync("poll", token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            AppendWatchLog($"poll loop stopped: {ex.Message}");
        }
    }

    private async Task PrimeOrScanAsync(string source, CancellationToken token)
    {
        if (_disposed)
        {
            return;
        }

        if (!await _scanGate.WaitAsync(0, token).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            if (!_fingerprintsPrimed)
            {
                var count = PrimeFingerprints(token);
                if (!token.IsCancellationRequested && !_disposed)
                {
                    _fingerprintsPrimed = true;
                    AppendWatchLog($"primed source={source} files={count}");
                }

                return;
            }

            ScanForChangedFiles(token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            AppendWatchLog($"{source} failed: {ex.Message}");
        }
        finally
        {
            _scanGate.Release();
        }
    }

    private int PrimeFingerprints(CancellationToken token)
    {
        var count = 0;
        foreach (var path in EnumerateCandidateFiles())
        {
            if (token.IsCancellationRequested || _disposed)
            {
                return count;
            }

            if (!ShouldTrack(path) || !TryGetFingerprint(path, out var fingerprint))
            {
                continue;
            }

            _fingerprints[Path.GetFullPath(path)] = fingerprint;
            count++;
        }

        return count;
    }

    private void ScanForChangedFiles(CancellationToken token)
    {
        var seen = new HashSet<string>(PathComparer);
        foreach (var path in EnumerateCandidateFiles())
        {
            if (token.IsCancellationRequested || _disposed)
            {
                return;
            }

            var fullPath = Path.GetFullPath(path);
            seen.Add(fullPath);

            if (!ShouldTrack(fullPath) || !TryGetFingerprint(fullPath, out var current))
            {
                continue;
            }

            if (!_fingerprints.TryGetValue(fullPath, out var previous))
            {
                _fingerprints[fullPath] = current;
                Schedule(fullPath, "poll new");
                continue;
            }

            if (!current.Equals(previous))
            {
                _fingerprints[fullPath] = current;
                Schedule(fullPath, "poll changed");
            }
        }

        foreach (var knownPath in _fingerprints.Keys)
        {
            if (!seen.Contains(knownPath) && !File.Exists(knownPath))
            {
                _fingerprints.TryRemove(knownPath, out _);
            }
        }
    }

    private IEnumerable<string> EnumerateCandidateFiles()
    {
        var stack = new Stack<string>();
        stack.Push(_projectRoot);

        while (stack.Count > 0)
        {
            var directory = stack.Pop();

            DirectoryInfo[] childDirectories;
            FileInfo[] files;
            try
            {
                var info = new DirectoryInfo(directory);
                childDirectories = info.EnumerateDirectories("*", SafeEnumerationOptions).ToArray();
                files = info.EnumerateFiles("*", SafeEnumerationOptions).ToArray();
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var child in childDirectories)
            {
                var relativePath = NormalizePath(Path.GetRelativePath(_projectRoot, child.FullName));
                if (!_fileRules.ShouldSkipDirectory(child.Name, relativePath))
                {
                    stack.Push(child.FullName);
                }
            }

            foreach (var file in files)
            {
                var relativePath = NormalizePath(Path.GetRelativePath(_projectRoot, file.FullName));
                if (_fileRules.ShouldTrackFile(relativePath, file.Name, file.Extension))
                {
                    yield return file.FullName;
                }
            }
        }
    }

    private void Schedule(string path, string source)
    {
        if (_disposed || !ShouldTrack(path))
        {
            return;
        }

        var fullPath = Path.GetFullPath(path);
        var next = new CancellationTokenSource();
        if (_pending.TryRemove(fullPath, out var previous))
        {
            previous.Cancel();
            previous.Dispose();
        }

        _pending[fullPath] = next;
        _ = CaptureAfterQuietPeriodAsync(fullPath, source, next);
    }

    private async Task CaptureAfterQuietPeriodAsync(string fullPath, string source, CancellationTokenSource cancellation)
    {
        try
        {
            await Task.Delay(DebounceMilliseconds, cancellation.Token).ConfigureAwait(false);
            if (cancellation.IsCancellationRequested || _disposed)
            {
                return;
            }

            var change = SaveVersionIfChanged(fullPath);
            if (change is not null)
            {
                AppendWatchLog($"captured source={source} {change.RelativePath} v{change.VersionBefore}>v{change.VersionAfter}");
                ChangeCaptured?.Invoke(this, change);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            AppendWatchLog($"capture failed path={fullPath}: {ex.Message}");
        }
        finally
        {
            _pending.TryRemove(fullPath, out _);
            cancellation.Dispose();
        }
    }

    private bool ShouldTrack(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch
        {
            return false;
        }

        if (!File.Exists(fullPath))
        {
            return false;
        }

        return _fileRules.GetTrackDecision(fullPath).ShouldTrack;
    }

    private ExternalFileChange? SaveVersionIfChanged(string fullPath)
    {
        lock (_saveGate)
        {
            if (!ShouldTrack(fullPath) || LooksBinary(fullPath))
            {
                return null;
            }

            var text = ReadTextWithRetry(fullPath);
            if (text is null)
            {
                return null;
            }

            Directory.CreateDirectory(_versionRoot);
            var index = ReadIndex();
            var relativePath = NormalizePath(Path.GetRelativePath(_projectRoot, fullPath));
            var record = GetOrCreateRecord(index, relativePath, fullPath);
            var previousVersion = record.CurrentVersion;
            var previousSnapshotPath = ResolveSnapshotPath(record.Versions.LastOrDefault()?.Snapshot);
            if (!string.IsNullOrWhiteSpace(previousSnapshotPath)
                && File.Exists(previousSnapshotPath)
                && string.Equals(ReadTextWithRetry(previousSnapshotPath), text, StringComparison.Ordinal))
            {
                return null;
            }

            var nextVersion = Math.Max(previousVersion, record.Versions.Count == 0 ? 0 : record.Versions.Max(version => version.Version)) + 1;
            var fileDir = Path.Combine(_versionRoot, "files", record.Key);
            Directory.CreateDirectory(fileDir);

            var snapshotName = $"v{nextVersion:D6}.snapshot";
            var snapshotPath = Path.Combine(fileDir, snapshotName);
            File.WriteAllText(snapshotPath, text, Utf8NoBom);

            var timestamp = DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            record.Versions.Add(new VersionSnapshotJson
            {
                Version = nextVersion,
                Timestamp = timestamp,
                Snapshot = NormalizePath(Path.Combine("files", record.Key, snapshotName)),
                Reason = "external file save"
            });
            record.CurrentVersion = nextVersion;
            record.FullPath = fullPath;

            SaveIndex(index);

            if (TryGetFingerprint(fullPath, out var fingerprint))
            {
                _fingerprints[Path.GetFullPath(fullPath)] = fingerprint;
            }

            var currentLines = SplitLines(text);
            var previousLines = string.IsNullOrWhiteSpace(previousSnapshotPath) || !File.Exists(previousSnapshotPath)
                ? []
                : SplitLines(ReadTextWithRetry(previousSnapshotPath) ?? "");
            var stats = CalculateChangeStats(previousLines, currentLines);

            return new ExternalFileChange(
                _projectRoot,
                relativePath,
                Path.GetFileName(fullPath),
                snapshotPath,
                previousSnapshotPath ?? "",
                previousVersion,
                nextVersion,
                timestamp,
                DateTimeOffset.Parse(timestamp, CultureInfo.InvariantCulture).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                stats.Added,
                stats.Removed,
                currentLines.LongLength);
        }
    }

    private VersionIndexJson ReadIndex()
    {
        if (!File.Exists(_indexPath))
        {
            return new VersionIndexJson();
        }

        try
        {
            return JsonSerializer.Deserialize<VersionIndexJson>(File.ReadAllText(_indexPath)) ?? new VersionIndexJson();
        }
        catch
        {
            return new VersionIndexJson();
        }
    }

    private void SaveIndex(VersionIndexJson index)
    {
        var json = JsonSerializer.Serialize(index, JsonOptions);
        var tempPath = Path.Combine(_versionRoot, $".index.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp");
        File.WriteAllText(tempPath, json + Environment.NewLine, Utf8NoBom);

        if (File.Exists(_indexPath))
        {
            File.Replace(tempPath, _indexPath, null, true);
        }
        else
        {
            File.Move(tempPath, _indexPath);
        }
    }

    private VersionFileJson GetOrCreateRecord(VersionIndexJson index, string relativePath, string fullPath)
    {
        var record = index.Files.FirstOrDefault(file =>
            string.Equals(NormalizePath(file.Path), relativePath, StringComparison.OrdinalIgnoreCase)
            || FullPathEquals(file.FullPath, fullPath));
        if (record is not null)
        {
            return record;
        }

        record = new VersionFileJson
        {
            Path = relativePath,
            FullPath = fullPath,
            Key = ShortSha1(relativePath),
            CurrentVersion = 0
        };
        index.Files.Add(record);
        return record;
    }

    private static string? ReadTextWithRetry(string path)
    {
        for (var attempt = 0; attempt < 8; attempt++)
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);
                return reader.ReadToEnd();
            }
            catch (IOException)
            {
                Thread.Sleep(80 + (attempt * 35));
            }
            catch (UnauthorizedAccessException)
            {
                Thread.Sleep(80 + (attempt * 35));
            }
        }

        return null;
    }

    private static bool LooksBinary(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            Span<byte> buffer = stackalloc byte[512];
            var read = stream.Read(buffer);
            return buffer[..read].Contains((byte)0);
        }
        catch
        {
            return true;
        }
    }

    private static bool TryGetFingerprint(string path, out FileFingerprint fingerprint)
    {
        fingerprint = default;
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                return false;
            }

            fingerprint = new FileFingerprint(info.LastWriteTimeUtc.Ticks, info.Length);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void AppendWatchLog(string message)
    {
        try
        {
            Directory.CreateDirectory(_versionRoot);
            File.AppendAllText(
                _watchLogPath,
                $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} {message}{Environment.NewLine}",
                Utf8NoBom);
        }
        catch
        {
            // Diagnostics must never break file tracking.
        }
    }

    private static string[] SplitLines(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return [];
        }

        return text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
    }

    private static ChangeStats CalculateChangeStats(IReadOnlyList<string> previous, IReadOnlyList<string> current)
    {
        if (previous.Count == 0)
        {
            return new ChangeStats(current.Count, 0);
        }

        var common = (long)previous.Count * current.Count <= MaxExactDiffCells
            ? CountCommonLines(previous, current)
            : CountStableEdges(previous, current);
        return new ChangeStats(
            Math.Max(0, current.Count - common),
            Math.Max(0, previous.Count - common));
    }

    private static int CountCommonLines(IReadOnlyList<string> previous, IReadOnlyList<string> current)
    {
        var previousRow = new int[current.Count + 1];
        var currentRow = new int[current.Count + 1];

        for (var previousIndex = previous.Count - 1; previousIndex >= 0; previousIndex--)
        {
            for (var currentIndex = current.Count - 1; currentIndex >= 0; currentIndex--)
            {
                currentRow[currentIndex] = previous[previousIndex] == current[currentIndex]
                    ? previousRow[currentIndex + 1] + 1
                    : Math.Max(previousRow[currentIndex], currentRow[currentIndex + 1]);
            }

            (previousRow, currentRow) = (currentRow, previousRow);
        }

        return previousRow[0];
    }

    private static int CountStableEdges(IReadOnlyList<string> previous, IReadOnlyList<string> current)
    {
        var prefix = 0;
        while (prefix < previous.Count && prefix < current.Count && previous[prefix] == current[prefix])
        {
            prefix++;
        }

        var suffix = 0;
        while (suffix + prefix < previous.Count
            && suffix + prefix < current.Count
            && previous[previous.Count - suffix - 1] == current[current.Count - suffix - 1])
        {
            suffix++;
        }

        return prefix + suffix;
    }

    private string ResolveSnapshotPath(string? snapshot)
    {
        if (string.IsNullOrWhiteSpace(snapshot))
        {
            return "";
        }

        return Path.IsPathRooted(snapshot)
            ? snapshot
            : Path.GetFullPath(Path.Combine(_versionRoot, snapshot));
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

    private static string NormalizePath(string path)
    {
        var normalized = path.Replace('\\', '/');
        while (normalized.StartsWith("./", StringComparison.Ordinal))
        {
            normalized = normalized[2..];
        }

        return normalized.TrimStart('/');
    }

    private static bool FullPathEquals(string? left, string right)
    {
        if (string.IsNullOrWhiteSpace(left))
        {
            return false;
        }

        try
        {
            return string.Equals(Path.GetFullPath(left), right, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static string ShortSha1(string text)
    {
        var bytes = SHA1.HashData(Encoding.UTF8.GetBytes(text ?? ""));
        return Convert.ToHexString(bytes).ToLowerInvariant()[..16];
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    private readonly record struct FileFingerprint(long LastWriteTicks, long Length);

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

    private sealed record ChangeStats(int Added, int Removed);
}
