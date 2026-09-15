using System.Text.Json;
using System.Text.RegularExpressions;

namespace ContextControl.Workbench.Services;

public sealed class GooglePageUnavailableException(string? pageUrl, string reason) : InvalidOperationException(reason)
{
    public string? PageUrl { get; } = pageUrl;
}

public static partial class GooglePageReader
{
    public static void ThrowIfHttpError(string? url, int status)
    {
        if (status < 400) return;
        var reason = status switch
        {
            401 => "This source requires sign-in (HTTP 401).",
            403 => "This source blocked access (HTTP 403).",
            429 => "This source is limiting requests (HTTP 429).",
            451 => "This source is unavailable (HTTP 451).",
            _ => $"This source returned HTTP {status}."
        };
        throw new GooglePageUnavailableException(url, reason);
    }

    public static GooglePageContent Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var page = document.RootElement;
        static string Read(JsonElement item, string name) => item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
        var url = Read(page, "url");
        var title = Read(page, "title").Trim().TrimEnd('.', '!');
        var heading = Read(page, "heading");
        var text = Read(page, "text");
        var gate = Read(page, "gateText");
        var passwordVisible = page.TryGetProperty("hasPasswordField", out var password) && password.ValueKind == JsonValueKind.True;
        var consentVisible = page.TryGetProperty("needsConsent", out var consent) && consent.ValueKind == JsonValueKind.True;
        if (BrowserPageGate.IsAuthenticationUrl(url) || passwordVisible || consentVisible
            || Uri.TryCreate(url, UriKind.Absolute, out var gateUri) && gateUri.Host.Equals("consent.google.com", StringComparison.OrdinalIgnoreCase)
            || ConsentScreen().IsMatch(title) || ConsentScreen().IsMatch(heading))
            throw new GooglePageUnavailableException(url, "This source displayed a cookie-consent or login screen. Its content was not read.");
        var hasArticle = page.TryGetProperty("hasArticle", out var article) && article.ValueKind == JsonValueKind.True;
        var shortScreen = text.Length < 5000 && !hasArticle;
        if (BlockScreen().IsMatch(gate)
            || (shortScreen && (BlockScreen().IsMatch(heading) || BlockScreen().IsMatch(text[..Math.Min(text.Length, 500)])
                || title.Equals("Just a moment", StringComparison.OrdinalIgnoreCase)
                || title.Equals("Access denied", StringComparison.OrdinalIgnoreCase)
                || title.StartsWith("Attention Required!", StringComparison.OrdinalIgnoreCase))))
            throw new GooglePageUnavailableException(url, "This source displayed an access block, sign-in requirement, or verification screen. Its article text was not read.");
        var image = Read(page, "imageUrl");
        var images = new List<GooglePageImage>();
        if (page.TryGetProperty("images", out var candidates) && candidates.ValueKind == JsonValueKind.Array)
            foreach (var candidate in candidates.EnumerateArray().Take(24))
            {
                var imageUrl = Read(candidate, "url");
                if (!GooglePhotoPreviewService.IsImageLocation(imageUrl)) continue;
                static string Bounded(string value) => value.Length <= 180 ? value : value[..180];
                var kind = Read(candidate, "kind");
                images.Add(new(imageUrl, Bounded(Read(candidate, "caption")), Bounded(Read(candidate, "section")), kind is "Logo" or "Video preview" ? kind : "Article image"));
            }
        return new GooglePageContent(url, text, GooglePhotoPreviewService.IsImageLocation(image) ? image : null,
            images.DistinctBy(item => item.Url).ToArray(), title);
    }

    [GeneratedRegex(@"\b(blocked by network security|blocked due to a network policy|your request has been blocked|access denied|verify (?:that )?you are (?:a )?human|checking (?:your )?browser|log in to (?:your reddit account|continue)|sign in to continue)\b", RegexOptions.IgnoreCase)]
    private static partial Regex BlockScreen();

    [GeneratedRegex(@"^(?:before you (?:continue|go) to google|prieš pereinant į.*google|prieš tęsdami.*google|facebook\s*[-–—:]\s*(?:log in or sign up|prisijunkite)|log in to facebook|prisijunkite prie.*facebook)", RegexOptions.IgnoreCase)]
    private static partial Regex ConsentScreen();
}
