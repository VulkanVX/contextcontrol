namespace ContextControl.Workbench.Services;

public sealed record ChatAction(string Command, string Title, string Icon, string Description, string Example, bool RequiresPrompt = true)
{
    public string Label => Command + "  ·  " + Title;
}

/// <summary>One explicit, allowlisted catalog shared by the composer and the action library.</summary>
public static class ChatActionCatalog
{
    public static IReadOnlyList<ChatAction> All { get; } = [
        new("/search", "Google research", "browser", "Search Google, read public sources and answer with references and relevant photos. Uses your embedded browser; queries leave this computer.", "/search latest news about the next World of Warcraft update"),
        new("/game", "Create a game", "game", "Generate a complete offline HTML game, run startup and input checks, and try up to two repairs. Opens in Game Lab. Needs an installed local chat model.", "/game Snake with arrow keys, touch buttons and restart"),
        new("/chat", "Local chat", "chat", "Send a plain prompt to your selected local model. Turns game creation and automatic Google research off.", "/chat explain recursion with a small example"),
        new("/image", "Generate an image", "image", "Use the separate ImageGen runtime and selected image model. Install an image-generation model first; a text model alone cannot generate photos.", "/image a pixel-art forest at sunrise"),
        new("/context", "Project coding", "code", "Use the existing DIR → CC → GO project workflow with the current project, selected source and enabled Skillbook instructions. Review patch previews before applying.", "/context fix the selected project's camera movement"),
        new("/run", "Open latest game", "game", "Open the most recent complete game in this chat in Game Lab. Does not send another model request.", "/run", false),
        new("/actions", "Action library", "list", "Browse every action, its requirements and an example. Selecting an action prepares a command; Send invokes it.", "/actions", false)
    ];
    public static IReadOnlyList<ChatAction> Match(string prefix) => All.Where(a => a.Command.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToArray();
    public static (ChatAction? Action, string Argument) Parse(string text)
    {
        text = text.Trim();
        var split = text.IndexOfAny([' ', '\t', '\r', '\n']);
        var command = split < 0 ? text : text[..split];
        return (All.FirstOrDefault(a => a.Command.Equals(command, StringComparison.OrdinalIgnoreCase)), split < 0 ? "" : text[(split + 1)..].Trim());
    }
}
