using System.Text.Json;
using ContextControl.Workbench.Controls;
using ContextControl.Workbench.Services;

internal static class BrowserConsentTests
{
    public static async Task Run(WebView2Host browser, CancellationToken token)
    {
        async Task SetBody(string html)
        {
            await browser.ExecuteScriptAsync("document.body.innerHTML = " + JsonSerializer.Serialize(html) + "; window.choice = ''; true;");
            await Task.Delay(80, token);
        }
        async Task<string> Inspect(string host)
        {
            var script = BrowserPageGate.InspectAndRejectOptionalCookies.Replace("const host = location.hostname.toLowerCase();", "const host = " + JsonSerializer.Serialize(host) + ";");
            using var result = JsonDocument.Parse(await browser.ExecuteScriptAsync(script));
            return result.RootElement.GetProperty("kind").GetString()!;
        }
        await SetBody("<div role='dialog'><h1>Prieš pereinant į „Google“</h1><p>Naudojame slapukus ir duomenis</p><button onclick=\"window.choice='reject'; this.parentElement.remove()\">Atmesti viską</button><button onclick=\"window.choice='accept'\">Priimti viską</button></div>");
        if (await Inspect("www.google.com") != "cookie-choice" || await browser.ExecuteScriptAsync("window.choice") != "\"reject\"")
            throw new Exception("Lithuanian Google consent must choose Reject all, never Accept all.");
        if (await Inspect("www.google.com") != "none") throw new Exception("Dismissed Google consent should allow research to continue.");
        await SetBody("<div role='dialog'><p>Allow cookies from Facebook</p><button onclick=\"window.choice='reject'; this.parentElement.remove()\">Decline optional cookies</button></div><form><input type='password'><button>Log in</button></form>");
        if (await Inspect("www.facebook.com") != "cookie-choice" || await Inspect("www.facebook.com") != "login")
            throw new Exception("Facebook must reject optional cookies and then identify the remaining login wall.");
        var json = await browser.ExecuteScriptAsync(GoogleResearchScripts.PageText);
        try { GooglePageReader.Parse(json); throw new Exception("Visible Facebook password form was treated as a read post."); }
        catch (GooglePageUnavailableException) { }
        await SetBody("<article><h1>Public post</h1><p>Readable public information.</p></article><footer>Cookies · Privacy</footer>");
        if (await Inspect("www.facebook.com") != "none") throw new Exception("A cookie footer on a public post must not block research.");
        await SetBody("<div role='dialog'><p>Before you continue to Google — cookies</p><button onclick=\"window.choice='accept'\">Accept all</button></div>");
        if (await Inspect("consent.google.com") != "consent" || await browser.ExecuteScriptAsync("window.choice") != "\"\"")
            throw new Exception("Missing rejection option must request attention without accepting cookies.");
        Console.WriteLine("Native cookie and login handling passed: Google Lithuanian rejection, Facebook rejection and login detection, public post, manual fallback.");
    }
}
