// CC-DESC: Recognizes consent and authentication screens without treating them as source evidence.
namespace ContextControl.Workbench.Services;

public static class BrowserPageGate
{
    public static bool IsAuthenticationUrl(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)) return false;
        if (uri.Host.Equals("accounts.google.com", StringComparison.OrdinalIgnoreCase)) return true;
        return (uri.Host.Equals("facebook.com", StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith(".facebook.com", StringComparison.OrdinalIgnoreCase))
            && (uri.AbsolutePath.StartsWith("/login", StringComparison.OrdinalIgnoreCase)
                || uri.AbsolutePath.StartsWith("/checkpoint", StringComparison.OrdinalIgnoreCase)
                || uri.AbsolutePath.StartsWith("/recover", StringComparison.OrdinalIgnoreCase));
    }

    // Click only the optional-cookie rejection choice on known providers. No sign-in,
    // paywall, challenge, or acceptance action is automated.
    public const string InspectAndRejectOptionalCookies = """
        (() => {
          const host = location.hostname.toLowerCase();
          const google = /(^|\.)google\.(com|lt|co\.uk|de|fr|ee|lv)$/.test(host);
          const facebook = /(^|\.)facebook\.com$/.test(host);
          const visible = e => !!e && !!e.getClientRects().length && getComputedStyle(e).visibility !== 'hidden';
          const normalize = t => (t || '').replace(/\s+/g, ' ').trim().toLowerCase();
          const bodyText = normalize(document.body?.innerText).slice(0,16000);
          const dialog = Array.from(document.querySelectorAll('[role="dialog"],dialog[open]')).find(visible);
          const dialogText = normalize(dialog?.innerText);
          const cookieWords = /cookies|slapuk|cookie|sīkdat|küpsis|куки|файлы cookie/;
          const googleConsent = google && (host.startsWith('consent.') || /before you (?:continue|go) to google|prieš pereinant į.*google|prieš tęsdami.*google/.test(bodyText)
              || (!!document.querySelector('form[action*="consent.google"]') && cookieWords.test(bodyText)));
          const facebookConsent = facebook && ((!!dialog && cookieWords.test(dialogText))
              || /allow the use of cookies|allow cookies from (?:facebook|meta)|leisti naudoti.*(?:facebook|meta).*slapuk/.test(bodyText));
          if (googleConsent || facebookConsent) {
            const rejectLabels = /^(reject all|decline optional cookies|only allow essential cookies|allow essential cookies only|reject optional cookies|atmesti viską|atmesti visus|atmesti nebūtinus slapukus|leisti tik būtinus slapukus|alle ablehnen|tout refuser|отклонить все|noraidīt visu|keeldu kõigist)$/;
            const container = dialog || document;
            const buttons = Array.from(container.querySelectorAll('button,[role="button"],input[type="submit"]')).filter(visible);
            const reject = buttons.find(e => rejectLabels.test(normalize(e.innerText || e.value || e.getAttribute('aria-label'))));
            if (reject && !reject.disabled && !reject.dataset.ccConsentSubmitted) {
              reject.dataset.ccConsentSubmitted = 'true'; reject.click();
              return { kind:'cookie-choice', url:location.href };
            }
            return { kind:'consent', url:location.href };
          }
          const password = Array.from(document.querySelectorAll('input[type="password"]')).some(visible);
          const facebookLogin = facebook && (/^\/(login|checkpoint|recover)(\/|\.|$)/.test(location.pathname)
              || password || /log in to (?:facebook|continue)|see more on facebook|prisijunkite prie.*facebook/.test(dialogText));
          if (facebookLogin || (password && !!dialog)) return { kind:'login', url:location.href };
          return { kind:'none', url:location.href };
        })()
        """;
}
