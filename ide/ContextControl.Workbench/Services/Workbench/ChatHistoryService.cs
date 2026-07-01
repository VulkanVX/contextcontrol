// CC-DESC: Persists lightweight local LLM chat sessions without storing attachment contents.

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Security.Cryptography;

namespace ContextControl.Workbench.Services;

public sealed class ChatHistoryService
{
    private const string DefaultConversationKind = "chat";
    private const int MaxSessions = 40;
    private const int MaxMessagesPerSession = 120;
    private const int MaxSavedMessageCharacters = 220_000;
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);
    private readonly string _historyDirectory;
    private readonly string _historyPath;
    private readonly string _mirrorDirectory;

    public ChatHistoryService(string contextControlRoot)
    {
        var root = string.IsNullOrWhiteSpace(contextControlRoot)
            ? AppContext.BaseDirectory
            : contextControlRoot;
        _historyDirectory = NormalizeDirectory(root);
        _historyPath = Path.Combine(_historyDirectory, ".ccWorkbench.chat-history.json");
        _mirrorDirectory = ResolveMirrorDirectory(_historyDirectory);
    }

    public string HistoryPath => _historyPath;

    public string GetHistoryPath(string? scopeKey)
    {
        var cleanScope = NormalizeScope(scopeKey);
        if (cleanScope.Equals("default", StringComparison.OrdinalIgnoreCase))
        {
            return _historyPath;
        }

        return Path.Combine(_historyDirectory, $".ccWorkbench.chat-history.{HashScope(cleanScope)}.json");
    }

    public ChatHistoryDocument Load()
    {
        return Load(null);
    }

    public ChatHistoryDocument Load(string? scopeKey)
    {
        return Load(scopeKey, DefaultConversationKind, includeFallbacks: true);
    }

    public ChatHistoryDocument Load(string? scopeKey, string? conversationKind, bool includeFallbacks)
    {
        ChatHistoryDocument? firstExisting = null;
        var cleanKind = NormalizeConversationKind(conversationKind);
        var loadPaths = GetLoadPaths(scopeKey, includeFallbacks).ToArray();
        foreach (var path in loadPaths)
        {
            if (!File.Exists(path)
                || !TryLoadDocument(path, out var document)
                || !MatchesConversationKind(document, cleanKind))
            {
                continue;
            }

            firstExisting ??= document;
            if (HasMeaningfulHistory(document))
            {
                return document;
            }
        }

        var bestSibling = includeFallbacks
            ? LoadBestSiblingHistory(loadPaths, cleanKind)
            : null;
        if (bestSibling is not null && HasMeaningfulHistory(bestSibling))
        {
            return bestSibling;
        }

        return firstExisting ?? new ChatHistoryDocument { ConversationKind = cleanKind };
    }

    private static bool TryLoadDocument(string historyPath, out ChatHistoryDocument document)
    {
        try
        {
            document = JsonSerializer.Deserialize<ChatHistoryDocument>(File.ReadAllText(historyPath), JsonOptions)
                ?? new ChatHistoryDocument();
            return true;
        }
        catch
        {
            document = new ChatHistoryDocument();
            return false;
        }
    }

    public void Save(ChatHistoryDocument document)
    {
        Save(document, null);
    }

    public void Save(ChatHistoryDocument document, string? scopeKey)
    {
        Save(document, scopeKey, DefaultConversationKind, mirrorDefaultScope: true);
    }

    public void Save(ChatHistoryDocument document, string? scopeKey, string? conversationKind, bool mirrorDefaultScope)
    {
        var cleanKind = NormalizeConversationKind(conversationKind);
        var cleanDocument = new ChatHistoryDocument
        {
            ConversationKind = cleanKind,
            SelectedSessionId = document.SelectedSessionId,
            PromptText = TrimLargeMessage(document.PromptText),
            PromptModeKey = TrimText(document.PromptModeKey, 24),
            IsAutopilotEnabled = document.IsAutopilotEnabled,
            IsPromptOpen = document.IsPromptOpen,
            LastUserRequest = TrimLargeMessage(document.LastUserRequest),
            SelectedRoute = TrimText(document.SelectedRoute, 80),
            SelectedLocalModelId = TrimText(document.SelectedLocalModelId, 120),
            SelectedImageModelId = TrimText(document.SelectedImageModelId, 120),
            TerminalOutputText = TrimLargeMessage(document.TerminalOutputText),
            Attachments = (document.Attachments ?? [])
                .Take(24)
                .Select(CleanAttachment)
                .ToList(),
            Sessions = (document.Sessions ?? [])
                .Where(session => !string.IsNullOrWhiteSpace(session.Id))
                .OrderByDescending(session => session.UpdatedUtc)
                .Take(MaxSessions)
                .Select(CleanSession)
                .ToList()
        };

        var writePaths = GetWritePaths(scopeKey, mirrorDefaultScope).ToArray();
        if (!HasMeaningfulHistory(cleanDocument) && ExistingMeaningfulHistoryExists(writePaths, cleanKind))
        {
            return;
        }

        foreach (var path in writePaths)
        {
            WriteDocument(path, cleanDocument);
        }
    }

    private static ChatHistorySessionData CleanSession(ChatHistorySessionData session)
    {
        return new ChatHistorySessionData
        {
            Id = session.Id,
            Title = TrimText(session.Title, 80),
            CreatedUtc = session.CreatedUtc == default ? DateTime.UtcNow : session.CreatedUtc,
            UpdatedUtc = session.UpdatedUtc == default ? DateTime.UtcNow : session.UpdatedUtc,
            DraftPromptText = TrimLargeMessage(session.DraftPromptText),
            WorkflowTaskText = TrimLargeMessage(session.WorkflowTaskText),
            PendingAttachments = (session.PendingAttachments ?? [])
                .Take(24)
                .Select(CleanAttachment)
                .ToList(),
            Messages = (session.Messages ?? [])
                .TakeLast(MaxMessagesPerSession)
                .Select(CleanMessage)
                .ToList()
        };
    }

    private static ChatHistoryMessageData CleanMessage(ChatHistoryMessageData message)
    {
        return new ChatHistoryMessageData
        {
            Role = TrimText(message.Role, 24),
            Text = TrimLargeMessage(message.Text),
            ModelId = TrimText(message.ModelId, 96),
            Phase = TrimText(message.Phase, 48),
            CapsuleSummary = TrimText(message.CapsuleSummary, 160),
            DiagnosticPrompt = TrimLargeMessage(message.DiagnosticPrompt),
            CreatedUtc = message.CreatedUtc == default ? DateTime.UtcNow : message.CreatedUtc,
            Stats = message.Stats,
            Attachments = (message.Attachments ?? [])
                .Take(24)
                .Select(attachment => new ChatHistoryAttachmentData
                {
                    Label = TrimText(attachment.Label, 120),
                    Path = TrimText(attachment.Path, 520),
                    Kind = TrimText(attachment.Kind, 32),
                    IncludeInPrompt = attachment.IncludeInPrompt
                })
                .ToList()
        };
    }

    private static ChatHistoryAttachmentData CleanAttachment(ChatHistoryAttachmentData attachment)
    {
        return new ChatHistoryAttachmentData
        {
            Label = TrimText(attachment.Label, 120),
            Path = TrimText(attachment.Path, 520),
            Kind = TrimText(attachment.Kind, 32),
            IncludeInPrompt = attachment.IncludeInPrompt
        };
    }

    private IEnumerable<string> GetLoadPaths(string? scopeKey, bool includeFallbacks)
    {
        var primary = GetHistoryPath(scopeKey);
        var mirror = GetMirrorHistoryPath(scopeKey);
        var paths = new List<string> { primary, mirror };
        if (includeFallbacks && !NormalizeScope(scopeKey).Equals("default", StringComparison.OrdinalIgnoreCase))
        {
            paths.Add(_historyPath);
            paths.Add(GetMirrorHistoryPath(null));
        }

        return DistinctPaths(paths);
    }

    private IEnumerable<string> GetWritePaths(string? scopeKey, bool mirrorDefaultScope)
    {
        var paths = new List<string>
        {
            GetHistoryPath(scopeKey),
            GetMirrorHistoryPath(scopeKey)
        };

        if (mirrorDefaultScope && !NormalizeScope(scopeKey).Equals("default", StringComparison.OrdinalIgnoreCase))
        {
            paths.Add(_historyPath);
            paths.Add(GetMirrorHistoryPath(null));
        }

        return DistinctPaths(paths);
    }

    private string GetMirrorHistoryPath(string? scopeKey)
    {
        var cleanScope = NormalizeScope(scopeKey);
        if (cleanScope.Equals("default", StringComparison.OrdinalIgnoreCase))
        {
            return Path.Combine(_mirrorDirectory, ".ccWorkbench.chat-history.json");
        }

        return Path.Combine(_mirrorDirectory, $".ccWorkbench.chat-history.{HashScope(cleanScope)}.json");
    }

    private ChatHistoryDocument? LoadBestSiblingHistory(IEnumerable<string> excludedPaths, string conversationKind)
    {
        var excluded = new HashSet<string>(excludedPaths, StringComparer.OrdinalIgnoreCase);
        ChatHistoryDocument? bestDocument = null;
        var bestScore = 0;

        foreach (var directory in DistinctPaths([_historyDirectory, _mirrorDirectory]))
        {
            if (!Directory.Exists(directory))
            {
                continue;
            }

            foreach (var path in Directory.EnumerateFiles(directory, ".ccWorkbench.chat-history*.json"))
            {
                if (excluded.Contains(path)
                    || !TryLoadDocument(path, out var document)
                    || !MatchesConversationKind(document, conversationKind))
                {
                    continue;
                }

                var score = HistoryScore(document);
                if (score > bestScore)
                {
                    bestDocument = document;
                    bestScore = score;
                }
            }
        }

        return bestDocument;
    }

    private bool ExistingMeaningfulHistoryExists(IEnumerable<string> writePaths, string conversationKind)
    {
        foreach (var path in writePaths)
        {
            if (File.Exists(path)
                && TryLoadDocument(path, out var document)
                && MatchesConversationKind(document, conversationKind)
                && HasMeaningfulHistory(document))
            {
                return true;
            }
        }

        return LoadBestSiblingHistory(writePaths, conversationKind) is { } bestSibling
            && HasMeaningfulHistory(bestSibling);
    }

    private static void WriteDocument(string historyPath, ChatHistoryDocument document)
    {
        var parent = Path.GetDirectoryName(historyPath);
        if (!string.IsNullOrWhiteSpace(parent))
        {
            Directory.CreateDirectory(parent);
        }

        File.WriteAllText(historyPath, JsonSerializer.Serialize(document, JsonOptions) + Environment.NewLine, Utf8NoBom);
    }

    private static bool HasMeaningfulHistory(ChatHistoryDocument document)
    {
        return HistoryScore(document) > 0;
    }

    private static int HistoryScore(ChatHistoryDocument document)
    {
        var score = 0;
        foreach (var session in document.Sessions ?? [])
        {
            score += (session.Messages?.Count ?? 0) * 1000;
            if (!string.IsNullOrWhiteSpace(session.DraftPromptText))
            {
                score += 10;
            }

            if (!string.IsNullOrWhiteSpace(session.WorkflowTaskText))
            {
                score += 10;
            }

            if ((session.PendingAttachments?.Count ?? 0) > 0)
            {
                score += 10;
            }

            if (!string.IsNullOrWhiteSpace(session.Title)
                && !session.Title.Equals("New chat", StringComparison.OrdinalIgnoreCase))
            {
                score += 1;
            }
        }

        return score;
    }

    private static IEnumerable<string> DistinctPaths(IEnumerable<string> paths)
    {
        return paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.GetFullPath(path))
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static string NormalizeDirectory(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch
        {
            return string.IsNullOrWhiteSpace(path) ? AppContext.BaseDirectory : path;
        }
    }

    private static string ResolveMirrorDirectory(string historyDirectory)
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            localAppData = Path.GetTempPath();
        }

        return Path.Combine(
            localAppData,
            "ContextControl",
            "Workbench",
            "ChatHistory",
            HashScope(NormalizeDirectory(historyDirectory)));
    }

    private static string TrimLargeMessage(string? value)
    {
        var clean = value ?? "";
        if (clean.Length <= MaxSavedMessageCharacters)
        {
            return clean;
        }

        const string marker = "\n\n[... chat message truncated for history size ...]\n\n";
        var headLength = MaxSavedMessageCharacters / 2;
        var tailLength = MaxSavedMessageCharacters - headLength - marker.Length;
        return clean[..headLength] + marker + clean[^tailLength..];
    }

    private static string TrimText(string? value, int maxLength)
    {
        var clean = value ?? "";
        return clean.Length <= maxLength ? clean : clean[..maxLength];
    }

    private static string NormalizeScope(string? scopeKey)
    {
        var clean = (scopeKey ?? "").Trim();
        return string.IsNullOrWhiteSpace(clean) ? "default" : clean;
    }

    private static string NormalizeConversationKind(string? conversationKind)
    {
        var clean = (conversationKind ?? "").Trim().ToLowerInvariant();
        return string.IsNullOrWhiteSpace(clean) ? DefaultConversationKind : clean;
    }

    private static bool MatchesConversationKind(ChatHistoryDocument document, string conversationKind)
    {
        return NormalizeConversationKind(document.ConversationKind).Equals(
            NormalizeConversationKind(conversationKind),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string HashScope(string scopeKey)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(scopeKey.ToUpperInvariant()));
        return Convert.ToHexString(bytes)[..16].ToLowerInvariant();
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

public sealed class ChatHistoryDocument
{
    public string ConversationKind { get; set; } = "chat";
    public string? SelectedSessionId { get; set; }
    public string PromptText { get; set; } = "";
    public string PromptModeKey { get; set; } = "";
    public bool? IsAutopilotEnabled { get; set; }
    public bool IsPromptOpen { get; set; }
    public string LastUserRequest { get; set; } = "";
    public string SelectedRoute { get; set; } = "";
    public string SelectedLocalModelId { get; set; } = "";
    public string SelectedImageModelId { get; set; } = "";
    public string TerminalOutputText { get; set; } = "";
    public List<ChatHistoryAttachmentData> Attachments { get; set; } = [];
    public List<ChatHistorySessionData> Sessions { get; set; } = [];
}

public sealed class ChatHistorySessionData
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
    public string DraftPromptText { get; set; } = "";
    public string WorkflowTaskText { get; set; } = "";
    public List<ChatHistoryAttachmentData> PendingAttachments { get; set; } = [];
    public List<ChatHistoryMessageData> Messages { get; set; } = [];
}

public sealed class ChatHistoryMessageData
{
    public string Role { get; set; } = "";
    public string Text { get; set; } = "";
    public string ModelId { get; set; } = "";
    public string Phase { get; set; } = "";
    public string CapsuleSummary { get; set; } = "";
    public string DiagnosticPrompt { get; set; } = "";
    public DateTime CreatedUtc { get; set; }
    public LocalLlmUsageStats? Stats { get; set; }
    public List<ChatHistoryAttachmentData> Attachments { get; set; } = [];
}

public sealed class ChatHistoryAttachmentData
{
    public string Label { get; set; } = "";
    public string Path { get; set; } = "";
    public string Kind { get; set; } = "";
    public bool IncludeInPrompt { get; set; }
}
