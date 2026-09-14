using System.Text;

namespace ContextControl.Workbench.ViewModels;

public sealed class EditorDocumentViewModel
{
    private const int MaxPreviewBytes = 768 * 1024;
    private const long MaxExactDiffCells = 1_500_000;

    private EditorDocumentViewModel(
        string name,
        string path,
        string language,
        string loc,
        string version,
        string bytes,
        string status,
        string text,
        IReadOnlyDictionary<int, string>? lineChanges = null,
        string addedChangeLabel = "",
        string removedChangeLabel = "")
    {
        Name = name;
        Path = path;
        Language = language;
        Loc = loc;
        Version = version;
        Bytes = bytes;
        Status = status;
        Text = text;
        LineChanges = lineChanges ?? new Dictionary<int, string>();
        AddedChangeLabel = addedChangeLabel;
        RemovedChangeLabel = removedChangeLabel;
    }

    public string Name { get; }
    public string Path { get; }
    public string Language { get; }
    public string Loc { get; }
    public string Version { get; }
    public string Bytes { get; }
    public string Status { get; }
    public string Text { get; }
    public IReadOnlyDictionary<int, string> LineChanges { get; }
    public string AddedChangeLabel { get; }
    public string RemovedChangeLabel { get; }
    public bool HasChangeLabels => !string.IsNullOrWhiteSpace(AddedChangeLabel)
        || !string.IsNullOrWhiteSpace(RemovedChangeLabel);

    public static EditorDocumentViewModel Load(string absolutePath, string displayPath, string version = "", long loc = 0)
    {
        var name = System.IO.Path.GetFileName(absolutePath);
        var language = DetectLanguage(absolutePath);
        var locLabel = loc > 0 ? $"{loc:N0} loc" : "";

        if (!File.Exists(absolutePath))
        {
            return Message(name, displayPath, language, locLabel, version, "file unavailable", "The selected file is not available on disk.");
        }

        try
        {
            var file = new FileInfo(absolutePath);
            if (LooksBinary(absolutePath))
            {
                return Message(name, displayPath, language, locLabel, version, "binary file", "Binary content is not previewed.", $"{file.Length:N0} bytes");
            }

            var truncatedByBytes = file.Length > MaxPreviewBytes;
            var text = ReadPreviewText(absolutePath, out _);
            var bytesLabel = $"{file.Length:N0} bytes";
            var status = truncatedByBytes
                ? $"preview: first {MaxPreviewBytes / 1024} KB"
                : "ready";

            return new EditorDocumentViewModel(name, displayPath, language, locLabel, version, bytesLabel, status, text);
        }
        catch (Exception ex)
        {
            return Message(name, displayPath, language, locLabel, version, "read failed", ex.Message);
        }
    }

    public static EditorDocumentViewModel LoadVersion(VersionEntryViewModel version)
    {
        var displayPath = string.IsNullOrWhiteSpace(version.FilePath) ? version.SnapshotPath : version.FilePath;
        var name = string.IsNullOrWhiteSpace(version.FileName)
            ? System.IO.Path.GetFileName(displayPath)
            : version.FileName;
        var language = DetectLanguage(displayPath);
        var locLabel = version.Loc > 0 ? $"{version.Loc:N0} loc" : "";
        var status = "";

        if (string.IsNullOrWhiteSpace(version.SnapshotPath) || !File.Exists(version.SnapshotPath))
        {
            return LoadCurrentVersionFallback(version, name, displayPath, language, locLabel);
        }

        try
        {
            var current = ReadPreviewLines(version.SnapshotPath, out _);
            var previous = string.IsNullOrWhiteSpace(version.PreviousSnapshotPath) || !File.Exists(version.PreviousSnapshotPath)
                ? []
                : ReadPreviewLines(version.PreviousSnapshotPath, out _);
            var text = BuildDiffPreview(previous, current, out var changes);
            var bytesLabel = $"{new FileInfo(version.SnapshotPath).Length:N0} bytes";

            return new EditorDocumentViewModel(
                name,
                displayPath,
                language,
                locLabel,
                version.Version,
                bytesLabel,
                status,
                text,
                changes,
                version.AddedLabel,
                version.RemovedLabel);
        }
        catch (Exception ex)
        {
            return Message(name, displayPath, language, locLabel, version.Version, "read failed", ex.Message);
        }
    }

    public static EditorDocumentViewModel Empty()
    {
        return Message("No file selected", "", "text", "", "", "ready", "Select a file from the project tree.");
    }

    public static EditorDocumentViewModel Loading(string path)
    {
        var name = string.IsNullOrWhiteSpace(path) ? "Loading" : System.IO.Path.GetFileName(path);
        return Message(name, path, DetectLanguage(path), "", "", "loading", "Loading file preview...");
    }

    private static EditorDocumentViewModel LoadCurrentVersionFallback(
        VersionEntryViewModel version,
        string name,
        string displayPath,
        string language,
        string locLabel)
    {
        if (string.IsNullOrWhiteSpace(version.CurrentFilePath) || !File.Exists(version.CurrentFilePath))
        {
            return Message(name, displayPath, language, locLabel, version.Version, "snapshot unavailable", "This version snapshot is not available on disk.");
        }

        try
        {
            var file = new FileInfo(version.CurrentFilePath);
            if (LooksBinary(version.CurrentFilePath))
            {
                return Message(name, displayPath, language, locLabel, version.Version, "binary file", "Binary content is not previewed.", $"{file.Length:N0} bytes");
            }

            var text = ReadPreviewText(version.CurrentFilePath, out _);
            var status = "current file";

            return new EditorDocumentViewModel(name, displayPath, language, locLabel, version.Version, $"{file.Length:N0} bytes", status, text);
        }
        catch (Exception ex)
        {
            return Message(name, displayPath, language, locLabel, version.Version, "read failed", ex.Message);
        }
    }

    private static EditorDocumentViewModel Message(string name, string path, string language, string loc, string version, string status, string message, string bytes = "")
    {
        return new EditorDocumentViewModel(name, path, language, loc, version, bytes, status, message);
    }

    private static string ReadPreviewText(string path, out bool truncatedByLines)
    {
        return string.Join(Environment.NewLine, ReadPreviewLines(path, out truncatedByLines));
    }

    private static List<string> ReadPreviewLines(string path, out bool truncatedByLines)
    {
        var lines = new List<string>();
        using var stream = File.OpenRead(path);
        using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);
        truncatedByLines = false;

        while (!reader.EndOfStream && stream.Position < MaxPreviewBytes)
        {
            lines.Add(reader.ReadLine() ?? "");
        }

        return lines;
    }

    private static string BuildDiffPreview(
        IReadOnlyList<string> previous,
        IReadOnlyList<string> current,
        out IReadOnlyDictionary<int, string> lineChanges)
    {
        if (previous.Count == 0)
        {
            lineChanges = Enumerable.Range(0, current.Count).ToDictionary(index => index, _ => "add");
            return string.Join(Environment.NewLine, current);
        }

        if ((long)previous.Count * current.Count > MaxExactDiffCells)
        {
            return BuildStableEdgeDiffPreview(previous, current, out lineChanges);
        }

        var output = new List<string>(Math.Max(previous.Count, current.Count));
        var changes = new Dictionary<int, string>();
        var lcs = BuildLcsTable(previous, current);
        var previousIndex = 0;
        var currentIndex = 0;

        while (previousIndex < previous.Count && currentIndex < current.Count)
        {
            if (previous[previousIndex] == current[currentIndex])
            {
                output.Add(current[currentIndex]);
                previousIndex++;
                currentIndex++;
                continue;
            }

            if (lcs[previousIndex + 1, currentIndex] >= lcs[previousIndex, currentIndex + 1])
            {
                changes[output.Count] = "delete";
                output.Add(previous[previousIndex]);
                previousIndex++;
            }
            else
            {
                changes[output.Count] = "add";
                output.Add(current[currentIndex]);
                currentIndex++;
            }
        }

        while (previousIndex < previous.Count)
        {
            changes[output.Count] = "delete";
            output.Add(previous[previousIndex]);
            previousIndex++;
        }

        while (currentIndex < current.Count)
        {
            changes[output.Count] = "add";
            output.Add(current[currentIndex]);
            currentIndex++;
        }

        lineChanges = changes;
        return string.Join(Environment.NewLine, output);
    }

    private static string BuildStableEdgeDiffPreview(
        IReadOnlyList<string> previous,
        IReadOnlyList<string> current,
        out IReadOnlyDictionary<int, string> lineChanges)
    {
        var prefix = 0;
        while (prefix < previous.Count
            && prefix < current.Count
            && previous[prefix] == current[prefix])
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

        var output = new List<string>(current.Count + Math.Max(0, previous.Count - prefix - suffix));
        var changes = new Dictionary<int, string>();

        for (var index = 0; index < prefix; index++)
        {
            output.Add(current[index]);
        }

        for (var index = prefix; index < previous.Count - suffix; index++)
        {
            changes[output.Count] = "delete";
            output.Add(previous[index]);
        }

        for (var index = prefix; index < current.Count - suffix; index++)
        {
            changes[output.Count] = "add";
            output.Add(current[index]);
        }

        for (var index = current.Count - suffix; index < current.Count; index++)
        {
            output.Add(current[index]);
        }

        lineChanges = changes;
        return string.Join(Environment.NewLine, output);
    }

    private static int[,] BuildLcsTable(IReadOnlyList<string> previous, IReadOnlyList<string> current)
    {
        var table = new int[previous.Count + 1, current.Count + 1];
        for (var previousIndex = previous.Count - 1; previousIndex >= 0; previousIndex--)
        {
            for (var currentIndex = current.Count - 1; currentIndex >= 0; currentIndex--)
            {
                table[previousIndex, currentIndex] = previous[previousIndex] == current[currentIndex]
                    ? table[previousIndex + 1, currentIndex + 1] + 1
                    : Math.Max(table[previousIndex + 1, currentIndex], table[previousIndex, currentIndex + 1]);
            }
        }

        return table;
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
            return false;
        }
    }

    private static string DetectLanguage(string path)
    {
        var extension = System.IO.Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        return extension switch
        {
            "" => "text",
            "cs" => "C#",
            "fs" or "fsi" or "fsx" => "F#",
            "ps1" or "psm1" or "psd1" => "PowerShell",
            "ts" => "TypeScript",
            "tsx" => "React TSX",
            "js" or "mjs" or "cjs" => "JavaScript",
            "jsx" => "React JSX",
            "py" => "Python",
            "rs" => "Rust",
            "cpp" or "cc" or "cxx" => "C++",
            "h" or "hh" or "hpp" or "hxx" => "C/C++ header",
            _ => extension
        };
    }
}
