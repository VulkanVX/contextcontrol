using System.Net;
using System.Text.RegularExpressions;
using ContextControl.Workbench.ViewModels;

namespace ContextControl.Workbench.Services;

/// <summary>A complete browser document derived only from the visible answer.</summary>
public sealed record GameArtifact(string Title, string Html)
{
    public const int MaxCharacters = 400_000;
    private sealed class CacheEntry { public string? Text; public GameArtifact? Artifact; }
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<LocalLlmChatMessageViewModel, CacheEntry> Cache = new();
    private static readonly Regex Fences = new(@"(?m)^ {0,3}(?<f>`{3,}|~{3,})(?<language>[^\r\n]*)\r?\n(?<code>[\s\S]*?)^ {0,3}\k<f>[ \t]*\r?$", RegexOptions.Compiled);
    public static bool RequestsNativeRuntime(string prompt) => Regex.IsMatch(prompt, @"(?<!\w)(python|pygame|unity|godot|unreal|c\+\+|c#|rust|java)(?!\w)", RegexOptions.IgnoreCase);
    public static bool IsCreationRequest(string prompt) => Regex.IsMatch(prompt, @"\b(create|make|build|write|generate|code)\b", RegexOptions.IgnoreCase)
        && Regex.IsMatch(prompt, @"\b(game|snake|pong|tetris|platformer|breakout)\b", RegexOptions.IgnoreCase)
        && !RequestsNativeRuntime(prompt);

    public static GameArtifact? FromMessage(LocalLlmChatMessageViewModel? message)
    {
        if (message is null || message.IsUser || message.IsAwaitingAnswer || message.RawText.Length > MaxCharacters
            || message.RawText.Contains("**Response incomplete.**", StringComparison.Ordinal)) return null;
        var cached = Cache.GetOrCreateValue(message);
        if (ReferenceEquals(cached.Text, message.RawText)) return cached.Artifact;
        cached.Text = message.RawText;
        cached.Artifact = Parse(message.RawText);
        return cached.Artifact;
    }
    public static GameArtifact? Parse(string raw)
    {
        if (raw.Length > MaxCharacters || raw.Contains("**Response incomplete.**", StringComparison.Ordinal)) return null;
        // Reasoning can contain tentative code. Never execute it as the final artifact.
        var visible = Regex.Replace(raw, @"<think>[\s\S]*?(?:</think>|$)", "", RegexOptions.IgnoreCase);
        var blocks = Fences.Matches(visible).Select(m => (Language: m.Groups["language"].Value.Trim().Split(' ')[0].ToLowerInvariant(), Code: m.Groups["code"].Value.Trim())).ToArray();
        var html = blocks.LastOrDefault(b => b.Language is "html" or "htm" || b.Code.StartsWith("<!doctype html", StringComparison.OrdinalIgnoreCase)).Code;
        if (html is null && visible.TrimStart().StartsWith("<!doctype html", StringComparison.OrdinalIgnoreCase)) html = visible.Trim();
        if (html is null || !Regex.IsMatch(html, @"</html>\s*$", RegexOptions.IgnoreCase)) return null;
        // Support the common three-block HTML/CSS/JS answer without writing or executing native files.
        var styles = blocks.Where(b => b.Language == "css").Select(b => b.Code).ToArray();
        var scripts = blocks.Where(b => b.Language is "js" or "javascript").Select(b => b.Code).ToArray();
        if (styles.Length == 1)
        {
            html = Regex.Replace(html, "<link\\b[^>]*href\\s*=\\s*[\"'][^\"':/]+\\.css[\"'][^>]*>", "", RegexOptions.IgnoreCase);
            html = Regex.Replace(html, "</head>", _ => "<style>" + styles[0] + "</style></head>", RegexOptions.IgnoreCase);
        }
        if (scripts.Length == 1)
        {
            html = Regex.Replace(html, "<script\\b[^>]*src\\s*=\\s*[\"'][^\"':/]+\\.js[\"'][^>]*>\\s*</script>", "", RegexOptions.IgnoreCase);
            html = Regex.Replace(html, "</body>", _ => "<script>" + scripts[0] + "</script></body>", RegexOptions.IgnoreCase);
        }
        var title = WebUtility.HtmlDecode(Regex.Match(html, @"<title[^>]*>([^<]+)</title>", RegexOptions.IgnoreCase).Groups[1].Value).Trim();
        return new(title.Length == 0 ? "Browser game" : title[..Math.Min(title.Length, 100)], html);
    }

    public static string Prompt(string request, GameArtifact? previous = null) => $$"""
        Create a playable browser game for ContextControl Game Lab. Return one complete ```html code block containing <!doctype html>, inline CSS, inline JavaScript, and the closing </html>. Include all game logic, visible controls, score, game-over state and restart. Use Canvas or ordinary DOM, keyboard controls and touch buttons. No packages, imports, external scripts/images/fonts, fetch, localStorage, or build tools: the preview runs offline in an isolated frame. Keep the implementation compact and complete. After the code, briefly describe controls. Do not replace code with placeholders.
        {{(previous is null ? "" : "Here is the current game. Apply the user's changes and return the entire updated HTML:\n```html\n" + previous.Html + "\n```\n")}}
        User request:
        {{request}}
        """;
}
