// CC-DESC: Represents one parsed visible part of a local LLM chat message.

namespace ContextControl.Workbench.ViewModels;

public sealed class LocalLlmChatPartViewModel(string kind, string text, ChatSnippetViewModel? snippet = null) : ObservableObject
{
    public string Kind { get; } = string.IsNullOrWhiteSpace(kind) ? "text" : kind.Trim();
    public string Text { get; } = text ?? "";
    public ChatSnippetViewModel? Snippet { get; } = snippet;
    public bool IsText => string.Equals(Kind, "text", StringComparison.OrdinalIgnoreCase);
    public bool IsSnippet => Snippet is not null;
}
