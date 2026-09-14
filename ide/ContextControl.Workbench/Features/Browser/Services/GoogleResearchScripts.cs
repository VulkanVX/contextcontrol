namespace ContextControl.Workbench.Services;

/// <summary>Read-only DOM extraction. Scripts never click, submit, or execute model output.</summary>
public static class GoogleResearchScripts
{
    public const string SearchResults = """
        (() => {
          const results = [], seen = new Set();
          const needsConsent = !!document.querySelector('form[action*="consent.google"], [role="dialog"] form[action*="consent"], [aria-modal="true"] form');
          if (needsConsent) return { url: location.href, needsConsent: true, results };
          for (const h of document.querySelectorAll('h3')) {
            const a = h.closest('a');
            if (!a || !h.innerText.trim()) continue;
            let url;
            try {
              url = new URL(a.href);
              if (url.hostname === 'www.google.com' && url.pathname === '/url') {
                const target = url.searchParams.get('q') || url.searchParams.get('url') || '';
                if (/^https?:\/\//.test(target)) url = new URL(target);
              }
            } catch { continue; }
            const googleRedirect = url.hostname === 'www.google.com' && ['/goto','/url'].includes(url.pathname);
            if (!['http:', 'https:'].includes(url.protocol) || (/^(.+\.)?google\.[a-z.]+$/.test(url.hostname) && !googleRedirect) || seen.has(url.href)) continue;
            seen.add(url.href);
            let block = h.closest('.MjjYud, .g, [data-sokoban-container]') || a.parentElement;
            for (let i = 0; i < 3 && block && block.innerText.length < 120; i++) block = block.parentElement;
            results.push({ title: h.innerText.trim().slice(0,160), url: url.href, snippet: (block?.innerText || '').trim().slice(0,700) });
            if (results.length === 6) break;
          }
          return { url: location.href, results };
        })()
        """;

    public const string PageText = """
        (() => {
          const root = document.querySelector('article, [role="main"], main') || document.body;
          if (!root) return { url: location.href, text: '' };
          const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT);
          const parts = []; let length = 0, node;
          while ((node = walker.nextNode()) && length < 24000) {
            const p = node.parentElement;
            if (!p || p.closest('script,style,noscript,nav,footer,header,form,button,svg,[aria-hidden="true"],[hidden]')) continue;
            if (!p.getClientRects().length) continue;
            const style = getComputedStyle(p);
            if (style.visibility === 'hidden' || style.display === 'none') continue;
            const text = node.textContent.replace(/\s+/g, ' ').trim();
            if (text) { parts.push(text); length += text.length + 1; }
          }
          return { url: location.href, title: document.title, text: parts.join('\n').slice(0,24000) };
        })()
        """;
}
